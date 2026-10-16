using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class OfficeRoomBuilder
{
    public const string RootName="Office - Furnished";
    public const float North=-123.3333333f,South=StaffRoomBuilder.North;
    const float West=10.15f,East=24.925f;
    static Transform root;
    static Material wall,floor;

    [MenuItem("Kehai/Office/2. Build Office and Update Model Island")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build in edit mode.");
        if(GameObject.Find(StaffRoomBuilder.RootName)==null)throw new InvalidOperationException("Build the staff room first.");
        Scene scene=SceneManager.GetActiveScene();
        Directory.CreateDirectory("Library/Kehai/SceneBackups");
        if(!string.IsNullOrEmpty(scene.path))File.Copy(scene.path,"Library/Kehai/SceneBackups/Office-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".unity",false);
        Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build office");int undo=Undo.GetCurrentGroup();
        OfficeRoomModels.BuildLibrary();
        var old=GameObject.Find(RootName);if(old!=null)Undo.DestroyObjectImmediate(old);
        root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Build office");
        wall=StaffRoomModels.Material("Room Walls",new Color(.83f,.84f,.79f));
        floor=StaffRoomModels.Material("Office Floor",new Color(.34f,.37f,.36f));
        Shell();FitOut();StaffRoomBuilder.Lighting(South,North);
        NameExtraLightLayer();
        ModelIslandLayout.Organize();
        StaffRoomBuilder.BakeNavigation();
        StaffRoomBuilder.RefreshLighting();
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();Undo.CollapseUndoOperations(undo);
        Selection.activeGameObject=root.gameObject;
        Debug.Log("Office built: 15 x 13 1/3m, manager desk, CCTV console, briefing table, storage, print station and lounge. Save with Cmd+S.");
    }

    static void Shell()
    {
        var walls=GameObject.Find("Layout_15x15/Walls").transform;
        foreach(var r in walls.GetComponentsInChildren<MeshRenderer>(true))
        {
            Bounds b=r.bounds;
            // Open the office entrance and relocate the former front-room entrance into WC.
            if(b.size.x>.1f||Mathf.Abs(b.center.x-25f)>.15f)continue;
            bool office=b.min.z>=-135.05f&&b.max.z<=-124.95f;
            bool wc=b.min.z>=-125.05f&&b.max.z<=-114.95f;
            if(office||wc){Undo.RecordObject(r.gameObject,"Replace east-wall doorway segment");r.gameObject.SetActive(false);}
        }
        var doors=GameObject.Find("Layout_15x15/SingleDoors");
        if(doors!=null)foreach(var d in doors.GetComponentsInChildren<HingeDoor>(true))
            if(Vector2.Distance(new Vector2(d.transform.position.x,d.transform.position.z),new Vector2(25,-122.5f))<2.5f)
            {Undo.RecordObject(d.gameObject,"Move WC entrance");d.gameObject.SetActive(false);}
        Solid("Office floor",new Vector3(17.5f,.012f,(South+North)*.5f),new Vector3(East-West,.024f,North-South-.15f),floor,false);
        Solid("Office lowered ceiling",new Vector3(17.5f,3.7f,(South+North)*.5f),new Vector3(East-West,.1f,North-South-.15f),wall);
        Solid("West lining",new Vector3(10.0875f,4,(South+North)*.5f),new Vector3(.125f,8,North-South-.15f),wall);
        Solid("Office WC partition",new Vector3(17.5f,4,North),new Vector3(15f,8,.15f),wall);
        // Replacement 20m wall run. Door clear openings are 1.5m inside their frames.
        EastWall(-135f,-130.83f);EastWall(-129.17f,-117.53f);EastWall(-115.87f,-115f);
        foreach(float z in new[]{-130f,-116.7f})
        {
            Solid("Door header",new Vector3(25,5.3f,z),new Vector3(.15f,5.4f,1.66f),wall);
            var door=Place("RoomDoor",z<-120?"Office entrance":"WC entrance",25,z,90);
            door.GetComponentInChildren<FurnitureDoor>().label=z<-120?"office door":"WC door";
        }
        // Finish the last south segment, whose original outside wall remains.
        Solid("East south lining",new Vector3(24.95f,4,(South-.0f-135f)*.5f),new Vector3(.05f,8,-135f-South),wall);
        StaffRoomBuilder.Text(root,"OFFICE / 02",new Vector3(East-.018f,2.95f,-130),90,.3f,new Vector2(3,.5f));
    }

    static void EastWall(float a,float b)=>Solid("East wall",new Vector3(25,4,(a+b)*.5f),new Vector3(.15f,8,b-a),wall);

    static void FitOut()
    {
        float northFace=North-.075f,southFace=South+.075f;
        // L desk sits against both north and west walls; return projects to the left of its chair.
        Place("ManagerDesk","Karen's desk",West+1.8f,northFace-.5f,0);
        Place("OfficeChair","Manager chair",13.15f,-125.65f,180);
        Place("DiningChair","Visitor chair 01",11.95f,-126.9f,180);
        Place("DiningChair","Visitor chair 02",13.35f,-126.9f,180);
        Place("Monitor","Manager monitor",13.1f,northFace-.22f,0,.8f);
        Place("Keyboard","Manager keyboard",13.1f,northFace-.7f,0,.8f);
        Place("DeskLamp","Desk task lamp",10.5f,northFace-.28f,0,.8f);
        Place("DocumentTray","In tray",10.55f,-125.35f,90,.8f);
        Place("PaperStack","Desk papers",11.1f,northFace-.65f,0,.8f);

        Place("SecurityConsole","Security workstation",17.25f,northFace-.5f,0);
        Place("OfficeChair","Security chair 01",16.35f,-125.35f,180);
        Place("OfficeChair","Security chair 02",18.2f,-125.35f,180);
        Place("TallCabinet","Archive cabinet 01",20.55f,northFace-.3f,0);
        Place("TallCabinet","Archive cabinet 02",21.75f,northFace-.3f,0);
        Place("TallPlant","Office northeast plant",East-.42f,northFace-.42f,0);
        Place("WallClock","Office clock",17.25f,northFace-.03f,0,2.65f);
        Place("SecurityCamera","Office camera",East-.22f,northFace-.4f,90,2.9f);

        for(int i=0;i<3;i++)Place("FilingCabinet","Filing cabinet "+(i+1),West+.325f,-129.2f-i*.85f,-90);
        Place("Binders","Filing reference binders",West+.2f,-130.05f,-90,1.36f);
        Place("Whiteboard","Planning whiteboard",West+.04f,-133.2f,-90,1.25f);
        Place("OfficeSafe","Office safe",West+.4f,southFace+.5f,-90);

        Place("BriefingTable","Open briefing table",17.15f,-130.2f,0);
        for(int i=0;i<4;i++)
        {
            float x=15.3f+i*1.2f;
            Place("DiningChair","Briefing chair north "+(i+1),x,-128.8f,0);
            Place("DiningChair","Briefing chair south "+(i+1),x,-131.6f,180);
        }
        Place("Laptop","Briefing laptop",16.5f,-130.15f,0,.8f);
        Place("PaperStack","Meeting papers",17.4f,-130.3f,0,.8f);
        Place("SmallPlant","Briefing table plant",18.6f,-130.2f,0,.8f);
        Place("CeilingPendant","Briefing pendant",17.15f,-130.2f,0,2.8f);

        Place("PrinterCabinet","Print station",12.7f,southFace+.35f,180);
        Place("Printer","Office printer",12.35f,southFace+.3f,180,.86f);
        Place("PaperStack","Printer paper",13.2f,southFace+.28f,180,.86f);
        Place("Credenza","Document counter",16.3f,southFace+.325f,180);
        Place("Binders","Personnel binders",15.6f,southFace+.25f,180,.86f);
        Place("DocumentTray","Outgoing documents",16.7f,southFace+.28f,180,.86f);
        Place("Noticeboard","Office notices",16.3f,southFace+.04f,180,1.3f);
        Place("FirstAid","Office first aid",18.4f,southFace+.09f,180,1.3f);
        Place("TallPlant","Office south plant",20.7f,southFace+.42f,180);

        Place("Sofa","Office waiting sofa",East-.5f,-133.2f,90);
        Place("CoffeeTable","Office lounge table",21.8f,-133.2f,90);
        Place("SmallPlant","Office lounge plant",21.8f,-133.6f,0,.43f);
        Place("WallScreen","Office wall screen",East-.08f,-133.2f,90,1.65f);
        Place("WaterDispenser","Office water dispenser",East-.25f,-125.9f,90);
        Place("RecyclingStation","Office paper recycling",East-.275f,-127.3f,90);
        Place("CoatRail","Office coat hooks",East-.025f,-128.3f,90,1.7f);
    }

    static GameObject Place(string model,string name,float x,float z,float yaw,float y=0)
    {
        var prefab=OfficeRoomModels.Prefab(model)??StaffRoomModels.Prefab(model);
        if(prefab==null)throw new InvalidOperationException("Missing office model "+model);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);go.name=name;
        go.transform.SetPositionAndRotation(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0));return go;
    }
    static void Solid(string name,Vector3 at,Vector3 size,Material mat,bool collide=true)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);go.transform.position=at;go.transform.localScale=size;
        go.GetComponent<Renderer>().sharedMaterial=mat;if(!collide)Object.DestroyImmediate(go.GetComponent<Collider>());
        if(name.Contains("ceiling"))go.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        if(name.Contains("floor")||name.Contains("ceiling"))go.AddComponent<InteriorLightAnchor>();
    }
    static void NameExtraLightLayer()
    {
        var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers=settings.FindProperty("m_RenderingLayers");
        if(layers==null)throw new InvalidOperationException("Rendering-layer settings not found.");
        bool added=layers.arraySize<9;if(added)layers.arraySize=9;
        if(added||string.IsNullOrWhiteSpace(layers.GetArrayElementAtIndex(8).stringValue))layers.GetArrayElementAtIndex(8).stringValue="Interior Extra";
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
