using System.Collections.Generic;
using Kehai.Aiko;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Kehai.Replay
{
    // The store, rebuilt from a recording. The scene loads as usual; then everything that would
    // play the game is switched off (every game script, the NavMesh agents, the player's
    // controller, physics, the HUD) and what the recording tracked is driven from it:
    //   - the scene's own shelf units (moved by the maze and by her), doors, door panels, bins,
    //     and the spills, bags and loose items that were there when the shift began;
    //   - puppets for everything that came and went: Aiko, customers, her understudy, items
    //     off their shelves, her props, spills, bags, footprints; and the player's body;
    //   - the shelf slots (stocked or empty) and the ceiling lights, as they changed.
    // `Apply(t)` puts all of it where it was at t, forwards or backwards.
    public sealed class ReplayStage
    {
        public sealed class Actor
        {
            public ReplayEntity Entity;
            public GameObject Root;
            public Transform Transform;
            public bool FromScene;          // the scene's own object, not a puppet
            public bool Active = true;
            public int ShownState = int.MinValue;
            public Light Lamp;              // the torch's beam, a CCTV camera's LED
            public ParticleSystem Particles;
            public float ParticleAge = -1f;
            public Renderer AikoEye;
            public Light AikoGaze;
        }

        sealed class SlotView
        {
            public ShelfSlot Slot;
            public bool Filled, Initial;
            public string Product;
            public readonly List<(float t, bool filled, string product)> Changes = new List<(float, bool, string)>();
        }

        sealed class LightView
        {
            public Light Light;
            public bool Initial, On;
            public readonly List<(float t, bool on)> Changes = new List<(float, bool)>();
        }

        public readonly ReplayData Data;
        public readonly List<Actor> Actors = new List<Actor>();
        public Camera Camera { get; private set; }
        public PlayerBodySlot Body { get; private set; }
        public int PlayerId { get; private set; } = -1;
        public int AikoId { get; private set; } = -1;
        public float FloorY { get; private set; }
        public float Start => Data.Ticks.Count > 0 ? Data.Ticks[0] : 0f;
        public float End => Data.End;
        public int CircuitBits { get; private set; } = 1;
        public bool ShowPlayerBody = true;
        public string Summary { get; private set; } = "";

        readonly Transform root;
        readonly Dictionary<int, Actor> byId = new Dictionary<int, Actor>();
        readonly List<SlotView> slots = new List<SlotView>();
        readonly List<SlotView> changingSlots = new List<SlotView>();
        readonly List<LightView> lights = new List<LightView>();
        readonly List<ReplayEvent> circuits = new List<ReplayEvent>();
        readonly Dictionary<int, Material> moodEyes = new Dictionary<int, Material>();
        ReplayPuppets puppets;
        SimulationMode physicsWas;
        float bodyLift;
        bool built;

        public ReplayStage(ReplayData data, Transform root)
        {
            Data = data;
            this.root = root;
        }

        public Actor ActorOf(int id) => byId.TryGetValue(id, out Actor a) ? a : null;

        // ---- building ------------------------------------------------------------------------

        public void Build()
        {
            // What the scene has, found before switching it off (registries empty as scripts disable).
            var units = new List<ShelfUnit>(Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include));
            var doors = new List<HingeDoor>(Object.FindObjectsByType<HingeDoor>(FindObjectsInactive.Include));
            var panels = new List<Transform>();
            foreach (AutoDoubleDoor d in Object.FindObjectsByType<AutoDoubleDoor>(FindObjectsInactive.Include))
            {
                if (d.LeftPanel != null) panels.Add(d.LeftPanel);
                if (d.RightPanel != null) panels.Add(d.RightPanel);
            }
            var bins = new List<Trashcan>(Object.FindObjectsByType<Trashcan>(FindObjectsInactive.Include));
            var spills = new List<Dirt>(Object.FindObjectsByType<Dirt>(FindObjectsInactive.Include));
            var bags = new List<TrashBag>(Object.FindObjectsByType<TrashBag>(FindObjectsInactive.Include));
            var sceneSlots = new List<ShelfSlot>(ShelfSlot.All);
            var items = new List<Item>(Object.FindObjectsByType<Item>(FindObjectsInactive.Include));
            var loose = items.FindAll(i => !i.isOnShelf);

            Neutralise(SceneManager.GetActiveScene());
            TakeCamera();
            puppets = new ReplayPuppets(root, items);
            FindFloor();

            var claimed = new HashSet<Object>();
            int fromScene = 0, made = 0;
            foreach (ReplayEntity e in Data.Entities.Values)
            {
                if (e.Samples.Count == 0) continue;
                Actor a = Make(e, units, doors, panels, bins, spills, bags, loose, claimed);
                if (a == null) continue;
                Actors.Add(a);
                byId[e.Id] = a;
                if (a.FromScene) fromScene++; else made++;
            }

            // Loose things the scene has but the recording didn't start with were gone by then.
            int hidden = 0;
            foreach (Item i in loose) if (!claimed.Contains(i)) { i.gameObject.SetActive(false); hidden++; }
            foreach (Dirt d in spills) if (!claimed.Contains(d)) { d.gameObject.SetActive(false); hidden++; }
            foreach (TrashBag b in bags) if (!claimed.Contains(b)) { b.gameObject.SetActive(false); hidden++; }

            // Shelves where they stood when the shift began, then the slots and lights on them.
            ApplyActors(Start, 0f);
            int slotsMatched = MatchSlots(sceneSlots);
            int lightsMatched = MatchLights();
            foreach (ReplayEvent ev in Data.Events) if (ev.Type == KrecEvent.Circuits) circuits.Add(ev);
            built = true;
            Apply(Start, 0f);

            Summary = $"{fromScene} of the store's own things and {made} puppets for {Data.Entities.Count} recorded; " +
                      $"{slotsMatched}/{Data.Header.Slots.Count} shelf slots and {lightsMatched}/{Data.Header.Lights.Count} lights matched; " +
                      $"{hidden} loose things the shift didn't start with hidden";
        }

        // The game stops here: no script of the game's runs, nothing simulates, nobody walks.
        void Neutralise(Scene scene)
        {
            physicsWas = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            System.Reflection.Assembly game = typeof(ShiftManager).Assembly;
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.transform == root || root.IsChildOf(go.transform)) continue;
                foreach (MonoBehaviour b in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b == null || b.GetType().Assembly != game) continue;
                    if (b.GetType().Namespace == typeof(ReplayStage).Namespace) continue;
                    if (b is ParentMaterialController) continue;   // only paints the shelves, in Start
                    if (b is ShelfDrawer) continue;                // draws the stock the replay sets
                    b.enabled = false;
                }
                foreach (NavMeshAgent a in go.GetComponentsInChildren<NavMeshAgent>(true)) a.enabled = false;
                foreach (CharacterController c in go.GetComponentsInChildren<CharacterController>(true)) c.enabled = false;
                foreach (Canvas c in go.GetComponentsInChildren<Canvas>(true)) c.enabled = false;
                foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) r.isKinematic = true;
            }
        }

        // The player's own camera becomes the replay's: it keeps the store's post-processing.
        void TakeCamera()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Camera cam = player != null ? player.GetComponentInChildren<Camera>(true) : null;
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Replay camera");
                cam = go.AddComponent<Camera>();
            }
            cam.transform.SetParent(null, true);
            for (int i = cam.transform.childCount - 1; i >= 0; i--) cam.transform.GetChild(i).gameObject.SetActive(false);
            cam.gameObject.SetActive(true);
            cam.enabled = true;
            cam.name = "Replay camera";
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (c != cam) c.enabled = false;
            foreach (AudioListener l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
                l.enabled = l.gameObject == cam.gameObject;
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            if (player != null) player.SetActive(false);
            Camera = cam;
        }

        // Where the floor is: under the player at the start, else where Aiko stood.
        void FindFloor()
        {
            ReplayEntity player = null, aiko = null;
            foreach (ReplayEntity e in Data.Entities.Values)
            {
                if (e.Kind == KrecKind.Player && player == null) player = e;
                if (e.Kind == KrecKind.Aiko && aiko == null) aiko = e;
            }
            FloorY = aiko != null ? aiko.Samples[0].Position.y : 0f;
            if (player == null) return;
            Vector3 p = player.Samples[0].Position;
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                FloorY = hit.point.y;
            bodyLift = FloorY - p.y;
        }

        Actor Make(ReplayEntity e, List<ShelfUnit> units, List<HingeDoor> doors, List<Transform> panels, List<Trashcan> bins,
                   List<Dirt> spills, List<TrashBag> bags, List<Item> loose, HashSet<Object> claimed)
        {
            EntitySample first = e.Samples[0];
            bool atStart = e.Spawned <= Start + 0.6f;
            var a = new Actor { Entity = e };
            Component scene = null;
            GameObject go = null;

            switch (e.Kind)
            {
                case KrecKind.Player:
                    Body = PlayerBodySlot.Create(root);
                    go = Body.gameObject;
                    PlayerId = e.Id;
                    break;
                case KrecKind.Aiko:
                    go = puppets.MakeAiko(out a.AikoEye, out a.AikoGaze);
                    AikoId = e.Id;
                    break;
                case KrecKind.Customer:
                case KrecKind.Understudy:
                    go = puppets.MakeCustomer(e.Label);
                    break;
                case KrecKind.ShelfUnit:
                    scene = Claim(units, MazeFrom(first.Position), 0.75f, claimed);
                    break;
                case KrecKind.Door:
                    scene = Claim(doors, first.Position, 0.5f, claimed);
                    break;
                case KrecKind.AutoDoorPanel:
                    scene = Claim(panels, first.Position, 0.5f, claimed);
                    break;
                case KrecKind.Bin:
                    scene = Claim(bins, first.Position, 0.5f, claimed);
                    break;
                case KrecKind.Spill:
                    if (atStart) scene = Claim(spills, first.Position, 0.3f, claimed);
                    if (scene == null) go = puppets.MakeSpill(e.Label);
                    break;
                case KrecKind.Bag:
                    if (atStart) scene = Claim(bags, first.Position, 0.3f, claimed);
                    if (scene == null) go = puppets.MakeBag(e.Label);
                    break;
                case KrecKind.Item:
                    if (atStart) scene = Claim(loose, first.Position, 0.35f, claimed);
                    if (scene == null) go = puppets.MakeItem(e.Key, e.Label);
                    break;
                case KrecKind.CrateWall:
                    go = puppets.MakeCrateWall(first.State > 0 ? first.State : 4, e.Id);
                    break;
                case KrecKind.Fog:
                {
                    float seconds = float.IsInfinity(e.Despawned) ? 60f : Mathf.Max(5f, e.Despawned - e.Spawned - 10f);
                    a.Particles = puppets.MakeFog(first.State > 0 ? first.State / 10f : 3f, seconds, (uint)e.Id, out go);
                    break;
                }
                case KrecKind.Cctv:
                    go = puppets.MakeCctv(e.Key == "Cctv/bolted" || (first.State & 1) != 0, out a.Lamp);
                    break;
                case KrecKind.Coffee:
                    go = puppets.MakeCoffee(first.State == 1);
                    break;
                case KrecKind.Footprint:
                    go = puppets.MakeFootprint();
                    break;
            }

            if (scene != null)
            {
                a.FromScene = true;
                a.Transform = scene.transform;
                a.Root = scene.gameObject;
            }
            else if (go != null)
            {
                a.Transform = go.transform;
                a.Root = go;
                go.name = $"{e.Kind} {e.Id} {e.Label}";
            }
            else return null;

            if (e.Kind == KrecKind.Item && a.Lamp == null && a.Root.GetComponent<Flashlight>() != null)
                a.Lamp = a.Root.GetComponentInChildren<Light>(true);
            if (e.Kind == KrecKind.Item && a.Lamp == null && e.Key == ItemType.Flashlight.ToString())
                a.Lamp = a.Root.GetComponentInChildren<Light>(true);
            return a;
        }

        // A recorded shelf unit that the maze had moved before the shift began stood somewhere
        // else in the scene file.
        Vector3 MazeFrom(Vector3 at)
        {
            foreach ((int bay, Vector3 from, Vector3 to) in Data.Header.MazeMoves)
                if ((to - at).sqrMagnitude < 0.3f * 0.3f) return from;
            return at;
        }

        static T Claim<T>(List<T> pool, Vector3 at, float within, HashSet<Object> claimed) where T : Component
        {
            T best = null;
            float bestD = within * within;
            foreach (T c in pool)
            {
                if (c == null || claimed.Contains(c)) continue;
                float d = (c.transform.position - at).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = c; }
            }
            if (best != null) claimed.Add(best);
            return best;
        }

        // Header slot i ↔ the scene's slot at the same place (after the shelves have moved there).
        int MatchSlots(List<ShelfSlot> sceneSlots)
        {
            var grid = new Dictionary<Vector3Int, List<ShelfSlot>>();
            foreach (ShelfSlot s in sceneSlots)
            {
                Vector3Int k = Cell(SlotPoint(s));
                if (!grid.TryGetValue(k, out List<ShelfSlot> bucket)) grid[k] = bucket = new List<ShelfSlot>();
                bucket.Add(s);
            }

            int matched = 0;
            var taken = new HashSet<ShelfSlot>();
            for (int i = 0; i < Data.Header.Slots.Count; i++)
            {
                (Vector3 at, bool filled, string product) = Data.Header.Slots[i];
                var view = new SlotView { Initial = filled, Filled = !filled, Product = product };
                Vector3Int c = Cell(at);
                float best = 0.15f * 0.15f;
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!grid.TryGetValue(c + new Vector3Int(x, y, z), out List<ShelfSlot> bucket)) continue;
                    foreach (ShelfSlot s in bucket)
                    {
                        if (taken.Contains(s)) continue;
                        float d = (SlotPoint(s) - at).sqrMagnitude;
                        if (d < best) { best = d; view.Slot = s; }
                    }
                }
                if (view.Slot != null)
                {
                    taken.Add(view.Slot);
                    matched++;
                }
                slots.Add(view);
            }

            foreach (ReplayEvent e in Data.Events)
                if (e.Type == KrecEvent.Slot && e.Index >= 0 && e.Index < slots.Count)
                    slots[e.Index].Changes.Add((e.T, e.Flag, e.Text));
            foreach (SlotView v in slots)
            {
                if (v.Slot == null) continue;
                if (v.Changes.Count > 0) changingSlots.Add(v);
                ShowSlot(v, v.Initial, v.Product);   // every slot once; only the changing ones again
            }
            return matched;
        }

        static Vector3 SlotPoint(ShelfSlot s) => s.Position;
        static Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x * 2f), Mathf.FloorToInt(p.y * 2f), Mathf.FloorToInt(p.z * 2f));

        int MatchLights()
        {
            IReadOnlyList<Light> scene = LightProbe.CeilingLights;
            int matched = 0;
            for (int i = 0; i < Data.Header.Lights.Count; i++)
            {
                (Vector3 at, bool on) = Data.Header.Lights[i];
                Light found = null;
                if (i < scene.Count && scene[i] != null && (scene[i].transform.position - at).sqrMagnitude < 0.1f * 0.1f) found = scene[i];
                else
                {
                    float best = 0.5f * 0.5f;
                    foreach (Light l in scene)
                    {
                        if (l == null) continue;
                        float d = (l.transform.position - at).sqrMagnitude;
                        if (d < best) { best = d; found = l; }
                    }
                }
                if (found != null) matched++;
                lights.Add(new LightView { Light = found, Initial = on, On = !on });
            }
            foreach (ReplayEvent e in Data.Events)
                if (e.Type == KrecEvent.Lights && e.Lights != null)
                    foreach ((int index, bool on) in e.Lights)
                        if (index >= 0 && index < lights.Count) lights[index].Changes.Add((e.T, on));
            return matched;
        }

        // ---- playing -------------------------------------------------------------------------

        // Everything as it was at t (clamped to the recording).
        public void Apply(float t, float dt)
        {
            float q = Mathf.Clamp(t, Start, End);
            ApplyActors(q, dt);
            if (!built) return;

            foreach (SlotView v in changingSlots)
            {
                bool filled = v.Initial;
                string product = v.Product;
                foreach ((float at, bool f, string p) in v.Changes)
                {
                    if (at > q) break;
                    filled = f;
                    product = p;
                }
                if (filled != v.Filled) ShowSlot(v, filled, product);
            }

            foreach (LightView l in lights)
            {
                bool on = l.Initial;
                foreach ((float at, bool o) in l.Changes)
                {
                    if (at > q) break;
                    on = o;
                }
                if (on == l.On) continue;
                l.On = on;
                if (l.Light != null) l.Light.enabled = on;
            }

            int bits = 1;
            foreach (ReplayEvent e in circuits)
            {
                if (e.T > q) break;
                bits = e.Kind;
            }
            CircuitBits = bits;
            Physics.SyncTransforms();
        }

        void ApplyActors(float q, float dt)
        {
            foreach (Actor a in Actors)
            {
                ReplayEntity e = a.Entity;
                if (!Data.TryPose(e.Id, q, out EntitySample pose))
                {
                    if (a.Active)
                    {
                        a.Root.SetActive(false);
                        a.Active = false;
                    }
                    continue;
                }

                bool show = pose.Visible && (e.Id != PlayerId || ShowPlayerBody);
                if (show != a.Active)
                {
                    a.Root.SetActive(show);
                    a.Active = show;
                }
                if (!show) continue;

                if (e.Id == PlayerId)
                    Body.Show(pose.Position + Vector3.up * bodyLift, pose.Rotation.eulerAngles.y, pose.State & 3, (pose.State & KrecState.PlayerCarrying) != 0, dt);
                else
                    a.Transform.SetPositionAndRotation(pose.Position, pose.Rotation);

                if (pose.State != a.ShownState)
                {
                    ShowState(a, pose.State);
                    a.ShownState = pose.State;
                }
                if (a.Particles != null) RunParticles(a, q - e.Spawned);
            }
        }

        void ShowState(Actor a, int state)
        {
            switch (a.Entity.Kind)
            {
                case KrecKind.Aiko:
                {
                    int mood = state & 3;
                    if (!moodEyes.TryGetValue(mood, out Material eye))
                        moodEyes[mood] = eye = AikoProps.Emissive(AikoBody.MoodColour((AikoBody.Mood)mood), mood == (int)AikoBody.Mood.Hunt ? 6f : 3f);
                    if (a.AikoEye != null) a.AikoEye.sharedMaterial = eye;
                    if (a.AikoGaze != null) a.AikoGaze.color = AikoBody.MoodColour((AikoBody.Mood)mood);
                    break;
                }
                case KrecKind.Item:
                    if (a.Lamp != null) a.Lamp.enabled = (state & KrecState.ItemLit) != 0;
                    break;
                case KrecKind.Cctv:
                    if (a.Lamp != null) a.Lamp.enabled = (state & 2) == 0;
                    break;
            }
        }

        // Her fog runs on the replay's clock: paused, it hangs; scrubbed, it's rebuilt at that age.
        static void RunParticles(Actor a, float age)
        {
            if (age < 0f) return;
            if (a.ParticleAge < 0f || age < a.ParticleAge || age - a.ParticleAge > 0.5f) a.Particles.Simulate(age, true, true, true);
            else if (age > a.ParticleAge) a.Particles.Simulate(age - a.ParticleAge, true, false, true);
            a.ParticleAge = age;
        }

        // The stock is data: the slot is shown full or empty, and the drawer does the rest.
        static void ShowSlot(SlotView v, bool filled, string product)
        {
            v.Filled = filled;
            v.Slot?.Show(filled, product);
        }

        public void Dispose()
        {
            Physics.simulationMode = physicsWas;
        }
    }
}
