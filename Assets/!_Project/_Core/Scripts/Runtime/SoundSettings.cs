using Kehai;
using UnityEngine;

// What the player can turn up and down (Esc → Settings). Master is the listener's own
// volume; the others scale each kind of sound as the game plays it. Kept in PlayerPrefs,
// so they survive a restart.
public enum SoundKind { Effects, Karen, Music, Voice }

public static class SoundSettings
{
    static readonly float[] levels = { 1f, 1f, 1f, 1f };
    static float master = 1f;

    public static event System.Action Changed;

    public static float Master
    {
        get => master;
        set
        {
            master = Mathf.Clamp01(value);
            AudioListener.volume = Audible(master);
            PlayerPrefs.SetFloat("Kehai.Volume.Master", master);
            Changed?.Invoke();
        }
    }

    // Unattended runs (EditMode tests, bot shifts, replay renders: all batch mode) are never
    // heard: nobody is listening, and the developer's speakers stay quiet. Renders mix their
    // sound offline, so they lose nothing.
    public static float Audible(float level) => Application.isBatchMode ? 0f : level;

    public static float Get(SoundKind kind) => levels[(int)kind];

    public static void Set(SoundKind kind, float value)
    {
        levels[(int)kind] = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("Kehai.Volume." + kind, levels[(int)kind]);
        Changed?.Invoke();
    }

    public static string Label(SoundKind kind)
    {
        switch (kind)
        {
            case SoundKind.Karen: return GameNames.Antagonist + " (her footsteps and warnings)";
            case SoundKind.Music: return "Music (the radio)";
            case SoundKind.Voice: return "Announcements (the PA)";
            default: return "Sound effects (the store, you)";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Load()
    {
        master = PlayerPrefs.GetFloat("Kehai.Volume.Master", 1f);
        AudioListener.volume = Audible(master);
        for (int i = 0; i < levels.Length; i++)
            levels[i] = PlayerPrefs.GetFloat("Kehai.Volume." + (SoundKind)i, 1f);
    }
}
