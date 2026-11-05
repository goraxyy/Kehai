using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Karen 0.2.0, phase 1: her part of the building. Its names (StoreMap.AreaAt), her belief
// grid with one cell for the outside, every place she looks inside it, and a body that can't
// walk round the building.
public class KarenAreaTests
{
    // ---- the names, without the scene ------------------------------------------------------

    [TestCase(18f, -140f, "Staff room")]
    [TestCase(18f, -130f, "Office")]
    [TestCase(18f, -116f, "WC")]
    [TestCase(18f, -160f, "Backstreet")]
    [TestCase(50f, -108f, "Pavement")]
    [TestCase(50f, -100f, "Street")]
    [TestCase(5f, -108f, "Street")]
    [TestCase(5f, -140f, "West alley")]
    [TestCase(90f, -140f, "East alley")]
    [TestCase(50f, -195f, "South yard")]
    [TestCase(50f, -115f, "Lobby")]
    [TestCase(50f, -150f, "Sales floor")]
    [TestCase(35f, -182f, "Stockroom")]
    [TestCase(70f, -182f, "Cold storage")]
    public void EveryPartOfTheBuilding_HasItsName(float x, float z, string area)
    {
        Assert.AreEqual(area, StoreMap.AreaAt(new Vector3(x, 0f, z)));
    }

    [Test]
    public void HerPart_IsTheBuildingTheBackstreetAndThePavement()
    {
        foreach (string a in new[] { "Sales floor", "Lobby", "Stockroom", "Cold storage", "Staff room", "Office", "WC", "Backstreet", "Pavement" })
            Assert.IsTrue(StoreMap.KarenWorksIn(a), a);
        foreach (string a in new[] { "Street", "West alley", "East alley", "South yard" })
            Assert.IsFalse(StoreMap.KarenWorksIn(a), a);
        Assert.IsTrue(StoreMap.IsOutdoors("Pavement"), "the pavement is still outdoors: the sun lights it");
    }

    [Test]
    public void TheOfficeAndTheWc_AreLitAsRoomsOfTheStaffRoom()
    {
        Assert.AreEqual(RoomLighting.StaffRoom, RoomLighting.BitFor("Office"));
        Assert.AreEqual(RoomLighting.StaffRoom, RoomLighting.BitFor("WC"));
        Assert.AreEqual(RoomLighting.Outside, RoomLighting.BitFor("Pavement"));
    }

