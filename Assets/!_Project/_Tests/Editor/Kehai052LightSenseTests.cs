using System;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// 0.5.2, C6.4: her light sense keeps each ceiling lamp's room instead of looking it up for every
// lamp on every call (that lookup was most of a batch frame's extra cost since 0.5.0). The answer
// must not change: a lamp lights only its own room, and a new room rule or a moved lamp is seen.
public class Kehai052LightSenseTests
{
    GameObject root;
    Func<Vector3, string> previousRoom;

    [SetUp]
    public void Setup()
    {
        if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("Kehai052LightSenseTests");
        previousRoom = RoomLightSwitch.RoomOf;
        RoomLightSwitch.RoomOf = p => p.x < 0f ? "Cold storage" : "Office";
    }

    [TearDown]
    public void Cleanup()
    {
        Object.DestroyImmediate(root);
        RoomLightSwitch.RoomOf = previousRoom ?? RoomLightSwitch.PhysicalRoom;
        LightProbe.Refresh();
    }

    Light Lamp(Vector3 at)
    {
        var group = new GameObject("CeilingLights");
        group.transform.SetParent(root.transform, false);
        var lamp = new GameObject("Lamp").AddComponent<Light>();
        lamp.transform.SetParent(group.transform, false);
        lamp.transform.position = at;
        LightProbe.Refresh();
        return lamp;
    }

    [Test]
    public void ALampLightsOnlyItsOwnRoom_AndANewRoomRuleIsSeen()
    {
        Lamp(new Vector3(-1f, 3f, 0f));
        Assert.Greater(LightProbe.LevelAt(new Vector3(-2f, 0f, 0f)), 0.8f, "under the lamp, in its room");
        Assert.Less(LightProbe.LevelAt(new Vector3(1f, 0f, 0f)), 0.2f, "2 m away, but through the wall in the office");

        RoomLightSwitch.RoomOf = p => p.x < -1.5f ? "Cold storage" : "Office";   // the lamp is now in the office
        Assert.Less(LightProbe.LevelAt(new Vector3(-2f, 0f, 0f)), 0.2f, "the cached room follows the new rule");
        Assert.Greater(LightProbe.LevelAt(new Vector3(1f, 0f, 0f)), 0.8f);
    }

    [Test]
    public void AMovedLampIsLookedUpAgain()
    {
        Light lamp = Lamp(new Vector3(-1f, 3f, 0f));
        Assert.Greater(LightProbe.LevelAt(new Vector3(-0.5f, 0f, 0f)), 0.8f);
        lamp.transform.position = new Vector3(0.5f, 3f, 0f);   // across the line, same grid bucket
        Assert.Less(LightProbe.LevelAt(new Vector3(-0.5f, 0f, 0f)), 0.2f, "its room is worked out again where it now hangs");
    }
}
