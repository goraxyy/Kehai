using Kehai.Karen;
using UnityEngine;

// The PA microphone (0.4.1): a box on the staff room wall by the time clock. At closing time
// you press E on it and say over the PA that the store is closed. Customers stop coming in
// from that moment (ShiftManager.AnnounceClosing); until you do, they keep coming, and the
// shift can't be ended while one is in the store.
//
// Its lamp tells you where things stand: dim before closing time, blinking amber when the
// announcement is owed, green once it's made. It needs the PA to be on: with the PA switched
// off at the PA box beside it, the microphone is dead too.
public sealed class ClosingMic : HighlightInteractable
{
    public static readonly Vector3 Size = new Vector3(0.26f, 0.30f, 0.12f);   // w, h, d in metres
    public const string Words = "Attention, customers. The store is now closed. Please take your shopping to the till.";
    public const string Speaker = "You";

    public static ClosingMic Instance { get; private set; }

    Renderer lamp;
    Material amber, green, dim;
    int shown = -1;

    static ShiftManager shiftManager;
    static ShiftManager Shift => shiftManager != null ? shiftManager : shiftManager = FindAnyObjectByType<ShiftManager>();
    static PaSystem Pa => KarenWorld.Instance != null ? KarenWorld.Instance.Pa : null;

    public static ClosingMic Build(Vector3 wall, Vector3 facing, Material outline)
    {
        facing.y = 0f;
        facing.Normalize();
        var root = new GameObject("PA_Microphone");
        root.layer = LayerMask.NameToLayer("Interactable");
        root.transform.SetPositionAndRotation(wall + facing * (Size.z * 0.5f), Quaternion.LookRotation(facing));

        var collider = root.AddComponent<BoxCollider>();
        collider.size = Size + new Vector3(0.02f, 0.02f, 0.12f);
        collider.center = new Vector3(0f, 0f, 0.04f);

        float front = Size.z * 0.5f;
        if (PropModels.Place(PropModels.ClosingMic, root.transform) == null)
        {
            // Its own boxes until the model's imported: the case, a speaker grille, the talk
            // button, and a microphone on a short stalk.
            Part(root.transform, "Case", Vector3.zero, Size, new Color(0.88f, 0.85f, 0.76f));
            Part(root.transform, "Grille", new Vector3(0f, 0.075f, front + 0.004f), new Vector3(0.16f, 0.07f, 0.006f), new Color(0.09f, 0.09f, 0.1f));
            Part(root.transform, "Talk button", new Vector3(0f, -0.07f, front + 0.012f), new Vector3(0.09f, 0.06f, 0.022f), new Color(0.78f, 0.2f, 0.2f));
            Part(root.transform, "Stalk", new Vector3(0.085f, 0.02f, front + 0.06f), new Vector3(0.012f, 0.012f, 0.12f), new Color(0.12f, 0.12f, 0.13f));
            Part(root.transform, "Microphone", new Vector3(0.085f, 0.02f, front + 0.13f), new Vector3(0.035f, 0.035f, 0.05f), new Color(0.2f, 0.2f, 0.22f));
        }

        var mic = root.AddComponent<ClosingMic>();
        OutlineHighlight outlineHighlight = root.GetComponent<OutlineHighlight>();
        outlineHighlight.outlineMaterial = outline;
        outlineHighlight.shape = OutlineHighlight.OutlineShape.BoundingBox;

        // The lamp is always its own, so it can light and blink.
        GameObject lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lampGo.name = "Lamp";
        lampGo.transform.SetParent(root.transform, false);
        lampGo.transform.localPosition = new Vector3(-0.095f, -0.07f, front + 0.01f);
        lampGo.transform.localScale = Vector3.one * 0.026f;
        CctvWall.Drop(lampGo.GetComponent<Collider>());
        mic.lamp = lampGo.GetComponent<Renderer>();
        mic.amber = KarenProps.Emissive(new Color(1f, 0.6f, 0.08f), 4f);
        mic.green = KarenProps.Emissive(new Color(0.15f, 1f, 0.3f), 3f);
        mic.dim = KarenProps.Lit(new Color(0.22f, 0.2f, 0.16f));
        mic.Show(0);
        return mic;
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

    // 0 dim, 1 amber, 2 green.
    void Show(int state)
    {
        if (shown == state || lamp == null) return;
        shown = state;
        lamp.sharedMaterial = state == 2 ? green : state == 1 ? amber : dim;
    }

    void Update()
    {
        ShiftManager shift = Shift;
        if (shift == null || !shift.IsShiftActive) { Show(0); return; }
        if (shift.ClosingAnnounced) Show(2);
        else if (shift.ClosingTime) Show(Mathf.Repeat(Time.time, 1f) < 0.5f ? 1 : 0);
        else Show(0);
    }

    // ---- the player --------------------------------------------------------------------------

    // What pressing E would do right now, in words. Empty: it would make the announcement.
    public static string Refusal(ShiftManager shift, PaSystem pa)
    {
        if (shift == null || !shift.IsShiftActive) return "The PA microphone";
        if (shift.ClosingAnnounced) return "The store is closed";
        if (!shift.ClosingTime) return "Not closing time yet";
        if (pa != null && !pa.Powered) return "The PA is switched off";
        return string.Empty;
    }

    public override string GetPrompt()
    {
        string no = Refusal(Shift, Pa);
        return no.Length == 0 ? "Announce that the store is closed" : no;
    }

    public override void Interact(PlayerInteract player) => Announce();

    // Press the button and say it. False when it isn't time, it's been said, or the PA is off.
    public bool Announce()
    {
        ShiftManager shift = Shift;
        PaSystem pa = Pa;
        if (Refusal(shift, pa).Length > 0)
        {
            if (shift != null && shift.AwaitingAnnouncement) OneShotAudio.PlayAt(ProceduralAudio.ErrorBuzz(), transform.position, 0.6f);
            return false;
        }
        if (!shift.AnnounceClosing()) return false;
        if (pa != null) pa.SayAs(Speaker, Words);
        // She hears the microphone click where it is, like any other thing you use.
        NoiseBus.Emit(transform.position, 0.5f, NoiseKind.Environmental, NoiseAuthor.Player);
        KarenNarrator.Say(StoryKind.Store, "You announced that the store is closed. No more customers will come in.", transform.position);
        Show(2);
        return true;
    }
}
