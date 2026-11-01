using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Matches the measured site plan: 53 bays, 6m deep x 3.2m wide, 8m two-way aisles.
// Vehicles and charging equipment are original visual props, not a driving simulation.
public static class ParkingLotBuilder
{
    public const string RootName="Parking - Finished";
    public static readonly string[] Models={"ParkingLamp","ParkingTree","EVCharger","ParkedCar"};
    static Transform root;
    static Material asphalt,paving,white,yellow,grass,steel,dark,green,glass,red;
    static int spaces,cars;
    [MenuItem("Kehai/Store Finish/Build Parking Lot")]
    public static void Build()
    {
        using var hierarchy = SceneHierarchyOrganizer.BuilderScope();
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build parking in edit mode.");
        if(GameObject.Find(StoreFinishBuilder.RootName)==null)throw new InvalidOperationException("Build the store finish first.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        System.IO.Directory.CreateDirectory("Library/Kehai/SceneBackups");
        System.IO.File.Copy(scene.path,"Library/Kehai/SceneBackups/Parking-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity");
        asphalt=StaffRoomModels.Material("Parking Asphalt",new Color(.19f,.21f,.22f));
        paving=StaffRoomModels.Material("Parking Concrete",new Color(.63f,.62f,.56f));
        white=StaffRoomModels.Material("Parking White",new Color(.9f,.88f,.78f));yellow=StaffRoomModels.Material("Parking Yellow",new Color(.85f,.64f,.18f));
        grass=StaffRoomModels.Material("Parking Grass",new Color(.25f,.36f,.25f));steel=StaffRoomModels.Material("Parking Metal",new Color(.4f,.46f,.47f));
        dark=StaffRoomModels.Material("Parking Graphite",new Color(.09f,.13f,.14f));green=StaffRoomModels.Material("Parking Leaves",new Color(.15f,.28f,.19f));
        glass=StaffRoomModels.Material("Car Glass",new Color(.14f,.23f,.25f));red=StaffRoomModels.Material("Car Paint",new Color(.5f,.2f,.16f));
        foreach(string name in Models)StaffRoomModels.BuildCustom(name,StoreFinishModels.Folder,p=>Model(p,name));
        var old=GameObject.Find(RootName);if(old!=null)Undo.DestroyObjectImmediate(old);
        root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Build measured parking lot");spaces=cars=0;
        var oldForecourt=GameObject.Find(StoreFinishBuilder.RootName+"/Forecourt floor");
        if(oldForecourt!=null)Undo.DestroyObjectImmediate(oldForecourt);
        Surface("Parking floor",0,-105.5f,98,52,.024f,asphalt);
        Surface("Public approach road floor",-7,-53.5f,112,10,.024f,asphalt);
        Surface("Entrance pedestrian apron",10,-110,80,4.5f,.12f,paving);
        foreach(float x in new[]{42.5f,67.5f})
        {
            Surface("Protected entrance walkway",x-2,-97.5f,4,32.5f,.12f,paving);
            Surface("Door-aligned raised crossing",x-2,-105.5f,4,8,.09f,paving);
            for(float z=-105;z<-97.5f;z+=1.1f)Mark("Crossing stripe",x,z,3.5f,.48f,white,.096f);
            foreach(float dx in new[]{-1.82f,1.82f})foreach(float z in new[]{-97.2f,-105.8f})Place("Bollard","Pedestrian protection",new Vector3(x+dx,.12f,z),0);
        }
        Surface("Accessible connector",64.5f,-97.5f,1,32.5f,.12f,paving);
        Bed("North verge",13,-57,77,3.5f);
        Bed("West edge",-6,-105.5f,6,8);Bed("West continuous planting",-6,-97.5f,11,44);
        Bed("East perimeter",98,-105.5f,4,52);
        Bed("West parking separator",13,-97.5f,1.5f,32.5f);Bed("Central planting",34.5f,-97.5f,6,32.5f);
        Bed("East narrow separator",89.5f,-97.5f,.5f,32.5f);
        float[] blocks={14.5f,44.5f,69.5f};
        for(int block=0;block<blocks.Length;block++)for(int side=0;side<2;side++)for(int j=0;j<10;j++)
        {
            if(((block==1&&side==1)||(block==2&&side==0))&&j<3)continue;
            if(block==0&&side==0&&j<3)continue;
            if(((block==0&&side==1)||(block==2&&side==1))&&(j==5||j==6))continue;
            bool ev=block==0&&side==0&&j>=3&&j<7;
            Bay(blocks[block]+side*14,-97.5f+j*3.2f,3.2f,ev?"EV":"Standard",spaces%7==2,side==1);
            if(ev)Place("EVCharger","EV charge point",new Vector3(14.1f,.12f,-95.9f+j*3.2f),-90);
        }
        foreach(float x in new[]{58.5f,69.5f})
        {
            foreach(float z in new[]{-97.5f,-92.1f})Bay(x,z,3.8f,"Accessible",false,x<60);
            Surface("Accessible transfer aisle",x,-93.7f,6,1.6f,.028f,paving);
            for(float dx=.4f;dx<6;dx+=.65f)Mark("Transfer hatching",x+dx,-92.9f,.10f,1.4f,yellow,.035f);
        }
        foreach(float z in new[]{-97.5f,-93.7f})Bay(14.5f,z,3.8f,"Family",false);
        foreach(float x in new[]{28.5f,83.5f})
        {
            Surface("Trolley return paving",x,-81.5f,6,6.4f,.12f,paving);
            Place("TrolleyCorral","Trolley return",new Vector3(x+3,.12f,-78.3f),90);
            ServiceYardBuilder.Station(root,"Parking recycling",new Vector3(x+3,.12f,-75.55f),0,true,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!_Project/_Game/Level/Prefabs/TrashBag.prefab"));
        }
        // Driveway centres split into two 4m lanes; stop lines face the public road.
        foreach(float x in new[]{9f,94f})
        {
            Mark("Give way",x,-54.1f,7,.18f,white);
            for(float z=-103;z<-55;z+=6)Mark("Approach centre dash",x,z,.1f,2.6f,white);
            Arrow(x-2,-70,180);Arrow(x+2,-90,0);
        }
        for(float x=16;x<90;x+=10)
        {Mark("Forecourt centre dash",x,-101.5f,3,.1f,white);Mark("North aisle centre dash",x,-61,3,.1f,white);}
        foreach(float x in new[]{24.5f,54.5f,79.5f}){Arrow(x-2,-74,180);Arrow(x+2,-86,0);}
        foreach(float x in new[]{20f,49f,79f}){Arrow(x,-59,270);Arrow(x,-63,90);Arrow(x,-99.5f,270);Arrow(x,-103.5f,90);}
        for(float x=-4;x<104;x+=7)Mark("Public road centre dash",x,-48.5f,3,.12f,white);
        foreach(var p in LightPositions())
        {
            var go=Place("ParkingLamp","Parking lighting",p,0);var lamp=new GameObject("Downlight");lamp.transform.SetParent(go.transform,false);lamp.transform.localPosition=new Vector3(0,5.8f,0);lamp.transform.localRotation=Quaternion.Euler(90,0,0);
            var l=lamp.AddComponent<Light>();l.type=LightType.Spot;l.spotAngle=125;l.range=20;l.intensity=55;l.color=new Color(1,.9f,.73f);
        }
        foreach(var p in new[]{new Vector3(20,0,-55.25f),new Vector3(35,0,-55.25f),new Vector3(54,0,-55.25f),new Vector3(76,0,-55.25f),new Vector3(37.25f,0,-75),new Vector3(37.25f,0,-85),new Vector3(37.25f,0,-94),new Vector3(-3,0,-70),new Vector3(-3,0,-88)})Place("ParkingTree","Parking tree",p,0);
        var surface=Kehai.Karen.KarenWorld.StoreSurface();if(surface!=null)
        {
            var c=surface.transform.TransformPoint(surface.center);float south=c.z-surface.size.z*.5f,north=-53.5f;
            c.z=(south+north)*.5f;surface.center=surface.transform.InverseTransformPoint(c);surface.size=new Vector3(surface.size.x,surface.size.y,north-south);
        }
        ModelIslandLayout.Organize();StaffRoomBuilder.BakeNavigation();StaffRoomBuilder.RefreshLighting();
        EnvironmentPolishBuilder.Midnight();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Parking built: "+spaces+" bays, "+cars+" stationary cars, protected walking routes, four EV points and two trolley returns.");
    }
    public static IEnumerable<Vector3> LightPositions()
    {
        foreach(float x in new[]{13.75f,37.25f,89.75f})foreach(float z in new[]{-70f,-82f,-94f})yield return new Vector3(x,0,z);
        foreach(float x in new[]{23f,45f,65f,84f})yield return new Vector3(x,0,-55.25f);
        foreach(float z in new[]{-69f,-84f,-99f})yield return new Vector3(100,0,z);
        foreach(float z in new[]{-70f,-92f})yield return new Vector3(-2,0,z);
    }
    static void Bay(float x,float z,float width,string type,bool car,bool aisleOnLeft=false)
    {
        spaces++;var color=type=="Accessible"?yellow:white;
        Mark(type+" bay "+spaces+" upper line",x+3,z+width,6,.075f,color);Mark(type+" bay "+spaces+" lower line",x+3,z,6,.075f,color);
        Mark(type+" bay "+spaces+" back line",x+(aisleOnLeft?6:0),z+width*.5f,.075f,width,color);
        var wheel=ColdStoreBuilder.Primitive(root,"Wheel stop",new Vector3(x+(aisleOnLeft?5.35f:.65f),.08f,z+width*.5f),new Vector3(.16f,.16f,1.6f),paving,true);wheel.AddComponent<InteriorLightAnchor>().outdoors=true;
        if(type!="Standard")
        {
            var t=new GameObject(type+" bay mark");t.transform.SetParent(root,false);t.transform.SetPositionAndRotation(new Vector3(x+4,.041f,z+width*.5f),Quaternion.Euler(90,0,90));
            var label=t.AddComponent<TextMeshPro>();label.font=TMP_Settings.defaultFontAsset;label.text=type=="Accessible"?"ACCESS":type.ToUpperInvariant();label.fontSize=5;label.alignment=TextAlignmentOptions.Center;label.color=color.GetColor("_BaseColor");label.rectTransform.sizeDelta=new Vector2(width-.3f,1);
        }
        if(car){Place("ParkedCar","Parked car "+(++cars),new Vector3(x+3,.024f,z+width*.5f),0);}
    }
    static GameObject Place(string model,string name,Vector3 at,float yaw)
    {
        var prefab=StoreFinishModels.Prefab(model)??PublicInteriorModels.Prefab(model);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);go.name=name;go.transform.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));
        var anchor=go.GetComponent<InteriorLightAnchor>();if(anchor!=null)anchor.outdoors=true;return go;
    }
    static void Surface(string name,float x,float z,float width,float depth,float top,Material material)
    {var go=ColdStoreBuilder.Primitive(root,name,new Vector3(x+width*.5f,top*.5f,z+depth*.5f),new Vector3(width,top,depth),material,true);go.AddComponent<InteriorLightAnchor>().outdoors=true;}
    static void Bed(string name,float x,float z,float width,float depth)
    {
        Surface(name,x,z,width,depth,.12f,grass);
        Mark("Planter kerb",x,z+depth*.5f,.12f,depth,paving,.14f);Mark("Planter kerb",x+width,z+depth*.5f,.12f,depth,paving,.14f);
    }
    static void Mark(string name,float x,float z,float width,float depth,Material material,float y=.031f)
    {var go=ColdStoreBuilder.Primitive(root,name,new Vector3(x,y,z),new Vector3(width,.005f,depth),material,false);go.AddComponent<InteriorLightAnchor>().outdoors=true;}
    static void Arrow(float x,float z,float yaw)
    {
        var t=new GameObject("Traffic arrow").transform;t.SetParent(root,false);t.SetPositionAndRotation(new Vector3(x,.032f,z),Quaternion.Euler(0,yaw,0));
        ColdStoreBuilder.Primitive(t,"Arrow stem",new Vector3(0,0,-.2f),new Vector3(.18f,.006f,1.6f),white,false);
        foreach(float sign in new[]{-1f,1f}){var a=ColdStoreBuilder.Primitive(t,"Arrow head",new Vector3(sign*.25f,0,.5f),new Vector3(.16f,.006f,.8f),white,false);a.transform.localRotation=Quaternion.Euler(0,sign*-40,0);}
        t.gameObject.AddComponent<InteriorLightAnchor>().outdoors=true;
    }
    static void Model(Transform p,string name)
    {
        if(name=="ParkingLamp")
        {C(p,"Pole",new Vector3(0,3,0),.075f,6,steel,Vector3.zero);B(p,"Base",new Vector3(0,.12f,0),new Vector3(.35f,.24f,.35f),dark,true);B(p,"Luminaire",new Vector3(0,6,0),new Vector3(1.1f,.14f,.42f),dark);B(p,"Diffuser",new Vector3(0,5.92f,0),new Vector3(.95f,.02f,.32f),white);var c=p.gameObject.AddComponent<BoxCollider>();c.center=new Vector3(0,3,0);c.size=new Vector3(.15f,6,.15f);}
        if(name=="ParkingTree")
        {C(p,"Trunk",new Vector3(0,1.5f,0),.15f,3,StaffRoomModels.Material("Parking Bark",new Color(.28f,.22f,.16f)),Vector3.zero);for(int i=0;i<3;i++)StaffRoomModels.Cylinder(p,"Faceted crown",new Vector3(i==1?.65f:i==2?-.6f:0,3.1f+i*.45f,i==2?.4f:0),1.25f,1.5f,green,new Vector3(0,i*17,0),.5f);var c=p.gameObject.AddComponent<CapsuleCollider>();c.center=new Vector3(0,1.5f,0);c.radius=.15f;c.height=3;}
        if(name=="EVCharger")
        {B(p,"Pedestal",new Vector3(0,.75f,0),new Vector3(.42f,1.5f,.28f),white,true);B(p,"Face",new Vector3(0,1.05f,-.15f),new Vector3(.34f,.6f,.025f),dark);B(p,"Screen",new Vector3(0,1.18f,-.169f),new Vector3(.23f,.2f,.015f),glass);StaffRoomModels.Rod(p,"Cable",new Vector3(.26f,1,-.08f),new Vector3(.33f,.25f,-.08f),.022f,dark);B(p,"Plug",new Vector3(.28f,1.07f,-.09f),new Vector3(.07f,.18f,.09f),green);}
        if(name=="ParkedCar")
        {
            B(p,"Body",new Vector3(0,.65f,0),new Vector3(4.4f,.65f,1.8f),red,true,.18f);B(p,"Cabin",new Vector3(-.2f,1.15f,0),new Vector3(2.4f,.65f,1.54f),red,true,.19f);
            foreach(float z in new[]{-.78f,.78f}){B(p,"Side glass",new Vector3(-.2f,1.22f,z),new Vector3(2.05f,.4f,.025f),glass);B(p,"Pillar",new Vector3(-.3f,1.22f,z*1.02f),new Vector3(.09f,.45f,.03f),dark);}
            foreach(float x in new[]{-1.43f,1.03f})B(p,"Windshield",new Vector3(x,1.18f,0),new Vector3(.03f,.43f,1.3f),glass);
            foreach(float x in new[]{-1.45f,1.45f})foreach(float z in new[]{-.9f,.9f}){C(p,"Tyre",new Vector3(x,.32f,z),.32f,.18f,dark,new Vector3(90,0,0));C(p,"Wheel",new Vector3(x,.32f,z*1.11f),.19f,.015f,steel,new Vector3(90,0,0));}
            foreach(float z in new[]{-.6f,.6f}){B(p,"Headlight",new Vector3(2.21f,.75f,z),new Vector3(.025f,.18f,.38f),white);B(p,"Tail light",new Vector3(-2.21f,.75f,z),new Vector3(.025f,.16f,.33f),yellow);}
        }
    }
    static void B(Transform p,string name,Vector3 pos,Vector3 size,Material material,bool solid=false,float bevel=.025f)=>StaffRoomModels.Box(p,name,pos,size,material,solid,bevel);
    static void C(Transform p,string name,Vector3 pos,float radius,float height,Material material,Vector3 rotation)=>StaffRoomModels.Cylinder(p,name,pos,radius,height,material,rotation);
}
