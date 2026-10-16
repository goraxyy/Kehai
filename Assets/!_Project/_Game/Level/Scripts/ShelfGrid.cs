using System.Collections.Generic;
using UnityEngine;

// How a bay's boards are cut up into slots. Every board (a "polka") is tiled with 0.5 m
// squares, so a two-sided 4 m board has 16 (8 along, one row facing each aisle), a one-sided
// one 8, a 1 m pillar board 4 and a 1 x 0.5 m "b" pillar board 2. Each square is then filled
// with slots according to what it sells:
//
//   small packs (fit a quarter of the square)      2 x 2 slots
//   long ones (a loaf, a bag of rice on its side)  2 x 1, or 1 x 2 when it's wide not deep
//   drinks                                          as many as their footprint allows, up to 3 x 3
//   anything bigger                                 1
//
// One slot holds one item, and the grid makes a full shelf look full. All the squares on one
// board facing one aisle sell the same product: that's a facing, and StoreLayout picks it.
// The slots are data (ShelfSlot), drawn by ShelfDrawer. MERCHANDISING.md is this in prose.
//
// Which way a square faces comes from the shelf model itself: its price strips (the
// "Pricetag" pieces along the boards' edges) are on exactly the sides that sell. A two-sided
// bay has them front and back; a pillar where a perpendicular bay joins the run has none on
// the side that bay covers; a "b" or "E" end piece sells from its back; some pillars sell
// sideways. A square faces the open edge it's on, and one inside the board (behind another)
// faces the way the stock in front of it does. A bay without strips falls back to front and
// back by its own middle.
public static class ShelfGrid
{
    public const float SquareSize = 0.5f;
    public const float Gap = 0.02f;          // air between neighbouring packs
    public const int MaxDrinksAcross = 3;

    // Where Kehai/Store/Stock the Maze used to bake each bay's slots and items into the scene,
    // before stock was data. Kehai/Store/Migrate to Data Shelves clears it out.
    public const string LegacyRootName = "GridSlots";

    // Same rule as ShelfPrefabBuilder: a board is a thin "polka" child of the bay; thicker
    // ones are structural bases.
    const float ThinBoardMax = 0.1f;
    // "Shelf deck" is the authoring name; old imported models still say "polka".
    public static bool IsDeck(Transform child) => child.name.ToLowerInvariant().Contains("polka") || child.name.StartsWith("Shelf deck",System.StringComparison.OrdinalIgnoreCase);

    public readonly struct Board
    {
        public readonly Vector3 Centre;      // bay-local, top surface
        public readonly Vector2 Size;        // x along the bay, y (z) across it

        public Board(Vector3 centre, Vector2 size) { Centre = centre; Size = size; }
    }

    // Which side of the bay a square sells to: its front (-Z), back (+Z), left (-X) or right (+X).
    public enum Side { Front = 0, Back = 1, Left = 2, Right = 3 }

    // The way out of the bay on that side, bay-local.
    public static Vector3 Outward(Side side) => side switch
    {
        Side.Back => Vector3.forward,
        Side.Left => Vector3.left,
        Side.Right => Vector3.right,
        _ => Vector3.back,
    };

    // The turn about the bay's up axis that makes an item face out of that side (items face -Z).
    public static float Yaw(Side side) => side switch
    {
        Side.Back => 180f,
        Side.Left => 90f,
        Side.Right => -90f,
        _ => 0f,
    };

    static bool Sideways(Side side) => side == Side.Left || side == Side.Right;

    public readonly struct Square
    {
        public readonly Vector3 Centre;      // bay-local, on the board's surface
        public readonly Vector2 Size;        // x along the bay, y (z) across it
        public readonly Side Side;

        public Square(Vector3 centre, Vector2 size, Side side) { Centre = centre; Size = size; Side = side; }
        public Square(Vector3 centre, Vector2 size, bool back) : this(centre, size, back ? Side.Back : Side.Front) { }

        public bool Back => Side == Side.Back;
        public float Height => Centre.y;

        // The middle of the edge it faces out of, bay-local, on the board's surface.
        public Vector3 Edge => Centre + Outward(Side) * ((Sideways(Side) ? Size.x : Size.y) * 0.5f);
    }

