using System;
using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // Observes one forced plan. A plan's generic EFFECT message precedes the attempted
    // mutation, which may refuse; it is never sufficient evidence of a landing.
    sealed class TrickCheckEvidence : IDisposable
    {
        readonly KehaiEnv env;
        readonly KarenBrain brain;
        readonly SimulatedPlayer bot;
        readonly PlanTree plan;
        readonly Tactic tactic;
        readonly TrickCheck.Row row;
        readonly TrickCheckCounter repair;
        readonly int uses, landedBefore;
        readonly float forceTime;
        int answersFrom, turns;
        string effectStep, target;
        bool actionSeen;
        Dictionary<string, object> previousResult;
        Vector3? cut;
        float cutUntil;
        Projectile projectile;
        int projectileNumber;
        bool inFlight, missed;
        float throwBy;
        int chimes;

        public TrickCheckEvidence(KehaiEnv env, KarenBrain brain, SimulatedPlayer bot, PlanTree plan, TrickCheck.Row row)
        {
            this.env = env;
            this.brain = brain;
            this.bot = bot;
            this.plan = plan;
            this.row = row;
            tactic = plan.Tactic;
            uses = tactic.UsesThisShift;
            brain.Stats.TacticLanded.TryGetValue(tactic.Id, out landedBefore);
            forceTime = Time.time;
            repair = TrickCheckCounter.For(tactic.Id);
            previousResult = env.LastResult;
            switch (tactic.Id)
            {
                case "ambush": row.expected = "Karen waits silently at the chosen spot and the bot turns back"; break;
                case "cut": row.expected = "Karen reaches her predicted cut in its window and the bot turns back"; break;
                case "camera_watch": row.expected = "The bot stays unseen during a full watch at the screens"; break;
                case "phantom_chime": row.expected = "The chosen doors chime and their cycle ends without a repair"; break;
                case "poll_witnesses": row.expected = "A fresh report from the target shopper at talking distance"; break;
                default:
                    if (tactic is ThrowTactic) row.expected = "The matching projectile lands after the bot sidesteps it";
                    break;
            }
            brain.Log.Written += OnThought;
            CutTactic.Aimed += OnCut;
            ThrowEvents.Thrown += OnThrown;
            ThrowEvents.Landed += OnLanded;
            NoiseBus.Emitted += OnNoise;
        }

        public void Dispose()
        {
            brain.Log.Written -= OnThought;
            CutTactic.Aimed -= OnCut;
            ThrowEvents.Thrown -= OnThrown;
            ThrowEvents.Landed -= OnLanded;
            NoiseBus.Emitted -= OnNoise;
        }

        bool OwnPlan => brain.CurrentPlan == plan && brain.ForcedPlanRunning;
        public bool Complete => row.answered >= 0;
        public bool WaitingForOutcome => repair != null && row.landed >= 0 ||
            tactic is ThrowTactic && (inFlight || missed && Time.time < throwBy) ||
            tactic is PhantomChimeTactic && effectStep != null && tactic.EffectRunning(brain.Ctx);
        public string Missing => row.landed < 0 ? "effect from this forced plan" :
            repair != null ? $"successful {repair.Verb} on {target ?? "the affected fixture"}" : row.expected;

        float Stamp => ShiftRecorder.Instance != null && ShiftRecorder.Instance.Recording
            ? ShiftRecorder.Instance.ShiftTime : brain.ShiftTime;

        void Land(string what)
        {
            if (row.landed >= 0) return;
            row.landed = Stamp;
            row.effect = what;
            turns = bot.TurnBacks;
        }

        void Answer(string what)
        {
            row.answered = Stamp;
            row.counter = what;
        }

        void OnThought(ThoughtRecord r)
        {
            if (r.Kind != "EFFECT" || !OwnPlan || !brain.Stats.TacticLanded.TryGetValue(tactic.Id, out int count) ||
                count <= landedBefore) return;
            // RecordEffect logs the current primitive's own label. PA speech can write an
            // unrelated EFFECT while this plan is walking or waiting.
            var step = plan.Plan.Current;
            if (step == null || !r.Text.EndsWith(step.Label, StringComparison.Ordinal) &&
                !(step is FlashSaleTactic.SaleAnnouncement || step is CameraWatchTactic.WatchCameras)) return;
            if (tactic is ShelfSweepTactic && step.Label != "mark sabotage") return;
            effectStep = step.Label;
        }

        void OnCut(Vector3 spot, float until)
        {
            if (OwnPlan && tactic is CutTactic) { cut = spot; cutUntil = until; }
        }

        void OnThrown(ThrowEvent e)
        {
            if (!OwnPlan || tactic.Id != "throw_" + e.Item.ToString().ToLowerInvariant()) return;
            projectile = e.Projectile;
            projectileNumber = projectile.Number;
            inFlight = true;
        }

        void OnLanded(ThrowEvent e)
        {
            if (!inFlight || !ReferenceEquals(e.Projectile, projectile) || !brain.ShiftActive || env.Done) return;
            inFlight = false;
            missed = !e.Hit;
            throwBy = Time.time + 2f; // the bot may still be finishing its sidestep
            Land($"{e.Item} #{projectileNumber} landed at {e.At} ({(e.Hit ? "hit the bot" : "missed the bot")})");
            if (e.Hit) row.failure = "Projectile hit the bot";
        }

        void OnNoise(NoiseEvent e)
        {
            if (!(tactic is PhantomChimeTactic phantom) || effectStep == null || tactic.UsesThisShift != uses ||
                !brain.ShiftActive || !tactic.EffectRunning(brain.Ctx) || phantom.LastDoor == null ||
                brain.Ctx.World.PhantomDoor != phantom.LastDoor || e.Author != NoiseAuthor.Karen ||
                e.Kind != NoiseKind.AutoDoor || e.Position != phantom.LastDoor.transform.position) return;
            chimes++;
            Land("Karen cycled the selected doors at " + plan.Target);
        }

        public void Sample()
        {
            if (Complete || row.failure != null) return;
            if (tactic.UsesThisShift != uses) { row.failure = "Another use of the same tactic started"; return; }
            if (repair != null) { ObserveRepair(); return; }

            switch (tactic)
            {
                case ThrowTactic _:
                    if (missed && projectileNumber == bot.LastDodgedProjectile)
                        Answer("Completed sidestep for projectile #" + projectileNumber);
                    break;
                case PhantomChimeTactic _:
                    if (chimes > 0 && !tactic.EffectRunning(brain.Ctx))
                        Answer($"{chimes} door chime(s), cycle finished; no player repair");
                    break;
                case CameraWatchTactic watch:
                    if (OwnPlan && effectStep != null && watch.EffectRunning(brain.Ctx)) Land("Watching the live cameras");
                    // Index 3 is the retreat after WatchCameras succeeded. An abort at the desk cannot count.
                    if (row.landed >= 0 && plan.Plan.Index >= 3)
                    {
                        if (watch.LastSightings == 0) Answer("Zero sightings during the completed watch");
                        else row.failure = $"Cameras reported the bot {watch.LastSightings} time(s)";
                    }
                    break;
                case PollWitnessesTactic _:
                    if (!OwnPlan) break;
                    foreach (var report in brain.Sensorium.Positive)
                        if (report.Channel == SenseChannel.Testimony && report.Timestamp > forceTime &&
                            report.Label.StartsWith("testimony(" + plan.Target + ",", StringComparison.Ordinal) &&
                            CustomerMemory.All.Count(w => w != null && !w.possessed && w.name == plan.Target &&
                                w.LastSeenAt == report.Position && Vector3.Distance(w.transform.position, brain.Body.Position) <= 2.2f) == 1)
                        {
                            Land(report.Label);
                            Answer("Shopper gave testimony; no player repair");
                            break;
                        }
                    break;
                case AmbushTactic _:
                    if (OwnPlan && brain.Body.Silent && plan.Plan.Current is Hold hold && hold.Started)
                        WatchTurnBack("Holding the ambush at " + plan.Target);
                    break;
                case CutTactic _:
                    if (OwnPlan && cut.HasValue && Time.time <= cutUntil && FairnessGuard.Flat(brain.Body.Position, cut.Value) < 1.5f)
                        WatchTurnBack("Reached the cut at " + cut.Value);
                    break;
                default:
                    row.failure = "No outcome check for this live tactic";
                    break;
            }
        }

        void WatchTurnBack(string effect)
        {
            Land(effect);
            if (bot.TurnBacks > turns) Answer("Bot turned back with Karen on its route");
        }

        void ObserveRepair()
        {
            bool running = tactic.EffectRunning(brain.Ctx);
            if (row.landed < 0)
            {
                if (effectStep == null || !running || !tactic.FixAt(brain.Ctx, out Vector3 at)) return;
                target = TrickCheckCounter.Target(tactic, env.World, at);
                Land(effectStep + " at " + brain.Map.NameAt(at) + " (" + (target ?? "unresolved fixture") + ")");
                previousResult = env.LastResult;
                answersFrom = brain.Stats.Answers.Count;
                if (target == null) row.failure = "No unambiguous repair fixture";
                return;
            }

            var result = env.LastResult;
            if (!ReferenceEquals(result, previousResult))
            {
                previousResult = result;
                if (!running && repair.Matches(result, target)) actionSeen = true;
            }
            if (running || !actionSeen) return;
            if (repair.Answer != null && !brain.Stats.Answers.Skip(answersFrom).Any(a => a.kind == repair.Answer)) return;
            Answer($"{repair.Verb} {target}: {repair.Message}");
        }
    }
}
