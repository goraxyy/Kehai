using System;
using System.Collections.Generic;

namespace Kehai.Playtest
{
    // C2.8: the first-launch walkthrough, after the consent page: four short pages, skippable,
    // reopened from "How to play" on the main menu. Each key line comes from Controls.All, so it
    // can't drift from the game; it teaches the controls, never Karen's tricks.
    public static class PlaytestWalkthrough
    {
        public static readonly (string title, string intro, string[] keys)[] Pages =
        {
            ("Your shift",
             "Clock in at the time clock by the staff door (look at it, press E), then do the jobs on your task list. " +
             "Keep the store running. " + GameNames.Antagonist + ", the store's AI, is watching.",
             new[] { "E", "C" }),
            ("Moving",
             "Sprinting is quick but loud; crouching is slow but quiet.",
             new[] { "W A S D", "Mouse", "Left Shift (hold)", "Left Ctrl (hold)" }),
            ("Using things",
             "Most jobs are E on the right thing, with the right thing in hand.",
             new[] { "E (hold)", "Q", "Q (hold, then let go)", "1 2 3 4 / mouse wheel" }),
            ("If something's wrong",
             "Press Shift + F7 the moment something breaks or confuses you, and type a line about it. " +
             "After three shifts, or when you quit, a few optional questions follow, and the game sends your session if you agreed.",
             new[] { "Esc" }),
        };

        // "<keys>  <what they do>", with the action cut to its first part; null if Controls lacks the key.
        public static string Line(string keys)
        {
            foreach (Controls.Section section in Controls.All)
                foreach (Controls.Entry e in section.Entries)
                    if (e.Keys == keys) return $"<b>{keys}</b>   {Short(e.Action)}";
            return null;
        }

        public static string Short(string action)
        {
            string s = action ?? "";
            int colon = s.IndexOf(':');
            if (colon > 0) s = s.Substring(0, colon);
            int stop = s.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 0) s = s.Substring(0, stop);
            if (s.Length <= 90) return s;
            int comma = s.LastIndexOf(", ", 90, StringComparison.Ordinal);
            return (comma > 20 ? s.Substring(0, comma) : s.Substring(0, 90)) + "…";
        }

        public static string Body(int page)
        {
            var p = Pages[page];
            var lines = new List<string> { p.intro, "" };
            foreach (string k in p.keys) lines.Add(Line(k) ?? k);
            return string.Join("\n", lines);
        }
    }
}
