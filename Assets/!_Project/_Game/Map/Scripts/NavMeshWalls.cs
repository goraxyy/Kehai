using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Kehai.Store
{
    // The building's walls are 3 cm boxes — thinner than a NavMesh voxel, so the bake left
    // them out and every NavMesh path ran straight through them: customers and Karen could
    // walk through walls, and anything following a path into one (the eval's agent driver)
    // stuck fast. This carves each thin wall back into the NavMesh when the store loads.
    //
    // From code rather than a re-bake because the scene isn't in version control; carving
    // only affects the runtime NavMesh, and doorways stay open because doors aren't walls.
    // The same rule decides what a wall is as StoreMap's physics sweeps: static, solid, and
    // not a door, an item, a person or anything else that moves.
    public static class NavMeshWalls
    {
        public const float MinHeight = 1.5f;        // taller than this, from the floor up
        public const float MaxThickness = 0.2f;     // thinner than this in one direction
        public const float MinLength = 0.5f;        // and at least this long in the other
        public const float CarveThickness = 0.3f;   // what the NavMesh treats it as

        public static int LastCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int n = Carve(scene);
            if (n > 0 && Application.isBatchMode)
                Debug.Log($"NavMeshWalls: carved {n} thin walls into the NavMesh in {scene.name}");
        }

        public static int Carve(Scene scene)
        {
            int count = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (BoxCollider box in root.GetComponentsInChildren<BoxCollider>())
            {
                if (!IsWall(box)) continue;
                if (box.GetComponent<NavMeshObstacle>() != null) continue;

                // Obstacle size is in the collider's local space: thicken the thin axis only,
                // so the ends — and the doorways between walls — stay where they are.
                Vector3 scale = box.transform.lossyScale;
                Vector3 size = box.size;
                Vector3 world = new Vector3(Mathf.Abs(size.x * scale.x), Mathf.Abs(size.y * scale.y), Mathf.Abs(size.z * scale.z));
                int thin = world.x <= world.z ? 0 : 2;
                if (world[thin] < CarveThickness && Mathf.Abs(scale[thin]) > 1e-5f)
                    size[thin] = CarveThickness / Mathf.Abs(scale[thin]);

                var obstacle = box.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = box.center;
                obstacle.size = size;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
                count++;
            }
            LastCount = count;
            return count;
        }

        public static bool IsWall(BoxCollider c)
        {
            if (!c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy) return false;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;

            Bounds b = c.bounds;
            float thin = Mathf.Min(b.size.x, b.size.z);
            float length = Mathf.Max(b.size.x, b.size.z);
            if (b.size.y < MinHeight || thin > MaxThickness || length < MinLength) return false;

            // Only walls that stand on the walkable floor.
            Vector3 foot = new Vector3(b.center.x, b.min.y, b.center.z);
            if (!NavMesh.SamplePosition(foot, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return false;
            if (b.min.y > hit.position.y + 0.6f) return false;

            return c.GetComponentInParent<HingeDoor>() == null
                && c.GetComponentInParent<AutoDoubleDoor>() == null
                && c.GetComponentInParent<Item>() == null
                && c.GetComponentInParent<NavMeshAgent>() == null
                && c.GetComponentInParent<IMapTransient>() == null;
        }
    }
}
