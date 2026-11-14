using System.Collections.Generic;
using Kehai;
using Kehai.Karen;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// The store's own cameras (0.4.1), put in at load as data, like the rest of KarenFixtures,
// since scenes aren't in git.
//
//   - Six cameras, one each in the cold room, the stockroom, the lobby, the staff room, in
//     front of the store and in the backstreet (Spots). Each is hung at the top of the nearest
//     wall, found with rays, and aimed at its room.
//   - The six monitors on the office's security desk were decoration (they're modelled into
//     the desk itself). A wall of six bigger screens stands in front of them (CctvWall), each
//     showing one camera live. From the office you can see into six rooms: where Karen is,
//     where the shoppers are.
//   - A camera box on the backstreet wall, by the breaker box and the fridge box (CameraBox):
//     hold E for twenty seconds to fix the cameras when she has broken them.
//
// The cameras work for whoever is at the screens. For you, that's the picture. For Karen, it's
// her Camera watch trick: while she stands at the screens (Watching), each camera's eye runs,
// and what it sees of you reaches her as a report, the same way a camera of her own does. At
// any other time the cameras tell her nothing: they aren't six more eyes for her, they're
// screens somebody has to be looking at. Her Camera break trick turns them all off, for both
// of you, until they're fixed at the box.
public sealed class StoreCameras : MonoBehaviour
{
    // One camera: the room it watches, where to look for its wall, and what it's aimed at.
    public readonly struct Spot
    {
        public readonly string Name, Area;
        public readonly Vector2 Probe;       // world x, z: a point in the room near the wall it hangs on
        public readonly Vector2 Wall;        // the way from the probe to that wall
        public readonly Vector3 LookAt;      // the point on the floor it's aimed at
        public Spot(string name, string area, Vector2 probe, Vector2 wall, Vector3 lookAt)
        {
            Name = name; Area = area; Probe = probe; Wall = wall; LookAt = lookAt;
        }
    }

    // The six the owner asked for. Areas are StoreMap.AreaAt's names; coordinates are world
    // metres (north is +z, the street; the stockroom is south).
    public static readonly Spot[] Spots =
    {
        new Spot("COLD ROOM", "Cold storage", new Vector2(83f, -177.5f), new Vector2(1f, 0f), new Vector3(66f, 0f, -184f)),
        new Spot("STOCKROOM", "Stockroom", new Vector2(15f, -177.5f), new Vector2(-1f, 0f), new Vector3(36f, 0f, -183f)),
        new Spot("LOBBY", "Lobby", new Vector2(83f, -112f), new Vector2(1f, 0f), new Vector3(60f, 0f, -116f)),
        new Spot("STAFF ROOM", "Staff room", new Vector2(12f, -138.5f), new Vector2(-1f, 0f), new Vector3(19f, 0f, -145f)),
        new Spot("STORE FRONT", "Pavement", new Vector2(30f, -108.5f), new Vector2(0f, -1f), new Vector3(54f, 0f, -107.5f)),
        new Spot("BACKSTREET", "Backstreet", new Vector2(14.5f, -152.5f), new Vector2(0f, 1f), new Vector3(18.5f, 0f, -168f)),
    };

    public const float MountHeight = 3f;         // above the floor, or just under a lower ceiling
    public const float FeedInterval = 0.12f;     // seconds between two feed renders: each screen about 1.4 times a second
    public const float FeedRange = 14f;          // the screens only redraw while you're this near them
    public const int FeedWidth = 384, FeedHeight = 216;
    public const float ReportEvery = 1f;         // a watched camera's sighting reaches her once a second

    // Unity's own null check, so a destroyed one (a scene unloaded, a test torn down) reads as none.
    static StoreCameras instance;
    public static StoreCameras Instance
    {
        get => instance != null ? instance : null;
        private set => instance = value;
    }

    readonly List<StoreCamera> cameras = new List<StoreCamera>();
    public IReadOnlyList<StoreCamera> Cameras => cameras;
    public CctvWall Wall { get; private set; }
    public CameraBox Box { get; private set; }

    // Broken by her (Camera break): every screen dark, no camera sees for anyone.
    public bool Broken { get; private set; }
    public float BrokenAt { get; private set; } = -1f;
    public int TimesBroken { get; private set; }

