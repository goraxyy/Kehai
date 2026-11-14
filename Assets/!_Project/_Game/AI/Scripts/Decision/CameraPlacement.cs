using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Camera bolt-on's choice (0.3.0 build plan C3.5): which shelf slot to stand a camera in, and
    // which way to point it, so it sees where you usually are.
    //
    //   Heat     — the cells she wants watched: where your habits put you (the belief grid's
    //              habit layer), and, less, where she thinks you are now.
    //   Slots    — shelf slots on the sales floor 3–16 m from that heat, empty ones first
    //              (a full one loses its product to make room).
    //   Angles   — 8 turns across the slot's front half and 3 tilts (0°, −10°, −20°).
    //   Score    — how much of the heat a camera there, pointed that way, would see: the cone
    //              and range of her camera's eye, line of sight through the shelving, and the
    //              eye's fall-off with distance. Then the placement memory has its say.
    public static class CameraPlacement
    {
        public const float MinRange = 3f, MaxRange = 16f;
        public static readonly float[] Yaws = { -70f, -50f, -30f, -10f, 10f, 30f, 50f, 70f };
        public static readonly float[] Pitches = { 0f, -10f, -20f };
        public const float FieldOfView = 70f;      // as CctvCamera's eye
        public const float HalfStrengthAt = 8f;    // as a SightSensor's default
        public const float ChestHeight = 1.2f;
        public const int MaxHeatCells = 48;
        public const int MaxSlots = 24;            // slots looked at closely per choice
        public const int MaxOptions = 12;          // places handed to the placement memory
        public const float EmptyBonus = 0.05f;

        public struct Option
        {
            public ShelfSlot Slot;
            public float Yaw, Pitch;
            public float Heat;      // 0..1: the share of the heat it would see
            public string Key;      // the place, for the placement memory
        }

        public readonly struct HeatCell
        {
            public readonly Vector3 At;    // chest height
            public readonly float W;
            public HeatCell(Vector3 at, float w) { At = at; W = w; }
        }

        // The cells she'd want watched, most first.
        public static List<HeatCell> Heat(KarenContext c)
        {
            var result = new List<HeatCell>();
            if (c.Map == null || c.Belief == null) return result;
            float[] habit = c.Belief.HabitPrior;
            float maxBelief = 1e-6f;
            for (int i = 0; i < c.Belief.CellCount; i++) maxBelief = Mathf.Max(maxBelief, c.Belief[i]);

            // With the destination model (P2, P3), her guessed routes are heat too (C4.6).
            float[] routes = null;
            if (c.Belief.Motion is DestinationMotion && c.Brain != null && c.Brain.Predictor != null && c.Brain.Predictor.Active)
            {
                routes = new float[c.Map.CellCount];
                foreach (RouteGuess g in c.Brain.Predictor.Guesses)
                    foreach (int cell in g.Path) routes[cell] = Mathf.Max(routes[cell], g.Weight);
            }

            var all = new List<HeatCell>();
            for (int i = 0; i < c.Belief.CellCount && i < c.Map.CellCount; i++)
            {
                if (!c.Map.IsKarens(i)) continue;
                float w = (habit != null && i < habit.Length ? habit[i] : 0f) + 0.5f * c.Belief[i] / maxBelief;
                if (routes != null) w += routes[i];
                if (w < 0.05f) continue;
                all.Add(new HeatCell(c.Map.CellPosition[i] + Vector3.up * ChestHeight, w));
            }
            all.Sort((a, b) => b.W.CompareTo(a.W));
            for (int i = 0; i < all.Count && i < MaxHeatCells; i++) result.Add(all[i]);
            return result;
        }

        // Where a camera in this slot, turned `yaw` and tilted `pitch`, would point.
        public static Vector3 Forward(Vector3 outward, float yaw, float pitch)
        {
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = Vector3.forward;
            return Quaternion.LookRotation(outward.normalized, Vector3.up) * Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        }

        // The share of the heat an eye at `eye`, looking along `forward`, would see. `visible`
        // says which heat cells it has a line of sight to (the rays are cast once per slot). Pure.
        public static float Coverage(Vector3 eye, Vector3 forward, IList<HeatCell> heat, IList<bool> visible)
        {
            float total = 0f, seen = 0f;
            for (int i = 0; i < heat.Count; i++)
            {
                total += heat[i].W;
                if (visible != null && !visible[i]) continue;
                Vector3 d = heat[i].At - eye;
                float distance = d.magnitude;
                if (distance < MinRange || distance > MaxRange) continue;
                if (Vector3.Angle(forward, d) > FieldOfView * 0.5f) continue;
                float falloff = 1f / (1f + (distance / HalfStrengthAt) * (distance / HalfStrengthAt));
                seen += heat[i].W * falloff;
            }
            return total > 0f ? Mathf.Clamp01(seen / total) : 0f;
        }

        public static string Key(ShelfSlot slot, float yaw, float pitch) =>
            $"{(slot.owner != null ? slot.owner.name : "shelf")} · slot {slot.Index} · {yaw:0}° · {pitch:0}°";

        // The best few places to put a camera, best first. `slots` defaults to every slot in the
        // store; `clear` to a physics ray (tests pass their own).
        public static List<Option> Options(KarenContext c, IEnumerable<ShelfSlot> slots = null,
                                           System.Func<Vector3, Vector3, bool> clear = null)
        {
            var options = new List<Option>();
            List<HeatCell> heat = Heat(c);
            if (heat.Count == 0) return options;
            clear = clear ?? LineOfSight.Clear;

            Vector3 centre = Vector3.zero;
            float weight = 0f;
            foreach (HeatCell h in heat) { centre += h.At * h.W; weight += h.W; }
            centre /= Mathf.Max(1e-4f, weight);

            // The slots near the heat, nearest first.
            var near = new List<(ShelfSlot slot, float d)>();
            IEnumerable<ShelfSlot> pool = slots ?? AllSlots();
            foreach (ShelfSlot s in pool)
            {
                if (s == null || s.IsBlocked) continue;
                Vector3 p = s.Position;
                if (StoreMap.AreaAt(p) != "Sales floor") continue;
                float d = Vector3.Distance(p, centre);
                if (d > MaxRange + 6f) continue;
                near.Add((s, d - (s.isFilled ? 0f : 3f)));   // empty slots first
            }
            near.Sort((a, b) => a.d.CompareTo(b.d));

            var visible = new bool[heat.Count];
            for (int n = 0; n < near.Count && n < MaxSlots; n++)
            {
                ShelfSlot slot = near[n].slot;
                Vector3 eye = slot.Position + Vector3.up * 0.09f;
                Vector3 from = eye + Flat(slot.Outward) * 0.2f;   // out of its own shelving
                bool any = false;
                for (int i = 0; i < heat.Count; i++)
                {
                    float dd = Vector3.Distance(eye, heat[i].At);
                    visible[i] = dd >= MinRange && dd <= MaxRange && clear(from, heat[i].At);
                    any |= visible[i];
                }
                if (!any) continue;

                foreach (float yaw in Yaws)
                    foreach (float pitch in Pitches)
                    {
                        float h = Coverage(eye, Forward(slot.Outward, yaw, pitch), heat, visible);
                        if (h <= 0.02f) continue;
                        if (!slot.isFilled) h = Mathf.Min(1f, h + EmptyBonus);
                        options.Add(new Option { Slot = slot, Yaw = yaw, Pitch = pitch, Heat = h, Key = Key(slot, yaw, pitch) });
                    }
            }

            options.Sort((a, b) => b.Heat.CompareTo(a.Heat));
            // One angle per slot among the best, so the memory chooses between places, not
            // between near-identical turns of the same one.
            var best = new List<Option>();
            var used = new HashSet<ShelfSlot>();
            foreach (Option o in options)
            {
                if (best.Count >= MaxOptions) break;
                if (used.Add(o.Slot)) best.Add(o);
            }
            return best;
        }

        static IEnumerable<ShelfSlot> AllSlots()
        {
            if (ShelfStock.Current == null) yield break;
            foreach (ShelfSlot s in ShelfSlot.All) yield return s;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
        }
    }
}
