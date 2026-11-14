using UnityEngine;

namespace Kehai.Store
{
    // Paints a StoreFloorPlan into a texture for the in-game map: outside dark, walkable
    // floor grey (outdoors a little greener), each room and each aisle in its own soft colour
    // (0.4.1, StoreFloorPlan.Zones), counters and fridges darker, shelves in their aisle's
    // colour but brighter, doors amber, walls pale on top. Built once per store.
    public static class FloorPlanTexture
    {
        public static readonly Color Background = new Color32(21, 22, 27, 255);      // UiTheme.Ink, solid
        public static readonly Color FloorIndoor = new Color32(58, 61, 70, 255);
        public static readonly Color FloorOutdoor = new Color32(40, 46, 44, 255);
        public static readonly Color Fixture = new Color32(30, 31, 37, 255);         // counters, racks: dark shapes on a coloured floor
        public static readonly Color Wall = new Color32(236, 231, 222, 255);         // warm white
        public static readonly Color Door = new Color32(238, 186, 104, 255);         // soft amber

        public static Texture2D Render(StoreFloorPlan plan, float pixelsPerMetre, int maxSize = 2048)
        {
            Rect b = plan.Bounds;
            float ppm = Mathf.Min(pixelsPerMetre, maxSize / Mathf.Max(b.width, b.height));
            int w = Mathf.Max(8, Mathf.CeilToInt(b.width * ppm));
            int h = Mathf.Max(8, Mathf.CeilToInt(b.height * ppm));
            var px = new Color32[w * h];
            Color32 bg = Background;
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            var canvas = new Canvas(px, w, h, b, ppm);
            for (int i = 0; i < plan.Floor.Count; i++)
                canvas.Triangle(plan.Floor[i][0], plan.Floor[i][1], plan.Floor[i][2], plan.FloorOutdoors[i] ? FloorOutdoor : FloorIndoor);

            // Each room and aisle in its own colour, laid over the floor it has and nothing else:
            // a cell's square only colours pixels that are already floor.
            canvas.MarkFloor();
            foreach (StoreFloorPlan.Zone zone in plan.Zones)
                foreach (Vector2 cell in zone.Cells)
                    canvas.FloorSquare(cell, Kehai.Store.StoreMap.CellSize + 0.2f, zone.Colour);

            foreach (StoreFloorPlan.Shape s in plan.Shapes)
                if (s.Kind == StoreFloorPlan.ShapeKind.Fixture) canvas.Quad(s.Corners, Fixture, 0f);
            foreach (StoreFloorPlan.Shape s in plan.Shapes)
                if (s.Kind == StoreFloorPlan.ShapeKind.Shelf)
                {
                    Color c = StoreFloorPlan.SectionColour(s.Section);
                    canvas.Quad(s.Corners, c * 0.55f + new Color(0f, 0f, 0f, 1f), 0f);
                    canvas.Outline(s.Corners, c, 1.2f / ppm);
                }
            foreach (StoreFloorPlan.Shape s in plan.Shapes)
                if (s.Kind == StoreFloorPlan.ShapeKind.Door || s.Kind == StoreFloorPlan.ShapeKind.AutoDoor) canvas.Quad(s.Corners, Door, 2f / ppm);
            foreach (StoreFloorPlan.Shape s in plan.Shapes)
                if (s.Kind == StoreFloorPlan.ShapeKind.Wall) canvas.Quad(s.Corners, Wall, 2f / ppm);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "FloorPlan" };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // A tiny software rasteriser: triangles, and quads thickened to a minimum width so
        // 3 cm walls still show as lines.
        sealed class Canvas
        {
            readonly Color32[] px;
            readonly int w, h;
            readonly Rect bounds;
            readonly float ppm;

            public Canvas(Color32[] px, int w, int h, Rect bounds, float ppm)
            {
                this.px = px; this.w = w; this.h = h; this.bounds = bounds; this.ppm = ppm;
            }

            Vector2 ToPixel(Vector2 world) => new Vector2((world.x - bounds.xMin) * ppm, (world.y - bounds.yMin) * ppm);

            bool[] floor;

            // Remember which pixels are floor right now (everything painted so far).
            public void MarkFloor()
            {
                floor = new bool[px.Length];
                Color32 bg = Background;
                for (int i = 0; i < px.Length; i++) floor[i] = px[i].r != bg.r || px[i].g != bg.g || px[i].b != bg.b;
            }

            // A square of `size` metres round `centre`, painted only where there is floor.
            public void FloorSquare(Vector2 centre, float size, Color colour)
            {
                if (floor == null) return;
                Vector2 c = ToPixel(centre);
                float half = size * ppm * 0.5f;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - half)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(c.x + half));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(c.y - half)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(c.y + half));
                Color32 c32 = colour;
                c32.a = 255;
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (floor[y * w + x]) px[y * w + x] = c32;
            }

            public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color colour)
            {
                Vector2 pa = ToPixel(a), pb = ToPixel(b), pc = ToPixel(c);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x, pc.x)));
                int x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x, pc.x)));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y, pc.y)));
                int y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y, pc.y)));
                float area = Edge(pa, pb, pc);
                if (Mathf.Abs(area) < 1e-6f) return;
                Color32 c32 = colour;
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(pb, pc, p), w1 = Edge(pc, pa, p), w2 = Edge(pa, pb, p);
                    bool inside = area > 0f ? w0 >= -0.01f && w1 >= -0.01f && w2 >= -0.01f
                                            : w0 <= 0.01f && w1 <= 0.01f && w2 <= 0.01f;
                    if (inside) px[y * w + x] = c32;
                }
            }

            static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

            // Fill a four-cornered footprint, stretched across its short side to at least
            // `minWidth` metres so thin things stay visible.
            public void Quad(Vector2[] c, Color colour, float minWidth)
            {
                Vector2[] q = Thicken(c, minWidth);
                Triangle(q[0], q[1], q[2], colour);
                Triangle(q[0], q[2], q[3], colour);
            }

            public void Outline(Vector2[] c, Color colour, float width)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector2 a = c[i], b = c[(i + 1) % 4];
                    Vector2 n = Vector2.Perpendicular((b - a).normalized) * width * 0.5f;
                    Triangle(a - n, b - n, b + n, colour);
                    Triangle(a - n, b + n, a + n, colour);
                }
            }

            static Vector2[] Thicken(Vector2[] c, float minWidth)
            {
                if (minWidth <= 0f) return c;
                Vector2 u = c[1] - c[0], v = c[3] - c[0];
                Vector2 centre = (c[0] + c[2]) * 0.5f;
                float lu = u.magnitude, lv = v.magnitude;
                if (lu < 1e-5f || lv < 1e-5f) return c;
                Vector2 du = u / lu * Mathf.Max(lu, minWidth) * 0.5f;
                Vector2 dv = v / lv * Mathf.Max(lv, minWidth) * 0.5f;
                return new[] { centre - du - dv, centre + du - dv, centre + du + dv, centre - du + dv };
            }
        }
    }
}