    // She is at the screens (Camera watch): the cameras' eyes run and report to her.
    public bool Watching { get; private set; }
    public float WatchedSeconds { get; private set; }
    public int Sightings { get; private set; }

    public static event System.Action<bool> BrokenChanged;

    int nextFeed;
    float nextFeedAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        BrokenChanged = null;
    }

    // ---- putting them in --------------------------------------------------------------------

    // Called by KarenFixtures.Install. Skips what's there, and does nothing in a scene that
    // isn't the store (no floor under a spot: no camera there).
    public static StoreCameras Install(Material outline) => InstallAt(Spots, outline);

    // The same for any list of spots: a test hangs one camera in a room made of boxes.
    public static StoreCameras InstallAt(IReadOnlyList<Spot> spots, Material outline)
    {
        if (Instance != null) return Instance;
        var root = new GameObject("Store_Cameras");
        var all = root.AddComponent<StoreCameras>();
        Instance = all;

        foreach (Spot spot in spots)
        {
            if (!Mount(spot, out Vector3 at)) continue;
            all.cameras.Add(StoreCamera.Build(root.transform, spot, at));
        }
        if (all.cameras.Count == 0)
        {
            // Not the store: leave nothing behind.
            Instance = null;
            CctvWall.Drop(root);
            return null;
        }

        all.Wall = CctvWall.Build(root.transform, all.cameras);
        all.Box = CameraBox.Place(outline);
        TaskManager.CamerasBroken = () => Instance != null && Instance.Broken;
        return all;
    }

    // Where a camera hangs: at the top of the wall the spot names, or under the ceiling at the
    // probe itself if there's no wall that way. False without a floor under the probe.
    public static bool Mount(Spot spot, out Vector3 at)
    {
        at = default;
        var from = new Vector3(spot.Probe.x, 1.5f, spot.Probe.y);
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;
        float y = floor.point.y + MountHeight;
        if (Physics.Raycast(floor.point + Vector3.up * 0.5f, Vector3.up, out RaycastHit ceiling, 10f, ~0, QueryTriggerInteraction.Ignore))
            y = Mathf.Min(y, ceiling.point.y - 0.35f);
        y = Mathf.Max(y, floor.point.y + 2f);

        var high = new Vector3(spot.Probe.x, y, spot.Probe.y);
        var toWall = new Vector3(spot.Wall.x, 0f, spot.Wall.y).normalized;
        at = Physics.Raycast(high, toWall, out RaycastHit wall, 8f, ~0, QueryTriggerInteraction.Ignore)
            ? wall.point - toWall * 0.24f
            : high;
        return true;
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        TaskManager.CamerasBroken = null;
    }

    // ---- her two tricks --------------------------------------------------------------------

    // Camera break: all of them, at once. False if they're already broken.
    public bool Break()
    {
        if (Broken) return false;
        Broken = true;
        BrokenAt = Time.time;
        TimesBroken++;
        SetWatching(false);
        foreach (StoreCamera cam in cameras) if (cam != null) cam.SetBroken(true);
        if (Wall != null) Wall.SetSignal(false);
        TaskManager.NotifyWorldChanged();
        BrokenChanged?.Invoke(true);
        return true;
    }

    // The camera box, held to the end. False if nothing was broken.
    public bool Fix()
    {
        if (!Broken) return false;
        Broken = false;
        BrokenAt = -1f;
        foreach (StoreCamera cam in cameras) if (cam != null) cam.SetBroken(false);
        if (Wall != null) Wall.SetSignal(true);
        nextFeedAt = 0f;
        TaskManager.NotifyWorldChanged();
        BrokenChanged?.Invoke(false);
        return true;
    }

    // Camera watch: she sat down at the screens, or got up. Broken cameras show her nothing.
    public void SetWatching(bool on)
    {
        on &= !Broken;
        if (Watching == on) return;
        Watching = on;
        foreach (StoreCamera cam in cameras) if (cam != null) cam.Eye.ResetAwareness();
    }

    // A new shift: everything works again and nobody is watching.
    public void ResetForShift()
    {
        SetWatching(false);
        if (Broken) Fix();
        WatchedSeconds = 0f;
        Sightings = 0;
        TimesBroken = 0;
    }

    // The room a camera covers that someone at `p` is in, or null: for the story and the tests.
    public static string CoveredArea(string area)
    {
        foreach (Spot s in Spots) if (s.Area == area) return s.Area;
        return null;
    }

    // ---- every frame ------------------------------------------------------------------------

    void Update()
    {
        Step(Time.deltaTime);
        if (!Broken) DrawFeeds();
    }

    // One step of the cameras as her eyes: only while she's at the screens and they work.
    // Update drives it; public so a test can.
    public void Step(float dt)
    {
        if (Watching && !Broken) Watch(dt);
    }

    // The cameras' eyes, while she's at the screens. Each is a SightSensor like any other eye:
    // it sees you only along a clear line, in its cone, lit enough, and not through her fog.
    void Watch(float dt)
    {
        WatchedSeconds += dt;
        foreach (StoreCamera cam in cameras)
        {
            if (cam == null) continue;
            cam.Eye.Tick(dt);
            if (cam.Eye.Awareness < cam.Eye.glimpseAt || Time.time < cam.NextReport) continue;
            cam.NextReport = Time.time + ReportEvery;
            Sightings++;
            InfrastructureFeed.Report(new Observation(SenseChannel.Infrastructure, cam.Eye.LastSeenPosition,
                cam.Eye.Awareness * 0.65f, 2.5f, $"store camera({cam.Spot.Area})"));
        }
    }

    static bool CanDraw => !Application.isBatchMode && HasGraphics;

    // A graphics device to draw a feed with: none in batch runs with -nographics (the tests, the
    // ablations), where a feed is never made. A batch run with graphics (the pictures) has one.
    public static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    // One feed at a time, in turn, and only while you're near enough to the screens to see
    // them: six extra views of the store are dear on a small computer.
    void DrawFeeds()
    {
        if (Wall == null || !CanDraw || Time.unscaledTime < nextFeedAt || cameras.Count == 0) return;
        Camera view = Camera.main;
        if (view == null || (view.transform.position - Wall.transform.position).sqrMagnitude > FeedRange * FeedRange) return;
        nextFeedAt = Time.unscaledTime + FeedInterval;
        nextFeed = (nextFeed + 1) % cameras.Count;
        if (cameras[nextFeed] != null) cameras[nextFeed].RenderFeed();
    }

    // Every feed now: for a screenshot, or the first look at the wall.
    public void RenderAllNow()
    {
        if (Broken) return;
        foreach (StoreCamera cam in cameras) if (cam != null) cam.RenderFeed();
    }
}

