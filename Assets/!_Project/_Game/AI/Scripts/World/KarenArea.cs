using System.Collections.Generic;
using Kehai.Store;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Where Karen's body can go: her part of the building only — indoors, the backstreet and
    // the pavement (StoreMap.KarenWorksIn). Picking targets inside isn't enough on its own: a
    // path between two of her places, the pavement and the backstreet say, could still run
    // round the building through an alley. So everything outside her part is marked as its own
    // NavMesh area, and her agent's area mask leaves it out. Shoppers and you walk everywhere,
    // as before.
    //
    // The area is an unnamed NavMesh area index: naming it would mean ProjectSettings, which
    // this repository doesn't carry. The marks are boxes along StoreMap.AreaAt's lines, made in
    // code at load and baked in once (the maze's rebakes keep them, as children of the surface).
    public static class KarenArea
    {
        public const int OutsideArea = 3;
        public static int AgentMask => NavMesh.AllAreas & ~(1 << OutsideArea);
        const string RootName = "KAREN_OutsideArea";
        const float Far = 1000f;

        // The parts of the map that aren't hers, as boxes: the street and parking lot past the
        // pavement's edge, the two alleys, and the yard behind the stockroom.
        public static Bounds[] OutsideBoxes()
        {
            float edge = StoreMap.PavementEdge;
            return new[]
            {
                Box(-Far, Far, edge, Far),            // the street and the parking lot
                Box(-Far, 10.5f, -Far, edge),         // the west alley
                Box(85.5f, Far, -Far, edge),          // the east alley
                Box(10.5f, 85.5f, -Far, -189.5f),     // the south yard
            };
        }

        static Bounds Box(float x0, float x1, float z0, float z1)
        {
            var b = new Bounds();
            b.SetMinMax(new Vector3(x0, -20f, z0), new Vector3(x1, 40f, z1));
            return b;
        }

        public static bool Installed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Installed = false;

        // Marks the outside on the store's NavMesh and rebakes it, once per load. Returns what
        // it did, for the thought log.
        public static string Install()
        {
            NavMeshSurface surface = KarenWorld.StoreSurface();
            if (surface == null) return "no NavMeshSurface covers the store: her body isn't kept inside";

            Transform parent = surface.collectObjects == CollectObjects.Children ? surface.transform : null;
            GameObject root = parent != null ? parent.Find(RootName)?.gameObject : GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                if (parent != null) root.transform.SetParent(parent, false);
                foreach (Bounds box in OutsideBoxes())
                {
                    var go = new GameObject("Outside");
                    go.transform.SetParent(root.transform, false);
                    var volume = go.AddComponent<NavMeshModifierVolume>();
                    Transform t = go.transform;
                    Vector3 scale = t.lossyScale;
                    volume.center = t.InverseTransformPoint(box.center);
                    volume.size = new Vector3(box.size.x / Mathf.Abs(scale.x), box.size.y / Mathf.Abs(scale.y), box.size.z / Mathf.Abs(scale.z));
                    volume.area = OutsideArea;
                }
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            using (new StoreRebake(surface)) surface.BuildNavMesh();
            Installed = true;
            return $"her body is kept to her part of the building: NavMesh area {OutsideArea} marks the outside (rebaked in {watch.ElapsedMilliseconds} ms)";
        }

        // The nearest point in her part of the building: where she goes when something would
        // send her outside it. Points already inside come back unchanged.
        public static Vector3 Clamp(StoreMap map, Vector3 p)
        {
            if (map == null || map.CellCount == 0) return p;
            int cell = map.CellAt(p);
            if (cell >= 0 ? map.IsKarens(cell) : StoreMap.KarenWorksIn(StoreMap.AreaAt(p))) return p;
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int c = 0; c < map.CellCount; c++)
            {
                if (!map.IsKarens(c)) continue;
                Vector3 d = map.CellPosition[c] - p;
                float sqr = d.x * d.x + d.z * d.z;
                if (sqr < bestSqr) { bestSqr = sqr; best = c; }
            }
            return best >= 0 ? map.CellPosition[best] : p;
        }
    }

    // A runtime rebake of the store's NavMesh, as the maze and KarenArea need it. For its length
    // the bake reads physics colliders (the store's floor is a statically combined render mesh,
    // which isn't readable in a build, or headless, and a bake from render meshes comes back
    // without a floor), leaves out shoppers and items (Interactable) and anything marked
    // NavIgnore, as the original bake did, and keeps people out of it.
    public sealed class StoreRebake : System.IDisposable
    {
        readonly NavMeshSurface surface;
        readonly NavMeshCollectGeometry geometry;
        readonly int layers;
        readonly List<NavMeshModifier> ignored;
        readonly List<(MeshCollider, BoxCollider)> standIns;

        public StoreRebake(NavMeshSurface surface)
        {
            this.surface = surface;
            ignored = IgnorePeople();
            standIns = BoxStandIns();
            geometry = surface.useGeometry;
            layers = surface.layerMask;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = layers & ~(1 << LayerMask.NameToLayer("Interactable"));
        }

        public void Dispose()
        {
            surface.useGeometry = geometry;
            surface.layerMask = layers;
            foreach (NavMeshModifier m in ignored) if (m != null) Object.DestroyImmediate(m);
            foreach (var (mesh, box) in standIns)
            {
                if (box != null) Object.DestroyImmediate(box);
                if (mesh != null) mesh.enabled = true;
            }
        }

        // A few props (racks, bins) collide through meshes that aren't imported readable, and a
        // runtime bake can't read those in a build. For the length of the bake each stands in
        // as the box around its mesh — plenty for a NavMesh.
        static List<(MeshCollider, BoxCollider)> BoxStandIns()
        {
            var list = new List<(MeshCollider, BoxCollider)>();
            foreach (MeshCollider mc in Object.FindObjectsByType<MeshCollider>())
            {
                if (!mc.enabled || mc.isTrigger || mc.sharedMesh == null || mc.sharedMesh.isReadable) continue;
                var box = mc.gameObject.AddComponent<BoxCollider>();
                box.center = mc.sharedMesh.bounds.center;
                box.size = mc.sharedMesh.bounds.size;
                mc.enabled = false;
                list.Add((mc, box));
            }
            return list;
        }

        // People aren't architecture: keep every body out of the bake, or each would leave a
        // hole in the floor where it happened to be standing.
        static List<NavMeshModifier> IgnorePeople()
        {
            var added = new List<NavMeshModifier>();
            var bodies = new List<Component>();
            bodies.AddRange(Object.FindObjectsByType<CharacterController>());
            bodies.AddRange(Object.FindObjectsByType<NavMeshAgent>());
            foreach (Component body in bodies)
            {
                if (body.GetComponent<NavMeshModifier>() != null) continue;
                var m = body.gameObject.AddComponent<NavMeshModifier>();
                m.ignoreFromBuild = true;
                added.Add(m);
            }
            return added;
        }
    }
}
