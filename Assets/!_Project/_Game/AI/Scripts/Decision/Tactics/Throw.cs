using UnityEngine;

namespace Kehai.Karen
{
    // Throw (0.2.0, a trick of Hunt's): while she sees you, she throws a product from the
    // shelves at where you'll be when it lands. Walls stop it and you can sidestep it. Each item
    // is its own choice for the learner (ThrownItems has their effects and ranges). Once a hunt;
    // a hunt's own cost is the Hunt action's, this is on top.
    public abstract class ThrowTactic : Tactic
    {
        protected abstract ThrownItem Item { get; }
        public override ActionId[] Actions => new[] { ActionId.Hunt };
        public override int Tier => 2;
        public override int IntroducedShift => 3;
        public override float TensionCost => 0.2f;
        public override float Cooldown => 30f;
        public override TellKind Tell => TellKind.Clunk;
        public override string Attacks => "you";

        public override bool Available(KarenContext c, out string why)
        {
            why = null;
            if (c.Brain != null && c.Brain.ThrewThisHunt) { why = "one throw a hunt"; return false; }
            SightSensor s = c.Body.Sight;
            if (!s.SeesNow) { why = "she can't see you to throw"; return false; }
            Vector3 d = s.LastSeenPosition - c.Body.Position;
            d.y = 0f;
            float min = ThrownItems.MinRange(Item), max = ThrownItems.MaxRange(Item);
            if (d.magnitude < min || d.magnitude > max)
            {
                why = $"you're {d.magnitude:0} m away; {Item.ToString().ToLowerInvariant()} is thrown from {min:0}–{max:0} m";
                return false;
            }
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            plan.Target = Item.ToString().ToLowerInvariant() + " at you";
            plan.Add(new ThrowStep(Item));
        }
    }

    public sealed class ThrowFlourTactic : ThrowTactic
    {
        protected override ThrownItem Item => ThrownItem.Flour;
        public override string Id => "throw_flour";
        public override string Title => "Throw: flour";
        public override float PanicPrior => 0.2f;
        public override string Chore => "keep moving while you can't see: it clears in four seconds";
    }

    public sealed class ThrowOilTactic : ThrowTactic
    {
        protected override ThrownItem Item => ThrownItem.Oil;
        public override string Id => "throw_oil";
        public override string Title => "Throw: oil";
        public override float PanicPrior => 0.25f;
        public override string Chore => "get off the slick: you're slower on it for two seconds";
    }

    public sealed class ThrowTinTactic : ThrowTactic
    {
        protected override ThrownItem Item => ThrownItem.Tin;
        public override string Id => "throw_tin";
        public override string Title => "Throw: a tin";
        public override float PanicPrior => 0.3f;
        public override string Chore => "shake it off and run: it's one second, and she threw from far";
    }

    // The throw: a short wind-up facing you, then the item leaves her hand on an arc aimed at
    // where she expects you when it lands — your last-seen position and the way you were going.
    public sealed class ThrowStep : Primitive
    {
        const float WindUp = 0.3f;
        readonly ThrownItem item;
        float t;

        public ThrowStep(ThrownItem item)
        {
            this.item = item;
            Label = "throw " + item.ToString().ToLowerInvariant();
        }

        protected override void Begin(KarenContext c)
        {
            c.Body.Stop();
            c.Body.TurnToward(c.Body.Sight.LastSeenPosition);
            if (c.Brain != null) c.Brain.ThrewThisHunt = true;
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            if (t < WindUp) return Status.Running;

            SightSensor s = c.Body.Sight;
            Vector3 from = c.Body.Position + Vector3.up * 1.6f + c.Body.Forward * 0.4f;
            Vector3 flat = s.LastSeenPosition - c.Body.Position;
            flat.y = 0f;
            float flight = 0.25f + flat.magnitude / 16f;
            Vector3 lead = s.LastSeenVelocity;
            lead.y = 0f;
            Vector3 aim = s.LastSeenPosition + Vector3.ClampMagnitude(lead, 7f) * flight + Vector3.up * 1f;   // chest height
            Projectile.Launch(item, from, aim, flight, c.Body.transform);
            c.Brain?.RecordEffect($"throw {item.ToString().ToLowerInvariant()} at you, {flat.magnitude:0} m");
            return Status.Success;
        }
    }
}
