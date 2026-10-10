using System.Collections.Generic;
using System.Text;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Replay
{
    [System.Flags]
    public enum MindLayer
    {
        None = 0,
        Belief = 1,      // her belief map: where she thinks you might be, as heat on the floor
        Guess = 2,       // her single best guess, a ring (tighter the surer she is) and a pin
        Cone = 4,        // what she can see: her view cone on the floor, red while she sees you
        Sound = 8,       // every noise, a ring as far as it carries
        Thoughts = 16,   // her thought log, the last few lines
        Actors = 32,     // where you and she really are, as dots (for the picture-in-picture)
        All = 63
    }

    // "Karen's mind", drawn into the store from the recording. Everything lives on its own layer
    // (ReplayLook.MindLayer), so a shot can show it over the store, hide it, or render it alone
    // on a transparent background.
    public sealed class MindLayers
    {
        public const float SoundSeconds = 1.2f;
        public const float SoundMaxRadius = 12f;
        public const int ThoughtLines = 3;            // the last three thoughts…
        public const int LinesPerThought = 2;         // …two lines each at most
        public const float ThoughtSeconds = 20f;

        public MindLayer Shown;

        readonly ReplayStage stage;
        readonly ReplayData data;
        readonly int layer;
        readonly Transform root;
        readonly KarenConfig config = new KarenConfig();
        readonly float y;

        GameObject belief, guess, guessPin, cone, thoughts, you, her;
        readonly List<GameObject> rings = new List<GameObject>();
        Texture2D beliefTexture;
        Color32[] beliefPixels;
        BeliefFrame beliefShown;
        Mesh coneMesh;
        TextMesh text;
        GameObject panel;
        Material textMaterial;
        MaterialPropertyBlock block;
        string textShown;

        public MindLayers(ReplayStage stage, Transform parent)
        {
            this.stage = stage;
            data = stage.Data;
            layer = ReplayLook.MindLayer;
            y = stage.FloorY;
            root = new GameObject("Karen's mind (replay)") { layer = layer }.transform;
            root.SetParent(parent, false);
            block = new MaterialPropertyBlock();
            BuildBelief();
            BuildGuess();
            BuildCone();
            BuildThoughts();
            you = Dot("You", ReplayLook.You);
            her = Dot(GameNames.Antagonist, ReplayLook.Karen);
        }

        // "belief,cone" → Belief | Cone; "all", "none", "" as they say.
        public static MindLayer Parse(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return MindLayer.None;
            MindLayer m = MindLayer.None;
            foreach (string raw in list.Split(',', '+', ' '))
            {
                string s = raw.Trim().ToLowerInvariant();
                if (s.Length == 0 || s == "none") continue;
                if (s == "all" || s == "mind") { m |= MindLayer.All; continue; }
                if (s == "rings" || s == "sounds") s = "sound";
                if (s == "thought" || s == "log") s = "thoughts";
                if (s == "view" || s == "sight") s = "cone";
                if (!System.Enum.TryParse(s, true, out MindLayer one)) throw new System.ArgumentException($"unknown layer \"{raw}\" (belief, guess, cone, sound, thoughts, actors, all, none)");
                m |= one;
            }
            return m;
        }

        public static string Describe(MindLayer m)
        {
            if (m == MindLayer.None) return "none";
            if (m == MindLayer.All) return "all";
            var names = new List<string>();
            foreach (MindLayer one in new[] { MindLayer.Belief, MindLayer.Guess, MindLayer.Cone, MindLayer.Sound, MindLayer.Thoughts, MindLayer.Actors })
                if ((m & one) != 0) names.Add(one.ToString().ToLowerInvariant());
            return string.Join(",", names);
        }

        // ---- building ------------------------------------------------------------------------

        GameObject Child(string name)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(root, false);
            return go;
        }

        void BuildBelief()
        {
            KrecHeader h = data.Header;
            if (h.BeliefCols <= 0 || h.BeliefRows <= 0) return;
            beliefTexture = new Texture2D(h.BeliefCols, h.BeliefRows, TextureFormat.RGBA32, false)
            {
                name = "Her belief", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            beliefPixels = new Color32[h.BeliefCols * h.BeliefRows];
            float x0 = h.BeliefOrigin.x, z0 = h.BeliefOrigin.y, x1 = x0 + h.BeliefCols * h.BeliefCell, z1 = z0 + h.BeliefRows * h.BeliefCell;
            float at = y + 0.04f;
            var mesh = new Mesh { name = "Belief map" };
            mesh.SetVertices(new List<Vector3> { new Vector3(x0, at, z0), new Vector3(x1, at, z0), new Vector3(x0, at, z1), new Vector3(x1, at, z1) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            mesh.RecalculateBounds();
            belief = ReplayLook.Shape("Belief map", mesh, ReplayLook.Overlay(beliefTexture, queue: 3100), root, layer);
        }

        void BuildGuess()
        {
            guess = ReplayLook.Shape("Her guess", ReplayLook.Ring(0.8f), ReplayLook.Overlay(null, queue: 3102), root, layer);
            guessPin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(guessPin.GetComponent<Collider>());
            guessPin.name = "Her guess (pin)";
            guessPin.layer = layer;
            guessPin.transform.SetParent(guess.transform.parent, false);
            guessPin.GetComponent<Renderer>().sharedMaterial = ReplayLook.Overlay(null, queue: 3102);
        }

        void BuildCone()
        {
            coneMesh = new Mesh { name = "Her view" };
            coneMesh.MarkDynamic();
            cone = ReplayLook.Shape("Her view cone", coneMesh, ReplayLook.Overlay(null, queue: 3101), root, layer);
        }

        void BuildThoughts()
        {
            thoughts = Child("Her thoughts");
            panel = ReplayLook.Shape("Panel", Quad(), ReplayLook.Overlay(null, onTop: true, queue: 3100), thoughts.transform, layer);
            panel.GetComponent<Renderer>().sharedMaterial.color = ReplayLook.Panel;
            var textGo = new GameObject("Text") { layer = layer };
            textGo.transform.SetParent(thoughts.transform, false);
            text = textGo.AddComponent<TextMesh>();
            text.font = ReplayLook.Font;
            text.fontSize = 64;
            text.anchor = TextAnchor.LowerLeft;
            text.alignment = TextAlignment.Left;
            text.richText = true;
            text.color = new Color(0.91f, 0.91f, 0.93f);
            textMaterial = ReplayLook.Overlay(text.font.material.mainTexture, onTop: true, textMode: true, queue: 3101);
            textGo.GetComponent<MeshRenderer>().sharedMaterial = textMaterial;
        }

        GameObject Dot(string name, Color colour)
        {
            GameObject go = ReplayLook.Shape(name, ReplayLook.Ring(0f, 40), ReplayLook.Flat(ReplayLook.WithAlpha(colour, 0.95f)), root, layer);
            go.transform.localScale = new Vector3(0.45f, 1f, 0.45f);
            return go;
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "Replay quad" };
            m.SetVertices(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- each frame --------------------------------------------------------------------

        public void Apply(float t, Camera cam)
        {
            root.gameObject.SetActive(Shown != MindLayer.None);
            if (Shown == MindLayer.None) return;

            ShowBelief(t);
            ShowGuess(t);
            ShowCone(t);
            ShowSounds(t);
            ShowThoughts(t, cam);
            ShowActors(t);
        }

        bool On(MindLayer m) => (Shown & m) != 0;

        void ShowBelief(float t)
        {
            if (belief == null) return;
            belief.SetActive(On(MindLayer.Belief));
            if (!On(MindLayer.Belief)) return;
            BeliefFrame f = data.BeliefAt(t);
            if (f == beliefShown) return;
            beliefShown = f;
            byte[] mask = data.Header.BeliefMask;
            for (int i = 0; i < beliefPixels.Length; i++)
            {
                float v = f != null && i < f.Grid.Length && (mask == null || i >= mask.Length || mask[i] != 0) ? f.Grid[i] / 255f : 0f;
                Color c = Color.Lerp(ReplayLook.Karen, ReplayLook.Guess, v * v);
                c.a = v <= 0.02f ? 0f : 0.12f + 0.66f * v;
                beliefPixels[i] = c;
            }
            beliefTexture.SetPixels32(beliefPixels);
            beliefTexture.Apply(false);
        }

        void ShowGuess(float t)
        {
            BeliefFrame f = On(MindLayer.Guess) ? data.BeliefAt(t) : null;
            guess.SetActive(f != null);
            guessPin.SetActive(f != null);
            if (f == null) return;
            float sure = Mathf.Clamp01(f.Confidence);
            float radius = 0.6f + 2.6f * (1f - sure);
            guess.transform.SetPositionAndRotation(new Vector3(f.Peak.x, y + 0.06f, f.Peak.y), Quaternion.identity);
            guess.transform.localScale = new Vector3(radius, 1f, radius);
            Color c = ReplayLook.WithAlpha(ReplayLook.Guess, 0.45f + 0.5f * sure);
            Tint(guess, c);
            guessPin.transform.SetPositionAndRotation(new Vector3(f.Peak.x, y + 1.3f, f.Peak.y), Quaternion.identity);
            guessPin.transform.localScale = new Vector3(0.07f, 1.3f, 0.07f);
            Tint(guessPin, c);
        }

        readonly List<Vector3> coneVerts = new List<Vector3>();
        readonly List<Color> coneColours = new List<Color>();
        readonly List<int> coneTris = new List<int>();
        static readonly RaycastHit[] hits = new RaycastHit[8];

        void ShowCone(float t)
        {
            EntitySample pose = default;
            bool show = On(MindLayer.Cone) && stage.KarenId >= 0 && data.TryPose(stage.KarenId, t, out pose);
            cone.SetActive(show);
            if (!show) return;

            const int rays = 40;
            Vector3 forward = pose.Rotation * Vector3.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            float half = config.sightFov * 0.5f, range = config.sightRange;
            bool sees = (pose.State & KrecState.KarenSees) != 0;
            Color tint = sees ? ReplayLook.Karen : KarenFloorCone.Tint((KarenBody.Mood)(pose.State & 3));
            Vector3 floor = new Vector3(pose.Position.x, y + 0.05f, pose.Position.z);
            Vector3 eye = new Vector3(pose.Position.x, y + 1.3f, pose.Position.z);

            coneVerts.Clear();
            coneColours.Clear();
            coneTris.Clear();
            coneVerts.Add(floor);
            coneColours.Add(ReplayLook.WithAlpha(tint, sees ? 0.5f : 0.36f));
            for (int i = 0; i <= rays; i++)
            {
                Vector3 d = Quaternion.AngleAxis(Mathf.Lerp(-half, half, i / (float)rays), Vector3.up) * forward;
                coneVerts.Add(floor + d * Reach(eye, d, range));
                coneColours.Add(ReplayLook.WithAlpha(tint, 0.08f));
                if (i > 0) coneTris.AddRange(new[] { 0, i, i + 1 });
            }
            coneMesh.Clear();
            coneMesh.SetVertices(coneVerts);
            coneMesh.SetColors(coneColours);
            coneMesh.SetTriangles(coneTris, 0);
            coneMesh.RecalculateBounds();
        }

        // How far she sees along one direction: the store stops her eyes (puppets have no colliders).
        static float Reach(Vector3 eye, Vector3 direction, float range)
        {
            int n = Physics.RaycastNonAlloc(eye, direction, hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = range;
            for (int i = 0; i < n; i++) best = Mathf.Min(best, hits[i].distance);
            return best;
        }

        void ShowSounds(float t)
        {
            int used = 0;
            if (On(MindLayer.Sound))
            {
                for (int i = data.FirstEventAt(t - SoundSeconds); i < data.Events.Count && used < 40; i++)
                {
                    ReplayEvent e = data.Events[i];
                    if (e.T > t) break;
                    if (e.Type != KrecEvent.Noise) continue;
                    float age = (t - e.T) / SoundSeconds;
                    float reach = Mathf.Min(e.Value * NoiseBus.CarryPerUnit, SoundMaxRadius);
                    if (reach < 0.3f) continue;
                    GameObject ring = Ring(used++);
                    float r = reach * (1f - (1f - age) * (1f - age) * (1f - age));
                    ring.transform.SetPositionAndRotation(new Vector3(e.Position.x, y + 0.08f, e.Position.z), Quaternion.identity);
                    ring.transform.localScale = new Vector3(Mathf.Max(0.05f, r), 1f, Mathf.Max(0.05f, r));
                    Color c = e.Author == (int)NoiseAuthor.Player ? ReplayLook.You : e.Author == (int)NoiseAuthor.Karen ? ReplayLook.Karen : ReplayLook.Other;
                    Tint(ring, ReplayLook.WithAlpha(c, 0.8f * (1f - age)));
                }
            }
            for (int i = used; i < rings.Count; i++) rings[i].SetActive(false);
        }

        GameObject Ring(int i)
        {
            while (rings.Count <= i)
                rings.Add(ReplayLook.Shape("Sound", ReplayLook.Ring(0.93f), ReplayLook.Overlay(null, queue: 3103), root, layer));
            rings[i].SetActive(true);
            return rings[i];
        }

        void Tint(GameObject go, Color c)
        {
            Renderer r = go.GetComponent<Renderer>();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        // Her thought log, bottom left of the picture, over everything.
        void ShowThoughts(float t, Camera cam)
        {
            bool show = On(MindLayer.Thoughts) && cam != null;
            thoughts.SetActive(show);
            if (!show) return;

            // In front of the camera, sized to the picture whatever its shape.
            Transform ct = cam.transform;
            float d = Mathf.Max(cam.nearClipPlane * 2f, 0.3f);
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), w = h * cam.aspect;
            float line = h * (cam.aspect < 1f ? 0.026f : 0.03f);
            float margin = h * 0.035f;
            float width = w * (cam.aspect < 1f ? 0.9f : 0.48f);
            int columns = Mathf.Max(20, Mathf.FloorToInt(width / (line * 0.52f)));

            string s = ThoughtText(t, columns, out int lines);
            if (s != textShown)
            {
                textShown = s;
                text.text = s;
            }
            if (textMaterial.mainTexture != text.font.material.mainTexture) textMaterial.mainTexture = text.font.material.mainTexture;
            panel.SetActive(lines > 0);

            thoughts.transform.SetPositionAndRotation(ct.position + ct.forward * d, ct.rotation);
            float left = -w * 0.5f + margin, bottom = -h * 0.5f + margin * (cam.aspect < 1f ? 5f : 1.6f);
            text.characterSize = line * 10f / text.fontSize;
            text.lineSpacing = 1.05f;
            text.transform.localPosition = new Vector3(left + line * 0.6f, bottom + line * 0.5f, 0f);
            float tall = lines * line * 1.12f + line;
            panel.transform.localPosition = new Vector3(left, bottom, 0.001f);
            panel.transform.localScale = new Vector3(width, tall, 1f);
        }

        readonly List<string> thoughtLines = new List<string>();

        string ThoughtText(float t, int columns, out int lines)
        {
            thoughtLines.Clear();
            for (int i = data.FirstEventAt(t + 1e-4f) - 1; i >= 0 && thoughtLines.Count < ThoughtLines; i--)
            {
                ReplayEvent e = data.Events[i];
                if (t - e.T > ThoughtSeconds) break;
                if (e.Type != KrecEvent.Thought) continue;
                thoughtLines.Add($"<color=#ffd640>{e.Extra}</color>  {Plain(e.Text, e.Extra)}");
            }
            thoughtLines.Reverse();
            var sb = new StringBuilder();
            lines = 0;
            if (thoughtLines.Count > 0)
            {
                sb.Append("<color=#ff5454>").Append(GameNames.Antagonist.ToUpperInvariant()).Append(" THINKS</color>");
                lines++;
            }
            foreach (string l in thoughtLines)
            {
                int n = 0;
                foreach (string wrapped in Wrap(l, columns))
                {
                    if (++n > LinesPerThought)
                    {
                        sb.Append('…');
                        break;
                    }
                    sb.Append('\n').Append(wrapped);
                    lines++;
                }
            }
            return sb.ToString();
        }

        // A logged line without the time and the kind it starts with ("[ 137.6] PLAN chase…").
        static string Plain(string text, string kind)
        {
            string s = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"^\s*\[\s*[-\d.:]+\s*\]\s*", "");
            if (!string.IsNullOrEmpty(kind) && s.StartsWith(kind, System.StringComparison.OrdinalIgnoreCase)) s = s.Substring(kind.Length).TrimStart(' ', ':');
            return s.Replace("<", "‹").Replace(">", "›");
        }

        // Word wrap that doesn't count rich-text tags.
        static IEnumerable<string> Wrap(string s, int columns)
        {
            var line = new StringBuilder();
            int visible = 0;
            foreach (string word in s.Split(' '))
            {
                int length = System.Text.RegularExpressions.Regex.Replace(word, "<[^>]+>", "").Length;
                if (visible > 0 && visible + 1 + length > columns)
                {
                    yield return line.ToString();
                    line.Clear();
                    line.Append("   ");
                    visible = 3;
                }
                if (visible > 0 && line.Length > 0 && line[line.Length - 1] != ' ') { line.Append(' '); visible++; }
                line.Append(word);
                visible += length;
            }
            if (line.Length > 0) yield return line.ToString();
        }

        void ShowActors(float t)
        {
            bool show = On(MindLayer.Actors);
            Place(you, show && stage.PlayerId >= 0 && data.TryPose(stage.PlayerId, t, out EntitySample p) ? p.Position : (Vector3?)null);
            Place(her, show && stage.KarenId >= 0 && data.TryPose(stage.KarenId, t, out EntitySample a) ? a.Position : (Vector3?)null);
        }

        void Place(GameObject dot, Vector3? at)
        {
            dot.SetActive(at.HasValue);
            if (at.HasValue) dot.transform.position = new Vector3(at.Value.x, y + 0.07f, at.Value.z);
        }
    }
}