// One of the store's cameras: a housing on a bracket, an eye (for Karen, while she watches) and
// a picture (for the office screens). It isn't something you use: it hangs out of reach.
public sealed class StoreCamera : MonoBehaviour
{
    public StoreCameras.Spot Spot { get; private set; }
    public SightSensor Eye { get; private set; }
    public Camera Feed { get; private set; }
    public RenderTexture Picture { get; private set; }
    public bool IsBroken { get; private set; }
    [System.NonSerialized] public float NextReport;

    Light led;

    public static StoreCamera Build(Transform parent, StoreCameras.Spot spot, Vector3 at)
    {
        var root = new GameObject("Store_Camera_" + spot.Area.Replace(' ', '_'));
        root.transform.SetParent(parent, false);
        Vector3 look = spot.LookAt - at;
        root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));

        if (PropModels.Place(PropModels.StoreCamera, root.transform) == null)
        {
            // Its own boxes until the model's imported: a wall plate, an arm, the housing, a
            // hood over the lens, the lens.
            Part(root.transform, "Housing", Vector3.zero, new Vector3(0.14f, 0.13f, 0.34f), new Color(0.9f, 0.9f, 0.88f));
            Part(root.transform, "Hood", new Vector3(0f, 0.075f, 0.06f), new Vector3(0.17f, 0.02f, 0.4f), new Color(0.82f, 0.82f, 0.8f));
            Part(root.transform, "Lens", new Vector3(0f, 0f, 0.175f), new Vector3(0.09f, 0.09f, 0.02f), new Color(0.03f, 0.03f, 0.04f));
            Part(root.transform, "Arm", new Vector3(0f, 0.11f, -0.12f), new Vector3(0.035f, 0.14f, 0.035f), new Color(0.3f, 0.3f, 0.32f));
        }

        var cam = root.AddComponent<StoreCamera>();
        cam.Spot = spot;

        var eyeGo = new GameObject("Eye");
        eyeGo.transform.SetParent(root.transform, false);
        eyeGo.transform.localPosition = new Vector3(0f, 0f, 0.2f);
        cam.Eye = eyeGo.AddComponent<SightSensor>();
        cam.Eye.range = 30f;
        cam.Eye.fieldOfView = 84f;
        cam.Eye.halfStrengthAt = 14f;
        cam.Eye.gain = 1.1f;
        cam.Eye.darkVision = 0.55f;
        cam.Eye.ignoreRoot = root.transform;

        var feedGo = new GameObject("Feed");
        feedGo.transform.SetParent(root.transform, false);
        feedGo.transform.localPosition = new Vector3(0f, 0f, 0.24f);   // just past the lens
        cam.Feed = feedGo.AddComponent<Camera>();
        cam.Feed.enabled = false;                 // drawn by hand, a feed at a time (StoreCameras.DrawFeeds)
        cam.Feed.fieldOfView = 68f;
        cam.Feed.nearClipPlane = 0.1f;
        cam.Feed.farClipPlane = 70f;
        cam.Feed.allowHDR = false;
        cam.Feed.allowMSAA = false;
        cam.Feed.clearFlags = CameraClearFlags.SolidColor;
        cam.Feed.backgroundColor = new Color(0.02f, 0.02f, 0.03f);

        var ledGo = new GameObject("Led");
        ledGo.transform.SetParent(root.transform, false);
        ledGo.transform.localPosition = new Vector3(0.05f, -0.05f, 0.18f);
        cam.led = ledGo.AddComponent<Light>();
        cam.led.type = LightType.Point;
        cam.led.range = 0.12f;      // a glint on the housing, not a pool of green on the wall
        cam.led.intensity = 0.6f;
        cam.led.color = new Color(0.2f, 1f, 0.3f);
        return cam;
    }

    static void Part(Transform parent, string name, Vector3 local, Vector3 size, Color colour)
    {
        GameObject go = KarenProps.Box(name, parent.position, size, colour, parent, collider: false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;
    }

    public void SetBroken(bool broken)
    {
        IsBroken = broken;
        if (led != null) led.enabled = !broken;
        if (Eye != null) Eye.ResetAwareness();
    }

    // The picture this camera sends to its screen, made the first time it's asked for.
    public RenderTexture EnsurePicture()
    {
        if (Picture != null) return Picture;
        Picture = new RenderTexture(StoreCameras.FeedWidth, StoreCameras.FeedHeight, 16, RenderTextureFormat.ARGB32)
        {
            name = "Feed " + Spot.Name, antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.DontSave,
        };
        return Picture;
    }

    public void RenderFeed()
    {
        if (IsBroken || Feed == null || !StoreCameras.HasGraphics) return;
        Feed.targetTexture = EnsurePicture();
        Feed.Render();
    }

    void OnDestroy()
    {
        if (Picture == null) return;
        if (Feed != null) Feed.targetTexture = null;
        Picture.Release();
        if (Application.isPlaying) Destroy(Picture); else DestroyImmediate(Picture);
    }
}