    // One board's worth of squares facing one way: a facing.
    public sealed class Facing
    {
        public readonly float Height;
        public readonly Side Side;
        public readonly List<Square> Squares = new List<Square>();

        public Facing(float height, Side side) { Height = height; Side = side; }

        public bool Back => Side == Side.Back;
        public long Key => Mathf.RoundToInt(Height * 100f) * 4L + (long)Side;
    }

    // A price strip of the shelf model: the board it's on (by height), the side it's on, and
    // how far along that side it runs (bay-local x for the front and back, z for the sides).
    public readonly struct Strip
    {
        public readonly Renderer Renderer;
        public readonly float Height;
        public readonly Side Side;
        public readonly float From, To;

        public Strip(Renderer renderer, float height, Side side, float from, float to)
        {
            Renderer = renderer; Height = height; Side = side; From = from; To = to;
        }

        public float Length => To - From;
    }

    public const string StripPrefix = "Pricetag";
    const float EdgeSlack = 0.06f;           // how far off a board's edge a strip may sit

    public static List<Board> Boards(Transform bay)
    {
        var boards = new List<Board>();
        foreach (Transform child in bay)
        {
            if (!child.gameObject.activeInHierarchy || !IsDeck(child)) continue;
            Vector3 s = child.localScale;
            if (s.y >= ThinBoardMax) continue;
            // Boards are axis-aligned in the bay, but a quarter turn swaps their sides.
            Vector3 extent = child.localRotation * new Vector3(s.x, 0f, s.z);
            var size = new Vector2(Mathf.Abs(extent.x), Mathf.Abs(extent.z));
            Vector3 top = child.localPosition + Vector3.up * (s.y * 0.5f);
            boards.Add(new Board(top, size));
        }
        return boards;
    }

