using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// All dimensions are metres. Models face local -Z and stand on local Y=0.
// Generated meshes/materials/prefabs are local art; this recipe is the tracked source.
public static class StaffRoomModels
{
    public const string Folder = "Assets/!_Project/_Art/Generated/StaffRoom";
    public static readonly string[] Names = {
        "Locker", "ChangingCabin", "DiningTable", "DiningChair", "KitchenCounter", "SinkCounter",
        "Fridge", "Microwave", "CoffeeMachine", "Radio", "TimeClock", "Sofa", "LoungeChair",
        "CoffeeTable", "FoldingTable", "WallShelf", "Whiteboard", "Noticeboard", "WallScreen", "WallClock",
        "RecyclingStation", "TallPlant", "SmallPlant", "FirstAid", "FireExtinguisher", "CoatRail",
        "OpenShelf", "Mug", "Kettle", "FoldedUniform", "RoomDoor", "CeilingPendant"
    };
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly List<Mesh> temporaryMeshes = new List<Mesh>();
    static Material ivory, sage, dark, wood, metal, fabric, green, leaf, soil, red, white, glass;

    [MenuItem("Kehai/Staff Room/1. Build Model Library")]
    public static void BuildLibrary()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build in edit mode.");
        Directory.CreateDirectory(Folder + "/Meshes");
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Prefabs");
        AssetDatabase.Refresh();
        Palette();
        foreach (string name in Names)
        {
            GameObject root = new GameObject(name);
            try
            {
                Build(root.transform, name);
                Bake(root, name);
                AddLightingAnchor(root,name);
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Prefabs/" + name + ".prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (Mesh mesh in temporaryMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
                temporaryMeshes.Clear();
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Built " + Names.Length + " staff-room models at real-world scale.");
    }

    public static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/" + name + ".prefab");

    public static void BuildCustom(string name,string folder,Action<Transform> build)
    {
        var root=new GameObject(name);
        try
        {
            Palette();build(root.transform);Bake(root,name,folder);AddLightingAnchor(root,name);
            PrefabUtility.SaveAsPrefabAsset(root,folder+"/Prefabs/"+name+".prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
            foreach(var mesh in temporaryMeshes)if(mesh!=null)Object.DestroyImmediate(mesh);
            temporaryMeshes.Clear();
        }
    }

    public static void AddLightingAnchor(GameObject root,string model)
    {
        if(model=="RoomDoor")return;
        var anchor=root.GetComponent<InteriorLightAnchor>();if(anchor==null)anchor=root.AddComponent<InteriorLightAnchor>();
        // Built models are at identity here. Use the front access side, not a wall-touching back.
        var filter=root.GetComponent<MeshFilter>();if(filter!=null&&filter.sharedMesh!=null)
            anchor.localPoint=new Vector3(filter.sharedMesh.bounds.center.x,0,filter.sharedMesh.bounds.min.z-.35f);
    }

    public static Material Material(string name, Color color)
    {
        string path = Folder + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.18f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        materials[name] = m;
        return m;
    }

    static void Palette()
    {
        ivory = Material("Warm White", new Color(.86f,.85f,.78f));
        sage = Material("Sage Enamel", new Color(.36f,.51f,.43f));
        dark = Material("Graphite", new Color(.105f,.14f,.15f));
        wood = Material("Light Oak", new Color(.64f,.46f,.28f));
        metal = Material("Brushed Steel", new Color(.51f,.57f,.58f));
        fabric = Material("Sand Upholstery", new Color(.69f,.62f,.48f));
        green = Material("Leaf Dark", new Color(.16f,.31f,.20f));
        leaf = Material("Leaf Light", new Color(.30f,.46f,.24f));
        soil = Material("Soil", new Color(.15f,.11f,.08f));
        red = Material("Safety Red", new Color(.69f,.16f,.12f));
        white = Material("Paper", new Color(.96f,.95f,.91f));
        glass = Material("Dark Screen", new Color(.045f,.08f,.09f));
    }

    static void Build(Transform p, string name)
    {
        switch (name)
        {
            case "Locker": Locker(p); break;
            case "ChangingCabin": Cabin(p); break;
            case "DiningTable": Table(p,4.8f,1.6f,.82f); break;
            case "DiningChair": Chair(p); break;
            case "FoldingTable": Table(p,2.4f,1f,.9f); break;
            case "CoffeeTable": Table(p,1.6f,.75f,.43f); break;
            case "KitchenCounter": Counter(p,false); break;
            case "SinkCounter": Counter(p,true); break;
            case "Fridge": Fridge(p); break;
            case "Microwave": Microwave(p); break;
            case "CoffeeMachine": Coffee(p); break;
            case "Radio": Radio(p); break;
            case "TimeClock": TimeClock(p); break;
            case "Sofa": Seat(p,3.5f); break;
            case "LoungeChair": Seat(p,1.05f); break;
            case "Whiteboard": Board(p,2.5f,1.2f,false); break;
            case "Noticeboard": Board(p,1.5f,1f,true); break;
            case "WallScreen": Screen(p); break;
            case "WallClock": Clock(p); break;
            case "RecyclingStation": Bins(p); break;
            case "TallPlant": Plant(p,1f,1.8f); break;
            case "SmallPlant": Plant(p,.27f,.5f); break;
            case "FirstAid": FirstAid(p); break;
            case "FireExtinguisher": Extinguisher(p); break;
            case "CoatRail": CoatRail(p); break;
            case "OpenShelf": Shelf(p); break;
            case "WallShelf":
                Box(p,"Shelf board",new Vector3(0,.02f,0),new Vector3(2.4f,.04f,.4f),wood,true);
                foreach(float x in new[]{-.9f,.9f})Box(p,"Bracket",new Vector3(x,-.1f,.14f),new Vector3(.04f,.24f,.12f),dark);
                break;
            case "Mug": Mug(p); break;
            case "Kettle": Kettle(p); break;
            case "FoldedUniform": Uniform(p); break;
            case "RoomDoor": RoomDoor(p); break;
            case "CeilingPendant": Pendant(p); break;
        }
    }

    static void Locker(Transform p)
    {
        // Hollow case: 1.2 x 1.1 x 2.4; no enclosing collider across the opening.
        Box(p,"Back",new Vector3(0,1.2f,.525f),new Vector3(1.2f,2.4f,.05f),sage,true);
        foreach(float x in new[]{-.57f,.57f}) Box(p,"Side",new Vector3(x,1.2f,0),new Vector3(.06f,2.4f,1.1f),sage,true);
        foreach(float y in new[]{.06f,2.37f}) Box(p,"Cap",new Vector3(0,y,0),new Vector3(1.08f,.06f,1.1f),sage,true);
        Box(p,"Upper shelf",new Vector3(0,1.98f,.02f),new Vector3(1.08f,.045f,.98f),ivory,true);
        Box(p,"Shoe shelf",new Vector3(0,.3f,.02f),new Vector3(1.08f,.045f,.98f),ivory,true);
        Rod(p,"Coat rail",new Vector3(-.5f,1.82f,.15f),new Vector3(.5f,1.82f,.15f),.018f,metal);
        Box(p,"Hook",new Vector3(0,1.75f,.42f),new Vector3(.04f,.14f,.13f),metal);
        var hinge = Door(p,"locker",new Vector3(-.54f,0,-.52f),1.08f,2.25f,.09f,sage,105f);
        Box(hinge,"Name plate",new Vector3(.54f,1.89f,-.041f),new Vector3(.28f,.13f,.012f),ivory);
        for(int i=0;i<4;i++)
        {
            Box(hinge,"Vent upper",new Vector3(.54f,1.55f+i*.045f,-.04f),new Vector3(.42f,.016f,.012f),dark);
            Box(hinge,"Vent lower",new Vector3(.54f,.4f+i*.045f,-.04f),new Vector3(.42f,.016f,.012f),dark);
        }
    }

    static void Cabin(Transform p)
    {
        Box(p,"Back",new Vector3(0,1.3f,1.31f),new Vector3(2.6f,2.6f,.08f),ivory,true);
        foreach(float x in new[]{-1.26f,1.26f}) Box(p,"Side",new Vector3(x,1.3f,0),new Vector3(.08f,2.6f,2.7f),ivory,true);
        Box(p,"Front return",new Vector3(.95f,1.3f,-1.31f),new Vector3(.7f,2.6f,.08f),sage,true);
        Box(p,"Front jamb",new Vector3(-1.1f,1.3f,-1.31f),new Vector3(.4f,2.6f,.08f),sage,true);
        Box(p,"Lintel",new Vector3(-.15f,2.48f,-1.31f),new Vector3(1.5f,.24f,.08f),sage,true);
        Door(p,"changing cabin",new Vector3(-.9f,0,-1.31f),1.5f,2.3f,.04f,sage,-95f);
        Box(p,"Bench",new Vector3(0,.47f,1.02f),new Vector3(2.2f,.09f,.5f),wood,true);
        foreach(float x in new[]{-.85f,.85f}) Box(p,"Bench foot",new Vector3(x,.23f,1.02f),new Vector3(.08f,.46f,.4f),dark,true);
        Box(p,"Mirror",new Vector3(-1.209f,1.4f,.35f),new Vector3(.015f,1.2f,.65f),metal);
        for(int i=0;i<3;i++) Box(p,"Coat hook",new Vector3(-.55f+i*.55f,1.8f,1.22f),new Vector3(.045f,.12f,.12f),dark);
    }

    public static Transform Door(Transform p,string label,Vector3 at,float width,float height,float bottom,Material material,float angle)
    {
        var hinge = new GameObject("Hinge - " + label).transform;
        hinge.SetParent(p,false); hinge.localPosition=at;
        var panel = Box(hinge,"Door leaf",new Vector3(width*.5f,height*.5f+bottom,0),new Vector3(width,height,.06f),material,true);
        var handle = new Vector3(width-.14f,1.12f,-.075f);
        handle.x-=.08f;
        Box(hinge,"Handle",handle,new Vector3(.22f,.035f,.065f),dark);
        Box(hinge,"Inside handle",new Vector3(handle.x,handle.y,.075f),new Vector3(.22f,.035f,.065f),dark);
        var interact=hinge.gameObject.AddComponent<FurnitureDoor>();
        interact.label=label; interact.openAngle=angle; interact.assembly=p; interact.panel=panel.GetComponent<BoxCollider>();
        hinge.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        var outline=hinge.GetComponent<OutlineHighlight>();
        outline.shape=OutlineHighlight.OutlineShape.PerMesh;
        var existing=Object.FindAnyObjectByType<OutlineHighlight>();
        if(existing!=null && existing!=outline) outline.outlineMaterial=existing.outlineMaterial;
        if(outline.outlineMaterial==null)
            foreach(string guid in AssetDatabase.FindAssets("CustomerOutline t:Material")) {outline.outlineMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));break;}
        int layer=LayerMask.NameToLayer("Interactable");
        if(layer>=0)foreach(Transform t in hinge.GetComponentsInChildren<Transform>())t.gameObject.layer=layer;
        return hinge;
    }

    static void Table(Transform p,float w,float d,float h)
    {
        Box(p,"Oak top",new Vector3(0,h-.04f,0),new Vector3(w,.08f,d),wood,true,.025f);
        foreach(float x in new[]{-w*.5f+.25f,w*.5f-.25f})
        {
            Box(p,"Trestle",new Vector3(x,(h-.08f)*.5f,0),new Vector3(.12f,h-.08f,d-.3f),dark,true);
            Box(p,"Foot",new Vector3(x,.045f,0),new Vector3(.32f,.09f,d-.1f),dark,true);
        }
        Box(p,"Brace",new Vector3(0,h*.45f,0),new Vector3(w-.5f,.1f,.1f),dark);
    }

    static void Chair(Transform p)
    {
        Box(p,"Seat",new Vector3(0,.47f,0),new Vector3(.65f,.09f,.65f),sage,true,.025f);
        Box(p,"Back",new Vector3(0,.8f,.28f),new Vector3(.65f,.65f,.09f),sage,true,.025f);
        foreach(float x in new[]{-.25f,.25f})foreach(float z in new[]{-.25f,.25f}) Box(p,"Leg",new Vector3(x,.22f,z),new Vector3(.05f,.44f,.05f),dark,true);
    }

    static void Counter(Transform p,bool sink)
    {
        Box(p,"Plinth",new Vector3(0,.07f,0),new Vector3(1.08f,.14f,.85f),dark,true);
        Box(p,"Cabinet",new Vector3(0,.52f,0),new Vector3(1.2f,.8f,1f),sage,true);
        Box(p,"Worktop",new Vector3(0,.94f,0),new Vector3(1.2f,.06f,1.1f),ivory,true);
        foreach(float x in new[]{-.3f,.3f})
        {
            Box(p,"Door",new Vector3(x,.53f,-.511f),new Vector3(.58f,.74f,.025f),sage);
            Box(p,"Pull",new Vector3(x,.82f,-.55f),new Vector3(.22f,.028f,.04f),dark);
        }
        if(!sink)return;
        Box(p,"Sink rim",new Vector3(0,.981f,0),new Vector3(.8f,.025f,.65f),metal);
        Box(p,"Basin",new Vector3(0,.996f,0),new Vector3(.68f,.01f,.53f),dark);
        Rod(p,"Tap upright",new Vector3(.4f,.98f,.32f),new Vector3(.4f,1.3f,.32f),.025f,metal);
        Rod(p,"Tap spout",new Vector3(.4f,1.3f,.32f),new Vector3(.12f,1.3f,.15f),.025f,metal);
        Box(p,"Soap dispenser",new Vector3(-.48f,1.08f,.3f),new Vector3(.1f,.2f,.1f),ivory);
    }

    static void Fridge(Transform p)
    {
        Box(p,"Body",new Vector3(0,1.05f,0),new Vector3(1.05f,2.1f,1f),ivory,true,.025f);
        Box(p,"Freezer door",new Vector3(0,1.74f,-.525f),new Vector3(1.02f,.66f,.06f),ivory);
        Box(p,"Chilled door",new Vector3(0,.72f,-.525f),new Vector3(1.02f,1.32f,.06f),ivory);
        Box(p,"Freezer pull",new Vector3(.4f,1.5f,-.59f),new Vector3(.035f,.25f,.08f),dark);
        Box(p,"Fridge pull",new Vector3(.4f,1.12f,-.59f),new Vector3(.035f,.35f,.08f),dark);
        Box(p,"Note",new Vector3(-.15f,1.7f,-.56f),new Vector3(.24f,.27f,.008f),white);
    }

    static void Microwave(Transform p)
    {
        Box(p,"Body",new Vector3(0,.19f,0),new Vector3(.7f,.38f,.46f),ivory,true);
        Box(p,"Glass",new Vector3(-.06f,.2f,-.237f),new Vector3(.48f,.27f,.025f),glass);
        Box(p,"Handle",new Vector3(.17f,.2f,-.27f),new Vector3(.025f,.22f,.055f),dark);
        for(int i=0;i<2;i++) Cylinder(p,"Dial",new Vector3(.28f,.12f+i*.14f,-.25f),.032f,.035f,dark,new Vector3(90,0,0));
    }

    static void Coffee(Transform p)
    {
        Box(p,"Body",new Vector3(0,.31f,.04f),new Vector3(.48f,.62f,.4f),dark,true);
        Box(p,"Water tank",new Vector3(0,.34f,.27f),new Vector3(.4f,.55f,.12f),metal);
        Box(p,"Display",new Vector3(0,.49f,-.168f),new Vector3(.22f,.09f,.02f),sage);
        Box(p,"Spout",new Vector3(0,.32f,-.22f),new Vector3(.1f,.07f,.12f),metal);
        Box(p,"Drip tray",new Vector3(0,.03f,-.12f),new Vector3(.48f,.06f,.55f),metal,true);
        var cup=new GameObject("Cup").transform;cup.SetParent(p,false);cup.localPosition=new Vector3(0,.07f,-.22f);Mug(cup);
    }

    static void Radio(Transform p)
    {
        Box(p,"Case",new Vector3(0,.22f,0),new Vector3(.7f,.44f,.27f),wood,true);
        Box(p,"Speaker grille",new Vector3(-.12f,.22f,-.143f),new Vector3(.36f,.3f,.022f),dark);
        for(int i=0;i<7;i++)Box(p,"Grille slat",new Vector3(-.27f+i*.05f,.22f,-.159f),new Vector3(.012f,.28f,.012f),metal);
        Box(p,"Dial scale",new Vector3(.21f,.3f,-.15f),new Vector3(.15f,.07f,.025f),ivory);
        Cylinder(p,"Tuning dial",new Vector3(.21f,.14f,-.16f),.045f,.035f,dark,new Vector3(90,0,0));
        Rod(p,"Antenna",new Vector3(.25f,.44f,.05f),new Vector3(.32f,.79f,.05f),.008f,metal);
    }

    static void TimeClock(Transform p)
    {
        Box(p,"Case",new Vector3(0,.25f,0),new Vector3(.38f,.5f,.14f),dark,true);
        Box(p,"Display",new Vector3(0,.34f,-.076f),new Vector3(.27f,.15f,.015f),sage);
        for(int x=0;x<3;x++)for(int y=0;y<3;y++)Box(p,"Button",new Vector3(-.07f+x*.07f,.11f+y*.05f,-.081f),new Vector3(.04f,.025f,.02f),ivory);
        Box(p,"Card reader",new Vector3(.15f,.15f,-.02f),new Vector3(.02f,.18f,.06f),red);
    }

    static void Seat(Transform p,float width)
    {
        Box(p,"Base",new Vector3(0,.24f,0),new Vector3(width-.08f,.35f,.94f),dark,true);
        Box(p,"Back cushion",new Vector3(0,.72f,.34f),new Vector3(width-.44f,.65f,.28f),fabric,true,.04f);
        foreach(float x in new[]{-width*.5f+.1f,width*.5f-.1f}) Box(p,"Arm",new Vector3(x,.56f,0),new Vector3(.2f,.52f,1f),sage,true,.035f);
        int count=width>2f?3:1;
        float cushion=(width-.45f)/count;
        for(int i=0;i<count;i++)Box(p,"Seat cushion",new Vector3(-width*.5f+.225f+cushion*(i+.5f),.48f,-.15f),new Vector3(cushion-.025f,.18f,.64f),fabric,true,.035f);
        foreach(float x in new[]{-width*.5f+.2f,width*.5f-.2f})foreach(float z in new[]{-.35f,.35f})Box(p,"Foot",new Vector3(x,.065f,z),new Vector3(.08f,.13f,.08f),wood,true);
    }

    static void Board(Transform p,float width,float height,bool notice)
    {
        Box(p,"Frame",new Vector3(0,height*.5f,0),new Vector3(width,height,.08f),dark,true);
        Box(p,"Surface",new Vector3(0,height*.5f,-.046f),new Vector3(width-.08f,height-.08f,.015f),notice?wood:white);
        if(notice)
        {
            for(int i=0;i<4;i++)
            {
                float x=-.5f+i*.33f;
                Box(p,"Notice",new Vector3(x,.55f,-.058f),new Vector3(.27f,.45f,.009f),white);
                Box(p,"Pin",new Vector3(x,.75f,-.07f),new Vector3(.025f,.025f,.015f),red);
                for(int j=0;j<4;j++)Box(p,"Print",new Vector3(x,.62f-j*.06f,-.064f),new Vector3(.18f,.009f,.004f),dark);
            }
        }
        else
        {
            Box(p,"Pen tray",new Vector3(0,.02f,-.1f),new Vector3(width-.2f,.035f,.15f),metal);
            for(int i=0;i<3;i++)Box(p,"Marker",new Vector3(.65f+i*.18f,.05f,-.1f),new Vector3(.13f,.025f,.025f),i==0?red:dark);
            Box(p,"Eraser",new Vector3(-.8f,.06f,-.1f),new Vector3(.16f,.05f,.065f),sage);
        }
    }

    static void Screen(Transform p)
    {
        Box(p,"Bracket",new Vector3(0,.5f,.04f),new Vector3(.3f,.3f,.08f),dark,true);
        Box(p,"Bezel",new Vector3(0,.5f,-.035f),new Vector3(1.7f,1f,.08f),dark,true);
        Box(p,"Screen",new Vector3(0,.5f,-.079f),new Vector3(1.61f,.91f,.012f),glass);
        Box(p,"Standby light",new Vector3(.7f,.025f,-.087f),new Vector3(.015f,.008f,.008f),red);
    }

    static void Clock(Transform p)
    {
        Cylinder(p,"Rim",new Vector3(0,.23f,0),.23f,.06f,dark,new Vector3(90,0,0));
        Cylinder(p,"Face",new Vector3(0,.23f,-.035f),.21f,.014f,ivory,new Vector3(90,0,0));
        for(int i=0;i<12;i++) {float a=i*Mathf.PI/6f;Box(p,"Hour mark",new Vector3(Mathf.Sin(a)*.17f,.23f+Mathf.Cos(a)*.17f,-.047f),new Vector3(.016f,.025f,.006f),dark);}
        Box(p,"Minute hand",new Vector3(0,.285f,-.055f),new Vector3(.012f,.13f,.01f),dark);
        Box(p,"Hour hand",new Vector3(.045f,.23f,-.06f),new Vector3(.1f,.014f,.01f),red);
    }

    static void Bins(Transform p)
    {
        for(int i=0;i<2;i++)
        {
            float x=i==0?-.24f:.24f;
            Box(p,"Bin body",new Vector3(x,.42f,0),new Vector3(.46f,.84f,.55f),dark,true,.025f);
            Box(p,"Sorting lid",new Vector3(x,.87f,0),new Vector3(.46f,.07f,.55f),i==0?sage:ivory);
            Box(p,"Opening",new Vector3(x,.91f,-.03f),new Vector3(.27f,.015f,.22f),glass);
            Box(p,"Label",new Vector3(x,.61f,-.281f),new Vector3(.26f,.14f,.015f),white);
        }
    }

    static void Plant(Transform p,float width,float height)
    {
        float potH=height*.33f;
        Cylinder(p,"Faceted planter",new Vector3(0,potH*.5f,0),width*.42f,potH,ivory,Vector3.zero,.8f);
        Cylinder(p,"Soil",new Vector3(0,potH-.02f,0),width*.38f,.025f,soil,Vector3.zero);
        Rod(p,"Stem",new Vector3(0,potH,0),new Vector3(0,height*.9f,0),width*.025f,wood);
        for(int i=0;i<9;i++)
        {
            float a=i*2.4f;
            Vector3 start=new Vector3(0,potH+(height-potH)*(.15f+.07f*i),0);
            Vector3 tip=start+new Vector3(Mathf.Cos(a)*width*.5f,height*.18f,Mathf.Sin(a)*width*.5f);
            Leaf(p,start,tip,width*.18f,i%2==0?green:leaf);
        }
        var c=p.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,potH*.5f,0);c.size=new Vector3(width*.84f,potH,width*.84f);
    }

