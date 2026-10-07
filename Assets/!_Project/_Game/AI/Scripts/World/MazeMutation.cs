using System.Collections.Generic;
using System.Text;
using Kehai.Store;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Aiko
{
    // The shelving is on castors (Aiko.md §5.5). Between shifts Aiko rolls a few bays to a
    // new spot on the 5 m lattice, so the store you walk into is *wrong* and the route you
    // learned is no longer the route.
    //
    // The rule that keeps it fair is checked, not hoped for: after the move, the walk from
    // the front doors to every place the job sends you may not grow by more than 40%, no
    // place may become unreachable, and the store may not gain a new dead-end chokepoint.
    // A move that breaks any of that is rolled back before anyone sees it.
    public static class MazeMutation
    {
        public const float MaxDetour = 1.4f;

        public struct Move
        {
            public ShelfUnit Bay;
            public Vector3 From;
            public Vector3 To;
        }

        // Returns the moves that stuck. `report` is a line per attempt for the thought log.
        // The moves of the last mutation, for the replay recorder's header.
        public static IReadOnlyList<Move> LastMoves { get; private set; } = new List<Move>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LastMoves = new List<Move>();

        public static List<Move> Mutate(AikoRng rng, int wanted, out string report)
        {
            var kept = new List<Move>();
            var log = new StringBuilder();
            NavMeshSurface surface = AikoWorld.StoreSurface();
            if (surface == null)
            {
                report = "no NavMeshSurface covers the store — mutation skipped";
                return kept;
            }

            StoreMap before = StoreMap.Current;
            int chokepointsBefore = CountChokepoints(before);
            Dictionary<string, float> baseline = MeasureRoutes(before);

            var bays = new List<ShelfUnit>();
            foreach (Bay b in before.Bays)
                if (b.Unit != null && StoreMap.AreaAt(b.Position) == "Sales floor") bays.Add(b.Unit);

            var ignored = IgnorePeople();
            var standIns = BoxStandIns();
            NavMeshCollectGeometry geometry = surface.useGeometry;
            int layers = surface.layerMask;
            // Rebake from physics colliders: the store's floor is a statically combined render
            // mesh, which isn't readable in a build (or headless), and a bake from render meshes
            // then comes back without a floor. Colliders are always readable. Shoppers and items
            // (Interactable) and anything marked NavIgnore stay out, as they did in the original bake.
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = layers & ~(1 << LayerMask.NameToLayer("Interactable"));

            int attempts = 0;
            while (kept.Count < wanted && attempts < wanted * 3 && bays.Count > 0)
            {
                attempts++;
                ShelfUnit bay = bays[rng.Range(0, bays.Count)];
                Vector3 from = bay.transform.position;
                Vector3 offset = Offsets[rng.Range(0, Offsets.Length)];
                Vector3 to = from + offset;

                if (StoreMap.AreaAt(to) != "Sales floor" || Occupied(bay, to))
                {
                    log.AppendLine($"  {bay.name} {Short(from)}→{Short(to)}: blocked");
                    continue;
                }

                bay.transform.position = to;
                surface.BuildNavMesh();
                StoreMap after = StoreMap.Rebuild();

                string why = Violation(after, baseline, chokepointsBefore);
                if (why != null)
                {
                    bay.transform.position = from;
                    surface.BuildNavMesh();
                    StoreMap.Rebuild();
                    log.AppendLine($"  {bay.name} {Short(from)}→{Short(to)}: rolled back ({why})");
                    continue;
                }

                kept.Add(new Move { Bay = bay, From = from, To = to });
                bays.Remove(bay);
                baseline = MeasureRoutes(after);
                chokepointsBefore = CountChokepoints(after);
                log.AppendLine($"  {bay.name} {Short(from)}→{Short(to)}: kept");
            }

            surface.useGeometry = geometry;
            surface.layerMask = layers;
            foreach (NavMeshModifier m in ignored) if (m != null) Object.Destroy(m);
            foreach (var (mesh, box) in standIns)
            {
                if (box != null) Object.Destroy(box);
                if (mesh != null) mesh.enabled = true;
            }

            report = $"maze mutation: {kept.Count}/{wanted} moves kept after {attempts} attempts\n" + log;
            LastMoves = kept;
            return kept;
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

        static readonly Vector3[] Offsets =
        {
            new Vector3(5f, 0f, 0f), new Vector3(-5f, 0f, 0f), new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, -5f)
        };

        static string Short(Vector3 p) => $"({p.x:0},{p.z:0})";

        static bool Occupied(ShelfUnit moving, Vector3 to)
        {
            var bounds = new Bounds(to + Vector3.up, new Vector3(4.2f, 2f, 1.2f));
            foreach (Collider c in Physics.OverlapBox(bounds.center, bounds.extents, moving.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(moving.transform)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (c.GetComponentInParent<ShelfUnit>() != null) return true;
                if (c.gameObject.layer == LayerMask.NameToLayer("Floor")) continue;
                if (c.bounds.size.y > 1f) return true;   // walls, counters, fridges
            }
            return false;
        }

        // Walking distance from the front of the store to every place the job sends you.
        static Dictionary<string, float> MeasureRoutes(StoreMap map)
        {
            var result = new Dictionary<string, float>();
            Region lobbyDoor = null;
            foreach (Region r in map.Regions)
                if (r.IsDoor && r.Name.StartsWith("Doors: Lobby - Sales floor")) { lobbyDoor = r; break; }
            if (lobbyDoor == null) return result;

            var path = new NavMeshPath();
            foreach (Landmark l in map.Landmarks)
            {
                if (l.Kind == LandmarkKind.Door || l.Kind == LandmarkKind.AutoDoor || l.Kind == LandmarkKind.CustomerSpawn) continue;
                if (!NavMesh.SamplePosition(l.Position, out NavMeshHit to, 3f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(lobbyDoor.Centroid, to.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                {
                    result[l.Name] = float.PositiveInfinity;
                    continue;
                }
                float length = 0f;
                for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                result[l.Name] = length;
            }
            return result;
        }

        static string Violation(StoreMap after, Dictionary<string, float> baseline, int chokepointsBefore)
        {
            Dictionary<string, float> now = MeasureRoutes(after);
            foreach (KeyValuePair<string, float> pair in baseline)
            {
                if (float.IsInfinity(pair.Value)) continue;
                if (!now.TryGetValue(pair.Key, out float length) || float.IsInfinity(length))
                    return $"{pair.Key} unreachable";
                if (length > pair.Value * MaxDetour)
                    return $"{pair.Key} {length / pair.Value:0.00}× longer";
            }
            if (CountChokepoints(after) > chokepointsBefore + 1) return "new dead end";
            return null;
        }

        static int CountChokepoints(StoreMap map)
        {
            int n = 0;
            foreach (Region r in map.Regions) if (r.IsChokepoint) n++;
            return n;
        }
    }
}
