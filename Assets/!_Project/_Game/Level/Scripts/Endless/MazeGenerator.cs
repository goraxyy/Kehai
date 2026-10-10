using System.Collections.Generic;
using UnityEngine;

// The endless maze's floor plan (IDEAS.md, "Infinite maze", and "Scaling", step 3). The world
// is a grid of 25 m chunks, each 5 x 5 cells of 5 m, and a closed wall between two cells is a
// run of shelving. A chunk is worked out from hash(seed, x, z) alone, so the maze is
//   - endless: nothing is stored, and a chunk is made when it's needed;
//   - stable: walk away and back, and the same aisles are there;
//   - reproducible: a seed is a whole world, which the eval needs.
//
// The borders are matched. Which edges of the border between two chunks are open comes from
// that border's own hash, so both chunks agree on it, and every border has at least two
// openings. Inside a chunk a spanning tree joins every cell, so the whole world is one place.
// Then it's braided: most dead ends are knocked through, so there are loops. A chase with no
// way round is frustrating rather than tense, and Karen's herding needs loops.
public static class MazeGenerator
{
    public const int Cells = 5;
    public const float CellSize = 5f;
    public const float ChunkSize = Cells * CellSize;
    public const int BorderOpenings = 2;
    public const float Braid = 0.75f;   // the chance a dead end is knocked through
    public const float Loops = 0.12f;   // the chance any other wall in a chunk goes too

    // One chunk's walls, true where a wall stands. East[i, j] is the east side of cell (i, j),
    // North[i, j] its north side: the last column of East and row of North are the borders
    // with the chunks to the east and north. West and South are this chunk's other two
    // borders, which those neighbours own; they're here so a cell knows all four sides.
    public sealed class Chunk
    {
        public readonly int X, Z;
        public readonly bool[,] East = new bool[Cells, Cells];
        public readonly bool[,] North = new bool[Cells, Cells];
        public readonly bool[] West = new bool[Cells];
        public readonly bool[] South = new bool[Cells];

        public Chunk(int x, int z) { X = x; Z = z; }

        // The world position of its south-west corner, on the floor.
        public Vector3 Origin => new Vector3(X * ChunkSize, 0f, Z * ChunkSize);

        public bool WestOf(int i, int j) => i == 0 ? West[j] : East[i - 1, j];
        public bool SouthOf(int i, int j) => j == 0 ? South[i] : North[i, j - 1];

        public int ClosedSides(int i, int j) =>
            (WestOf(i, j) ? 1 : 0) + (East[i, j] ? 1 : 0) + (SouthOf(i, j) ? 1 : 0) + (North[i, j] ? 1 : 0);
    }

    public static Chunk Generate(int seed, int cx, int cz)
    {
        var c = new Chunk(cx, cz);

        // The four borders, from their own hashes.
        bool[] west = Border(seed, 1, cx, cz), east = Border(seed, 1, cx + 1, cz);
        bool[] south = Border(seed, 2, cx, cz), north = Border(seed, 2, cx, cz + 1);
        for (int k = 0; k < Cells; k++)
        {
            c.West[k] = west[k];
            c.East[Cells - 1, k] = east[k];
            c.South[k] = south[k];
            c.North[k, Cells - 1] = north[k];
        }

        // Every wall inside stands, then a spanning tree (Kruskal's, in a shuffled order)
        // knocks through just enough of them to join every cell.
        var rng = new Rng(Hash(seed, 0, cx, cz));
        var inside = new List<(bool east, int i, int j)>();
        for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (i < Cells - 1) { c.East[i, j] = true; inside.Add((true, i, j)); }
                if (j < Cells - 1) { c.North[i, j] = true; inside.Add((false, i, j)); }
            }
        for (int k = inside.Count - 1; k > 0; k--)
        {
            int r = rng.Range(k + 1);
            (inside[k], inside[r]) = (inside[r], inside[k]);
        }

        var parent = new int[Cells * Cells];
        for (int k = 0; k < parent.Length; k++) parent[k] = k;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        foreach ((bool isEast, int i, int j) in inside)
        {
            int a = Find(i * Cells + j), b = Find(isEast ? (i + 1) * Cells + j : i * Cells + j + 1);
            if (a == b) continue;
            parent[a] = b;
            Open(c, isEast, i, j);
        }

