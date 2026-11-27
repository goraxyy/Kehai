using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEngine;

// The owner's call (26 November, A9): Karen plays with the fitted senses, S1. These pin what makes
// S1's numbers safe to play, so a refit can't quietly break them:
// - they're the fit's, and the default;
// - a surer look is a tighter circle, an older tip a wider one, a farther noise no surer;
// - a fitted confidence over 1 (hearing is fitted at about 3) does no more than a sure sighting,
//   because the belief clamps it before use.
public class Kehai052SensesTests
{
    [Test]
    public void TheFittedSenses_AreTheDefault_AndHangTogether()
    {
        SenseModel s1 = SenseModel.For("s1");
        Assert.IsTrue(s1.Trained, "the fitted table, not the hand-set numbers");
        StringAssert.StartsWith("fit-", s1.Version);
        Assert.AreEqual("s1", new KarenConfig().senses);

        Assert.LessOrEqual(s1.SightSigma(1f), s1.SightSigma(0f), "a surer look draws a tighter circle");
        Assert.GreaterOrEqual(s1.SightSigma(1f), 0.5f - 1e-4f, "never tighter than the filter's 0.5 m floor");
        Assert.Greater(s1.TestimonySigma(30f), s1.TestimonySigma(0f), "an older tip is a wider circle");
        Assert.GreaterOrEqual(s1.HearingSigma(30f, 30f, false), 0.5f - 1e-4f);
        Assert.LessOrEqual(s1.HearingConfidence(20f, 25f), s1.HearingConfidence(2f, 25f), "a farther noise is no surer");
    }

    [Test]
    public void AConfidenceOverOne_DoesNoMoreThanASureSighting()
    {
        StoreMap map = StoreMap.Synthetic(20, 20, null);
        Vector3 at = map.CellPosition[map.CellCount / 2];
        var sure = new BeliefGrid(map);
        var over = new BeliefGrid(map);
        sure.Apply(new Observation(SenseChannel.Hearing, at, 1f, 1f, "a sure noise"));
        over.Apply(new Observation(SenseChannel.Hearing, at, SenseModel.For("s1").HearingConfidence(2f, 25f), 1f, "a fitted noise"));
        float[] a = sure.Snapshot(), b = over.Snapshot();
        Assert.AreEqual(a.Length, b.Length);
        Assert.IsTrue(a.Zip(b, (x, y) => Mathf.Abs(x - y)).All(d => d < 1e-6f), "clamped to 1 before use");
    }
}