    // ---- with the store -------------------------------------------------------------------

    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    StoreMap map;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        map = StoreMap.Rebuild();
    }

    // The tests below that watch belief move switch off the drift back to the prior, which
    // would otherwise put a little mass everywhere.
    BeliefGrid Grid(bool drift = true)
    {
        var grid = new BeliefGrid(map);
        if (!drift) grid.Uncertainty = 0f;
        return grid;
    }

    static int NearestExit(BeliefGrid grid, Vector3 p)
    {
        int best = -1;
        float bestSqr = float.MaxValue;
        for (int k = 0; k < grid.ExitCount; k++)
        {
            Vector3 d = grid.ExitPosition(k) - p;
            if (d.x * d.x + d.z * d.z < bestSqr) { bestSqr = d.x * d.x + d.z * d.z; best = k; }
        }
        return best;
    }

    static readonly Vector3 Pavement = new Vector3(50f, 0f, -108f);
    static readonly Vector3 BackstreetMouth = new Vector3(11f, 0f, -160f);
    static readonly Vector3 ParkingLot = new Vector3(50f, 0f, -96f);

    [Test]
    public void HerGrid_IsHerCellsAndOneOutsideCell()
    {
        BeliefGrid grid = Grid();
        int hers = Enumerable.Range(0, map.CellCount).Count(map.IsKarens);
        Assert.That(hers, Is.InRange(2000, 2700), $"her part of the store is {hers} cells");
        Assert.GreaterOrEqual(grid.OutsideCell, 0, "no outside cell");
        Assert.IsFalse(map.IsKarens(grid.OutsideCell), "the outside cell is in her part");
        for (int c = 0; c < map.CellCount; c++)
            if (!map.IsKarens(c) && c != grid.OutsideCell) Assert.AreEqual(0f, grid[c], $"cell {c}, outside, holds belief");
        Assert.AreEqual(grid.OutsidePrior, grid.OutsideMass, 1e-5f, "the outside starts with next to nothing");
        Assert.GreaterOrEqual(grid.ExitCount, 2, "the pavement and the backstreet are two ways out");
    }

    [Test]
    public void TheWalkRound_FromFrontToBack_TakesAboutFifteenSeconds()
    {
        BeliefGrid grid = Grid();
        int front = NearestExit(grid, Pavement), back = NearestExit(grid, BackstreetMouth);
        Assert.AreNotEqual(front, back, "the front and the back are one exit");
        Assert.That(grid.ExitDelay(front, back), Is.InRange(10f, 25f), "about 95 m at a 6.5 m/s sprint");
    }

    [Test]
    public void SeenOutside_SheBelievesYouOutside_AndYouComeBackTheWayYouWent()
    {
        BeliefGrid grid = Grid(drift: false);
        grid.Apply(new Observation(SenseChannel.Sight, ParkingLot, 0.95f, 1.5f, "seen in the parking lot"));
        Assert.Greater(grid.OutsideMass, 0.5f, "a sighting in the parking lot left her believing you inside");

        int front = NearestExit(grid, Pavement), back = NearestExit(grid, BackstreetMouth);
        Assert.AreEqual(front, grid.LikeliestExit(), "the outside's mass should wait by the front");

        IReadOnlyList<int> backDoor = grid.ExitBorder(back);
        for (int i = 0; i < 100; i++) grid.Predict(0.1f, 2.5f);      // 10 s: too soon to have walked round
        float early = backDoor.Sum(c => grid[c]);
        for (int i = 0; i < 200; i++) grid.Predict(0.1f, 2.5f);      // 30 s in all
        float late = backDoor.Sum(c => grid[c]);
        Assert.Less(early, 1e-3f, "belief reached the back before you could have walked round");
        // It comes round small — every exit reachable by then takes an equal share — but it comes.
        Assert.Greater(late, 1e-3f, "belief never came round to the back");
        Assert.Greater(late, early * 10f, "belief at the back didn't grow once the walk round was possible");
        Assert.Greater(grid.ExitBorder(front).Sum(c => grid[c]), 0f, "nothing came back in at the front");
    }

    [Test]
    public void EveryPlaceSheLooks_IsInHerPart()
    {
        BeliefGrid grid = Grid();
        var rng = new System.Random(7);
        Bounds b = map.Bounds;
        for (int i = 0; i < 1000; i++)
        {
            var p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble()), 0f,
                                Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble()));
            grid.Apply(new Observation(SenseChannel.Sight, p, 0.3f + 0.65f * (float)rng.NextDouble(), 2f, "test"));
            if (rng.NextDouble() < 0.3)
                for (int k = 0; k < 20; k++) grid.Predict(0.25f, 2.5f);

            int region = TacticHelpers.LookRegion(map, grid);
            Assert.IsTrue(TacticHelpers.Hers(map, map.Regions[region]), $"she'd look in {map.RegionName(region)}, outside her part");

            Vector3 sent = KarenArea.Clamp(map, p);
            Assert.IsTrue(map.IsKarens(map.CellAt(sent)), $"sent to {p}, she'd go to {sent}, outside her part");
        }
    }

    [Test]
    public void TheCoffeeMachineAndTheRadio_AreOnTheStaffRoomsSideOfTheWall()
    {
        foreach (LandmarkKind kind in new[] { LandmarkKind.CoffeeMachine, LandmarkKind.Radio })
        {
            Landmark? l = map.FindLandmark(kind);
            if (!l.HasValue) Assert.Inconclusive($"no {kind} in this scene");
            Assert.IsTrue(map.IsKarens(l.Value.Cell), $"{l.Value.Name} sits in {map.RegionName(l.Value.Region)}");
        }
    }

    // The NavMesh marks that keep her in, baked into the open scene, then the scene reloaded.
    [Test]
    public void HerBody_NeverWalksRoundTheBuilding()
    {
        string report = KarenArea.Install();
        try
        {
            Assert.IsTrue(KarenArea.Installed, report);
            Assert.IsTrue(NavMesh.SamplePosition(Pavement, out NavMeshHit from, 3f, KarenArea.AgentMask), "no floor of hers on the pavement");
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(18f, 0f, -162f), out NavMeshHit to, 3f, KarenArea.AgentMask), "no floor of hers in the backstreet");

            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(from.position, to.position, KarenArea.AgentMask, path), "no path for her at all");
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status, "she can't get from the pavement to the backstreet");
            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 a = path.corners[i - 1], c = path.corners[i];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, c) / 0.5f));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(a, c, (float)s / steps);
                    Assert.IsTrue(StoreMap.KarenWorksIn(StoreMap.AreaAt(p)), $"her way from the pavement to the backstreet goes through the {StoreMap.AreaAt(p)} at {p}");
                }
            }
        }
        finally
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            map = StoreMap.Rebuild();
        }
    }
}
