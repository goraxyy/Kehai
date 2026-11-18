using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kehai 0.5.1 items 6 and 11: room doors open both ways and one E is one use (the cold room's
// doors never opened); furniture leaves open outward and turn round mid-swing; the automatic
// doors are dead in a blackout, can be slid open by hand, and come back with the power.
// Built from boxes in an empty scene. Nothing here plays a sound: every clip is null.
public class Kehai051DoorTests
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Call(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    static void Power(bool on) => typeof(PowerSystem).GetProperty("PowerOn").GetSetMethod(true).Invoke(null, new object[] { on });

    [SetUp]
    public void EmptyScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Power(true);
    }

    [TearDown]
    public void PowerBack() => Power(true);

    static GameObject Box(string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size;
        Physics.SyncTransforms();
        return go;
    }

    // A 2 m room door hinged at the origin, lying along +x when shut, as the store's do: it
    // listens for E itself unless told otherwise (aimedInteraction off, as Door_03..06 were).
    static HingeDoor RoomDoor(bool wrapped)
    {
        Box("floor", new Vector3(0f, -.1f, 0f), new Vector3(20f, .2f, 20f));
        var hinge = new GameObject("Door_03");
        var panel = Box("Finished door leaf", new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, .1f));
        panel.transform.SetParent(hinge.transform, true);
        var door = hinge.AddComponent<HingeDoor>();
        door.aimedInteraction = false;
        if (wrapped)
        {
            var use = hinge.AddComponent<FurnitureDoor>();
            use.roomDoor = door;
            use.panel = panel.GetComponent<BoxCollider>();
            use.label = "door";
        }
        Call(door, "Start");
        return door;
    }

    static void Swing(HingeDoor door, float seconds)
    {
        for (float t = 0f; t < seconds; t += 1f / 60f) door.StepSwing(1f / 60f);
    }

    static float PanelZ(HingeDoor door) => door.Panel.transform.position.z;

    // ---- item 11: the cold room's doors ------------------------------------------------------

    [Test]
    public void AWrappedRoomDoor_NeverListensForEItself()
    {
        Assert.IsFalse(RoomDoor(true).ListensForE, "its FurnitureDoor is the only way E reaches it");
        Assert.IsTrue(RoomDoor(false).ListensForE, "an unwrapped, unaimed door still has the nearby-E shortcut");
    }

    // On 0.5.0 this left the door shut: the wrapper's Use started the swing and the second Use,
    // the same press arriving again in the same frame, turned it round.
    [Test]
    public void OnePressReachingTheDoorTwiceInAFrame_LeavesItOpening()
    {
        HingeDoor door = RoomDoor(true);
        var you = new GameObject("employee");
        you.transform.position = new Vector3(1f, 0f, -1.5f);
        var interact = you.AddComponent<PlayerInteract>();

        door.GetComponent<FurnitureDoor>().Interact(interact);       // the aimed E
        door.Use(you.transform.position, NoiseAuthor.Player);         // the same press, by the old nearby-E path
        Assert.IsTrue(door.IsOpen && door.IsMoving, "still opening");
        Swing(door, 4f);
        Assert.IsTrue(door.IsOpen && !door.IsMoving, "and it opens all the way");
        Assert.Greater(Quaternion.Angle(Quaternion.identity, door.transform.rotation), 85f);
    }

    [Test]
    public void ARoomDoor_OpensAwayFromTheOpener_FromEitherSide()
    {
        HingeDoor door = RoomDoor(true);
        Assert.IsTrue(door.Use(new Vector3(1f, 0f, -1.5f), NoiseAuthor.Player));
        Swing(door, 4f);
        Assert.Greater(PanelZ(door), .5f, "opened from the -z side, it swings to +z");
        door.Use(new Vector3(1f, 0f, -1.5f), NoiseAuthor.Player);
        Swing(door, 4f);
        Assert.IsFalse(door.IsOpen);

        Assert.IsTrue(door.Use(new Vector3(1f, 0f, 1.5f), NoiseAuthor.Player));
        Swing(door, 4f);
        Assert.Less(PanelZ(door), -.5f, "opened from the +z side, it swings to -z");
    }

    [Test]
    public void ARoomDoor_OpensTheOtherWay_WhenTheFarSideIsAWall_ButItsOwnJambsNeverDecide()
    {
        HingeDoor door = RoomDoor(true);
        // Jambs touching the shut leaf at both ends: they touch it at every angle near the hinge.
        Box("Jamb_01", new Vector3(-.05f, 1f, 0f), new Vector3(.1f, 2.2f, .3f));
        Box("White_Jamb_01", new Vector3(2.05f, 1f, 0f), new Vector3(.1f, 2.2f, .3f));
        // Opened from -z this door would swing to +z, into this.
        Box("wall", new Vector3(0f, 1f, 1.2f), new Vector3(.5f, 2f, 1.2f));

        door.Use(new Vector3(1f, 0f, -1.5f), NoiseAuthor.Player);
        Swing(door, 4f);
        Assert.Less(PanelZ(door), -.5f, "the clear way, towards the opener");
        door.Use(new Vector3(1f, 0f, -1.5f), NoiseAuthor.Player);
        Swing(door, 4f);

        door.Use(new Vector3(1f, 0f, 1.5f), NoiseAuthor.Player);
        Swing(door, 4f);
        Assert.Less(PanelZ(door), -.5f, "from the wall's side the away way is clear: jambs don't block it");
    }

    [Test]
    public void ARoomDoor_WithBothWaysBlocked_StillOpens_AwayFromTheOpener()
    {
        HingeDoor door = RoomDoor(true);
        Box("wall +z", new Vector3(0f, 1f, 1.2f), new Vector3(.5f, 2f, 1.2f));
        Box("wall -z", new Vector3(0f, 1f, -1.2f), new Vector3(.5f, 2f, 1.2f));
        door.Use(new Vector3(1f, 0f, -1.5f), NoiseAuthor.Player);
        Swing(door, 4f);
        Assert.IsTrue(door.IsOpen && !door.IsMoving, "the check picks a side; it never refuses to open");
        Assert.Greater(PanelZ(door), .5f);
    }

    [Test]
    public void ERepeatedlyMidSwing_TurnsARoomDoorRound_EachTime_WithoutSnapping()
    {
        HingeDoor door = RoomDoor(true);
        Vector3 at = new Vector3(1f, 0f, -1.5f);
        door.Use(at, NoiseAuthor.Player);
        Swing(door, .2f);
        for (int i = 0; i < 4; i++)
        {
            Quaternion before = door.transform.rotation;
            Assert.IsTrue(door.Use(at, NoiseAuthor.Player));
            Assert.AreEqual(i % 2 == 1, door.IsOpen, "each E turns it round");
            Assert.Less(Quaternion.Angle(before, door.transform.rotation), .01f, "no snap on the turn");
            Swing(door, .1f);
        }
        Swing(door, 4f);                                // the fourth E left it opening
        Assert.IsTrue(door.IsOpen && !door.IsMoving);
        Assert.Greater(PanelZ(door), .5f, "still the side it first opened to");
        Assert.Greater(Quaternion.Angle(Quaternion.identity, door.transform.rotation), 85f);
    }

    // ---- item 11: furniture leaves --------------------------------------------------------------

    static FurnitureDoor Leaf(Transform cabinet, out Transform hinge)
    {
        var h = new GameObject("leaf hinge");
        if (cabinet != null) h.transform.SetParent(cabinet, false);
        var panel = Box("leaf", new Vector3(.5f, 1f, 0f), new Vector3(1f, 2f, .05f));
        panel.transform.SetParent(h.transform, true);
        var door = h.AddComponent<FurnitureDoor>();
        door.panel = panel.GetComponent<BoxCollider>();
        Call(door, "Awake");
        hinge = h.transform;
        return door;
    }

    static float Yaw(Transform t) => Mathf.DeltaAngle(0f, t.localEulerAngles.y);

    [Test]
    public void AFurnitureLeaf_ReversesMidSwing_BothWays_FromWhereItIs()
    {
        Box("floor", new Vector3(0f, -.1f, 0f), new Vector3(10f, .2f, 10f));
        FurnitureDoor door = Leaf(null, out Transform hinge);
        door.UseFrom(Vector3.back);
        door.StepSwing(.3f);
        float opening = Yaw(hinge);
        Assert.Greater(opening, 30f, "outward: the authored +105");

        door.UseFrom(Vector3.back);                     // E while opening: closing
        Assert.IsFalse(door.IsOpen);
        door.StepSwing(.1f);
        float closing = Yaw(hinge);
        Assert.Less(closing, opening);
        Assert.Greater(closing, 0f, "part-way back, no snap shut");

        door.UseFrom(Vector3.forward);                  // E while closing: opening, same side
        Assert.IsTrue(door.IsOpen);
        door.StepSwing(.1f);
        Assert.Greater(Yaw(hinge), closing);
        door.StepSwing(2f);
        Assert.IsTrue(door.IsFullyOpen);
        Assert.AreEqual(door.openAngle, Yaw(hinge), .5f);
    }

    // A cabinet leaf whose inward side is its own cabinet has no two-way clearance: with the
    // outward side obstructed it still opens outward, as far as the obstruction lets it.
    [Test]
    public void ACabinetLeaf_NeverSwingsIntoItsCabinet_EvenWhenOutwardIsObstructed()
    {
        Box("floor", new Vector3(0f, -.1f, 0f), new Vector3(10f, .2f, 10f));
        var cabinet = new GameObject("cabinet").transform;
        Box("cabinet body", new Vector3(.5f, 1f, .4f), new Vector3(1f, 2f, .7f)).transform.SetParent(cabinet, true);
        FurnitureDoor door = Leaf(cabinet, out Transform hinge);
        Box("crate in front", new Vector3(-.1f, 1f, -.6f), new Vector3(.6f, 2f, .4f));

        door.UseFrom(new Vector3(.5f, 0f, -2f));
        door.StepSwing(2f);
        float yaw = Yaw(hinge);
        Assert.Greater(yaw, 5f, "outward, the only side it has");
        Assert.Less(yaw, door.openAngle - 1f, "stopped by the crate");
    }

    // ---- item 6: the automatic doors in a blackout ----------------------------------------------

    static AutoDoubleDoor AutoDoor(bool solidPanels)
    {
        var go = new GameObject("auto door");
        go.AddComponent<BoxCollider>();
        foreach (string name in new[] { "LeftDoor", "RightDoor" })
        {
            var leaf = new GameObject(name);
            leaf.transform.SetParent(go.transform, false);
            leaf.transform.localPosition = new Vector3(name == "LeftDoor" ? -.75f : .75f, 1.1f, 0f);
            if (solidPanels) leaf.AddComponent<BoxCollider>().size = new Vector3(1.5f, 2.2f, .02f);
        }
        var door = go.AddComponent<AutoDoubleDoor>();
        Call(door, "OnEnable");
        Call(door, "Start");
        return door;
    }

    [Test]
    public void InABlackout_NobodyPowersTheDoors_NotKarenNotAPhantomNotASensor()
    {
        AutoDoubleDoor door = AutoDoor(false);
        var shopper = new GameObject("shopper");
        shopper.transform.position = new Vector3(0f, 0f, 30f);   // out of the sensor when the power returns
        shopper.AddComponent<CharacterController>();
        Physics.SyncTransforms();
        int autoNoises = 0;
        System.Action<NoiseEvent> listen = n => { if (n.Kind == NoiseKind.AutoDoor) autoNoises++; };
        NoiseBus.Emitted += listen;
        try
        {
            Power(false);
            door.StepMotion(.1f);
            Assert.IsFalse(door.OpenForKaren(), "Karen");
            Assert.IsFalse(door.PhantomCycle(), "the phantom cycle");
            door.GetType().GetMethod("OnTriggerEnter", Private).Invoke(door, new object[] { shopper.GetComponent<CharacterController>() });
            door.StepMotion(1f);
            Assert.IsFalse(door.IsOpen, "the sensor");
            Assert.AreEqual(0f, door.Openness);
            Assert.AreEqual(0, autoNoises, "no chime noise either");
            Assert.IsNull(AutoDoubleDoor.Ahead(door.transform.position + Vector3.back * 3f, Vector3.forward * 7.5f),
                "nothing to ask or wait at: Karen doesn't stand at dead glass");

            Power(true);
            door.StepMotion(.1f);
            Assert.IsTrue(door.PhantomCycle(), "power back: the phantom cycle runs again");
            Assert.AreSame(door, AutoDoubleDoor.Ahead(door.transform.position + Vector3.back * 3f, Vector3.forward * 7.5f));
        }
        finally
        {
            NoiseBus.Emitted -= listen;
            Object.DestroyImmediate(shopper);
            Object.DestroyImmediate(door.gameObject);
        }
    }

    [Test]
    public void ADeadDoor_SlidesOpenByHand_Silently_AndPowerTakesItBack()
    {
        AutoDoubleDoor door = AutoDoor(true);
        int autoNoises = 0, doorNoises = 0;
        System.Action<NoiseEvent> listen = n =>
        {
            if (n.Kind == NoiseKind.AutoDoor) autoNoises++;
            if (n.Kind == NoiseKind.Door) doorNoises++;
        };
        NoiseBus.Emitted += listen;
        try
        {
            Assert.AreEqual(2, door.Handles.Count, "one hand-push target per panel");
            foreach (var handle in door.Handles) Assert.IsFalse(handle.gameObject.activeSelf, "nothing to push while powered");
            Assert.IsFalse(door.PushByHand(), "powered doors open by their sensor");

            Power(false);
            door.StepMotion(.1f);
            foreach (var handle in door.Handles)
            {
                Assert.IsTrue(handle.gameObject.activeSelf, "dead and shut: E can push it");
                Assert.IsTrue(handle.GetComponent<BoxCollider>().isTrigger, "a trigger: her sight and the NavMesh ignore it");
            }
            foreach (var obstacle in door.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>())
                Assert.IsTrue(obstacle.enabled, "dead panels are carved out while the power is off");

            door.Handles[0].Interact(null);
            Assert.IsTrue(door.HandOpened);
            Assert.IsFalse(door.PushByHand(), "once is enough");
            for (int i = 0; i < 300; i++) door.StepMotion(1f / 60f);
            Assert.AreEqual(1f, door.Openness, .01f, "slid all the way by hand");
            Assert.IsFalse(door.IsOpen, "no power was used: the door never opened itself");
            Assert.AreEqual(0, autoNoises, "no chime");
            Assert.AreEqual(1, doorNoises, "but glass in its runners is heard");
            foreach (var handle in door.Handles) Assert.IsFalse(handle.gameObject.activeSelf);

            Power(true);
            door.StepMotion(.1f);
            Assert.IsFalse(door.HandOpened);
            Assert.IsTrue(door.IsOpen, "part-open panels close in the usual way, after the delay");
            foreach (var obstacle in door.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>())
                Assert.IsFalse(obstacle.enabled, "no carving left behind on restoration");
            foreach (var handle in door.Handles) Assert.IsFalse(handle.gameObject.activeSelf);
        }
        finally
        {
            NoiseBus.Emitted -= listen;
            Object.DestroyImmediate(door.gameObject);
        }
    }

    [Test]
    public void ADoorStoppedMidSlide_ResumesNormallyWhenThePowerReturns()
    {
        AutoDoubleDoor door = AutoDoor(false);
        try
        {
            door.OpenForKaren();
            door.StepMotion(.2f);
            float stopped = door.Openness;
            Assert.Greater(stopped, 0f);
            Assert.Less(stopped, 1f);
            Power(false);
            door.StepMotion(1f);
            Assert.AreEqual(stopped, door.Openness, 1e-5f, "dead panels stop where they are");
            Power(true);
            door.StepMotion(.1f);
            Assert.IsTrue(door.IsOpen, "nobody pulled it shut on the way back");
            Assert.Greater(door.Openness, stopped, "and the motor runs again");
        }
        finally { Object.DestroyImmediate(door.gameObject); }
    }
}
