#if UNITY_EDITOR
using Kehai.Eval;
using Kehai.Karen;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Sandbox
{
    // The sandbox island in the store scene (0.5.2, C11.11, the owner's call of 21 November): one
    // island beside the store where you test her tricks and the interactions, with the models
    // island moved beside it and walkable ground joining the store, the sandbox island and the
    // models island. All built by code when the store loads in the editor; nothing is saved into
    // the scene.
    //
    //   - Every editor Play of the store: the island's ground, and the models island moved down
    //     onto the road's north edge (its floor ran from z 0; the road ends at z -43.5, a gap
    //     with no ground the player fell into).
    //   - Kehai → Sandbox → Island: the same, with a quiet store (no shift, her store self off)
    //     and the sight area on the island. Pursuit, tricks and interactions follow (C11.11),
    //     with her own test map of the island (C11.13).
    //
    // Where (world metres): the road in front of the store runs x -7..105, z -53.5..-43.5; the
    // store's own ground ends at z -49.9. The models island's floor is x 0..120 and, moved, z
    // -43.5..76.5. The sandbox island is x -80..0, z -49.9..30.1: it meets the models island at
    // x 0 and the road and the store's ground along its south edge.
    public static class SandboxIsland
    {
        public const string SessionKey = "Kehai.Sandbox.Island";
        public const string StoreScene = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
        public const string RootName = "Sandbox island";
        public const string ModelsIslandName = "Models_Island";
        public static readonly Vector3 ModelsShift = new Vector3(0f, 0f, -43.5f);
        public static readonly Vector3 GroundCentre = new Vector3(-40f, -0.1f, -9.9f);
        public static readonly Vector3 GroundSize = new Vector3(80f, 0.2f, 80f);
        public static readonly Vector3 SightAt = new Vector3(-58f, 0f, -24f);

        public static bool Active { get; private set; }   // the quiet-store sandbox, opened from the menu

        static KarenConfig previousConfig;
        static bool previousDisabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            Active = SessionState.GetBool(SessionKey, false);
            if (!Active) return;
            // The quiet store: her store self off and no shift; the career is never read or written.
            previousConfig = KarenBootstrap.Override;
            previousDisabled = KarenBootstrap.Disabled;
            KarenBootstrap.Override = TricksSandbox.NewConfig();
            KarenBootstrap.Disabled = true;
            KehaiEnv.Ensure();   // keeps the title menu closed
        }

        public static void EndSession()
        {
            if (!Active) return;
            KarenBootstrap.Override = previousConfig;
            KarenBootstrap.Disabled = previousDisabled;
            Active = false;
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single || scene.path != StoreScene) return;
            Build(scene, Active);
        }

        public static GameObject Build(Scene scene, bool withAreas)
        {
            foreach (GameObject top in scene.GetRootGameObjects())
                if (top.name == RootName) return top;
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // The models island, down onto the road's north edge.
            foreach (GameObject top in scene.GetRootGameObjects())
                if (top.name == ModelsIslandName) top.transform.position += ModelsShift;

            SandboxKit.Box(root.transform, "Ground", GroundCentre, GroundSize, SandboxKit.Floor);

            if (withAreas)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) player.GetOrAdd<PlayerPresence>();
                var sight = new GameObject("Sight area");
                sight.transform.SetParent(root.transform, false);
                sight.transform.position = SightAt;
                sight.SetActive(false);   // placed and set up before its Awake builds the room
                var area = sight.AddComponent<SightSandbox>();
                area.buildPlayer = false;
                sight.SetActive(true);
            }
            Physics.SyncTransforms();
            return root;
        }
    }
}
#endif
