using UnityEngine;

namespace Kehai.Karen
{
    public sealed class FlashSaleTactic : Tactic
    {
        public override string Id => "flash_sale";
        public override string Title => "Flash sale";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Flush };
        public override int Tier => 2;
        public override int IntroducedShift => 2;
        public override float TensionCost => .15f;
        public override float Cooldown => 240f;
        public override float PanicPrior => .1f;
        public override TellKind Tell => TellKind.PaChime;
        public override bool LeavesChore => true;
        public override string Chore => "hold the PA microphone in the staff room for 20 s to call off the sale";
        public override string Attacks => "work,social";
        public override bool Available(KarenContext c, out string why)
        {
            why = c.Shift == null || !c.Shift.IsShiftActive || FairnessGuard.DoorsClosed(c) ? FairnessGuard.ClosedWhy :
                c.World == null || c.World.Pa == null || !c.World.Pa.Powered ? "the PA is switched off" :
                ClosingMic.Instance == null ? "no PA microphone" : FlashSaleState.Active ? "a sale is already running" : null;
            return why == null;
        }
        public override bool EffectRunning(KarenContext c) => FlashSaleState.Active;
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = ClosingMic.Instance != null ? ClosingMic.Instance.transform.position : default;
            return FlashSaleState.Active && ClosingMic.Instance != null;
        }
        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            plan.Target = "the PA microphone, staff room";
            plan.Add(new SaleAnnouncement());
        }
        // Apply only when the actual speech starts, after the real chime. Queuing, a jam,
        // an aborted plan or closing during the chime must never silently start a sale.
        public sealed class SaleAnnouncement : Primitive
        {
            PaSystem pa;
            PaAnnouncement announcement;
            bool landed;
            public SaleAnnouncement() { Label = "announce the flash sale"; }
            protected override void Begin(KarenContext c)
            {
                pa = c.World != null ? c.World.Pa : null;
                if (pa == null) return;
                pa.SpeechStarted += OnSpeech;
                announcement = pa.Announce("Attention, shoppers! Two for one in aisle 7, for the next ten minutes.");
                void OnSpeech(PaAnnouncement a)
                {
                    if (a != announcement) return;
                    landed = FlashSaleState.Start(c.Shift, pa);
                    if (landed) c.Brain?.RecordEffect("start the flash sale: customer limit 4");
                }
                handler = OnSpeech;
            }
            System.Action<PaAnnouncement> handler;
            protected override Status Tick(KarenContext c, float dt) => landed ? Status.Success :
                announcement == null || announcement.Done || FairnessGuard.DoorsClosed(c) ? Status.Failure : Status.Running;
            protected override void End(KarenContext c, bool aborted)
            {
                if (pa == null) return;
                pa.SpeechStarted -= handler;
                if (!landed) pa.Cancel(announcement);
            }
        }
    }
}
