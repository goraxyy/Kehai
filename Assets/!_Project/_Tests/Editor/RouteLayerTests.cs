using System.Linq;
using Kehai.Karen;
using Kehai.Replay;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Karen 0.2.0, phase 3: her route guesses drawn in replays (B3.3), written by the second builder.
// Only from what the recording says at t: seeking back and forth, weights, windows, old files.

public class RouteLayerTests
{
    GameObject root;
    Camera cam;
    ReplayData data;
    MindLayers mind;
    RouteFrame first, second;

    [SetUp] public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("route layer test");
        cam = new GameObject("camera").AddComponent<Camera>();
        cam.transform.SetParent(root.transform); cam.transform.position = new Vector3(0,10,-10);
        cam.transform.LookAt(Vector3.zero); cam.aspect = 16f/9f;
        data = new ReplayData { Header = new KrecHeader() };
        first = new RouteFrame { T=10, LostAt=8, Seen=Vector3.zero, Sprint=6, Walk=3, Crouch=2,
            Guesses=new[]{ new RouteLine { Name="stockroom", Weight=.8f, Path=new[]{Vector3.zero,new Vector3(6,0,0)}, Along=new[]{0f,6f}},
            new RouteLine {Name="hiding close by", Weight=.2f, Path=new[]{new Vector3(0,0,1)},Along=new[]{0f}} } };
        second = new RouteFrame {T=12,LostAt=8,Sprint=6,Walk=3,Crouch=2,Guesses=new[]{
            new RouteLine {Name="stockroom",Weight=.1f,Path=first.Guesses[0].Path,Along=first.Guesses[0].Along},
            new RouteLine {Name="hiding close by",Weight=.9f,Path=first.Guesses[1].Path,Along=first.Guesses[1].Along}}};
        data.Routes.Add(first); data.Routes.Add(second); data.Routes.Add(new RouteFrame{T=14});
        mind = new MindLayers(new ReplayStage(data,root.transform),root.transform) {Shown=MindLayer.Routes};
    }
    [TearDown] public void Cleanup() { Object.DestroyImmediate(root); }
    LineRenderer[] Lines(bool inactive=false) => root.GetComponentsInChildren<LineRenderer>(inactive);
    TextMesh RouteLabel() => root.GetComponentsInChildren<TextMesh>().First(t=>t.text.Contains("stockroom"));

    [Test] public void RoutesAreSelectableWithoutChangingExistingLayerBits()
    {
        Assert.AreEqual(32,(int)MindLayer.Actors);
        Assert.AreEqual(MindLayer.Routes,MindLayers.Parse("routes"));
        Assert.AreEqual(MindLayer.Routes,MindLayers.Parse("route"));
        Assert.AreEqual("routes",MindLayers.Describe(MindLayer.Routes));
        Assert.AreEqual(MindLayer.All,MindLayers.Parse(MindLayers.Describe(MindLayer.All)));
        Assert.IsTrue((MindLayer.All & MindLayer.Routes)!=0);
    }
    [Test] public void SeekingUsesRecordedPathsAndClearsFutureAndDroppedGuesses()
    {
        mind.Apply(9,cam); Assert.IsEmpty(Lines());
        mind.Apply(10,cam); Assert.AreEqual(2,Lines().Length);
        Assert.That(Lines()[0].GetPosition(1).x,Is.EqualTo(6).Within(.001f));
        mind.Apply(14,cam); Assert.IsEmpty(Lines());
        mind.Apply(10,cam); Assert.AreEqual(2,Lines().Length);
        mind.Apply(9,cam); Assert.IsEmpty(Lines());
        Assert.AreEqual(2,Lines(true).Length,"seeking reuses the existing drawings");
        mind.Shown=MindLayer.Belief; mind.Apply(10,cam); Assert.IsEmpty(Lines());
        mind.Shown=MindLayer.Routes; mind.Apply(10,cam); Assert.AreEqual(2,Lines().Length);
    }
    [Test] public void WeightUpdatesAndWindowsUseRecordingTimeNotTheLiveClock()
    {
        mind.Apply(10,cam); float width=Lines()[0].startWidth;
        StringAssert.Contains("80",RouteLabel().text);
        StringAssert.Contains("Arrival 1.0–3.0s; likely 2.0s after last sighting",RouteLabel().text);
        mind.Apply(12,cam); Assert.Less(Lines()[0].startWidth,width);
        StringAssert.Contains("10",RouteLabel().text);
        mind.Apply(10,cam); Assert.AreEqual(width,Lines()[0].startWidth);
        StringAssert.Contains("80",RouteLabel().text);
        Assert.AreEqual(.8f,first.Guesses[0].Weight,"drawing must not mutate recorded guesses");
    }
    [Test] public void HidingGuessIsVisibleAndLabelsArePlainText()
    {
        first.Guesses[0].Name="<b>stockroom</b>";
        mind.Apply(10,cam);
        Assert.IsTrue(Lines()[1].loop); Assert.AreEqual(24,Lines()[1].positionCount);
        Assert.IsFalse(RouteLabel().richText); StringAssert.Contains("<b>stockroom</b>",RouteLabel().text);
        Assert.IsEmpty(root.GetComponentsInChildren<Collider>());
    }
    [Test] public void OldRecordingsHaveNoRoutesAndReplayConeStaysHidden()
    {
        data.Routes.Clear(); mind.Shown=MindLayer.All; mind.Apply(15,cam);
        Assert.IsEmpty(Lines());
        var cone=root.transform.Find("Karen's mind (replay)/Her view cone");
        Assert.IsNotNull(cone); Assert.IsFalse(cone.gameObject.activeSelf);
    }
    [Test] public void RouteMaterialsAreReleasedWithTheReplay()
    {
        mind.Apply(10,cam);
        Material line=Lines()[0].sharedMaterial;
        Material text=RouteLabel().GetComponent<Renderer>().sharedMaterial;
        Object.DestroyImmediate(root); root=null;
        Assert.IsTrue(line==null); Assert.IsTrue(text==null);
    }
}
