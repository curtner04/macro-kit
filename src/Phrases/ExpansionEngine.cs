using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MacroKit.Core;

namespace MacroKit.Phrases
{
    /// <summary>
    /// Text expansion: watches the keys the user types and replaces a saved trigger with its replacement text.
    /// OnKey runs on the keyboard hook thread and only decides; a dedicated worker thread does the typing.
    /// </summary>
    public sealed class ExpansionEngine : IDisposable
    {
        const int BufferSize = 64;
        const int MaxQueuedKeys = 4096;
        const int PasteSettleMs = 250;
        const int ModifierWaitMs = 300;
        const int ModifierPollMs = 10;
        static readonly ushort[] HeldKeysToWaitFor = { 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };                    // Ctrl, Alt, Win (left and right)
        static readonly ushort[] HeldKeysToWaitForWhenPasting = { 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0xA0, 0xA1 }; // and Shift: Ctrl+Shift+V is not paste
        const ushort VkBack = 0x08, VkTab = 0x09, VkEnter = 0x0D, VkEscape = 0x1B, VkSpace = 0x20, VkV = 0x56, VkLeftCtrl = 0xA2;

        /// <summary>Phrase settings copied when SetPhrases runs, so later edits to a PhraseDef cannot race with typing.</summary>
        sealed class Entry
        {
            public PhraseDef Def;
            public string Trigger;
            public string Replacement;
            public bool MatchCase;
            public bool AfterDelimiter;
            public bool Paste;
        }

        /// <summary>One expansion for the worker: how many characters to erase and what to send afterwards.</summary>
        sealed class Job
        {
            public Entry Entry;
            public int Backspaces;
            public string Tail;   // the delimiter that ended the trigger, re-sent after the replacement (null for Immediately)
            public string Typed;      // the trigger exactly as typed (its case may differ from Entry.Trigger)
            public string Swallowed;  // the character(s) of the key-down that was swallowed to start the expansion
        }

        enum KeyKind { Ignore, Modifier, Clear, Backspace, Text, Enter, Tab }

        struct QueuedKey
        {
            public ushort Vk;
            public bool Down;
            public KeyKind Kind;
            public string Text;
        }

        /// <summary>Compares process names ignoring case and a trailing ".exe", without allocating.</summary>
        sealed class ProcessNameComparer : IEqualityComparer<string>
        {
            public static readonly ProcessNameComparer Instance = new ProcessNameComparer();

            static int StemLength(string s)
            {
                int n = s.Length;
                if (n > 4 && string.Compare(s, n - 4, ".exe", 0, 4, StringComparison.OrdinalIgnoreCase) == 0) n -= 4;
                return n;
            }

            public bool Equals(string a, string b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null) return false;
                int la = StemLength(a);
                return la == StemLength(b) && string.Compare(a, 0, b, 0, la, StringComparison.OrdinalIgnoreCase) == 0;
            }

            public int GetHashCode(string s)
            {
                int hash = 17;
                int n = StemLength(s);
                for (int i = 0; i < n; i++) hash = hash * 31 + char.ToUpperInvariant(s[i]);
                return hash;
            }
        }

        readonly IInputSink input;
        readonly IClipboard clipboard;
        readonly IClock clock;
        readonly object gate = new object();

        // Read on the hook thread without the lock, replaced whole by the setters.
        volatile Entry[] entries = new Entry[0];
        volatile HashSet<string> excluded = new HashSet<string>(ProcessNameComparer.Instance);
        volatile bool enabled = true;
        volatile bool expanding;
        volatile bool disposed;
        string ownProcess;

        // Guarded by gate.
        readonly char[] buffer = new char[BufferSize];
        int length;
        readonly Queue<QueuedKey> queued = new Queue<QueuedKey>();
        readonly List<ushort> swallowedUps = new List<ushort>();
        bool lCtrl, rCtrl, lAlt, rAlt, lWin, rWin;

        BlockingCollection<Job> jobs;
        Thread worker;

