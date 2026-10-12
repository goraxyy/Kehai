using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Resolution, full screen and quality (Esc → Settings). Unity's player remembers the
// resolution and the full-screen mode itself; the quality is kept in PlayerPrefs.
//
// Quality is three presets over the project's own pipeline settings, not Unity's quality
// levels: the other level has the GPU Resident Drawer off, so the shelves would cost more to
// draw on "Low" than on "High". High is the project's settings as they are. Medium and Low
// render fewer pixels, with less anti-aliasing and shorter, coarser shadows, on a copy of the
// pipeline asset, so the asset itself is never changed (in the editor, the original is put back
// when Play stops).
public static class DisplaySettings
{
    public enum Quality { Low, Medium, High }

    const string QualityKey = "Kehai.Quality";

    static bool captured;
    static RenderPipelineAsset levelOverride;   // what the quality level had: the asset, or none
    static UniversalRenderPipelineAsset original, copy;

    public static Quality Current { get; private set; } = Quality.High;

    public static void SetQuality(Quality quality)
    {
        Current = quality;
        PlayerPrefs.SetInt(QualityKey, (int)quality);
        Apply();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Load()
    {
        // Tests, bot shifts and renders keep the project's settings, so their numbers compare.
        if (Application.isBatchMode) return;
        Current = (Quality)Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, (int)Quality.High), 0, 2);
        Apply();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= PutBack;
        UnityEditor.EditorApplication.playModeStateChanged += PutBack;
#endif
    }

    static void Apply()
    {
        if (!captured)
        {
            levelOverride = QualitySettings.renderPipeline;
            original = (levelOverride ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            captured = true;
        }
        if (original == null) return;

        if (Current == Quality.High)
        {
            if (copy != null && QualitySettings.renderPipeline == copy) QualitySettings.renderPipeline = levelOverride;
            return;
        }

        if (copy == null)
        {
            copy = Object.Instantiate(original);
            copy.name = original.name + " (quality)";
            copy.hideFlags = HideFlags.DontSave;
        }
        bool low = Current == Quality.Low;
        copy.renderScale = original.renderScale * (low ? 0.7f : 0.85f);
        copy.msaaSampleCount = low ? 1 : Mathf.Min(original.msaaSampleCount, 2);
        copy.shadowDistance = original.shadowDistance * (low ? 0.35f : 0.6f);
        copy.shadowCascadeCount = low ? 1 : Mathf.Min(original.shadowCascadeCount, 2);
        copy.mainLightShadowmapResolution = Mathf.Min(original.mainLightShadowmapResolution, low ? 1024 : 2048);
        if (QualitySettings.renderPipeline != copy) QualitySettings.renderPipeline = copy;
    }

#if UNITY_EDITOR
    // The quality level's own asset goes back before the editor could save the settings.
    static void PutBack(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode || !captured) return;
        if (copy != null && QualitySettings.renderPipeline == copy) QualitySettings.renderPipeline = levelOverride;
        captured = false;
        copy = null;
        original = null;
    }
#endif

    // ---- the screen ---------------------------------------------------------------------------

    // The sizes the screen offers, smallest first, each once.
    public static List<Vector2Int> Resolutions()
    {
        var sizes = new List<Vector2Int>();
        foreach (Resolution r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!sizes.Contains(size)) sizes.Add(size);
        }
        var now = new Vector2Int(Screen.width, Screen.height);
        if (!sizes.Contains(now)) sizes.Add(now);
        sizes.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return sizes;
    }

    public static Vector2Int Resolution => new Vector2Int(Screen.width, Screen.height);

    public static void SetResolution(Vector2Int size) => Screen.SetResolution(size.x, size.y, Screen.fullScreenMode);

    public static bool FullScreen
    {
        get => Screen.fullScreenMode != FullScreenMode.Windowed;
        set => Screen.fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }
}
