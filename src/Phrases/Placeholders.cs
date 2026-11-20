using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MacroKit.Phrases
{
    /// <summary>
    /// Fills the placeholders a phrase's text can hold when it expands:
    /// {date} and {time} (this PC's short date and time), {date:dd MMM yyyy} or {time:HH:mm} with a .NET format,
    /// {clipboard} (the text on the clipboard), {cursor} (where the caret ends up) and {fill:Name} (asked for as the phrase expands).
    /// Names ignore case. Anything else in braces is left exactly as typed.
    /// </summary>
    public static class Placeholders
    {
        /// <summary>The placeholders, for help text and the editor's hint.</summary>
        public const string Help = "{date}, {time}, {clipboard}, {cursor}, {fill:Name}; a format such as {date:dd MMM yyyy} or {time:HH:mm}";

        /// <summary>The {fill:Name} names in the text, in order, each once (names compare ignoring case).</summary>
        public static List<string> Fields(string text)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(text)) return names;
            foreach (string inner in Inners(text))
            {
                string name = FieldName(inner);
                if (name != null && !names.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) names.Add(name);
            }
            return names;
        }

        static IEnumerable<string> Inners(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) yield break;
                int end = text.IndexOf('}', open + 1), nextOpen = text.IndexOf('{', open + 1);
                if (end > open && (nextOpen < 0 || nextOpen > end)) { yield return text.Substring(open + 1, end - open - 1); i = end + 1; }
                else i = open + 1;
            }
        }

        // "fill:Name" gives "Name"; anything else null.
        static string FieldName(string inner)
        {
            int colon = inner.IndexOf(':');
            if (colon < 0 || !string.Equals(inner.Substring(0, colon).Trim(), "fill", StringComparison.OrdinalIgnoreCase)) return null;
            string name = inner.Substring(colon + 1).Trim();
            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// How many Left-arrow presses move the caret from the end of <paramref name="text"/> back to position
        /// <paramref name="at"/>: a line break (CR LF or LF) and a surrogate pair each count once. 0 when at is -1 (no {cursor}).
        /// </summary>
        public static int LeftPresses(string text, int at)
        {
            if (at < 0 || text == null) return 0;
            int presses = 0;
            for (int i = text.Length - 1; i >= at; i--)
            {
                if (text[i] == '\n' && i > at && text[i - 1] == '\r') i--;
                else if (char.IsLowSurrogate(text[i]) && i > at && char.IsHighSurrogate(text[i - 1])) i--;
                presses++;
            }
            return presses;
        }

        /// <summary>
        /// Like <see cref="Fill(string, DateTime, Func{string}, IDictionary{string, string})"/>; <paramref name="cursorAt"/> is where
        /// the first {cursor} was in the filled text, or -1. The position is kept apart from the text, so nothing the
        /// clipboard or an answer holds can be mistaken for it.
        /// </summary>
        public static string Fill(string text, DateTime now, Func<string> clipboard, IDictionary<string, string> fields, out int cursorAt)
        {
            return FillCore(text, now, clipboard, fields, out cursorAt);
        }

        /// <summary>Like <see cref="Fill(string, DateTime, Func{string})"/>, with the answers for {fill:Name} (missing ones are empty).</summary>
        public static string Fill(string text, DateTime now, Func<string> clipboard, IDictionary<string, string> fields)
        {
            int cursorAt;
            return FillCore(text, now, clipboard, fields, out cursorAt);
        }

        /// <summary>
        /// The text with every placeholder filled. <paramref name="clipboard"/> is called at most once, only when the text
        /// uses {clipboard}; null from it counts as empty.
        /// </summary>
        public static string Fill(string text, DateTime now, Func<string> clipboard)
        {
            int cursorAt;
            return FillCore(text, now, clipboard, null, out cursorAt);
        }

        static string FillCore(string text, DateTime now, Func<string> clipboard, IDictionary<string, string> fields, out int cursorAt)
        {
            cursorAt = -1;
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length + 16);
            string clip = null;
            bool clipRead = false;
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '{')
                {
                    int end = text.IndexOf('}', i + 1);
                    int nextOpen = text.IndexOf('{', i + 1);
                    if (end > i && (nextOpen < 0 || nextOpen > end))
                    {
                        string inner = text.Substring(i + 1, end - i - 1);
                        string value = Value(inner, now, () =>
                        {
                            if (!clipRead) { clip = (clipboard == null ? null : clipboard()) ?? ""; clipRead = true; }
                            return clip;
                        }, fields);
                        if (ReferenceEquals(value, CursorValue))
                        {
                            if (cursorAt < 0) cursorAt = sb.Length;   // the first {cursor} counts; any later one just goes
                            i = end + 1;
                            continue;
                        }
                        if (value != null) { sb.Append(value); i = end + 1; continue; }
                    }
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        // What Value gives for {cursor}: told apart by reference, so no real text can ever look like it.
        static readonly string CursorValue = new string('|', 1);

        // The value of one placeholder (CursorValue for {cursor}), or null when the braces hold something else.
        static string Value(string inner, DateTime now, Func<string> clipboard, IDictionary<string, string> fields)
        {
            string field = FieldName(inner);
            if (field != null)
            {
                if (fields == null) return "";
                foreach (KeyValuePair<string, string> pair in fields)
                {
                    if (string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase)) return pair.Value ?? "";
                }
                return "";
            }
            string name = inner, format = null;
            int colon = inner.IndexOf(':');
            if (colon >= 0) { name = inner.Substring(0, colon); format = inner.Substring(colon + 1); }
            name = name.Trim().ToLowerInvariant();
            switch (name)
            {
                case "date":
                    return Format(now, string.IsNullOrEmpty(format) ? "d" : format);
                case "time":
                    return Format(now, string.IsNullOrEmpty(format) ? "t" : format);
                case "clipboard":
                    return format == null ? clipboard() : null;
                case "cursor":
                    return format == null ? CursorValue : null;
                default:
                    return null;
            }
        }

        // A one-letter format is a .NET standard format ("d", "t"...). The four letters that are only custom formats (h, H, K, z)
        // would throw there, so they are read as custom ones: {time:H} is the hour rather than braces left as typed.
        static string Format(DateTime now, string format)
        {
            try { return now.ToString(format, CultureInfo.CurrentCulture); }
            catch (FormatException) { }
            if (format.Length != 1 || "hHKz".IndexOf(format[0]) < 0) return null;
            try { return now.ToString("%" + format, CultureInfo.CurrentCulture); }
            catch (FormatException) { return null; }
        }
    }
}
