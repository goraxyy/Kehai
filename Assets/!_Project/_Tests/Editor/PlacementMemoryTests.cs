using System.IO;
using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.3.0, placement memory (build plan C1.5): for each trick, a score per place, from how
// well doing it there paid before. She mostly picks the best she knows and sometimes tries the
// least tried; it fades over shifts and is saved with the Ledger.
public class PlacementMemoryTests
{
    static readonly PlaceCandidate[] Three =
    {
        new PlaceCandidate("Aisle 3", 0.4f), new PlaceCandidate("Aisle 7", 0.5f), new PlaceCandidate("Dairy", 0.3f),
    };

    [Test]
    public void BeforeAnyUse_SheGoesByHerGuess()
    {
        var m = new PlacementMemory();
        Assert.AreEqual(1, m.Pick("spill", Three, null));
        Assert.AreEqual(0.5f, m.Score("spill", Three[1]), 1e-5f);
    }

    [Test]
    public void APlaceThatPaid_BeatsABetterGuess()
    {
        var m = new PlacementMemory();
        m.Settle("spill", "Dairy", 1f, 1);
        m.Settle("spill", "Dairy", 1f, 1);
        Assert.Greater(m.Score("spill", Three[2]), m.Score("spill", Three[1]));
        Assert.AreEqual(2, m.Pick("spill", Three, null));
    }

    [Test]
    public void APlaceThatFailed_LosesToTheNext()
    {
        var m = new PlacementMemory();
        m.Settle("spill", "Aisle 7", 0f, 1);
        Assert.AreEqual(0, m.Pick("spill", Three, null), "Aisle 7 paid nothing, so Aisle 3's guess wins");
    }

    [Test]
    public void EachTrick_KeepsItsOwnPlaces()
    {
        var m = new PlacementMemory();
        m.Settle("spill", "Dairy", 1f, 1);
        Assert.AreEqual(0.3f, m.Score("shelf_sweep", Three[2]), 1e-5f);
    }

    [Test]
    public void NowAndThen_SheTriesTheLeastTried()
    {
        var m = new PlacementMemory();
        for (int i = 0; i < 4; i++) m.Settle("spill", "Aisle 7", 1f, 1);
        m.Settle("spill", "Aisle 3", 0.2f, 1);
        var rng = new KarenRng(11);
        int dairy = 0, picks = 2000;
        for (int i = 0; i < picks; i++)
            if (m.Pick("spill", Three, rng) == 2) dairy++;
        // Dairy, never tried, comes up only when she explores: about one pick in seven.
        Assert.That(dairy / (float)picks, Is.InRange(PlacementMemory.ExploreChance * 0.7f, PlacementMemory.ExploreChance * 1.3f));
    }

    [Test]
    public void WithoutLearning_SheNeverExplores_AndIgnoresWhatPaid()
    {
        var m = new PlacementMemory();
        for (int i = 0; i < 5; i++) m.Settle("spill", "Dairy", 1f, 1);
        var rng = new KarenRng(3);
        for (int i = 0; i < 200; i++) Assert.AreEqual(1, m.Pick("spill", Three, rng, learn: false));
    }

    // Fairness rule 7: change your habits and she loses the place.
    [Test]
    public void APlaceSheStopsUsing_FadesBackToHerGuess_ThenGoes()
    {
        var m = new PlacementMemory();
        m.Settle("spill", "Dairy", 1f, 1);
        m.Settle("spill", "Dairy", 1f, 1);
        float learned = m.Score("spill", Three[2]);
        m.Fade(1);
        Assert.AreEqual(learned, m.Score("spill", Three[2]), 1e-5f, "used this shift: kept");
        m.Fade(2);
        m.Fade(3);
        Assert.Less(m.Score("spill", Three[2]), learned);
        Assert.Greater(m.Score("spill", Three[2]), Three[2].Prior);
        for (int s = 4; s < 12; s++) m.Fade(s);
        Assert.IsNull(m.Find("spill", "Dairy"), "forgotten");
        Assert.AreEqual(Three[2].Prior, m.Score("spill", Three[2]), 1e-5f);
    }

    [Test]
    public void ItIsSavedWithTheLedger_AndDropsTricksThatAreGone()
    {
        string path = Path.Combine(Path.GetTempPath(), "kehai_places_" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var a = new KarenLedger(new KarenConfig { ledgerPath = path }) { Persistent = true };
            a.Load();
            a.Places.Settle("spill", "Dairy", 0.8f, 1);
            a.Places.Settle("camera_bolt_on", "Aisle 3 · slot 2 · 45° · -10°", 0.6f, 1);
            a.Save();

            // An older save that still knew a trick 0.3.0 removed.
            LedgerData saved = JsonUtility.FromJson<LedgerData>(File.ReadAllText(path));
            saved.places.Add(new PlaceStat { trick = "pa_decoy", key = "Aisle 1", q = 1f, n = 3f });
            File.WriteAllText(path, JsonUtility.ToJson(saved));

            var b = new KarenLedger(new KarenConfig { ledgerPath = path }) { Persistent = true };
            b.Load();
            Assert.AreEqual(0.8f, b.Places.Find("spill", "Dairy").q, 1e-5f);
            Assert.IsNotNull(b.Places.Find("camera_bolt_on", "Aisle 3 · slot 2 · 45° · -10°"));
            Assert.AreEqual(2, b.Places.Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Test]
    public void BelowTheLedgerRung_NothingCarriesToTheNextShift()
    {
        var ledger = new KarenLedger(new KarenConfig { rung = KarenRung.D_Bandit }) { Persistent = false };
        ledger.Load();
        ledger.BeginShift(1);
        ledger.Places.Settle("spill", "Dairy", 1f, 1);
        ledger.BeginShift(2);
        Assert.AreEqual(0, ledger.Places.Count);
    }

    [Test]
    public void ItKeepsAtMost64PlacesATrick_DroppingTheLeastUsed()
    {
        var m = new PlacementMemory();
        m.Settle("camera_bolt_on", "keep", 1f, 1);
        m.Settle("camera_bolt_on", "keep", 1f, 1);
        for (int i = 0; i < 80; i++) m.Settle("camera_bolt_on", "slot " + i, 0.5f, 1);
        Assert.AreEqual(PlacementMemory.MaxPerTrick, m.All.Count(p => p.trick == "camera_bolt_on"));
        Assert.IsNotNull(m.Find("camera_bolt_on", "keep"));
    }
}
