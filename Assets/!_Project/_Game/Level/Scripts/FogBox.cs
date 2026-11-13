using Kehai.Karen;
using UnityEngine;

// The fridge control box (Karen 0.3.0, Fog; build plan B2.1). It hangs on the backstreet wall by
// the breaker box. While any room is fogged its amber lamp blinks; hold E for twenty seconds
// (BackstreetFix.HoldSeconds, 0.4.1: three until then) and the fridges reset and the fog thins
// (FogState.Clear). It's where the fog's chore is fixed, so
// it tells FogState where it is (FogState.ResetBox), for her FixAt and the bots.
public sealed class FogBox : HighlightInteractable, IHoldInteractable
{
    public const float HoldSeconds = BackstreetFix.HoldSeconds;
    public static readonly Vector3 Size = new Vector3(0.40f, 0.50f, 0.14f);   // w, h, d in metres

    Renderer lamp;
    Material lampOn, lampOff;
    bool lit;

    // Builds it with its back against a wall at `wall`, facing `facing` (out into the room).
    public static FogBox Build(Vector3 wall, Vector3 facing, Material outline)
    {
        facing.y = 0f;
        facing.Normalize();
        var root = new GameObject("Fridge_Control_Box");
        root.layer = LayerMask.NameToLayer("Interactable");
        root.transform.SetPositionAndRotation(wall + facing * (Size.z * 0.5f), Quaternion.LookRotation(facing));

        var collider = root.AddComponent<BoxCollider>();
        collider.size = Size + new Vector3(0.02f, 0.02f, 0.04f);

        if (PropModels.Place(PropModels.FogBox, root.transform) == null)
        {
            // Its own boxes until the model's imported: the box, its door, the red reset knob.
            Part(root.transform, "Body", Vector3.zero, Size, new Color(0.33f, 0.42f, 0.47f));
            Part(root.transform, "Door", new Vector3(0f, 0f, Size.z * 0.5f + 0.004f), new Vector3(0.36f, 0.46f, 0.008f), new Color(0.27f, 0.35f, 0.39f));
            Part(root.transform, "Label", new Vector3(-0.035f, 0.17f, Size.z * 0.5f + 0.01f), new Vector3(0.29f, 0.075f, 0.004f), new Color(0.95f, 0.95f, 0.93f));
            Part(root.transform, "Knob plate", new Vector3(0f, -0.03f, Size.z * 0.5f + 0.01f), new Vector3(0.12f, 0.12f, 0.006f), new Color(0.95f, 0.74f, 0.05f));
            Part(root.transform, "Knob", new Vector3(0f, -0.03f, Size.z * 0.5f + 0.03f), new Vector3(0.1f, 0.022f, 0.03f), new Color(0.72f, 0.06f, 0.05f));
        }

        var box = root.AddComponent<FogBox>();
        OutlineHighlight outlineHighlight = root.GetComponent<OutlineHighlight>();
        outlineHighlight.outlineMaterial = outline;
        outlineHighlight.shape = OutlineHighlight.OutlineShape.BoundingBox;

        // The lamp, laid over the model's: amber, blinking while the fog's on.
        GameObject lampGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        lampGo.name = "Lamp";
        lampGo.transform.SetParent(root.transform, false);
        lampGo.transform.localPosition = new Vector3(0.155f, 0.17f, Size.z * 0.5f + 0.012f);
        lampGo.transform.localScale = Vector3.one * 0.034f;
        Object.Destroy(lampGo.GetComponent<Collider>());
        box.lamp = lampGo.GetComponent<Renderer>();
        box.lampOn = KarenProps.Emissive(new Color(1f, 0.55f, 0.05f), 4f);
        box.lampOff = KarenProps.Lit(new Color(0.35f, 0.2f, 0.05f));
        box.lamp.sharedMaterial = box.lampOff;
        return box;
    }

    static void Part(Transform parent, string name, Vector3 local, Vector3 size, Color colour)
    {
        GameObject go = KarenProps.Box(name, parent.position, size, colour, parent, collider: false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;
        go.layer = parent.gameObject.layer;
    }

    void OnEnable() => FogState.ResetBox = transform;

    void OnDisable()
    {
        if (FogState.ResetBox == transform) FogState.ResetBox = null;
    }

    void Update()
    {
        bool on = FogState.Any && Mathf.Repeat(Time.time, 1f) < 0.5f;
        if (on == lit || lamp == null) return;
        lit = on;
        lamp.sharedMaterial = on ? lampOn : lampOff;
    }

    // ---- the player ---------------------------------------------------------------------

    public override void Interact(PlayerInteract player)
    {
        // A tap does nothing: it's a hold, so it can't be done by accident in passing.
    }

    public override string GetPrompt() => FogState.Any ? BackstreetFix.Prompt("reset the fridges") : "Fridge control: all normal";

    public bool CanHold(PlayerInteract player) => FogState.Any;
    public float HoldDuration => HoldSeconds;

    public void OnHoldProgress(float normalised) => BackstreetFix.WorkNoise(transform.position, normalised);

    public void OnHoldCancelled() { }

    public void OnHoldComplete(PlayerInteract player)
    {
        FogState.Clear();   // her Ledger hears of it from FogState.Cleared (fog_reset)
        BackstreetFix.DoneNoise(transform.position);
    }
}
