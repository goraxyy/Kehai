using System.Collections;
using Kehai.Karen;
using Kehai.Sandbox;
using UnityEngine;

// The mini store's panel, each control as you'd use it (0.5.2, C11.19): Restart gives a fresh
// shift, Start moves her, Freeze stops her, Tab opens the panel and pauses the game, a sense
// switched off stays off once she starts, and Fire now starts the picked trick.
public static partial class MiniStoreCheck
{
    static IEnumerator PanelControls()
    {
        var allowed = MiniStoreSettings.Allowed;
        MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.None;

        // Restart: a fresh Karen on a fresh shift, nobody caught.
        TricksSandbox box = Object.FindAnyObjectByType<TricksSandbox>();
        KarenBrain old = Object.FindAnyObjectByType<KarenBrain>();
        box.RestartNow();
        yield return Wait(2f);
        KarenBrain k = null;
        for (float t = 0f; t < 20f && (k == null || k == old || k.Body == null || k.Ctx == null); t += Time.deltaTime)
        {
            k = Object.FindAnyObjectByType<KarenBrain>();
            box = Object.FindAnyObjectByType<TricksSandbox>();
            yield return null;
        }
        Expect(k != null && k != old && !k.KilledThisShift, "panel: Restart gives a fresh Karen");
        if (k == null || box == null) yield break;
        Expect(!k.enabled, "panel: after a restart she waits, frozen");

        // A sense switched off before Start stays off after it (the shift's start sets them).
        MiniStoreSettings.Hearing = false;
        MiniStoreSettings.ApplySenses(k);
        box.StartRunning();
        yield return Wait(0.5f);
        Expect(k.enabled && k.Shift != null && k.Shift.IsShiftActive, "panel: Start (P) starts her shift and moves her");
        Expect(!k.Sensorium.UseHearing, "panel: hearing switched off stays off once she starts");
        MiniStoreSettings.Hearing = true;
        MiniStoreSettings.ApplySenses(k);
        Expect(k.Sensorium.UseHearing, "panel: hearing back on");

        // Freeze: she stops where she is.
        yield return Wait(2f);
        box.FreezeNow();
        Vector3 at = k.Body.Position;
        yield return Wait(1.5f);
        Expect(!k.enabled && Vector3.Distance(at, k.Body.Position) < 0.2f,
               $"panel: Freeze (P) stops her ({Vector3.Distance(at, k.Body.Position):0.00} m in 1.5 s)");

        // Tab: the panel opens and pauses the game; Tab again closes it.
        box.Panel.SetOpen(true);
        Expect(box.Panel.Open && GamePause.Paused, "panel: Tab opens it and pauses the game");
        box.Panel.SetOpen(false);
        Expect(!box.Panel.Open && !GamePause.Paused, "panel: Tab again closes it");

        // Fire now: the picked trick starts at once, even with none allowed for her own choices.
        string pick = "phantom_chime";
        MiniStoreSettings.Chosen = pick;
        string why = MiniStorePanel.Readiness(k, pick);
        bool fired = box.TryFire(pick);
        yield return Wait(0.5f);
        bool running = k.CurrentPlan != null && k.CurrentPlan.Tactic != null && k.CurrentPlan.Tactic.Id == pick;
        Expect(fired && running, $"panel: Fire now (Return) starts {pick} ({box.Feedback}; readiness said: {why})");

        MiniStoreSettings.Allowed = allowed;
        MiniStoreSettings.ApplyTo(k.config);
    }
}
