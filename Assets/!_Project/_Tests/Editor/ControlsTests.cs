using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

// The key list (Esc → Keys, and CONTROLS.md) has to be the truth: every key the code reads
// is on it, and the file says the same as the game.
public class ControlsTests
{
    static string Root => Directory.GetParent(Application.dataPath).FullName;

    [Test]
    public void ControlsMd_MatchesTheInGameList()
    {
        string md = File.ReadAllText(Path.Combine(Root, "docs", "design", "CONTROLS.md"));
        var missing = Controls.All.SelectMany(s => s.Entries)
            .Where(e => !md.Contains($"| {e.Keys} | {e.Action} |"))
            .Select(e => e.Keys).ToList();
        Assert.IsEmpty(missing, "CONTROLS.md is out of date for: " + string.Join(", ", missing));
    }

    // How a KeyCode is written in the list, where that differs from its name.
    static readonly Dictionary<string, string> Written = new Dictionary<string, string>
    {
        { "Alpha1", "1" }, { "Alpha2", "2" }, { "Alpha3", "3" }, { "Alpha4", "4" },
        { "LeftShift", "Left Shift" }, { "LeftControl", "Left Ctrl" },
        { "Return", "Enter" }, { "KeypadEnter", "Enter" }, { "Escape", "Esc" },
        { "UpArrow", "Up" }, { "DownArrow", "Down" }, { "LeftArrow", "Left" }, { "RightArrow", "Right" },
        { "Comma", "," }, { "Period", "." }, { "Minus", "-" }, { "Equals", "=" }, { "LeftBracket", "[" }, { "RightBracket", "]" },
    };

    [Test]
    public void EveryKeyTheGameReads_IsListed()
    {
        string listed = string.Join("\n", Controls.All.SelectMany(s => s.Entries).Select(e => e.Keys));
        var used = new SortedSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(Application.dataPath, "!_Project"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("_Tests") || file.Contains("/Eval/") || file.Contains("\\Eval\\")) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"KeyCode\.([A-Za-z0-9]+)"))
                if (m.Groups[1].Value != "None") used.Add(m.Groups[1].Value);
        }
        var missing = used.Where(k =>
        {
            string name = Written.TryGetValue(k, out string w) ? w : k;
            return !Regex.IsMatch(listed, $@"(^|[^A-Za-z0-9]){Regex.Escape(name)}($|[^A-Za-z0-9])", RegexOptions.Multiline);
        }).ToList();
        Assert.IsEmpty(missing, "keys the game reads but Controls.cs doesn't list: " + string.Join(", ", missing));
    }
}
