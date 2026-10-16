using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Metres, floor pivot, front toward local -Z. Generated art stays local; these recipes
// are the source of truth. Fixtures are models; only cabin doors have gameplay here.
public static class PublicInteriorModels
{
    public const string Folder="Assets/!_Project/_Art/Generated/PublicInteriors";
    public static readonly string[] WC={"ToiletCabin","Toilet","DoubleVanity","HandDryer","PaperDispenser",
        "SoapDispenser","SanitaryBin","CleaningStation","CleaningHideCabinet","DryingStation","CleaningBucket","CleaningSupplies","CautionSign","VentGrille"};
    public static readonly string[] Lobby={"LobbyBench","DrinksVending","SnacksVending","ServiceCounter",
        "ParcelShelf","BagLockers","UmbrellaStand","SanitiserStand","BasketStack","Trolley",
        "TrolleyCorral","SecurityGate","Directory","QueuePost","LobbyPlanter","ReturnCart","EntryMat"};
    static Material white,sage,dark,oak,steel,red,glass,blue,yellow,paper,ceramic;

    [MenuItem("Kehai/Public Interiors/1. Build WC and Lobby Model Library")]
    public static void BuildLibrary()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build in edit mode.");
        if(StaffRoomModels.Prefab("Locker")==null)StaffRoomModels.BuildLibrary();
        Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Prefabs");AssetDatabase.Refresh();
        white=Shared("Warm White");sage=Shared("Sage Enamel");dark=Shared("Graphite");oak=Shared("Light Oak");
        steel=Shared("Brushed Steel");red=Shared("Safety Red");glass=Shared("Dark Screen");paper=Shared("Paper");
        blue=StaffRoomModels.Material("Public Muted Blue",new Color(.29f,.48f,.55f));
        yellow=StaffRoomModels.Material("Caution Yellow",new Color(.9f,.67f,.15f));
        ceramic=StaffRoomModels.Material("Sanitary Ceramic",new Color(.92f,.94f,.91f));ceramic.SetFloat("_Smoothness",.5f);
        foreach(var name in WC)StaffRoomModels.BuildCustom(name,Folder,p=>Build(p,name));
        foreach(var name in Lobby)StaffRoomModels.BuildCustom(name,Folder,p=>Build(p,name));
        foreach(string cabinetName in new[]{"CleaningStation","CleaningHideCabinet"})
        {
            string path=Folder+"/Prefabs/"+cabinetName+".prefab";
            var cabinet=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(cabinetName=="CleaningHideCabinet")
                {
                    var inside=cabinet.AddComponent<ColdHideInterior>();inside.interior=new Bounds(new Vector3(0,1.25f,0),new Vector3(1.32f,2.35f,1.22f));
                    inside.insideMaterial=ColdStoreBuilder.Glass("Cupboard interior view",.23f);
                }
                var door=cabinet.GetComponentInChildren<FurnitureDoor>();var targets=new System.Collections.Generic.List<MeshFilter>();
                foreach(float x in new[]{.025f,1.295f})targets.Add(ColdStoreBuilder.Primitive(door.transform,"Frame side",new Vector3(x,1.24f,0),new Vector3(.05f,2.32f,.07f),white,false).GetComponent<MeshFilter>());
                foreach(float y in new[]{.105f,2.375f})targets.Add(ColdStoreBuilder.Primitive(door.transform,"Frame rail",new Vector3(.66f,y,0),new Vector3(1.32f,.05f,.07f),white,false).GetComponent<MeshFilter>());
                door.GetComponent<OutlineHighlight>().targetMeshes=targets.ToArray();
                PrefabUtility.SaveAsPrefabAsset(cabinet,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(cabinet);}
        }
        AssetDatabase.SaveAssets();
    }

    static Material Shared(string name)=>AssetDatabase.LoadAssetAtPath<Material>(StaffRoomModels.Folder+"/Materials/"+name+".mat");
    public static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Prefabs/"+name+".prefab");
    static void B(Transform p,string n,float x,float y,float z,float w,float h,float d,Material m,bool solid=false)
        =>StaffRoomModels.Box(p,n,new Vector3(x,y,z),new Vector3(w,h,d),m,solid,.012f);
    static GameObject C(Transform p,string n,Vector3 at,float r,float h,Material m,Vector3 rotation,float taper=1)
        =>StaffRoomModels.Cylinder(p,n,at,r,h,m,rotation,taper);
    static void R(Transform p,string n,Vector3 a,Vector3 b,float r,Material m)=>StaffRoomModels.Rod(p,n,a,b,r,m);
    static Transform Child(Transform p,string n,Vector3 at)
    {var t=new GameObject(n).transform;t.SetParent(p,false);t.localPosition=at;return t;}

    static void Build(Transform p,string name)
    {
        switch(name)
        {
            case "ToiletCabin":Cabin(p);break;
            case "Toilet":Toilet(p);break;
            case "DoubleVanity":Vanity(p);break;
            case "HandDryer":
                B(p,"Body",0,.2f,0,.36f,.4f,.2f,white,true);B(p,"Air outlet",0,.02f,-.06f,.25f,.035f,.09f,dark);
                B(p,"Sensor",0,.09f,-.105f,.05f,.03f,.015f,glass);break;
            case "PaperDispenser":
                B(p,"Case",0,.19f,0,.34f,.38f,.16f,steel,true);B(p,"Window",0,.21f,-.084f,.06f,.16f,.012f,glass);
                B(p,"Towel",0,-.025f,-.04f,.23f,.12f,.015f,paper);break;
            case "SoapDispenser":Soap(p);break;
            case "SanitaryBin":
                B(p,"Bin",0,.28f,0,.24f,.56f,.35f,white,true);B(p,"Lid",0,.58f,0,.25f,.045f,.36f,sage);
                B(p,"Pedal",0,.045f,-.2f,.15f,.035f,.08f,steel);break;
            case "CleaningStation":
            case "CleaningHideCabinet":CleaningCabinet(p,name=="CleaningStation");break;
            case "DryingStation":
                B(p,"Shared mounting panel",0,.25f,.09f,.84f,.7f,.035f,white,true);
                Build(Child(p,"Hand dryer",new Vector3(-.22f,0,0)),"HandDryer");
                Build(Child(p,"Paper towels",new Vector3(.22f,.04f,.02f)),"PaperDispenser");break;
            case "CleaningBucket":
                C(p,"Bucket",new Vector3(0,.2f,0),.22f,.4f,blue,Vector3.zero,.8f);
                C(p,"Bucket opening",new Vector3(0,.401f,0),.185f,.012f,dark,Vector3.zero);
                foreach(float x in new[]{-.22f,.22f})R(p,"Handle side",new Vector3(x,.25f,0),new Vector3(x,.57f,0),.012f,steel);
                R(p,"Handle grip",new Vector3(-.22f,.57f,0),new Vector3(.22f,.57f,0),.018f,dark);break;
            case "CleaningSupplies":
                for(int i=0;i<3;i++){var at=new Vector3(-.2f+i*.2f,.16f,0);C(p,"Cleaning bottle",at,.065f,.32f,i==0?blue:i==1?sage:white,Vector3.zero);B(p,"Bottle cap",at.x,.34f,0,.055f,.06f,.055f,dark);}
                B(p,"Cloth stack",0,.035f,-.2f,.4f,.07f,.24f,paper);break;
            case "CautionSign":
                foreach(float s in new[]{-1f,1f}){var b=StaffRoomModels.Box(p,"Folding panel",new Vector3(0,.42f,s*.19f),new Vector3(.48f,.84f,.035f),yellow,false);b.transform.localEulerAngles=new Vector3(s*24,0,0);}
                B(p,"Warning",0,.46f,-.205f,.28f,.26f,.02f,dark);break;
            case "VentGrille":
                B(p,"Frame",0,.15f,0,.65f,.3f,.05f,white);
                for(int i=0;i<6;i++)B(p,"Louvre",0,.035f+i*.047f,-.028f,.57f,.022f,.014f,dark);break;
            case "LobbyBench":Bench(p);break;
            case "DrinksVending":Vending(p,false);break;
            case "SnacksVending":Vending(p,true);break;
            case "ServiceCounter":Counter(p);break;
            case "ParcelShelf":Parcels(p);break;
            case "BagLockers":Lockers(p);break;
            case "UmbrellaStand":Umbrellas(p);break;
            case "SanitiserStand":
                B(p,"Foot",0,.025f,0,.45f,.05f,.45f,dark,true);B(p,"Stem",0,.62f,.05f,.08f,1.2f,.08f,steel);
                B(p,"Plate",0,1.17f,.02f,.28f,.4f,.045f,sage);Soap(Child(p,"Sanitiser",new Vector3(0,1,-.07f)));
                B(p,"Sign",0,1.58f,.04f,.35f,.25f,.035f,white);break;
            case "BasketStack":Baskets(p);break;
            case "Trolley":Trolley(p);break;
            case "TrolleyCorral":Corral(p);break;
            case "SecurityGate":
                B(p,"Foot",0,.04f,0,.32f,.08f,.65f,dark,true);
                foreach(float z in new[]{-.23f,.23f})B(p,"Upright",0,1.04f,z,.11f,2,.09f,white,true);
                B(p,"Top",0,2.02f,0,.11f,.09f,.55f,white);B(p,"Status",0,2.08f,0,.115f,.025f,.16f,red);
                B(p,"Antenna",0,1.05f,0,.07f,1.3f,.35f,sage);break;
            case "Directory":
                B(p,"Frame",0,.9f,0,2f,1.8f,.09f,dark);B(p,"Board",0,.9f,-.051f,1.86f,1.66f,.022f,white);
                B(p,"Header",0,1.57f,-.065f,1.72f,.24f,.012f,sage);break;
            case "QueuePost":
                C(p,"Base",new Vector3(0,.035f,0),.24f,.07f,dark,Vector3.zero);
                C(p,"Post",new Vector3(0,.5f,0),.035f,.94f,steel,Vector3.zero);
                C(p,"Belt head",new Vector3(0,1,0),.07f,.15f,dark,Vector3.zero);break;
            case "LobbyPlanter":Planter(p);break;
            case "ReturnCart":
                foreach(float x in new[]{-.48f,.48f})foreach(float z in new[]{-.32f,.32f})C(p,"Wheel",new Vector3(x,.1f,z),.1f,.05f,dark,new Vector3(0,0,90));
                foreach(float y in new[]{.25f,.95f})B(p,"Tray",0,y,0,1.05f,.07f,.7f,sage,true);
                foreach(float x in new[]{-.49f,.49f})foreach(float z in new[]{-.31f,.31f})R(p,"Post",new Vector3(x,.2f,z),new Vector3(x,1.15f,z),.025f,steel);
                R(p,"Handle",new Vector3(-.49f,1.15f,-.31f),new Vector3(.49f,1.15f,-.31f),.03f,red);break;
            case "EntryMat":
                B(p,"Rubber border",0,.006f,0,3.5f,.012f,2.1f,dark);
                for(int i=0;i<22;i++)B(p,"Rib",-1.64f+i*.156f,.014f,0,.105f,.007f,1.94f,sage);break;
        }
    }

    static void Cabin(Transform p)
    {
        // 2.65 x 3.4m gross; 2.49 x 3.24m inside. Inward 1.5m door clears the toilet.
        B(p,"Back",0,1.35f,1.66f,2.65f,2.7f,.08f,white,true);
        foreach(float x in new[]{-1.285f,1.285f})B(p,"Side",x,1.35f,0,.08f,2.7f,3.4f,white,true);
        B(p,"Hinge jamb",-1.1175f,1.35f,-1.66f,.415f,2.7f,.08f,sage,true);
        B(p,"Latch return",.9575f,1.35f,-1.66f,.735f,2.7f,.08f,sage,true);
        B(p,"Lintel",-.16f,2.58f,-1.66f,1.5f,.24f,.08f,sage,true);
        var door=StaffRoomModels.Door(p,"toilet cabin",new Vector3(-.91f,0,-1.66f),1.5f,2.4f,.035f,sage,-95f);
        B(door,"Number plate",.75f,1.9f,-.041f,.35f,.18f,.018f,white);
        B(door,"Privacy latch",1.3f,1,-.05f,.08f,.08f,.035f,steel);
        Toilet(Child(p,"Toilet",new Vector3(.33f,0,1.015f)));
        B(p,"Paper holder",1.2f,.8f,.55f,.09f,.18f,.22f,steel);
        C(p,"Toilet roll",new Vector3(1.12f,.8f,.55f),.085f,.15f,paper,new Vector3(0,0,90));
        B(p,"Bag shelf",-.7f,1.1f,1.46f,.65f,.06f,.3f,oak);
        B(p,"Coat hook",-.85f,1.85f,1.56f,.05f,.13f,.12f,steel);
        Build(Child(p,"Sanitary bin",new Vector3(.99f,0,1.15f)),"SanitaryBin");
    }
    static void Toilet(Transform p)
    {
        var foot=C(p,"Pedestal",new Vector3(0,.18f,-.05f),.21f,.36f,ceramic,Vector3.zero,.7f);foot.transform.localScale=new Vector3(1,1,1.35f);
        var bowl=C(p,"Bowl",new Vector3(0,.38f,-.12f),.33f,.2f,ceramic,Vector3.zero,.7f);bowl.transform.localScale=new Vector3(1,1,1.35f);
        var water=C(p,"Bowl recess",new Vector3(0,.485f,-.14f),.235f,.012f,dark,Vector3.zero);water.transform.localScale=new Vector3(1,1,1.4f);
        // Faceted open seat, instead of a solid disc covering the bowl.
        for(int i=0;i<12;i++)
        {
            float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;
            R(p,"Seat rim",new Vector3(Mathf.Cos(a)*.29f,.51f,Mathf.Sin(a)*.4f-.12f),new Vector3(Mathf.Cos(b)*.29f,.51f,Mathf.Sin(b)*.4f-.12f),.035f,ceramic);
        }
        B(p,"Cistern",0,.71f,.46f,.63f,.56f,.29f,ceramic,true);B(p,"Cistern lid",0,1,.46f,.66f,.045f,.32f,ceramic);
        B(p,"Flush button",0,1.026f,.46f,.1f,.015f,.06f,steel);
        var collider=p.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,.27f,-.03f);collider.size=new Vector3(.66f,.54f,1.1f);
    }
    static void Vanity(Transform p)
    {
        B(p,"Double base",0,.4f,0,3.6f,.8f,1.6f,sage,true);
        B(p,"Counter",0,.85f,0,3.7f,.1f,1.7f,ceramic,true);
        B(p,"Mirror spine",0,1.65f,0,3.5f,1.35f,.09f,steel,true);
        foreach(float side in new[]{-1f,1f})
        {
            B(p,"Mirror",0,1.68f,side*.055f,3.36f,1.2f,.014f,blue,true);
            for(int i=0;i<3;i++)
            {
                float x=-1.2f+i*1.2f,z=side*.46f;
                B(p,"Basin recess",x,.906f,z,.71f,.012f,.44f,blue);
                B(p,"Basin front rim",x,.93f,z+side*.25f,.85f,.06f,.08f,ceramic);
                B(p,"Basin rear rim",x,.93f,z-side*.25f,.85f,.06f,.08f,ceramic);
                foreach(float dx in new[]{-.385f,.385f})B(p,"Basin side rim",x+dx,.93f,z,.08f,.06f,.44f,ceramic);
                R(p,"Tap stem",new Vector3(x,.9f,side*.16f),new Vector3(x,1.17f,side*.16f),.025f,steel);
                R(p,"Tap spout",new Vector3(x,1.17f,side*.16f),new Vector3(x,1.17f,side*.35f),.025f,steel);
                var soap=Child(p,"Soap",new Vector3(x+.48f,.91f,side*.19f));soap.localRotation=Quaternion.Euler(0,side<0?0:180,0);Soap(soap);
            }
        }
    }
    static void Soap(Transform p)
    {
        B(p,"Dispenser",0,.13f,0,.13f,.26f,.105f,white);B(p,"Window",0,.13f,-.058f,.05f,.1f,.012f,blue);
        B(p,"Nozzle",0,.015f,-.07f,.045f,.03f,.07f,dark);
    }
    static void CleaningCabinet(Transform p,bool stocked)
    {
        foreach(float x in new[]{-.71f,.71f})B(p,"Side",x,1.25f,0,.08f,2.5f,1.4f,white,true);
        B(p,"Back",0,1.25f,.66f,1.34f,2.5f,.08f,white,true);
        B(p,"Top",0,2.46f,0,1.34f,.08f,1.4f,white,true);
        B(p,"Low threshold",0,.035f,0,1.34f,.07f,1.4f,white,true);
        var hinge=StaffRoomModels.Door(p,stocked?"cleaning supplies cupboard":"empty cupboard",new Vector3(-.66f,0,-.715f),1.32f,2.32f,.08f,sage,90);
        if(stocked)
        {
            foreach(float y in new[]{.65f,1.15f,1.65f})B(p,"Supplies shelf",.31f,y,0,.65f,.04f,1.2f,white,true);
            B(p,"Tool partition",-.05f,1.25f,.1f,.04f,2.35f,1.1f,white,true);
            Build(Child(p,"Bucket",new Vector3(.31f,.07f,-.1f)),"CleaningBucket");
            foreach(float y in new[]{.67f,1.17f,1.67f})Build(Child(p,"Supplies",new Vector3(.3f,y,.1f)),"CleaningSupplies");
        }
    }
    static void Bench(Transform p)
    {
        for(int i=0;i<5;i++)B(p,"Seat slat",0,.49f,-.28f+i*.13f,3.2f,.065f,.1f,oak,true);
        for(int i=0;i<4;i++)B(p,"Back slat",0,.7f+i*.12f,.32f,3.2f,.09f,.065f,oak,true);
        foreach(float x in new[]{-1.3f,1.3f})
        {
            B(p,"Leg",x,.23f,0,.08f,.46f,.6f,dark,true);B(p,"Arm",x,.72f,-.03f,.07f,.06f,.6f,dark);
            B(p,"Back support",x,.75f,.34f,.06f,.75f,.06f,dark);
        }
    }
    static void Vending(Transform p,bool snacks)
    {
        B(p,"Case",0,1.15f,0,1.35f,2.3f,.85f,snacks?sage:red,true);
        B(p,"Display surround",-.12f,1.4f,-.435f,1.02f,1.45f,.035f,dark);
        B(p,"Dark interior",-.12f,1.4f,-.456f,.94f,1.35f,.014f,glass);
        for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            float xx=-.47f+x*.235f,yy=.88f+y*.32f;Material m=(x+y)%3==0?red:(x+y)%3==1?blue:yellow;
            if(snacks)B(p,"Snack packet",xx,yy,-.49f,.15f,.23f,.05f,m);
            else{C(p,"Bottle",new Vector3(xx,yy,-.49f),.055f,.2f,m,Vector3.zero);C(p,"Cap",new Vector3(xx,yy+.113f,-.49f),.032f,.026f,white,Vector3.zero);}
        }
        B(p,"Payment panel",.51f,1.15f,-.443f,.22f,.6f,.04f,steel);B(p,"Card reader",.51f,1.3f,-.47f,.12f,.13f,.025f,glass);
        B(p,"Pickup recess",-.08f,.34f,-.44f,.94f,.29f,.035f,dark);B(p,"Top sign",0,2.19f,-.44f,1.15f,.16f,.025f,white);
    }
    static void Counter(Transform p)
    {
        B(p,"Base",0,.51f,0,5f,1.02f,.9f,sage,true);B(p,"Top",0,1.08f,0,5.1f,.12f,1f,oak,true);
        B(p,"Customer stripe",0,.7f,-.46f,4.8f,.09f,.025f,white);
        B(p,"Return extension",-2.15f,.41f,-.7f,.7f,.82f,.5f,sage,true);B(p,"Lower counter",-2.15f,.85f,-.7f,.8f,.08f,.6f,oak,true);
        B(p,"Terminal foot",.8f,1.16f,0,.3f,.05f,.25f,dark);B(p,"Terminal",.8f,1.4f,.08f,.55f,.4f,.065f,dark);
        B(p,"Screen",.8f,1.4f,.119f,.49f,.33f,.015f,glass);B(p,"Card reader",.8f,1.19f,-.28f,.14f,.1f,.22f,dark);
        C(p,"Service bell",new Vector3(-.4f,1.17f,-.25f),.08f,.06f,steel,Vector3.zero);
    }
    static void Parcels(Transform p)
    {
        B(p,"Back",0,1.2f,.29f,3.4f,2.4f,.04f,sage,true);
        foreach(float x in new[]{-1.67f,1.67f})B(p,"Side",x,1.2f,0,.06f,2.4f,.6f,sage,true);
        for(int y=0;y<4;y++)
        {
            float yy=.1f+y*.58f;B(p,"Shelf",0,yy,0,3.4f,.05f,.6f,oak,true);
            for(int x=0;x<4;x++){float xx=-1.2f+x*.8f;B(p,"Parcel",xx,yy+.22f,0,.57f,.39f,.46f,oak);B(p,"Shipping label",xx,yy+.24f,-.235f,.2f,.12f,.012f,paper);}
        }
    }
    static void Lockers(Transform p)
    {
        B(p,"Case",0,1.2f,0,3.6f,2.4f,.6f,white,true);
        // Customer bag lockers are visual props; the large staff lockers remain interactable.
        for(int x=0;x<6;x++)for(int y=0;y<2;y++)
        {float xx=-1.5f+x*.6f,yy=.62f+y*1.16f;B(p,"Door",xx,yy,-.31f,.575f,1.12f,.025f,sage);B(p,"Number plate",xx,yy+.3f,-.33f,.18f,.09f,.015f,white);B(p,"Lock",xx+.18f,yy,-.345f,.04f,.1f,.035f,steel);}
    }
    static void Umbrellas(Transform p)
    {
        B(p,"Drip tray",0,.04f,0,.65f,.08f,.45f,dark,true);
        foreach(float x in new[]{-.29f,.29f})B(p,"End",x,.32f,0,.06f,.6f,.45f,steel,true);
        for(int i=0;i<4;i++)
        {float x=-.21f+i*.14f;R(p,"Umbrella",new Vector3(x,.08f,0),new Vector3(x,.88f,0),.04f,i%2==0?dark:blue);R(p,"Handle",new Vector3(x,.88f,0),new Vector3(x+.08f,.88f,0),.022f,oak);}
    }
    static void Baskets(Transform p)
    {
        B(p,"Stand",0,.08f,0,.85f,.16f,.65f,dark,true);
        for(int i=0;i<7;i++)
        {
            float y=.18f+i*.1f;B(p,"Base",0,y,0,.68f,.035f,.47f,red);
            foreach(float x in new[]{-.36f,.36f})B(p,"Side",x,y+.12f,0,.035f,.24f,.52f,red);
            foreach(float z in new[]{-.25f,.25f})B(p,"End",0,y+.12f,z,.72f,.24f,.035f,red);
        }
        var c=p.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,.55f,0);c.size=new Vector3(.8f,1.1f,.6f);
    }
    static void Trolley(Transform p)
    {
        foreach(float x in new[]{-.36f,.36f})foreach(float z in new[]{-.48f,.48f})C(p,"Caster",new Vector3(x,.1f,z),.1f,.06f,dark,new Vector3(0,0,90));
        foreach(float x in new[]{-.35f,.35f})
        {
            R(p,"Lower frame",new Vector3(x,.21f,-.5f),new Vector3(x,.21f,.5f),.025f,steel);
            R(p,"Handle support",new Vector3(x,.21f,.5f),new Vector3(x,1.1f,.55f),.025f,steel);
            for(int z=0;z<7;z++)R(p,"Basket wire",new Vector3(x*.8f,.57f,-.4f+z*.14f),new Vector3(x,.99f,-.45f+z*.16f),.009f,steel);
            R(p,"Rim",new Vector3(x,.99f,-.45f),new Vector3(x,.99f,.51f),.022f,steel);
        }
        foreach(float z in new[]{-.45f,.51f})
        {
            R(p,"End rim",new Vector3(-.35f,.99f,z),new Vector3(.35f,.99f,z),.022f,steel);
            for(int x=0;x<7;x++)R(p,"End wire",new Vector3(-.28f+x*.093f,.57f,z*.9f),new Vector3(-.35f+x*.1167f,.99f,z),.009f,steel);
        }
        R(p,"Red handle",new Vector3(-.39f,1.1f,.55f),new Vector3(.39f,1.1f,.55f),.035f,red);
        B(p,"Basket bottom",0,.57f,.02f,.58f,.025f,.8f,steel);
        var c=p.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,.58f,.02f);c.size=new Vector3(.8f,1.16f,1.2f);
    }
    static void Corral(Transform p)
    {
        // Two lanes, 1.4m each, open at -Z. Nested trolleys are placed by the room builder.
        foreach(float x in new[]{-1.4f,0,1.4f})
        {
            foreach(float z in new[]{-1.65f,1.65f})R(p,"Post",new Vector3(x,.02f,z),new Vector3(x,.95f,z),.04f,steel);
            R(p,"Rail",new Vector3(x,.95f,-1.65f),new Vector3(x,.95f,1.65f),.04f,steel);
            var collision=Child(p,"Rail collision",new Vector3(x,.5f,0));
            collision.gameObject.AddComponent<BoxCollider>().size=new Vector3(.08f,1f,3.4f);
        }
        B(p,"Rear rail",0,.85f,1.65f,2.8f,.08f,.08f,steel,true);
    }
    static void Planter(Transform p)
    {
        B(p,"Planter",0,.3f,0,2.4f,.6f,.65f,white,true);B(p,"Soil",0,.606f,0,2.24f,.025f,.5f,dark);
        Material green=Shared("Leaf Dark"),light=Shared("Leaf Light");
        for(int i=0;i<7;i++)
        {
            float x=-1f+i*.33f;R(p,"Stem",new Vector3(x,.6f,0),new Vector3(x,1.12f,0),.018f,oak);
            for(int j=0;j<3;j++){var leaf=C(p,"Faceted leaf",new Vector3(x+(j-1)*.09f,.84f+j*.1f,(j%2==0?1:-1)*.12f),.15f,.035f,j%2==0?green:light,new Vector3(35,j*70,20));leaf.transform.localScale=new Vector3(1,1,1.6f);}
        }
    }
}