    static void FirstAid(Transform p)
    {
        Box(p,"Cabinet",new Vector3(0,.28f,0),new Vector3(.5f,.56f,.18f),ivory,true);
        Box(p,"Cross upright",new Vector3(0,.3f,-.096f),new Vector3(.07f,.3f,.014f),sage);
        Box(p,"Cross bar",new Vector3(0,.3f,-.096f),new Vector3(.3f,.07f,.014f),sage);
    }

    static void Extinguisher(Transform p)
    {
        Cylinder(p,"Bottle",new Vector3(0,.3f,0),.13f,.6f,red,Vector3.zero);
        Box(p,"Wall bracket",new Vector3(0,.3f,.14f),new Vector3(.18f,.4f,.04f),dark);
        Box(p,"Lever",new Vector3(0,.66f,0),new Vector3(.18f,.04f,.07f),dark);
        Rod(p,"Hose",new Vector3(.1f,.62f,0),new Vector3(.16f,.18f,0),.025f,dark);
        Box(p,"Label",new Vector3(0,.35f,-.13f),new Vector3(.13f,.21f,.015f),white);
    }

    static void CoatRail(Transform p)
    {
        Box(p,"Backboard",new Vector3(0,.1f,0),new Vector3(1.4f,.2f,.05f),wood,true);
        for(int i=0;i<5;i++)Box(p,"Hook",new Vector3(-.55f+i*.275f,.08f,-.065f),new Vector3(.035f,.09f,.1f),dark);
    }