// The office's screens (0.4.1): six, one a camera, on the security desk where its six small
// monitors are. Those are part of the desk's own model (one mesh), so they can't be switched
// on or made bigger; the new screens stand just in front of them and cover them. Bigger than
// those (1.3 m wide against 0.94), in the same three-by-two. Each shows its camera live and the
// camera's name under it; with the cameras broken, grey static and NO SIGNAL.
public sealed class CctvWall : MonoBehaviour
{
    public const string ConsoleName = "Security workstation";       // the office builder's name for the desk
    // The old monitors, in the desk's own frame (OfficeRoomModels, "SecurityConsole"): three
    // across, two up, their glass on the −z side, the lower row's foot 0.88 m up, 0.21 m back.
    public const float OldBottom = 0.88f, OldDepth = 0.21f;
    public static readonly Vector2 Screen = new Vector2(1.3f, 0.78f);   // one screen's case, metres
    public static readonly Vector2 Glass = new Vector2(1.22f, 0.686f);  // its picture: 16 by 9
    public static readonly Vector2 Pitch = new Vector2(1.36f, 0.86f);   // centre to centre
    public const float OldMonitorWidth = 0.94f;

    readonly List<Renderer> glasses = new List<Renderer>();
    readonly List<TextMeshPro> labels = new List<TextMeshPro>();
    readonly List<Material> pictures = new List<Material>();
    IReadOnlyList<StoreCamera> cameras;
    Texture2D noise;
    bool signal = true;