    // The board's squares, each facing the side it sells to: with no strips, front and back by
    // the board's own middle (two or more squares deep) or the front (one deep); with strips,
    // the open edge each square is on (front and back first at a corner), or for a square
    // inside the board, the open edge nearest it (front and back first again).
    public static IEnumerable<Square> SquaresOf(Board board, IReadOnlyList<Strip> strips = null)
    {
        int nx = Mathf.Max(1, Mathf.RoundToInt(board.Size.x / SquareSize));
        int nz = Mathf.Max(1, Mathf.RoundToInt(board.Size.y / SquareSize));
        var size = new Vector2(board.Size.x / nx, board.Size.y / nz);
        var open = new List<Strip>();
        if (strips != null)
            foreach (Strip strip in strips)
                if (Mathf.Abs(strip.Height - board.Centre.y) < 0.03f) open.Add(strip);

        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var centre = new Vector3(board.Centre.x - board.Size.x * 0.5f + (i + 0.5f) * size.x,
                                         board.Centre.y,
                                         board.Centre.z - board.Size.y * 0.5f + (j + 0.5f) * size.y);
                Side side;
                if (open.Count == 0)
                {
                    bool back = nz > 1 ? centre.z >= board.Centre.z : centre.z >= 0f;
                    side = back ? Side.Back : Side.Front;
                }
                else side = SideOf(board, open, centre, i == 0, i == nx - 1, j == 0, j == nz - 1);
                yield return new Square(centre, size, side);
            }
    }

    static Side SideOf(Board board, List<Strip> open, Vector3 centre, bool left, bool right, bool front, bool back)
    {
        Side? onEdge = null;
        float nearest = float.MaxValue;
        Side nearestSide = open[0].Side;
        foreach (Strip strip in open)
        {
            bool sideways = Sideways(strip.Side);
            float along = sideways ? centre.z : centre.x;
            bool covers = along >= strip.From - EdgeSlack && along <= strip.To + EdgeSlack;
            bool atEdge = strip.Side switch
            {
                Side.Front => front, Side.Back => back, Side.Left => left, _ => right,
            };
            if (covers && atEdge && (onEdge == null || (Sideways(onEdge.Value) && !sideways))) onEdge = strip.Side;

            float edge = strip.Side switch
            {
                Side.Front => centre.z - (board.Centre.z - board.Size.y * 0.5f),
                Side.Back => board.Centre.z + board.Size.y * 0.5f - centre.z,
                Side.Left => centre.x - (board.Centre.x - board.Size.x * 0.5f),
                _ => board.Centre.x + board.Size.x * 0.5f - centre.x,
            };
            float score = edge + (sideways ? 0.01f : 0f);
            if (score < nearest) { nearest = score; nearestSide = strip.Side; }
        }
        return onEdge ?? nearestSide;
    }

    // The bay's price strips, each matched to the side of the board it runs along.
    public static List<Strip> Strips(Transform bay, List<Board> boards = null)
    {
        boards ??= Boards(bay);
        var strips = new List<Strip>();
        foreach (MeshRenderer r in bay.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!r.name.StartsWith(StripPrefix)) continue;
            Bounds local = LocalBounds(bay, r.bounds);
            foreach (Board board in boards)
            {
                if (board.Centre.y < local.min.y - 0.02f || board.Centre.y > local.max.y + 0.02f) continue;
                float x0 = board.Centre.x - board.Size.x * 0.5f, x1 = board.Centre.x + board.Size.x * 0.5f;
                float z0 = board.Centre.z - board.Size.y * 0.5f, z1 = board.Centre.z + board.Size.y * 0.5f;
                bool alongX = local.size.x >= local.size.z;
                float at = alongX ? local.center.z : local.center.x;
                float from = alongX ? local.min.x : local.min.z, to = alongX ? local.max.x : local.max.z;
                // It has to run along this board, not merely sit at its height.
                bool overlaps = alongX ? to > x0 + 0.01f && from < x1 - 0.01f : to > z0 + 0.01f && from < z1 - 0.01f;
                if (!overlaps) continue;
                Side? side = null;
                if (alongX && Mathf.Abs(at - z0) < EdgeSlack) side = Side.Front;
                else if (alongX && Mathf.Abs(at - z1) < EdgeSlack) side = Side.Back;
                else if (!alongX && Mathf.Abs(at - x0) < EdgeSlack) side = Side.Left;
                else if (!alongX && Mathf.Abs(at - x1) < EdgeSlack) side = Side.Right;
                if (side == null) continue;
                strips.Add(new Strip(r, board.Centre.y, side.Value, from, to));
                break;
            }
        }
        return strips;
    }

    // A part's box in the bay's space, from its renderer's box in the world: the bays stand
    // square to the world, so it's exact. Not from its mesh: in play the shelves are statically
    // batched, and every part's mesh is the whole scene's.
    static Bounds LocalBounds(Transform bay, Bounds world)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = -min;
        for (int c = 0; c < 8; c++)
        {
            Vector3 corner = world.center + Vector3.Scale(world.extents,
                new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
            Vector3 p = bay.InverseTransformPoint(corner);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    // A bay's facings, in a fixed order: by height, then front, back, left, right.
    public static List<Facing> Facings(Transform bay)
    {
        List<Board> boards = Boards(bay);
        List<Strip> strips = Strips(bay, boards);
        var byKey = new Dictionary<long, Facing>();
        foreach (Board board in boards)
            foreach (Square sq in SquaresOf(board, strips))
            {
                var probe = new Facing(sq.Height, sq.Side);
                if (!byKey.TryGetValue(probe.Key, out Facing facing)) byKey[probe.Key] = facing = probe;
                facing.Squares.Add(sq);
            }
        var list = new List<Facing>(byKey.Values);
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }

    // How many of a product one square holds, across (x) and deep (z).
    public static Vector2Int CellsFor(ProductDef product, Vector3 packSize, Vector2 square)
    {
        float w = packSize.x, d = packSize.z;
        var half = square * 0.5f;
        if (product != null && product.Category == ItemType.SoftDrinks)
        {
            int across = Mathf.Clamp(Mathf.FloorToInt((square.x + Gap) / (w + Gap)), 1, MaxDrinksAcross);
            int deep = Mathf.Clamp(Mathf.FloorToInt((square.y + Gap) / (d + Gap)), 1, MaxDrinksAcross);
            return new Vector2Int(across, deep);
        }
        bool narrow = w <= half.x - Gap * 0.5f;
        bool shallow = d <= half.y - Gap * 0.5f;
        if (narrow && shallow) return new Vector2Int(2, 2);
        if (narrow && d <= square.y) return new Vector2Int(2, 1);
        if (shallow && w <= square.x) return new Vector2Int(1, 2);
        return Vector2Int.one;
    }

    // The centres of a square's slots, bay-local, on the board.
    public static IEnumerable<Vector3> CellCentres(Square sq, Vector2Int cells)
    {
        var cell = new Vector2(sq.Size.x / cells.x, sq.Size.y / cells.y);
        for (int i = 0; i < cells.x; i++)
            for (int j = 0; j < cells.y; j++)
                yield return new Vector3(sq.Centre.x - sq.Size.x * 0.5f + (i + 0.5f) * cell.x,
                                         sq.Centre.y,
                                         sq.Centre.z - sq.Size.y * 0.5f + (j + 0.5f) * cell.y);
    }

    // The pack's size as it stands on the shelf: its model if imported, the catalogue's
    // (scaled as the models are) otherwise.
    public static Vector3 PackSize(ProductDef product)
    {
        if (product == null) return new Vector3(0.2f, 0.4f, 0.2f);
        ProductLook.Look? look = ProductLook.For(product.Id);
        return look != null ? look.Value.Mesh.bounds.size : product.Size * ProductLook.ScaleFor(product);
    }

    public static int SlotsIn(Facing facing, ProductDef product)
    {
        if (facing.Squares.Count == 0) return 0;
        Vector2Int cells = CellsIn(facing.Squares[0], product, PackSize(product));
        return facing.Squares.Count * cells.x * cells.y;
    }

    // How a square is cut for a product, counted along the bay (x) and across it (y). A square
    // that sells sideways has the pack turned a quarter, so its width runs across the bay.
    static Vector2Int CellsIn(Square sq, ProductDef product, Vector3 pack)
    {
        if (!Sideways(sq.Side)) return CellsFor(product, pack, sq.Size);
        Vector2Int turned = CellsFor(product, pack, new Vector2(sq.Size.y, sq.Size.x));
        return new Vector2Int(turned.y, turned.x);
    }

    // ------------------------------------------------------------------ the slots

    // One slot of a facing: the middle of its cell, bay-local, on the board, and the cell's
    // floor (x along the bay, y across it).
    public readonly struct Cell
    {
        public readonly Vector3 Centre;
        public readonly Vector2 Size;
        public readonly Side Side;

        public Cell(Vector3 centre, Vector2 size, Side side) { Centre = centre; Size = size; Side = side; }

        public bool Back => Side == Side.Back;
    }

    // A facing's slots for a product: every square cut into as many cells as the pack fits.
    // StoreLayout makes a ShelfSlot of each.
    public static IEnumerable<Cell> CellsOf(Facing facing, ProductDef product)
    {
        if (product == null) yield break;
        Vector3 pack = PackSize(product);
        foreach (Square sq in facing.Squares)
        {
            Vector2Int cells = CellsIn(sq, product, pack);
            var size = new Vector2(sq.Size.x / cells.x, sq.Size.y / cells.y);
            foreach (Vector3 centre in CellCentres(sq, cells))
                yield return new Cell(centre, size, sq.Side);
        }
    }
}
