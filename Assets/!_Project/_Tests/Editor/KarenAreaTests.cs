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

    // ---- phase 2: the hunt -----------------------------------------------------------------

    [Test]
    public void SeenOutside_SheWontHunt()
    {
        var config = new KarenConfig { rung = KarenRung.E_Ledger, seed = 7 };
        var ledger = new KarenLedger(config);
        var c = new KarenContext { Config = config, Map = map, Belief = Grid(), Director = new KarenDirector(config, ledger) };
        Assert.Less(c.Belief.OutsideMass, 0.5f);
        c.Belief.Apply(new Observation(SenseChannel.Sight, ParkingLot, 0.95f, 1.5f, "seen in the parking lot"));
        Assert.IsFalse(KarenDirector.HuntAllowed(true, false, false, 999f, 1f, c.Belief.OutsideMass, config, out string why), "she'd hunt you in the parking lot");
        Assert.AreEqual(0f, c.Director.ActionPermission(ActionId.Hunt, c), "she'd hunt you in the parking lot");
    }

    [Test]
    public void TheForecast_RunsOnAlongTheFloor_TheWayYouWereGoing()
    {
        BeliefGrid grid = Grid();
        // A spot on the sales floor with floor ahead of it along +x.
        int start = Enumerable.Range(0, map.CellCount).First(c =>
            StoreMap.AreaAt(map.CellPosition[c]) == "Sales floor" && map.CellAt(map.CellPosition[c] + Vector3.right * 6f, 0.8f) >= 0);
        Vector3 seen = map.CellPosition[start];
        Assert.AreEqual(seen, TacticHelpers.Forecast(map, grid, seen, Vector3.zero, 3f), "standing still, you're where she saw you");

        Vector3 ahead = TacticHelpers.Forecast(map, grid, seen, Vector3.right * 4f, 3f);
        Vector3 moved = ahead - seen;
        moved.y = 0f;
        Assert.IsTrue(map.IsKarens(map.CellAt(ahead)), "the forecast is outside her part");
        Assert.Greater(moved.magnitude, 3f, "she'd run to where she lost you, not on");
        Assert.LessOrEqual(moved.magnitude, 12.5f, "further than 3 s at 4 m/s");
        Assert.Greater(Vector3.Dot(moved.normalized, Vector3.right), 0.3f, "the forecast isn't the way you were going");
    }

    // ---- phase 3: guesses about your route, and cutting you off -----------------------------

    (string, Vector3)[] Goals(BeliefGrid grid)
    {
        var goals = new List<(string, Vector3)>();
        Landmark? clock = map.FindLandmark(LandmarkKind.TimeClock);
        if (clock.HasValue) goals.Add(("to the time clock", clock.Value.Position));
        Landmark? box = map.FindLandmark(LandmarkKind.BreakerBox);
        if (box.HasValue) goals.Add(("to the breaker box", box.Value.Position));
        goals.Add(("out the front", grid.ExitPosition(NearestExit(grid, Pavement))));
        goals.Add(("out the back", grid.ExitPosition(NearestExit(grid, BackstreetMouth))));
        return goals.ToArray();
    }

    // A runner on the sales floor, `near`–`far` metres' walk from `goal`, heading there at a
    // sprint, and her guesses from the moment she lost them.
    RouteGuess Runner(BeliefGrid grid, RoutePredictor p, out Vector3 start, string goal = "to the time clock", float near = 22f, float far = 28f)
    {
        var goals = Goals(grid);
        Assume.That(goals.Any(g => g.Item1 == goal), $"no '{goal}' in this scene");
        Vector3 at = goals.First(g => g.Item1 == goal).Item2;
        float[] d = map.Distances(map.CellAt(at), float.MaxValue, null, TacticHelpers.NotHers(map));
        int cell = Enumerable.Range(0, map.CellCount)
            .First(c => StoreMap.AreaAt(map.CellPosition[c]) == "Sales floor" && d[c] > near && d[c] < far);
        start = map.CellPosition[cell];

        p.Build(start, Vector3.zero, 0f, goals);
        RouteGuess route = p.Guesses.First(g => g.Name == goal);
        int i = 1;
        while (i < route.Path.Length - 1 && route.Along[i] < 6f) i++;
        Vector3 heading = map.CellPosition[route.Path[i]] - start;
        heading.y = 0f;
        p.Build(start, heading.normalized * RoutePredictor.Sprint, 0f, goals);
        grid.ResetTo(start, 2f);
        return p.Guesses.First(g => g.Name == goal);
    }

    // She hears your steps along `route`, once a second, and reweighs her guesses.
    static void Follow(BeliefGrid grid, RoutePredictor p, RouteGuess route, float speed, int seconds)
    {
        for (int t = 1; t <= seconds; t++)
        {
            for (int k = 0; k < 10; k++) grid.Predict(0.1f, 4f);
            grid.Apply(new Observation(SenseChannel.Hearing, p.PositionOn(route, t, speed), 0.5f, 3f, "your steps"));
            p.Reweigh(grid, t);
        }
    }

    [Test]
    public void Guesses_KeepGoing_AndTheRightOneComesOnTop()
    {
        BeliefGrid grid = Grid();
        var p = new RoutePredictor(map);
        RouteGuess right = Runner(grid, p, out _);
        Assert.That(p.Guesses.Count, Is.InRange(3, 6), "3–6 guesses");
        Follow(grid, p, right, RoutePredictor.Sprint, 3);
        Assert.AreSame(right, p.Best, $"heard all the way to the clock, she guesses {p.Best.Name} ({p.Best.Weight:P0})");
    }

    [Test]
    public void Guesses_TurnBack_AndTheTurnBackGuessRises()
    {
        BeliefGrid grid = Grid();
        var p = new RoutePredictor(map);
        Runner(grid, p, out _);
        RouteGuess back = p.Guesses.FirstOrDefault(g => g.Kind == GuessKind.TurnedBack);
        Assert.IsNotNull(back, "no turn-back guess");
        float before = back.Weight;
        Follow(grid, p, back, RoutePredictor.Walk, 3);
        Assert.Greater(back.Weight, before, "heard going back the way you came, the turn-back guess didn't rise");
        Assert.AreSame(back, p.Best, $"she guesses {p.Best.Name} ({p.Best.Weight:P0})");
    }

    [Test]
    public void Guesses_SheLookedAndYouWerentThere_AndThatGuessFalls()
    {
        BeliefGrid grid = Grid();
        var p = new RoutePredictor(map);
        RouteGuess right = Runner(grid, p, out _);
        for (int k = 0; k < 20; k++) grid.Predict(0.1f, 4f);
        p.Reweigh(grid, 2f);
        float before = right.Weight;
        // She sees the stretch of that route where you'd be by now, and the floor either side of
        // it, as her eyes would: nobody.
        var seen = new HashSet<int>();
        for (int i = 0; i < right.Path.Length; i++)
        {
            if (right.Along[i] < RoutePredictor.Crouch * 2f - 1.5f || right.Along[i] > RoutePredictor.Sprint * 2f + 1.5f) continue;
            int c = right.Path[i];
            seen.Add(c);
            for (int e = map.EdgeStart[c]; e < map.EdgeStart[c + 1]; e++) seen.Add(map.EdgeTo[e]);
        }
        var cells = seen.ToList();
        grid.ApplySweep(cells, cells.Select(_ => 0.95f).ToList());
        p.Reweigh(grid, 2f);
        Assert.Less(right.Weight, before * 0.5f, "she looked where that guess put you and found nobody, and still believes it");
    }

    // A scripted runner sprints 45–60 m across the store to the breaker box; she starts 12–25 m
    // away, having lost them. Running at her best guess against cutting them off, over eight
    // starts: Cut is never worse than the sprint by more than a cell, and gets clearly closer in
    // some. (On a short dash — 25 m to the time clock from 10–18 m — there's nowhere she can be a
    // second first, and Cut never fires: the first run of this test showed it.)
    [Test]
    public void Cut_GetsCloserThanTheSprint_AndKeepsToHerPart()
    {
        BeliefGrid probe = Grid();
        var probeP = new RoutePredictor(map);
        Runner(probe, probeP, out Vector3 start, LongGoal, LongNear, LongFar);
        var rng = new System.Random(11);
        var starts = Enumerable.Range(0, map.CellCount)
            .Where(c => map.IsKarens(c) && StoreMap.AreaAt(map.CellPosition[c]) == "Sales floor")
            .Where(c => { Vector3 d = map.CellPosition[c] - start; d.y = 0f; return d.magnitude > 12f && d.magnitude < 25f; })
            .OrderBy(_ => rng.Next()).Take(8).ToList();
        Assume.That(starts.Count, Is.EqualTo(8));

        float cut = 0f, sprint = 0f;
        int better = 0;
        var lines = new List<string>();
        foreach (int her in starts)
        {
            float c = Chase(her, useCut: true), r = Chase(her, useCut: false);
            cut += c;
            sprint += r;
            if (c < r - 1.5f) better++;
            lines.Add($"{c:0.0} vs {r:0.0}");
            Assert.LessOrEqual(c, r + 1.5f, $"from {map.CellPosition[her]}, Cut ended {c:0.0} m off, the sprint {r:0.0} m");
        }
        string summary = $"closest approach, Cut vs sprint: {string.Join(", ", lines)}; means {cut / starts.Count:0.0} vs {sprint / starts.Count:0.0} m";
        Debug.Log(summary);
        Assert.Greater(better, 0, "Cut never got clearly closer: " + summary);
        Assert.Less(cut, sprint, summary);
    }

    const string LongGoal = "to the breaker box";
    const float LongNear = 45f, LongFar = 60f;

    // The closest she comes to the runner, moving on her own cells at 7.5 m/s.
    float Chase(int her, bool useCut)
    {
        BeliefGrid grid = Grid();
        var p = new RoutePredictor(map);
        RouteGuess route = Runner(grid, p, out _, LongGoal, LongNear, LongFar);
        var fields = new Dictionary<int, float[]>();
        float herCarry = 0f, runAt = 0f, closest = float.MaxValue;
        for (int step = 0; step < 150; step++)
        {
            float now = (step + 1) * 0.1f;
            runAt = Mathf.Min(route.Length, runAt + RoutePredictor.Sprint * 0.1f);
            Vector3 you = p.PositionOn(route, runAt / RoutePredictor.Sprint, RoutePredictor.Sprint);
            grid.Predict(0.1f, 4f);
            if (step % 10 == 9) grid.Apply(new Observation(SenseChannel.Hearing, you, 0.4f, 3f, "your steps"));
            if (step % 3 == 0) p.Reweigh(grid, now);

            int target = useCut && CutTactic.ChooseSpot(map, p, map.CellPosition[her], 7.5f, now, out CutTactic.CutSpot spot)
                ? spot.Cell
                : map.CellAt(p.Expected(now, 2f, RoutePredictor.Sprint));
            if (target >= 0) her = StepToward(her, target, ref herCarry, 7.5f * 0.1f, fields);
            Assert.IsTrue(map.IsKarens(her), $"she left her part of the building, at {map.CellPosition[her]}");

            Vector3 gap = map.CellPosition[her] - you;
            gap.y = 0f;
            closest = Mathf.Min(closest, gap.magnitude);
            if (runAt >= route.Length) break;
        }
        return closest;
    }

    int StepToward(int from, int target, ref float carry, float metres, Dictionary<int, float[]> fields)
    {
        if (!fields.TryGetValue(target, out float[] d))
            fields[target] = d = map.Distances(target, float.MaxValue, null, TacticHelpers.NotHers(map));
        carry += metres;
        int cell = from;
        while (cell != target)
        {
            int next = -1;
            float best = d[cell], length = 0f;
            for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++)
                if (d[map.EdgeTo[e]] < best) { best = d[map.EdgeTo[e]]; next = map.EdgeTo[e]; length = map.EdgeLength[e]; }
            if (next < 0 || carry < length) break;
            carry -= length;
            cell = next;
        }
        if (cell == target) carry = 0f;
        return cell;
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
