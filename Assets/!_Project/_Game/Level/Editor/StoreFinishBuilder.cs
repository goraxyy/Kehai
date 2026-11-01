using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

// Proposed identity: KEHAI / EVERYDAY MARKET. Cream ribbed panels, charcoal concrete and
// muted red fascia; fictional Pipisi, Krunchos and Ramyum posters. No road or parking work.
public static class StoreFinishBuilder
{
    public const string RootName="Store - Finished";
    static Transform root;
    static Material cream,dark,red,metal,glass;
    static GameObject bag;
    static bool exterior;

    [MenuItem("Kehai/Store Finish/Build Sales Floor, Front and Backstreet")]
    public static void Build()
    {
        using var hierarchy = SceneHierarchyOrganizer.BuilderScope();
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Build in edit mode.");
        var layout=GameObject.Find("Layout_15x15");if(layout==null)throw new InvalidOperationException("Open SampleScene first.");
        var scene=layout.scene;Directory.CreateDirectory("Library/Kehai/SceneBackups");
        if(!string.IsNullOrEmpty(scene.path))File.Copy(scene.path,"Library/Kehai/SceneBackups/StoreFinish-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity",false);
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Finish supermarket");
        StoreFinishModels.BuildLibrary();
        PublicInteriorModels.BuildLibrary();
        cream=StaffRoomModels.Material("Facade Cream",new Color(.77f,.77f,.7f));
        dark=StaffRoomModels.Material("Facade Charcoal",new Color(.13f,.16f,.17f));
        red=StaffRoomModels.Material("Facade Red",new Color(.56f,.13f,.11f));
        metal=StaffRoomModels.Material("Facade Steel",new Color(.42f,.46f,.47f));
        glass=ColdStoreBuilder.Glass("Portal Glass",.13f);
        bag=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!_Project/_Game/Level/Prefabs/TrashBag.prefab");
        var old=GameObject.Find(RootName);if(old!=null)Undo.DestroyObjectImmediate(old);
        root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Finish supermarket");
        exterior=false;
        ColdStoreBuilder.Build();
        FinishFloors();FinishCheckouts(layout.transform.Find("Cashierdesks"));FinishTools();
        exterior=true;Facade(layout.transform.Find("Walls"));Front();
        exterior=false;ServiceYardBuilder.Build(root,bag);BinsAndPlants();
        foreach(var door in Object.FindObjectsByType<AutoDoubleDoor>())
        {
            var sensor=door.GetComponent<BoxCollider>();if(sensor==null)continue;
            Undo.RecordObject(sensor,"Make automatic sensor two-sided");sensor.isTrigger=true;sensor.center=new Vector3(0,1.25f,0);sensor.size=new Vector3(4,2.5f,5);
            foreach(Transform t in door.transform)if(t.name=="LeftDoor"||t.name=="RightDoor")
            {var r=t.GetComponent<Renderer>();if(r!=null){Undo.RecordObject(r,"Finish automatic door glass");r.sharedMaterial=glass;}DoorFrame(t);}
        }
        FinishWallsAndSecurity(layout.transform);
        EnvironmentPolishBuilder.ApplyDetails();EnvironmentPolishBuilder.RefineWC();PriceTagMaker.EnsureStripMaterials();
        ModelIslandLayout.Organize();RetailShelfFinish.Apply();
        // Include the pedestrian apron so customers can reach the front bins; no car surfaces.
        var surface=Kehai.Karen.KarenWorld.StoreSurface();
        if(surface!=null)
        {
            Vector3 centre=surface.transform.TransformPoint(surface.center);float south=centre.z-surface.size.z*.5f;
            if(centre.z+surface.size.z*.5f < -103f)
            {Undo.RecordObject(surface,"Include entrance apron in navigation");centre.z=(south-103f)*.5f;surface.center=surface.transform.InverseTransformPoint(centre);surface.size=new Vector3(surface.size.x,surface.size.y,-103f-south);}
        }
        StaffRoomBuilder.BakeNavigation();StaffRoomBuilder.RefreshLighting();
        EnvironmentPolishBuilder.Midnight();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(undo);
        Selection.activeGameObject=root.gameObject;Debug.Log("Supermarket finish built. Save SampleScene with Cmd+S. Roads and parking are unchanged.");
    }

