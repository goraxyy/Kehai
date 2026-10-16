using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class StaffRoomBuilder
{
    public const string RootName = "Staff Room - Furnished";
    public const float North = -136.6666667f;
    const float WestFace = 10.15f, EastFace = 24.925f, SouthFace = -149.925f;
    static Transform root;
    static Material wall, floor, trim;

    [MenuItem("Kehai/Staff Room/2. Build Staff Room and Model Island")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build in edit mode.");
        Scene scene=SceneManager.GetActiveScene();
        if(GameObject.Find("Layout_15x15/Walls")==null)throw new InvalidOperationException("Open SampleScene with Layout_15x15 first.");
        // Backup the last saved scene without overwriting any earlier backup.
        if(!string.IsNullOrEmpty(scene.path))
        {
            Directory.CreateDirectory("Library/Kehai/SceneBackups");
            File.Copy(scene.path,"Library/Kehai/SceneBackups/StaffRoom-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",false);
        }
        Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build staff room");int undo=Undo.GetCurrentGroup();
        StaffRoomModels.BuildLibrary();
        var old=GameObject.Find(RootName);if(old!=null)Undo.DestroyObjectImmediate(old);
        root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Build staff room");
        wall=StaffRoomModels.Material("Room Walls",new Color(.83f,.84f,.79f));
        floor=StaffRoomModels.Material("Room Floor",new Color(.39f,.43f,.41f));
        trim=StaffRoomModels.Material("Room Trim",new Color(.2f,.27f,.25f));
        Shell();
        FitOut();
        Lighting(-150f,North);
        ModelIslandLayout.Organize();
        BakeNavigation();
        RefreshLighting();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undo);
        Selection.activeGameObject=root.gameObject;
        Debug.Log("Staff room built: 15 x 13 1/3 m, ten 1.2 x 1.4 x 2.4 m hiding lockers, two changing cabins, kitchen and lounge. Save the scene with Cmd+S.");
    }

    public static void RefreshLighting()
    {
        foreach(string name in new[]{RoomLighting.PatchesName,RoomLighting.ProbesName})
        {var old=GameObject.Find(name);if(old!=null)Undo.DestroyObjectImmediate(old);}
        RoomLighting.Apply(Kehai.Store.StoreMap.Rebuild());
    }

    [MenuItem("Kehai/Staff Room/4. Bake Updated Navigation")]
    public static void BakeNavigation()
    {
        var surface=Kehai.Karen.KarenWorld.StoreSurface();
        if(surface==null)throw new InvalidOperationException("No store navigation surface exists.");
        Undo.RecordObject(surface,"Bake furnished staff room");
        surface.useGeometry=UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask&=~LayerMask.GetMask("Interactable","Ignore Raycast");
        foreach(var player in Object.FindObjectsByType<CharacterController>())
        {
            var modifier=player.GetComponent<Unity.AI.Navigation.NavMeshModifier>();
            if(modifier==null)modifier=Undo.AddComponent<Unity.AI.Navigation.NavMeshModifier>(player.gameObject);
            Undo.RecordObject(modifier,"Exclude moving player from navigation bake");modifier.ignoreFromBuild=true;
        }
        Physics.SyncTransforms();
        surface.BuildNavMesh();
        if(surface.navMeshData==null)throw new InvalidOperationException("Navigation bake did not produce data.");
        // Keep earlier assets so Undo/scene backups can still restore the previous layout.
        string path=AssetDatabase.GenerateUniqueAssetPath(StaffRoomModels.Folder+"/StoreNavigation.asset");
        AssetDatabase.CreateAsset(surface.navMeshData,path);
        EditorUtility.SetDirty(surface);
    }

    static void Shell()
    {
        // Retain replaced geometry, inactive, so the earlier layout can be recovered.
        var walls=GameObject.Find("Layout_15x15/Walls").transform;
        foreach(var r in walls.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b=r.bounds;
            if(b.min.x<9.8f||b.max.x>25.1f||b.size.z>.3f)continue;
            if(Mathf.Abs(b.center.z+130.25f)<.1f || Mathf.Abs(b.center.z+150f)<.1f)
            {Undo.RecordObject(r.gameObject,"Retire old partition");r.gameObject.SetActive(false);}
        }
        var doors=GameObject.Find("Layout_15x15/SingleDoors");
        if(doors!=null)foreach(var d in doors.GetComponentsInChildren<HingeDoor>(true))
            if(Vector2.Distance(new Vector2(d.transform.position.x,d.transform.position.z),new Vector2(25,-142.5f))<2.5f)
            {Undo.RecordObject(d.gameObject,"Replace staff door");d.gameObject.SetActive(false);}

        Solid("Staff floor",new Vector3(17.5f,.012f,(SouthFace+North-.075f)*.5f),new Vector3(EastFace-WestFace,.024f,North-.075f-SouthFace),floor,false);
        Solid("Staff lowered ceiling",new Vector3(17.5f,3.7f,(SouthFace+North-.075f)*.5f),new Vector3(EastFace-WestFace,.1f,North-.075f-SouthFace),wall);
        // The thin original side walls remain; these linings establish exact furniture faces.
        Solid("West lining",new Vector3(10.0875f,4,(North+SouthFace)*.5f),new Vector3(.125f,8,North-SouthFace),wall);
        foreach(var segment in new[]{new Vector2(North-.075f,-141.5f),new Vector2(-143.5f,SouthFace)})
        {
            float length=Mathf.Abs(segment.y-segment.x);
            Solid("East lining",new Vector3(25f,4,(segment.x+segment.y)*.5f),new Vector3(.15f,8,length),wall);
        }
        Solid("East doorway top",new Vector3(25f,5.3f,-142.5f),new Vector3(.15f,5.4f,2f),wall);
        Partition("Office partition",North,22.65f,24.35f);
        Partition("Backstreet partition",-150,16.65f,18.35f);
        // Existing east opening is 2m; new door has 1.5m clear between the jambs.
        foreach(float z in new[]{-143.415f,-141.585f})Solid("Entrance infill",new Vector3(25f,1.3f,z),new Vector3(.15f,2.6f,.17f),wall);
        Place("RoomDoor","Staff entrance",25f,-142.5f,90);
        Place("RoomDoor","Office connection",23.5f,North,0);
        Place("RoomDoor","Backstreet connection",17.5f,-150,180);
        // Durable lower-wall band, above the furniture and below notices.
        Solid("West wall band",new Vector3(10.158f,1.15f,(North+SouthFace)*.5f),new Vector3(.016f,.1f,North-SouthFace),trim,false);
        Text(root,"STAFF / 03",new Vector3(24.906f,2.95f,-142.5f),90,.3f,new Vector2(3,.5f));
    }

    static void Partition(string name,float z,float doorLeft,float doorRight)
    {
        Solid(name+" left",new Vector3((10+doorLeft)*.5f,4,z),new Vector3(doorLeft-10,8,.15f),wall);
        Solid(name+" right",new Vector3((25+doorRight)*.5f,4,z),new Vector3(25-doorRight,8,.15f),wall);
        Solid(name+" header",new Vector3((doorLeft+doorRight)*.5f,5.3f,z),new Vector3(doorRight-doorLeft,5.4f,.15f),wall);
    }

    static void FitOut()
    {
        for(int i=0;i<10;i++)
        {
            var locker=Place("Locker","Locker "+(i+1).ToString("00"),WestFace+.6f+1.2f*i,North-.075f-.7f,0);
            var door=locker.GetComponentInChildren<FurnitureDoor>();door.label="locker "+(i+1).ToString("00");
            Text(door.transform,(i+1).ToString("00"),new Vector3(.54f,1.89f,-.055f),0,.07f,new Vector2(.24f,.1f));
        }
        // Deeper lockers keep their backs flush; open leaves leave over 1.4m before the cabins.
        Place("ChangingCabin","Changing cabin 01",WestFace+1.35f,-142f,-90);
        Place("ChangingCabin","Changing cabin 02",WestFace+1.35f,-144.6f,-90);
        for(int i=0;i<2;i++)Text(root,"CHANGE 0"+(i+1),new Vector3(12.86f,2.43f,-142f-i*2.6f),-90,.13f,new Vector2(1.5f,.22f));

        Place("Fridge","Staff fridge",WestFace+.5f,-149.4f,-90);
        Place("KitchenCounter","Appliance counter",WestFace+.55f,-148.275f,-90);
        Place("SinkCounter","Sink and soap",WestFace+.55f,-147.075f,-90);
        Place("Microwave","Microwave",WestFace+.3f,-148.65f,-90,.97f);
        Place("WallShelf","Kitchen wall shelf",WestFace+.2f,-147.4f,-90,1.65f);
        Place("Kettle","Kettle",WestFace+.2f,-147.6f,-90,1.69f);
        // Functional props keep their original components and serialized references.
        Dress("CoffeeMachine","CoffeeMachine",new Vector3(WestFace+.42f,.97f,-148.04f),-90);
        Dress("MusicBox","Radio",new Vector3(WestFace+.2f,1.69f,-146.65f),-90);
        Dress("Puncher","TimeClock",new Vector3(EastFace-.07f,1.2f,-139.3f),90);

        Place("DiningTable","Shared dining table",16.8f,-142.35f,0);
        for(int i=0;i<4;i++)
        {
            float x=14.95f+i*1.2f;
            Place("DiningChair","Dining chair north "+(i+1),x,-140.9f,0);
            Place("DiningChair","Dining chair south "+(i+1),x,-143.8f,180);
            Place("Mug","Cup "+(i+1),x,-142.0f,0,.82f);
        }
        Place("SmallPlant","Table plant",16.75f,-142.5f,0,.82f);
        Place("CeilingPendant","Dining pendant",16.8f,-142.35f,0,2.8f);
        Place("FoldingTable","Uniform folding table",15.55f,-146.4f,0);
        Place("FoldedUniform","Clean uniforms",14.9f,-146.4f,0,.9f);
        Place("FoldedUniform","Spare uniforms",15.85f,-146.4f,180,.9f);

        Place("Sofa","Staff sofa",21.6f,SouthFace+.5f,180);
        Place("CoffeeTable","Lounge coffee table",21.5f,-147.5f,0);
        Place("LoungeChair","Lounge chair",19.65f,-147.1f,110);
        Place("SmallPlant","Lounge table plant",22f,-147.5f,0,.43f);
        Place("WallScreen","Staff screen",21.6f,SouthFace+.08f,180,1.65f);
        var board=Place("Whiteboard","Shift whiteboard",EastFace,-146.3f,90,1.25f);
        board.transform.localScale=new Vector3(1.8f,1,1);
        board.transform.position+=Vector3.right*(EastFace-BoundsOf(board).max.x);
        Place("Noticeboard","Staff notices",EastFace-.04f,-137.7f,90,1.05f);
        Place("WallClock","Staff clock",EastFace-.03f,-139.3f,90,2.1f);
        Place("FirstAid","First aid",16f,SouthFace+.09f,180,1.3f);
        Place("FireExtinguisher","Fire extinguisher",18.85f,SouthFace+.16f,180,.35f);
        Place("CoatRail","Visitor coat rail",19.6f,SouthFace+.025f,180,1.7f);
        Place("RecyclingStation","Kitchen recycling",14.8f,SouthFace+.275f,180);
        Place("TallPlant","Lounge corner plant",EastFace-.42f,SouthFace+.42f,180);
        // No pots or loose bins in the door approaches or the main east circulation lane.
        var stock=GameObject.Find("Stock_def");
        if(stock!=null)MoveTool(stock,new Vector3(12.6f,.5f,SouthFace+.5f),Quaternion.identity);
        var mop=GameObject.Find("Mop");
        if(mop!=null)
        {
            Bounds b=BoundsOf(mop);Vector3 delta=new Vector3(EastFace-b.max.x,0,-140.5f-b.center.z);
            MoveTool(mop,mop.transform.position+delta,mop.transform.rotation);
        }
        // Existing shelf remains available as the staff supply shelf, aligned by real bounds.
        var fridges=GameObject.Find("Layout_15x15/Fridges");
        if(fridges!=null)foreach(Transform t in fridges.transform)
            if(t.name.StartsWith("Prop_StorageShelve",StringComparison.Ordinal))
            {
                Undo.RecordObject(t,"Align staff supply shelf");Bounds b=BoundsOf(t.gameObject);
                t.position+=new Vector3(EastFace-b.max.x,0,-145.5f-b.center.z);
                break;
            }
    }

    public static void Lighting(float south,float north,float west=10f,float east=25f)
    {
        var lamps=Object.FindAnyObjectByType<CeilingLamps>();
        foreach(var light in Object.FindObjectsByType<Light>())
        {
            Vector3 p=light.transform.position;
            if(light.type!=LightType.Spot||p.x<=west||p.x>=east||p.z<=south||p.z>=north)continue;
            Undo.RecordObject(light.transform,"Lower staff light");Undo.RecordObject(light,"Tune staff light");
            light.transform.position=new Vector3(p.x,3.5f,p.z);light.intensity=8;light.range=10;light.spotAngle=110;light.innerSpotAngle=75;
            if(lamps==null)continue;
            foreach(var lamp in lamps.lamps)
            {
                if(lamp.light!=light||lamp.panel==null)continue;
                var body=lamp.panel.transform.parent;Undo.RegisterFullObjectHierarchyUndo(body.gameObject,"Resize staff luminaire");
                body.position=new Vector3(p.x,3.512f,p.z);
                foreach(Transform t in body)
                {
                    if(t.name=="Box"){t.localPosition=new Vector3(0,.05f,0);t.localScale=new Vector3(1.2f,.1f,.6f);}
                    if(t.name=="Panel"){t.localPosition=new Vector3(0,-.003f,0);t.localScale=new Vector3(1.1f,.004f,.5f);}
                    if(t.name=="Wire"){t.localPosition=new Vector3(0,.12f,0);t.localScale=new Vector3(.012f,.04f,.012f);}
                }
            }
        }
    }

    public static GameObject Place(string model,string name,float x,float z,float yaw,float y=0)
    {
        var prefab=StaffRoomModels.Prefab(model);if(prefab==null)throw new InvalidOperationException("Missing model "+model);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
        go.name=name;go.transform.SetPositionAndRotation(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0));
        return go;
    }

    static void Dress(string name,string model,Vector3 at,float yaw)
    {
        var go=GameObject.Find(name);if(go==null){Debug.LogWarning("Staff room: missing gameplay object "+name);return;}
        Undo.RegisterFullObjectHierarchyUndo(go,"Dress "+name);
        var previous=go.transform.Find("Staff Visual");if(previous!=null)Undo.DestroyObjectImmediate(previous.gameObject);
        // Replace only the old primitive rendering; gameplay components stay on the same object.
        var renderer=go.GetComponent<MeshRenderer>();if(renderer!=null)Undo.DestroyObjectImmediate(renderer);
        var filter=go.GetComponent<MeshFilter>();if(filter!=null)Undo.DestroyObjectImmediate(filter);
        go.transform.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));go.transform.localScale=Vector3.one;
        var visual=(GameObject)PrefabUtility.InstantiatePrefab(StaffRoomModels.Prefab(model),go.transform);
        visual.name="Staff Visual";visual.transform.localPosition=Vector3.zero;visual.transform.localRotation=Quaternion.identity;
        PrefabUtility.UnpackPrefabInstance(visual,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        foreach(var c in visual.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        foreach(Transform t in visual.GetComponentsInChildren<Transform>(true))t.gameObject.layer=go.layer;
        var box=go.GetComponent<BoxCollider>();
        if(box!=null)
        {
            Bounds local=new Bounds();bool first=true;
            foreach(var mf in visual.GetComponentsInChildren<MeshFilter>())
            {
                Bounds b=mf.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    Vector3 c=b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    Vector3 point=go.transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if(first){local=new Bounds(point,Vector3.zero);first=false;}else local.Encapsulate(point);
                }
            }
            box.center=local.center;box.size=local.size;
        }
        EditorUtility.SetDirty(go);
        var lighting=go.GetComponent<InteriorLightAnchor>();if(lighting==null)lighting=go.AddComponent<InteriorLightAnchor>();
        if(box!=null)lighting.localPoint=new Vector3(box.center.x,0,box.center.z-box.size.z*.5f-.35f);
    }

    static void MoveTool(GameObject tool,Vector3 position,Quaternion rotation)
    {
        Undo.RecordObject(tool.transform,"Move staff tool");tool.transform.SetPositionAndRotation(position,rotation);
        var home=tool.GetComponent<ItemHome>();if(home!=null&&home.snapPoint!=null)
        {Undo.RecordObject(home.snapPoint,"Move tool home");home.snapPoint.SetPositionAndRotation(position,rotation);}
    }

    static void Solid(string name,Vector3 center,Vector3 size,Material material,bool collider=true)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);
        go.transform.position=center;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        if(!collider)Object.DestroyImmediate(go.GetComponent<Collider>());
        if(name.Contains("ceiling"))go.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        if(name.Contains("floor")||name.Contains("ceiling"))go.AddComponent<InteriorLightAnchor>();
    }

    public static Bounds BoundsOf(GameObject go)
    {
        var renderers=go.GetComponentsInChildren<Renderer>(true);Bounds b=new Bounds(go.transform.position,Vector3.zero);bool any=false;
        foreach(var r in renderers){if(r is not MeshRenderer||r.GetComponent<TMP_Text>()!=null)continue;if(!any){b=r.bounds;any=true;}else b.Encapsulate(r.bounds);}return b;
    }

    public static void Text(Transform parent,string value,Vector3 position,float yaw,float height,Vector2 size,bool world=false)
    {
        var go=new GameObject("Label - "+value);go.transform.SetParent(parent,false);
        if(world)go.transform.position=position;else go.transform.localPosition=position;
        go.transform.localRotation=Quaternion.Euler(0,yaw,0);
        var text=go.AddComponent<TextMeshPro>();text.text=value;text.font=TMP_Settings.defaultFontAsset;text.fontSize=height*10;
        text.alignment=TextAlignmentOptions.Center;text.color=new Color(.12f,.17f,.15f);text.rectTransform.sizeDelta=size;
        text.textWrappingMode=TextWrappingModes.NoWrap;
    }
}
