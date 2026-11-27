using System.IO;
using Kehai.Sandbox;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.2, C11.11: in the real store scene, the sandbox island and the moved models island can be
// walked onto from the road, with ground all the way and nothing in the way at the seams.
public class SandboxIslandTests
{
    const float Radius = 0.5f, Height = 2f;   // the player's CharacterController

    [SetUp]
    public void Store()
    {
        if (!File.Exists(SandboxIsland.StoreScene)) Assert.Ignore("the private store scene isn't in this project");
        EditorSceneManager.OpenScene(SandboxIsland.StoreScene, OpenSceneMode.Single);
    }

    [TearDown]
    public void Cleanup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    static bool Ground(Vector3 at, out float y)
    {
        y = 0f;
        if (!Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)) return false;
        y = hit.point.y;
        return true;
    }

    // A walk from `from` to `to`: ground under every half metre, within a step of the last, and
    // the player's capsule meets nothing on the way.
    static void Walkable(Vector3 from, Vector3 to, string what)
    {
        float lastY = float.NaN;
        int steps = Mathf.CeilToInt(Vector3.Distance(from, to) / 0.5f);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
            Assert.IsTrue(Ground(p, out float y), $"{what}: no ground at {p}");
            if (!float.IsNaN(lastY)) Assert.Less(Mathf.Abs(y - lastY), 0.3f, $"{what}: a step too high at {p}");
            lastY = y;
        }
        Vector3 bottom = from + Vector3.up * (Radius + 0.1f), top = from + Vector3.up * (Height - Radius);
        Vector3 d = to - from;
        bool blocked = Physics.CapsuleCast(bottom, top, Radius, d.normalized, out RaycastHit hit, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
        Assert.IsFalse(blocked, blocked ? $"{what}: {hit.collider.name} in the way at {hit.point}" : "");
    }

    [Test]
    public void TheIslands_CanBeWalkedOntoFromTheRoad()
    {
        var scene = EditorSceneManager.GetActiveScene();
        GameObject models = GameObject.Find(SandboxIsland.ModelsIslandName);
        Assert.IsNotNull(models);
        Transform part = models.transform.GetChild(0);   // the contents move; the root holds the store's NavMesh volume
        Vector3 before = part.position, rootBefore = models.transform.position;

        GameObject root = SandboxIsland.Build(scene, withAreas: false);
        Assert.That(Vector3.Distance(before + SandboxIsland.ModelsShift, part.position), Is.LessThan(0.001f), "the models island moved onto the road's edge");
        Assert.AreSame(root, SandboxIsland.Build(scene, withAreas: false), "built once");
        Assert.That(Vector3.Distance(before + SandboxIsland.ModelsShift, part.position), Is.LessThan(0.001f), "and moved once");
        Assert.AreEqual(rootBefore, models.transform.position, "its root, with the store's NavMesh volume, stayed");
        Physics.SyncTransforms();

        Walkable(new Vector3(60f, 0f, -47f), new Vector3(60f, 0f, -36f), "road to the models island");
        Walkable(new Vector3(-3f, 0f, -47f), new Vector3(-3f, 0f, -30f), "road to the sandbox island");
        Walkable(new Vector3(-10f, 0f, -20f), new Vector3(10f, 0f, -20f), "sandbox island to the models island");
    }

    // C8.1 (27 November): the store's NavMesh volume sits on the models island; moving the island
    // must leave it where it was, or the parking lot falls out of her map in every editor Play.
    [Test]
    public void MovingTheModelsIsland_LeavesTheStoresBakeVolumeWhereItWas()
    {
        GameObject models = GameObject.Find(SandboxIsland.ModelsIslandName);
        var surfaces = models.GetComponentsInChildren<Unity.AI.Navigation.NavMeshSurface>(true);
        Assert.IsNotEmpty(surfaces, "the store's NavMeshSurface is on the models island");
        var before = new Vector3[surfaces.Length];
        for (int i = 0; i < surfaces.Length; i++) before[i] = surfaces[i].transform.TransformPoint(surfaces[i].center);

        SandboxIsland.Build(EditorSceneManager.GetActiveScene(), withAreas: false);
        for (int i = 0; i < surfaces.Length; i++)
            Assert.AreEqual(before[i], surfaces[i].transform.TransformPoint(surfaces[i].center),
                $"{surfaces[i].name}'s bake volume stayed in the world, to the bit");
    }
}
