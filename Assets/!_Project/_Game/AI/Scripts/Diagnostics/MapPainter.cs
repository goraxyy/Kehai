using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Draws the store and everyone in it with IMGUI — the F1 live map and the F2 replay.
    // The shapes and colours here are the same ones the shift report uses in the browser:
    //
    //   you              cyan arrow, pointing where you look
    //   Karen            red diamond with her view cone; flashing when she's chasing
    //   her guess        dashed yellow ring where she thinks you are (smaller = surer)
    //   customers        dots: grey shopping, amber heading to the till, orange waiting (with
    //                    seconds), purple "?" asking for directions — with a dotted line to the
    //                    shelf they want, which glows purple — green following you, red when
    //                    Karen has taken one over
    //   sounds           rings spreading from where they were made: cyan yours, red hers,
    //                    grey customers', violet the store's; bigger = heard further away
    //   warnings         yellow "!" where one of her tricks is about to happen
    //   jobs             brown blobs spills, orange outlines empty shelves, bins green→red as
    //                    they fill, dark squares rubbish bags, red crosses locked doors
    public sealed class MapPainter
    {
        public static readonly Color You = new Color32(77, 210, 255, 255);
        public static readonly Color KarenRed = new Color32(255, 84, 84, 255);
        public static readonly Color Guess = new Color32(255, 214, 64, 255);
        public static readonly Color Shopper = new Color32(160, 168, 178, 255);
        public static readonly Color ToTill = new Color32(242, 201, 76, 255);
        public static readonly Color Waiting = new Color32(242, 140, 50, 255);
        public static readonly Color Asking = new Color32(187, 107, 217, 255);
        public static readonly Color Following = new Color32(64, 200, 110, 255);
        public static readonly Color Leaving = new Color32(95, 102, 112, 255);
        public static readonly Color Possessed = new Color32(230, 40, 60, 255);
        public static readonly Color SoundYou = You;
        public static readonly Color SoundKaren = KarenRed;
        public static readonly Color SoundCustomer = new Color32(200, 200, 200, 255);
        public static readonly Color SoundStore = new Color32(180, 140, 255, 255);
        public static readonly Color Spill = new Color32(150, 100, 45, 255);
        public static readonly Color EmptyShelf = new Color32(255, 150, 40, 255);
        public static readonly Color Warning = new Color32(255, 230, 60, 255);

        readonly StoreFloorPlan plan;
        readonly Texture2D planTexture;
        public Rect Area { get; private set; }
        public float Scale { get; private set; }     // screen pixels per metre

        static Texture2D circle, ring, arrow, cone, cross, soft;
        Texture2D heat;
        Color32[] heatPixels;

        public MapPainter(StoreFloorPlan plan)
        {
            this.plan = plan;
            planTexture = FloorPlanTexture.Render(plan, 8f);
            EnsureSprites();
        }

        // Fit the map into `space`, keeping its shape.
        public void Layout(Rect space)
        {
            Rect b = plan.Bounds;
            Scale = Mathf.Min(space.width / b.width, space.height / b.height);
            float w = b.width * Scale, h = b.height * Scale;
            Area = new Rect(space.x + (space.width - w) * 0.5f, space.y + (space.height - h) * 0.5f, w, h);
        }

        public Vector2 ToScreen(Vector2 world) =>
            new Vector2(Area.x + (world.x - plan.Bounds.xMin) * Scale, Area.y + (plan.Bounds.yMax - world.y) * Scale);

        public float Px(float metres) => metres * Scale;

        // ---- the whole picture ------------------------------------------------------------

        public void Draw(ShiftFrame f, IEnumerable<ShiftEvent> events, float now, bool fromWallClock, int fontSize, bool showRooms = true)
        {
            GUI.DrawTexture(Area, planTexture);
            if (!f.Power) Fill(Area, new Color(0f, 0f, 0.05f, 0.5f));

            if (showRooms)
                foreach (StoreFloorPlan.Label room in plan.Rooms)
                    Text(ToScreen(room.At), room.Text, new Color(1f, 1f, 1f, 0.35f), fontSize, TextAnchor.MiddleCenter);

            // Jobs.
            StoreMap map = StoreMap.Current;
            foreach (int bay in f.EmptyBays) OutlineBay(bay, EmptyShelf, 2f);
            foreach (Vector2 s in f.Spills) Sprite(circle, ToScreen(s), Mathf.Max(8f, Px(1.4f)), Spill);
            foreach (Vector4 bin in f.Bins)
            {
                float full = bin.w > 0 ? Mathf.Clamp01(bin.z / bin.w) : 0f;
                Color c = Color.Lerp(new Color(0.3f, 0.8f, 0.4f), new Color(0.95f, 0.25f, 0.2f), full);
                Square(ToScreen(new Vector2(bin.x, bin.y)), Mathf.Max(7f, Px(0.9f)), c);
            }
            foreach (Vector2 bag in f.Bags) Square(ToScreen(bag), Mathf.Max(6f, Px(0.7f)), new Color(0.25f, 0.2f, 0.3f));
            foreach (Vector2 door in f.LockedDoors) Sprite(cross, ToScreen(door), Mathf.Max(12f, Px(2f)), KarenRed);
            foreach (Vector4 prop in f.Props)
            {
                Vector2 at = ToScreen(new Vector2(prop.y, prop.z));
                switch ((PropKind)(int)prop.x)
                {
                    case PropKind.Crates: Square(at, Mathf.Max(10f, Px(1.6f)), new Color(0.55f, 0.35f, 0.15f)); break;
                    case PropKind.Fog: Sprite(soft, at, Px(prop.w * 2f), new Color(0.85f, 0.9f, 1f, 0.45f)); break;
                    case PropKind.Camera: Square(at, Mathf.Max(7f, Px(0.8f)), Color.white); break;
                    case PropKind.Coffee: Sprite(circle, at, Mathf.Max(6f, Px(0.6f)), new Color(0.8f, 0.6f, 0.4f)); break;
                }
            }

            // Sounds and warnings, fading.
            foreach (ShiftEvent e in events)
            {
                if (!e.HasPlace) continue;
                float age = now - (fromWallClock ? e.WallTime : e.T);
                if (age < 0f) continue;
                Vector2 at = ToScreen(e.At);
                if (e.Kind == "sound" && age < 1.6f)
                {
                    float grow = Mathf.Min(e.Radius, 25f) * (0.25f + 0.75f * age / 1.6f);
                    Color c = SoundColour(e.Who);
                    c.a = 0.85f * (1f - age / 1.6f);
                    Sprite(ring, at, Mathf.Max(10f, Px(grow * 2f)), c);
                }
                else if (e.Kind == nameof(StoryKind.Warning) && age < 3.5f)
                {
                    bool on = Mathf.Repeat(age * 3f, 1f) < 0.65f;
                    if (on) Sprite(ring, at, Mathf.Max(24f, Px(8f)), new Color(Warning.r, Warning.g, Warning.b, 0.8f));
                    Text(at, "!", Warning, Mathf.RoundToInt(fontSize * 1.4f), TextAnchor.MiddleCenter, true);
                }
            }

            // Customers.
            var planByBay = BayShapes();
            foreach (PersonState c in f.Customers)
            {
                Vector2 at = ToScreen(c.At);
                Color colour = CustomerColour(c.State);
                float size = Mathf.Max(9f, Px(0.9f));
                if (c.Bay >= 0 && planByBay.TryGetValue(c.Bay, out StoreFloorPlan.Shape shelf))
                {
                    Vector2 shelfAt = ToScreen((shelf.Corners[0] + shelf.Corners[2]) * 0.5f);
                    if (c.State == CustomerMark.Following) Line(at, ToScreen(f.Player), 2f, new Color(Following.r, Following.g, Following.b, 0.8f), 8f);
                    Line(at, shelfAt, 2f, new Color(Asking.r, Asking.g, Asking.b, 0.9f), 6f);
                    OutlineBay(c.Bay, Asking, 3f);
                }
                Sprite(circle, at, size, colour);
                if (c.State == CustomerMark.Asking || c.State == CustomerMark.LostTheGuide)
                    Text(at + new Vector2(0f, -size), "?", Asking, fontSize, TextAnchor.LowerCenter, true);
                else if (c.State == CustomerMark.Queueing && c.Wait >= 20f)
                    Text(at + new Vector2(size * 0.7f, 0f), $"{c.Wait:0}s", c.Wait >= 60f ? KarenRed : Waiting, Mathf.RoundToInt(fontSize * 0.8f), TextAnchor.MiddleLeft, true);
                else if (c.State == CustomerMark.Possessed || c.State == CustomerMark.Fake)
                    Sprite(ring, at, size * 1.8f, Possessed);
            }

            // Karen.
            if (f.KarenPresent)
            {
                Vector2 k = ToScreen(f.Karen);
                if (f.GuessConfidence > 0.05f)
                {
                    float r = Mathf.Lerp(12f, 3f, Mathf.Clamp01(f.GuessConfidence));
                    DashedCircle(ToScreen(f.Guess), Px(r), new Color(Guess.r, Guess.g, Guess.b, 0.9f));
                    Text(ToScreen(f.Guess) + new Vector2(0f, Px(r) + 2f), GameNames.Antagonist + "'s guess", Guess, Mathf.RoundToInt(fontSize * 0.75f), TextAnchor.UpperCenter, true);
                }
                Rotated(cone, k, Px(36f), f.KarenYaw, new Color(1f, 0.25f, 0.25f, f.KarenSees ? 0.38f : 0.18f));
                bool flash = f.Chasing && Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.5f;
                Rotated(Texture2D.whiteTexture, k, Mathf.Max(12f, Px(1.3f)), 45f, flash ? Color.white : KarenRed);
                Text(k + new Vector2(Mathf.Max(10f, Px(1.2f)), 0f), f.Chasing ? GameNames.Antagonist + " — CHASING" : GameNames.Antagonist, KarenRed, fontSize, TextAnchor.MiddleLeft, true);
            }

            // You.
            Vector2 me = ToScreen(f.Player);
            Rotated(arrow, me, Mathf.Max(18f, Px(2.2f)) + 4f, f.PlayerYaw, Color.white);
            Rotated(arrow, me, Mathf.Max(18f, Px(2.2f)), f.PlayerYaw, You);
            Text(me + new Vector2(Mathf.Max(12f, Px(1.4f)), 0f), f.PlayerHeld ? "You (lectured)" : "You", You, fontSize, TextAnchor.MiddleLeft, true);
        }

        // Karen's belief as a heat layer (H on the F1 map).
        public void DrawBelief(BeliefGrid belief)
        {
            StoreMap map = StoreMap.Current;
            if (heat == null || heat.width != map.Columns || heat.height != map.Rows)
            {
                heat = new Texture2D(map.Columns, map.Rows, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                heatPixels = new Color32[map.Columns * map.Rows];
            }
            for (int i = 0; i < heatPixels.Length; i++) heatPixels[i] = new Color32(0, 0, 0, 0);
            float max = 1e-6f;
            for (int c = 0; c < map.CellCount; c++) max = Mathf.Max(max, belief[c]);
            for (int c = 0; c < map.CellCount; c++)
            {
                float t = Mathf.Clamp01(1f + Mathf.Log10(Mathf.Max(1e-9f, belief[c]) / max) / 3f);
                if (t <= 0f) continue;
                map.CellGrid(c, out int col, out int row);
                Color colour = Color.Lerp(new Color(1f, 0.85f, 0.2f, 0.05f), new Color(1f, 0.3f, 0.1f, 0.75f), t);
                heatPixels[row * map.Columns + col] = colour;
            }
            heat.SetPixels32(heatPixels);
            heat.Apply(false, false);
            Vector2 topLeft = ToScreen(new Vector2(map.Origin.x, map.Origin.z + map.Rows * StoreMap.CellSize));
            GUI.DrawTexture(new Rect(topLeft.x, topLeft.y, Px(map.Columns * StoreMap.CellSize), Px(map.Rows * StoreMap.CellSize)), heat);
        }

        // ---- the legend ------------------------------------------------------------------------

        public static void Legend(Rect r, int fontSize)
        {
            EnsureSprites();
            var items = new (Texture2D tex, Color colour, string label, float rot)[]
            {
                (arrow, You, "You", 0f), (Texture2D.whiteTexture, KarenRed, GameNames.Antagonist, 45f), (ring, Guess, GameNames.Antagonist + "'s guess", 0f),
                (circle, Shopper, "Shopping", 0f), (circle, ToTill, "Going to till", 0f), (circle, Waiting, "Waiting at till", 0f),
                (circle, Asking, "Wants directions", 0f), (circle, Following, "Following you", 0f), (circle, Possessed, GameNames.Antagonist + "'s puppet", 0f),
                (ring, SoundYou, "Your noise", 0f), (ring, SoundKaren, "Her noise", 0f), (ring, SoundCustomer, "Other noise", 0f),
                (circle, Warning, "Warning sound", 0f), (circle, Spill, "Spill", 0f), (Texture2D.whiteTexture, EmptyShelf, "Empty shelf", 0f),
                (Texture2D.whiteTexture, new Color(0.3f, 0.8f, 0.4f), "Bin (fills red)", 0f), (cross, KarenRed, "Locked door", 0f),
            };
            int perRow = Mathf.Max(1, Mathf.FloorToInt(r.width / (fontSize * 8.5f)));
            float cellW = r.width / perRow, cellH = fontSize * 1.6f;
            for (int i = 0; i < items.Length; i++)
            {
                float x = r.x + (i % perRow) * cellW, y = r.y + (i / perRow) * cellH;
                var icon = new Vector2(x + fontSize * 0.6f, y + cellH * 0.5f);
                if (items[i].rot != 0f) RotatedStatic(items[i].tex, icon, fontSize * 0.8f, items[i].rot, items[i].colour);
                else SpriteStatic(items[i].tex, icon, fontSize * 0.9f, items[i].colour);
                TextStatic(new Vector2(x + fontSize * 1.4f, y + cellH * 0.5f), items[i].label, new Color(0.9f, 0.9f, 0.9f), Mathf.RoundToInt(fontSize * 0.85f), TextAnchor.MiddleLeft, false);
            }
        }

        public static float LegendHeight(float width, int fontSize)
        {
            int perRow = Mathf.Max(1, Mathf.FloorToInt(width / (fontSize * 8.5f)));
            return Mathf.Ceil(17f / perRow) * fontSize * 1.6f;
        }

        // ---- colours ------------------------------------------------------------------------------

        public static Color CustomerColour(CustomerMark m)
        {
            switch (m)
            {
                case CustomerMark.HeadingToTill: return ToTill;
                case CustomerMark.Queueing: return Waiting;
                case CustomerMark.Asking:
                case CustomerMark.Talking:
                case CustomerMark.LostTheGuide: return Asking;
                case CustomerMark.Following: return Following;
                case CustomerMark.Leaving: return Leaving;
                case CustomerMark.Possessed:
                case CustomerMark.Fake:
                case CustomerMark.LookingAround: return Possessed;
                default: return Shopper;
            }
        }

        public static Color SoundColour(string who)
        {
            switch (who)
            {
                case "you": return SoundYou;
                case GameNames.Antagonist: return SoundKaren;
                case "a customer": return SoundCustomer;
                default: return SoundStore;
            }
        }

        // ---- drawing helpers --------------------------------------------------------------------

        Dictionary<int, StoreFloorPlan.Shape> byBay;

        Dictionary<int, StoreFloorPlan.Shape> BayShapes()
        {
            if (byBay != null) return byBay;
            byBay = new Dictionary<int, StoreFloorPlan.Shape>();
            foreach (StoreFloorPlan.Shape s in plan.Shapes) if (s.Bay >= 0) byBay[s.Bay] = s;
            return byBay;
        }

        void OutlineBay(int bay, Color colour, float width)
        {
            if (!BayShapes().TryGetValue(bay, out StoreFloorPlan.Shape s)) return;
            for (int i = 0; i < 4; i++) Line(ToScreen(s.Corners[i]), ToScreen(s.Corners[(i + 1) % 4]), width, colour, 0f);
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void Square(Vector2 at, float size, Color c) => Fill(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), c);

        void Sprite(Texture2D tex, Vector2 at, float size, Color c) => SpriteStatic(tex, at, size, c);

        static void SpriteStatic(Texture2D tex, Vector2 at, float size, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), tex);
            GUI.color = Color.white;
        }

        void Rotated(Texture2D tex, Vector2 at, float size, float degrees, Color c) => RotatedStatic(tex, at, size, degrees, c);

        static void RotatedStatic(Texture2D tex, Vector2 at, float size, float degrees, Color c)
        {
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, at);
            SpriteStatic(tex, at, size, c);
            GUI.matrix = saved;
        }

        public static void Line(Vector2 a, Vector2 b, float width, Color c, float dash)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.color = c;
            if (dash <= 0f) GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, length, width), Texture2D.whiteTexture);
            else
                for (float s = 0f; s < length; s += dash * 2f)
                    GUI.DrawTexture(new Rect(a.x + s, a.y - width * 0.5f, Mathf.Min(dash, length - s), width), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.matrix = saved;
        }

        static void DashedCircle(Vector2 centre, float radius, Color c)
        {
            const int segments = 24;
            for (int i = 0; i < segments; i += 2)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Line(centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, 2.5f, c, 0f);
            }
        }

        void Text(Vector2 at, string text, Color c, int size, TextAnchor anchor, bool shadow = true) => TextStatic(at, text, c, size, anchor, shadow);

        static GUIStyle label;

        public static void TextStatic(Vector2 at, string text, Color c, int size, TextAnchor anchor, bool shadow)
        {
            if (label == null) label = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false, clipping = TextClipping.Overflow };
            label.fontSize = size;
            label.alignment = anchor;
            var r = new Rect(at.x - 300f, at.y - size, 600f, size * 2f);
            switch (anchor)
            {
                case TextAnchor.MiddleLeft: r = new Rect(at.x, at.y - size, 600f, size * 2f); break;
                case TextAnchor.LowerCenter: r = new Rect(at.x - 300f, at.y - size * 2f, 600f, size * 2f); break;
                case TextAnchor.UpperCenter: r = new Rect(at.x - 300f, at.y, 600f, size * 2f); break;
            }
            if (shadow)
            {
                label.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
                GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, label);
            }
            label.normal.textColor = c;
            GUI.Label(r, text, label);
        }

        // ---- sprites, drawn once in code --------------------------------------------------------

        static void EnsureSprites()
        {
            if (circle != null) return;
            circle = Make(64, (x, y) => Disc(x, y, 0.46f));
            ring = Make(128, (x, y) => Mathf.Clamp01(Disc(x, y, 0.48f) - Disc(x, y, 0.40f)));
            soft = Make(64, (x, y) => Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) / 0.5f) * 0.9f);
            // Arrow pointing up (north): a triangle with a notch in its tail.
            arrow = Make(64, (x, up) =>
            {
                bool inTri = up > -0.42f && up < 0.46f && Mathf.Abs(x) < (0.46f - up) * 0.55f;
                bool notch = up < -0.18f && Mathf.Abs(x) < (-0.18f - up) * 1.2f;
                return inTri && !notch ? 1f : 0f;
            });
            // A 120° wedge pointing up, fading with distance.
            cone = Make(128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y) / 0.5f;
                if (r > 1f) return 0f;
                float angle = Mathf.Abs(Mathf.Atan2(x, y) * Mathf.Rad2Deg);   // 0 = straight up
                return angle <= 60f ? Mathf.Clamp01(1f - r) * Mathf.Clamp01((60f - angle) / 6f) : 0f;
            });
            cross = Make(64, (x, y) => Mathf.Abs(Mathf.Abs(x) - Mathf.Abs(y)) < 0.08f && Mathf.Abs(x) < 0.42f ? 1f : 0f);
        }

        static float Disc(float x, float y, float radius) => Mathf.Clamp01((radius - Mathf.Sqrt(x * x + y * y)) * 64f);

        // `alpha(x, up)`: both run -0.5..0.5 across the sprite, `up` positive towards the top
        // of the screen. (Texture row 0 is the bottom, and IMGUI draws it at the bottom.)
        static Texture2D Make(int size, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float x = (i + 0.5f) / size - 0.5f;
                float up = (j + 0.5f) / size - 0.5f;
                byte a = (byte)(Mathf.Clamp01(alpha(x, up)) * 255f);
                px[j * size + i] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
