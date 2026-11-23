using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MacroKit.Core
{
    /// <summary>One guide in the How-to window: a "## Title {#id}" section of HowTo.md and the lines under it.</summary>
    public sealed class HowToTopic
    {
        public string Id = "", Title = "";
        public readonly List<string> Lines = new List<string>();

        /// <summary>The example macros: the lines between ``` fences, one string per example.</summary>
        public List<string> Examples()
        {
            var examples = new List<string>();
            List<string> current = null;
            foreach (string line in Lines)
            {
                if (line.Trim() == "```")
                {
                    if (current == null) current = new List<string>();
                    else { examples.Add(string.Join("\n", current)); current = null; }
                    continue;
                }
                if (current != null) current.Add(line);
            }
            return examples;
        }

        /// <summary>1.13.3: the pictures the guide shows (the names in its "![caption](name.png)" lines).</summary>
        public List<string> Pictures()
        {
            var names = new List<string>();
            string caption, name;
            foreach (string line in Lines)
                if (HowToGuide.TryPicture(line, out caption, out name)) names.Add(name);
            return names;
        }
    }

    /// <summary>
    /// 1.13.1: the how-to guides for the commands that do more than one thing (src\App\HowTo.md, built into the app as
    /// App.HowTo.md). Each command group in the commands panel links to its guide.
    /// </summary>
    public static class HowToGuide
    {
        public const string ResourceName = "App.HowTo.md";

        /// <summary>The guide each command group's "How it works" link opens. Groups of simple steps have none.</summary>
        public static readonly IDictionary<string, string> GroupTopics = new Dictionary<string, string>
        {
            { "keys", "keys" }, { "multi", "multi" }, { "apps", "apps" }, { "windows", "windows" },
            { "pictures", "pictures" }, { "vars", "vars" }, { "conditions", "conditions" },
        };

        /// <summary>1.13.3: the guides whose Show me button takes you to the place in Macro Kit they are about.</summary>
        public static readonly ICollection<string> ShowMeTopics = new HashSet<string>
        {
            "start", "keys", "multi", "apps", "windows", "pictures", "vars", "conditions", "where", "schedule", "recording",
        };

        static readonly Regex Heading = new Regex(@"^##\s+(.+?)\s*\{#([a-z0-9-]+)\}\s*$");
        static readonly Regex PictureLine = new Regex(@"^!\[(.*)\]\(([a-z0-9-]+)\.png\)$");

        /// <summary>
        /// 1.13.3: a picture line, "![caption](name.png)". The app shows the built-in name-light.png or name-dark.png to suit
        /// the theme (src\App\HowToImages, made by the self test).
        /// </summary>
        public static bool TryPicture(string line, out string caption, out string name)
        {
            Match m = PictureLine.Match((line ?? "").Trim());
            caption = m.Success ? m.Groups[1].Value.Trim() : "";
            name = m.Success ? m.Groups[2].Value : "";
            return m.Success;
        }

        public static List<HowToTopic> Parse(string text)
        {
            var topics = new List<HowToTopic>();
            HowToTopic current = null;
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimEnd();
                Match m = Heading.Match(line);
                if (m.Success)
                {
                    current = new HowToTopic { Title = m.Groups[1].Value, Id = m.Groups[2].Value };
                    topics.Add(current);
                    continue;
                }
                if (current != null) current.Lines.Add(line);
            }
            // No blank lines left over at either end of a guide.
            foreach (HowToTopic t in topics)
            {
                while (t.Lines.Count > 0 && t.Lines[0].Length == 0) t.Lines.RemoveAt(0);
                while (t.Lines.Count > 0 && t.Lines[t.Lines.Count - 1].Length == 0) t.Lines.RemoveAt(t.Lines.Count - 1);
            }
            return topics;
        }

        /// <summary>The guides built into this program, or an empty list when the build has none.</summary>
        public static List<HowToTopic> Load()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null) return new List<HowToTopic>();
                using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
            }
        }
    }
}
