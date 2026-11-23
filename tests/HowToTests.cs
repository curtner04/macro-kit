using System;
using System.Collections.Generic;
using System.Linq;

namespace MacroKit.Tests
{
    using MacroKit.Core;
    using MacroKit.Macros;

    /// <summary>1.13.1: the how-to guides (src\App\HowTo.md, built in as App.HowTo.md).</summary>
    public static class HowToTests
    {
        static void WithPro(Action body)
        {
            LicenseInfo before = Edition.License;
            Edition.Set(new LicenseInfo { Serial = 1, Issued = new DateTime(2026, 11, 17), Name = "Test" });
            try { body(); }
            finally { Edition.Set(before); }
        }

        [Test]
        public static void TheGuidesAreBuiltIn()
        {
            List<HowToTopic> topics = HowToGuide.Load();
            Assert.True(topics.Count >= 8, "guides found: " + topics.Count);
            Assert.Equal("start", topics[0].Id);
            Assert.Equal(topics.Count, topics.Select(t => t.Id).Distinct().Count(), "every guide has its own id");
            foreach (HowToTopic t in topics)
            {
                Assert.True(t.Title.Length > 0 && t.Lines.Count > 0, "guide " + t.Id + " has a title and text");
                Assert.True(t.Lines.Count(l => l.Trim() == "```") % 2 == 0, "guide " + t.Id + " closes every example");
            }
        }

        [Test]
        public static void PictureLinesNameABuiltInPicture()
        {
            string caption, name;
            Assert.True(HowToGuide.TryPicture("![The Keys group.](keys.png)", out caption, out name));
            Assert.Equal("The Keys group.", caption);
            Assert.Equal("keys", name);
            Assert.False(HowToGuide.TryPicture("Press **Show me** (keys.png)", out caption, out name));
            Assert.False(HowToGuide.TryPicture("![x](../secret.png)", out caption, out name), "only plain names");
            var topics = HowToGuide.Load();
            Assert.True(topics.Sum(t => t.Pictures().Count) >= 10, "the guides show pictures");
            foreach (HowToTopic t in topics)
                if (t.Pictures().Count > 0) Assert.True(HowToGuide.ShowMeTopics.Contains(t.Id), t.Id + " has pictures and a Show me button");
        }

        [Test]
        public static void EveryCommandGroupLinkHasAGuide()
        {
            var ids = new HashSet<string>(HowToGuide.Load().Select(t => t.Id));
            foreach (KeyValuePair<string, string> link in HowToGuide.GroupTopics)
                Assert.True(ids.Contains(link.Value), "the " + link.Key + " group links to a guide that exists: " + link.Value);
            Assert.True(ids.Contains("pictures"), "waiting for a picture has a guide");
        }

        [Test]
        public static void EveryExampleIsAWorkingMacro()
        {
            WithPro(() =>
            {
                int count = 0;
                foreach (HowToTopic t in HowToGuide.Load())
                    foreach (string example in t.Examples())
                    {
                        count++;
                        PlainResult plain = PlainMacro.ToScript(example);
                        PlainLine bad = plain.Lines.FirstOrDefault(l => l.Error != null);
                        Assert.True(bad == null, "guide " + t.Id + ": the line '" + (bad == null ? "" : bad.Source) + "' is not understood: " + (bad == null ? "" : bad.Error));
                        ParseResult parsed = MacroParser.Parse(plain.Script, false);
                        Assert.True(parsed.Ok, "guide " + t.Id + ": " + string.Join("; ", parsed.Errors.Select(e => e.Message)) + " in\n" + plain.Script);
                        Assert.True(parsed.Steps.Count > 0, "guide " + t.Id + " example has steps");
                    }
                Assert.True(count >= 8, "examples checked: " + count);
            });
        }

        [Test]
        public static void ParsingSplitsGuidesAndKeepsExamples()
        {
            List<HowToTopic> topics = HowToGuide.Parse("# Title\r\nignored\r\n## One {#one}\r\n\r\nText\r\n```\r\nctrl+c\r\nwait 50ms\r\n```\r\n\r\n## Two words {#two-b}\r\nMore\r\n");
            Assert.Equal(2, topics.Count);
            Assert.Equal("One", topics[0].Title);
            Assert.Equal("two-b", topics[1].Id);
            Assert.Equal("Text", topics[0].Lines[0]);
            Assert.Equal("```", topics[0].Lines[topics[0].Lines.Count - 1], "no blank lines at the end");
            Assert.Equal(1, topics[0].Examples().Count);
            Assert.Equal("ctrl+c\nwait 50ms", topics[0].Examples()[0]);
            Assert.Equal(0, HowToGuide.Parse(null).Count);
        }
    }
}
