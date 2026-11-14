using UnityEngine;

namespace Kehai.Eval
{
    // What the trained mind of a simulated player is paid for (0.4.0, build plan §7.3, C2.4). The
    // persona sets the weights, so one network learns every character.
    //
    //   + a job done, + clocking out, − shoppers left waiting      × diligence
    //   − being caught                                             × caution
    //   − fear × seconds                                           × what scares them
    //   − seconds sprinting                                        × (1 − pace)
    //   + a little for the usual route or spot                     × habit
    //   + a little for going to look at something odd, once        × curiosity
    //
    // The Survivor (C2.8) is paid only for clocking out and against being caught: it wins only by
    // clocking out, and a shift that times out is a loss.
    public struct EmployeeRewardWeights
    {
        public float Job, ClockOut, Waiting, Caught, Fear, Sprint, Habit, Curiosity, TimedOut;

        public static EmployeeRewardWeights For(Persona p)
        {
            if (p != null && p.IsSurvivor)
                return new EmployeeRewardWeights { ClockOut = 1f, Caught = -1f, TimedOut = -1f };
            float diligence = p != null ? p[Trait.Diligence] : 0.5f;
            float caution = p != null ? p[Trait.Caution] : 0.5f;
            float pace = p != null ? p[Trait.Pace] : 0.5f;
            float scared = p != null
                ? (p[Trait.FearOfDark] + p[Trait.FearOfSounds] + p[Trait.FearOfHer] + p[Trait.FearOfDashes] + p[Trait.FearOfChase]) / 5f
                : 0.5f;
            return new EmployeeRewardWeights
            {
                Job = 0.05f * (0.3f + diligence),
                ClockOut = 0.5f * (0.5f + diligence),
                Waiting = -0.03f * diligence,
                Caught = -(0.3f + 0.7f * caution),
                Fear = -0.004f * scared,
                Sprint = -0.003f * (1f - pace),
                Habit = 0.01f * (p != null ? p[Trait.Habit] : 0.5f),
                Curiosity = 0.02f * (p != null ? p[Trait.Curiosity] : 0.5f),
                TimedOut = -0.2f
            };
        }
    }

    // What happened since the last decision: counted by the player, paid by the weights.
    public struct EmployeeOutcome
    {
        public int Jobs, CustomersLost;
        public float FearSeconds, SprintSeconds;
        public bool UsedHabit, LookedOnce;
        public bool ClockedOut, Caught, TimedOut;
    }

    public static class EmployeeReward
    {
        public static float Pay(in EmployeeRewardWeights w, in EmployeeOutcome o)
        {
            float r = w.Job * o.Jobs + w.Waiting * o.CustomersLost + w.Fear * o.FearSeconds + w.Sprint * o.SprintSeconds;
            if (o.UsedHabit) r += w.Habit;
            if (o.LookedOnce) r += w.Curiosity;
            if (o.ClockedOut) r += w.ClockOut;
            if (o.Caught) r += w.Caught;
            if (o.TimedOut) r += w.TimedOut;
            return float.IsNaN(r) || float.IsInfinity(r) ? 0f : r;
        }

        // The largest a shift can pay or cost per decision, for the bounds test: one decision's
        // window is at most a whole 300 s shift.
        public static float Bound(in EmployeeRewardWeights w, int jobs = 40, int lost = 20, float seconds = 300f) =>
            Mathf.Abs(w.Job) * jobs + Mathf.Abs(w.Waiting) * lost + (Mathf.Abs(w.Fear) + Mathf.Abs(w.Sprint)) * seconds
            + Mathf.Abs(w.Habit) + Mathf.Abs(w.Curiosity) + Mathf.Abs(w.ClockOut) + Mathf.Abs(w.Caught) + Mathf.Abs(w.TimedOut);
    }
}
