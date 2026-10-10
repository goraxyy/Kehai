using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kehai.Replay
{
    // What the replay draws with: the shift report's colours (so a clip, the report and the
    // marketing all look like one thing), the overlay material, and the layer Karen's mind is
    // drawn on so a shot can show it, hide it, or render it alone.
    public static class ReplayLook
    {
        public static readonly Color You = Hex(0x4dd2ff);
        public static readonly Color Karen = Hex(0xff5454);
        public static readonly Color Guess = Hex(0xffd640);
        public static readonly Color Other = Hex(0xc8c8c8);
        public static readonly Color Panel = new Color(0.059f, 0.067f, 0.086f, 0.72f);   // the report's #0f1116

        static Shader overlay;
        static int mindLayer = -1;
        static readonly Dictionary<(Color, bool), Material> flat = new Dictionary<(Color, bool), Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            overlay = null;
            mindLayer = -1;
            flat.Clear();
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        // A layer nobody uses, from the top down (the project's own layers sit at the bottom).
        // Karen's mind lives there.
        public static int MindLayer
        {
            get
            {
                if (mindLayer >= 0) return mindLayer;
                for (int i = 31; i >= 8; i--)
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { mindLayer = i; break; }
                if (mindLayer < 0) mindLayer = 31;
                return mindLayer;
            }
        }

        static Shader OverlayShader
        {
            get
            {
                if (overlay != null) return overlay;
                overlay = Resources.Load<Shader>("KehaiReplayOverlay") ?? Shader.Find("Hidden/Kehai/ReplayOverlay") ?? Shader.Find("Sprites/Default");
                return overlay;
            }
        }

        // Unlit, alpha blended, coloured per vertex. `onTop` draws it over the whole store
        // (text, the eyelids); otherwise the store hides it the way it hides anything else.
        public static Material Overlay(Texture texture = null, bool onTop = false, bool textMode = false, int queue = 3100)
        {
            var m = new Material(OverlayShader) { name = "Replay overlay", renderQueue = onTop ? 4000 + (queue - 3100) : queue };
            if (texture != null) m.mainTexture = texture;
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)(onTop ? CompareFunction.Always : CompareFunction.LessEqual));
            if (m.HasProperty("_TextMode")) m.SetFloat("_TextMode", textMode ? 1f : 0f);
            return m;
        }

        // One shared material per colour, for the many things drawn in a single flat colour.
        public static Material Flat(Color colour, bool onTop = false)
        {
            if (flat.TryGetValue((colour, onTop), out Material m) && m != null) return m;
            m = Overlay(null, onTop);
            m.color = colour;
            flat[(colour, onTop)] = m;
            return m;
        }

        // A flat ring (or a disc, with inner 0) in the XZ plane, 1 m radius.
        public static Mesh Ring(float inner, int segments = 64)
        {
            var mesh = new Mesh { name = "Replay ring" };
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d * inner);
                v.Add(d);
                if (i == segments) continue;
                int k = i * 2;
                tri.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
            }
            mesh.SetVertices(v);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject Shape(string name, Mesh mesh, Material material, Transform parent, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
