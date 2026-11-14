using System;
using System.Linq;
using Kehai.Eval;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

public class StoreLayouts050Tests
{
    [Test] public void TrainingPoolNeverContainsReservedLayouts()
    {
        for (int seed=10000;seed<30000;seed++)
            for(int shift=1;shift<=4;shift++)
                Assert.IsTrue(EvalSplits.LayoutMayTrainOn(StoreLayouts.ForTraining(seed,shift)));
    }
    [Test] public void CareerScheduleHasApprovedPair()
    {
        Assert.AreEqual(new[]{"today","today","labyrinth","brick-bond"},Enumerable.Range(1,4).Select(StoreLayouts.ForShift));
    }
    [Test] public void RetainedDataHasCorrectMetreScale()
    {
        Assert.AreEqual(6,StoreLayouts.All.Count);
        foreach(var layout in StoreLayouts.All)
        {
            Assert.AreEqual(26,layout.Width);Assert.AreEqual(24,layout.Height);
            Assert.AreEqual(25f+1.25f,layout.Position(1,1).x);
            Assert.AreEqual(-121.25f,layout.Position(1,1).z);
            Assert.IsFalse(layout.Shelf(0,0));
        }
        Assert.GreaterOrEqual(StoreLayouts.Pitch-StoreLayouts.ShelfDepth,1.5f);
        Assert.Less(StoreLayouts.SharedBoard,1.25f);
        Assert.Greater(StoreLayouts.UpperBoard,1.25f);
    }
    [Test] public void HeldOutCannotEnterTrainingByExplicitName()
    {
        string before=StoreLayoutRuntime.EpisodeLayout;
        try
        {
            foreach(string id in new[]{"rings","split"})
            {
                StoreLayoutRuntime.EpisodeLayout=id;
                Assert.Throws<InvalidOperationException>(()=>StoreLayoutRuntime.Choose(3,10000,true,true));
                Assert.Throws<InvalidOperationException>(()=>StoreLayoutRuntime.Choose(3,20000,false,false));
                Assert.AreEqual(id,StoreLayoutRuntime.Choose(3,30000,false,true));
            }
        }
        finally { StoreLayoutRuntime.EpisodeLayout=before; }
    }
    [Test] public void UnknownLayoutIsRefused()
    {
        Assert.Throws<ArgumentException>(()=>StoreLayouts.Find("whatever"));
    }
    [Test] public void EveryAmbientSectionHasABayInEachLayout()
    {
        foreach(var layout in StoreLayouts.All)
            foreach(var zone in StoreLayout.Zones.Where(z=>z.Section!=ItemType.Dairy && z.Section!=ItemType.Frozen && z.Section!=ItemType.Confectionery))
                Assert.IsTrue(Enumerable.Range(1,layout.Height-2).Any(r=>Enumerable.Range(1,layout.Width-2).Any(c=>layout.Shelf(r,c)&&StoreLayout.SectionAt(layout.Position(r,c))==zone.Section)),layout.Name+" missing "+zone.Section);
        // Dairy/frozen retain authored cold fixtures; sweets retain the original checkout display.
    }
    [Test] public void BenchmarkPinsTheSameLoadedPackAcrossShiftSelection()
    {
        ModelPack previous=ModelPack.Current;
        var selected=new ModelPack();var replacement=new ModelPack();
        try
        {
            ModelPack.Use(selected);
            using(var pin=ModelPack.PinSelection())
            {
                // A scene/shift may try to select a newer pack. The career retains its snapshot.
                ModelPack.Use(replacement);
                ModelPack.Select("unused-while-pinned.kpack");
                Assert.AreSame(selected,ModelPack.Current);
            }
            // Disposing the pin does not replace a running brain; next normal selection may.
            Assert.AreSame(selected,ModelPack.Current);
        }
        finally { ModelPack.Use(previous); }
    }

}
