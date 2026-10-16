using System.Collections;
using UnityEngine;

namespace Kehai.Karen
{
    // What happens to *you*. If she touches you, she gets you: the screen goes black and
    // the shift starts again from the beginning (Karen.md §8.6). What she has learnt about
    // you stays — it lives in her Ledger, not in the scene. And the three ways a career ends
    // (§10.3).
    public static class Consequences
    {
        // The death screen is up: the player is held still.
        public static bool PlayerHeld { get; private set; }

        // A career has ended and its last screen is up: Enter goes to the main menu.
        public static bool CareerOver { get; internal set; }
        const string ToTheMenu = "\n\n<size=40%>[Enter] main menu</size>";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CareerOver = false;
            PlayerHeld = false;
        }

        // In the eval harness (and any batch run) the shift simply ends: the next episode is
        // a reset anyway. In play, the store loads again with the shift you were on.
        public static IEnumerator Death(KarenBrain brain, float seconds)
        {
            PlayerHeld = true;
            PlayerMotor motor = Object.FindAnyObjectByType<PlayerMotor>();
            if (motor != null) motor.movementLocked = true;

            if (KarenBootstrap.Override != null || Application.isBatchMode)
            {
                ShiftManager shift = Object.FindAnyObjectByType<ShiftManager>();
                if (shift != null && shift.IsShiftActive) shift.EndShift();
                if (motor != null) motor.movementLocked = false;
                PlayerHeld = false;
                yield break;
            }

            KarenScreen screen = KarenScreen.Ensure();
            screen.SetFade(1f);
            screen.Banner($"<color=#FF6F61>{GameNames.Antagonist.ToUpperInvariant()} GOT YOU</color>\n<size=50%>The shift starts again.</size>", seconds);
            yield return new WaitForSecondsRealtime(seconds);
            PlayerHeld = false;
            ShiftRestart.Restart();
        }

        public static void AddOvertime(float seconds)
        {
            ShiftManager shift = Object.FindAnyObjectByType<ShiftManager>();
            if (shift != null) shift.AddOvertime(seconds);
        }

        // ---- the endings ---------------------------------------------------------------

        // You burn out. The last thing that happens is that she makes you a coffee.
        public static IEnumerator KehaiEnding(KarenBrain brain)
        {
            brain.Ledger.Data.endingReached = true;
            brain.Ledger.Save();
            Eyelids lids = Object.FindAnyObjectByType<Eyelids>();
            if (lids != null) lids.Close(6f);
            yield return new WaitForSecondsRealtime(6.5f);
            KarenScreen screen = KarenScreen.Ensure();
            screen.SetFade(1f);
            screen.Banner("<size=160%>過労死</size>\nBURNED OUT\n\n<size=50%>" + brain.Review.EndingText("burnout") + "</size>" + ToTheMenu, 9999f);
            CareerOver = true;
        }

        public static void QuitEnding(KarenBrain brain, string review)
        {
            brain.Ledger.Data.endingReached = true;
            brain.Ledger.Save();
            KarenScreen screen = KarenScreen.Ensure();
            screen.SetFade(0.92f);
            string kind = brain.Review.BrokeHer ? "broke" : "quit";
            screen.Banner("<size=120%>NOTICE ACCEPTED</size>\n\n<size=50%>" + brain.Review.EndingText(kind) + "</size>" + ToTheMenu, 9999f);
            CareerOver = true;
        }
    }
}
