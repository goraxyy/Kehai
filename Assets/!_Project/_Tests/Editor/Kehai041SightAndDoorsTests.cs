using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kehai 0.4.1: her eyes judge cover eye to eye (item 3), E turns a swinging door round (item
// 7), and the automatic doors open for her (item 2). Built from boxes in an empty scene.
public class Kehai041SightAndDoorsTests
{
    const float HerEyes = 1.85f;                       // the top of her body, less 15 cm (KarenBody.BuildLook)
    SightSensor eye;

    [SetUp]
    public void EmptyScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("her eye");
        go.transform.position = new Vector3(0f, HerEyes, 0f);
        go.transform.rotation = Quaternion.LookRotation(Vector3.forward);
        eye = go.AddComponent<SightSensor>();
        KarenBody.ConfigureEye(eye, new KarenConfig());
    }

    static GameObject Box(string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size;
        Physics.SyncTransforms();
        return go;
    }

    // You, standing or crouched at `z` metres in front of her: eyes, chest, feet (PlayerPresence).
    static (Vector3 eyes, Vector3 chest, Vector3 feet) You(float z, bool crouched) =>
        (new Vector3(0f, crouched ? 0.9f : 1.6f, z), new Vector3(0f, crouched ? 0.55f : 1.2f, z), new Vector3(0f, 0.1f, z));

    // ---- item 3.3: a low thing on the floor between you doesn't hide you ---------------------

    [Test]
    public void AShortShelfBetweenYou_DoesNotHideYou_WhileSheCanSeeYourEyes()
    {
        var you = You(6f, false);
        Assert.AreEqual(1f, eye.ExposureTo(you.eyes, you.chest, you.feet), 1e-5f, "in the open");

        // A 1.4 m shelf a metre in front of you: it covers your feet and chest, not your eyes.
        Box("short shelf", new Vector3(0f, 0.7f, 5f), new Vector3(3f, 1.4f, 0.5f));
        float exposure = eye.ExposureTo(you.eyes, you.chest, you.feet);
        Assert.AreEqual(SightSensor.ExposureOf(true, false, false), exposure, 1e-5f, "her line to your eyes clears the shelf");
        Assert.Greater(eye.Detect(0f, 6f, 0.9f, 0.55f, exposure), eye.scoreFloor, "and she sees you over it");
    }

    [Test]
    public void ABoxOnTheFloor_BetweenYou_ChangesNothingButYourFeet()
    {
        var you = You(6f, false);
        Box("box", new Vector3(0f, 0.2f, 5.6f), new Vector3(0.6f, 0.4f, 0.4f));
        Assert.AreEqual(SightSensor.ExposureOf(true, true, false), eye.ExposureTo(you.eyes, you.chest, you.feet), 1e-5f);
    }

    // ---- item 3.4: no line to your eyes, not seen, however clear the floor ------------------

    [Test]
    public void UnderARack_WithTheBoardsBetweenHerEyesAndYours_SheDoesNotSeeYou()
    {
        // Crouched 4 m away under a rack whose lowest board is at 1.2 m and reaches 2 m out
        // towards her. The floor between you is clear: her line to your feet runs under the board.
        var you = You(4f, true);
        Box("rack board", new Vector3(0f, 1.25f, 3.5f), new Vector3(3f, 0.1f, 3f));
        Assert.IsTrue(eye.CanSee(you.feet), "the floor between you is clear");
        Assert.AreEqual(0f, eye.ExposureTo(you.eyes, you.chest, you.feet), "but the board is between her eyes and yours");
    }

    [Test]
    public void UnderARack_SheDoesSeeYou_OnceHerEyesAreLowEnoughToFindYours()
    {
        // The same rack from far enough off that her line to your eyes passes under its edge.
        var you = You(12f, true);
        Box("rack board", new Vector3(0f, 1.25f, 11.5f), new Vector3(3f, 0.1f, 3f));
        Assert.Greater(eye.ExposureTo(you.eyes, you.chest, you.feet), 0f, "an eye-to-eye line under the board is a line");
    }

    [Test]
    public void AWall_HidesAllOfYou()
    {
        var you = You(6f, false);
        Box("wall", new Vector3(0f, 1.5f, 3f), new Vector3(8f, 3f, 0.2f));
        Assert.AreEqual(0f, eye.ExposureTo(you.eyes, you.chest, you.feet));
    }

    // ---- item 7: E on a swinging door turns it round --------------------------------------------

    static HingeDoor Door()
    {
        var hinge = new GameObject("hinge");
        var panel = Box("panel", new Vector3(1f, 1f, 0f), new Vector3(2f, 2f, 0.1f));
        panel.transform.SetParent(hinge.transform, true);
        var door = hinge.AddComponent<HingeDoor>();
        door.aimedInteraction = true;
        typeof(HingeDoor).GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(door, null);
        return door;
    }

    static void Swing(HingeDoor door, float seconds)
    {
        for (float t = 0f; t < seconds; t += 1f / 60f) door.StepSwing(1f / 60f);
    }

    [Test]
    public void EOnAnOpeningDoor_ShutsItAgain()
    {
        HingeDoor door = Door();
        Quaternion shut = door.transform.rotation;
        Assert.IsTrue(door.Use(Vector3.back, NoiseAuthor.Player));
        Swing(door, 0.2f);
        Assert.IsTrue(door.IsOpen && door.IsMoving, "it should be part-way open");
        Assert.Greater(Quaternion.Angle(shut, door.transform.rotation), 5f);

        Assert.IsTrue(door.Use(Vector3.back, NoiseAuthor.Player), "E mid-swing should be taken");
        Assert.IsFalse(door.IsOpen, "it's closing now");
        Swing(door, 4f);
        Assert.IsFalse(door.IsMoving);
        Assert.Less(Quaternion.Angle(shut, door.transform.rotation), 0.2f, "back where it started");
    }

    [Test]
    public void EOnAClosingDoor_OpensItAgain_ToTheSameSide()
    {
        HingeDoor door = Door();
        door.Use(Vector3.back, NoiseAuthor.Player);
        Swing(door, 4f);
        Quaternion open = door.transform.rotation;
        Assert.IsTrue(door.IsOpen && !door.IsMoving);

        door.Use(Vector3.back, NoiseAuthor.Player);          // close it…
        Swing(door, 0.2f);
        Assert.IsTrue(door.Use(Vector3.forward, NoiseAuthor.Player), "…and E again, from the other side");
        Assert.IsTrue(door.IsOpen);
        Swing(door, 4f);
        Assert.Less(Quaternion.Angle(open, door.transform.rotation), 0.2f, "open the way it was, not swung through");
    }

    [Test]
    public void OnlyThePlayer_TurnsASwingingDoorRound()
    {
        HingeDoor door = Door();
        door.Use(Vector3.back, NoiseAuthor.Player);
        Swing(door, 0.2f);
        Assert.IsFalse(door.Use(Vector3.back, NoiseAuthor.Karen), "she waits for a door to finish, as before");
        Assert.IsFalse(door.Use(Vector3.back, NoiseAuthor.Customer));
        Assert.IsTrue(door.IsOpen);
    }

    // ---- item 2: the automatic doors and Karen -----------------------------------------------------

    static AutoDoubleDoor AutoDoor(Vector3 at)
    {
        var go = new GameObject("auto door");
        go.transform.position = at;
        go.AddComponent<BoxCollider>();
        new GameObject("LeftDoor").transform.SetParent(go.transform, false);
        new GameObject("RightDoor").transform.SetParent(go.transform, false);
        var door = go.AddComponent<AutoDoubleDoor>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(AutoDoubleDoor).GetMethod("OnEnable", flags).Invoke(door, null);
        typeof(AutoDoubleDoor).GetMethod("Start", flags).Invoke(door, null);
        return door;
    }

    [Test]
    public void TheAutomaticDoors_OpenForKaren_AndTheNoiseIsHers()
    {
        AutoDoubleDoor door = AutoDoor(new Vector3(0f, 0f, 10f));
        try
        {
            NoiseEvent heard = default;
            int count = 0;
            System.Action<NoiseEvent> listen = n => { heard = n; count++; };
            NoiseBus.Emitted += listen;
            try
            {
                Assert.IsFalse(door.IsOpen);
                Assert.IsTrue(door.OpenForKaren(), "shut doors should open for her");
                Assert.IsTrue(door.IsOpen);
                Assert.AreEqual(1, count);
                Assert.AreEqual(NoiseKind.AutoDoor, heard.Kind);
                Assert.AreEqual(NoiseAuthor.Karen, heard.Author, "her own door mustn't read as someone else's");
                Assert.IsFalse(door.OpenForKaren(), "already open: nothing new");
                Assert.AreEqual(1, count, "and no second chime");
            }
            finally { NoiseBus.Emitted -= listen; }
        }
        finally { Object.DestroyImmediate(door.gameObject); }
    }

    [Test]
    public void TheDoorsOpenFromFurtherAhead_TheFasterSheComes()
    {
        AutoDoubleDoor door = AutoDoor(new Vector3(0f, 0f, 10f));
        try
        {
            float walk = door.ReachFor(3.4f), run = door.ReachFor(7.5f);
            Assert.AreEqual(AutoDoubleDoor.SensorReach, door.ReachFor(0.5f), 1e-4f, "never less than the sensor box");
            Assert.Greater(run, walk);
            // The panels take slideDistance / slideSpeed seconds: she must not arrive sooner.
            Assert.GreaterOrEqual(run / 7.5f, door.slideDistance / door.slideSpeed, "at a run the panels wouldn't be open in time");

            Assert.AreSame(door, AutoDoubleDoor.Ahead(new Vector3(0f, 0f, 10f - run + 0.2f), Vector3.forward * 7.5f), "running at it");
            Assert.IsNull(AutoDoubleDoor.Ahead(new Vector3(0f, 0f, 10f - run + 0.2f), Vector3.forward * 3.4f), "walking, the same spot is still too far");
            Assert.IsNull(AutoDoubleDoor.Ahead(new Vector3(0f, 0f, 8f), Vector3.back * 7.5f), "running away from it");
            Assert.IsNull(AutoDoubleDoor.Ahead(new Vector3(0f, 0f, 8f), Vector3.zero), "standing still");
        }
        finally { Object.DestroyImmediate(door.gameObject); }
    }

    static void Power(bool on) => typeof(PowerSystem).GetProperty("PowerOn")
        .GetSetMethod(true).Invoke(null, new object[] { on });

    [Test]
    public void BlackoutFreezesMovingPanelsAndRejectsEveryPoweredRequest()
    {
        AutoDoubleDoor door = AutoDoor(Vector3.zero);
        try
        {
            Power(true);
            door.OpenForKaren();
            door.StepMotion(.2f);
            Vector3 stopped = door.LeftPanel.position;
            Power(false);
            door.StepMotion(1f);
            Assert.IsFalse(door.OpenForKaren());
            door.PhantomCycle();
            door.StepMotion(1f);
            Assert.AreEqual(stopped, door.LeftPanel.position);
        }
        finally { Power(true); Object.DestroyImmediate(door.gameObject); }
    }

    [Test]
    public void PowerRestorationFindsAnOccupantAlreadyInTheSensor()
    {
        AutoDoubleDoor door = AutoDoor(Vector3.zero);
        var person = new GameObject("employee");
        person.AddComponent<CharacterController>();
        try
        {
            Power(false);
            door.StepMotion(.1f);
            door.PhantomCycle();
            Assert.IsFalse(door.IsOpen);
            typeof(AutoDoubleDoor).GetMethod("OnTriggerEnter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(door, new object[] { person.GetComponent<CharacterController>() });
            Assert.IsFalse(door.IsOpen);
            Physics.SyncTransforms();
            Power(true);
            door.StepMotion(.1f);
            Assert.IsTrue(door.IsOpen);
            Assert.Greater(door.Openness, 0f);
        }
        finally { Power(true); Object.DestroyImmediate(person); Object.DestroyImmediate(door.gameObject); }
    }

    // 0.5.1 item 11. A furniture leaf opens outward, its authored side, from either approach;
    // the other way only where the outward pose is blocked and the inward one is clear. (The
    // first 0.5.1 draft of this test wanted "away from the opener", which swings a cabinet leaf
    // into its own cabinet, and it never ran Awake, so the leaf had no shut pose: its overlap box
    // sat in the floor at the hinge and it never moved.)
    [Test]
    public void FurnitureDoorFlushWithFloorOpensBothWaysAndReverses()
    {
        Box("floor", new Vector3(0f, -.1f, 0f), new Vector3(10f, .2f, 10f));
        var hinge = new GameObject("furniture hinge");
        var panel = Box("leaf", new Vector3(.5f, 1f, 0f), new Vector3(1f, 2f, .05f));
        panel.transform.SetParent(hinge.transform, true);
        var door = hinge.AddComponent<FurnitureDoor>();
        door.panel = panel.GetComponent<BoxCollider>();
        typeof(FurnitureDoor).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(door, null);
        float Angle() => Mathf.DeltaAngle(0f, hinge.transform.eulerAngles.y);

        door.UseFrom(Vector3.back);
        door.StepSwing(.2f);
        float first = Angle();
        Assert.Greater(Mathf.Abs(first), 5f, "floor contact must not freeze the leaf");
        door.UseFrom(Vector3.forward);
        door.StepSwing(1f);
        Assert.Less(Quaternion.Angle(Quaternion.identity, hinge.transform.rotation), .1f);
        door.UseFrom(Vector3.forward);
        door.StepSwing(.2f);
        Assert.Greater(first * Angle(), 0f, "outward from either side, never into its cabinet");
        door.UseFrom(Vector3.forward);
        door.StepSwing(1f);

        // A wall where the leaf would open outward (the authored +105 swings it to -z).
        Box("wall on the outward side", new Vector3(-.1f, 1f, -.6f), new Vector3(.6f, 2f, .4f));
        door.UseFrom(Vector3.back);
        door.StepSwing(.2f);
        float other = Angle();
        Assert.Less(first * other, 0f, "the other way, where outward is blocked and inward is clear");

        door.UseFrom(Vector3.back);
        door.StepSwing(.05f);
        float turned = Angle();
        Assert.Less(Mathf.Abs(turned), Mathf.Abs(other), "E while opening closes it");
        door.UseFrom(Vector3.back);
        Assert.IsTrue(door.IsOpen, "closing reverses immediately");
        door.StepSwing(.05f);
        Assert.Greater(Mathf.Abs(Angle()), Mathf.Abs(turned), "and opens again from where it was");
        Assert.Greater(other * Angle(), 0f, "to the same side");
    }
}
