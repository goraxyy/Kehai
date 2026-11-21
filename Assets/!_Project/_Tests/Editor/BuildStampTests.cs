using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// 0.5.2, C10.2: every copy of the game says what it is, and every shift record carries it.
public class BuildStampTests
{
    [Test]
    public void TheLine_NamesTheVersionTheCommitsAndHerNumbers()
    {
        string line = BuildStamp.Line;
        StringAssert.StartsWith("Kehai " + BuildStamp.Current.version, line);
        StringAssert.Contains(" · code ", line);
        StringAssert.Contains(" · art ", line);
        StringAssert.Contains("Karen: ", line);
        StringAssert.Contains(Edition.VersionLabel(Application.version, Edition.Current), BuildStamp.Current.version);
    }

    [Test]
    public void TheStampFile_ReadsBackAsWritten()
    {
        var d = new BuildStamp.Data { version = "0.6.0-beta.2-testing", edition = "testing", code = "a1b2c3d", art = "9f8e7d6" };
        BuildStamp.Data back = JsonUtility.FromJson<BuildStamp.Data>(JsonUtility.ToJson(d));
        Assert.AreEqual(d.version, back.version);
        Assert.AreEqual(d.code, back.code);
        Assert.AreEqual(d.art, back.art);
    }

    [Test]
    public void EveryShiftRecord_CarriesTheStamp()
    {
        string json = new ShiftRecording().ToJson(null, null);
        StringAssert.Contains("\"build\":\"Kehai ", json);
    }
}
