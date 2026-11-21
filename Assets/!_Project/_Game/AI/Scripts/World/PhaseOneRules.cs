using System;

namespace Kehai.Karen
{
    // No Unity dependency: the sale policy is also exercised by the offline check.
    public sealed class SaleRules
    {
        public const int NormalCap = 2, SaleCap = 4, Ceiling = 5;
        public const float HoldSeconds = 20f;
        public bool Active { get; private set; }
        public int Cap(int configured, int? lessonCap = null)
        {
            int limit = Math.Max(0, Math.Min(Ceiling, Active ? SaleCap : configured));
            return lessonCap.HasValue ? Math.Min(limit, Math.Max(0, lessonCap.Value)) : limit;
        }
        public bool Start(bool open, bool powered)
        {
            if (!open || !powered || Active) return false;
            Active = true;
            return true;
        }
        public bool End()
        {
            bool was = Active;
            Active = false;
            return was;
        }
    }


    // Shared by the real customer and the offline tests. A complaint cannot wait forever.
    public sealed class ComplaintPatience
    {
        public const float WaitingSeconds = 90f, WalkingSeconds = 120f;
        public float Remaining { get; private set; } = WalkingSeconds;
        public bool AtTill { get; private set; }
        public void Arrive() { if (AtTill) return; AtTill = true; Remaining = WaitingSeconds; }
        public bool Step(float dt, bool paused)
        {
            if (!paused && !float.IsNaN(dt) && !float.IsInfinity(dt) && dt > 0f) Remaining -= dt;
            return Remaining <= 0f;
        }
    }
}
