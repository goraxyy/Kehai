using System;

namespace Kehai.Karen
{
    public static class FlashSaleState
    {
        static readonly SaleRules rules = new SaleRules();
        public static bool Active => rules.Active;
        public static event Action<bool> Changed;
        public static int Cap(int configured) => rules.Cap(configured);
        public static bool Start(ShiftManager shift, PaSystem pa)
        {
            if (!rules.Start(shift != null && shift.IsShiftActive && !shift.DoorsClosed,
                             pa != null && pa.Powered)) return false;
            TaskManager.NotifyWorldChanged();
            Changed?.Invoke(true);
            return true;
        }
        public static bool End()
        {
            if (!rules.End()) return false;
            TaskManager.NotifyWorldChanged();
            Changed?.Invoke(false);
            return true;
        }
        // Called by ShiftManager at door close, and by PaSystem as a fallback each frame.
        public static void Step(ShiftManager shift)
        {
            if (shift != null && (!shift.IsShiftActive || shift.DoorsClosed)) End();
        }
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { rules.End(); Changed = null; }
    }
}
