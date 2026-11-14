using UnityEngine;
using UnityEngine.SceneManagement;

// Runs before ordinary scene scripts. Training scenes are stripped at build time as well.
[DefaultExecutionOrder(-32000)]
public sealed class EditionSceneGate : MonoBehaviour
{
    void Awake() => Apply(gameObject.scene);
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