        // Braid: most dead ends get one more way out, through a wall of their own.
        var sides = new List<(bool east, int i, int j)>(4);
        for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (c.ClosedSides(i, j) < 3 || rng.Value >= Braid) continue;
                sides.Clear();
                if (i > 0 && c.East[i - 1, j]) sides.Add((true, i - 1, j));
                if (i < Cells - 1 && c.East[i, j]) sides.Add((true, i, j));
                if (j > 0 && c.North[i, j - 1]) sides.Add((false, i, j - 1));
                if (j < Cells - 1 && c.North[i, j]) sides.Add((false, i, j));
                if (sides.Count == 0) continue;
                (bool e, int si, int sj) = sides[rng.Range(sides.Count)];
                Open(c, e, si, sj);
            }

        // And a few more, for loops and the odd open floor.
        foreach ((bool isEast, int i, int j) in inside)
            if ((isEast ? c.East[i, j] : c.North[i, j]) && rng.Value < Loops) Open(c, isEast, i, j);

        return c;
    }

    static void Open(Chunk c, bool east, int i, int j)
    {
        if (east) c.East[i, j] = false;
        else c.North[i, j] = false;
    }

    // Which of a border's five edges stand. `axis` 1: the border along x = cx * ChunkSize (the
    // west side of chunk cx), 2: along z = cz * ChunkSize (the south side of chunk cz).
    static bool[] Border(int seed, int axis, int cx, int cz)
    {
        var closed = new bool[Cells];
        for (int k = 0; k < Cells; k++) closed[k] = true;
        var rng = new Rng(Hash(seed, axis, cx, cz));
        for (int opened = 0; opened < BorderOpenings;)
        {
            int k = rng.Range(Cells);
            if (!closed[k]) continue;
            closed[k] = false;
            opened++;
        }
        return closed;
    }

    // ---------------------------------------------------------------- what stands where

    public enum PieceKind { Run, Pillar }

    // A shelf run on a wall, or a pillar where walls meet. World position on the floor at the
    // middle of the piece; a run with Yaw 90 runs north-south.
    public readonly struct Piece
    {
        public readonly PieceKind Kind;
        public readonly Vector3 Position;
        public readonly float Yaw;

        public Piece(PieceKind kind, Vector3 position, float yaw) { Kind = kind; Position = position; Yaw = yaw; }
    }

    // The pieces a chunk places: a run on each of its own walls (its east and north borders
    // included, its west and south not: those are the neighbours'), and a pillar on each
    // corner it owns where any wall meets.
    public static List<Piece> Pieces(MazeWorld world, int cx, int cz)
    {
        Chunk c = world.Get(cx, cz);
        var pieces = new List<Piece>();
        Vector3 o = c.Origin;
        for (int i = 0; i < Cells; i++)
            for (int j = 0; j < Cells; j++)
            {
                if (c.East[i, j])
                    pieces.Add(new Piece(PieceKind.Run, o + new Vector3((i + 1) * CellSize, 0f, (j + 0.5f) * CellSize), 90f));
                if (c.North[i, j])
                    pieces.Add(new Piece(PieceKind.Run, o + new Vector3((i + 0.5f) * CellSize, 0f, (j + 1) * CellSize), 0f));

                int vx = cx * Cells + i, vz = cz * Cells + j;
                if (world.WallAlongX(vx, vz) || world.WallAlongX(vx - 1, vz) ||
                    world.WallAlongZ(vx, vz) || world.WallAlongZ(vx, vz - 1))
                    pieces.Add(new Piece(PieceKind.Pillar, o + new Vector3(i * CellSize, 0f, j * CellSize), 0f));
            }
        return pieces;
    }

    // The aisle a chunk sells: one section per chunk, from its hash.
    public static ItemType SectionOf(int seed, int cx, int cz)
    {
        ItemType[] sections = ProductCatalog.StockSections;
        return sections[(int)(Hash(seed, 3, cx, cz) % (uint)sections.Length)];
    }

    // ---------------------------------------------------------------- hashing

    public static uint Hash(int seed, int salt, int x, int z)
    {
        unchecked
        {
            uint h = (uint)seed * 0x9E3779B1u + (uint)salt * 0x85EBCA77u;
            h = (h ^ (h >> 15)) * 0x2C1B3C6Du;
            h ^= (uint)x * 0x27D4EB2Fu;
            h = (h ^ (h >> 13)) * 0x165667B1u;
            h ^= (uint)z * 0xC2B2AE3Du;
            h = (h ^ (h >> 16)) * 0x85EBCA6Bu;
            h ^= h >> 13;
            return h == 0 ? 0x6D2B79F5u : h;
        }
    }

    // xorshift32: small, fast, and the same everywhere.
    struct Rng
    {
        uint state;

        public Rng(uint seed) { state = seed == 0 ? 0x6D2B79F5u : seed; }

        uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public float Value => (Next() >> 8) * (1f / 16777216f);
        public int Range(int n) => (int)(Next() % (uint)n);
    }
}

// Chunks as they're asked for, kept, and the walls of the world by place. Walls are in world
// cell units: WallAlongX(x, z) is the wall along the south side of cell (x, z), WallAlongZ(x, z)
// the one along its west side.
public sealed class MazeWorld
{
    public readonly int Seed;
    readonly Dictionary<Vector2Int, MazeGenerator.Chunk> chunks = new Dictionary<Vector2Int, MazeGenerator.Chunk>();

    public MazeWorld(int seed) { Seed = seed; }

    public MazeGenerator.Chunk Get(int cx, int cz)
    {
        var key = new Vector2Int(cx, cz);
        if (!chunks.TryGetValue(key, out MazeGenerator.Chunk c)) chunks[key] = c = MazeGenerator.Generate(Seed, cx, cz);
        return c;
    }

    // Forget chunks far from `around`, so a long walk doesn't keep them all.
    public void Forget(Vector2Int around, int keep)
    {
        var far = new List<Vector2Int>();
        foreach (Vector2Int k in chunks.Keys)
            if (Mathf.Abs(k.x - around.x) > keep || Mathf.Abs(k.y - around.y) > keep) far.Add(k);
        foreach (Vector2Int k in far) chunks.Remove(k);
    }

    public bool WallAlongX(int x, int z)
    {
        MazeGenerator.Chunk c = ChunkOf(x, z, out int i, out int j);
        return c.SouthOf(i, j);
    }

    public bool WallAlongZ(int x, int z)
    {
        MazeGenerator.Chunk c = ChunkOf(x, z, out int i, out int j);
        return c.WestOf(i, j);
    }

    MazeGenerator.Chunk ChunkOf(int x, int z, out int i, out int j)
    {
        int cx = FloorDiv(x, MazeGenerator.Cells), cz = FloorDiv(z, MazeGenerator.Cells);
        i = x - cx * MazeGenerator.Cells;
        j = z - cz * MazeGenerator.Cells;
        return Get(cx, cz);
    }

    public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    // The chunk a world point is in.
    public static Vector2Int ChunkAt(Vector3 p) =>
        new Vector2Int(Mathf.FloorToInt(p.x / MazeGenerator.ChunkSize), Mathf.FloorToInt(p.z / MazeGenerator.ChunkSize));
}