    static void Shelf(Transform p)
    {
        foreach(float x in new[]{-.72f,.72f})Box(p,"Side",new Vector3(x,1,0),new Vector3(.06f,2f,.5f),sage,true);
        Box(p,"Back",new Vector3(0,1,.235f),new Vector3(1.5f,2,.03f),sage,true);
        for(int i=0;i<5;i++)Box(p,"Shelf",new Vector3(0,.08f+i*.47f,0),new Vector3(1.5f,.045f,.5f),wood,true);
    }

    static void Mug(Transform p)
    {
        Cylinder(p,"Cup",new Vector3(0,.065f,0),.055f,.13f,ivory,Vector3.zero);
        Cylinder(p,"Coffee",new Vector3(0,.131f,0),.045f,.003f,soil,Vector3.zero);
        Box(p,"Handle",new Vector3(.065f,.075f,0),new Vector3(.045f,.07f,.025f),ivory);
    }

    static void Kettle(Transform p)
    {
        Cylinder(p,"Jug",new Vector3(0,.15f,0),.12f,.3f,ivory,Vector3.zero,.8f);
        Cylinder(p,"Lid",new Vector3(0,.315f,0),.105f,.03f,dark,Vector3.zero);
        Box(p,"Handle",new Vector3(.14f,.18f,0),new Vector3(.06f,.21f,.045f),dark);
        Rod(p,"Spout",new Vector3(-.08f,.22f,0),new Vector3(-.18f,.3f,0),.035f,metal);
    }

