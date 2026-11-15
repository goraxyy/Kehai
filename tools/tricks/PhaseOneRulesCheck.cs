using System;
using Kehai.Karen;

static class PhaseOneRulesCheck
{
    static int checks;
    static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }
    static void Main()
    {
        foreach (bool open in new[] { false, true })
        foreach (bool powered in new[] { false, true })
        {
            var sale = new SaleRules();
            Check(sale.Start(open, powered) == (open && powered), "sale start requires open doors and PA power");
        }
        var rules = new SaleRules();
        for (int cap = -12; cap <= 12; cap++)
            Check(rules.Cap(cap) == Math.Clamp(cap, 0, 5), "normal configured cap retains the absolute ceiling");
        Check(rules.Cap(SaleRules.NormalCap) == 2, "normal shift cap");
        Check(rules.Start(true, true), "first sale starts");
        for (int cap = -12; cap <= 12; cap++) Check(rules.Cap(cap) == 4, "sale cap is exactly four");
        Check(!rules.Start(true, true), "duplicate sale cannot start");
        Check(!rules.Start(true, false) && rules.Active, "PA off refuses another sale and preserves the running one");
        Check(!rules.Start(false, true) && rules.Active, "failed start does not replace a running sale");
        Check(rules.End() && !rules.Active && rules.Cap(2) == 2, "end/reset restores normal cap");
        Check(!rules.End(), "ending twice emits no second transition");
        Check(!rules.Start(false, true), "closed doors cannot restart sale");
        Check(SaleRules.HoldSeconds == 20f, "mic hold duration");

        var walking = new ComplaintPatience();
        Check(!walking.Step(119f, false), "customer has time to reach till");
        Check(walking.Step(1f, false), "unreachable till times out rather than blocking shift");
        var waiting = new ComplaintPatience();
        waiting.Step(110f, false);
        waiting.Arrive();
        Check(waiting.AtTill && waiting.Remaining == 90f, "arrival starts fresh patience");
        Check(!waiting.Step(20f, false), "the full twenty-second response fits comfortably");
        waiting.Arrive();
        Check(waiting.Remaining == 70f, "repeated arrival cannot extend patience");
        Check(!waiting.Step(1000000f, true) && waiting.Remaining == 70f, "pause/power failure freezes customer timers");
        Check(!waiting.Step(float.NaN, false) && waiting.Remaining == 70f, "NaN cannot poison timeout");
        Check(!waiting.Step(float.PositiveInfinity, false), "infinite dt cannot lose customer");
        Check(!waiting.Step(-100f, false) && waiting.Remaining == 70f, "negative time cannot extend patience");
        Check(!waiting.Step(69f, false), "customer waits until deadline");
        Check(waiting.Step(1f, false), "ignored complaint eventually leaves");
        Console.WriteLine($"PASS: {checks} phase-one policy checks (actual C# runtime sources; no Unity).");
    }
}
