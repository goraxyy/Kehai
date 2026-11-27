#if UNITY_EDITOR
using Kehai.Eval;
using Kehai.Karen;
using Kehai.Store;
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
        public static readonly Vector3 InteractionsAt = new Vector3(-58f, 0f, 10f);
        // The standalone pursuit room's place moved by whole 15 m steps (her map's 5 m tiles and
        // 1.5 m cells), so her regions there are cut the same way and the pursuit checks meet the
        // same aisles: (55, -148) - (90, 0, -150).
        public static readonly Vector3 PursuitAt = PursuitSandbox.StandaloneCentre + new Vector3(-90f, 0f, 150f);
        // Her test map's bounds: the island's ground, floor to a few metres up (C11.13), nudged so
        // her 1.5 m cells fall on the pursuit room exactly as they do in the standalone room. Her
        // map's grid starts at the bounds' corner, so a grid cut 0.2 and 0.4 m off moved her search
        // spots onto your hiding place (the island pursuit checks failed 2 of 4 so, 23 November).
        public static Bounds MapBounds
        {
            get
            {
                Vector3 min = new Vector3(GroundCentre.x - GroundSize.x * 0.5f, 0f, GroundCentre.z - GroundSize.z * 0.5f);
                float half = StoreMap.CellSize * 0.5f;   // StoreMap widens the bounds by half a cell each side
                min.x += GridNudge(min.x - half - PursuitAt.x, PursuitGrid.x);
                min.z += GridNudge(min.z - half - PursuitAt.z, PursuitGrid.y);
                return new Bounds(new Vector3(min.x + GroundSize.x * 0.5f, 2f, min.z + GroundSize.z * 0.5f), new Vector3(GroundSize.x, 6f, GroundSize.z));
            }
        }

        // Where her map's grid starts from the standalone pursuit room's centre, as measured there
        // (PursuitSandboxChecks logs it; run 20261123-015528): the room's NavMesh sets it.
        public static readonly Vector2 PursuitGrid = new Vector2(-14.08f, -14.08f);

        // The smallest move that puts a grid starting `from` the room's centre in step with `want`.
        public static float GridNudge(float from, float want)
        {
            float cell = StoreMap.CellSize;
            float off = Mathf.Repeat(from - want, cell);
            return off > cell * 0.5f ? cell - off : -off;
        }
        public const string PursuitChecksKey = SessionKey + ".pursuitChecks";
        public const string InteractionChecksKey = SessionKey + ".interactionChecks";

        public static bool Active { get; private set; }   // the quiet-store sandbox, opened from the menu

        static KarenConfig previousConfig;
        static bool previousDisabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            Active = SessionState.GetBool(SessionKey, false);
            Debug.Log($"SandboxIsland: installed, sandbox {(Active ? "on" : "off")}");
            if (!Active) return;
            // The quiet store: her store self off and no shift; the career is never read or written.
            previousConfig = KarenBootstrap.Override;
            previousDisabled = KarenBootstrap.Disabled;
            KarenBootstrap.Override = TricksSandbox.NewConfig();
            KarenBootstrap.Disabled = true;
            KehaiEnv.Ensure();   // keeps the title menu closed
            StoreMap.UseTestArea(MapBounds);   // her test map: the island only
            PursuitSandbox.Centre = PursuitAt;
        }

        public static void EndSession()
        {
            if (!Active) return;
            KarenBootstrap.Override = previousConfig;
            KarenBootstrap.Disabled = previousDisabled;
            StoreMap.UseTestArea(null);
            PursuitSandbox.Centre = PursuitSandbox.StandaloneCentre;
            Active = false;
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"SandboxIsland: scene loaded '{scene.path}' ({mode})");
            if (scene.path != StoreScene) return;
            Build(scene, Active);
        }

        // The store's own NavMeshSurface sits on the models island and bakes a volume around the
        // store. Moving the island dragged that volume 43.5 m south with it, and the parking lot
        // fell out of her map in every editor Play of the store: 3,428 cells against the
        // player's and the Training scene's 4,777 (0.5.2, C8.1, 27 November). Each volume stays
        // where it was in the world.
        public static void MoveKeepingBakeVolumes(Transform root, Vector3 shift)
        {
            var surfaces = root.GetComponentsInChildren<Unity.AI.Navigation.NavMeshSurface>(true);
            var centres = new Vector3[surfaces.Length];
            for (int i = 0; i < surfaces.Length; i++) centres[i] = surfaces[i].transform.TransformPoint(surfaces[i].center);
            root.position += shift;
            for (int i = 0; i < surfaces.Length; i++) surfaces[i].center = surfaces[i].transform.InverseTransformPoint(centres[i]);
        }

        public static GameObject Build(Scene scene, bool withAreas)
        {
            foreach (GameObject top in scene.GetRootGameObjects())
                if (top.name == RootName) return top;
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // The models island, down onto the road's north edge.
            foreach (GameObject top in scene.GetRootGameObjects())
                if (top.name == ModelsIslandName) MoveKeepingBakeVolumes(top.transform, ModelsShift);

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

                // The pursuit area: her real brain, on her test map of the island.
                var pursuit = new GameObject("Pursuit area");
                pursuit.transform.SetParent(root.transform, false);
                pursuit.SetActive(false);
                var chase = pursuit.AddComponent<PursuitSandbox>();
                chase.buildPlayer = false;
                if (SessionState.GetBool(PursuitChecksKey, false)) pursuit.AddComponent<PursuitChecks>();
                pursuit.SetActive(true);

                var interactions = new GameObject("Interactions area");
                interactions.transform.SetParent(root.transform, false);
                interactions.transform.position = InteractionsAt;
                interactions.SetActive(false);
                interactions.AddComponent<SightSandbox>().buildPlayer = false;
                interactions.AddComponent<InteractionSandbox>();
                if (SessionState.GetBool(InteractionChecksKey, false)) interactions.AddComponent<InteractionChecks>();
                interactions.SetActive(true);
                // The rooms have baked their NavMesh: her test map is built from them now.
                StoreMap.Rebuild();
            }
            Physics.SyncTransforms();
            return root;
        }
    }
}
#endif
