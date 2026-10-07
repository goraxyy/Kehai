using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// The endless maze's floor plan (MazeGenerator): the same from the same seed, one connected
// place across chunk borders, braided so it's mostly loops, and every wall and corner built
// once by the chunk that owns it.
public class EndlessMazeTests
{
    static readonly int[] Seeds = { 1, 7, 42, -5 };

    // Chunks -4..3 each way: 1,600 cells, either side of the origin.
    const int Min = -4, Max = 3;
    const int X0 = Min * MazeGenerator.Cells, X1 = (Max + 1) * MazeGenerator.Cells;

    [Test]
    public void AChunk_IsTheSameEveryTime_AndAnotherSeedIsAnotherMaze()
    {
        MazeGenerator.Chunk a = MazeGenerator.Generate(7, 2, -3), b = MazeGenerator.Generate(7, 2, -3);
        MazeGenerator.Chunk other = MazeGenerator.Generate(8, 2, -3);
        bool same = true, differs = false;
        for (int i = 0; i < MazeGenerator.Cells; i++)
            for (int j = 0; j < MazeGenerator.Cells; j++)
            {
                same &= a.East[i, j] == b.East[i, j] && a.North[i, j] == b.North[i, j];
                differs |= a.East[i, j] != other.East[i, j] || a.North[i, j] != other.North[i, j];
            }
        Assert.IsTrue(same, "the same seed and chunk made two mazes");
        Assert.IsTrue(differs, "another seed made the same maze");
    }

    [Test]
    public void Neighbours_AgreeOnTheBorderBetweenThem_AndItHasAWayThrough()
    {
        foreach (int seed in Seeds)
        {
            var world = new MazeWorld(seed);
            for (int cx = Min; cx <= Max; cx++)
                for (int cz = Min; cz <= Max; cz++)
                {
                    MazeGenerator.Chunk c = world.Get(cx, cz), east = world.Get(cx + 1, cz), north = world.Get(cx, cz + 1);
                    int eastOpen = 0, northOpen = 0;
                    for (int k = 0; k < MazeGenerator.Cells; k++)
                    {
                        Assert.AreEqual(c.East[MazeGenerator.Cells - 1, k], east.West[k], $"seed {seed}: chunk {cx},{cz} and its east neighbour");
                        Assert.AreEqual(c.North[k, MazeGenerator.Cells - 1], north.South[k], $"seed {seed}: chunk {cx},{cz} and its north neighbour");
                        if (!east.West[k]) eastOpen++;
                        if (!north.South[k]) northOpen++;
                    }
                    Assert.GreaterOrEqual(eastOpen, MazeGenerator.BorderOpenings);
                    Assert.GreaterOrEqual(northOpen, MazeGenerator.BorderOpenings);
                }
        }
    }

    [Test]
    public void EveryCell_CanBeReachedFromEveryOther()
    {
        foreach (int seed in Seeds)
        {
            var world = new MazeWorld(seed);
            var seen = new HashSet<Vector2Int> { new Vector2Int(X0, X0) };
            var queue = new Queue<Vector2Int>(seen);
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                void Go(int x, int z, bool wall)
                {
                    var n = new Vector2Int(x, z);
                    if (!wall && x >= X0 && z >= X0 && x < X1 && z < X1 && seen.Add(n)) queue.Enqueue(n);
                }
                Go(c.x + 1, c.y, world.WallAlongZ(c.x + 1, c.y));
                Go(c.x - 1, c.y, world.WallAlongZ(c.x, c.y));
                Go(c.x, c.y + 1, world.WallAlongX(c.x, c.y + 1));
                Go(c.x, c.y - 1, world.WallAlongX(c.x, c.y));
            }
            Assert.AreEqual((X1 - X0) * (X1 - X0), seen.Count, $"seed {seed}: cells cut off");
        }
    }

    // Braided: a perfect maze is about a third dead ends. This one has a few.
    [Test]
    public void DeadEnds_AreFew()
    {
        foreach (int seed in Seeds)
        {
            var world = new MazeWorld(seed);
            int dead = 0, cells = 0;
            for (int x = X0; x < X1; x++)
                for (int z = X0; z < X1; z++)
                {
                    cells++;
                    int closed = (world.WallAlongZ(x, z) ? 1 : 0) + (world.WallAlongZ(x + 1, z) ? 1 : 0) +
                                 (world.WallAlongX(x, z) ? 1 : 0) + (world.WallAlongX(x, z + 1) ? 1 : 0);
                    if (closed >= 3) dead++;
                }
            Assert.Less(dead, cells * 0.08f, $"seed {seed}: {dead} dead ends in {cells} cells");
        }
    }

    [Test]
    public void EveryWall_AndCorner_IsBuiltOnce_OnAWallThatStands()
    {
        var world = new MazeWorld(42);
        var at = new HashSet<Vector2Int>();
        int runs = 0;
        for (int cx = Min; cx <= Max; cx++)
            for (int cz = Min; cz <= Max; cz++)
                foreach (MazeGenerator.Piece p in MazeGenerator.Pieces(world, cx, cz))
                {
                    var key = new Vector2Int(Mathf.RoundToInt(p.Position.x * 10f), Mathf.RoundToInt(p.Position.z * 10f));
                    Assert.IsTrue(at.Add(key), $"two pieces at {p.Position}");
                    if (p.Kind != MazeGenerator.PieceKind.Run) continue;
                    runs++;
                    // A run stands on the middle of a wall, which is closed.
                    float cell = MazeGenerator.CellSize;
                    bool standing = p.Yaw == 90f
                        ? world.WallAlongZ(Mathf.RoundToInt(p.Position.x / cell), Mathf.FloorToInt(p.Position.z / cell))
                        : world.WallAlongX(Mathf.FloorToInt(p.Position.x / cell), Mathf.RoundToInt(p.Position.z / cell));
                    Assert.IsTrue(standing, $"a run where there's no wall, at {p.Position}");
                }
        Assert.Greater(runs, 0);
    }

    [Test]
    public void EachChunk_SellsOneOfTheShopsSections()
    {
        for (int cx = -3; cx <= 3; cx++)
            CollectionAssert.Contains(ProductCatalog.StockSections, MazeGenerator.SectionOf(1, cx, 0));
    }
}
