using Kehai.Karen;
using Kehai.Karen.TestBrains;
using NUnit.Framework;

// C5.7 (build plan §5.5, item 5): on 1,000 situations the C# network and the trainer's own model
// give the same chances, to 4 decimal places. The fixture is a small ML-Agents actor with random
// weights, exported by tools/learn/export_brain.py (tools/learn/make_brain_fixture.py).
public class BrainNetParityTests
{
    [Test]
    public void TheCSharpNetwork_MatchesTheTrainer_To4Decimals()
    {
        BrainNet net = TinyBrain.Build();
        int n = TinyBrainParity.X.Length / TinyBrainParity.Inputs;
        Assert.AreEqual(1000, n);
        double worst = 0.0;
        var x = new float[TinyBrainParity.Inputs];
        for (int i = 0; i < n; i++)
        {
            System.Array.Copy(TinyBrainParity.X, i * x.Length, x, 0, x.Length);
            float[][] logits = net.Logits(x);
            float[] p0 = BrainNet.Softmax(logits[0], null), p1 = BrainNet.Softmax(logits[1], null);
            for (int k = 0; k < p0.Length; k++) worst = System.Math.Max(worst, System.Math.Abs(p0[k] - TinyBrainParity.P0[i * p0.Length + k]));
            for (int k = 0; k < p1.Length; k++) worst = System.Math.Max(worst, System.Math.Abs(p1[k] - TinyBrainParity.P1[i * p1.Length + k]));
        }
        Assert.Less(worst, 5e-5, "largest difference in a chance");
    }

    [Test]
    public void ACardNamesItsOutputs_AndAWrongSizeIsRefused()
    {
        BrainNet net = TinyBrain.Build();
        Assert.AreEqual(6, net.InputSize);
        Assert.AreEqual(2, net.Heads);
        Assert.AreEqual("a3", net.Card.OutputNames[0][3]);
        Assert.IsFalse(net.Card.Trained);
        Assert.Throws<System.ArgumentException>(() => net.Logits(new float[5]));
    }

    [Test]
    public void Softmax_ClosedOutputsGetNothing()
    {
        float[] p = BrainNet.Softmax(new[] { 1f, 5f, 2f }, new[] { true, false, true });
        Assert.AreEqual(0f, p[1]);
        Assert.AreEqual(1f, p[0] + p[2], 1e-6f);
        Assert.Greater(p[2], p[0]);
    }
}
