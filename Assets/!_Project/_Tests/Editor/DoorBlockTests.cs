using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Karen 0.2.0, phase 2: Block door (B2.1), written by the second builder. A real restock box
// holds a door shut until someone picks it up, recalls it home, moves it or the shift resets.

public class DoorBlockTests
{
    HingeDoor door;
    ProductBox stock;
    Item item;
    ItemHome home;
    PlayerInteract player;
    readonly List<GameObject> objects = new List<GameObject>();
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Call(object value, string name) => value.GetType().GetMethod(name, Private).Invoke(value, null);
    GameObject New(string name) { var go = new GameObject(name); objects.Add(go); return go; }
    GameObject Solid(string name, Vector3 at, Vector3 size)
    {
        var go = New(name); go.transform.position = at; go.AddComponent<BoxCollider>().size = size; return go;
    }
    [SetUp] public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Solid("floor", new Vector3(0,-.1f,0), new Vector3(40,.2f,40));
        var hinge = New("hinge");
        var panel = Solid("panel", new Vector3(1,1,0), new Vector3(2,2,.1f));
        panel.transform.SetParent(hinge.transform, true);
        door = hinge.AddComponent<HingeDoor>(); door.aimedInteraction = true; Call(door,"Start");
        var source = Solid("restock box", new Vector3(8,.425f,0), new Vector3(1.8f,.85f,.9f));
        source.AddComponent<Rigidbody>().isKinematic = true;
        item = source.AddComponent<Item>(); Call(item,"Awake");
        home = source.AddComponent<ItemHome>(); Call(home,"Awake");
        var snap = New("stockroom home"); snap.transform.position = source.transform.position;
        home.snapPoint = snap.transform;
        stock = source.AddComponent<ProductBox>(); stock.productId = "canned_tuna"; stock.home = source.transform.position;
        var pickup = source.AddComponent<PickupInteractable>(); Call(pickup,"Awake");
        var p = New("test player"); player = p.AddComponent<PlayerInteract>();
        player.carrySlot = p.AddComponent<CarrySlot>();
        player.carrySlot.holdPoint = New("hand").transform; player.carrySlot.holdPoint.position = Vector3.up * 2;
        player.carrySlot.stashPoint = New("stash").transform;
        Physics.SyncTransforms();
    }
    [TearDown] public void TearDown()
    {
        DoorBlock.ClearAll();
        for (int i=objects.Count-1;i>=0;i--) if(objects[i]!=null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }
    DoorBlock Block()
    {
        var block=DoorBlock.Spawn(door,stock,12);
        Assert.IsNotNull(block,"floor beside the door should fit the existing box");
        Assert.IsTrue(door.Locked); Assert.IsTrue(block.IsBlocking);
        return block;
    }
    [Test] public void BoxBlocksDoorAndCarvesPanel()
    {
        var block=Block();
        // Supply a muted editor-owned voice: the runtime pool uses DontDestroyOnLoad,
        // which is intentionally unavailable in EditMode.
        var voice = New("muted test voice").AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.mute = true;
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var pool = typeof(OneShotAudio).GetField("pool",flags);
        var next = typeof(OneShotAudio).GetField("next",flags);
        object previousPool = pool.GetValue(null), previousNext = next.GetValue(null);
        try
        {
            pool.SetValue(null,new[]{voice}); next.SetValue(null,0);
            Assert.IsFalse(door.Use(Vector3.back,Kehai.Karen.NoiseAuthor.Player));
        }
        finally { pool.SetValue(null,previousPool); next.SetValue(null,previousNext); }
        Assert.IsFalse(door.IsOpen);
        Assert.IsTrue(door.Panel.GetComponent<NavMeshObstacle>().enabled);
        Assert.AreSame(item,block.Box);
        Assert.AreSame(home.snapPoint, item.GetComponent<ItemHome>().snapPoint);
        Assert.AreEqual(DoorBlock.PickUpPrompt,block.GetPrompt());
    }
    [Test] public void PickupImmediatelyReleasesDoorAndRetainsBoxInHand()
    {
        var block=Block(); block.Interact(player);
        Assert.IsFalse(door.Locked); Assert.IsNull(door.Blocker);
        Assert.IsFalse(door.Panel.GetComponent<NavMeshObstacle>().enabled);
        Assert.AreSame(item,player.carrySlot.currentItem); Assert.IsTrue(item.isCarried);
    }
    [Test] public void PickupIntoStowedSlotImmediatelyReleasesDoor()
    {
        player.carrySlot.items[0]=New("occupied slot").AddComponent<Item>();
        var block=Block(); block.Interact(player);
        Assert.IsFalse(door.Locked); Assert.AreSame(item,player.carrySlot.items[1]);
        Assert.IsFalse(item.gameObject.activeSelf); Assert.IsFalse(item.isCarried);
    }
    [Test] public void FullInventoryLeavesDoorBlocked()
    {
        for(int i=0;i<4;i++) player.carrySlot.items[i]=New("occupied slot").AddComponent<Item>();
        var block=Block(); block.Interact(player);
        Assert.IsTrue(door.Locked); Assert.IsTrue(block.IsBlocking);
    }
    [Test] public void RecallReleasesDoorWithoutChangingOriginalHome()
    {
        var block=Block(); home.ReturnHome(); block.Refresh();
        Assert.IsFalse(door.Locked); Assert.IsTrue(stock.AtHome);
    }
    [Test] public void ClearAllReturnsUntouchedBoxesAndRemovesRegions()
    {
        Block(); var regions=new HashSet<int>(); DoorBlock.AddClosedRegions(regions);
        CollectionAssert.AreEquivalent(new[]{12},regions);
        DoorBlock.ClearAll(); Assert.IsFalse(door.Locked); Assert.IsTrue(stock.AtHome);
        regions.Clear(); DoorBlock.AddClosedRegions(regions); Assert.IsEmpty(regions);
    }
    [Test] public void ClearedBoxCanBeUsedAgain()
    {
        Block(); DoorBlock.ClearAll(); Physics.SyncTransforms();
        Assert.IsNotNull(DoorBlock.Spawn(door,stock,12));
    }
    [Test] public void RemovedBoxDoesNotLeaveStaleRegion()
    {
        Block().Interact(player);
        var regions=new HashSet<int>(); DoorBlock.AddClosedRegions(regions); Assert.IsEmpty(regions);
    }
    [Test] public void DestroyedBoxReleasesDoor()
    {
        var block=Block(); Object.DestroyImmediate(item.gameObject); block.Refresh(); Assert.IsFalse(door.Locked);
    }
    [Test] public void MovedBoxReleasesDoorWithoutRecallingIt()
    {
        var block=Block(); Vector3 moved=item.transform.position+Vector3.right*3;
        item.transform.position=moved; block.Refresh();
        Assert.IsFalse(door.Locked); Assert.AreEqual(moved,item.transform.position);
    }
    [Test] public void SeparateLegacyLockSurvivesPickup()
    {
        var block=Block(); door.SetLocked(true); block.Interact(player);
        Assert.IsTrue(door.Locked); Assert.IsNull(door.Blocker);
        door.SetLocked(false); Assert.IsFalse(door.Locked);
    }
    [Test] public void OpenAndMovingDoorsAreRejected()
    {
        typeof(HingeDoor).GetField("isOpen",Private).SetValue(door,true);
        Assert.IsNull(DoorBlock.Spawn(door,stock,12));
        typeof(HingeDoor).GetField("isOpen",Private).SetValue(door,false);
        typeof(HingeDoor).GetField("isMoving",Private).SetValue(door,true);
        Assert.IsNull(DoorBlock.Spawn(door,stock,12)); Assert.IsTrue(stock.AtHome);
    }
    [Test] public void BoxTakenBetweenSelectionAndPlacementIsRejected()
    {
        Assert.IsTrue(DoorBlock.TryPlacement(door,stock,out _,out _));
        Assert.IsTrue(player.carrySlot.TryPickup(item));
        Assert.IsNull(DoorBlock.Spawn(door,stock,12)); Assert.IsFalse(door.Locked);
    }
    [Test] public void OccupiedFloorIsRejectedWithoutMovingBox()
    {
        Solid("blocked side 1",new Vector3(1,.5f,1),new Vector3(3,1,1.9f));
        Solid("blocked side 2",new Vector3(1,.5f,-1),new Vector3(3,1,1.9f));
        Physics.SyncTransforms(); Assert.IsNull(DoorBlock.Spawn(door,stock,12)); Assert.IsTrue(stock.AtHome);
    }
    [Test] public void RotatedDoorPlacesBoxAgainstPanel()
    {
        // Start captured the centre in its initial orientation; rotate a newly authored hinge before Start.
        Object.DestroyImmediate(door.Panel.GetComponent<NavMeshObstacle>());
        door.transform.rotation=Quaternion.Euler(0,45,0); Physics.SyncTransforms(); Call(door,"Start");
        var block=Block();
        Vector3 normal=Vector3.Cross((door.DoorCentre-door.transform.position).normalized,Vector3.up).normalized;
        Assert.That(Mathf.Abs(Vector3.Dot(item.transform.position-door.DoorCentre,normal)),Is.EqualTo(.54f).Within(.01f));
    }
    [Test] public void SlotAndBoxHighlightTogether()
    {
        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(mesh);
        Object.DestroyImmediate(mesh.GetComponent<Collider>());
        mesh.transform.SetParent(stock.transform,false);
        mesh.transform.localScale=new Vector3(1.8f,.85f,.9f);
        var material = new Material(Shader.Find("Kehai/CustomerOutline"));
        var outline=stock.gameObject.AddComponent<OutlineHighlight>();
        outline.outlineMaterial=material; outline.shape=OutlineHighlight.OutlineShape.BoundingBox;
        var block=Block();
        try
        {
            block.OnHoverEnter();
            var boxShell=stock.transform.Find("__Outline");
            var slotShell=block.transform.Find("__Outline");
            Assert.IsNotNull(boxShell); Assert.IsNotNull(slotShell);
            Assert.IsTrue(boxShell.gameObject.activeSelf); Assert.IsTrue(slotShell.gameObject.activeSelf);
            block.OnHoverExit();
            Assert.IsFalse(boxShell.gameObject.activeSelf); Assert.IsFalse(slotShell.gameObject.activeSelf);
        }
        finally { Object.DestroyImmediate(material); }
    }

    // [C]'s side of the integration: her plans see the closure, and the door says why it won't open.
    [Test] public void KarensClosedRegions_HoldABlockedDoor_UntilTheBoxMoves()
    {
        var brain=New("karen").AddComponent<Kehai.Karen.KarenBrain>();
        var block=Block();
        CollectionAssert.Contains(brain.ClosedRegions,12);
        block.Interact(player);
        CollectionAssert.DoesNotContain(brain.ClosedRegions,12);
    }
    [Test] public void TheDoorHandle_SaysToMoveTheBox()
    {
        var handle=New("door handle").AddComponent<FurnitureDoor>(); handle.roomDoor=door;
        Assert.AreEqual("Open locker",handle.GetPrompt());
        Block();
        Assert.AreEqual(DoorBlock.PickUpPrompt,handle.GetPrompt());
    }
    // 0.3.0: the box goes on the side asked for, and the door says which side you're on.
    [Test] public void TheBox_GoesOnTheSideAsked_AndEachSideSaysItsOwnThing()
    {
        Assert.IsTrue(DoorBlock.SideNormal(door,out Vector3 normal));
        foreach (int side in new[]{1,-1})
        {
            var block=DoorBlock.Spawn(door,stock,12,side);
            if (block==null) continue;   // no floor that side in this little scene
            Assert.AreEqual(side,block.Side);
            Assert.AreEqual(side,DoorBlock.SideOf(door,block.Box.transform.position));
            player.transform.position=door.DoorCentre+normal*side*1.5f;
            Assert.AreEqual(DoorBlock.PickUpPrompt,block.PromptFor(player));
            player.transform.position=door.DoorCentre-normal*side*1.5f;
            Assert.AreEqual(DoorBlock.OtherSidePrompt,block.PromptFor(player));
            block.Clear();
            home.ReturnHome();
        }
    }
}
