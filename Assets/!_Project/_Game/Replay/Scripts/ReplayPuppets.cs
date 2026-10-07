using System.Collections.Generic;
using Kehai.Aiko;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Replay
{
    // Stand-ins for what came and went during the shift: Aiko, customers, her understudy, items
    // off their shelves, her props, spills, bags, footprints. Each is built the way the game
    // builds the real thing (its prefab, or the same code) and then stripped of everything
    // that acts: scripts, colliders, bodies, agents, sounds. The replay moves them.
    public sealed class ReplayPuppets
    {
        readonly Transform parent;
        readonly Transform holder;                  // inactive: copies made here never wake up
        readonly GameObject customerPrefab, dirtPrefab, bagPrefab;
        readonly Dictionary<string, GameObject> itemByProduct = new Dictionary<string, GameObject>();
        readonly Dictionary<ItemType, GameObject> itemByType = new Dictionary<ItemType, GameObject>();
        GameObject anyItem;

        public ReplayPuppets(Transform parent, IEnumerable<Item> sceneItems)
        {
            this.parent = parent;
            holder = new GameObject("~puppet workshop").transform;
            holder.SetParent(parent, false);
            holder.gameObject.SetActive(false);

            CustomerSpawner spawner = Object.FindAnyObjectByType<CustomerSpawner>(FindObjectsInactive.Include);
            customerPrefab = spawner != null ? spawner.customerPrefab : null;
            CustomerNPC npc = customerPrefab != null ? customerPrefab.GetComponent<CustomerNPC>() : null;
            dirtPrefab = npc != null ? npc.dirtPrefab : null;
            foreach (Trashcan bin in Object.FindObjectsByType<Trashcan>(FindObjectsInactive.Include))
                if (bin.trashBagPrefab != null) { bagPrefab = bin.trashBagPrefab; break; }

            foreach (Item item in sceneItems)
            {
                if (item == null) continue;
                if (!string.IsNullOrEmpty(item.productId) && !itemByProduct.ContainsKey(item.productId)) itemByProduct[item.productId] = item.gameObject;
                if (!itemByType.ContainsKey(item.type)) itemByType[item.type] = item.gameObject;
                if (anyItem == null && item.type != ItemType.Stock) anyItem = item.gameObject;
            }
        }

        // A copy of `source` that looks the same and does nothing.
        public GameObject Copy(GameObject source, string name)
        {
            if (source == null) return null;
            GameObject go = Object.Instantiate(source, holder);
            go.name = name;
            Strip(go);
            go.SetActive(true);
            go.transform.SetParent(parent, true);
            return go;
        }

        // Everything that acts goes; renderers, meshes, lights and particles stay. Scripts go
        // last-added first, so a script that requires another is gone before the one it needs.
        public static void Strip(GameObject go)
        {
            MonoBehaviour[] scripts = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = scripts.Length - 1; i >= 0; i--)
                if (scripts[i] != null && !(scripts[i] is PlayerBodySlot)) Object.DestroyImmediate(scripts[i]);
            foreach (Canvas c in go.GetComponentsInChildren<Canvas>(true)) if (c != null) Object.DestroyImmediate(c.gameObject);
            foreach (NavMeshAgent a in go.GetComponentsInChildren<NavMeshAgent>(true)) Object.DestroyImmediate(a);
            foreach (NavMeshObstacle o in go.GetComponentsInChildren<NavMeshObstacle>(true)) Object.DestroyImmediate(o);
            foreach (Joint j in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(j);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(r);
            foreach (AudioSource s in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(s);
        }

        // ---- the people ------------------------------------------------------------------

        public GameObject MakeAiko(out Renderer eye, out Light gaze)
        {
            var root = new GameObject(GameNames.Antagonist + " (replay)");
            root.transform.SetParent(parent, false);
            (eye, gaze) = AikoBody.BuildLook(root.transform, new AikoConfig());
            return root;
        }

        public GameObject MakeCustomer(string name) => customerPrefab != null ? Copy(customerPrefab, name) : Capsule(name, ReplayLook.Other);

        // ---- things ------------------------------------------------------------------------

        // An item by its recorded key: the product it was (one in the scene, or its prefab: stock
        // on the shelves isn't GameObjects to copy), or its section, or a box.
        public GameObject MakeItem(string key, string name)
        {
            GameObject source = null;
            if (!string.IsNullOrEmpty(key) && !itemByProduct.TryGetValue(key, out source))
            {
                source = ProductLook.Prefab(key);
                if (source == null && System.Enum.TryParse(key, out ItemType type)) itemByType.TryGetValue(type, out source);
            }
            source = source != null ? source : anyItem;
            return source != null ? Copy(source, name) : Box(name, new Vector3(0.25f, 0.3f, 0.12f), new Color(0.8f, 0.75f, 0.6f));
        }

        public GameObject MakeSpill(string name) =>
            dirtPrefab != null ? Copy(dirtPrefab, name) : Disc(name, 0.6f, new Color(0.35f, 0.25f, 0.12f, 0.85f));

        public GameObject MakeBag(string name) =>
            bagPrefab != null ? Copy(bagPrefab, name) : Box(name, new Vector3(0.45f, 0.6f, 0.45f), new Color(0.08f, 0.08f, 0.09f));

        // ---- her props: the game's own builders, then stripped ------------------------------

        public GameObject MakeCrateWall(int columns, int seed)
        {
            Random.State was = Random.state;
            Random.InitState(seed);   // the same jitter every time the replay is opened
            GameObject go = CrateWall.Spawn(Vector3.zero, Vector3.right, Mathf.Max(1, columns) * 0.62f - 0.01f).gameObject;
            Random.state = was;
            return Adopt(go);
        }

        public ParticleSystem MakeFog(float radius, float seconds, uint seed, out GameObject go)
        {
            go = Adopt(FogCloud.Build(Vector3.zero, radius, seconds).gameObject);
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;
            return ps;
        }

        public GameObject MakeCctv(bool boltedOn, out Light led)
        {
            GameObject go = Adopt(CctvCamera.Spawn(Vector3.zero, Vector3.forward, boltedOn).gameObject);
            led = go.GetComponentInChildren<Light>(true);
            return go;
        }

        public GameObject MakeCoffee(bool last) => Adopt(CoffeeCup.Spawn(Vector3.zero, last).gameObject);

        // A wet print, as Footprint draws it: a dark quad (the recorded pose lays it on the floor).
        public GameObject MakeFootprint()
        {
            var root = new GameObject("Footprint");
            root.transform.SetParent(parent, false);
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localScale = new Vector3(0.12f, 0.28f, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = AikoProps.Lit(new Color(0.12f, 0.1f, 0.08f));
            return root;
        }

        GameObject Adopt(GameObject go)
        {
            Strip(go);
            go.transform.SetParent(parent, false);
            return go;
        }

        // ---- shapes, when the scene has nothing better ------------------------------------

        GameObject Capsule(string name, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.GetComponent<Renderer>().sharedMaterial = AikoProps.Lit(colour);
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.up;
            return root;
        }

        GameObject Box(string name, Vector3 size, Color colour)
        {
            GameObject go = AikoProps.Box(name, Vector3.zero, size, colour, parent, collider: false);
            return go;
        }

        GameObject Disc(string name, float radius, Color colour)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            GameObject disc = ReplayLook.Shape("Disc", ReplayLook.Ring(0f), ReplayLook.Flat(colour), root.transform, 0);
            disc.transform.localScale = new Vector3(radius, 1f, radius);
            disc.transform.localPosition = Vector3.up * 0.01f;
            return root;
        }
    }
}
