using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

// Light.enabled does not control reflection probes, emission, or unlit fixture panels.
// Override those at render time without editing the shared materials/art assets.
[DefaultExecutionOrder(10000)]
public sealed class BlackoutRendering : MonoBehaviour
{
    sealed class Surface
    {
        public Renderer renderer;
        public int index;
        public MaterialPropertyBlock saved;
        public bool emission, unlit;
    }

    readonly Dictionary<ReflectionProbe, float> probes = new Dictionary<ReflectionProbe, float>();
    readonly List<Surface> surfaces = new List<Surface>();
    readonly Dictionary<Light, bool> lightStates = new Dictionary<Light, bool>();
    readonly Dictionary<Camera, Color> backgrounds = new Dictionary<Camera, Color>();
    // Made on first use: Unity forbids creating one in a field initialiser (it runs in the constructor).
    MaterialPropertyBlock block;
    Color fogColour;
    bool dark;
    float nextSweep;

    void OnEnable()
    {
        PowerSystem.PowerChanged += OnPower;
        OnPower(PowerSystem.PowerOn);
    }

    void OnPower(bool on)
    {
        if (dark == !on) return;
        dark = !on;
        if (dark) { Capture(); Suppress(); }
        else Restore();
    }

    void LateUpdate()
    {
        OnPower(PowerSystem.PowerOn);
        if (!dark) return;
        // A running flicker or room switch cannot relight the store: lights and fog every frame
        // (cheap), the surfaces' overrides twice a second (thousands of renderers on an 8 GB M2).
        KeepDark();
        if (Time.unscaledTime >= nextSweep) { nextSweep = Time.unscaledTime + .5f; Suppress(); }
    }

    void KeepDark()
    {
        RenderSettings.fogColor = Color.black;
        foreach (var pair in lightStates) if (pair.Key != null && pair.Key.enabled) pair.Key.enabled = false;
    }

    void Capture()
    {
        fogColour = RenderSettings.fogColor;
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsInactive.Include)) backgrounds[camera] = camera.backgroundColor;
        foreach (var probe in FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include)) probes[probe] = probe.intensity;
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null) continue;
                bool emission = material.HasProperty("_EmissionColor");
                // Includes ceiling panels, live CCTV pictures, and world-space fixture text.
                bool unlit = material.shader != null &&
                    (material.shader.name.IndexOf("Unlit", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     material.shader.name.StartsWith("TextMeshPro/", System.StringComparison.Ordinal));
                if (!emission && !unlit) continue;
                var saved = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(saved, i);
                surfaces.Add(new Surface { renderer = renderer, index = i, saved = saved, emission = emission, unlit = unlit });
            }
        }
        foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            // A carried torch is battery powered. Outdoor lighting is a separate supply.
            if (light.GetComponentInParent<Flashlight>() != null ||
                RoomLighting.BitFor(Kehai.Store.StoreMap.AreaAt(light.transform.position)) == RoomLighting.Outside) continue;
            lightStates[light] = light.enabled;
        }
    }

    void Suppress()
    {
        if (block == null) block = new MaterialPropertyBlock();
        RenderSettings.fogColor = Color.black;
        foreach (var pair in backgrounds) if (pair.Key != null) pair.Key.backgroundColor = Color.black;
        foreach (var pair in probes) if (pair.Key != null) pair.Key.intensity = 0f;
        foreach (var surface in surfaces)
        {
            if (surface.renderer == null) continue;
            surface.renderer.GetPropertyBlock(block, surface.index);
            // An indexed block takes priority over the renderer block. Keep its other values.
            if (block.isEmpty) surface.renderer.GetPropertyBlock(block);
            if (surface.emission) block.SetColor("_EmissionColor", Color.black);
            if (surface.unlit)
            {
                block.SetColor("_BaseColor", Color.black);
                block.SetColor("_Color", Color.black);
                block.SetColor("_FaceColor", Color.black);
                block.SetColor("_OutlineColor", Color.black);
                block.SetColor("_UnderlayColor", Color.black);
                block.SetColor("_GlowColor", Color.black);
            }
            surface.renderer.SetPropertyBlock(block, surface.index);
        }
        foreach (var pair in lightStates) if (pair.Key != null) pair.Key.enabled = false;
    }

    void Restore()
    {
        RenderSettings.fogColor = fogColour;
        foreach (var pair in backgrounds) if (pair.Key != null) pair.Key.backgroundColor = pair.Value;
        foreach (var pair in probes) if (pair.Key != null) pair.Key.intensity = pair.Value;
        foreach (var surface in surfaces)
            if (surface.renderer != null) surface.renderer.SetPropertyBlock(surface.saved, surface.index);
        foreach (var pair in lightStates)
        {
            if (pair.Key == null || !pair.Value || !PowerSystem.PowerOn) continue;
            var panel = BreakerPanel.Instance;
            pair.Key.enabled = RoomLightSwitch.Allows(pair.Key.transform.position) &&
                (panel == null || panel.IsOn(LightControl.CircuitOf(pair.Key.transform.position)));
        }
        probes.Clear(); surfaces.Clear(); lightStates.Clear(); backgrounds.Clear();
        LightControl.Instance?.ApplyCircuits();
    }

    void OnDisable()
    {
        PowerSystem.PowerChanged -= OnPower;
        if (dark) Restore();
        dark = false;
    }
}