    static void Uniform(Transform p)
    {
        for(int i=0;i<3;i++)Box(p,"Folded shirt",new Vector3(0,.025f+i*.045f,0),new Vector3(.48f,.045f,.38f),i%2==0?sage:ivory);
        Box(p,"Collar",new Vector3(0,.15f,.1f),new Vector3(.14f,.025f,.09f),dark);
    }

    static void RoomDoor(Transform p)
    {
        foreach(float x in new[]{-.79f,.79f})Box(p,"Jamb",new Vector3(x,1.3f,0),new Vector3(.08f,2.6f,.18f),ivory,true);
        Box(p,"Header",new Vector3(0,2.56f,0),new Vector3(1.66f,.08f,.18f),ivory,true);
        var hinge=Door(p,"staff door",new Vector3(-.75f,0,0),1.5f,2.46f,.035f,sage,95f);
        var door=hinge.gameObject.AddComponent<HingeDoor>();
        door.aimedInteraction=true;door.fixedSwingDirection=true;door.openAngle=95f;
        hinge.GetComponent<FurnitureDoor>().roomDoor=door;
        Box(hinge,"Kick plate",new Vector3(.75f,.2f,-.039f),new Vector3(1.42f,.27f,.014f),metal);
        Box(hinge,"Push plate",new Vector3(1.32f,1.25f,-.04f),new Vector3(.13f,.36f,.02f),metal);
    }