    // The way the screens face: at whoever sits at the desk.
    public Vector3 Front { get; private set; }

    // Where someone stands to watch them: a step and a half back from the middle of the wall.
    public Vector3 StandPoint => new Vector3(transform.position.x, 0f, transform.position.z) + Front * 1.6f;

    // The store's own security desk (not the copy on the models island), by the name the office
    // builder gave it. Null in a scene without one.
    public static Transform FindConsole()
    {
        GameObject desk = SceneObjectNames.Find(ConsoleName);
        return desk != null ? desk.transform : null;
    }

    public static CctvWall Build(Transform parent, IReadOnlyList<StoreCamera> cameras) => Build(parent, cameras, FindConsole());

    public static CctvWall Build(Transform parent, IReadOnlyList<StoreCamera> cameras, Transform console)
    {
        if (console == null || cameras == null || cameras.Count == 0) return null;

        var root = new GameObject("CCTV_Screen_Wall");
        root.transform.SetParent(parent, false);
        int rows = Mathf.CeilToInt(cameras.Count / 3f);
        // The bottom row's foot is where the old bottom row's was; the wall stands just in front
        // of the old monitors, facing the way they face.
        float middle = OldBottom + Screen.y * 0.5f + (rows - 1) * Pitch.y * 0.5f;
        root.transform.SetPositionAndRotation(console.TransformPoint(new Vector3(0f, middle, OldDepth - 0.085f)), console.rotation);
        Quaternion facing = console.rotation;
        var wall = root.AddComponent<CctvWall>();
        wall.cameras = cameras;
        wall.Front = -(facing * Vector3.forward);

        for (int i = 0; i < cameras.Count; i++)
        {
            int col = i % 3, row = i / 3;
            // Read like a page from the desk: the first camera top left. The screens face −z,
            // and someone looking at them from that side has −x on their left.
            var local = new Vector3((col - 1) * Pitch.x, ((rows - 1) * 0.5f - row) * Pitch.y, 0f);
            wall.AddScreen(local, cameras[i]);
        }
        wall.SetSignal(true);
        return wall;
    }

    void AddScreen(Vector3 local, StoreCamera cam)
    {
        var screen = new GameObject("Screen_" + cam.Spot.Area.Replace(' ', '_'));
        screen.transform.SetParent(transform, false);
        screen.transform.localPosition = local;

        GameObject model = PropModels.Place(PropModels.CctvScreen, screen.transform);
        // The screens face their −z side (as the old monitors did); a model faces +z once
        // imported (PropModels.Place), so it's turned round.
        if (model != null) model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        else
        {
            GameObject bezel = KarenProps.Box("Bezel", screen.transform.position, new Vector3(Screen.x, Screen.y, 0.07f), new Color(0.07f, 0.07f, 0.08f), screen.transform, collider: false);
            bezel.transform.localPosition = Vector3.zero;
            bezel.transform.localRotation = Quaternion.identity;
        }

        // The picture is always its own: a quad just in front of the case, facing the desk.
        GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glass.name = "Picture";
        Drop(glass.GetComponent<Collider>());
        glass.transform.SetParent(screen.transform, false);
        glass.transform.localPosition = new Vector3(0f, 0.025f, -0.041f);
        glass.transform.localRotation = Quaternion.identity;      // a quad's face looks along its −z
        glass.transform.localScale = new Vector3(Glass.x, Glass.y, 1f);
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        var material = new Material(unlit) { name = "Feed " + cam.Spot.Name };
        var renderer = glass.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        glasses.Add(renderer);
        pictures.Add(material);

        // Its name, under the picture.
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(screen.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, -Screen.y * 0.5f + 0.035f, -0.043f);
        labelGo.transform.localRotation = Quaternion.identity;               // like the quad, read from its −z side
        var label = labelGo.AddComponent<TextMeshPro>();
        label.font = UiTheme.TmpFont;
        label.fontSize = 0.55f;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(Screen.x, 0.07f);
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = cam.Spot.Name;
        labels.Add(label);
    }

