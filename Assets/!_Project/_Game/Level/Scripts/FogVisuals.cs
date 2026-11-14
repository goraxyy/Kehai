using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

// What her fog looks like (Karen 0.3.0; build plan B2.1). It only shows what FogState says:
//
//   - fog pours into the fogged room from its sources (KarenWorld.FogSources: the cold room's
//     fridges, the stockroom's doors to it), harder as it rolls in, and stops when the room is
//     reset;
//   - while your camera is inside a fogged room, the renderer's distance fog closes in to the
//     visibility (KarenConfig.fogVisibility, 2 m): you see about as far as she does.
//
// 0.4.1: all of it is seen from inside the room only (Shows). From the doorway, or through a
// window, a fogged room looks like any other; the fog is there when you step in. Until then
// the clouds poured in plain view of the next room, and with the sales floor a fog room the
// fog seemed to cover the building.
//
// Distance fog, a term: the renderer fades everything beyond a distance into one colour. Here it
// ends at the visibility while you're in the fog. (Elsewhere: Minecraft's render distance fog.)
public sealed class FogVisuals : MonoBehaviour
{
    public static readonly Color Colour = new Color(0.82f, 0.85f, 0.88f);

    static FogVisuals instance;
    readonly Dictionary<string, List<ParticleSystem>> pouring = new Dictionary<string, List<ParticleSystem>>();
    readonly List<(string room, ParticleSystem cloud)> thinning = new List<(string, ParticleSystem)>();

    // Whether someone standing in `viewerRoom` sees the fog of `fogRoom`: only from inside it.
    public static bool Shows(string fogRoom, string viewerRoom) => fogRoom != null && fogRoom == viewerRoom;

    bool saved, applied;
    bool fogWas;
    FogMode modeWas;
    float startWas, endWas;
    Color colourWas;

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("KAREN_FogVisuals");
        instance = go.AddComponent<FogVisuals>();
    }

    void OnEnable()
    {
        FogState.Filled += OnFilled;
        FogState.Cleared += OnCleared;
    }

    void OnDisable()
    {
        FogState.Filled -= OnFilled;
        FogState.Cleared -= OnCleared;
        Restore();
        if (instance == this) instance = null;
    }

    void OnFilled(string room)
    {
        KarenWorld world = KarenWorld.Instance;
        if (world == null) return;
        if (!pouring.TryGetValue(room, out List<ParticleSystem> list)) pouring[room] = list = new List<ParticleSystem>();
        foreach (Vector3 source in world.FogSources(room))
        {
            FogCloud cloud = FogCloud.Build(new Vector3(source.x, FloorUnder(source), source.z), 1.6f, 9999f);
            ParticleSystem ps = cloud.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 14f;
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.rateOverTime = 4f;
            ps.Play();
            list.Add(ps);
        }
    }

    void OnCleared(string room)
    {
        if (!pouring.TryGetValue(room, out List<ParticleSystem> list)) return;
        foreach (ParticleSystem ps in list)
        {
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(ps.gameObject, 15f);   // what's already out thins away
            thinning.Add((room, ps));
        }
        pouring.Remove(room);
    }

    static float FloorUnder(Vector3 p) =>
        Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y - 0.8f;

    void Update()
    {
        // The fridges pour harder as the fog rolls in.
        foreach (KeyValuePair<string, List<ParticleSystem>> pair in pouring)
        {
            float thick = FogState.Thickness(pair.Key);
            foreach (ParticleSystem ps in pair.Value)
            {
                if (ps == null) continue;
                var emission = ps.emission;
                emission.rateOverTime = Mathf.Lerp(4f, 18f, thick);
            }
        }

        Camera eye = Camera.main;

        // The clouds are drawn for someone standing in their room, and for nobody else.
        string here = eye != null && (pouring.Count > 0 || thinning.Count > 0) ? FogState.RoomOf(eye.transform.position) : null;
        foreach (KeyValuePair<string, List<ParticleSystem>> pair in pouring)
            foreach (ParticleSystem ps in pair.Value) Draw(ps, Shows(pair.Key, here));
        for (int i = thinning.Count - 1; i >= 0; i--)
        {
            if (thinning[i].cloud == null) { thinning.RemoveAt(i); continue; }
            Draw(thinning[i].cloud, Shows(thinning[i].room, here));
        }

        float visibility = eye != null ? FogState.VisibilityAt(eye.transform.position) : FogState.Open;
        if (visibility < FogState.Open - 0.01f) Apply(visibility);
        else Restore();
    }

    static void Draw(ParticleSystem ps, bool show)
    {
        if (ps == null) return;
        Renderer r = ps.GetComponent<Renderer>();
        if (r != null && r.enabled != show) r.enabled = show;
    }

    void Apply(float visibility)
    {
        if (!saved)
        {
            saved = true;
            fogWas = RenderSettings.fog;
            modeWas = RenderSettings.fogMode;
            startWas = RenderSettings.fogStartDistance;
            endWas = RenderSettings.fogEndDistance;
            colourWas = RenderSettings.fogColor;
        }
        applied = true;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = Mathf.Min(0.3f, visibility * 0.2f);
        RenderSettings.fogEndDistance = visibility;
        RenderSettings.fogColor = Colour;
    }

    void Restore()
    {
        if (!applied) return;
        applied = false;
        RenderSettings.fog = fogWas;
        RenderSettings.fogMode = modeWas;
        RenderSettings.fogStartDistance = startWas;
        RenderSettings.fogEndDistance = endWas;
        RenderSettings.fogColor = colourWas;
    }
}
