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

    internal sealed class RecordingIndexFile
    {
        public int Schema { get; set; }
        public List<RecordingInfo> Recordings { get; set; }
        public RecordingIndexFile() { Schema = 1; Recordings = new List<RecordingInfo>(); }
    }

    /// <summary>
    /// Reads and writes everything Macro Kit saves, as JSON files in one data folder. Thread-safe (one lock).
    /// Every write goes to a .tmp file first and is swapped in with File.Replace, keeping the old file as .previous.
    /// A file that cannot be read is moved aside (name.unreadable-yyyyMMdd-HHmmss) and defaults are used; the move is reported
    /// in <see cref="Warnings"/> so the UI can tell the user. User data is never silently overwritten.
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

        const int ReadRetries = 3;
        const int ReadRetryMs = 50;
        readonly HashSet<string> readOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // paths that exist but could not be read

        public string DataRoot { get { return root; } }

        string SettingsFile { get { return Path.Combine(root, "settings.json"); } }
        string PhrasesPath { get { return Path.Combine(root, "phrases.json"); } }
        string MacrosPath { get { return Path.Combine(root, "macros.json"); } }
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
        /// The shell should tell the user while this list is not empty.
        /// </summary>
        public IList<string> ReadOnlyFiles
        {
            get
            {
                lock (gate)
                {
                    var names = new List<string>();
                    foreach (string path in readOnly) names.Add(Path.GetFileName(path));
                    names.Sort(StringComparer.OrdinalIgnoreCase);
                    return names;
                }
            }
        }

        // ---- settings, phrases, macros ------------------------------------------------------------

        public AppSettings LoadSettings()
        {
            lock (gate)
            {
                AppSettings settings = ReadFile<AppSettings>(SettingsFile);
                bool fresh = settings == null && !IsBlocked(SettingsFile);   // only a file that is truly missing (or was set aside) gets defaults written
                if (settings == null) settings = new AppSettings();
                if (settings.Playback == null) settings.Playback = new PlaybackSettings();
                if (settings.ExcludedPrograms == null) settings.ExcludedPrograms = new List<string>();
                if (fresh) WriteGuarded(SettingsFile, Json.Serialize(settings));   // first start: the file is required for transfer
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
                PhrasesFile file = ReadFile<PhrasesFile>(PhrasesPath);
                if (file == null)
                {
                    file = new PhrasesFile { Phrases = SeedPhrases() };
                    WriteGuarded(PhrasesPath, Json.Serialize(file));
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
                MacrosFile file = ReadFile<MacrosFile>(MacrosPath);
                if (file == null)
                {
                    file = new MacrosFile { Macros = SeedMacros() };
                    WriteGuarded(MacrosPath, Json.Serialize(file));
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
                }
                return file.Macros;
            }
        }

        public void SaveMacros(IEnumerable<MacroDef> macros)
        {
            if (macros == null) throw new ArgumentNullException("macros");
            lock (gate) WriteGuarded(MacrosPath, Json.Serialize(new MacrosFile { Macros = new List<MacroDef>(macros) }));
        }

        /// <summary>First-start phrase: /shrug becomes the shrug emoticon.</summary>
        public static List<PhraseDef> SeedPhrases()
        {
            return new List<PhraseDef>
            {
                new PhraseDef { Trigger = "/shrug", Replacement = "¯\\_(ツ)_/¯", Enabled = true, When = ExpandWhen.Immediately, Method = InsertMethod.Type },
            };
        }

        /// <summary>First-start macro: copy, switch, paste, switch back, next row.</summary>
        public static List<MacroDef> SeedMacros()
        {
            return new List<MacroDef>
            {
                new MacroDef
                {
                    Name = "Copy, switch, paste, next row",
                    Script = "{{CTRL}{C}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{CTRL}{V}}{{PAUSE:50}}{{ALT}{TAB}}{{PAUSE:100}}{{DOWN}}{{PAUSE:50}}",
                    Repeat = RepeatMode.Times,
                    RepeatTimes = 5,
                },
            };
        }

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
                WriteAtomic(file, Json.Serialize(recording));
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
            if (indexed.SetEquals(onDisk) && indexed.Count == index.Count) return index;
            return RebuildIndexLocked(onDisk);
        }

        List<RecordingInfo> RebuildIndexLocked(IEnumerable<string> ids)
        {
            var rebuilt = new List<RecordingInfo>();
            foreach (string id in ids)
            {
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
            WriteGuarded(IndexPath, Json.Serialize(new RecordingIndexFile { Recordings = index }));
        }

        // Recording ids are file names: only letters, digits and dashes (Guid "N" or "D" formats).
        static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!(char.IsLetterOrDigit(c) && c < 128) && c != '-') return false;
            return true;
        }

        // ---- files --------------------------------------------------------------------------------

        /// <summary>True when the file exists but could not be read this session; it is never written (see <see cref="ReadOnlyFiles"/>).</summary>
        bool IsBlocked(string path)
        {
            return readOnly.Contains(path);
        }

        // Writes unless the file was found unreadable this session: overwriting a file that merely could not be read would destroy it.
        void WriteGuarded(string path, string text)
        {
            if (IsBlocked(path))
            {
                Log.Warn("Not saving " + Path.GetFileName(path) + " because it could not be read this session.");
                return;
            }
            WriteAtomic(path, text);
        }

        // Returns null when the file does not exist, could not be parsed (it is moved aside and a warning is recorded), or exists
        // but could not be opened (locked or access denied after a few tries: it is left alone, a warning is recorded and
        // IsBlocked(path) is true from then on, so the caller must not seed or save over it).
        T ReadFile<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            string text = null;
            Exception failure = null;
            for (int attempt = 0; attempt <= ReadRetries; attempt++)
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
                if (attempt < ReadRetries) Thread.Sleep(ReadRetryMs);
            }
            if (failure != null)
            {
                Log.Warn("Could not read " + Path.GetFileName(path), failure);
                if (readOnly.Add(path))
                    warnings.Add(Path.GetFileName(path) + " is in use or can't be opened right now, so defaults are being used and this file won't be saved over. Close whatever is using it and restart Macro Kit.");
                return null;
            }
            try
            {
                return Json.Deserialize<T>(text);
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidCastException)
            {
                MoveAside(path, e);
                return null;
            }
        }

        void MoveAside(string path, Exception cause)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string target = path + ".unreadable-" + stamp;
            for (int n = 2; File.Exists(target); n++) target = path + ".unreadable-" + stamp + "-" + n;
            try
            {
                File.Move(path, target);
                warnings.Add(Path.GetFileName(path) + " could not be read, so it was set aside as " + Path.GetFileName(target) + " and defaults are being used.");
                Log.Warn("Set aside unreadable file " + Path.GetFileName(path), cause);
            }
            catch (Exception e)
            {
                warnings.Add(Path.GetFileName(path) + " could not be read and could not be set aside. Defaults are being used for now.");
                Log.Error("Could not set aside " + Path.GetFileName(path), e);
            }
        }

        /// <summary>Writes text (UTF-8, no BOM) via a .tmp file; an existing file is replaced and kept as .previous.</summary>
        internal static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Utf8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous");
            else File.Move(temp, path);
        }
    }
}