    // Destroy, in play or in an editor test.
    public static void Drop(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    static void Texture(Material m, Texture t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        m.mainTexture = t;
    }

    // The cameras work (true) or she has broken them (false).
    public void SetSignal(bool on)
    {
        signal = on;
        for (int i = 0; i < pictures.Count; i++)
        {
            // Without a graphics device (-nographics) there's no picture to show: the noise stands in.
            Texture(pictures[i], on && StoreCameras.HasGraphics ? (Texture)cameras[i].EnsurePicture() : Noise());
            pictures[i].color = on ? Color.white : new Color(0.55f, 0.55f, 0.6f);
            labels[i].text = on ? cameras[i].Spot.Name : cameras[i].Spot.Name + "  ·  NO SIGNAL";
            labels[i].color = on ? (Color)UiTheme.Text : (Color)UiTheme.Accent;
        }
        if (on && StoreCameras.Instance != null) StoreCameras.Instance.RenderAllNow();
    }

    public bool HasSignal => signal;

    // Grey static, made once.
    Texture2D Noise()
    {
        if (noise != null) return noise;
        const int w = 96, h = 54;
        noise = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "No signal", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
        var px = new Color32[w * h];
        var random = new System.Random(41);
        for (int i = 0; i < px.Length; i++)
        {
            byte v = (byte)random.Next(20, 150);
            px[i] = new Color32(v, v, v, 255);
        }
        noise.SetPixels32(px);
        noise.Apply(false, false);
        return noise;
    }

    void Update()
    {
        if (signal || pictures.Count == 0) return;
        // The static crawls.
        var offset = new Vector2(Mathf.Repeat(Time.unscaledTime * 7.3f, 1f), Mathf.Repeat(Time.unscaledTime * 3.1f, 1f));
        foreach (Material m in pictures)
        {
            if (m.HasProperty("_BaseMap")) m.SetTextureOffset("_BaseMap", offset);
            else m.mainTextureOffset = offset;
        }
    }

    void OnDestroy()
    {
        foreach (Material m in pictures) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        if (noise != null) { if (Application.isPlaying) Destroy(noise); else DestroyImmediate(noise); }
    }
}

// The camera box (0.4.1): on the backstreet wall with the breaker box and the fridge box. When
// she has broken the store's cameras its red lamp blinks; hold E for twenty seconds
// (BackstreetFix) and they all come back. It's where Camera break's chore is fixed.
public sealed class CameraBox : HighlightInteractable, IHoldInteractable
{
    public static readonly Vector3 Size = new Vector3(0.36f, 0.44f, 0.13f);   // w, h, d in metres
    public const float Gap = 0.15f;      // between it and the box beside it

    public static CameraBox Instance { get; private set; }

    Renderer lamp;
    Material lampOn, lampOff, lampOk;
    int shown = -1;

    static bool Broken => StoreCameras.Instance != null && StoreCameras.Instance.Broken;

