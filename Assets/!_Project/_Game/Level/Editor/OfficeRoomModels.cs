using System.IO;
using UnityEditor;
using UnityEngine;

public static class OfficeRoomModels
{
    public const string Folder="Assets/!_Project/_Art/Generated/Office";
    public static readonly string[] Names={"ManagerDesk","OfficeChair","SecurityConsole","BriefingTable",
        "FilingCabinet","TallCabinet","OfficeSafe","Credenza","PrinterCabinet","Printer","Monitor",
        "Keyboard","DeskLamp","Laptop","DocumentTray","Binders","PaperStack","WaterDispenser","SecurityCamera"};
    static Material oak,ivory,sage,dark,metal,screen,red,paper,blue;

    [MenuItem("Kehai/Office/1. Build Model Library")]
    public static void BuildLibrary()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new System.InvalidOperationException("Build in edit mode.");
        if(StaffRoomModels.Prefab("Locker")==null)StaffRoomModels.BuildLibrary();
        Directory.CreateDirectory(Folder+"/Meshes");Directory.CreateDirectory(Folder+"/Prefabs");AssetDatabase.Refresh();
        oak=Shared("Light Oak");ivory=Shared("Warm White");sage=Shared("Sage Enamel");dark=Shared("Graphite");
        metal=Shared("Brushed Steel");red=Shared("Safety Red");paper=Shared("Paper");screen=Shared("Dark Screen");
        blue=StaffRoomModels.Material("Water Blue",new Color(.36f,.58f,.64f));
        foreach(string name in Names)StaffRoomModels.BuildCustom(name,Folder,p=>Build(p,name));
        AssetDatabase.SaveAssets();
    }
    static Material Shared(string name)=>AssetDatabase.LoadAssetAtPath<Material>(StaffRoomModels.Folder+"/Materials/"+name+".mat");
    public static GameObject Prefab(string name)=>AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Prefabs/"+name+".prefab");

    static void B(Transform p,string name,float x,float y,float z,float w,float h,float d,Material mat,bool solid=false)
        =>StaffRoomModels.Box(p,name,new Vector3(x,y,z),new Vector3(w,h,d),mat,solid,.012f);
    static void C(Transform p,string name,Vector3 at,float r,float h,Material m,Vector3 euler)
        =>StaffRoomModels.Cylinder(p,name,at,r,h,m,euler);
    static void Rod(Transform p,string name,Vector3 a,Vector3 b,float r,Material m)=>StaffRoomModels.Rod(p,name,a,b,r,m);
    static Transform Child(Transform p,string name,Vector3 at)
    {var t=new GameObject(name).transform;t.SetParent(p,false);t.localPosition=at;return t;}

    static void Build(Transform p,string name)
    {
        switch(name)
        {
            case "ManagerDesk":
                Table(p,3.6f,1f,.8f);
                B(p,"Return worktop",-1.3f,.76f,-1.1f,1f,.08f,1.2f,oak,true);
                B(p,"Return pedestal",-1.3f,.36f,-1.1f,.9f,.72f,1.1f,sage,true);
                B(p,"Modesty panel",0,.45f,.38f,3.25f,.55f,.045f,sage,true);
                Drawers(p,-1.3f,.9f,3,-1.66f);
                break;
            case "OfficeChair":Chair(p);break;
            case "SecurityConsole":
                Table(p,3.6f,1f,.8f);
                for(int i=0;i<3;i++)for(int j=0;j<2;j++)
                {
                    var monitor=Child(p,"CCTV monitor",new Vector3(-1.12f+i*1.12f,.88f+j*.67f,.21f));
                    Monitor(monitor,false);
                }
                for(int i=0;i<2;i++)Keyboard(Child(p,"Keyboard",new Vector3(-.85f+i*1.7f,.8f,-.23f)));
                B(p,"Recorder",1.3f,.3f,.12f,.48f,.55f,.65f,dark,true);
                for(int i=0;i<3;i++)B(p,"Recorder status",1.15f+i*.075f,.42f,-.214f,.025f,.025f,.012f,sage);
                break;
            case "BriefingTable":Table(p,4.8f,1.5f,.8f);break;
            case "FilingCabinet":
                B(p,"Case",0,.68f,0,.85f,1.36f,.65f,sage,true);Drawers(p,0,.85f,4,-.337f);break;
            case "TallCabinet":
                B(p,"Case",0,1.1f,0,1.2f,2.2f,.6f,ivory,true);
                for(int i=0;i<2;i++){float x=i==0?-.3f:.3f;B(p,"Door",x,1.1f,-.31f,.585f,2.08f,.025f,sage);B(p,"Handle",x*.28f,1.1f,-.35f,.035f,.25f,.055f,dark);}break;
            case "OfficeSafe":Safe(p);break;
            case "Credenza":
                B(p,"Case",0,.4f,0,2.4f,.8f,.65f,ivory,true);B(p,"Oak top",0,.83f,0,2.4f,.06f,.65f,oak,true);
                for(int i=0;i<4;i++){float x=-.9f+i*.6f;B(p,"Door",x,.42f,-.337f,.58f,.74f,.025f,sage);B(p,"Pull",x,.66f,-.36f,.22f,.028f,.04f,dark);}break;
            case "PrinterCabinet":
                B(p,"Case",0,.4f,0,1.6f,.8f,.65f,sage,true);B(p,"Top",0,.83f,0,1.6f,.06f,.7f,oak,true);
                for(int i=0;i<2;i++){float x=i==0?-.4f:.4f;B(p,"Door",x,.4f,-.337f,.78f,.7f,.025f,sage);B(p,"Pull",x,.65f,-.37f,.18f,.025f,.04f,dark);}break;
            case "Printer":Printer(p);break;
            case "Monitor":Monitor(p,true);break;
            case "Keyboard":Keyboard(p);break;
            case "DeskLamp":Lamp(p);break;
            case "Laptop":
                B(p,"Base",0,.022f,0,.36f,.044f,.26f,metal);B(p,"Keys",0,.049f,-.015f,.29f,.012f,.17f,dark);
                B(p,"Display case",0,.16f,.115f,.36f,.28f,.028f,dark);B(p,"Screen",0,.16f,.097f,.32f,.23f,.012f,screen);break;
            case "DocumentTray":
                for(int i=0;i<3;i++){float y=.025f+i*.075f;B(p,"Tray",0,y,0,.36f,.02f,.27f,metal);foreach(float x in new[]{-.17f,.17f})B(p,"Side",x,y+.025f,0,.02f,.05f,.27f,dark);}
                B(p,"Documents",0,.19f,0,.29f,.015f,.21f,paper);break;
            case "Binders":Binders(p);break;
            case "PaperStack":
                for(int i=0;i<5;i++)B(p,"Paper ream",0,.01f+i*.018f,0,.3f,.016f,.22f,i%2==0?paper:ivory);break;
            case "WaterDispenser":Water(p);break;
            case "SecurityCamera":
                B(p,"Wall plate",0,.1f,.2f,.15f,.2f,.04f,dark);
                Rod(p,"Bracket",new Vector3(0,.1f,.18f),new Vector3(0,.05f,0),.028f,metal);
                B(p,"Housing",0,.12f,-.1f,.22f,.17f,.35f,ivory);
                C(p,"Lens",new Vector3(0,.12f,-.28f),.055f,.035f,screen,new Vector3(90,0,0));break;
        }
    }
    static void Table(Transform p,float w,float d,float h)
    {
        B(p,"Oak worktop",0,h-.04f,0,w,.08f,d,oak,true);
        foreach(float x in new[]{-w*.5f+.15f,w*.5f-.15f})
        {
            B(p,"Steel side",x,(h-.08f)*.5f,0,.08f,h-.08f,d-.15f,dark,true);
            B(p,"Foot",x,.04f,0,.25f,.08f,d-.05f,dark,true);
        }
        B(p,"Brace",0,.4f,0,w-.3f,.06f,.07f,dark);
    }
    static void Chair(Transform p)
    {
        B(p,"Seat cushion",0,.52f,0,.72f,.13f,.7f,dark,true);
        B(p,"Back",0,.97f,.29f,.7f,.8f,.12f,sage,true);
        foreach(float x in new[]{-.39f,.39f})
        {B(p,"Arm rest",x,.77f,0,.075f,.055f,.45f,dark);B(p,"Arm support",x,.62f,.08f,.04f,.3f,.04f,metal);}
        C(p,"Lift",new Vector3(0,.27f,0),.05f,.45f,metal,Vector3.zero);
        for(int i=0;i<5;i++)
        {
            float a=i*2*Mathf.PI/5;Vector3 tip=new Vector3(Mathf.Cos(a)*.4f,.09f,Mathf.Sin(a)*.4f);
            Rod(p,"Spoke",new Vector3(0,.16f,0),tip,.025f,dark);
            C(p,"Caster",tip-Vector3.up*.035f,.055f,.06f,dark,new Vector3(90,0,0));
        }
        var collider=p.gameObject.AddComponent<BoxCollider>();collider.center=new Vector3(0,.25f,0);collider.size=new Vector3(.82f,.5f,.82f);
    }
    static void Drawers(Transform p,float x,float width,int n,float front)
    {
        float h=n==4?.3f:.21f;
        for(int i=0;i<n;i++)
        {float y=.11f+h*.5f+i*h;B(p,"Drawer",x,y,front,width-.06f,h-.018f,.025f,sage);B(p,"Pull",x,y+h*.2f,front-.04f,.23f,.025f,.055f,dark);B(p,"Label",x,y-.05f,front-.018f,.15f,.055f,.008f,paper);}
    }
    static void Monitor(Transform p,bool stand)
    {
        float bottom=stand?.21f:0;
        if(stand){B(p,"Foot",0,.02f,0,.35f,.04f,.23f,dark);B(p,"Stem",0,.18f,.04f,.055f,.32f,.055f,metal);}
        B(p,"Bezel",0,bottom+.28f,0,.94f,.56f,.065f,dark);
        B(p,"Glass",0,bottom+.28f,-.038f,.87f,.49f,.014f,screen);
        // Abstract CCTV interface, not a claim that live camera feeds exist yet.
        for(int i=0;i<3;i++)B(p,"Feed stripe",-.25f,bottom+.16f+i*.1f,-.048f,.25f,.012f,.006f,sage);
        B(p,"Status",.37f,bottom+.06f,-.048f,.025f,.015f,.006f,red);
    }
    static void Keyboard(Transform p)
    {
        B(p,"Base",0,.018f,0,.45f,.036f,.18f,dark);
        for(int x=0;x<12;x++)for(int z=0;z<4;z++)B(p,"Key",-.195f+x*.035f,.044f,-.06f+z*.04f,.027f,.018f,.027f,ivory);
        B(p,"Mouse",.32f,.028f,0,.07f,.056f,.12f,dark);
    }
    static void Lamp(Transform p)
    {
        C(p,"Base",new Vector3(0,.02f,0),.12f,.04f,dark,Vector3.zero);
        Rod(p,"Arm",new Vector3(0,.04f,0),new Vector3(0,.42f,.06f),.018f,metal);
        Rod(p,"Top arm",new Vector3(0,.42f,.06f),new Vector3(0,.5f,-.14f),.018f,metal);
        B(p,"Shade",0,.49f,-.16f,.22f,.09f,.15f,sage);B(p,"Diffuser",0,.441f,-.16f,.18f,.01f,.11f,ivory);
    }
    static void Safe(Transform p)
    {
        B(p,"Steel case",0,.7f,0,1f,1.4f,.8f,dark,true);
        B(p,"Inset door",0,.71f,-.415f,.85f,1.22f,.06f,metal);
        C(p,"Combination dial",new Vector3(-.2f,.87f,-.47f),.065f,.04f,dark,new Vector3(90,0,0));
        C(p,"Handle hub",new Vector3(.2f,.65f,-.5f),.06f,.08f,dark,new Vector3(90,0,0));
        for(int i=0;i<3;i++){float a=i*2*Mathf.PI/3;Rod(p,"Handle spoke",new Vector3(.2f,.65f,-.54f),new Vector3(.2f+Mathf.Cos(a)*.15f,.65f+Mathf.Sin(a)*.15f,-.54f),.018f,dark);}
        B(p,"Serial plate",0,1.15f,-.453f,.23f,.065f,.014f,paper);
    }
    static void Printer(Transform p)
    {
        B(p,"Printer body",0,.23f,0,.68f,.46f,.55f,ivory,true);
        B(p,"Scanner lid",0,.49f,0,.68f,.06f,.55f,dark);
        B(p,"Paper drawer",0,.12f,-.288f,.6f,.18f,.04f,sage);
        B(p,"Output slot",0,.34f,-.29f,.43f,.08f,.03f,screen);
        B(p,"Output page",0,.3f,-.34f,.3f,.012f,.25f,paper);
        B(p,"Control panel",.23f,.45f,-.29f,.19f,.07f,.07f,dark);
    }
    static void Binders(Transform p)
    {
        for(int i=0;i<6;i++)
        {float x=-.25f+i*.1f;B(p,"Binder",x,.17f,0,.085f,.34f,.27f,i%3==0?red:i%3==1?sage:ivory);B(p,"Spine label",x,.22f,-.141f,.052f,.13f,.009f,paper);B(p,"Finger ring",x,.075f,-.15f,.028f,.028f,.012f,dark);}
    }
    static void Water(Transform p)
    {
        B(p,"Cabinet",0,.55f,0,.48f,1.1f,.5f,ivory,true);
        B(p,"Dispensing bay",0,.82f,-.257f,.35f,.35f,.025f,dark);
        B(p,"Drip tray",0,.65f,-.31f,.38f,.035f,.14f,metal);
        for(int i=0;i<2;i++)B(p,"Tap",-.1f+i*.2f,.94f,-.3f,.05f,.065f,.08f,i==0?blue:red);
        C(p,"Bottle",new Vector3(0,1.34f,0),.18f,.45f,blue,Vector3.zero);
        C(p,"Bottle neck",new Vector3(0,1.1f,0),.08f,.1f,blue,Vector3.zero);
        B(p,"Cup holder",.27f,.83f,.05f,.08f,.3f,.08f,dark);
    }
}
