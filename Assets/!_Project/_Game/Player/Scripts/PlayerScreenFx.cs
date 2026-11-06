using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Adds presentation to PlayerEffects. Movement restrictions remain entirely in PlayerMotor.
// Remove last frame's offsets before the motor writes its look/crouch pose, then add this
// frame's offsets in LateUpdate. This avoids camera drift and preserves crouch height.
[DefaultExecutionOrder(-50), DisallowMultipleComponent]
public sealed class PlayerScreenFx : MonoBehaviour
{
    PlayerEffects effects;
    PlayerMotor motor;
    Camera view;
    Transform cameraPose;
    Vector3 appliedPosition;
    Quaternion appliedRotation = Quaternion.identity;
    float phase, bob, stunStart, stunEnd = -999f;
    Volume volume;
    VolumeProfile profile;
    UniversalAdditionalCameraData cameraData;
    bool changedPostProcessing, previousPostProcessing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        PlayerEffects.Changed -= Changed;
        SceneManager.sceneLoaded -= SceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode) return;
        PlayerEffects.Changed -= Changed;
        PlayerEffects.Changed += Changed;
        SceneManager.sceneLoaded -= SceneLoaded;
        SceneManager.sceneLoaded += SceneLoaded;
        SceneLoaded(default, default);
    }

    static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (PlayerEffects on in FindObjectsByType<PlayerEffects>()) Changed(on);
    }

    static void Changed(PlayerEffects on)
    {
        if (on == null || Application.isBatchMode) return;
        var fx = on.GetComponent<PlayerScreenFx>();
        if (fx == null && on.GetComponentInChildren<Camera>() != null) fx = on.gameObject.AddComponent<PlayerScreenFx>();
        if (fx != null) fx.Read(on);
    }

    void Awake()
    {
        effects = GetComponent<PlayerEffects>();
        motor = GetComponent<PlayerMotor>();
        view = GetComponentInChildren<Camera>();
        if (view == null || effects == null || motor == null) { enabled = false; return; }
        cameraPose = view.transform;
        cameraData = view.GetUniversalAdditionalCameraData();
        Read(effects);
    }

    void Read(PlayerEffects on)
    {
        if (on != effects) return;
        if (!on.Stunned) { stunEnd = -999f; return; }
        if (on.Stunned && on.StunnedUntil > stunEnd)
        {
            stunStart = Time.time;
            stunEnd = on.StunnedUntil;
        }
    }

    void Update() => RemoveOffset();

    void RemoveOffset()
    {
        if (cameraPose == null) return;
        cameraPose.localPosition -= appliedPosition;
        cameraPose.localRotation *= Quaternion.Inverse(appliedRotation);
        appliedPosition = Vector3.zero;
        appliedRotation = Quaternion.identity;
    }

    void LateUpdate()
    {
        if (effects == null || motor == null || cameraPose == null) return;
        float dt = Time.deltaTime;
        float speed = GamePause.Paused ? 0f : motor.PlanarSpeed;
        // Distance drives the gait: oil slows its frequency along with the actual player.
        phase = Mathf.Repeat(phase + speed * dt * 2.5f, Mathf.PI * 2f);
        bob = Mathf.MoveTowards(bob, speed > .1f && !effects.Stunned ? .014f : 0f, dt * .1f);
        appliedPosition = new Vector3(Mathf.Sin(phase * .5f) * bob * .35f, Mathf.Sin(phase) * bob, 0f);
        if (effects.Stunned)
        {
            float age = Time.time - stunStart;
            float fade = Mathf.Clamp01((stunEnd - Time.time) / Mathf.Max(.01f, stunEnd - stunStart));
            appliedPosition.x += Mathf.Sin(age * 19f) * .025f * fade;
            appliedRotation = Quaternion.Euler(Mathf.Sin(age * 12f) * 1.4f * fade, 0f, Mathf.Sin(age * 16f) * 2.5f * fade);
        }
        cameraPose.localPosition += appliedPosition;
        cameraPose.localRotation *= appliedRotation;
        ApplyBlur(effects.Blurred);
    }

    void ApplyBlur(bool blurred)
    {
        if (blurred && volume == null) BuildBlur();
        if (volume != null) volume.weight = blurred ? 1f : 0f;
        if (blurred && cameraData != null && !changedPostProcessing)
        {
            previousPostProcessing = cameraData.renderPostProcessing;
            cameraData.renderPostProcessing = true;
            changedPostProcessing = true;
        }
        else if (!blurred) RestorePostProcessing();
    }

    void BuildBlur()
    {
        var go = new GameObject("Powder blur (local volume)");
        go.transform.SetParent(cameraData != null && cameraData.volumeTrigger != null ? cameraData.volumeTrigger : cameraPose, false);
        // Use a layer already sampled by this camera; do not alter the scene's volume mask.
        int mask = cameraData != null ? cameraData.volumeLayerMask.value : 1;
        int layer = 0;
        while (layer < 31 && (mask & (1 << layer)) == 0) layer++;
        go.layer = layer;
        var bounds = go.AddComponent<SphereCollider>();
        bounds.isTrigger = true; bounds.radius = .3f;
        volume = go.AddComponent<Volume>();
        volume.isGlobal = false; volume.priority = 1000f; volume.blendDistance = 0f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;
        var blur = profile.Add<DepthOfField>(true);
        blur.mode.Override(DepthOfFieldMode.Gaussian);
        blur.gaussianStart.Override(0f);
        blur.gaussianEnd.Override(.5f);
        blur.gaussianMaxRadius.Override(1.5f);
        blur.highQualitySampling.Override(true);
    }

    void RestorePostProcessing()
    {
        if (!changedPostProcessing) return;
        if (cameraData != null) cameraData.renderPostProcessing = previousPostProcessing;
        changedPostProcessing = false;
    }

    void OnDisable()
    {
        RemoveOffset();
        if (volume != null) volume.weight = 0f;
        RestorePostProcessing();
    }

    void OnDestroy()
    {
        if (volume != null) Destroy(volume.gameObject);
        if (profile != null)
        {
            foreach (VolumeComponent component in profile.components) if (component != null) Destroy(component);
            Destroy(profile);
        }
    }
}
