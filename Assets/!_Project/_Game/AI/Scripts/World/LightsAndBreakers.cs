using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // The store's lights, split into the circuits Karen can trip (Karen.md §8.1).
    public enum LightCircuit { East, West, Back }

    public sealed class LightControl : MonoBehaviour
    {
        public static LightControl Instance { get; private set; }

        readonly Dictionary<Light, LightCircuit> circuit = new Dictionary<Light, LightCircuit>();
        readonly HashSet<Light> killed = new HashSet<Light>();   // lights she put out one at a time

        void Awake()
        {
            Instance = this;
            foreach (Light light in LightProbe.CeilingLights)
                circuit[light] = CircuitOf(light.transform.position);

            PowerSystem.PowerChanged += OnPower;
        }

        void OnDestroy()
        {
            PowerSystem.PowerChanged -= OnPower;
            if (Instance == this) Instance = null;
        }

        public static LightCircuit CircuitOf(Vector3 p)
        {
            if (p.z < -175.5f || p.x < 25f) return LightCircuit.Back;
            return p.x >= 55f ? LightCircuit.East : LightCircuit.West;
        }

        // The mains coming back resets anything she did to individual lamps, but a tripped
        // circuit stays dark until its breaker is thrown.
        void OnPower(bool on)
        {
            if (!on) return;
            killed.Clear();
            ApplyCircuits();
        }

        public void ApplyCircuits()
        {
            BreakerPanel panel = BreakerPanel.Instance;
            foreach (KeyValuePair<Light, LightCircuit> pair in circuit)
            {
                if (pair.Key == null) continue;
                bool on = PowerSystem.PowerOn && (panel == null || panel.IsOn(pair.Value)) && !killed.Contains(pair.Key);
                pair.Key.enabled = on;
            }
        }

        public IEnumerator Flicker(Light light, float seconds)
        {
            if (light == null) yield break;
            float end = Time.time + seconds;
            while (Time.time < end && light != null)
            {
                light.enabled = !light.enabled;
                yield return new WaitForSeconds(Random.Range(0.03f, 0.12f));
            }
            if (light != null) light.enabled = PowerSystem.PowerOn && !killed.Contains(light);
        }

        public void FlickerCircuit(LightCircuit which, float seconds)
        {
            foreach (KeyValuePair<Light, LightCircuit> pair in circuit)
                if (pair.Value == which && pair.Key != null && pair.Key.enabled && Random.value < 0.35f)
                    StartCoroutine(Flicker(pair.Key, seconds));
        }

        public void FlickerAll(float seconds)
        {
            foreach (Light light in circuit.Keys)
                if (light != null && light.enabled && Random.value < 0.3f)
                    StartCoroutine(Flicker(light, seconds));
        }

        // Mirror-black: one light, directly above.
        public void Kill(Light light)
        {
            if (light == null) return;
            killed.Add(light);
            light.enabled = false;
        }

        public int KillWithin(Vector3 centre, float radius)
        {
            int n = 0;
            float sqr = radius * radius;
            foreach (Light light in circuit.Keys)
            {
                if (light == null || !light.enabled) continue;
                Vector3 d = light.transform.position - centre;
                d.y = 0f;
                if (d.sqrMagnitude > sqr) continue;
                Kill(light);
                n++;
            }
            return n;
        }
    }

    // The breaker box out in the backstreet, rebuilt as a puzzle (Karen.md §8.1, §3.5).
    //
    // Three light circuits, each with its own hum when you look at it. After a blackout
    // they have to go back on in rising pitch — low, middle, high — in the dark, by ear.
    // Get it wrong and everything you'd already thrown trips again.
    //
    // A fourth switch jams the PA. The panel can only carry two of the three light
    // circuits while the jammer is drawing power, so silencing Karen costs you a wing of
    // the store.
    public sealed class BreakerPanel : MonoBehaviour
    {
        public static BreakerPanel Instance { get; private set; }

        public ElectricBox box;
        readonly bool[] on = { true, true, true };
        readonly int[] pitch = { 0, 1, 2 };
        readonly List<BreakerSwitch> switches = new List<BreakerSwitch>();
        BreakerSwitch jammerSwitch;

        public bool PaJammed { get; private set; }
        public bool AllOn => on[0] && on[1] && on[2];
        public int TrippedCount => (on[0] ? 0 : 1) + (on[1] ? 0 : 1) + (on[2] ? 0 : 1);
        public float LastFullReset { get; private set; } = -1f;
        public float BlackoutStarted { get; private set; } = -1f;
        public int Mistakes { get; private set; }

        public event System.Action<float> BlackoutResolved;   // seconds it took

        public bool IsOn(LightCircuit c) => on[(int)c];

        void Awake()
        {
            Instance = this;
            if (box == null) box = FindAnyObjectByType<ElectricBox>();
            if (box != null) BuildSwitches();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void BuildSwitches()
        {
            // Mount the switches on the face of the box that looks back into the building.
            Bounds bounds = new Bounds(box.transform.position + Vector3.up, Vector3.one);
            foreach (Renderer r in box.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);

            // Face whichever side of the box you can actually stand on.
            Vector3 centre = new Vector3(bounds.center.x, 0f, bounds.center.z);
            Vector3 facing = box.transform.forward;
            var map = Kehai.Store.StoreMap.Current;
            int cell = map.CellAt(centre, 4f);
            if (cell >= 0)
            {
                Vector3 toFloor = map.CellPosition[cell] - centre;
                toFloor.y = 0f;
                if (toFloor.sqrMagnitude > 0.01f)
                    facing = Mathf.Abs(toFloor.x) > Mathf.Abs(toFloor.z)
                        ? new Vector3(Mathf.Sign(toFloor.x), 0f, 0f)
                        : new Vector3(0f, 0f, Mathf.Sign(toFloor.z));
            }
            facing.y = 0f;
            facing.Normalize();
            Vector3 across = Vector3.Cross(Vector3.up, facing).normalized;
            float depth = Mathf.Abs(Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(facing.x), 0f, Mathf.Abs(facing.z))));
            Vector3 face = centre + facing * (depth + 0.05f);

            for (int i = 0; i < 3; i++)
            {
                Vector3 p = face + Vector3.up * 1.35f + across * ((i - 1.5f) * 0.28f);
                switches.Add(BreakerSwitch.Create(this, i, p, facing, new Color(0.9f, 0.75f, 0.2f)));
            }
            Vector3 jp = face + Vector3.up * 1.35f + across * (1.5f * 0.28f);
            jammerSwitch = BreakerSwitch.Create(this, 3, jp, facing, new Color(0.9f, 0.25f, 0.2f));
            RefreshVisuals();
        }

        // ---- Karen's side ------------------------------------------------------

        public void TripAll(KarenRng rng)
        {
            for (int i = 0; i < 3; i++) on[i] = false;
            Shuffle(rng);
            BlackoutStarted = Time.time;
            LightControl.Instance?.ApplyCircuits();
            RefreshVisuals();
        }

        public void Trip(LightCircuit circuit)
        {
            on[(int)circuit] = false;
            if (BlackoutStarted < 0f) BlackoutStarted = Time.time;
            LightControl.Instance?.ApplyCircuits();
            RefreshVisuals();
        }

        void Shuffle(KarenRng rng)
        {
            for (int i = 2; i > 0; i--)
            {
                int j = rng != null ? rng.Range(0, i + 1) : Random.Range(0, i + 1);
                (pitch[i], pitch[j]) = (pitch[j], pitch[i]);
            }
        }

        // ---- the player's side ---------------------------------------------------

        public int PitchOf(int index) => index < 3 ? pitch[index] : 3;
        public bool SwitchOn(int index) => index < 3 ? on[index] : PaJammed;

        public void Flip(int index, Vector3 at)
        {
            if (index == 3) { FlipJammer(at); return; }

            if (on[index])
            {
                // Throwing a live breaker off is allowed — it's your store too.
                on[index] = false;
                OneShotAudio.PlayAt(ProceduralAudio.BreakerThrow(), at);
            }
            else if (!CanCarryAnother())
            {
                OneShotAudio.PlayAt(ProceduralAudio.ErrorBuzz(), at);
                return;
            }
            else if (IsNextInOrder(index))
            {
                on[index] = true;
                OneShotAudio.PlayAt(ProceduralAudio.BreakerThrow(), at);
            }
            else
            {
                // Wrong order: everything already thrown trips again.
                for (int i = 0; i < 3; i++) on[i] = false;
                Mistakes++;
                OneShotAudio.PlayAt(ProceduralAudio.ErrorBuzz(), at);
            }

            NoiseBus.Emit(at, 0.5f, NoiseKind.Environmental, NoiseAuthor.Player);
            Settle();
        }

        // Only the circuits that are still off have to go in rising pitch among themselves.
        bool IsNextInOrder(int index)
        {
            for (int i = 0; i < 3; i++)
                if (!on[i] && pitch[i] < pitch[index]) return false;
            return true;
        }

        bool CanCarryAnother()
        {
            int live = (on[0] ? 1 : 0) + (on[1] ? 1 : 0) + (on[2] ? 1 : 0);
            return !PaJammed || live < 2;
        }

        void FlipJammer(Vector3 at)
        {
            PaJammed = !PaJammed;
            if (PaJammed)
            {
                // The load limit: switching the jammer in drops a light circuit.
                int live = (on[0] ? 1 : 0) + (on[1] ? 1 : 0) + (on[2] ? 1 : 0);
                if (live > 2)
                    for (int i = 2; i >= 0; i--)
                        if (on[i]) { on[i] = false; break; }
                KarenWorld.Instance?.Pa.Clear();
                KarenBrain.Instance?.Ledger.RecordCounterplay("pa_jammed");
            }
            OneShotAudio.PlayAt(ProceduralAudio.BreakerThrow(), at);
            Settle();
        }

        // The panel carries three light circuits, or two while the jammer draws power. Once
        // it is carrying all it can, the mains come back.
        void Settle()
        {
            int needed = PaJammed ? 2 : 3;
            bool resolved = CountOn() >= needed;
            if (resolved && !PowerSystem.PowerOn) PowerSystem.Instance?.RestorePower();

            LightControl.Instance?.ApplyCircuits();
            RefreshVisuals();

            if (resolved && BlackoutStarted > 0f)
            {
                BlackoutResolved?.Invoke(Time.time - BlackoutStarted);
                BlackoutStarted = -1f;
                LastFullReset = Time.time;
            }
        }

        int CountOn() => (on[0] ? 1 : 0) + (on[1] ? 1 : 0) + (on[2] ? 1 : 0);

        void RefreshVisuals()
        {
            foreach (BreakerSwitch s in switches) if (s != null) s.Refresh();
            if (jammerSwitch != null) jammerSwitch.Refresh();
        }

        public void ResetForShift()
        {
            for (int i = 0; i < 3; i++) on[i] = true;
            PaJammed = false;
            BlackoutStarted = -1f;
            Mistakes = 0;
            LightControl.Instance?.ApplyCircuits();
            RefreshVisuals();
        }

        public string Prompt(int index)
        {
            if (index == 3) return PaJammed ? "Un-jam the PA" : "Jam the PA (costs a light circuit)";
            string name = ((LightCircuit)index).ToString().ToLowerInvariant();
            return on[index] ? $"Throw the {name} lights off" : $"Throw the {name} lights on";
        }
    }

    // One switch on the panel. Looking at it plays its hum; that's the whole puzzle.
    public sealed class BreakerSwitch : MonoBehaviour, IInteractable, IHoverable
    {
        BreakerPanel panel;
        int index;
        Renderer lamp;
        Color colour;
        float lastHum;

        public static BreakerSwitch Create(BreakerPanel panel, int index, Vector3 position, Vector3 facing, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = index < 3 ? $"Breaker_{(LightCircuit)index}" : "Breaker_PAJammer";
            go.layer = LayerMask.NameToLayer("Interactable");
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(facing);
            go.transform.localScale = new Vector3(0.16f, 0.24f, 0.08f);

            var s = go.AddComponent<BreakerSwitch>();
            s.panel = panel;
            s.index = index;
            s.colour = colour;
            s.lamp = go.GetComponent<Renderer>();
            s.lamp.sharedMaterial = KarenProps.Lit(colour);
            return s;
        }

        public void Refresh()
        {
            if (lamp == null) return;
            bool on = panel.SwitchOn(index);
            lamp.material.color = on ? colour : colour * 0.25f;
            transform.localScale = new Vector3(0.16f, on ? 0.24f : 0.18f, 0.08f);
        }

        public void Interact(PlayerInteract player) => panel.Flip(index, transform.position);
        public string GetPrompt() => panel.Prompt(index);

        public void OnHoverEnter()
        {
            if (Time.time - lastHum < 0.4f) return;
            lastHum = Time.time;
            OneShotAudio.PlayAt(ProceduralAudio.Hum(panel.PitchOf(index)), transform.position, 0.9f);
        }

        public void OnHoverExit() { }
    }
}
