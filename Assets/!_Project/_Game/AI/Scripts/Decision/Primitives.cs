using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    public enum Status { Running, Success, Failure }

    // The leaves of a plan: things the body can actually do this frame (Karen.md §6.4).
    // A primitive may carry a guard — a precondition that must stay true for the rest of
    // the plan to make sense. A violated guard aborts to the interrupt branch rather than
    // leaving the body frozen mid-plan.
    public abstract class Primitive
    {
        public string Label;
        public Func<KarenContext, bool> Guard;
        public string GuardName;
        bool started;

        public bool Started => started;

        public Status Run(KarenContext c, float dt)
        {
            if (!started)
            {
                started = true;
                Begin(c);
            }
            Status s = Tick(c, dt);
            if (s != Status.Running) End(c, false);
            return s;
        }

        public void Abort(KarenContext c)
        {
            if (started) End(c, true);
        }

        public Primitive When(Func<KarenContext, bool> guard, string name)
        {
            Guard = guard;
            GuardName = name;
            return this;
        }

        protected virtual void Begin(KarenContext c) { }
        protected abstract Status Tick(KarenContext c, float dt);
        protected virtual void End(KarenContext c, bool aborted) { }

        public override string ToString() => Label ?? GetType().Name;
    }

    // ---- movement ----------------------------------------------------------------

    public sealed class MoveTo : Primitive
    {
        readonly Func<KarenContext, Vector3> target;
        readonly KarenBody.Pace pace;
        readonly float arrive;
        readonly float timeout;
        readonly bool follow;
        float elapsed, retarget;

        // `follow` re-reads the target every half second — for walking after belief.
        public MoveTo(Func<KarenContext, Vector3> target, KarenBody.Pace pace, string label,
                      float arrive = 1.3f, float timeout = 45f, bool follow = false)
        {
            this.target = target;
            this.pace = pace;
            this.arrive = arrive;
            this.timeout = timeout;
            this.follow = follow;
            Label = label;
        }

        public static MoveTo Point(Vector3 p, KarenBody.Pace pace, string label, float arrive = 1.3f) =>
            new MoveTo(_ => p, pace, label, arrive);

        protected override void Begin(KarenContext c) => c.Body.MoveTo(target(c), pace);

        protected override Status Tick(KarenContext c, float dt)
        {
            elapsed += dt;
            if (elapsed > timeout) return Status.Failure;

            if (follow)
            {
                retarget -= dt;
                if (retarget <= 0f)
                {
                    retarget = 0.5f;
                    c.Body.MoveTo(target(c), pace);
                }
            }

            if (c.Body.PathFailed) return Status.Failure;
            return c.Body.Arrived(arrive) ? Status.Success : Status.Running;
        }
    }

    // Stand and sweep the head left and right — the cone passes over cells that turn out
    // empty, which is where negative information comes from.
    public sealed class LookAround : Primitive
    {
        readonly float seconds;
        float t;

        public LookAround(float seconds, string label = "look around")
        {
            this.seconds = seconds;
            Label = label;
        }

        protected override void Begin(KarenContext c) => c.Body.Stop();

        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            c.Body.SweepHead(Mathf.Sin(t / seconds * Mathf.PI * 2f) * 70f);
            return t >= seconds ? Status.Success : Status.Running;
        }

        protected override void End(KarenContext c, bool aborted) => c.Body.SweepHead(0f);
    }

    public sealed class Wait : Primitive
    {
        readonly float seconds;
        float t;
        public Wait(float seconds, string label = null) { this.seconds = seconds; Label = label ?? $"wait {seconds:0.#}s"; }
        protected override Status Tick(KarenContext c, float dt) { t += dt; return t >= seconds ? Status.Success : Status.Running; }
    }

    public sealed class WaitUntil : Primitive
    {
        readonly Func<KarenContext, bool> condition;
        readonly float max;
        readonly bool failOnTimeout;
        float t;

        public WaitUntil(Func<KarenContext, bool> condition, float max, string label, bool failOnTimeout = false)
        {
            this.condition = condition;
            this.max = max;
            this.failOnTimeout = failOnTimeout;
            Label = label;
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            if (condition(c)) return Status.Success;
            if (t >= max) return failOnTimeout ? Status.Failure : Status.Success;
            return Status.Running;
        }
    }

    public sealed class Face : Primitive
    {
        readonly Func<KarenContext, Vector3> target;
        float t;
        public Face(Func<KarenContext, Vector3> target, string label = "face") { this.target = target; Label = label; }
        protected override void Begin(KarenContext c) => c.Body.Stop();
        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            c.Body.TurnToward(target(c));
            return t > 0.6f ? Status.Success : Status.Running;
        }
    }

    // ---- going quiet --------------------------------------------------------------

    public sealed class SetSilent : Primitive
    {
        readonly bool silent;
        public SetSilent(bool silent) { this.silent = silent; Label = silent ? "go silent" : "footsteps back"; }
        protected override Status Tick(KarenContext c, float dt) { c.Body.Silent = silent; return Status.Success; }
    }

    // Stand utterly still and quiet until the condition breaks or time runs out (ambush).
    public sealed class Hold : Primitive
    {
        readonly float max;
        readonly Func<KarenContext, bool> until;
        float t;

        public Hold(float max, Func<KarenContext, bool> until, string label)
        {
            this.max = max;
            this.until = until;
            Label = label;
        }

        protected override void Begin(KarenContext c)
        {
            c.Body.Stop();
            c.Body.Silent = true;
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            if (until != null && until(c)) return Status.Success;
            return t >= max ? Status.Success : Status.Running;
        }

        protected override void End(KarenContext c, bool aborted) => c.Body.Silent = false;
    }

    // ---- tells and effects ----------------------------------------------------------

    // A trick's sound, played as it acts. Until 0.2.0 this was the fairness contract's rule 3:
    // every trick warned you at least 0.8 s before it landed, and the plan waited here for
    // that long. The rule is gone (build plan §7.8); the sound stays, as atmosphere, and the
    // plan goes straight on. `lead` is how long the sound plays. The brain still logs it.
    public sealed class Tell : Primitive
    {
        readonly TellKind kind;
        readonly Func<KarenContext, Vector3> at;
        readonly float lead;

        public Tell(TellKind kind, Func<KarenContext, Vector3> at, float lead)
        {
            this.kind = kind;
            this.at = at;
            this.lead = lead;
            Label = $"tell:{kind}";
        }

        protected override void Begin(KarenContext c)
        {
            Vector3 where = at(c);
            c.World.PlayTell(kind, where, lead);
            c.Brain.RecordTell(kind, where, lead);
        }

        protected override Status Tick(KarenContext c, float dt) => Status.Success;
    }

    public sealed class Effect : Primitive
    {
        readonly Action<KarenContext> act;

        public Effect(string label, Action<KarenContext> act)
        {
            Label = label;
            this.act = act;
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            c.Brain.RecordEffect(Label);
            act(c);
            return Status.Success;
        }
    }

    // An effect that takes time — a shelf stripped four items at a time, a camera drilled in.
    public sealed class Process : Primitive
    {
        readonly Func<KarenContext, float, bool> step;   // return true when finished
        readonly float max;
        float t;
        bool logged;

        public Process(string label, Func<KarenContext, float, bool> step, float max = 30f)
        {
            Label = label;
            this.step = step;
            this.max = max;
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            if (!logged) { c.Brain.RecordEffect(Label); logged = true; }
            t += dt;
            if (step(c, dt)) return Status.Success;
            return t >= max ? Status.Failure : Status.Running;
        }
    }

    public sealed class Speak : Primitive
    {
        readonly Func<KarenContext, string> line;
        readonly bool waitForIt;
        PaAnnouncement announcement;

        public Speak(Func<KarenContext, string> line, bool waitForIt = true, string label = "PA")
        {
            this.line = line;
            this.waitForIt = waitForIt;
            Label = label;
        }

        public static Speak Line(string text, bool wait = true) => new Speak(_ => text, wait, "PA: " + text);

        protected override void Begin(KarenContext c)
        {
            // The PA system chimes before it speaks; KarenBrain logs the chime as the tell and
            // the words as the effect when they actually play.
            string text = line(c);
            announcement = c.World.Pa.Announce(text);
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            if (!waitForIt || announcement == null) return Status.Success;
            return announcement.Done ? Status.Success : Status.Running;
        }
    }

    // ---- pursuit ------------------------------------------------------------------

    // Hunt's run (0.2.0): at you while she sees you; when she loses you, on to where her
    // reckoning says you'll be (TacticHelpers.Forecast), not where you were. It hands over —
    // to Search, as the brain decides next — when she gets there and you're not, after
    // huntMemorySeconds with nothing new, in a recovery, or when her belief puts you outside,
    // where she can't follow (the run stops at the pavement's edge).
    public sealed class Pursue : Primitive
    {
        readonly float max;
        float t, lostFor, retarget;
        Vector3 goal;

        public Pursue(float max = 25f)
        {
            this.max = max;
            Label = "pursue";
        }

        protected override void Begin(KarenContext c)
        {
            c.Body.SetMood(KarenBody.Mood.Hunt);
            c.Brain.SetChasing(true);
            goal = c.Body.Sight.LastSeenPosition;
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            SightSensor sight = c.Body.Sight;
            bool sees = sight.SeesNow;
            if (sees) lostFor = 0f; else lostFor += dt;

            retarget -= dt;
            if (retarget <= 0f)
            {
                retarget = 0.25f;
                // Seen: lead you a little along the way you were moving. Lost: on to where
                // you'd be two seconds from now, by her reckoning.
                goal = sees || sight.Time_SinceSeen() < 0.5f
                    ? sight.LastSeenPosition + Vector3.ClampMagnitude(sight.LastSeenVelocity * 0.5f, 3f)
                    : TacticHelpers.Forecast(c, 2f);
                c.Body.MoveTo(goal, KarenBody.Pace.Run);
            }

            Vector3 d = c.Body.Position - goal;
            d.y = 0f;
            if (HandOver(sees, lostFor, d.magnitude, c.Belief.Staleness, c.Director.InRecovery, c.Belief.OutsideMass,
                         c.Config.huntMemorySeconds, out string why))
            {
                c.Think("HUNT", "HUNT     " + why);
                return Status.Failure;
            }
            return t >= max ? Status.Success : Status.Running;
        }

        // When a hunt gives up, and why. Pure, so the rules can be tested.
        public static bool HandOver(bool sees, float lostFor, float toGoal, float staleness, bool inRecovery,
                                    float outsideMass, float memory, out string why)
        {
            why = null;
            if (outsideMass > 0.5f) why = "you went outside: she can't follow you there";
            else if (inRecovery) why = "a recovery: she lets you go";
            else if (!sees && lostFor > 0.5f && toGoal < 1.5f) why = "got to where you'd be, and you weren't there";
            else if (!sees && lostFor > memory && staleness > memory) why = $"nothing new for {memory:0}s";
            return why != null;
        }

        protected override void End(KarenContext c, bool aborted)
        {
            c.Brain.SetChasing(false);
            c.Body.SetMood(KarenBody.Mood.Calm);
        }
    }

    public static class SightExtensions
    {
        public static float Time_SinceSeen(this SightSensor s) => Time.time - s.LastSeenTime;
    }

    // ---- the behaviour tree ---------------------------------------------------------

    // A deliberately dumb behaviour tree (Karen.md §6.5): a selector whose first branch is
    // the global interrupt check and whose second is the plan as a sequence. All the
    // intelligence is upstream; this only has to stop cleanly when told to.
    public abstract class BtNode
    {
        public abstract Status Tick(KarenContext c, float dt);
        public virtual void Abort(KarenContext c) { }
    }

    public sealed class BtCondition : BtNode
    {
        readonly Func<KarenContext, bool> test;
        public BtCondition(Func<KarenContext, bool> test) => this.test = test;
        public override Status Tick(KarenContext c, float dt) => test(c) ? Status.Success : Status.Failure;
    }

    public sealed class BtSequence : BtNode
    {
        readonly List<Primitive> steps;
        int index;

        public BtSequence(List<Primitive> steps) => this.steps = steps;
        public Primitive Current => index < steps.Count ? steps[index] : null;
        public int Index => index;
        public int Count => steps.Count;
        public string ViolatedGuard { get; private set; }

        public override Status Tick(KarenContext c, float dt)
        {
            while (index < steps.Count)
            {
                Primitive step = steps[index];
                if (step.Guard != null && !step.Guard(c))
                {
                    ViolatedGuard = step.GuardName ?? step.Label;
                    step.Abort(c);
                    return Status.Failure;
                }

                Status s = step.Run(c, dt);
                if (s == Status.Running) return Status.Running;
                if (s == Status.Failure) return Status.Failure;
                index++;
                dt = 0f;   // chained instant steps run in the same frame with no extra time
            }
            return Status.Success;
        }

        public override void Abort(KarenContext c) => Current?.Abort(c);
    }

    // Selector(interrupt, plan). Returns Failure when interrupted, so the brain re-decides.
    public sealed class PlanTree : BtNode
    {
        readonly BtCondition interrupt;
        public readonly BtSequence Plan;
        public string Name;
        public ActionId Action;
        public Tactic Tactic;
        public string Target;       // what it's aimed at, in words (a place, a shelf, "close distance")
        public bool Interrupted { get; private set; }

        public PlanTree(string name, ActionId goal, Tactic tactic, List<Primitive> steps, Func<KarenContext, bool> interrupt)
        {
            Name = name;
            Action = goal;
            Tactic = tactic;
            Plan = new BtSequence(steps);
            this.interrupt = new BtCondition(interrupt);
        }

        public override Status Tick(KarenContext c, float dt)
        {
            if (interrupt.Tick(c, dt) == Status.Success)
            {
                Interrupted = true;
                Plan.Abort(c);
                return Status.Failure;
            }
            return Plan.Tick(c, dt);
        }

        public override void Abort(KarenContext c) => Plan.Abort(c);
    }
}
