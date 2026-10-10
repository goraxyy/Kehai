using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Where Karen is looking, painted on the floor: her field of view out to her sight
    // range, cut short by shelves and walls the way her eyes are, brightest where she sees
    // best (close and straight ahead) and fading where she'd barely notice you. Its colour
    // is her mood, and it pulses red while she can see you. Her spotlight shows the same
    // thing, but a spotlight is hard to read in a lit store. Esc → Settings turns it off.
    [RequireComponent(typeof(KarenBody))]
    public sealed class KarenFloorCone : MonoBehaviour
    {
        const int Rays = 48;
        const float EyeHeight = 1.3f;   // over counters, not over shelves
        const float Lift = 0.03f;       // just above the floor
        const float RimWidth = 0.12f;
        static readonly float[] Rings = { 0.25f, 0.55f, 1f };

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("Kehai.KarenCone", 1) == 1;
            set => PlayerPrefs.SetInt("Kehai.KarenCone", value ? 1 : 0);
        }

        KarenBody body;
        Mesh mesh;
        MeshRenderer view;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colours = new List<Color>();
        readonly List<int> triangles = new List<int>();
        readonly List<Vector3> rim = new List<Vector3>();
        readonly float[] reach = new float[Rays + 1];
        readonly Vector3[] directions = new Vector3[Rays + 1];
        static readonly RaycastHit[] hits = new RaycastHit[8];
        static Material material;

        void Start()
        {
            body = GetComponent<KarenBody>();
            if (Application.isBatchMode) { enabled = false; return; }   // nothing to see headless

            var cone = new GameObject("Floor cone");
            cone.transform.SetParent(transform, false);
            mesh = new Mesh { name = GameNames.Antagonist + " floor cone" };
            mesh.MarkDynamic();
            cone.AddComponent<MeshFilter>().sharedMesh = mesh;
            view = cone.AddComponent<MeshRenderer>();
            view.sharedMaterial = Material;
            view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.receiveShadows = false;
        }

        void LateUpdate()
        {
            SightSensor sight = body.Sight;
            bool show = Enabled && sight != null;
            view.enabled = show;
            if (!show) return;

            Vector3 forward = sight.Forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = transform.forward;
            forward.Normalize();

            float half = sight.HalfAngle, range = sight.range;
            Vector3 floor = transform.position + Vector3.up * Lift;
            Vector3 eye = transform.position + Vector3.up * EyeHeight;
            for (int i = 0; i <= Rays; i++)
            {
                directions[i] = Quaternion.AngleAxis(Mathf.Lerp(-half, half, i / (float)Rays), Vector3.up) * forward;
                reach[i] = Reach(eye, directions[i], range);
            }

            bool seeing = sight.Awareness >= sight.seeAt;
            Color tint = seeing ? new Color(1f, 0.12f, 0.08f) : Tint(body.CurrentMood);
            float strength = seeing ? 0.62f + 0.18f * Mathf.Sin(Time.time * 10f) : 0.5f;

            Transform local = view.transform;
            vertices.Clear();
            colours.Clear();
            triangles.Clear();

            vertices.Add(local.InverseTransformPoint(floor));
            colours.Add(Fade(tint, strength));
            foreach (float ring in Rings)
            {
                for (int i = 0; i <= Rays; i++)
                {
                    float d = reach[i] * ring;
                    vertices.Add(local.InverseTransformPoint(floor + directions[i] * d));
                    // As her eyes weigh it: best straight ahead and close, weaker to the sides and far off.
                    float side = Mathf.Abs(i / (float)Rays * 2f - 1f);
                    float weight = Mathf.Max(0.3f, 1f - side * side) / (1f + (d / 8f) * (d / 8f));
                    colours.Add(Fade(tint, strength * Mathf.Max(0.3f, weight)));
                }
            }
            for (int i = 0; i < Rays; i++)
            {
                triangles.Add(0);
                triangles.Add(1 + i);
                triangles.Add(2 + i);
            }
            for (int r = 0; r + 1 < Rings.Length; r++)
            {
                int a = 1 + r * (Rays + 1), b = a + Rays + 1;
                for (int i = 0; i < Rays; i++) Quad(a + i, a + i + 1, b + i + 1, b + i);
            }

            // A crisp rim, so where her sight ends is easy to read at a glance.
            rim.Clear();
            rim.Add(floor);
            for (int i = 0; i <= Rays; i++) rim.Add(floor + directions[i] * reach[i]);
            rim.Add(floor);
            Color edge = Fade(tint, Mathf.Min(0.95f, strength * 1.8f));
            for (int i = 0; i + 1 < rim.Count; i++)
            {
                Vector3 along = rim[i + 1] - rim[i];
                if (along.sqrMagnitude < 1e-6f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, along.normalized) * (RimWidth * 0.5f);
                int v = vertices.Count;
                vertices.Add(local.InverseTransformPoint(rim[i] - side));
                vertices.Add(local.InverseTransformPoint(rim[i + 1] - side));
                vertices.Add(local.InverseTransformPoint(rim[i + 1] + side));
                vertices.Add(local.InverseTransformPoint(rim[i] + side));
                for (int k = 0; k < 4; k++) colours.Add(edge);
                Quad(v, v + 1, v + 2, v + 3);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        void Quad(int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        // How far she can see along one direction: the store stops her eyes, people don't.
        static float Reach(Vector3 eye, Vector3 direction, float range)
        {
            int n = Physics.RaycastNonAlloc(eye, direction, hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = range;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (c.GetComponentInParent<CharacterController>() != null || c.GetComponentInParent<NavMeshAgent>() != null) continue;
                best = Mathf.Min(best, hits[i].distance);
            }
            return best;
        }

        internal static Color Tint(KarenBody.Mood mood)
        {
            switch (mood)
            {
                case KarenBody.Mood.Alert: return new Color(1f, 0.62f, 0.1f);
                case KarenBody.Mood.Hunt: return new Color(1f, 0.15f, 0.1f);
                case KarenBody.Mood.Kind: return new Color(0.35f, 0.95f, 0.55f);
                default: return new Color(0.15f, 0.5f, 1f);
            }
        }

        static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));

        // Unlit, see-through and coloured per vertex: the sprite shader does exactly that.
        static Material Material
        {
            get
            {
                if (material != null) return material;
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
                material = new Material(shader) { name = GameNames.Antagonist + " floor cone", renderQueue = 3000 };
                return material;
            }
        }
    }
}
