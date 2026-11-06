using UnityEngine;

namespace Kehai.Karen
{
    // Developer aids only. Always off in a player release and in unattended runs;
    // each Play session starts off, independently of old floor-cone preferences.
    public sealed class KarenFx : MonoBehaviour
    {
        public static bool StepsAudible => Enabled;
        public static bool ConeVisible => Enabled;

        public static bool Enabled
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return show && !Application.isBatchMode;
#else
                return false;
#endif
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static bool show;
        static KarenFx instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            show = false;
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Application.isBatchMode || instance != null) return;
            var go = new GameObject("Karen developer effects");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<KarenFx>();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) show = !show;
        }
#endif
    }
}
