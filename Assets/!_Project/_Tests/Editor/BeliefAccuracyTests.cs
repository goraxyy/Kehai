using System.Linq;
using Kehai;
using Kehai.Eval;
using NUnit.Framework;
using UnityEngine;

// The belief-accuracy metric (BeliefAccuracy.cs) on a grid small enough to score by hand:
// six cells 1.5 m apart, three across and two deep.
//
//   z = 1.5   [3] [4] [5]
//   z = 0     [0] [1] [2]
//             x=0 1.5  3
public class BeliefAccuracyTests
{
    static readonly Vector3[] Cells =
    {
        new Vector3(0f, 0f, 0f), new Vector3(1.5f, 0f, 0f), new Vector3(3f, 0f, 0f),
        new Vector3(0f, 0f, 1.5f), new Vector3(1.5f, 0f, 1.5f), new Vector3(3f, 0f, 1.5f),
    };

    static readonly float[] Belief = { 0.4f, 0.2f, 0.1f, 0.1f, 0.1f, 0.1f };

    [Test]
    public void Score_TrueCellIsThePeak()
    {
        BeliefScore.Result r = BeliefScore.Score(Belief, Cells, 0);
        Assert.AreEqual(Mathf.Log(0.4f), r.LogP, 1e-6f);
        Assert.AreEqual(1f, r.Top1);
        Assert.AreEqual(1f, r.Top5);
        Assert.AreEqual(1f, r.Top10);
        Assert.AreEqual(0f, r.Error, 1e-6f);
    }

    [Test]
    public void Score_TrueCellIsSecond()
    {
        BeliefScore.Result r = BeliefScore.Score(Belief, Cells, 1);
        Assert.AreEqual(Mathf.Log(0.2f), r.LogP, 1e-6f);
        Assert.AreEqual(0f, r.Top1);
        Assert.AreEqual(1f, r.Top5);
        Assert.AreEqual(1.5f, r.Error, 1e-5f, "metres from the peak (cell 0) to the truth (cell 1)");
    }

    [Test]
    public void Score_TiesCountAsTheChanceARandomOrderPutsTheTruthInside()
    {
        // Cell 5 shares 0.1 with three others, behind two likelier cells: ranks 3–6, so it is
        // in the top five in 3 of the 4 orders of the tie.
        BeliefScore.Result r = BeliefScore.Score(Belief, Cells, 5);
        Assert.AreEqual(Mathf.Log(0.1f), r.LogP, 1e-6f);
        Assert.AreEqual(0f, r.Top1);
        Assert.AreEqual(0.75f, r.Top5, 1e-6f);
        Assert.AreEqual(1f, r.Top10);
        Assert.AreEqual(Mathf.Sqrt(3f * 3f + 1.5f * 1.5f), r.Error, 1e-5f);
    }

    [Test]
    public void Score_ZeroProbabilityIsFloored()
    {
        float[] p = { 0.5f, 0.5f, 0f, 0f, 0f, 0f };
        BeliefScore.Result r = BeliefScore.Score(p, Cells, 4);
        Assert.AreEqual(Mathf.Log(BeliefScore.ProbabilityFloor), r.LogP, 1e-5f);
        Assert.IsFalse(float.IsInfinity(r.LogP));
        // Two peaks: the error is the mean distance from both to the truth.
        float expected = (Mathf.Sqrt(1.5f * 1.5f + 1.5f * 1.5f) + 1.5f) / 2f;
        Assert.AreEqual(expected, r.Error, 1e-5f);
    }

    [Test]
    public void Score_UniformIsKOverN()
    {
        var p = new float[Cells.Length];
        BeliefScore.FillUniform(p);
        BeliefScore.Result r = BeliefScore.Score(p, Cells, 2);
        Assert.AreEqual(-Mathf.Log(6f), r.LogP, 1e-5f);
        Assert.AreEqual(1f / 6f, r.Top1, 1e-6f);
        Assert.AreEqual(5f / 6f, r.Top5, 1e-6f);
        Assert.AreEqual(1f, r.Top10);
        float mean = Cells.Average(c => Vector3.Distance(c, Cells[2]));
        Assert.AreEqual(mean, r.Error, 1e-5f, "every cell is a peak, so the error is the mean distance");
    }

    [Test]
    public void LastSeen_PeaksAtTheFixRanksByDistanceAndWidensWithTime()
    {
        var fresh = new float[Cells.Length];
        BeliefScore.FillLastSeen(fresh, Cells, Cells[0], 0f);
        Assert.AreEqual(1f, fresh.Sum(), 1e-5f);
        Assert.Greater(fresh[0], fresh[1]);
        Assert.Greater(fresh[1], fresh[4], "a diagonal neighbour is further than a straight one");
        Assert.Greater(fresh[4], fresh[5]);
        Assert.AreEqual(1f, BeliefScore.Score(fresh, Cells, 0).Top1);

        var stale = new float[Cells.Length];
        BeliefScore.FillLastSeen(stale, Cells, Cells[0], 30f);
        Assert.AreEqual(1f, stale.Sum(), 1e-5f);
        Assert.Less(stale[0], fresh[0], "thirty seconds later she should be less sure");
        Assert.Greater(stale[5], fresh[5]);
    }

    [Test]
    public void Episode_WritesTheColumnsAndNullsAnEmptySplit()
    {
        var m = new EpisodeMetrics();
        m.Belief.OutOfView.Add(BeliefScore.Score(Belief, Cells, 1));
        m.Belief.OutOfView.Add(BeliefScore.Score(Belief, Cells, 0));
        var d = m.ToDictionary();

        Assert.AreEqual(2, d["belief_out_of_view_samples"]);
        Assert.AreEqual(0.5f, (float)d["belief_out_of_view_top1"], 1e-6f);
        Assert.AreEqual(0.75f, (float)d["belief_out_of_view_err_m"], 1e-6f);
        Assert.AreEqual((Mathf.Log(0.2f) + Mathf.Log(0.4f)) / 2f, (float)d["belief_out_of_view_logp"], 1e-5f);

        string json = MiniJson.Serialize(d);
        StringAssert.Contains("\"belief_in_view_logp\":null", json, "a split with no samples should read as null, not NaN");
        StringAssert.Contains("\"last_seen_out_of_view_top5\":null", json);
    }
}
