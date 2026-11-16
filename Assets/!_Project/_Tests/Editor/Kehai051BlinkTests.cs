using System.IO;
using Kehai.Blink;
using NUnit.Framework;
using UnityEngine;

// 0.5.1, item 7: the camera helper is found, built and packaged, and its failures read plainly.
// Nothing here starts the helper or the camera.
public class Kehai051BlinkTests
{
    [Test]
    public void HelperExitCodes_BecomePlainStatuses()
    {
        StringAssert.Contains("No camera found", BlinkSidecar.ProblemFor(2));
        StringAssert.Contains("wouldn't start", BlinkSidecar.ProblemFor(3));
        StringAssert.Contains("Privacy & Security", BlinkSidecar.ProblemFor(4));
        StringAssert.Contains("exit code 9", BlinkSidecar.ProblemFor(9));
    }

    [Test]
    public void BuildScriptAndSourceShipWithTheProject_AndTheBuildEmbedsTheHelper()
    {
        string root = KehaiBuild.ProjectRoot;
        Assert.IsTrue(File.Exists(Path.Combine(root, "tools/blink/mac/build.sh")));
        Assert.IsTrue(File.Exists(Path.Combine(root, "tools/blink/mac/BlinkVision.swift")));
        Assert.AreEqual(Path.Combine(root, "tools", "blink", "mac", "build.sh"), KehaiBuild.BlinkHelperScript);
        // The packaged path the game looks in first: <app>/Contents/Helpers/BlinkVision.
        string source = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/Blink/Scripts/BlinkSidecar.cs"));
        StringAssert.Contains("\"Helpers/BlinkVision\"", source);
        string build = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Core/Editor/KehaiBuild.cs"));
        StringAssert.Contains("\"Contents\", \"Helpers\", \"BlinkVision\"", build);
    }
}
