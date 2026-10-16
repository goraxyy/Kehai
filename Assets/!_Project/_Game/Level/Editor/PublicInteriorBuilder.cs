using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Scene authoring only. Rebuilds replace their named roots; duplicate generated furniture
// before hand-editing it. Save SampleScene with Cmd+S. All art and scene assets stay local.
public static class PublicInteriorBuilder
{
    public const string WCRoot="WC - Furnished",LobbyRoot="Lobby - Furnished";
    public const float WCSouth=OfficeRoomBuilder.North,WCNorth=-110f;
    const float West=10.15f,East=24.925f;
    static Transform root;
    static Material wall,floor,trim;

    [MenuItem("Kehai/Public Interiors/2. Build WC and Update Model Island")]
    public static void BuildWC()
    {
        Begin(WCRoot);
        float south=WCSouth+.075f,north=WCNorth-.075f;
        // The office builder owns the south partition and WC entrance at (25,-116.7).
        Solid("WC floor",new Vector3((West+East)*.5f,.012f,(south+north)*.5f),new Vector3(East-West,.024f,north-south),floor,false,true);
        Solid("WC lowered ceiling",new Vector3((West+East)*.5f,3.7f,(south+north)*.5f),new Vector3(East-West,.1f,north-south),wall,true,true);
        Solid("West lining",new Vector3(10.0875f,1.825f,(south+north)*.5f),new Vector3(.125f,3.65f,north-south),wall);
        Solid("North lining",new Vector3((West+East)*.5f,1.825f,-110.0375f),new Vector3(East-West,3.65f,.075f),wall);
        Solid("East north lining",new Vector3(24.9625f,1.825f,-112.5f),new Vector3(.075f,3.65f,5),wall);
        // Flush 2.65m modules, five along each wall. No separate family cabin.
        for(int row=0;row<2;row++)for(int i=0;i<5;i++)
        {
            int number=row*5+i+1;
            var cabin=Place("ToiletCabin","WC cabin "+number.ToString("00"),West+1.325f+i*2.65f,row==0?north-1.7f:south+1.7f,row==0?0:180);
            var door=cabin.GetComponentInChildren<FurnitureDoor>();door.label="WC cabin "+number.ToString("00");
            Label(door.transform,number.ToString("00"),new Vector3(.75f,1.9f,-.054f),0,.1f,new Vector2(.3f,.15f));
        }
        Place("DoubleVanity","Six-basin island",17.35f,-116.6667f,0);
        Place("CleaningStation","Cleaning supplies",West+.275f,-118.55f,-90);
        Label(root,"CLEANING",new Vector3(West+.568f,1.65f,-118.55f),-90,.11f,new Vector2(1,.2f));
        Place("TallPlant","WC west plant",West+.42f,-114.25f,-90);
        // Drying stations sit outside the entrance sweep and the cabin thresholds.
        foreach(float z in new[]{-114.45f,-118.65f})
        {
            Place("HandDryer","WC hand dryer",East-.1f,z,90,1.05f);
            Place("PaperDispenser","WC paper towels",East-.08f,z-.6f,90,1.16f);
            Place("SanitaryBin","WC paper bin",East-.175f,z-.6f,90);
        }
        Place("VentGrille","WC extract west",West+.025f,-116.6f,-90,2.95f);
        Place("VentGrille","WC extract east",East-.025f,-116.6f,90,2.95f);
        Label(root,"WC / 01",new Vector3(East-.025f,2.95f,-116.7f),90,.26f,new Vector2(3,.5f));
        Label(root,"WASH YOUR HANDS",new Vector3(17.35f,2.25f,-116.723f),0,.15f,new Vector2(3,.3f));
        Label(root,"WASH YOUR HANDS",new Vector3(17.35f,2.25f,-116.610f),180,.15f,new Vector2(3,.3f));
        TileLines(West,East,south,north,1.325f);
        StaffRoomBuilder.Lighting(WCSouth,WCNorth);
        // Light anchors stay in the common aisle if coarse map sampling misses a cabin.
        foreach(var anchor in root.GetComponentsInChildren<InteriorLightAnchor>())
            if(anchor.name.StartsWith("WC cabin",StringComparison.Ordinal))anchor.localPoint=new Vector3(0,0,-1.9f);
        EnvironmentPolishBuilder.RefineWC();
        Finish("WC built: ten 2.65 x 3.4m cabins, six basins, drying stations; inward cabin doors and a 2.3m+ aisle on both sides of the vanity.");
    }

