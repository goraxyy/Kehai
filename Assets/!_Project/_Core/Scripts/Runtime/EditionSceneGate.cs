using UnityEngine;
using UnityEngine.SceneManagement;

// Runs before ordinary scene scripts. Training scenes are stripped at build time as well.
[DefaultExecutionOrder(-32000)]
public sealed class EditionSceneGate : MonoBehaviour
{
    public bool trainingPreview;

#if UNITY_EDITOR
    KehaiEdition? previousEdition;
#endif

    void Awake()
    {
#if UNITY_EDITOR
        if (trainingPreview)
        {
            previousEdition = Edition.EditorOverride;
            Edition.EditorOverride = KehaiEdition.Training;
        }
#endif
        Apply(gameObject.scene);
    }

#if UNITY_EDITOR
    void OnDestroy()
    {
        if (trainingPreview && Edition.EditorOverride == KehaiEdition.Training)
            Edition.EditorOverride = previousEdition;
    }
#endif

    public static void Apply(Scene scene)
    {
        if (!Edition.Training) return;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true)) c.gameObject.SetActive(false);
            foreach (TMPro.TMP_Text t in root.GetComponentsInChildren<TMPro.TMP_Text>(true)) t.gameObject.SetActive(false);
            foreach (AudioSource a in root.GetComponentsInChildren<AudioSource>(true)) { a.playOnAwake = false; a.enabled = false; }
        }
    }
}