    static void Pendant(Transform p)
    {
        Rod(p,"Suspension",new Vector3(0,.15f,0),new Vector3(0,.85f,0),.012f,dark);
        Box(p,"Shade",new Vector3(0,.1f,0),new Vector3(1.8f,.2f,.32f),dark);
        Box(p,"Diffuser",new Vector3(0,.001f,0),new Vector3(1.65f,.015f,.25f),ivory);
    }

    public static GameObject Box(Transform p,string name,Vector3 center,Vector3 size,Material material,bool solid=false,float bevel=.008f)
    {
        var go=new GameObject(name);go.transform.SetParent(p,false);go.transform.localPosition=center;
        var mesh=BevelBox(size,Mathf.Min(bevel,Mathf.Min(size.x,Mathf.Min(size.y,size.z))*.2f));
        temporaryMeshes.Add(mesh);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial=material;
        if(solid)go.AddComponent<BoxCollider>().size=size;
        return go;
    }

    public static void Rod(Transform p,string name,Vector3 a,Vector3 b,float radius,Material mat)
    {
        var go=Cylinder(p,name,(a+b)*.5f,radius,(b-a).magnitude,mat,Vector3.zero);
        go.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
    }

    public static GameObject Cylinder(Transform p,string name,Vector3 center,float radius,float height,Material mat,Vector3 rotation,float bottomRatio=1f)
    {
        const int n=10;
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<n;i++)
        {
            float a=2*Mathf.PI*i/n,b=2*Mathf.PI*(i+1)/n;
            var loA=new Vector3(Mathf.Cos(a)*radius*bottomRatio,-height*.5f,Mathf.Sin(a)*radius*bottomRatio);
            var loB=new Vector3(Mathf.Cos(b)*radius*bottomRatio,-height*.5f,Mathf.Sin(b)*radius*bottomRatio);
            var hiA=new Vector3(Mathf.Cos(a)*radius,height*.5f,Mathf.Sin(a)*radius);
            var hiB=new Vector3(Mathf.Cos(b)*radius,height*.5f,Mathf.Sin(b)*radius);
            Face(vertices,triangles,new[]{loA,hiA,hiB,loB},new Vector3(Mathf.Cos((a+b)*.5f),0,Mathf.Sin((a+b)*.5f)));
            Face(vertices,triangles,new[]{Vector3.up*height*.5f,hiA,hiB},Vector3.up);
            Face(vertices,triangles,new[]{Vector3.down*height*.5f,loA,loB},Vector3.down);
        }
        var mesh=Mesh(vertices,triangles);temporaryMeshes.Add(mesh);
        var go=new GameObject(name);go.transform.SetParent(p,false);go.transform.localPosition=center;go.transform.localEulerAngles=rotation;
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;return go;
    }

    static void Leaf(Transform p,Vector3 a,Vector3 b,float width,Material mat)
    {
        Vector3 side=Vector3.Cross(b-a,Vector3.up).normalized*width;
        Vector3 mid=Vector3.Lerp(a,b,.55f),ridge=mid+Vector3.up*width*.35f;
        var v=new List<Vector3>();var t=new List<int>();
        foreach(float sign in new[]{1f,-1f})
        {
            Face(v,t,new[]{a,mid-side,ridge},Vector3.up*sign);
            Face(v,t,new[]{mid-side,b,ridge},Vector3.up*sign);
            Face(v,t,new[]{b,mid+side,ridge},Vector3.up*sign);
            Face(v,t,new[]{mid+side,a,ridge},Vector3.up*sign);
        }
        var mesh=Mesh(v,t);temporaryMeshes.Add(mesh);
        var go=new GameObject("Leaf");go.transform.SetParent(p,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
    }

    static Mesh BevelBox(Vector3 size,float bevel)
    {
        Vector3 h=size*.5f,k=h-Vector3.one*bevel;
        var v=new List<Vector3>();var t=new List<int>();
        for(int axis=0;axis<3;axis++)for(int s=-1;s<=1;s+=2)
        {
            int u=(axis+1)%3,w=(axis+2)%3;var points=new Vector3[4];
            int[] us={-1,1,1,-1},ws={-1,-1,1,1};
            for(int i=0;i<4;i++){var q=Vector3.zero;q[axis]=s*h[axis];q[u]=us[i]*k[u];q[w]=ws[i]*k[w];points[i]=q;}
            var normal=Vector3.zero;normal[axis]=s;Face(v,t,points,normal);
        }
        for(int axis=0;axis<3;axis++)for(int su=-1;su<=1;su+=2)for(int sw=-1;sw<=1;sw+=2)
        {
            int u=(axis+1)%3,w=(axis+2)%3;var points=new Vector3[4];
            for(int i=0;i<4;i++){var q=Vector3.zero;q[axis]=(i==0||i==3?-1:1)*k[axis];q[u]=su*(i<2?h[u]:k[u]);q[w]=sw*(i<2?k[w]:h[w]);points[i]=q;}
            var normal=Vector3.zero;normal[u]=su;normal[w]=sw;Face(v,t,points,normal);
        }
        for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
        {
            Vector3 sign=new Vector3(x,y,z);var points=new Vector3[3];
            for(int a=0;a<3;a++){var q=k;q[a]=h[a];points[a]=Vector3.Scale(q,sign);}Face(v,t,points,sign);
        }
        return Mesh(v,t);
    }

    static void Face(List<Vector3> v,List<int> t,Vector3[] p,Vector3 normal)
    {
        if(Vector3.Dot(Vector3.Cross(p[1]-p[0],p[2]-p[0]),normal)<0)Array.Reverse(p);
        int start=v.Count;v.AddRange(p);for(int i=1;i<p.Length-1;i++){t.Add(start);t.Add(start+i);t.Add(start+i+1);}
    }
    static Mesh Mesh(List<Vector3> v,List<int> t)
    {
        var mesh=new Mesh();mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
    }

    static void Bake(GameObject root,string name,string folder=Folder)
    {
        var groups=new List<Transform>{root.transform};
        foreach(var door in root.GetComponentsInChildren<FurnitureDoor>())groups.Add(door.transform);
        foreach(var group in groups)
        {
            var byMaterial=new Dictionary<Material,List<CombineInstance>>();
            var filters=new List<MeshFilter>();
            foreach(var f in group.GetComponentsInChildren<MeshFilter>())
            {
                var owner=f.GetComponentInParent<FurnitureDoor>();
                if((group==root.transform && owner!=null)||(group!=root.transform && (owner==null||owner.transform!=group)))continue;
                var r=f.GetComponent<MeshRenderer>();if(r==null||f.sharedMesh==null)continue;
                if(!byMaterial.TryGetValue(r.sharedMaterial,out var list))byMaterial[r.sharedMaterial]=list=new List<CombineInstance>();
                list.Add(new CombineInstance{mesh=f.sharedMesh,transform=group.worldToLocalMatrix*f.transform.localToWorldMatrix});filters.Add(f);
            }
            if(filters.Count==0)continue;
            var submeshes=new List<Mesh>();var combines=new List<CombineInstance>();var mats=new List<Material>();
            foreach(var entry in byMaterial)
            {
                var sub=new Mesh();sub.CombineMeshes(entry.Value.ToArray(),true,true);submeshes.Add(sub);
                combines.Add(new CombineInstance{mesh=sub,transform=Matrix4x4.identity});mats.Add(entry.Key);
            }
            var merged=new Mesh{name=name+(group==root.transform?" Body":" Door")};merged.CombineMeshes(combines.ToArray(),false,false);
            string path=folder+"/Meshes/"+name+(group==root.transform?" Body":" Door")+".asset";
            Mesh saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(saved==null){AssetDatabase.CreateAsset(merged,path);saved=merged;}else{EditorUtility.CopySerialized(merged,saved);Object.DestroyImmediate(merged);EditorUtility.SetDirty(saved);}
            // CopySerialized updates the asset but can leave the GPU drawing its previous geometry.
            // Touch the mesh buffers so rebuilt handles, cushions and decks update immediately.
            saved.vertices=saved.vertices;saved.normals=saved.normals;
            for(int i=0;i<saved.subMeshCount;i++)saved.SetTriangles(saved.GetTriangles(i),i,false);
            saved.UploadMeshData(false);
            foreach(var sub in submeshes)Object.DestroyImmediate(sub);
            foreach(var f in filters){Object.DestroyImmediate(f.GetComponent<MeshRenderer>());Object.DestroyImmediate(f);}
            group.gameObject.AddComponent<MeshFilter>().sharedMesh=saved;
            group.gameObject.AddComponent<MeshRenderer>().sharedMaterials=mats.ToArray();
            // Empty visual helpers are no longer needed; preserve collider/hinge transforms.
            var children=group.GetComponentsInChildren<Transform>();
            for(int i=children.Length-1;i>=0;i--){var tr=children[i];if(tr==group)continue;if(tr.childCount==0&&tr.GetComponents<Component>().Length==1)Object.DestroyImmediate(tr.gameObject);}
        }
    }
}