    [MenuItem("Kehai/Public Interiors/3. Build Lobby and Update Model Island")]
    public static void BuildLobby()
    {
        Begin(LobbyRoot);
        const float west=25.075f,east=84.925f,south=-119.925f,north=-110.075f;
        Solid("Lobby floor",new Vector3(55,.012f,-115),new Vector3(east-west,.024f,north-south),floor,false,true);
        Solid("Lobby lowered ceiling",new Vector3(55,3.7f,-115),new Vector3(east-west,.1f,north-south),wall,true,true);
        // Preserve all four existing automatic door objects, triggers and gameplay references.
        foreach(float z in new[]{-120f,-110f})
        {
            WallRun(25f,40.9f,z);WallRun(44.1f,65.9f,z);WallRun(69.1f,85f,z);
            foreach(float x in new[]{42.5f,67.5f})Solid("Door lining header",new Vector3(x,3.125f,z),new Vector3(3.2f,1.05f,.15f),wall);
        }
        Solid("Lobby east lining",new Vector3(84.9625f,1.825f,-115),new Vector3(.075f,3.65f,10),wall);
        // Finish the public side of the WC wall, leaving the existing 1.7m frame opening.
        Solid("Lobby west south lining",new Vector3(25.0875f,1.825f,-118.775f),new Vector3(.025f,3.65f,2.45f),wall);
        Solid("Lobby west north lining",new Vector3(25.0875f,1.825f,-112.925f),new Vector3(.025f,3.65f,5.85f),wall);

        // West: customer service and WC approach. Counter leaves 2m behind it for staff.
        Place("ServiceCounter","Customer service counter",31,-113.2f,0);
        Place("ParcelShelf","Parcel and returns shelf",31,north-.31f,0);
        Place("OfficeChair","Service chair",31.8f,-111.65f,0);
        Place("ReturnCart","Returned goods cart",27.2f,-111.4f,0);
        Place("BagLockers","Twelve customer bag lockers",35,south+.3f,180);
        for(int i=0;i<6;i++)for(int j=0;j<2;j++)
            Label(root,(i+j*6+1).ToString("00"),new Vector3(36.5f-i*.6f,.92f+j*1.16f,south+.64f),180,.055f,new Vector2(.15f,.08f));
        Place("UmbrellaStand","Umbrella return",37.25f,south+.225f,180);
        Place("Directory","Store directory",37.65f,north-.045f,0,1.05f);
        Label(root,"KEHAI  /  STORE GUIDE\n\nWC  <     CUSTOMER SERVICE  <\n\nGROCERIES  /  DRINKS  /  CHILLED\n\nCHECKOUTS  /  EXIT",new Vector3(37.65f,1.95f,north-.115f),0,.11f,new Vector2(1.7f,1.4f));
        Place("RecyclingStation","Service recycling",35.5f,north-.275f,0);
        Place("LobbyPlanter","WC approach planter",28.5f,south+.325f,180);
        Sign("CUSTOMER SERVICE",new Vector3(31,2.85f,-112.4f),0,4.8f);
        Sign("WC",new Vector3(26.7f,2.85f,-116.7f),90,2f);

        // Centre: waiting space at the walls; no display island in the cross-lobby path.
        foreach(float x in new[]{48.5f,55.3f,62.1f})Place("LobbyBench","Waiting bench",x,north-.37f,0);
        foreach(float x in new[]{51.9f,58.7f})Place("LobbyPlanter","Waiting area greenery",x,north-.325f,0);
        Place("DrinksVending","Drinks vending",47.7f,south+.425f,180);
        Place("SnacksVending","Snacks vending",49.05f,south+.425f,180);
        Place("RecyclingStation","Vending recycling",51,south+.275f,180);
        Place("Noticeboard","Community noticeboard",54,south+.04f,180,1.25f);
        Place("LobbyBench","South waiting bench",58,south+.37f,180);
        Place("LobbyPlanter","South waiting greenery",62,south+.325f,180);
        Label(root,"DRINKS",new Vector3(47.7f,2.19f,south+.88f),180,.1f,new Vector2(1,.15f));
        Label(root,"SNACKS",new Vector3(49.05f,2.19f,south+.88f),180,.1f,new Vector2(1,.15f));
        Sign("WELCOME TO KEHAI",new Vector3(55,2.95f,-115),0,6);

        // East: basket pickup and four trolley lanes, approached from the west.
        for(int i=0;i<3;i++)Place("BasketStack","Basket pickup "+(i+1),71+i*.85f,north-.325f,0);
        Place("SanitiserStand","Entry hand sanitiser",70.7f,-112.2f,0);
        Place("LobbyPlanter","Basket area greenery",76,north-.325f,0);
        Place("RecyclingStation","Entry recycling",71.5f,south+.275f,180);
        Place("LobbyBench","Trolley waiting bench",76,south+.37f,180);
        for(int row=0;row<2;row++)
        {
            var corral=Place("TrolleyCorral","Trolley bay "+(row+1),east-1.69f,row==0?-112.1f:-117.9f,90);
            for(int lane=0;lane<2;lane++)for(int i=0;i<4;i++)
            {
                Vector3 p=corral.transform.TransformPoint(new Vector3(lane==0?-.7f:.7f,0,-1.05f+i*.6f));
                Place("Trolley","Parked trolley "+(row*8+lane*4+i+1),p.x,p.z,90);
            }
        }
        Sign("BASKETS",new Vector3(72,2.85f,-111.1f),0,3);
        Sign("TROLLEYS",new Vector3(81.3f,2.85f,-115),90,3);

        foreach(float x in new[]{42.5f,67.5f})
        {
            Place("EntryMat","Outer door mat",x,-111.2f,0,.027f);
            Place("EntryMat","Inner door mat",x,-118.8f,0,.027f);
            Place("SecurityGate","Security gate left",x-1.85f,-118.55f,0);
            Place("SecurityGate","Security gate right",x+1.85f,-118.55f,0);
            Sign(x<50?"EXIT":"ENTRANCE",new Vector3(x,2.93f,-119.1f),180,3);
            Sign(x<50?"THANK YOU":"WELCOME",new Vector3(x,2.93f,-110.9f),0,3);
        }
        Place("FireExtinguisher","Lobby extinguisher",west+.16f,-118.9f,-90,.35f);
        Place("FirstAid","Lobby first aid",west+.09f,-114.6f,-90,1.25f);
        TileLines(west,east,south,north,2.5f);
        StaffRoomBuilder.Lighting(-120,-110,25,85);
        Finish("Lobby built: customer service, waiting, vending, baskets and sixteen trolley models; existing door lanes stay clear. Vending, bag lockers and carts are visual props.");
    }

