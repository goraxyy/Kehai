using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Materials and shapes for everything Karen puts into the store. Built from primitives
    // and URP Lit at runtime, since the repository carries no art.
    public static class KarenProps
    {
        static readonly Dictionary<Color, Material> lit = new Dictionary<Color, Material>();
        static Material particle;
        static Texture2D softDot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lit.Clear();
            particle = null;
            softDot = null;
        }

        static Shader LitShader => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        public static Material Lit(Color colour, float smoothness = 0.3f)
        {
            if (lit.TryGetValue(colour, out Material m) && m != null) return m;
            m = new Material(LitShader) { name = "KAREN_" + ColorUtility.ToHtmlStringRGB(colour) };
            m.color = colour;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            lit[colour] = m;
            return m;
        }

        public static Material Emissive(Color colour, float intensity)
        {
            var m = new Material(LitShader) { name = "KAREN_Glow" };
            m.color = colour * 0.2f;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour * 0.2f);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", colour * intensity);
            return m;
        }

        public static Material Particle()
        {
            if (particle != null) return particle;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            particle = new Material(shader) { name = "KAREN_Fog" };
            particle.mainTexture = SoftDot();
            if (particle.HasProperty("_BaseMap")) particle.SetTexture("_BaseMap", SoftDot());
            if (particle.HasProperty("_Surface")) particle.SetFloat("_Surface", 1f);   // transparent
            particle.renderQueue = 3000;
            return particle;
        }

        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;
            const int size = 64;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            softDot.Apply();
            return softDot;
        }

        public static GameObject Box(string name, Vector3 position, Vector3 size, Color colour, Transform parent = null, bool collider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = Lit(colour);
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }
    }

    // ---- crate wall (§8.4) --------------------------------------------------------------

    // A stack of stock crates dragged across an aisle. Blocks people and carves the NavMesh
    // while it stands. Clearing it is a six-second hold that makes a racket the whole
    // store can hear.
    public sealed class CrateWall : MonoBehaviour, IInteractable, IHoldInteractable, IMapTransient
    {
        public float clearSeconds = 6f;
        public float lifetime = 150f;
        public int Region = -1;
        float born;
        float lastNoise;

        public static CrateWall Spawn(Vector3 at, Vector3 across, float width)
        {
            var root = new GameObject("KAREN_CrateWall");
            root.layer = LayerMask.NameToLayer("Interactable");
            root.transform.position = at;
            root.transform.rotation = Quaternion.LookRotation(Vector3.Cross(across, Vector3.up), Vector3.up);

            int columns = Mathf.Clamp(Mathf.CeilToInt(width / 0.62f), 2, 7);
            var colour = new Color(0.55f, 0.42f, 0.28f);
            for (int c = 0; c < columns; c++)
            for (int h = 0; h < 2; h++)
            {
                float x = (c - (columns - 1) * 0.5f) * 0.62f;
                GameObject crate = KarenProps.Box("Crate", Vector3.zero, new Vector3(0.6f, 0.55f, 0.6f), colour, root.transform);
                crate.transform.localPosition = new Vector3(x, 0.28f + h * 0.56f, Random.Range(-0.05f, 0.05f));
                crate.transform.localRotation = Quaternion.Euler(0f, Random.Range(-8f, 8f), 0f);
                crate.layer = root.layer;
            }

            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = new Vector3(columns * 0.62f, 1.2f, 0.7f);
            obstacle.center = new Vector3(0f, 0.6f, 0f);
            obstacle.carving = true;

            var wall = root.AddComponent<CrateWall>();
            wall.born = Time.time;
            return wall;
        }

        void Update()
        {
            if (Time.time - born > lifetime) Destroy(gameObject);
        }

        public void Interact(PlayerInteract player) { }
        public string GetPrompt() => "Hold E to drag the crates aside (loud)";

        public bool CanHold(PlayerInteract player) => true;
        public float HoldDuration => clearSeconds;

        public void OnHoldProgress(float normalised)
        {
            if (Time.time - lastNoise < 0.5f) return;
            lastNoise = Time.time;
            NoiseBus.Emit(transform.position, 0.9f, NoiseKind.CrateClearing, NoiseAuthor.Player);
            OneShotAudio.PlayAt(ProceduralAudio.Tell(TellKind.Scrape), transform.position, 0.6f);
        }

        public void OnHoldCancelled() { }

        public void OnHoldComplete(PlayerInteract player)
        {
            KarenBrain.Instance?.Ledger.RecordCounterplay("crates_cleared");
            Destroy(gameObject);
        }
    }

    // ---- fog (§8.1) ---------------------------------------------------------------------

    // A sabotaged freezer venting cold vapour across an aisle. Your eyes suffer; hers don't.
    public sealed class FogCloud : MonoBehaviour
    {
        public float Radius { get; private set; }

        public static FogCloud Spawn(Vector3 at, float radius, float seconds)
        {
            FogCloud fog = Build(at, radius, seconds);
            fog.GetComponent<ParticleSystem>().Play();
            Destroy(fog.gameObject, seconds + 10f);
            return fog;
        }

        // The cloud, not yet venting and never timing out: the replay runs its particles itself.
        public static FogCloud Build(Vector3 at, float radius, float seconds)
        {
            var go = new GameObject("KAREN_Fog");
            go.transform.position = at + Vector3.up * 0.2f;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = seconds;
            main.loop = false;
            main.startLifetime = 9f;
            main.startSpeed = 0.25f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startColor = new Color(0.85f, 0.9f, 0.95f, 0.35f);
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 70f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, 0.6f, radius * 2f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.y = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.2f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = KarenProps.Particle();
            var fog = go.AddComponent<FogCloud>();
            fog.Radius = radius;
            return fog;
        }
    }

    // ---- CCTV (§3.5, §8.1) --------------------------------------------------------------

    // A camera Karen watches through. Low-confidence sightings in a narrow cone. Hold E for
    // four seconds to pull its plug — which buys silence and announces that you exist:
    // a dead camera is information, and she comes to look at the blind spot.
    public sealed class CctvCamera : MonoBehaviour, IInteractable, IHoldInteractable, IHoverable
    {
        static readonly List<CctvCamera> all = new List<CctvCamera>();
        public static IReadOnlyList<CctvCamera> All => all;

        public bool Dead { get; private set; }
        public bool BoltedOn;            // installed by Karen over a hiding place
        SightSensor eye;
        Light led;
        float nextReport;

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public static CctvCamera Spawn(Vector3 position, Vector3 look, bool boltedOn)
        {
            var root = new GameObject(boltedOn ? "KAREN_Camera_BoltedOn" : "KAREN_Camera");
            root.layer = LayerMask.NameToLayer("Interactable");
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward);

            GameObject body = KarenProps.Box("Housing", position, new Vector3(0.18f, 0.16f, 0.34f), new Color(0.85f, 0.85f, 0.82f), root.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.layer = root.layer;
            GameObject lens = KarenProps.Box("Lens", position, new Vector3(0.1f, 0.1f, 0.04f), Color.black, root.transform, collider: false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.18f);

            var cam = root.AddComponent<CctvCamera>();
            cam.BoltedOn = boltedOn;

            var eyeGo = new GameObject("Eye");
            eyeGo.transform.SetParent(root.transform, false);
            eyeGo.transform.localPosition = new Vector3(0f, 0f, 0.2f);
            cam.eye = eyeGo.AddComponent<SightSensor>();
            cam.eye.range = 16f;
            cam.eye.fieldOfView = 70f;
            cam.eye.gain = 1.1f;
            cam.eye.darkVision = 0.55f;
            cam.eye.ignoreRoot = root.transform;

            var ledGo = new GameObject("Led");
            ledGo.transform.SetParent(root.transform, false);
            ledGo.transform.localPosition = new Vector3(0.06f, 0.06f, 0.17f);
            cam.led = ledGo.AddComponent<Light>();
            cam.led.type = LightType.Point;
            cam.led.range = 0.6f;
            cam.led.intensity = 1.5f;
            cam.led.color = Color.red;
            return cam;
        }

        void Update()
        {
            if (Dead) return;
            eye.Tick(Time.deltaTime);
            if (eye.Awareness < eye.glimpseAt || Time.time < nextReport) return;

            nextReport = Time.time + 1f;
            InfrastructureFeed.Report(new Observation(SenseChannel.Infrastructure, eye.LastSeenPosition,
                eye.Awareness * 0.65f, 2.5f, $"cctv({name})"));
        }

        public void Interact(PlayerInteract player) { }
        public string GetPrompt() => Dead ? "Unplugged" : "Hold E to unplug the camera";
        public bool CanHold(PlayerInteract player) => !Dead;
        public float HoldDuration => 4f;
        public void OnHoldProgress(float normalised)
        {
            if (normalised > 0.05f && Random.value < 0.02f)
                NoiseBus.Emit(transform.position, 0.3f, NoiseKind.Unplugging, NoiseAuthor.Player);
        }
        public void OnHoldCancelled() { }

        public void OnHoldComplete(PlayerInteract player)
        {
            Dead = true;
            if (led != null) led.enabled = false;
            OneShotAudio.PlayAt(ProceduralAudio.Unplug(), transform.position);

            // The camera going dark is itself a report: someone is standing right here.
            InfrastructureFeed.Report(new Observation(SenseChannel.Infrastructure, transform.position - transform.forward * 1.2f,
                0.7f, 4f, $"camera dead({name})"));
            KarenBrain.Instance?.Ledger.RecordCounterplay("camera_unplugged");
        }

        public void OnHoverEnter() { }
        public void OnHoverExit() { }
    }

    // ---- the coffee (§8.6, §10.3) -------------------------------------------------------

    // The favour. Left somewhere you'll find it, still warm. Drinking it is exactly as good
    // as the machine's — nothing about this is a trick, and that's the horror. The last one
    // she ever makes you ends the career.
    public sealed class CoffeeCup : MonoBehaviour, IInteractable
    {
        public bool IsLast;
        public event System.Action Drunk;

        public static CoffeeCup Spawn(Vector3 at, bool isLast)
        {
            var root = new GameObject(isLast ? "KAREN_Coffee_Last" : "KAREN_Coffee");
            root.layer = LayerMask.NameToLayer("Interactable");
            root.transform.position = at;

            GameObject cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            cup.transform.SetParent(root.transform, false);
            cup.transform.localScale = new Vector3(0.09f, 0.06f, 0.09f);
            cup.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            cup.GetComponent<Renderer>().sharedMaterial = KarenProps.Lit(new Color(0.95f, 0.95f, 0.92f));
            cup.layer = root.layer;
            var box = cup.GetComponent<Collider>();
            if (box != null) box.isTrigger = true;

            var c = root.AddComponent<CoffeeCup>();
            c.IsLast = isLast;
            return c;
        }

        public void Interact(PlayerInteract player)
        {
            FindAnyObjectByType<BurnoutSystem>()?.DrinkCoffee();
            OneShotAudio.PlayAt(ProceduralAudio.Pour(), transform.position, 0.7f);
            GameEvents.RaiseCoffeeDrunk(transform.position);
            Drunk?.Invoke();
            Destroy(gameObject);
        }

        public string GetPrompt() => IsLast ? "Drink it. You've earned it." : "Drink the coffee " + GameNames.Antagonist + " made you";
    }

    // ---- wet footprints (§3.3) ----------------------------------------------------------

    // Walk through a spill and you leave a trail for 45 seconds that points where you went.
    // Mop your own trail (a short hold with the mop) or wear a tracking beacon.
    public sealed class FootprintTrail : MonoBehaviour
    {
        public int printsPerSpill = 10;
        public float printSpacing = 0.65f;
        public float lifetime = 45f;

        int printsLeft;
        Vector3 lastPrint;
        bool leftFoot;

        void Update()
        {
            Vector3 p = transform.position;

            // Stepping in anything wet recharges the trail.
            if (Dirt.AnyWithin(p, 0.8f))
            {
                printsLeft = printsPerSpill;
                lastPrint = p;
                return;
            }

            if (printsLeft <= 0) return;
            Vector3 d = p - lastPrint;
            d.y = 0f;
            if (d.magnitude < printSpacing) return;

            Vector3 heading = d.normalized;
            leftFoot = !leftFoot;
            Vector3 side = Vector3.Cross(Vector3.up, heading) * (leftFoot ? 0.12f : -0.12f);
            Footprint.Spawn(p + side, heading, lifetime, 1f - (float)(printsPerSpill - printsLeft) / printsPerSpill);
            lastPrint = p;
            printsLeft--;
        }
    }

    public sealed class Footprint : MonoBehaviour, IInteractable, IHoldInteractable
    {
        float born, lifetime, wetness;
        Renderer mark;
        Trace trace;

        public static Footprint Spawn(Vector3 at, Vector3 heading, float lifetime, float wetness)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "KAREN_Footprint";
            go.layer = LayerMask.NameToLayer("Interactable");
            go.transform.position = new Vector3(at.x, at.y + 0.015f, at.z);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, heading);
            go.transform.localScale = new Vector3(0.12f, 0.28f, 1f);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var trigger = go.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.5f, 2f, 0.5f);

            var f = go.AddComponent<Footprint>();
            f.born = Time.time;
            f.lifetime = lifetime;
            f.wetness = Mathf.Clamp01(wetness);
            f.mark = go.GetComponent<Renderer>();
            f.mark.material = KarenProps.Lit(new Color(0.12f, 0.1f, 0.08f));
            f.trace = TraceRegistry.Add(TraceKind.WetFootprint, at, lifetime, heading, f);
            return f;
        }

        void Update()
        {
            float age = (Time.time - born) / lifetime;
            if (age >= 1f) { Destroy(gameObject); return; }
            Color c = mark.material.color;
            c.a = (1f - age) * (0.35f + 0.5f * wetness);
            mark.material.color = c;
        }

        void OnDestroy()
        {
            if (trace != null) trace.Resolved = true;
        }

        public void Interact(PlayerInteract player) { }
        public string GetPrompt() => "Hold E with the mop to wipe the footprints";
        public bool CanHold(PlayerInteract player) =>
            player != null && player.carrySlot != null && player.carrySlot.IsCarrying && player.carrySlot.currentItem.type == ItemType.Mop;
        public float HoldDuration => 0.6f;
        public void OnHoldProgress(float normalised) { }
        public void OnHoldCancelled() { }

        public void OnHoldComplete(PlayerInteract player)
        {
            // Wipe this print and the ones right beside it.
            foreach (Collider c in Physics.OverlapSphere(transform.position, 1.2f, ~0, QueryTriggerInteraction.Collide))
            {
                Footprint other = c.GetComponent<Footprint>();
                if (other != null && other != this) Destroy(other.gameObject);
            }
            KarenBrain.Instance?.Ledger.RecordCounterplay("footprints_mopped");
            Destroy(gameObject);
        }
    }
}