    // Beside the fridge box (which is beside the breaker box), further along the wall.
    public static CameraBox Place(Material outline)
    {
        if (Instance != null) return Instance;
        ElectricBox breakers = FindAnyObjectByType<ElectricBox>();
        if (breakers == null) return null;
        BreakerPanel.FaceOf(breakers, out Vector3 face, out Vector3 facing, out Vector3 across, out float halfWidth);
        float along = halfWidth + KarenFixtures.FogBoxGap + FogBox.Size.x + Gap + Size.x * 0.5f;
        Vector3 at = face + across * along;
        float floor = Physics.Raycast(new Vector3(face.x, face.y + 1.5f, face.z), Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
        at.y = floor + KarenFixtures.BoxHeight;
        Vector3 from = at + facing * 0.6f;
        Vector3 wall = Physics.Raycast(from, -facing, out RaycastHit w, 1.6f, ~0, QueryTriggerInteraction.Ignore) ? w.point : at;
        return Build(wall, facing, outline);
    }

    public static CameraBox Build(Vector3 wall, Vector3 facing, Material outline)
    {
        facing.y = 0f;
        facing.Normalize();
        var root = new GameObject("Camera_Box");
        root.layer = LayerMask.NameToLayer("Interactable");
        root.transform.SetPositionAndRotation(wall + facing * (Size.z * 0.5f), Quaternion.LookRotation(facing));

        var collider = root.AddComponent<BoxCollider>();
        collider.size = Size + new Vector3(0.02f, 0.02f, 0.04f);

        float front = Size.z * 0.5f;
        GameObject model = PropModels.Place(PropModels.CameraBox, root.transform);
        if (model == null)
        {
            // Its own boxes until the model's imported: the case, its door, a little camera
            // picture, the reset switch.
            Part(root.transform, "Body", Vector3.zero, Size, new Color(0.24f, 0.27f, 0.33f));
            Part(root.transform, "Door", new Vector3(0f, 0f, front + 0.004f), new Vector3(Size.x - 0.04f, Size.y - 0.04f, 0.008f), new Color(0.19f, 0.22f, 0.27f));
            Part(root.transform, "Label", new Vector3(-0.03f, 0.15f, front + 0.01f), new Vector3(0.24f, 0.07f, 0.004f), new Color(0.95f, 0.95f, 0.93f));
            Part(root.transform, "Camera sign", new Vector3(-0.05f, 0.0f, front + 0.01f), new Vector3(0.12f, 0.07f, 0.006f), new Color(0.9f, 0.9f, 0.88f));
            Part(root.transform, "Camera sign lens", new Vector3(0.025f, 0.0f, front + 0.012f), new Vector3(0.03f, 0.04f, 0.008f), new Color(0.05f, 0.05f, 0.06f));
            Part(root.transform, "Switch", new Vector3(0f, -0.13f, front + 0.02f), new Vector3(0.1f, 0.03f, 0.03f), new Color(0.85f, 0.85f, 0.87f));
        }

        var box = root.AddComponent<CameraBox>();
        OutlineHighlight outlineHighlight = root.GetComponent<OutlineHighlight>();
        outlineHighlight.outlineMaterial = outline;
        outlineHighlight.shape = OutlineHighlight.OutlineShape.BoundingBox;

        GameObject lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lampGo.name = "Lamp";
        lampGo.transform.SetParent(root.transform, false);
        lampGo.transform.localPosition = new Vector3(0.13f * PropModels.Side(model), 0.15f, front + 0.012f);
        lampGo.transform.localScale = Vector3.one * 0.032f;
        CctvWall.Drop(lampGo.GetComponent<Collider>());
        box.lamp = lampGo.GetComponent<Renderer>();
        box.lampOn = KarenProps.Emissive(new Color(1f, 0.12f, 0.08f), 4f);
        box.lampOff = KarenProps.Lit(new Color(0.3f, 0.06f, 0.05f));
        box.lampOk = KarenProps.Emissive(new Color(0.15f, 1f, 0.3f), 2f);
        box.Show(2);
        return box;
    }

    static void Part(Transform parent, string name, Vector3 local, Vector3 size, Color colour)
    {
        GameObject go = KarenProps.Box(name, parent.position, size, colour, parent, collider: false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;
        go.layer = parent.gameObject.layer;
    }

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    // 0 red off, 1 red on, 2 green.
    void Show(int state)
    {
        if (shown == state || lamp == null) return;
        shown = state;
        lamp.sharedMaterial = state == 2 ? lampOk : state == 1 ? lampOn : lampOff;
    }

    void Update() => Show(!Broken ? 2 : Mathf.Repeat(Time.time, 1f) < 0.5f ? 1 : 0);

    // ---- the player ---------------------------------------------------------------------

    public override void Interact(PlayerInteract player)
    {
        // A tap does nothing: it's a hold.
    }

    public override string GetPrompt() => Broken ? BackstreetFix.Prompt("fix the cameras") : "Cameras: all working";

    public bool CanHold(PlayerInteract player) => Broken;
    public float HoldDuration => BackstreetFix.HoldSeconds;
    public void OnHoldProgress(float normalised) => BackstreetFix.WorkNoise(transform.position, normalised);
    public void OnHoldCancelled() { }

    public void OnHoldComplete(PlayerInteract player)
    {
        if (StoreCameras.Instance == null || !StoreCameras.Instance.Fix()) return;   // her Ledger hears of it from BrokenChanged
        BackstreetFix.DoneNoise(transform.position);
        KarenNarrator.Say(StoryKind.Store, "You fixed the store's cameras. The office screens are back.", transform.position);
    }
}
