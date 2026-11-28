#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The roadside art over the mini store (Codex's, 27 November; adopted into the game 28
    // November). The art is a render-only package in the private art folder
    // (Assets/KehaiRoadsideArt, never in the repository): its shelves are matched to the mini
    // store's own fixtures and parented to them, its walls replace the Shell's look (the Shell's
    // colliders stay), and its product meshes stand in for the game's looks through ProductLook.
    // The game keeps everything that plays: the slots, the items, the doors, the lamps, her map.
    // Without the art, nothing here does anything.
    //
    // The art is read from the folder by path, never from a Resources folder, so no build carries
    // it (a Resources folder ships in every edition, used or not). RoadsideArtImport moves an older
    // package out of Resources when the editor loads.
    public sealed class MiniStoreRoadside : MonoBehaviour
    {
        public const string ArtRoot = "Assets/KehaiRoadsideArt";
        public const string Prefabs = ArtRoot + "/Prefabs";
        const string OldResources = ArtRoot + "/Resources/KehaiRoadside";

        const int Resolution = 96;
        const float FieldWidth = 94f;
        static MiniStoreRoadside active;
        readonly Dictionary<string, Transform> fixtures = new Dictionary<string, Transform>();
        Light[] lamps;
        byte[] previous;
        readonly byte[] pixels = new byte[Resolution * Resolution];
        readonly int[] nearest = new int[Resolution * Resolution];
        Texture2D power;
        public int BoundShelves { get; private set; }

        static GameObject Load(string relative) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + relative + ".prefab")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>(OldResources + "/" + relative + ".prefab");

        public static GameObject ArtPrefab => Load("Art");
        public static bool HasArt => ArtPrefab != null;

        // Three warm, broad spots under the canopy, among the store's lamps (so on its breakers and
        // in her light sense), when the art is there to be lit.
        public static void LightCanopy(Transform ceiling)
        {
            if (!HasArt) return;
            foreach (float x in new[] { -10.5f, 1.5f, 12.75f })
            {
                var fixture = new GameObject("Canopy downlight (Lobby)");
                fixture.transform.SetParent(ceiling, false);
                fixture.transform.localPosition = new Vector3(x, 3.35f, 22.03f);
                fixture.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1f, -.32f));
                var light = fixture.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 117f;
                light.innerSpotAngle = 70f;
                light.range = 12f;
                light.intensity = 28f;
                light.color = new Color(1f, .852f, .686f);
                light.shadows = LightShadows.None;
            }
        }

        static void ShelfPresentation(MeshRenderer renderer)
        {
            // Small packaging receives direct light and shelf shadows. Only the fixtures cast.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var lod = renderer.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.009f, new Renderer[] { renderer }) });
            lod.RecalculateBounds();
        }

        // Which mini store fixture the art's bay or checkout of the same place dresses: bays are
        // named by their spot in tenths of a metre.
        public static void Record(Transform group, string name, Vector3 at, Transform placed)
        {
            string key = KeyFor(name, at);
            if (key == null || group == null || group.parent == null) return;
            var store = group.parent.gameObject;
            var binding = store.GetComponent<MiniStoreRoadside>() ?? store.AddComponent<MiniStoreRoadside>();
            binding.fixtures[key] = placed;
        }

        public static string KeyFor(string name, Vector3 at)
        {
            if (name == "Checkout_03") return "Checkout_Art";
            if (name == "Tall shelf" || name == "Short shelf") return $"Bay_{Mathf.RoundToInt(at.x * 10f)}_{Mathf.RoundToInt(at.z * 10f)}";
            return null;
        }

        // True when the art went on.
        public static bool Attach(GameObject store)
        {
            var prefab = ArtPrefab;
            var binding = store != null ? store.GetComponent<MiniStoreRoadside>() : null;
            if (prefab == null || binding == null || binding.BoundShelves > 0) return false;
            binding.Install(prefab);
            return binding.BoundShelves > 0;
        }

        void Install(GameObject prefab)
        {
            var art = Instantiate(prefab, transform, false);
            art.name = "Roadside art";
            var glass = art.GetComponentsInChildren<MeshRenderer>()
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.name == "Glass");
            var parts = art.GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Bay_") || r.name == "Checkout_Art");
            var links = new Dictionary<Transform, Transform>();
            foreach (var part in parts)
            {
                if (!fixtures.TryGetValue(part.name, out var fixture) || !fixture)
                {
                    Debug.LogWarning("Roadside art could not match the gameplay fixture " + part.name);
                    Destroy(art);
                    return;
                }
                links.Add(part.transform, fixture);
            }
            if (links.Count != fixtures.Count)
            {
                Debug.LogWarning("Roadside art layout differs from the active floorplan");
                Destroy(art);
                return;
            }
            var shell = transform.Find("Shell");
            var lightRoot = transform.Find("CeilingLights");
            if (!shell || !lightRoot) { Destroy(art); return; }
            lamps = lightRoot.GetComponentsInChildren<Light>();
            if (lamps.Length == 0) { Destroy(art); return; }
            foreach (var link in links)
            {
                foreach (var original in link.Value.GetComponentsInChildren<Renderer>(true)) original.forceRenderingOff = true;
                link.Key.SetParent(link.Value, true);
            }
            if (glass)
                foreach (var door in transform.GetComponentsInChildren<AutoDoubleDoor>(true))
                    foreach (var pane in door.GetComponentsInChildren<MeshRenderer>(true))
                        if (pane.name == "Left_Door_01" || pane.name == "Right_Door_01")
                            pane.sharedMaterial = glass;
            BoundShelves = links.Count(kv => kv.Key.name.StartsWith("Bay_"));
            foreach (var renderer in shell.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.name != "Pavement") renderer.forceRenderingOff = true;
            foreach (var lamp in lamps.Where(l => l.name.Contains("(Sales floor)") && l.type == LightType.Spot))
            {
                lamp.spotAngle = Mathf.Clamp(lamp.spotAngle, 102f, 118f);
                lamp.innerSpotAngle = lamp.spotAngle * .54f;
            }
            foreach (var lamp in lamps.Where(l => l.name.Contains("(Sales floor)")).OrderBy(l => HorizontalDistance(l.transform.position, transform.TransformPoint(new Vector3(0, 0, 6)))).Take(2))
            {
                lamp.shadows = LightShadows.Hard;
                lamp.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Low;
            }
            previous = new byte[lamps.Length];
            power = new Texture2D(Resolution, Resolution, TextureFormat.R8, false, true)
                { name = "Mini store live circuits", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var lampRooms = lamps.Select(l => PowerRoom(l.transform.position)).ToArray();
            var lampPositions = lamps.Select(l => l.transform.position).ToArray();
            for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    var world = transform.TransformPoint(new Vector3(((x + .5f) / Resolution - .5f) * FieldWidth, 0, ((y + .5f) / Resolution - .5f) * FieldWidth));
                    string room = PowerRoom(world);
                    float best = float.PositiveInfinity;
                    for (int i = 0; i < lamps.Length; i++)
                    {
                        if (room != null && room != lampRooms[i]) continue;
                        float distance = HorizontalDistance(world, lampPositions[i]);
                        if (distance >= best) continue;
                        nearest[y * Resolution + x] = i;
                        best = distance;
                    }
                }
            Shader.SetGlobalMatrix("_MarketWorldToLocal", transform.worldToLocalMatrix);
            Shader.SetGlobalTexture("_MarketPower", power);
            Shader.SetGlobalFloat("_MarketPowerBound", 1f);
            PowerSystem.PowerChanged += MainsChanged;
            RefreshPower(true);
            active = this;
            ProductLook.SetVisualLayer(Resolve, ShelfPresentation);
            var eye = Camera.main;
            if (eye && RenderSettings.skybox == null)
            {
                eye.clearFlags = CameraClearFlags.SolidColor;
                eye.backgroundColor = new Color(.052f, .079f, .101f, 1f);
            }
        }

        static string PowerRoom(Vector3 world)
        {
            var p = MiniStore.ToStore(world);
            foreach (var room in MiniStore.Rooms)
                if (!room.outdoors && room.store.Contains(new Vector2(p.x, p.z))) return room.name;
            return MiniStore.Local(world).z >= 20f ? "Lobby" : null;
        }

        // Squared, for comparing.
        static float HorizontalDistance(Vector3 a, Vector3 b) =>
            (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

        static ProductLook.Look Resolve(string id, ProductLook.Look original)
        {
            var visual = Load("Products/" + id);
            if (!visual) return original;
            return new ProductLook.Look(visual.GetComponent<MeshFilter>().sharedMesh,
                visual.GetComponent<MeshRenderer>().sharedMaterials, original.Mass);
        }

        void MainsChanged(bool on)
        {
            if (!on)
                foreach (var lamp in lamps)
                    if (lamp) lamp.enabled = false;
            RefreshPower(true);
        }

        void LateUpdate() => RefreshPower(false);

        void RefreshPower(bool force)
        {
            if (!power) return;
            bool changed = force;
            for (int i = 0; i < lamps.Length; i++)
            {
                byte value = (byte)(PowerSystem.PowerOn && lamps[i] && lamps[i].isActiveAndEnabled && lamps[i].intensity > .001f ? 255 : 0);
                changed |= value != previous[i];
                previous[i] = value;
            }
            if (!changed) return;
            for (int i = 0; i < pixels.Length; i++) pixels[i] = previous[nearest[i]];
            power.SetPixelData(pixels, 0);
            power.Apply(false, false);
        }

        void OnDestroy()
        {
            PowerSystem.PowerChanged -= MainsChanged;
            if (active == this)
            {
                ProductLook.SetVisualLayer(null);
                Shader.SetGlobalFloat("_MarketPowerBound", 0);
                Shader.SetGlobalTexture("_MarketPower", null);
                active = null;
            }
            if (power) Destroy(power);
        }
    }
}
#endif
