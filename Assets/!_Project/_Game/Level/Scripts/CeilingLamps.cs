using System.Collections.Generic;
using UnityEngine;

// A lamp over every ceiling light: a black box hung on a wire from the roof, sitting right
// above the light, with a white panel on its underside, a little smaller than the box. The
// panel glows while its light is on and goes dark when that light is off (a blackout, a tripped
// breaker, Aiko), so the lights you can see are the lights that are working.
//
// Built at load like the aisle signs, or kept from the scene when Kehai/Store/Hang Signs and
// Lamps has baked it in.
public class CeilingLamps : MonoBehaviour
{
    public const string RootName = "CeilingLamps";
    public const float BoxSize = 0.32f;
    public const float PanelSize = 0.24f;      // smaller than the box's side
    const float CheckEvery = 0.25f;

    [System.Serializable]
    public class Lamp
    {
        public Light light;
        public MeshRenderer panel;
        [System.NonSerialized] public int shown = -1;
    }

    public List<Lamp> lamps = new List<Lamp>();
    public Material panelOn, panelOff;
    float nextCheck;

    // The baked lamps if the scene has them, otherwise new ones.
    public static CeilingLamps Ensure()
    {
        var existing = GameObject.Find(RootName);
        if (existing != null && existing.TryGetComponent(out CeilingLamps baked) && baked.lamps.Count > 0)
        {
            baked.Refresh(true);
            return baked;
        }
        return Build(AisleSigns.Flat(new Color(0.015f, 0.015f, 0.018f), 0.25f),
                     AisleSigns.Flat(AisleSigns.WireColour, 0.3f), Glow(), AisleSigns.Flat(new Color(0.12f, 0.12f, 0.12f), 0.4f));
    }

    public static CeilingLamps Build(Material box, Material wire, Material on, Material off)
    {
        var old = GameObject.Find(RootName);
        if (old != null)
        {
            old.name += " (replaced)";
            if (Application.isPlaying) Destroy(old);
            else DestroyImmediate(old);
        }

        var root = new GameObject(RootName);
        var lamps = root.AddComponent<CeilingLamps>();
        lamps.panelOn = on;
        lamps.panelOff = off;

        foreach (Light light in Kehai.Aiko.LightProbe.CeilingLights)
        {
            if (light == null) continue;
            Vector3 p = light.transform.position;
            float roof = RoofAbove(p);

            // The box sits on the light, the panel just under the box, the wire up to the roof.
            float bottom = p.y + 0.012f;
            var lamp = new GameObject("Lamp");
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.position = new Vector3(p.x, bottom, p.z);

            Box(lamp.transform, "Box", new Vector3(0f, BoxSize * 0.5f, 0f), Vector3.one * BoxSize, box);
            float wireLength = roof - (bottom + BoxSize);
            if (wireLength > 0.01f)
                Box(lamp.transform, "Wire", new Vector3(0f, BoxSize + wireLength * 0.5f, 0f),
                    new Vector3(0.012f, wireLength, 0.012f), wire);
            MeshRenderer panel = Box(lamp.transform, "Panel", new Vector3(0f, -0.003f, 0f),
                                     new Vector3(PanelSize, 0.004f, PanelSize), on);

            lamps.lamps.Add(new Lamp { light = light, panel = panel });
        }
        lamps.Refresh(true);
        return lamps;
    }

    void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + CheckEvery;
        Refresh(false);
    }

    void Refresh(bool force)
    {
        foreach (Lamp lamp in lamps)
        {
            if (lamp.panel == null) continue;
            int on = lamp.light != null && lamp.light.enabled && lamp.light.gameObject.activeInHierarchy && lamp.light.intensity > 0f ? 1 : 0;
            if (!force && on == lamp.shown) continue;
            lamp.shown = on;
            lamp.panel.sharedMaterial = on == 1 ? panelOn : panelOff;
        }
    }

    static float RoofAbove(Vector3 p)
    {
        return Physics.Raycast(p + Vector3.down * 0.05f, Vector3.up, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y
            : p.y + BoxSize + 0.1f;
    }

    static MeshRenderer Box(Transform parent, string name, Vector3 local, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (Application.isPlaying) Destroy(go.GetComponent<Collider>());
        else DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return renderer;
    }

    // The lit panel: unlit white, so it reads as a light whatever lights it.
    public static Material Glow()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(1f, 0.98f, 0.92f));
        else m.color = new Color(1f, 0.98f, 0.92f);
        return m;
    }
}
