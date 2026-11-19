using System;
using System.Globalization;
using System.Text;

namespace MacroKit.Phrases
{
    /// <summary>
    /// Fills the placeholders a phrase's text can hold when it expands:
    /// {date} and {time} (this PC's short date and time), {date:dd MMM yyyy} or {time:HH:mm} with a .NET format,
    /// and {clipboard} (the text on the clipboard). Names ignore case. Anything else in braces is left exactly as typed.
    /// </summary>
    public static class Placeholders
    {
        /// <summary>The placeholders, for help text and the editor's hint.</summary>
        public const string Help = "{date}, {time}, {clipboard}; a format such as {date:dd MMM yyyy} or {time:HH:mm}";

        /// <summary>
        /// The text with every placeholder filled. <paramref name="clipboard"/> is called at most once, only when the text
        /// uses {clipboard}; null from it counts as empty.
        /// </summary>
        public static string Fill(string text, DateTime now, Func<string> clipboard)
        {
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
                        });
                        if (value != null) { sb.Append(value); i = end + 1; continue; }
                    }
                }
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        // The value of one placeholder, or null when the braces hold something else.
        static string Value(string inner, DateTime now, Func<string> clipboard)
        {
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
                default:
                    return null;
            }
        }

        static string Format(DateTime now, string format)
        {
            try { return now.ToString(format, CultureInfo.CurrentCulture); }
            catch (FormatException) { return null; }
        }
    }
}
