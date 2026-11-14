using UnityEngine;

// Karen's 0.3.0 props as models (build plan B6.1): her camera, the fridge control box and the
// PA box. 0.4.1 adds the PA microphone, the camera box, the store's cameras and the office
// screen. The Blender models live on the Mac (~/Desktop/KehaiRelated/Props), never in git; the
// editor menu Kehai/Props/Import Karen's Props copies them into Resources/KarenProps, which git
// ignores. Until then — and in tests and batch runs on a clean checkout — every prop builds
// its own boxes, so nothing depends on the art being there.
public static class PropModels
{
    public const string Folder = "KarenProps";
    public const string Camera = "karen_camera", FogBox = "fridge_control_box", PaBox = "pa_box";
    public const string ClosingMic = "pa_microphone", CameraBox = "camera_box", StoreCamera = "store_camera", CctvScreen = "cctv_screen";

    // Every model, for the importer and the tests.
    public static readonly string[] All = { Camera, FogBox, PaBox, ClosingMic, CameraBox, StoreCamera, CctvScreen };

    // Whether the model has been imported.
    public static bool Has(string name) => Resources.Load<GameObject>(Folder + "/" + name) != null;

    // The model under `parent`, facing the parent's forward, colliders stripped: the prop keeps
    // its own. Null without the model. Made in Blender facing −Y, an imported model already faces
    // +Z, the parent's forward: until 0.4.1 it was turned round as well, so every prop showed the
    // wall its back (seen in 0.4.1's pictures, the first time the models were imported).
    // A model is one mesh, so the props light and blink their lamps with small objects of
    // their own, laid over the model's lamp.
    // Which way a model's left and right fall in the prop's own frame: Blender's +X shows on the
    // viewer's right, which is the prop's −X, as it faces the viewer. The props lay their own
    // lamps over the model's (a model is one mesh and can't blink), so a lamp at Blender's x sits
    // at prop x × Side(model). Without a model the prop's own boxes keep the code's layout.
    public static float Side(GameObject model) => model != null ? -1f : 1f;

    public static GameObject Place(string name, Transform parent)
    {
        GameObject model = Resources.Load<GameObject>(Folder + "/" + name);
        if (model == null || parent == null) return null;
        GameObject go = Object.Instantiate(model, parent, false);
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
        }
        go.layer = parent.gameObject.layer;
        return go;
    }
}
