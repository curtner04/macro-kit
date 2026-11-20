using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MacroKit.Core
{
    internal sealed class PhrasesFile
    {
        public int Schema { get; set; }
        public List<PhraseDef> Phrases { get; set; }
        public PhrasesFile() { Schema = 1; Phrases = new List<PhraseDef>(); }
    }

    internal sealed class MacrosFile
    {
        public int Schema { get; set; }
        public List<MacroDef> Macros { get; set; }
        public MacrosFile() { Schema = 1; Macros = new List<MacroDef>(); }
    }

    internal sealed class KeybindAppsFile
    {
        public int Schema { get; set; }
        public List<KeybindAppDef> Apps { get; set; }
        public KeybindAppsFile() { Schema = 1; Apps = new List<KeybindAppDef>(); }
    }

    internal sealed class RecordingIndexFile
    {
        public int Schema { get; set; }
        public List<RecordingInfo> Recordings { get; set; }
        public RecordingIndexFile() { Schema = 1; Recordings = new List<RecordingInfo>(); }
    }

    /// <summary>
    /// Reads and writes everything Macro Kit saves, as JSON files in one data folder. Thread-safe (one lock).
    /// Every write goes to a .tmp file first and is swapped in with File.Replace, keeping the old file as .previous.
    /// A file that cannot be read is moved aside (name.unreadable-yyyyMMdd-HHmmss) and its .previous copy is restored, or defaults
    /// are used when there is none; the move is reported in <see cref="Warnings"/> so the UI can tell the user. User data is never
    /// silently overwritten.
    /// </summary>
    public sealed class Storage
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        readonly object gate = new object();
        readonly List<string> warnings = new List<string>();
        readonly string root;

        public Storage() : this(Paths.DataRoot) { }

        public Storage(string dataRoot)
        {
            root = dataRoot;
        }

        // A file another program has open for a moment (a sync tool, a virus scan) is tried again with a doubling wait:
        // 50, 100, 200, 400 and 800 ms for settings, phrases, macros and keybinds, which are read once at startup;
        // only the first two waits for recordings and the index, which are read often and in bulk.
        const int ReadRetries = 5;
        const int QuickReadRetries = 2;
        const int ReadRetryMs = 50;
        readonly HashSet<string> readOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // paths that exist but could not be read
        readonly HashSet<string> skippedWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // changes to read-only paths that were not saved
        readonly HashSet<string> sessionCopied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // files whose .session copy was made

        public string DataRoot { get { return root; } }

        string SettingsFile { get { return Path.Combine(root, "settings.json"); } }
        string PhrasesPath { get { return Path.Combine(root, "phrases.json"); } }
        string MacrosPath { get { return Path.Combine(root, "macros.json"); } }
        string KeybindAppsPath { get { return Path.Combine(root, "keybinds.json"); } }
        string IndexPath { get { return Path.Combine(root, "recordings.json"); } }
        string RecordingsDir { get { return Path.Combine(root, "recordings"); } }
        string RecordingPath(string id) { return Path.Combine(RecordingsDir, id + ".json"); }

        /// <summary>A snapshot of the unreadable-file messages collected this session.</summary>
        public IList<string> Warnings
        {
            get { lock (gate) return new List<string>(warnings); }
        }

        /// <summary>
        /// The names of files that exist but could not be read this session (for example settings.json held open by another program).
        /// Defaults are used for them in memory and saving them is skipped, so the real file is never overwritten.
        /// The shell should tell the user while this list is not empty. Recordings are not listed: one is tried again each time it is used.
        /// </summary>
        public IList<string> ReadOnlyFiles
        {
            get
            {
                lock (gate)
                {
                    var names = new List<string>();
                    foreach (string path in readOnly) if (!IsRecordingPath(path)) names.Add(Path.GetFileName(path));
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    return names;
                }
            }
        }

        /// <summary>
        /// The names of read-only files (see <see cref="ReadOnlyFiles"/>) that a change made this session could not be saved to.
        /// Startup's own writes (seeding the examples, the recordings index) do not count. Leaving should ask first while this is not empty.
        /// </summary>
        public IList<string> UnsavedBlockedFiles
        {
            get
            {
                lock (gate)
                {
                    var names = new List<string>();
                    foreach (string path in skippedWrites) if (!IsRecordingPath(path)) names.Add(Path.GetFileName(path));
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    return names;
                }
            }
        }

        /// <summary>True when the last <see cref="LoadSettings"/> read settings.json (or an earlier saved copy) rather than starting from defaults.</summary>
        public bool SettingsFromFile { get; private set; }

        // ---- settings, phrases, macros ------------------------------------------------------------

        public AppSettings LoadSettings()
        {
            lock (gate)
            {
                AppSettings settings = ReadFile<AppSettings>(SettingsFile, true);
                SettingsFromFile = settings != null;
                bool fresh = settings == null && !IsBlocked(SettingsFile);   // only a file that is truly missing (or was set aside) gets defaults written
                if (settings == null) settings = new AppSettings();
                if (settings.Playback == null) settings.Playback = new PlaybackSettings();
                if (settings.ExcludedPrograms == null) settings.ExcludedPrograms = new List<string>();
                if (fresh) WriteGuarded(SettingsFile, Json.Serialize(settings), false);   // first start: the file is required for transfer
                return settings;
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            lock (gate) WriteGuarded(SettingsFile, Json.Serialize(settings));
        }

        public List<PhraseDef> LoadPhrases()
        {
            lock (gate)
            {
                PhrasesFile file = ReadFile<PhrasesFile>(PhrasesPath, true);
                if (file == null)
                {
                    file = new PhrasesFile { Phrases = SeedPhrases() };
                    WriteGuarded(PhrasesPath, Json.Serialize(file), false);
                }
                var ids = new HashSet<string>();
                foreach (PhraseDef phrase in file.Phrases)
                {
                    if (string.IsNullOrEmpty(phrase.Id) || !ids.Add(phrase.Id)) { phrase.Id = Guid.NewGuid().ToString("N"); ids.Add(phrase.Id); }
                    if (phrase.Trigger == null) phrase.Trigger = "";
                    if (phrase.Replacement == null) phrase.Replacement = "";
                }
                return file.Phrases;
            }
        }

        public void SavePhrases(IEnumerable<PhraseDef> phrases)
        {
            if (phrases == null) throw new ArgumentNullException("phrases");
            lock (gate) WriteGuarded(PhrasesPath, Json.Serialize(new PhrasesFile { Phrases = new List<PhraseDef>(phrases) }));
        }

        public List<MacroDef> LoadMacros()
        {
            lock (gate)
            {
                MacrosFile file = ReadFile<MacrosFile>(MacrosPath, true);
                if (file == null)
                {
                    file = new MacrosFile { Macros = SeedMacros() };
                    WriteGuarded(MacrosPath, Json.Serialize(file), false);
                }
                var ids = new HashSet<string>();
                foreach (MacroDef macro in file.Macros)
                {
                    if (string.IsNullOrEmpty(macro.Id) || !ids.Add(macro.Id)) { macro.Id = Guid.NewGuid().ToString("N"); ids.Add(macro.Id); }
                    if (macro.Script == null) macro.Script = "";
                    if (macro.Name == null) macro.Name = "";
                    if (macro.Hotkey == null) macro.Hotkey = "";
                    if (macro.TargetWindow == null) macro.TargetWindow = new WindowMatch();
                    if (macro.AltTabPartner == null) macro.AltTabPartner = new WindowMatch();
                    if (macro.Schedule == null) macro.Schedule = new MacroSchedule();
                    if (macro.Schedule.Time == null) macro.Schedule.Time = "09:00";
                    if (macro.Schedule.At == null) macro.Schedule.At = "";
                }
                return file.Macros;
            }
        }

        public void SaveMacros(IEnumerable<MacroDef> macros)
        {
            if (macros == null) throw new ArgumentNullException("macros");
            lock (gate) WriteGuarded(MacrosPath, Json.Serialize(new MacrosFile { Macros = new List<MacroDef>(macros) }));
        }

        /// <summary>The apps added by hand on the Global Keybinds tab. A missing file is an empty list and is not created until something is added.</summary>
        public List<KeybindAppDef> LoadKeybindApps()
        {
            lock (gate)
            {
                KeybindAppsFile file = ReadFile<KeybindAppsFile>(KeybindAppsPath, true) ?? new KeybindAppsFile();
                if (file.Apps == null) file.Apps = new List<KeybindAppDef>();
                var ids = new HashSet<string>();
                file.Apps.RemoveAll(a => a == null);
                foreach (KeybindAppDef app in file.Apps)
                {
                    if (string.IsNullOrEmpty(app.Id) || !ids.Add(app.Id)) { app.Id = Guid.NewGuid().ToString("N"); ids.Add(app.Id); }
                    if (app.Name == null) app.Name = "";
                    if (app.Program == null) app.Program = "";
                    if (app.Shortcuts == null) app.Shortcuts = new List<KeybindDef>();
                    app.Shortcuts.RemoveAll(s => s == null);
                    foreach (KeybindDef shortcut in app.Shortcuts)
                    {
                        if (shortcut.Keys == null) shortcut.Keys = "";
                        if (shortcut.Action == null) shortcut.Action = "";
                    }
                }
                return file.Apps;
            }
        }

        public void SaveKeybindApps(IEnumerable<KeybindAppDef> apps)
        {
            if (apps == null) throw new ArgumentNullException("apps");
            lock (gate) WriteGuarded(KeybindAppsPath, Json.Serialize(new KeybindAppsFile { Apps = new List<KeybindAppDef>(apps) }));
        }

        /// <summary>First-start phrases: the examples.</summary>
        public static List<PhraseDef> SeedPhrases() { return Examples.Phrases(); }

        /// <summary>First-start macros: the examples (the first one is copy, switch, paste, switch back, next row).</summary>
        public static List<MacroDef> SeedMacros() { return Examples.Macros(); }

        // ---- recordings ---------------------------------------------------------------------------

        /// <summary>The saved recordings, newest first. The index is rebuilt from the recording files when it is missing or disagrees with them.</summary>
        public List<RecordingInfo> LoadRecordingIndex()
        {
            lock (gate) return new List<RecordingInfo>(LoadIndexLocked());
        }

        /// <summary>One saved recording, or null when it is missing or unreadable (an unreadable file is set aside).</summary>
        public Recording LoadRecording(string id)
        {
            if (!IsSafeId(id)) return null;
            lock (gate)
            {
                Recording recording = ReadRecordingFile(id);
                if (recording == null && !File.Exists(RecordingPath(id)))
                {
                    // Set aside or never there: make sure the index does not keep listing it.
                    List<RecordingInfo> index = LoadIndexLocked();
                    if (index.RemoveAll(i => i.Id == id) > 0) SaveIndexLocked(index);
                }
                return recording;
            }
        }

        /// <summary>Saves (or replaces) a recording and updates the index. Fills in a missing Id, Name, Created and DurationMs.</summary>
        public void SaveRecording(Recording recording)
        {
            if (recording == null) throw new ArgumentNullException("recording");
            lock (gate)
            {
                if (recording.Events == null) recording.Events = new List<RecEvent>();
                if (!IsSafeId(recording.Id)) recording.Id = Guid.NewGuid().ToString("N");
                // Not saved over while it is in use, and the index is left alone too, so the two never disagree.
                if (IsBlocked(RecordingPath(recording.Id))) throw new IOException(RecordingInUse);
                if (string.IsNullOrWhiteSpace(recording.Name)) recording.Name = Recording.DefaultName(DateTime.Now);
                if (string.IsNullOrWhiteSpace(recording.Created)) recording.Created = Recording.NowIso();
                if (recording.DurationMs <= 0 && recording.Events.Count > 0) recording.DurationMs = Math.Max(0, recording.Events[recording.Events.Count - 1].T);
                WriteGuarded(RecordingPath(recording.Id), Json.Serialize(recording));
                List<RecordingInfo> index = LoadIndexLocked();
                RecordingInfo info = InfoOf(recording);
                int at = index.FindIndex(i => i.Id == recording.Id);
                if (at >= 0) index[at] = info; else index.Insert(0, info);
                SaveIndexLocked(index);
            }
        }

        public void DeleteRecording(string id)
        {
            if (!IsSafeId(id)) return;
            lock (gate)
            {
                string file = RecordingPath(id);
                foreach (string path in new[] { file, file + ".previous", file + ".tmp" })
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                List<RecordingInfo> index = LoadIndexLocked();
                if (index.RemoveAll(i => i.Id == id) > 0) SaveIndexLocked(index);
            }
        }

        public void RenameRecording(string id, string name)
        {
            lock (gate)
            {
                Recording recording = IsSafeId(id) ? ReadRecordingFile(id) : null;
                if (recording == null && IsSafeId(id) && IsBlocked(RecordingPath(id))) throw new IOException(RecordingInUse);
                if (recording == null) throw new FileNotFoundException("That recording is no longer saved.");
                recording.Name = string.IsNullOrWhiteSpace(name) ? Recording.DefaultName(DateTime.Now) : name.Trim();
                SaveRecording(recording);
            }
        }

        /// <summary>Writes the recording as a standalone JSON file anywhere (for sharing or backup).</summary>
        public void ExportRecording(string id, string file)
        {
            lock (gate)
            {
                Recording recording = IsSafeId(id) ? ReadRecordingFile(id) : null;
                if (recording == null) throw new FileNotFoundException("That recording is no longer saved.");
                WriteAtomic(file, Json.Serialize(recording), false);   // no .previous beside a file the person shares
            }
        }

        /// <summary>Reads an exported recording file and saves it as a new recording (new Id). Throws InvalidDataException with a plain message when the file is not a recording.</summary>
        public Recording ImportRecording(string file)
        {
            Recording recording;
            try
            {
                object tree = Json.ParseTree(File.ReadAllText(file, Utf8));
                if (!Json.HasProperty(tree, "Events")) throw new InvalidDataException("That file is not a Macro Kit recording.");
                recording = Json.FromTree<Recording>(tree);
            }
            catch (InvalidDataException) { throw; }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidCastException)
            {
                throw new InvalidDataException("That file is not a Macro Kit recording.", e);
            }
            if (recording.Schema > 1) throw new InvalidDataException("That recording was made by a newer version of Macro Kit.");
            recording.Id = Guid.NewGuid().ToString("N");
            if (recording.Events == null) recording.Events = new List<RecEvent>();
            lock (gate) SaveRecording(recording);
            return recording;
        }

        static RecordingInfo InfoOf(Recording r)
        {
            return new RecordingInfo { Id = r.Id, Name = r.Name ?? "", Created = r.Created ?? "", DurationMs = r.DurationMs, EventCount = r.Events == null ? 0 : r.Events.Count };
        }

        const string RecordingInUse = "That recording is in use by another program, so it can't be changed right now.";

        bool IsRecordingPath(string path)
        {
            return string.Equals(Path.GetDirectoryName(path), RecordingsDir, StringComparison.OrdinalIgnoreCase);
        }

        // A recording's name from the index (read directly, so a problem with the index is not reported twice), or its file name.
        string RecordingLabel(string path)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            try
            {
                RecordingIndexFile file = File.Exists(IndexPath) ? Json.Deserialize<RecordingIndexFile>(File.ReadAllText(IndexPath, Utf8)) : null;
                RecordingInfo info = file == null || file.Recordings == null ? null : file.Recordings.Find(i => i != null && i.Id == id);
                if (info != null && !string.IsNullOrWhiteSpace(info.Name)) return info.Name;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is FormatException || e is InvalidCastException) { }
            return Path.GetFileName(path);
        }

        Recording ReadRecordingFile(string id)
        {
            Recording recording = ReadFile<Recording>(RecordingPath(id));
            if (recording != null)
            {
                recording.Id = id;   // the file name is the identity
                if (recording.Events == null) recording.Events = new List<RecEvent>();
            }
            return recording;
        }

        List<RecordingInfo> LoadIndexLocked()
        {
            RecordingIndexFile file = ReadFile<RecordingIndexFile>(IndexPath);
            List<RecordingInfo> index = file == null ? new List<RecordingInfo>() : file.Recordings;
            var onDisk = new HashSet<string>();
            if (Directory.Exists(RecordingsDir))
            {
                foreach (string path in Directory.GetFiles(RecordingsDir, "*.json"))
                {
                    string id = Path.GetFileNameWithoutExtension(path);
                    if (IsSafeId(id)) onDisk.Add(id);
                }
            }
            var indexed = new HashSet<string>();
            foreach (RecordingInfo info in index) indexed.Add(info.Id ?? "");
            // A recording that could not be opened this session neither forces a rebuild (which would read every recording
            // again, under the lock, on every call) nor is dropped from an index that lists it.
            var compareIndexed = new HashSet<string>(indexed);
            compareIndexed.RemoveWhere(id => IsSafeId(id) && IsBlocked(RecordingPath(id)));
            var compareOnDisk = new HashSet<string>(onDisk);
            compareOnDisk.RemoveWhere(id => IsBlocked(RecordingPath(id)));
            if (compareIndexed.SetEquals(compareOnDisk) && indexed.Count == index.Count) return index;
            return RebuildIndexLocked(onDisk, index);
        }

        List<RecordingInfo> RebuildIndexLocked(IEnumerable<string> ids, List<RecordingInfo> old)
        {
            var rebuilt = new List<RecordingInfo>();
            foreach (string id in ids)
            {
                if (IsBlocked(RecordingPath(id)))
                {
                    // Not read again: keep what the index knew about it, if anything.
                    RecordingInfo known = old.Find(i => i.Id == id);
                    if (known != null) rebuilt.Add(known);
                    continue;
                }
                Recording recording = ReadRecordingFile(id);   // an unreadable file is set aside by ReadFile
                if (recording != null) rebuilt.Add(InfoOf(recording));
            }
            rebuilt.Sort((a, b) => CreatedOf(b).CompareTo(CreatedOf(a)));
            SaveIndexLocked(rebuilt);
            return rebuilt;
        }

        static DateTimeOffset CreatedOf(RecordingInfo info)
        {
            DateTimeOffset when;
            return DateTimeOffset.TryParse(info.Created, CultureInfo.InvariantCulture, DateTimeStyles.None, out when) ? when : DateTimeOffset.MinValue;
        }

        void SaveIndexLocked(List<RecordingInfo> index)
        {
            WriteGuarded(IndexPath, Json.Serialize(new RecordingIndexFile { Recordings = index }), false);   // rebuilt from the recordings next time if skipped
        }

        // Recording ids are file names: only letters, digits and dashes (Guid "N" or "D" formats).
        static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '-') return false;
            return true;
        }

        // ---- license -----------------------------------------------------------------------------

        string LicensePath { get { return Path.Combine(root, "license.key"); } }

        /// <summary>The saved Macro Kit Pro key text, or null when there is none (or it can't be read).</summary>
        public string LoadLicenseKey()
        {
            lock (gate)
            {
                try { return File.Exists(LicensePath) ? File.ReadAllText(LicensePath, Utf8).Trim() : null; }
                catch (Exception ex) { Log.Warn("Could not read license.key: " + ex.Message); return null; }
            }
        }

        public void SaveLicenseKey(string key)
        {
            lock (gate)
            {
                // No .previous: a replaced or removed key should not linger in the folder (or travel with an export).
                // One left by an older version is removed too.
                if (File.Exists(LicensePath + ".previous")) File.Delete(LicensePath + ".previous");
                if (string.IsNullOrEmpty(key)) { if (File.Exists(LicensePath)) File.Delete(LicensePath); return; }
                WriteAtomic(LicensePath, key.Trim() + "\n", false);
            }
        }

        // ---- files --------------------------------------------------------------------------------

        /// <summary>True when the file exists but could not be read this session; it is never written (see <see cref="ReadOnlyFiles"/>).</summary>
        bool IsBlocked(string path)
        {
            return readOnly.Contains(path);
        }

        // Writes unless the file was found unreadable this session: overwriting a file that merely could not be read would destroy it.
        // A skipped write that carries a change (not startup's own seeding) is remembered for UnsavedBlockedFiles.
        void WriteGuarded(string path, string text, bool change = true)
        {
            if (IsBlocked(path))
            {
                if (change) skippedWrites.Add(path);
                Log.Warn("Not saving " + Path.GetFileName(path) + " because it could not be read this session.");
                return;
            }
            if (KeepsSessionCopy(path))
            {
                // Saving the same text again would turn .previous into a copy of the current file, losing the version before.
                if (SameAsSaved(path, text)) return;
                if (sessionCopied.Add(path)) CopySessionStart(path);
            }
            WriteAtomic(path, text);
        }

        // Settings, phrases, macros and keybinds also keep name.json.session: the file as it was when this session first changed
        // it, so a bad edit can still be undone by hand after .previous has moved on.
        bool KeepsSessionCopy(string path)
        {
            return path == SettingsFile || path == PhrasesPath || path == MacrosPath || path == KeybindAppsPath;
        }

        static bool SameAsSaved(string path, string text)
        {
            try { return File.Exists(path) && File.ReadAllText(path, Utf8) == text; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }

        static void CopySessionStart(string path)
        {
            try { if (File.Exists(path)) File.Copy(path, path + ".session", true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Log.Warn("Could not keep the session copy of " + Path.GetFileName(path), e); }
        }

        // Returns null when the file does not exist, could not be parsed (it is moved aside and a warning is recorded), or exists
        // but could not be opened (locked or access denied after a few tries: it is left alone, a warning is recorded and
        // IsBlocked(path) is true from then on, so the caller must not seed or save over it; a recording only until it reads fine again).
        // A file that could not be parsed is replaced by its .previous copy when that one reads fine.
        // patient: wait longer for a lock to clear (the files read once at startup; see ReadRetries).
        T ReadFile<T>(string path, bool patient = false) where T : class
        {
            if (!File.Exists(path)) return null;
            string text = null;
            Exception failure = null;
            int retries = patient ? ReadRetries : QuickReadRetries;
            for (int attempt = 0; attempt <= retries; attempt++)
            {
                try
                {
                    text = File.ReadAllText(path, Utf8);
                    failure = null;
                    break;
                }
                catch (FileNotFoundException) { return null; }          // it vanished after the check: missing
                catch (DirectoryNotFoundException) { return null; }
                catch (IOException e) { failure = e; }                  // locked by another program, for example
                catch (UnauthorizedAccessException e) { failure = e; }
                if (attempt < retries) Thread.Sleep(ReadRetryMs << attempt);
            }
            if (failure != null)
            {
                Log.Warn("Could not read " + Path.GetFileName(path), failure);
                if (readOnly.Add(path))
                    warnings.Add(IsRecordingPath(path)
                        ? "A recording (“" + RecordingLabel(path) + "”) is in use or can't be opened right now; try again in a moment."
                        : Path.GetFileName(path) + " is in use or can't be opened right now, so defaults are being used and this file won't be saved over. Close whatever is using it and restart Macro Kit.");
                return null;
            }
            // A recording that reads fine again is safe to save again (the startup files stay blocked for the session).
            if (IsRecordingPath(path)) readOnly.Remove(path);
            try
            {
                return Json.Deserialize<T>(text);
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidCastException)
            {
                string aside = MoveAside(path, e);
                if (aside == null) return null;
                T restored = RestorePrevious<T>(path);
                warnings.Add(Path.GetFileName(path) + (restored != null
                    ? " could not be read and was set aside as " + Path.GetFileName(aside) + "; an earlier saved copy was put back, so your most recent change to it may be missing."
                    : " could not be read, so it was set aside as " + Path.GetFileName(aside) + " and defaults are being used."));
                return restored;
            }
        }

        // After a damaged file was set aside: an earlier saved copy (.previous, the version before the latest save) is put back in its place when it reads fine,
        // so the person keeps their data instead of getting the defaults or the examples. Null when there is no usable copy.
        T RestorePrevious<T>(string path) where T : class
        {
            string previous = path + ".previous";
            if (!File.Exists(previous)) return null;
            T restored;
            try { restored = Json.Deserialize<T>(File.ReadAllText(previous, Utf8)); }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidCastException || e is IOException || e is UnauthorizedAccessException)
            {
                Log.Warn("The earlier saved copy of " + Path.GetFileName(path) + " could not be read either", e);
                return null;
            }
            try { File.Copy(previous, path, false); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Log.Warn("Could not put back the earlier saved copy of " + Path.GetFileName(path), e); }
            Log.Info("Restored " + Path.GetFileName(path) + " from an earlier saved copy");
            return restored;
        }

        // Returns the set-aside path, or null when the file could not be moved; it is then treated as read-only for the session,
        // so it is never seeded, saved over or rotated.
        string MoveAside(string path, Exception cause)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string target = path + ".unreadable-" + stamp;
            for (int n = 2; File.Exists(target); n++) target = path + ".unreadable-" + stamp + "-" + n;
            try
            {
                File.Move(path, target);
                Log.Warn("Set aside unreadable file " + Path.GetFileName(path), cause);
                return target;
            }
            catch (Exception e)
            {
                readOnly.Add(path);
                warnings.Add(Path.GetFileName(path) + " could not be read and could not be set aside. Defaults are being used for now.");
                Log.Error("Could not set aside " + Path.GetFileName(path), e);
                return null;
            }
        }

        /// <summary>
        /// Writes text (UTF-8, no BOM) via a .tmp file; an existing file is replaced and, with <paramref name="keepPrevious"/>, kept as .previous.
        /// The .tmp file is removed when the write fails.
        /// </summary>
        internal static void WriteAtomic(string path, string text, bool keepPrevious = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Utf8.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, keepPrevious ? path + ".previous" : null);
                else File.Move(temp, path);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { /* the next save overwrites it */ }
                throw;
            }
        }
    }
}
