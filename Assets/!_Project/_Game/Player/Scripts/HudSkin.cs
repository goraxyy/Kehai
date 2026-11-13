using Kehai;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The HUD in the theme's look (0.4.1). The task list, the energy bar and the hand slots are
// built in the scene, which isn't in git, so they're dressed here at load instead: the rounded
// face on their text, round corners on their panels and boxes, and the theme's colours — the
// charcoal behind, the coral for what's selected or running out, soft green and amber for the
// energy bar. Nothing moves and nothing changes size: only how it's drawn. The owner's
// redesign replaces all of it; until then it matches the menus.
public static class HudSkin
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        Apply();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

    public static void Apply()
    {
        if (Application.isBatchMode) return;
        foreach (TaskListUI list in Object.FindObjectsByType<TaskListUI>(FindObjectsInactive.Include)) Dress(list);
        foreach (BurnoutUI bar in Object.FindObjectsByType<BurnoutUI>(FindObjectsInactive.Include)) Dress(bar);
        foreach (InventoryUI slots in Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include)) Dress(slots);
    }

    // A panel's backing: round corners, the theme's charcoal, a little see-through.
    public static void Round(Image image, Color colour, float cornerScale = 1f)
    {
        if (image == null) return;
        image.sprite = UiTheme.Rounded;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = Mathf.Max(0.1f, cornerScale);
        image.color = colour;
    }

    static Color Backing(float alpha = 0.86f)
    {
        Color c = UiTheme.Ink;
        c.a = alpha;
        return c;
    }

    public static void Dress(TaskListUI list)
    {
        if (list == null) return;
        if (list.text != null)
        {
            if (UiTheme.TmpFont != null) list.text.font = UiTheme.TmpFont;
            list.text.color = UiTheme.Text;
        }
        if (list.panel != null) Round(list.panel.GetComponent<Image>(), Backing(), 1.4f);
    }

    public static void Dress(BurnoutUI bar)
    {
        if (bar == null) return;
        bar.fullColour = UiTheme.Good;
        bar.midColour = UiTheme.Warn;
        bar.emptyColour = UiTheme.Accent;
        if (bar.fillImage != null) Round(bar.fillImage, bar.fillImage.color, 4f);
        // The strip behind the fill is its parent.
        if (bar.fill != null && bar.fill.parent != null) Round(bar.fill.parent.GetComponent<Image>(), Backing(0.9f), 4f);
    }

    public static void Dress(InventoryUI slots)
    {
        if (slots == null) return;
        slots.activeSlotBorderColor = UiTheme.Accent;
        foreach (Image back in slots.slotBackgrounds) if (back != null) Round(back, Backing(0.9f), 2f);
        foreach (Image fill in slots.itemFills) if (fill != null) Round(fill, UiTheme.Text, 2.4f);
        foreach (Image border in slots.slotBorders) if (border != null) Round(border, border.color, 2f);
    }
}
