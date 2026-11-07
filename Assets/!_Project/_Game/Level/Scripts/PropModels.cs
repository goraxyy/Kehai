using UnityEngine;

// Karen's 0.3.0 props as models (build plan B6.1): her camera, the fridge control box and the
// PA box. The Blender models live on the Mac (~/Desktop/KehaiRelated/Props), never in git; the
// editor menu Kehai/Props/Import Karen's Props copies them into Resources/KarenProps, which git
// ignores. Until then — and in tests and batch runs on a clean checkout — every prop builds
// its own boxes, so nothing depends on the art being there.
public static class PropModels
{
    public const string Folder = "KarenProps";
    public const string Camera = "karen_camera", FogBox = "fridge_control_box", PaBox = "pa_box";

    // Whether the model has been imported.
    public static bool Has(string name) => Resources.Load<GameObject>(Folder + "/" + name) != null;

    // The model under `parent`, facing the parent's forward (the models are made facing -Z, as
    // the products are), colliders stripped: the prop keeps its own. Null without the model.
    // A model is one mesh, so the props light and blink their lamps with small objects of
    // their own, laid over the model's lamp.
    public static GameObject Place(string name, Transform parent)
    {
        GameObject model = Resources.Load<GameObject>(Folder + "/" + name);
        if (model == null || parent == null) return null;
        GameObject go = Object.Instantiate(model, parent, false);
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
        {
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
        }
        go.layer = parent.gameObject.layer;
        return go;
    }
}