        public ExpansionEngine(IInputSink input, IClipboard clipboard, IClock clock)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (clipboard == null) throw new ArgumentNullException("clipboard");
            if (clock == null) throw new ArgumentNullException("clock");
            this.input = input;
            this.clipboard = clipboard;
            this.clock = clock;
            try { ownProcess = Process.GetCurrentProcess().ProcessName; }
            catch (Exception ex) { Log.Warn("Could not read this program's process name.", ex); }
        }

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value;
                Reset();
            }
        }

        public bool IsExpanding { get { return expanding; } }

        public event Action<PhraseDef> Expanded;

        /// <summary>
        /// An expansion could not be completed (the clipboard was busy, or Windows blocked the typing). The argument is a short,
        /// plain message for the user and never contains the phrase text. Where possible what the user had typed was put back.
        /// Raised on the worker thread.
        /// </summary>
        public event Action<string> ExpansionFailed;

        /// <summary>Test hook: runs at the start of every expansion, before any key or clipboard call (tests press a modifier here).</summary>
        internal Action BeforeExecute { get; set; }

        /// <summary>Test hook: the process name that is always excluded (normally Macro Kit's own).</summary>
        internal string OwnProcessName
        {
            get { return ownProcess; }
            set { ownProcess = value; }
        }

        /// <summary>Test hook: run each expansion on the calling thread, inside OnKey, instead of on the worker thread.</summary>
        internal bool RunInline { get; set; }

        /// <summary>Test hook: waits until no expansion is running or queued; false when the timeout passed first.</summary>
        internal bool WaitIdle(int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (expanding)
            {
                if (watch.ElapsedMilliseconds > timeoutMs) return false;
                Thread.Sleep(1);
            }
            return true;
        }

        public void SetPhrases(IEnumerable<PhraseDef> phrases)
        {
            var list = new List<Entry>();
            if (phrases != null)
            {
                foreach (PhraseDef p in phrases)
                {
                    if (p == null || !p.Enabled || string.IsNullOrEmpty(p.Trigger)) continue;
                    list.Add(new Entry
                    {
                        Def = p,
                        Trigger = p.Trigger,
                        Replacement = p.Replacement ?? "",
                        MatchCase = p.MatchCase,
                        AfterDelimiter = p.When == ExpandWhen.AfterDelimiter,
                        Paste = p.Method == InsertMethod.Paste
                    });
                }
            }
            // Longest trigger first, so the first match found is the longest one (the sort is stable).
            entries = list.OrderByDescending(e => e.Trigger.Length).ToArray();
        }

        public void SetExcluded(IEnumerable<string> processNames)
        {
            var set = new HashSet<string>(ProcessNameComparer.Instance);
            if (processNames != null)
            {
                foreach (string name in processNames)
                {
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name.Trim());
                }
            }
            excluded = set;
        }

        /// <summary>Forgets what was typed (mouse click, foreground change, Enabled change).</summary>
        public void Reset()
        {
            lock (gate) { length = 0; }
        }

        /// <summary>
        /// Called on the hook thread for every keyboard event. Returns true to swallow the event.
        /// Never throws and never waits for the worker.
        /// </summary>
        public bool OnKey(KeyEvent e, string typed, string foregroundProcess)
        {
            try
            {
                if (e.Injected || disposed) return false;
                Job job = null;
                bool swallow;
                lock (gate)
                {
                    swallow = HandleKey(e, typed, foregroundProcess, out job);
                }
                if (job != null) Dispatch(job);
                return swallow;
            }
            catch (Exception ex)
            {
                try { Log.Error("Text expansion could not handle a key.", ex); } catch { }
                return false;
            }
        }

        // Runs under the lock.
        bool HandleKey(KeyEvent e, string typed, string foregroundProcess, out Job job)
        {
            job = null;
            ushort vk = KeyNames.BaseVk(e.Vk);
            bool isModifier = TrackModifier(vk, e.Down);

            // The key-up (and any auto-repeat) of the key that started an expansion belongs to the swallowed key-down.
            if (swallowedUps.Count > 0 && swallowedUps.Contains(vk))
            {
                if (!e.Down) { swallowedUps.Remove(vk); return true; }
                if (expanding) return true;
                swallowedUps.Remove(vk);   // a fresh press after the expansion finished
            }

            if (expanding)
            {
                // Keep the user's typing in order: hold it back and replay it once the expansion is done.
                if (queued.Count < MaxQueuedKeys)
                {
                    string text = null;
                    KeyKind kind = e.Down ? Classify(vk, typed, isModifier, out text) : KeyKind.Ignore;
                    queued.Enqueue(new QueuedKey { Vk = e.Vk, Down = e.Down, Kind = kind, Text = text });
                }
                return true;
            }

            if (!enabled || IsExcluded(foregroundProcess))
            {
                length = 0;
                return false;
            }
            if (!e.Down) return false;

            // A key-up can go missing (Win+L, a UAC prompt, recording that started with the hotkey held), which would leave a
            // modifier flag stuck and every key a "shortcut". The system's own key state is the truth, so look at it on every real key.
            if (!isModifier) SyncModifiers();

            string chars;
            KeyKind keyKind = Classify(vk, typed, isModifier, out chars);
            switch (keyKind)
            {
                case KeyKind.Clear:
                    length = 0;
                    return false;
                case KeyKind.Backspace:
                    if (length > 0) length--;
                    return false;
                case KeyKind.Text:
                case KeyKind.Enter:
                case KeyKind.Tab:
                    break;
                default:
                    return false;
            }

            int before = length;
            if (keyKind == KeyKind.Text)
            {
                // A full buffer drops its oldest characters, which moves everything left; 'before' (where the new key starts) moves with it.
                before = Math.Max(0, before - Append(chars));
            }
            bool isDelimiter = keyKind != KeyKind.Text || (chars.Length == 1 && IsDelimiterChar(chars[0]));

            Entry hit = FindImmediate();
            int backspaces = 0;
            string tail = null;
            string typedTrigger = null;
            if (hit != null)
            {
                backspaces = Math.Max(0, hit.Trigger.Length - chars.Length);
                typedTrigger = new string(buffer, length - hit.Trigger.Length, hit.Trigger.Length);
            }
            else if (isDelimiter)
            {
                hit = FindAfterDelimiter(before);
                backspaces = hit == null ? 0 : hit.Trigger.Length;
                tail = chars;
                if (hit != null) typedTrigger = new string(buffer, before - hit.Trigger.Length, hit.Trigger.Length);
            }

            if (hit == null)
            {
                if (keyKind != KeyKind.Text) length = 0;   // a new line or the next field starts a fresh word
                return false;
            }

            length = 0;
            expanding = true;
            swallowedUps.Add(vk);
            job = new Job { Entry = hit, Backspaces = backspaces, Tail = hit.AfterDelimiter ? tail : null, Typed = typedTrigger, Swallowed = chars };
            return true;
        }

        // Runs under the lock. Copies the six modifier flags from the system's real key state.
        void SyncModifiers()
        {
            lCtrl = input.IsPhysicallyDown(0xA2);
            rCtrl = input.IsPhysicallyDown(0xA3);
            lAlt = input.IsPhysicallyDown(0xA4);
            rAlt = input.IsPhysicallyDown(0xA5);
            lWin = input.IsPhysicallyDown(0x5B);
            rWin = input.IsPhysicallyDown(0x5C);
        }

        // Runs under the lock. Updates the held-modifier flags and says whether the key is a modifier.
        bool TrackModifier(ushort vk, bool down)
        {
            switch (vk)
            {
                case 0x11: case 0xA2: lCtrl = down; return true;
                case 0xA3: rCtrl = down; return true;
                case 0x12: case 0xA4: lAlt = down; return true;
                case 0xA5: rAlt = down; return true;
                case 0x5B: lWin = down; return true;
                case 0x5C: rWin = down; return true;
                case 0x10: case 0xA0: case 0xA1: return true;
            }
            return false;
        }

        // Runs under the lock. Decides what a key-down does to the typed-text buffer.
        KeyKind Classify(ushort vk, string typed, bool isModifier, out string text)
        {
            text = null;
            if (isModifier) return KeyKind.Modifier;

            bool ctrl = lCtrl || rCtrl, alt = lAlt || rAlt, win = lWin || rWin;
            // Ctrl alone, Alt alone or Win is a shortcut; Ctrl+Alt together is AltGr, which types characters.
            bool shortcut = win || ctrl != alt;
            bool altGr = ctrl && alt;

            if (vk == VkBack) return shortcut || altGr ? KeyKind.Clear : KeyKind.Backspace;
            if (vk == VkEscape || (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E) return KeyKind.Clear;
            if (shortcut) return KeyKind.Clear;
            if (vk == VkEnter) { text = "\n"; return altGr ? KeyKind.Clear : KeyKind.Enter; }
            if (vk == VkTab) { text = "\t"; return altGr ? KeyKind.Clear : KeyKind.Tab; }

            text = PrintableOnly(typed);
            if (text == null && vk == VkSpace && !altGr) text = " ";
            if (text == null) return altGr ? KeyKind.Clear : KeyKind.Ignore;
            return KeyKind.Text;
        }

        /// <summary>The printable characters of typed, or null when there are none. Returns the same string when nothing needs removing.</summary>
        static string PrintableOnly(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return null;
            bool clean = true;
            foreach (char c in typed)
            {
                if (c < 0x20 || c == 0x7F) { clean = false; break; }
            }
            if (clean) return typed;
            var chars = new List<char>(typed.Length);
            foreach (char c in typed)
            {
                if (c >= 0x20 && c != 0x7F) chars.Add(c);
            }
            return chars.Count == 0 ? null : new string(chars.ToArray());
        }

        static bool IsDelimiterChar(char c)
        {
            return char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c);
        }

        bool IsExcluded(string process)
        {
            if (string.IsNullOrEmpty(process)) return false;
            string own = ownProcess;
            if (own != null && ProcessNameComparer.Instance.Equals(own, process)) return true;
            return excluded.Contains(process);
        }

        // Runs under the lock. Returns how many old characters fell off the front of a full buffer.
        int Append(string text)
        {
            int dropped = 0;
            foreach (char c in text)
            {
                if (length == BufferSize)
                {
                    Array.Copy(buffer, 1, buffer, 0, BufferSize - 1);
                    length--;
                    dropped++;
                }
                buffer[length++] = c;
            }
            return dropped;
        }

        // Runs under the lock. The longest "Immediately" trigger the buffer now ends with.
        Entry FindImmediate()
        {
            Entry[] list = entries;
            for (int i = 0; i < list.Length; i++)
            {
                Entry entry = list[i];
                if (!entry.AfterDelimiter && EndsWith(entry, length)) return entry;
            }
            return null;
        }

        // Runs under the lock. The longest "AfterDelimiter" trigger that ends at 'end' and starts a word.
        Entry FindAfterDelimiter(int end)
        {
            Entry[] list = entries;
            for (int i = 0; i < list.Length; i++)
            {
                Entry entry = list[i];
                if (!entry.AfterDelimiter || !EndsWith(entry, end)) continue;
                int start = end - entry.Trigger.Length;
                if (start == 0 || IsDelimiterChar(buffer[start - 1])) return entry;
            }
            return null;
        }

        bool EndsWith(Entry entry, int end)
        {
            string trigger = entry.Trigger;
            int start = end - trigger.Length;
            if (start < 0) return false;
            for (int i = 0; i < trigger.Length; i++)
            {
                char a = buffer[start + i], b = trigger[i];
                if (a == b) continue;
                if (entry.MatchCase || char.ToUpperInvariant(a) != char.ToUpperInvariant(b)) return false;
            }
            return true;
        }

        void Dispatch(Job job)
        {
            if (RunInline)
            {
                Execute(job);
                return;
            }
            lock (gate)
            {
                if (disposed)
                {
                    queued.Clear();
                    expanding = false;
                    return;
                }
                if (jobs == null)
                {
                    jobs = new BlockingCollection<Job>();
                    worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Macro Kit text expansion" };
                    worker.Start();
                }
            }
            try { jobs.Add(job); }
            catch (InvalidOperationException) { ReleaseAfterDispose(); }   // disposed while the key was being handled
        }

        void ReleaseAfterDispose()
        {
            lock (gate) { queued.Clear(); expanding = false; }
        }

        void WorkerLoop()
        {
            foreach (Job job in jobs.GetConsumingEnumerable()) Execute(job);
        }

        void Execute(Job job)
        {
            int erased = 0;          // backspaces sent so far
            bool sentAnything = false;
            string failure = null;   // set when the user should be told
            try
            {
                Action before = BeforeExecute;
                if (before != null) before();

                bool paste = job.Entry.Paste;
                object snapshot = null;
                uint ourSequence = 0;
                bool wroteClipboard = false;
                if (paste)
                {
                    // The clipboard comes first: if it is busy nothing has been erased yet and the trigger simply stays as typed.
                    if (!PrepareClipboard(job.Entry.Replacement, out snapshot, out ourSequence))
                    {
                        RetypeSwallowedKey(job);
                        failure = "Couldn't expand that phrase because another app was using the clipboard. What you typed was left as it was.";
                        Reset();
                        return;   // the finally block below reports it
                    }
                    wroteClipboard = true;
                }

                try
                {
                    ReleaseHeldModifiers(paste ? HeldKeysToWaitForWhenPasting : HeldKeysToWaitFor);
                    sentAnything = true;
                    for (int i = 0; i < job.Backspaces; i++)
                    {
                        input.KeyDown(VkBack);
                        erased++;
                        input.KeyUp(VkBack);
                    }
                    if (paste)
                    {
                        input.KeyCombo(new ushort[] { VkLeftCtrl, VkV });
                        clock.Sleep(PasteSettleMs, CancellationToken.None);
                    }
                    else if (job.Entry.Replacement.Length > 0)
                    {
                        input.TypeText(job.Entry.Replacement);
                    }
                }
                finally
                {
                    if (wroteClipboard) RestoreClipboard(snapshot, ourSequence);
                }

                if (!string.IsNullOrEmpty(job.Tail))
                {
                    try { input.TypeText(job.Tail); }
                    catch (Exception ex)
                    {
                        Log.Error("Could not type the character after an expansion for phrase " + job.Entry.Def.Id + ".", ex);
                        failure = "The phrase was expanded, but Windows would not type the character that came after it.";
                    }
                }

                if (failure == null)
                {
                    Action<PhraseDef> handler = Expanded;
                    if (handler != null)
                    {
                        try { handler(job.Entry.Def); }
                        catch (Exception ex) { Log.Error("A phrase expanded handler failed.", ex); }
                    }
                }
            }
            catch (Exception ex)
            {
                // The phrase id only, never the text.
                Log.Error("Text expansion failed for phrase " + job.Entry.Def.Id + ".", ex);
                Reset();
                if (sentAnything)
                {
                    bool back = RetypeTrigger(job, erased);
                    failure = back
                        ? "Couldn't expand that phrase because Windows blocked the typing, so what you typed was put back."
                        : "Couldn't expand that phrase because Windows blocked the typing. Some of what you typed may be missing.";
                }
                else
                {
                    RetypeSwallowedKey(job);
                    failure = "Couldn't expand that phrase.";
                }
            }
            finally
            {
                if (failure != null) RaiseFailed(failure);
                ReplayQueuedKeys();
            }
        }

        // Snapshots the clipboard and puts the replacement on it, kept out of Win+V history and cloud sync. False when that failed;
        // the clipboard is then back as it was.
        bool PrepareClipboard(string replacement, out object snapshot, out uint ourSequence)
        {
            snapshot = null;
            ourSequence = 0;
            uint beforeWrite = 0;
            try
            {
                snapshot = clipboard.Snapshot();
                beforeWrite = clipboard.SequenceNumber;
                clipboard.SetTextPrivate(replacement);
                ourSequence = clipboard.SequenceNumber;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not put the phrase on the clipboard.", ex);
                // A write that failed half way may have emptied the clipboard; put back what was there.
                if (snapshot != null)
                {
                    try { if (clipboard.SequenceNumber != beforeWrite) clipboard.Restore(snapshot); }
                    catch (Exception restoreError) { Log.Warn("Could not put the clipboard back.", restoreError); }
                }
                return false;
            }
        }

        // Puts the user's clipboard back, unless something else has copied to it since we wrote the phrase (that newer copy wins).
        void RestoreClipboard(object snapshot, uint ourSequence)
        {
            try
            {
                if (clipboard.SequenceNumber != ourSequence)
                {
                    Log.Info("Left the clipboard alone after a paste because another program changed it.");
                    return;
                }
                clipboard.Restore(snapshot);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not put the clipboard back after a paste.", ex);
            }
        }

        // The keys that started the expansion may still be held, and Ctrl/Alt/Win (or Shift for a paste) would turn the backspaces
        // and Ctrl+V into other shortcuts. Give the user a moment to let go; if they don't, release the keys for them (without pressing them again).
        void ReleaseHeldModifiers(ushort[] keys)
        {
            if (!AnyDown(keys)) return;
            long deadline = clock.NowMs + ModifierWaitMs;
            while (AnyDown(keys) && clock.NowMs < deadline) clock.Sleep(ModifierPollMs, CancellationToken.None);
            foreach (ushort vk in keys)
            {
                if (!input.IsPhysicallyDown(vk)) continue;
                try { input.KeyUp(vk); }
                catch (Exception ex) { Log.Warn("Could not release a held modifier key before an expansion.", ex); }
            }
        }

        bool AnyDown(ushort[] keys)
        {
            foreach (ushort vk in keys)
            {
                if (input.IsPhysicallyDown(vk)) return true;
            }
            return false;
        }

        // After a failure that erased some of the trigger: type back what is missing (the erased part and the swallowed key).
        bool RetypeTrigger(Job job, int erased)
        {
            try
            {
                string whole = (job.Typed ?? "") + (job.Entry.AfterDelimiter ? job.Tail ?? "" : "");
                int missingFrom = Math.Min(whole.Length, Math.Max(0, job.Backspaces - erased));
                string text = whole.Substring(missingFrom);
                if (text.Length > 0) input.TypeText(text);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not type back the trigger after a failed expansion.", ex);
                return false;
            }
        }

        // Nothing was erased, but the key that completed the trigger was swallowed: type it so the trigger is complete again.
        void RetypeSwallowedKey(Job job)
        {
            try
            {
                if (!string.IsNullOrEmpty(job.Swallowed)) input.TypeText(job.Swallowed);
            }
            catch (Exception ex)
            {
                Log.Error("Could not type back the last key after a failed expansion.", ex);
            }
        }

        void RaiseFailed(string message)
        {
            Action<string> handler = ExpansionFailed;
            if (handler == null) return;
            try { handler(message); }
            catch (Exception ex) { Log.Error("An expansion failed handler failed.", ex); }
        }

        /// <summary>Sends the keys the user typed during the expansion, in order, then lets OnKey through again.</summary>
        void ReplayQueuedKeys()
        {
            while (true)
            {
                QueuedKey key;
                lock (gate)
                {
                    if (queued.Count == 0)
                    {
                        expanding = false;
                        return;
                    }
                    key = queued.Dequeue();
                    ApplyToBuffer(key);
                }
                try
                {
                    if (key.Down) input.KeyDown(key.Vk); else input.KeyUp(key.Vk);
                }
                catch (Exception ex)
                {
                    Log.Error("Could not replay a key typed during an expansion.", ex);
                }
            }
        }

        // Runs under the lock. The replayed keys are injected, so the buffer would never see them otherwise.
        void ApplyToBuffer(QueuedKey key)
        {
            if (!key.Down) return;
            switch (key.Kind)
            {
                case KeyKind.Text: Append(key.Text); break;
                case KeyKind.Backspace: if (length > 0) length--; break;
                case KeyKind.Clear:
                case KeyKind.Enter:
                case KeyKind.Tab: length = 0; break;
            }
        }

        /// <summary>Stops the worker thread. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            BlockingCollection<Job> collection;
            Thread thread;
            lock (gate)
            {
                collection = jobs;
                thread = worker;
            }
            if (collection != null)
            {
                collection.CompleteAdding();
                if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
            }
        }
    }
}
