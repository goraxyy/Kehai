using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The mains. Everything electric in the store hangs off this: the ceiling lights and
// the radio. Kill it and the place goes black until someone walks out to the breakers
// in the back alley and flips them again.
public class PowerSystem : MonoBehaviour
{
    public static PowerSystem Instance { get; private set; }

    [Header("What the power drives")]
    [Tooltip("Every Light underneath here goes out with the mains.")]
    public Transform lightRoot;

    [Tooltip("Found automatically if left empty.")]
    public MusicBox musicBox;

    [Header("Input")]
    [Tooltip("Trips the breakers from anywhere in the building.")]
    public KeyCode cutPowerKey = KeyCode.L;

    [Header("Audio")]
    public AudioClip powerDownSound;
    public AudioClip powerUpSound;

    // Real light bounces off the floor and fills in everything the cones miss. URP has
    // no realtime GI, so a gradient ambient stands in for it: bright underneath (the lit
    // floor throwing light back up), dim overhead, so it reads as bounce rather than as
    // a flat wash. It is driven by the mains, which is what keeps a blackout black.
    [Header("Bounce light (while powered)")]
    [Tooltip("Fill on upward-facing surfaces — shelf tops, which the cones already hit.")]
    public Color bounceFromAbove = new Color(0.001f, 0.001f, 0.001f);

    [Tooltip("Fill on walls and anything vertical.")]
    public Color bounceFromSides = new Color(0.001f, 0.001f, 0.001f);

    [Tooltip("Fill on downward-facing surfaces — the ceiling and undersides catching floor bounce.")]
    public Color bounceFromBelow = new Color(0.001f, 0.001f, 0.001f);

    [Range(0f, 1f)]
    public float poweredReflections = 0.25f;

    [Header("Blackout")]
    [Tooltip("What is left to see by once the mains are down.")]
    public Color blackoutAmbient = Color.black;

    [Range(0f, 1f)]
    public float blackoutReflections = 0f;

    [Min(0f)]
    [Tooltip("How long the fill takes to come up or die away.")]
    public float ambientFadeSeconds = 0.6f;

    float ambientBlend = 1f;     // 1 = fully lit, 0 = blackout

    public bool HasPower { get; private set; } = true;

    // Static mirror so anything that cares about the mains can read and subscribe
    // without holding a reference to this object.
    public static bool PowerOn { get; private set; } = true;
    public static event System.Action<bool> PowerChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PowerOn = true;
        PowerChanged = null;
    }

    readonly List<Light> lights = new List<Light>();

    void Awake()
    {
        Instance = this;

        if (musicBox == null) musicBox = FindAnyObjectByType<MusicBox>();
        // This is a code-authored profile, including scenes serialized before 0.5.1.
        bounceFromAbove = bounceFromSides = bounceFromBelow = new Color(.001f, .001f, .001f);
        blackoutAmbient = Color.black;
        blackoutReflections = 0f;
        RenderSettings.skybox = null;
        foreach (Light light in FindObjectsByType<Light>())
            if (light.type == LightType.Directional) light.intensity = 0f;
        Collect();
        foreach (Light light in lights)
            if (light != null && light.type != LightType.Directional) light.intensity *= 1.5f;
        if (GetComponent<BlackoutRendering>() == null) gameObject.AddComponent<BlackoutRendering>();
    }

    void Start()
    {
        // Push the starting state once everything else has woken up.
        ambientBlend = HasPower ? 1f : 0f;
        Apply();
    }

    void Update()
    {
        if (Edition.AllowDebugTools && Input.GetKeyDown(cutPowerKey) && HasPower && !GamePause.Paused) CutPower();

        float target = HasPower ? 1f : 0f;
        if (!Mathf.Approximately(ambientBlend, target))
        {
            ambientBlend = ambientFadeSeconds > 0f
                ? Mathf.MoveTowards(ambientBlend, target, Time.deltaTime / ambientFadeSeconds)
                : target;

            ApplyAmbient();
        }
    }

    void ApplyAmbient()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(blackoutAmbient, bounceFromAbove, ambientBlend);
        RenderSettings.ambientEquatorColor = Color.Lerp(blackoutAmbient, bounceFromSides, ambientBlend);
        RenderSettings.ambientGroundColor = Color.Lerp(blackoutAmbient, bounceFromBelow, ambientBlend);
        RenderSettings.reflectionIntensity = Mathf.Lerp(blackoutReflections, poweredReflections, ambientBlend);
    }

    void Collect()
    {
        lights.Clear();
        if (lightRoot == null) return;

        lightRoot.GetComponentsInChildren(true, lights);
    }

    public void CutPower()
    {
        if (!HasPower) return;

        ambientBlend = 0f; // No ambient fade is allowed to illuminate an outage.
        HasPower = false;
        Apply();
        OneShotAudio.PlayAt(powerDownSound, PlayerPosition());
    }

    public void RestorePower()
    {
        if (HasPower) return;

        HasPower = true;
        Apply();
        OneShotAudio.PlayAt(powerUpSound, PlayerPosition());
    }

    void Apply()
    {
        // Disabling the component rather than the GameObject keeps the hierarchy
        // intact, and flipping 240 of them is a one-off cost, not a per-frame one.
        for (int i = 0; i < lights.Count; i++)
            if (lights[i] != null) lights[i].enabled = HasPower;

        if (musicBox != null) musicBox.SetPowered(HasPower);

        if (PowerOn != HasPower)
        {
            PowerOn = HasPower;
            PowerChanged?.Invoke(HasPower);
        }

        ApplyAmbient();
    }

#if UNITY_EDITOR
    // So the fill can be dialled in from the Inspector without entering play mode.
    void OnValidate()
    {
        ambientBlend = HasPower ? 1f : 0f;
        ApplyAmbient();
    }
#endif

    static Vector3 PlayerPosition()
    {
        var listener = FindAnyObjectByType<AudioListener>();
        return listener != null ? listener.transform.position : Vector3.zero;
    }
}
