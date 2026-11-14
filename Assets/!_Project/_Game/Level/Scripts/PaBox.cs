using Kehai.Karen;
using UnityEngine;

// The PA box (Karen 0.3.0; build plan B2.2): on the staff room wall by the time clock, a lever
// that switches the store's PA off and on. Off, she has no voice: no announcements, no sounds
// through the speakers. It costs nothing (the breaker box's old jammer cost a light circuit),
// and the PA comes back on at the shift's end. Her Ledger hears of it from PaSystem.PowerChanged.
public sealed class PaBox : HighlightInteractable
{
    public static readonly Vector3 Size = new Vector3(0.28f, 0.36f, 0.11f);   // w, h, d in metres

    Renderer onLamp, offLamp;
    Transform lever;
    Material green, greenDim, red, redDim;
    bool shown;
    bool shownSet;

    static PaSystem Pa => KarenWorld.Instance != null ? KarenWorld.Instance.Pa : null;

    public static PaBox Build(Vector3 wall, Vector3 facing, Material outline)
    {
        facing.y = 0f;
        facing.Normalize();
        var root = new GameObject("PA_Box");
        root.layer = LayerMask.NameToLayer("Interactable");
        root.transform.SetPositionAndRotation(wall + facing * (Size.z * 0.5f), Quaternion.LookRotation(facing));

        var collider = root.AddComponent<BoxCollider>();
        collider.size = Size + new Vector3(0.02f, 0.02f, 0.05f);

        float front = Size.z * 0.5f;
        GameObject model = PropModels.Place(PropModels.PaBox, root.transform);
        if (model == null)
        {
            Part(root.transform, "Body", Vector3.zero, Size, new Color(0.9f, 0.86f, 0.74f));
            Part(root.transform, "Grille", new Vector3(0f, 0.07f, front + 0.004f), new Vector3(0.11f, 0.11f, 0.006f), new Color(0.08f, 0.08f, 0.09f));
            Part(root.transform, "Switch plate", new Vector3(0f, -0.08f, front + 0.005f), new Vector3(0.07f, 0.1f, 0.01f), new Color(0.8f, 0.8f, 0.82f));
        }

        var box = root.AddComponent<PaBox>();
        OutlineHighlight outlineHighlight = root.GetComponent<OutlineHighlight>();
        outlineHighlight.outlineMaterial = outline;
        outlineHighlight.shape = OutlineHighlight.OutlineShape.BoundingBox;

        // The lever and the two lamps are always its own, so they can move and light up.
        GameObject leverGo = KarenProps.Box("Lever", root.transform.position, new Vector3(0.014f, 0.06f, 0.014f), new Color(0.06f, 0.06f, 0.06f), root.transform, collider: false);
        leverGo.transform.localPosition = new Vector3(0f, -0.065f, front + 0.03f);
        box.lever = leverGo.transform;
        box.green = KarenProps.Emissive(new Color(0.15f, 1f, 0.25f), 3f);
        box.greenDim = KarenProps.Lit(new Color(0.05f, 0.25f, 0.08f));
        box.red = KarenProps.Emissive(new Color(1f, 0.08f, 0.05f), 3f);
        box.redDim = KarenProps.Lit(new Color(0.3f, 0.04f, 0.04f));
        float side = PropModels.Side(model);
        box.onLamp = Lamp(root.transform, "ON lamp", new Vector3(-0.075f * side, -0.05f, front + 0.008f));
        box.offLamp = Lamp(root.transform, "OFF lamp", new Vector3(0.075f * side, -0.05f, front + 0.008f));
        box.Show(true);
        return box;
    }

    static Renderer Lamp(Transform parent, string name, Vector3 local)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = Vector3.one * 0.024f;
        Object.Destroy(go.GetComponent<Collider>());
        return go.GetComponent<Renderer>();
    }

    static void Part(Transform parent, string name, Vector3 local, Vector3 size, Color colour)
    {
        GameObject go = KarenProps.Box(name, parent.position, size, colour, parent, collider: false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.identity;
        go.layer = parent.gameObject.layer;
    }

    void Show(bool on)
    {
        if (shownSet && shown == on) return;
        shownSet = true;
        shown = on;
        if (onLamp != null) onLamp.sharedMaterial = on ? green : greenDim;
        if (offLamp != null) offLamp.sharedMaterial = on ? redDim : red;
        if (lever != null) lever.localRotation = Quaternion.Euler(on ? -35f : 35f, 0f, 0f);
    }

    // The PA comes back on by itself at the shift's end: keep the box showing the truth.
    void Update()
    {
        PaSystem pa = Pa;
        Show(pa == null || pa.Powered);
    }

    public override void Interact(PlayerInteract player)
    {
        PaSystem pa = Pa;
        if (pa == null) return;
        pa.SetPowered(!pa.Powered);
        OneShotAudio.PlayAt(ProceduralAudio.BreakerThrow(), transform.position);
        NoiseBus.Emit(transform.position, 0.4f, NoiseKind.Environmental, NoiseAuthor.Player);
        Show(pa.Powered);
    }

    public override string GetPrompt()
    {
        PaSystem pa = Pa;
        if (pa == null) return "The PA";
        return pa.Powered ? "Switch the PA off" : "Switch the PA on";
    }
}