    static void FinishFloors()
    {
        foreach(string name in new[]{"WC - Furnished","Lobby - Furnished"})
        {
            var room=GameObject.Find(name);if(room==null)continue;
            for(int i=room.transform.childCount-1;i>=0;i--)
                if(room.transform.GetChild(i).name=="Floor joint")Undo.DestroyObjectImmediate(room.transform.GetChild(i).gameObject);
            foreach(var r in room.GetComponentsInChildren<MeshRenderer>())if(r.name.EndsWith("floor",StringComparison.OrdinalIgnoreCase))
            {Undo.RecordObject(r,"Use one tiled floor surface");r.sharedMaterial=FloorMaterial(name.StartsWith("WC")?"WC Tiles":"Lobby Tiles",r.bounds.size.x,r.bounds.size.z,name.StartsWith("WC")?1.325f:2.5f,new Color(.48f,.52f,.49f));}
        }
        Floor("Sales floor",new Vector3(55,.012f,-147.5f),new Vector3(59.85f,.024f,54.85f),2.5f,new Color(.51f,.53f,.51f));
        if(GameObject.Find(ParkingLotBuilder.RootName)==null)Floor("Forecourt floor",new Vector3(55,.012f,-107.3f),new Vector3(60,.024f,5.25f),1.25f,new Color(.52f,.5f,.45f));
        Floor("Backstreet floor",new Vector3(17.5f,.012f,-162.5f),new Vector3(14.85f,.024f,24.85f),2.5f,new Color(.35f,.38f,.36f));
    }
    static void Floor(string name,Vector3 at,Vector3 size,float pitch,Color color)
        =>Box(name,at,size,FloorMaterial(name,size.x,size.z,pitch,color),false,true);
    public static Material FloorMaterial(string name,float width,float depth,float pitch,Color color)
    {
        string folder=StoreFinishModels.Folder+"/Materials";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        string path=folder+"/Tile Pattern.asset";var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(texture==null)
        {
            texture=new Texture2D(128,128,TextureFormat.RGBA32,true){name="Tile Pattern",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            var pixels=new Color[128*128];for(int y=0;y<128;y++)for(int x=0;x<128;x++)pixels[y*128+x]=x<1||y<1?new Color(.55f,.55f,.55f):Color.white;
            texture.SetPixels(pixels);texture.Apply(true);AssetDatabase.CreateAsset(texture,path);
        }
        var mat=AssetDatabase.LoadAssetAtPath<Material>(folder+"/"+name+".mat");
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,enableInstancing=true};AssetDatabase.CreateAsset(mat,folder+"/"+name+".mat");}
        mat.SetColor("_BaseColor",color);mat.SetTexture("_BaseMap",texture);mat.SetTextureScale("_BaseMap",new Vector2(width/pitch,depth/pitch));mat.SetFloat("_Smoothness",.18f);EditorUtility.SetDirty(mat);return mat;
    }

    static void FinishCheckouts(Transform desks)
    {
        if(desks==null)return;int index=0;
        foreach(Transform desk in desks)
        {
            var main=desk.Find("Cube");if(main==null)continue;
            Vector3 centre=main.position;centre.y=0;
            foreach(Transform t in desk)if(t.name.StartsWith("Cube",StringComparison.Ordinal))
            {Undo.RecordObject(t.gameObject,"Retain original checkout under new model");t.gameObject.SetActive(false);}
            foreach(var shelf in desk.GetComponentsInChildren<ShelfUnit>()){Undo.RecordObject(shelf.gameObject,"Replace checkout impulse shelf");shelf.gameObject.SetActive(false);}
            var model=Place("Checkout","Checkout "+(++index),centre.x,centre.z,0);
            model.GetComponent<InteriorLightAnchor>().localPoint=new Vector3(1.8f,0,0);
            Sign("CHECKOUT 0"+index,new Vector3(centre.x+.4f,2.8f,centre.z+2f),0,2.5f,.45f,dark);
        }
    }

    static void Facade(Transform walls)
    {
        // Skin only existing opaque wall segments. Door openings retain their exact widths.
        foreach(var r in walls.GetComponentsInChildren<MeshRenderer>())
        {
            Bounds b=r.bounds;Undo.RecordObject(r,"Finish store walls");r.sharedMaterial=cream;
            bool north=Mathf.Abs(b.center.z+110)<.2f&&b.size.z<.25f;
            bool south=Mathf.Abs(b.center.z+190)<.2f&&b.size.z<.25f;
            bool west=Mathf.Abs(b.center.x-10)<.2f&&b.size.x<.25f;
            bool east=Mathf.Abs(b.center.x-85)<.2f&&b.size.x<.25f;
            if(!(north||south||west||east)||b.min.y>=4)continue;
            Vector3 c=b.center,s=b.size;bool alongX=north||south;
            // The separate upper cladding starts at 4m: never cover it with a second skin.
            float top=Mathf.Min(4f,b.max.y);c.y=(b.min.y+top)*.5f;s.y=top-b.min.y;
            if(alongX){c.z=north?-109.88f:-190.12f;s.z=.18f;}else{c.x=west?9.88f:85.12f;s.x=.18f;}
            Box("Cream wall panel",c,s,cream,true,true);
            if(b.min.y<.1f)
            {
                c.y=.4f;s.y=.8f;if(alongX){c.z+=north?.011f:-.011f;s.z+=.02f;}else{c.x+=west?-.011f:.011f;s.x+=.02f;}
                Box("Charcoal plinth",c,s,dark,true,true);
            }
        }
        foreach(int side in new[]{0,1,2,3})
        {
            bool alongX=side<2;float length=alongX?75:80;
            Vector3 c=alongX?new Vector3(47.5f,5.5f,side==0?-109.88f:-190.12f):new Vector3(side==2?9.88f:85.12f,5.5f,-150);
            Box("Upper cream cladding",c,alongX?new Vector3(length,3,.18f):new Vector3(.18f,3,length),cream,true,true);
            c.y=7.35f;Box("Red brand fascia",c,alongX?new Vector3(length,.7f,.22f):new Vector3(.22f,.7f,length),red,true,true);
            c.y=8.35f;Box("Parapet",c,alongX?new Vector3(length,1.3f,.24f):new Vector3(.24f,1.3f,length),cream,true,true);
            c.y=9.03f;Box("Parapet cap",c,alongX?new Vector3(length+.2f,.12f,.34f):new Vector3(.34f,.12f,length+.2f),dark,true,true);
            for(float t=-length*.5f+1;t<length*.5f;t+=2.5f)
            {
                var rib=c;rib.y=5.5f;if(alongX)rib.x+=t;else rib.z+=t;
                Box("Panel rib",rib,alongX?new Vector3(.035f,3,.21f):new Vector3(.21f,3,.035f),metal,false,true);
            }
        }
        // Roof services sit above the existing roof (top 8.6m), hidden partly by the parapet.
        foreach(float x in new[]{34f,64f})foreach(float z in new[]{-139f,-164f})Place("HVAC","Rooftop plant",x,z,0,8.6f);
        foreach(float z in new[]{-115f,-140f,-170f,-185f})
        {
            Place("Downpipe","West downpipe",9.72f,z,90);
            Place("Downpipe","East downpipe",85.28f,z,-90);
        }
        Sign("KEHAI",new Vector3(85.26f,5.65f,-148),-90,13,1.6f,dark);
        Sign("KEHAI  /  SERVICE",new Vector3(47.5f,5.8f,-190.28f),0,16,1.2f,dark);
    }

    static void FinishTools()
    {
        var flashlight=GameObject.Find("Flashlight");
        if(flashlight!=null)
        {
            ReplaceToolVisual(flashlight,"Flashlight",new Vector3(0,-.073f,0),new[]{"Long","Short"});
            Undo.RecordObject(flashlight.transform,"Place flashlight in staff room");
            flashlight.transform.SetPositionAndRotation(new Vector3(15.5f,.973f,-146.4f),Quaternion.identity);
            var collider=flashlight.GetComponent<BoxCollider>();if(collider!=null){Undo.RecordObject(collider,"Fit flashlight collider");collider.center=Vector3.zero;collider.size=new Vector3(.34f,.146f,.146f);}
            var torch=flashlight.GetComponent<Flashlight>();
            if(torch!=null&&torch.beam!=null){Undo.RecordObject(torch.beam.transform,"Align lens and beam");torch.beam.transform.localPosition=new Vector3(.175f,0,0);}
        }
        var mop=GameObject.Find("Mop");
        if(mop!=null)
        {
            ReplaceToolVisual(mop,"Mop",new Vector3(0,-1,0),new[]{"Cylinder","Cube"});
            Undo.RecordObject(mop.transform,"Set mop on staff-room floor");
            // The angled handle rests against the east wall; the pad stays outside the door lane.
            mop.transform.SetPositionAndRotation(new Vector3(24.48f,1,-140.5f),Quaternion.Euler(0,0,-25));
            Bounds b=StaffRoomBuilder.BoundsOf(mop.transform.Find("Finished model").gameObject);mop.transform.position+=Vector3.up*(.024f-b.min.y);
            var collider=mop.GetComponent<BoxCollider>();if(collider!=null){Undo.RecordObject(collider,"Fit mop collider");collider.center=new Vector3(0,-.03f,0);collider.size=new Vector3(.48f,1.94f,.25f);}
            var home=mop.GetComponent<ItemHome>();if(home!=null&&home.snapPoint!=null)
            {Undo.RecordObject(home.snapPoint,"Keep mop home aligned");home.snapPoint.SetPositionAndRotation(mop.transform.position,mop.transform.rotation);}
        }
    }
    static void ReplaceToolVisual(GameObject tool,string model,Vector3 offset,string[] legacy)
    {
        Undo.RegisterFullObjectHierarchyUndo(tool,"Remodel "+model);
        var prior=tool.transform.Find("Finished model");if(prior!=null)Undo.DestroyObjectImmediate(prior.gameObject);
        foreach(string name in legacy){var old=tool.transform.Find(name);if(old!=null)old.gameObject.SetActive(false);}
        var visual=(GameObject)PrefabUtility.InstantiatePrefab(StoreFinishModels.Prefab(model),tool.transform);
        visual.name="Finished model";visual.transform.localPosition=offset;visual.transform.localRotation=Quaternion.identity;
        var anchor=visual.GetComponent<InteriorLightAnchor>();if(anchor!=null)Object.DestroyImmediate(anchor);
        foreach(var r in visual.GetComponentsInChildren<Renderer>())r.renderingLayerMask=(uint)RoomLighting.Moving;
    }

    static void Front()
    {
        Sign("KEHAI",new Vector3(55,5.9f,-109.7f),180,18,1.65f,red);
        Sign("EVERYDAY MARKET",new Vector3(55,4.65f,-109.69f),180,15,.5f,dark);
        Box("Entrance canopy",new Vector3(55,4.2f,-107.15f),new Vector3(46,.32f,5.7f),dark,true,true);
        Box("Canopy underside",new Vector3(55,4.025f,-107.15f),new Vector3(45.6f,.03f,5.3f),cream,false,true);
        foreach(float x in new[]{33f,55f,77f})Box("Canopy column",new Vector3(x,2,-104.6f),new Vector3(.18f,4,.18f),dark,true,true);
        for(float x=34;x<78;x+=6)LightAt("Canopy light",new Vector3(x,3.97f,-106.5f));
        foreach(float x in new[]{42.5f,67.5f})
        {
            // Only the working door leaves are glass; no fake duplicate doors beside them.
            Box("Portal header",new Vector3(x,2.65f,-109.7f),new Vector3(3.05f,.22f,.12f),dark,false,true);
            Sign(x<50?"EXIT":"ENTRANCE",new Vector3(x,3.22f,-109.65f),180,3.2f,.4f,dark);
        }
        Poster("PIPISI","COLD DRINKS",51.2f,-109.58f,180,new Color(.12f,.23f,.48f));
        Poster("KRUNCHOS","SNACK BREAK",55,-109.58f,180,new Color(.7f,.46f,.12f));
        Poster("RAMYUM","WARM UP",58.8f,-109.58f,180,new Color(.6f,.15f,.1f));
        Sign("OPEN 24H",new Vector3(82.7f,4.6f,-109.6f),180,3,.65f,red);
        Place("BikeRack","Front bike rack",16.5f,-107.5f,0);
        foreach(float x in new[]{28f,48f,61f,80f})Place("LobbyPlanter","Front planter",x,-109.45f,180);
        Place("LobbyBench","Front bench",30.9f,-109.4f,180);
        Place("LobbyBench","Front bench east",76.8f,-109.4f,180);
        foreach(float x in new[]{39f,46f,64f,71f})Place("Bollard","Portal protection",x,-104.8f,0);
        Bin("Front bin west",35,-109.5f,180,true);Bin("Front bin east",73.7f,-109.5f,180,true);
    }


    static void BinsAndPlants()
    {
        // Retain the three earlier bin objects inactive for recovery, away from the new layout.
        foreach(var can in Object.FindObjectsByType<Trashcan>())
            if(!can.transform.IsChildOf(root)&&can.customerStandPoint==null)
            {Undo.RecordObject(can.gameObject,"Replace old bin placement");can.gameObject.SetActive(false);}
        foreach(float x in new[]{31f,54f,78f})Bin("Sales floor bin",x,-120.4f,0,true);
        foreach(float x in new[]{28f,51f,75f})Place("LobbyPlanter","Sales entrance greenery",x,-120.4f,0);
        foreach(string room in new[]{PublicInteriorBuilder.LobbyRoot,StaffRoomBuilder.RootName,OfficeRoomBuilder.RootName})
        {
            var go=GameObject.Find(room);if(go==null)continue;
            foreach(Transform t in go.transform)
                if(t.name.IndexOf("recycling",StringComparison.OrdinalIgnoreCase)>=0)
                {
                    Undo.RecordObject(t.gameObject,"Replace joined recycling station");t.gameObject.SetActive(false);
                    ServiceYardBuilder.Station(root,room+" recycling",t.position,t.eulerAngles.y,room==PublicInteriorBuilder.LobbyRoot,bag);
                }
        }
        var wc=GameObject.Find(PublicInteriorBuilder.WCRoot);
        if(wc!=null)foreach(Transform t in wc.transform)if(t.name.StartsWith("WC paper bin"))
        {Undo.RecordObject(t.gameObject,"Replace old WC bins");t.gameObject.SetActive(false);}
        ServiceYardBuilder.Station(root,"WC recycling",new Vector3(24.625f,0,-118.95f),90,false,bag);
    }
    static void Bin(string name,float x,float z,float yaw,bool customers)
        =>ServiceYardBuilder.Station(root,name,new Vector3(x,0,z),yaw,customers,bag);

    static void DoorFrame(Transform leaf)
    {
        var old=leaf.Find("Frame finish");if(old!=null)Object.DestroyImmediate(old.gameObject);
        var frame=new GameObject("Frame finish").transform;frame.SetParent(leaf,false);
        Vector3 scale=leaf.lossyScale;
        foreach(float x in new[]{-.725f,.725f})
            ColdStoreBuilder.Primitive(frame,"Side rail",new Vector3(x/scale.x,0,0),new Vector3(.045f/scale.x,2.5f/scale.y,.055f/scale.z),dark,false);
        foreach(float y in new[]{-1.225f,1.225f})
            ColdStoreBuilder.Primitive(frame,"Top / bottom rail",new Vector3(0,y/scale.y,0),new Vector3(1.5f/scale.x,.045f/scale.y,.055f/scale.z),dark,false);
        ColdStoreBuilder.Primitive(frame,"Visibility safety strip",Vector3.zero,new Vector3(1.4f/scale.x,.04f/scale.y,.026f/scale.z),cream,false);
    }
    static void FinishWallsAndSecurity(Transform layout)
    {
        foreach(var r in layout.GetComponentsInChildren<MeshRenderer>())
            if(r.sharedMaterial!=null&&r.sharedMaterial.name.IndexOf("Blue",StringComparison.OrdinalIgnoreCase)>=0&&r.GetComponentInParent<ShelfUnit>()==null)
            {Undo.RecordObject(r,"Unify old door surrounds");r.sharedMaterial=cream;}
        foreach(var d in Object.FindObjectsByType<AutoDoubleDoor>())
            foreach(var r in d.GetComponentsInChildren<MeshRenderer>())if(r.name!="LeftDoor"&&r.name!="RightDoor"&&r.transform.parent==d.transform)r.sharedMaterial=dark;
        foreach(float x in new[]{42.5f,67.5f})foreach(float dx in new[]{-1.85f,1.85f})
            Place("SecurityGate","Sales-floor security antenna",x+dx,-122.1f,0);
    }

    static GameObject Place(string model,string name,float x,float z,float yaw,float y=0)
    {
        var prefab=StoreFinishModels.Prefab(model)??PublicInteriorModels.Prefab(model)??StaffRoomModels.Prefab(model);
        if(prefab==null)throw new InvalidOperationException("Missing model "+model);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);go.name=name;go.transform.SetPositionAndRotation(new Vector3(x,y,z),Quaternion.Euler(0,yaw,0));
        var anchor=go.GetComponent<InteriorLightAnchor>();if(anchor!=null)anchor.outdoors=exterior;
        return go;
    }
    static GameObject Box(string name,Vector3 at,Vector3 size,Material mat,bool solid,bool anchored)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);go.transform.position=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());
        if(anchored)go.AddComponent<InteriorLightAnchor>().outdoors=exterior;
        if(at.y>3.8f)go.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        return go;
    }
    static void LightAt(string name,Vector3 at)
    {
        var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.position=at;go.transform.rotation=Quaternion.Euler(90,0,0);
        var light=go.AddComponent<Light>();light.type=LightType.Spot;light.spotAngle=110;light.innerSpotAngle=75;light.range=8;light.intensity=4;light.shadows=LightShadows.None;light.color=new Color(1,.92f,.79f);
    }
    static void Sign(string text,Vector3 at,float yaw,float width,float height,Material color)
    {
        var go=Box("Sign - "+text,at,new Vector3(width,height,.09f),color,false,true);go.transform.rotation=Quaternion.Euler(0,yaw,0);
        var label=new GameObject("Letters").transform;label.SetParent(go.transform,false);label.localScale=new Vector3(1/width,1/height,1/.09f);label.localPosition=new Vector3(0,0,-.56f);
        var tmp=label.gameObject.AddComponent<TextMeshPro>();tmp.font=TMP_Settings.defaultFontAsset;tmp.text=text;tmp.fontSize=height*20;tmp.enableAutoSizing=true;tmp.fontSizeMin=.5f;tmp.fontSizeMax=height*32;tmp.alignment=TextAlignmentOptions.Center;tmp.color=new Color(.94f,.93f,.85f);tmp.rectTransform.sizeDelta=new Vector2(width-.15f,height*.9f);tmp.textWrappingMode=TextWrappingModes.NoWrap;
    }
    static void Poster(string brand,string line,float x,float z,float yaw,Color color)
    {
        var mat=StaffRoomModels.Material("Poster "+brand,color);
        Sign(brand+"\n"+line,new Vector3(x,1.6f,z),yaw,2.1f,2.4f,mat);
    }
}
