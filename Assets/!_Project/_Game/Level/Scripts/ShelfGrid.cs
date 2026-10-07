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
// board facing one aisle sell the same product: that's a facing, and Planogram picks it.
// The slots are data (ShelfSlot), drawn by ShelfDrawer. MERCHANDISING.md is this in prose.
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

    public readonly struct Board
    {
        public readonly Vector3 Centre;      // bay-local, top surface
        public readonly Vector2 Size;        // x along the bay, y (z) across it

        public Board(Vector3 centre, Vector2 size) { Centre = centre; Size = size; }
    }

    public readonly struct Square
    {
        public readonly Vector3 Centre;      // bay-local, on the board's surface
        public readonly Vector2 Size;
        public readonly bool Back;           // faces +Z (the bay's back), else -Z

        public Square(Vector3 centre, Vector2 size, bool back) { Centre = centre; Size = size; Back = back; }

        public float Height => Centre.y;
    }

    // One board's worth of squares facing one way: a facing.
    public sealed class Facing
    {
        public readonly float Height;
        public readonly bool Back;
        public readonly List<Square> Squares = new List<Square>();

        public Facing(float height, bool back) { Height = height; Back = back; }

        public long Key => Mathf.RoundToInt(Height * 100f) * 4L + (Back ? 1L : 0L);
    }

    public static List<Board> Boards(Transform bay)
    {
        var boards = new List<Board>();
        foreach (Transform child in bay)
        {
            if (!child.name.ToLowerInvariant().Contains("polka")) continue;
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

    public static IEnumerable<Square> SquaresOf(Board board)
    {
        int nx = Mathf.Max(1, Mathf.RoundToInt(board.Size.x / SquareSize));
        int nz = Mathf.Max(1, Mathf.RoundToInt(board.Size.y / SquareSize));
        var size = new Vector2(board.Size.x / nx, board.Size.y / nz);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                var centre = new Vector3(board.Centre.x - board.Size.x * 0.5f + (i + 0.5f) * size.x,
                                         board.Centre.y,
                                         board.Centre.z - board.Size.y * 0.5f + (j + 0.5f) * size.y);
                yield return new Square(centre, size, centre.z >= 0f);
            }
    }

    // A bay's facings, in a fixed order: by height, then front before back.
    public static List<Facing> Facings(Transform bay)
    {
        var byKey = new Dictionary<long, Facing>();
        foreach (Board board in Boards(bay))
            foreach (Square sq in SquaresOf(board))
            {
                var probe = new Facing(sq.Height, sq.Back);
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
        Vector2Int cells = CellsFor(product, PackSize(product), facing.Squares[0].Size);
        return facing.Squares.Count * cells.x * cells.y;
    }

    // ------------------------------------------------------------------ the slots

    // One slot of a facing: the middle of its cell, bay-local, on the board, and the cell's
    // floor (x along the bay, y across it).
    public readonly struct Cell
    {
        public readonly Vector3 Centre;
        public readonly Vector2 Size;
        public readonly bool Back;

        public Cell(Vector3 centre, Vector2 size, bool back) { Centre = centre; Size = size; Back = back; }
    }

    // A facing's slots for a product: every square cut into as many cells as the pack fits.
    // StoreLayout makes a ShelfSlot of each.
    public static IEnumerable<Cell> CellsOf(Facing facing, ProductDef product)
    {
        if (product == null) yield break;
        Vector3 pack = PackSize(product);
        foreach (Square sq in facing.Squares)
        {
            Vector2Int cells = CellsFor(product, pack, sq.Size);
            var size = new Vector2(sq.Size.x / cells.x, sq.Size.y / cells.y);
            foreach (Vector3 centre in CellCentres(sq, cells))
                yield return new Cell(centre, size, sq.Back);
        }
    }
}
