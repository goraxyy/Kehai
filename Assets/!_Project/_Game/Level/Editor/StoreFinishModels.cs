using System.IO;
using UnityEditor;
using UnityEngine;

// Shared metre-scale models for the shop and service exterior. Assets are generated locally.
public static class StoreFinishModels
{
    public const string Folder="Assets/!_Project/_Art/Generated/StoreFinish";
    public static readonly string[] Names={"Checkout","PublicBin","PalletStack","CardboardBales","UtilityCabinet",
        "ServiceCage","HVAC","Downpipe","WallLamp","Bollard","BikeRack","PosterFrame","ServiceWorkbench","Flashlight","Mop"};
    static Material grey,dark,light,oak,metal,red,glass,green,paper;

    public static void BuildLibrary()
    {
        if(StaffRoomModels.Prefab("Locker")==null)StaffRoomModels.BuildLibrary();
        Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Prefabs");AssetDatabase.Refresh();
        grey=StaffRoomModels.Material("Retail Grey",new Color(.48f,.5f,.51f));
        light=StaffRoomModels.Material("Shelf Enamel",new Color(.65f,.67f,.66f));
        dark=Shared("Graphite");oak=Shared("Light Oak");metal=Shared("Brushed Steel");red=Shared("Safety Red");
        glass=Shared("Dark Screen");green=Shared("Sage Enamel");paper=Shared("Paper");
        foreach(string name in Names)StaffRoomModels.BuildCustom(name,Folder,p=>Build(p,name));
        CheckoutRack();
        AssetDatabase.SaveAssets();
    }
    static Material Shared(string n)=>AssetDatabase.LoadAssetAtPath<Material>(StaffRoomModels.Folder+"/Materials/"+n+".mat");
    public static GameObject Prefab(string n)=>AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Prefabs/"+n+".prefab");
    static void B(Transform p,string n,float x,float y,float z,float w,float h,float d,Material m,bool solid=false)
        =>StaffRoomModels.Box(p,n,new Vector3(x,y,z),new Vector3(w,h,d),m,solid,.014f);
    static void C(Transform p,string n,Vector3 at,float r,float h,Material m,Vector3 rot)
        =>StaffRoomModels.Cylinder(p,n,at,r,h,m,rot);
    static void R(Transform p,string n,Vector3 a,Vector3 b,float radius,Material m)=>StaffRoomModels.Rod(p,n,a,b,radius,m);
    static void Build(Transform p,string name)
    {
        switch(name)
        {
            case "Checkout":Checkout(p);break;
            case "PublicBin":Bin(p);break;
            case "Flashlight":
                // +X follows the existing beam and held-item orientation.
                C(p,"Body",new Vector3(-.025f,.073f,0),.043f,.23f,dark,new Vector3(0,0,90));
                for(int i=0;i<6;i++)C(p,"Grip ring",new Vector3(-.1f+i*.026f,.073f,0),.046f,.012f,grey,new Vector3(0,0,90));
                C(p,"Tail cap",new Vector3(-.15f,.073f,0),.048f,.025f,metal,new Vector3(0,0,90));
                C(p,"Lamp head",new Vector3(.11f,.073f,0),.073f,.085f,dark,new Vector3(0,0,90));
                C(p,"Lens bezel",new Vector3(.155f,.073f,0),.069f,.016f,metal,new Vector3(0,0,90));
                C(p,"Lens",new Vector3(.165f,.073f,0),.057f,.006f,paper,new Vector3(0,0,90));
                B(p,"Switch",.035f,.12f,0,.038f,.016f,.025f,red);
                break;
            case "Mop":
                B(p,"Fabric pad",0,.023f,0,.48f,.046f,.2f,paper);
                for(int i=0;i<13;i++)B(p,"Fabric edge",-.222f+i*.037f,.018f,-.106f,.027f,.036f,.035f,paper);
                B(p,"Head plate",0,.057f,0,.45f,.03f,.17f,green);
                C(p,"Swivel joint",new Vector3(0,.102f,0),.035f,.09f,dark,new Vector3(0,0,90));
                C(p,"Aluminium shaft",new Vector3(0,.99f,0),.014f,1.77f,metal,Vector3.zero);
                C(p,"Grip",new Vector3(0,1.78f,0),.022f,.25f,dark,Vector3.zero);
                C(p,"End cap",new Vector3(0,1.92f,0),.027f,.035f,green,Vector3.zero);
                break;
            case "PalletStack":
                for(int y=0;y<3;y++)
                {
                    float h=y*.16f;
                    foreach(float x in new[]{-.48f,0,.48f})B(p,"Runner",x,h+.04f,0,.12f,.08f,1.2f,oak);
                    for(int i=0;i<6;i++)B(p,"Slat",0,h+.12f,-.5f+i*.2f,1.2f,.08f,.16f,oak);
                }
                Collider(p,new Vector3(0,.24f,0),new Vector3(1.2f,.48f,1.2f));break;
            case "CardboardBales":
                for(int i=0;i<3;i++)
                {
                    float x=-.72f+i*.72f;B(p,"Bale",x,.65f,0,.7f,1.3f,.9f,oak,true);
                    for(int j=0;j<10;j++)B(p,"Folded edges",x,.08f+j*.12f,-.455f,.65f,.013f,.01f,dark);
                    foreach(float dx in new[]{-.22f,.22f})B(p,"Binding",x+dx,.65f,-.465f,.035f,1.3f,.02f,green);
                }break;
            case "UtilityCabinet":
                B(p,"Case",0,1.1f,0,1.6f,2.2f,.7f,grey,true);
                foreach(float x in new[]{-.4f,.4f})
                {B(p,"Door",x,1.1f,-.365f,.77f,2.05f,.035f,dark);B(p,"Handle",x*.2f,1.05f,-.4f,.04f,.28f,.04f,metal);}
                B(p,"Danger label",0,1.65f,-.391f,.32f,.25f,.018f,paper);break;
            case "ServiceCage":Cage(p);break;
            case "HVAC":
                B(p,"Housing",0,.65f,0,2.4f,1.1f,1.8f,grey,true);
                foreach(float x in new[]{-.7f,.7f})
                {
                    C(p,"Fan grille",new Vector3(x,1.22f,0),.48f,.04f,dark,Vector3.zero);
                    for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f;R(p,"Fan blade",new Vector3(x,1.25f,0),new Vector3(x+Mathf.Cos(a)*.37f,1.25f,Mathf.Sin(a)*.37f),.055f,metal);}
                }
                foreach(float x in new[]{-.9f,.9f})B(p,"Foot",x,.07f,0,.18f,.14f,1.7f,dark,true);
                for(int i=0;i<10;i++)B(p,"Vent",0,.3f+i*.075f,-.909f,2.1f,.025f,.018f,dark);break;
            case "Downpipe":
                C(p,"Pipe",new Vector3(0,4,0),.065f,8,grey,Vector3.zero);
                foreach(float y in new[]{.4f,3.7f,7.5f})B(p,"Wall bracket",0,y,.06f,.2f,.1f,.12f,dark);
                R(p,"Outlet",new Vector3(0,.12f,0),new Vector3(0,.12f,-.25f),.065f,grey);break;
            case "WallLamp":
                B(p,"Back plate",0,.12f,.08f,.35f,.24f,.05f,dark);
                B(p,"Housing",0,.17f,0,.45f,.24f,.23f,dark);B(p,"Diffuser",0,.04f,-.02f,.38f,.02f,.17f,paper);break;
            case "Bollard":
                C(p,"Post",new Vector3(0,.5f,0),.11f,1,dark,Vector3.zero);C(p,"Reflector",new Vector3(0,.83f,0),.113f,.1f,paper,Vector3.zero);
                Collider(p,new Vector3(0,.5f,0),new Vector3(.22f,1,.22f));break;
            case "BikeRack":
                for(int i=0;i<5;i++)
                {float x=-2f+i;R(p,"Left leg",new Vector3(x-.35f,0,0),new Vector3(x-.35f,.85f,0),.035f,metal);R(p,"Top",new Vector3(x-.35f,.85f,0),new Vector3(x+.35f,.85f,0),.035f,metal);R(p,"Right leg",new Vector3(x+.35f,.85f,0),new Vector3(x+.35f,0,0),.035f,metal);}
                Collider(p,new Vector3(0,.425f,0),new Vector3(4.8f,.85f,.1f));break;
            case "PosterFrame":
                B(p,"Case",0,.95f,0,1.4f,1.9f,.12f,dark);B(p,"Poster",0,.95f,-.067f,1.25f,1.75f,.02f,paper);break;
            case "ServiceWorkbench":
                B(p,"Top",0,.95f,0,3.2f,.1f,.85f,oak,true);B(p,"Case",-.9f,.45f,0,1.1f,.9f,.8f,grey,true);
                foreach(float x in new[]{.4f,1.45f})B(p,"Leg",x,.45f,0,.08f,.9f,.7f,dark,true);
                for(int i=0;i<3;i++){B(p,"Drawer",-.9f,.18f+i*.26f,-.41f,.99f,.23f,.03f,dark);B(p,"Pull",-.9f,.22f+i*.26f,-.44f,.35f,.025f,.03f,metal);}break;
        }
    }
    static void Checkout(Transform p)
    {
        // Wider belt case; leave the existing impulse rack on its original -X side.
        B(p,"Belt cabinet",.55f,.44f,0,1.1f,.88f,5,dark,true);
        B(p,"Counter rim",.55f,.94f,0,1.1f,.12f,5,oak,true);
        B(p,"Conveyor",.55f,1.009f,-.95f,.87f,.018f,2.7f,glass);
        foreach(float x in new[]{.06f,1.04f})B(p,"Steel rail",x,1.045f,-.95f,.045f,.07f,2.8f,metal);
        for(int i=0;i<12;i++)B(p,"Belt seam",.55f,1.02f,-2.18f+i*.22f,.82f,.003f,.008f,grey);
        B(p,"Bagging cabinet",-.2f,.44f,2,2.6f,.88f,1,dark,true);
        B(p,"Bagging worktop",-.2f,.94f,2,2.6f,.12f,1,oak,true);
        B(p,"Scanner",.55f,1.016f,.7f,.6f,.025f,.5f,glass);
        B(p,"Scan line",.55f,1.032f,.7f,.48f,.004f,.015f,red);
        B(p,"POS foot",.4f,1.03f,1.2f,.3f,.06f,.28f,dark);
        B(p,"POS stem",.4f,1.22f,1.2f,.055f,.35f,.055f,metal);
        B(p,"POS monitor",.4f,1.46f,1.2f,.07f,.42f,.58f,dark);
        B(p,"POS screen",.359f,1.46f,1.2f,.014f,.34f,.5f,glass);
        B(p,"Card reader",1.02f,1.11f,1,.12f,.19f,.2f,dark);
        B(p,"Receipt printer",.1f,1.13f,1.65f,.25f,.26f,.29f,grey);
        B(p,"Receipt",.1f,1.27f,1.59f,.1f,.012f,.16f,paper);
        B(p,"Red fascia",1.107f,.66f,0,.015f,.12f,4.7f,red);
        B(p,"Bag rack",-.9f,1.02f,2,.45f,.04f,.5f,metal);
    }
    static void Bin(Transform p)
    {
        B(p,"Body",0,.48f,0,.7f,.96f,.6f,dark,true);
        B(p,"Inset door",0,.48f,-.311f,.58f,.79f,.025f,grey);
        B(p,"Opening",0,.97f,0,.48f,.015f,.38f,glass);
        foreach(float x in new[]{-.31f,.31f})B(p,"Lid side",x,1,0,.08f,.08f,.6f,metal);
        foreach(float z in new[]{-.26f,.26f})B(p,"Lid end",0,1,z,.54f,.08f,.08f,metal);
        B(p,"Label",0,.68f,-.329f,.28f,.16f,.012f,paper);
        B(p,"Pull",.22f,.43f,-.335f,.028f,.18f,.03f,metal);
    }
    static void Cage(Transform p)
    {
        B(p,"Floor",0,.06f,0,2.4f,.12f,1.2f,dark,true);
        foreach(float x in new[]{-1.15f,1.15f})foreach(float z in new[]{-.55f,.55f})B(p,"Post",x,1.1f,z,.07f,2.2f,.07f,metal);
        for(int i=0;i<13;i++)B(p,"Back wire",-1.1f+i*.1833f,1.1f,.55f,.015f,2.1f,.015f,metal);
        foreach(float x in new[]{-1.15f,1.15f})for(int i=0;i<7;i++)B(p,"Side wire",x,1.1f,-.5f+i*.1667f,.015f,2.1f,.015f,metal);
        for(int i=0;i<8;i++)
        {
            float y=.2f+i*.27f;B(p,"Cross wire",0,y,.55f,2.3f,.015f,.015f,metal);
            foreach(float x in new[]{-1.15f,1.15f})B(p,"Side cross wire",x,y,0,.015f,.015f,1.1f,metal);
        }
        Collider(p,new Vector3(0,1.1f,0),new Vector3(2.4f,2.2f,1.2f));
    }
    static void Collider(Transform p,Vector3 centre,Vector3 size)
    {var c=p.gameObject.AddComponent<BoxCollider>();c.center=centre;c.size=size;}
    static void CheckoutRack()
    {
        string path=Folder+"/Prefabs/Checkout.prefab";var go=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var rack=new GameObject("Checkout impulse shelf");rack.transform.SetParent(go.transform,false);
            rack.transform.localPosition=new Vector3(.35f,1.05f,-.95f);rack.transform.localRotation=Quaternion.Euler(0,-90,0);
            rack.AddComponent<ShelfUnit>();
            ColdStoreBuilder.Primitive(rack.transform,"Wall",new Vector3(0,.4f,.16f),new Vector3(2,.8f,.04f),grey,true);
            for(int i=0;i<3;i++)
            {
                float y=.08f+i*.27f;
                ColdStoreBuilder.Primitive(rack.transform,"Shelf deck "+(i+1),new Vector3(0,y,0),new Vector3(2,.025f,.35f),light,true);
                ColdStoreBuilder.Primitive(rack.transform,"Pricetag "+(i+1),new Vector3(0,y+.01f,-.175f),new Vector3(2,.07f,.015f),paper,false);
            }
            PrefabUtility.SaveAsPrefabAsset(go,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(go);}
    }
}
