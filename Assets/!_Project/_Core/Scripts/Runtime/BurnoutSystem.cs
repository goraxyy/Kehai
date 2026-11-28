using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Energy runs down over the course of a shift and is topped back up with coffee.
// Empty means no more sprinting, and the edges of the screen close in as it drops.
public class BurnoutSystem : MonoBehaviour
{
    [Header("Energy")]
    [Range(0f, 1f)]
    [Tooltip("1 = fresh, 0 = burnt out.")]
    public float energy = 1f;

    [Tooltip("How much of the bar drains per minute of ordinary work.")]
    public float drainPerMinute = 0.2f;

    [Tooltip("Running burns energy this many times faster.")]
    public float sprintDrainMultiplier = 2f;

    [Tooltip("Extra drain per second while the supervisor is chasing.")]
    public float extraDrainDuringChase = 0.03f;

    [Tooltip("How much one coffee puts back.")]
    public float coffeeRefill = 0.35f;

    [Tooltip("Only drain while a shift is running, so setup time isn't punished.")]
    public bool onlyDrainDuringShift = true;

    [Header("Shift start")]
    [Tooltip("Each shift starts a little more tired than the last.")]
    public float startingEnergyLossPerShift = 0.1f;

    [Header("Vision")]
    public Volume globalVolume;

    [Range(0f, 1f)]
    [Tooltip("Your sight is untouched until energy falls to this. 0.5 = half the bar.")]
    public float fadeStartEnergy = 0.5f;

    [Tooltip("Vignette above the threshold — normally none at all.")]
    public float restedVignette = 0f;

    [Tooltip("Vignette when completely burnt out.")]
    public float burntOutVignette = 0.85f;

    [Range(0.01f, 1f)]
    [Tooltip("Lower is a harder edge to the darkness creeping in.")]
    public float vignetteSmoothness = 0.28f;

    [Header("Blur")]
    [Tooltip("Blur the view as well as darkening it. Uses Gaussian depth of field.")]
    public bool blurWhenTired = true;

    [Range(0.5f, 1.5f)]
    [Tooltip("Blur radius when completely burnt out.")]
    public float burntOutBlur = 1.3f;

    [Tooltip("Distance at which blur begins when rested — far enough to be invisible.")]
    public float restedBlurStart = 12f;

    [Tooltip("Distance at which blur begins when burnt out. Small = everything is soft.")]
    public float burntOutBlurStart = 0.6f;

    public float Energy01 => Mathf.Clamp01(energy);

    // The whole point of the mechanic: run out and you can't run.
    public bool CanSprint => energy > 0f;

    Vignette vignette;
    DepthOfField depthOfField;
    ShiftManager shiftManager;
    PlayerMotor playerMotor;
    bool isInChase;

    void Awake()
    {
        // The scene's own volume, never the film look's (Settings → Display): its vignette sits
        // under the scene's, and fading it would do nothing.
        if (globalVolume == null)
            foreach (Volume v in FindObjectsByType<Volume>())
                if (!Kehai.Look.ScreenExtras.IsFilmVolume(v)) { globalVolume = v; break; }
        shiftManager = FindAnyObjectByType<ShiftManager>();
        playerMotor = FindAnyObjectByType<PlayerMotor>();

        if (globalVolume != null && globalVolume.profile != null)
        {
            // .profile (not sharedProfile) gives this Volume its own runtime copy,
            // so driving the vignette never writes back into the project asset.
            if (globalVolume.profile.TryGet(out vignette))
            {
                // A parameter only reaches the renderer when its override is switched on.
                vignette.active = true;
                vignette.intensity.overrideState = true;
                vignette.color.overrideState = true;
                vignette.smoothness.overrideState = true;
                vignette.color.value = Color.black;
            }
            else
            {
                Debug.LogWarning("No Vignette override on the Volume profile; vision fade is off.", this);
            }

            SetUpBlur();
        }
    }

    void SetUpBlur()
    {
        if (!blurWhenTired) return;

        // .profile is this Volume's own runtime copy, so adding the override when the
        // profile hasn't got one costs nothing on disk and the project asset is untouched.
        if (!globalVolume.profile.TryGet(out depthOfField))
            depthOfField = globalVolume.profile.Add<DepthOfField>(true);

        if (depthOfField == null) return;

        depthOfField.active = false;                       // switched on once you tire
        depthOfField.mode.overrideState = true;
        depthOfField.mode.value = DepthOfFieldMode.Gaussian;
        depthOfField.gaussianStart.overrideState = true;
        depthOfField.gaussianEnd.overrideState = true;
        depthOfField.gaussianMaxRadius.overrideState = true;
        depthOfField.gaussianEnd.value = restedBlurStart;
    }

    void Update() => Step(Time.deltaTime);

    // One frame of the bar over `dt` seconds: the drain (only while a shift runs), then the
    // vision fade. Public so tests can step it with a known time.
    public void Step(float dt)
    {
        if (ShouldDrain())
        {
            float perSecond = drainPerMinute / 60f;

            // Running wears you out faster. Holding Shift while standing still, or while
            // stunned, isn't running (0.5.1 item 9): it costs what walking costs, as it
            // already does for Karen, who only hears sprinting from moving feet.
            if (playerMotor != null && playerMotor.IsMoving && playerMotor.IsSprinting())
                perSecond *= sprintDrainMultiplier;

            energy -= perSecond * dt;

            if (isInChase)
                energy -= extraDrainDuringChase * dt;

            energy = Mathf.Clamp01(energy);
        }

        UpdateVisuals();
    }

    bool ShouldDrain()
    {
        if (!onlyDrainDuringShift) return true;
        return shiftManager != null && shiftManager.IsShiftActive;
    }

    void UpdateVisuals()
    {
        if (vignette == null) return;

        // Nothing happens while you are over the threshold; past it the dark closes
        // in fast, so the second half of a shift is where you feel it.
        float fade = Mathf.InverseLerp(fadeStartEnergy, 0f, Energy01);

        vignette.intensity.value = Mathf.Lerp(restedVignette, burntOutVignette, fade);
        vignette.smoothness.value = vignetteSmoothness;

        UpdateBlur(fade);
    }

    // Pulling the blur's start distance in toward the camera is what makes the whole
    // view go soft rather than just the far wall.
    void UpdateBlur(float fade)
    {
        if (depthOfField == null) return;

        // Below a whisker of fade there is nothing to show, and an inactive override
        // costs nothing in the frame.
        if (fade <= 0.001f)
        {
            depthOfField.active = false;
            return;
        }

        depthOfField.active = true;
        depthOfField.gaussianStart.value = Mathf.Lerp(restedBlurStart, burntOutBlurStart, fade);
        depthOfField.gaussianEnd.value = Mathf.Max(depthOfField.gaussianStart.value + 0.1f, restedBlurStart);
        depthOfField.gaussianMaxRadius.value = Mathf.Lerp(0.5f, burntOutBlur, fade);
    }

    public void SetChaseState(bool chasing)
    {
        isInChase = chasing;
    }

    public void DrinkCoffee()
    {
        energy = Mathf.Clamp01(energy + coffeeRefill);
    }

    public void ResetForNewShift(int shiftIndex)
    {
        energy = Mathf.Clamp01(1f - startingEnergyLossPerShift * Mathf.Max(0, shiftIndex));
        isInChase = false;
    }
}
