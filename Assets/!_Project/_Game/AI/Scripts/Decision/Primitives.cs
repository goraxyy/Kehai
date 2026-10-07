using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Aiko
{
    public enum Status { Running, Success, Failure }

    // The leaves of a plan: things the body can actually do this frame (AIKO.md §6.4).
    // A primitive may carry a guard — a precondition that must stay true for the rest of
    // the plan to make sense. A violated guard aborts to the interrupt branch rather than
    // leaving the body frozen mid-plan.
    public abstract class Primitive
    {
        public string Label;
        public Func<AikoContext, bool> Guard;
        public string GuardName;
        bool started;

        public bool Started => started;

        public Status Run(AikoContext c, float dt)
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

        public void Abort(AikoContext c)
        {
            if (started) End(c, true);
        }

        public Primitive When(Func<AikoContext, bool> guard, string name)
        {
            Guard = guard;
            GuardName = name;
            return this;
        }

        protected virtual void Begin(AikoContext c) { }
        protected abstract Status Tick(AikoContext c, float dt);
        protected virtual void End(AikoContext c, bool aborted) { }

        public override string ToString() => Label ?? GetType().Name;
    }

    // ---- movement ----------------------------------------------------------------

    public sealed class MoveTo : Primitive
    {
        readonly Func<AikoContext, Vector3> target;
        readonly AikoBody.Pace pace;
        readonly float arrive;
        readonly float timeout;
        readonly bool follow;
        float elapsed, retarget;

        // `follow` re-reads the target every half second — for walking after belief.
        public MoveTo(Func<AikoContext, Vector3> target, AikoBody.Pace pace, string label,
                      float arrive = 1.3f, float timeout = 45f, bool follow = false)
        {
            this.target = target;
            this.pace = pace;
            this.arrive = arrive;
            this.timeout = timeout;
            this.follow = follow;
            Label = label;
        }

        public static MoveTo Point(Vector3 p, AikoBody.Pace pace, string label, float arrive = 1.3f) =>
            new MoveTo(_ => p, pace, label, arrive);

        protected override void Begin(AikoContext c) => c.Body.MoveTo(target(c), pace);

        protected override Status Tick(AikoContext c, float dt)
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

        protected override void Begin(AikoContext c) => c.Body.Stop();

        protected override Status Tick(AikoContext c, float dt)
        {
            t += dt;
            c.Body.SweepHead(Mathf.Sin(t / seconds * Mathf.PI * 2f) * 70f);
            return t >= seconds ? Status.Success : Status.Running;
        }

        protected override void End(AikoContext c, bool aborted) => c.Body.SweepHead(0f);
    }

    public sealed class Wait : Primitive
    {
        readonly float seconds;
        float t;
        public Wait(float seconds, string label = null) { this.seconds = seconds; Label = label ?? $"wait {seconds:0.#}s"; }
        protected override Status Tick(AikoContext c, float dt) { t += dt; return t >= seconds ? Status.Success : Status.Running; }
    }

    public sealed class WaitUntil : Primitive
    {
        readonly Func<AikoContext, bool> condition;
        readonly float max;
        readonly bool failOnTimeout;
        float t;

        public WaitUntil(Func<AikoContext, bool> condition, float max, string label, bool failOnTimeout = false)
        {
            this.condition = condition;
            this.max = max;
            this.failOnTimeout = failOnTimeout;
            Label = label;
        }

        protected override Status Tick(AikoContext c, float dt)
        {
            t += dt;
            if (condition(c)) return Status.Success;
            if (t >= max) return failOnTimeout ? Status.Failure : Status.Success;
            return Status.Running;
        }
    }

    public sealed class Face : Primitive
    {
        readonly Func<AikoContext, Vector3> target;
        float t;
        public Face(Func<AikoContext, Vector3> target, string label = "face") { this.target = target; Label = label; }
        protected override void Begin(AikoContext c) => c.Body.Stop();
        protected override Status Tick(AikoContext c, float dt)
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
        protected override Status Tick(AikoContext c, float dt) { c.Body.Silent = silent; return Status.Success; }
    }

    // Stand utterly still and quiet until the condition breaks or time runs out (ambush).
    public sealed class Hold : Primitive
    {
        readonly float max;
        readonly Func<AikoContext, bool> until;
        float t;

        public Hold(float max, Func<AikoContext, bool> until, string label)
        {
            this.max = max;
            this.until = until;
            Label = label;
        }

        protected override void Begin(AikoContext c)
        {
            c.Body.Stop();
            c.Body.Silent = true;
        }

        protected override Status Tick(AikoContext c, float dt)
        {
            t += dt;
            if (until != null && until(c)) return Status.Success;
            return t >= max ? Status.Success : Status.Running;
        }

        protected override void End(AikoContext c, bool aborted) => c.Body.Silent = false;
    }

    // ---- tells and effects ----------------------------------------------------------

    // The fairness contract's rule 3: every tactic announces itself at least minTellLead
    // seconds before it lands, through a sense the player still has. The brain logs the
    // TELL; the EFFECT that follows is timestamped too, so the gap can be asserted.
    public sealed class Tell : Primitive
    {
        readonly TellKind kind;
        readonly Func<AikoContext, Vector3> at;
        readonly float lead;
        float startedAt, required;

        public Tell(TellKind kind, Func<AikoContext, Vector3> at, float lead)
        {
            this.kind = kind;
            this.at = at;
            this.lead = lead;
            Label = $"tell:{kind}";
        }

        protected override void Begin(AikoContext c)
        {
            required = Mathf.Max(lead, c.Config.minTellLead);
            Vector3 where = at(c);
            c.World.PlayTell(kind, where, required);
            c.Brain.RecordTell(kind, where, required, ofPlan: true);
            startedAt = Time.time;
        }

        // Measured from when the tell began, so the frame it starts in doesn't count towards
        // the lead (at a coarse time step that frame alone is most of a tenth of a second).
        protected override Status Tick(AikoContext c, float dt) =>
            Time.time - startedAt >= required ? Status.Success : Status.Running;
    }

    public sealed class Effect : Primitive
    {
        readonly Action<AikoContext> act;

        public Effect(string label, Action<AikoContext> act)
        {
            Label = label;
            this.act = act;
        }

        protected override Status Tick(AikoContext c, float dt)
        {
            c.Brain.RecordEffect(Label);
            act(c);
            return Status.Success;
        }
    }

    // An effect that takes time — a shelf stripped four items at a time, a camera drilled in.
    public sealed class Process : Primitive
    {
        readonly Func<AikoContext, float, bool> step;   // return true when finished
        readonly float max;
        float t;
        bool logged;

        public Process(string label, Func<AikoContext, float, bool> step, float max = 30f)
        {
            Label = label;
            this.step = step;
            this.max = max;
        }

        protected override Status Tick(AikoContext c, float dt)
        {
            if (!logged) { c.Brain.RecordEffect(Label); logged = true; }
            t += dt;
            if (step(c, dt)) return Status.Success;
            return t >= max ? Status.Failure : Status.Running;
        }
    }

    public sealed class Speak : Primitive
    {
        readonly Func<AikoContext, string> line;
        readonly bool waitForIt;
        PaAnnouncement announcement;

        public Speak(Func<AikoContext, string> line, bool waitForIt = true, string label = "PA")
        {
            this.line = line;
            this.waitForIt = waitForIt;
            Label = label;
        }

        public static Speak Line(string text, bool wait = true) => new Speak(_ => text, wait, "PA: " + text);

        protected override void Begin(AikoContext c)
        {
            // The PA system chimes before it speaks; AikoBrain logs the chime as the tell and
            // the words as the effect when they actually play.
            string text = line(c);
            announcement = c.World.Pa.Announce(text);
            if (!announcement.Jammed) c.Brain.NotePlanTell();
        }

        protected override Status Tick(AikoContext c, float dt)
        {
            if (!waitForIt || announcement == null) return Status.Success;
            return announcement.Done ? Status.Success : Status.Running;
        }
    }

    // ---- pursuit ------------------------------------------------------------------

    // The chase. Runs at whatever she can currently see, falls back to belief when she
    // can't, and gives up once she has lost you for long enough.
    public sealed class Pursue : Primitive
    {
        readonly float max;
        float t, lostFor, retarget;

        public Pursue(float max = 25f)
        {
            this.max = max;
            Label = "pursue";
        }

        protected override void Begin(AikoContext c)
        {
            c.Body.SetMood(AikoBody.Mood.Hunt);
            c.Brain.SetChasing(true);
        }

        protected override Status Tick(AikoContext c, float dt)
        {
            t += dt;
            SightSensor sight = c.Body.Sight;
            if (sight.SeesNow) lostFor = 0f; else lostFor += dt;

            retarget -= dt;
            if (retarget <= 0f)
            {
                retarget = 0.25f;
                // Lead the target a little along the way it was last seen moving.
                Vector3 goal = sight.Time_SinceSeen() < 2f
                    ? sight.LastSeenPosition + Vector3.ClampMagnitude(sight.LastSeenVelocity * 0.5f, 3f)
                    : c.Belief.PeakPosition;
                c.Body.MoveTo(goal, AikoBody.Pace.Run);
            }

            if (lostFor > 5f) return Status.Failure;
            return t >= max ? Status.Success : Status.Running;
        }

        protected override void End(AikoContext c, bool aborted)
        {
            c.Brain.SetChasing(false);
            c.Body.SetMood(AikoBody.Mood.Calm);
        }
    }

    public static class SightExtensions
    {
        public static float Time_SinceSeen(this SightSensor s) => Time.time - s.LastSeenTime;
    }

    // ---- the behaviour tree ---------------------------------------------------------

    // A deliberately dumb behaviour tree (AIKO.md §6.5): a selector whose first branch is
    // the global interrupt check and whose second is the plan as a sequence. All the
    // intelligence is upstream; this only has to stop cleanly when told to.
    public abstract class BtNode
    {
        public abstract Status Tick(AikoContext c, float dt);
        public virtual void Abort(AikoContext c) { }
    }

    public sealed class BtCondition : BtNode
    {
        readonly Func<AikoContext, bool> test;
        public BtCondition(Func<AikoContext, bool> test) => this.test = test;
        public override Status Tick(AikoContext c, float dt) => test(c) ? Status.Success : Status.Failure;
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

        public override Status Tick(AikoContext c, float dt)
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

        public override void Abort(AikoContext c) => Current?.Abort(c);
    }

    // Selector(interrupt, plan). Returns Failure when interrupted, so the brain re-decides.
    public sealed class PlanTree : BtNode
    {
        readonly BtCondition interrupt;
        public readonly BtSequence Plan;
        public string Name;
        public GoalId Goal;
        public Tactic Tactic;
        public string Target;       // what it's aimed at, in words (a place, a shelf, "close distance")
        public bool Interrupted { get; private set; }

        public PlanTree(string name, GoalId goal, Tactic tactic, List<Primitive> steps, Func<AikoContext, bool> interrupt)
        {
            Name = name;
            Goal = goal;
            Tactic = tactic;
            Plan = new BtSequence(steps);
            this.interrupt = new BtCondition(interrupt);
        }

        public override Status Tick(AikoContext c, float dt)
        {
            if (interrupt.Tick(c, dt) == Status.Success)
            {
                Interrupted = true;
                Plan.Abort(c);
                return Status.Failure;
            }
            return Plan.Tick(c, dt);
        }

        public override void Abort(AikoContext c) => Plan.Abort(c);
    }
}