    static void Begin(string name)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build in edit mode.");
        if(GameObject.Find("Layout_15x15/Walls")==null||GameObject.Find(OfficeRoomBuilder.RootName)==null)
            throw new InvalidOperationException("Open the furnished SampleScene and build the staff room and office first.");
        var scene=SceneManager.GetActiveScene();
        Directory.CreateDirectory("Library/Kehai/SceneBackups");
        if(!string.IsNullOrEmpty(scene.path))File.Copy(scene.path,"Library/Kehai/SceneBackups/"+name+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity",false);
        PublicInteriorModels.BuildLibrary();
        Undo.IncrementCurrentGroup();Undo.SetCurrentGroupName("Build "+name);
        var old=GameObject.Find(name);if(old!=null)Undo.DestroyObjectImmediate(old);
        root=new GameObject(name).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Build "+name);
        wall=StaffRoomModels.Material("Room Walls",new Color(.83f,.84f,.79f));
        floor=StaffRoomModels.Material(name==WCRoot?"WC Floor":"Lobby Floor",name==WCRoot?new Color(.43f,.5f,.47f):new Color(.6f,.62f,.57f));
        trim=StaffRoomModels.Material("Interior Grout",new Color(.31f,.37f,.34f));
    }
    static void Finish(string message)
    {
        ModelIslandLayout.Organize();StaffRoomBuilder.BakeNavigation();StaffRoomBuilder.RefreshLighting();
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);AssetDatabase.SaveAssets();
        Selection.activeGameObject=root.gameObject;Debug.Log(message+" Save the scene with Cmd+S.");
    }
    static GameObject Place(string model,string name,float x,float z,float yaw,float y=0)
    {
        var prefab=PublicInteriorModels.Prefab(model)??StaffRoomModels.Prefab(model)??OfficeRoomModels.Prefab(model);
        if(prefab==null)throw new InvalidOperationException("Missing public interior model "+model);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);go.name=name;
        go.transform.SetPositionAndRotation(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0));return go;
    }
    static void Solid(string name,Vector3 at,Vector3 size,Material mat,bool collide=true,bool anchored=false)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);
        go.transform.position=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!collide)Object.DestroyImmediate(go.GetComponent<Collider>());
        if(name.Contains("ceiling"))go.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        if(anchored)go.AddComponent<InteriorLightAnchor>();
    }
    static void WallRun(float left,float right,float z)
        =>Solid("Lobby wall lining",new Vector3((left+right)*.5f,1.825f,z),new Vector3(right-left,3.65f,.15f),wall);
    static void TileLines(float west,float east,float south,float north,float pitch)
    {
        // Grout belongs to the floor material; crossing strips flicker where they overlap.
        foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
            if(r.name.EndsWith("floor",StringComparison.OrdinalIgnoreCase))
                r.sharedMaterial=StoreFinishBuilder.FloorMaterial(root.name==WCRoot?"WC Tiles":"Lobby Tiles",east-west,north-south,pitch,new Color(.48f,.52f,.49f));
    }
    static void Sign(string value,Vector3 at,float yaw,float width)
    {
        var sign=new GameObject("Sign - "+value).transform;sign.SetParent(root,false);sign.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));
        var panel=GameObject.CreatePrimitive(PrimitiveType.Cube);panel.name="Sign board";panel.transform.SetParent(sign,false);panel.transform.localScale=new Vector3(width,.45f,.07f);
        panel.GetComponent<Renderer>().sharedMaterial=StaffRoomModels.Material("Public Sign",new Color(.12f,.2f,.18f));Object.DestroyImmediate(panel.GetComponent<Collider>());
        float suspension=3.65f-at.y-.225f;
        foreach(float x in new[]{-width*.35f,width*.35f})
        {
            var rod=GameObject.CreatePrimitive(PrimitiveType.Cube);rod.name="Ceiling suspension";rod.transform.SetParent(sign,false);
            rod.transform.localPosition=new Vector3(x,.225f+suspension*.5f,0);rod.transform.localScale=new Vector3(.025f,suspension,.025f);
            rod.GetComponent<Renderer>().sharedMaterial=panel.GetComponent<Renderer>().sharedMaterial;Object.DestroyImmediate(rod.GetComponent<Collider>());
        }
        sign.gameObject.AddComponent<InteriorLightAnchor>();
        foreach(float side in new[]{-1f,1f})
        {Label(sign,value,new Vector3(0,0,side*.04f),side<0?0:180,.22f,new Vector2(width-.2f,.4f));sign.GetChild(sign.childCount-1).GetComponent<TMP_Text>().color=new Color(.95f,.95f,.88f);}
    }
    static void Label(Transform p,string text,Vector3 at,float yaw,float height,Vector2 size)
        =>StaffRoomBuilder.Text(p,text,at,yaw,height,size);
}
