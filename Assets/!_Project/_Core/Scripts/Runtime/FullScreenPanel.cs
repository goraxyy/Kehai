using System.Collections.Generic;
using UnityEngine;

// While a full-screen panel is open (the F1 map, the F2 replay, the F8 webcam panel), the
// game's HUD canvases are switched off: Unity draws screen-space canvases on top of IMGUI,
// so the task list and inventory would otherwise sit across the map. They come back, as
// they were, when the last panel closes.
public static class FullScreenPanel
{
    static readonly HashSet<object> open = new HashSet<object>();
    static readonly List<Canvas> hidden = new List<Canvas>();

    public static bool AnyOpen => open.Count > 0;
    public static bool IsOpen(object owner) => open.Contains(owner);

    // The frame a panel last closed on, so the Esc that closed it doesn't also open the menu.
    public static int ClosedOnFrame { get; private set; } = -1;

    public static void Set(object owner, bool isOpen)
    {
        bool wasOpen = open.Count > 0;
        if (isOpen) open.Add(owner);
        else if (open.Remove(owner)) ClosedOnFrame = Time.frameCount;
        bool nowOpen = open.Count > 0;
        if (nowOpen == wasOpen) return;

        if (nowOpen)
        {
            hidden.Clear();
            foreach (Canvas c in Object.FindObjectsByType<Canvas>())
                if (c != null && c.enabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    c.enabled = false;
                    hidden.Add(c);
                }
        }
        else
        {
            foreach (Canvas c in hidden) if (c != null) c.enabled = true;
            hidden.Clear();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        open.Clear();
        hidden.Clear();
        ClosedOnFrame = -1;
    }
}
