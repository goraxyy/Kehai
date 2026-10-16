using System;
using System.IO;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Targeted finishing pass: preserves room roots, authored furniture positions and storage racks.
public static class EnvironmentPolishBuilder
{
    public const string RootName="Environment - Refined";
    static Transform root;
    static Material white,wall,sage,metal;

    [MenuItem("Kehai/Store Finish/Refine Fixtures, WC and Midnight Lighting")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Refine in edit mode.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Directory.CreateDirectory("Library/Kehai/SceneBackups");
        if(!string.IsNullOrEmpty(scene.path))File.Copy(scene.path,"Library/Kehai/SceneBackups/Refine-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity");
        StaffRoomModels.BuildLibrary();PublicInteriorModels.BuildLibrary();StoreFinishModels.BuildLibrary();
        PriceTagMaker.EnsureStripMaterials();ColdStoreBuilder.Build();
        ApplyDetails();RefineWC();
        ModelIslandLayout.Organize();RetailShelfFinish.Apply();
        StoreLayout.ApplyToScene();StorageBoxesPreview.Refresh(true);ShelfDrawer.Ensure();
        StaffRoomBuilder.BakeNavigation();StaffRoomBuilder.RefreshLighting();Midnight();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Environment refinement complete. Save SampleScene with Cmd+S.");
    }

    public static void ApplyDetails()
    {
        white=StaffRoomModels.Material("Door Frame White",new Color(.9f,.91f,.87f));
        wall=StaffRoomModels.Material("Room Walls",new Color(.83f,.84f,.79f));
        sage=StaffRoomModels.Material("Door Neutral Grey",new Color(.68f,.69f,.67f));
        metal=StaffRoomModels.Material("Brushed Steel",new Color(.51f,.57f,.58f));
        var old=GameObject.Find(RootName);if(old!=null)Undo.DestroyObjectImmediate(old);
        root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Refine environment");
        ReplaceLegacyWalls();FinishDoors();FitPowerAndContainers();MoveMopToStock();RaiseSunkenProps();ParkingLights();
    }

    // Refit portals without moving authored furniture, tools or WC props.
    public static void RefitDoorsAndUtilities()
    {
        white=StaffRoomModels.Material("Door Frame White",new Color(.9f,.91f,.87f));
        sage=StaffRoomModels.Material("Door Neutral Grey",new Color(.68f,.69f,.67f));
        metal=StaffRoomModels.Material("Brushed Steel",new Color(.51f,.57f,.58f));
        var existing=GameObject.Find(RootName);root=existing!=null?existing.transform:new GameObject(RootName).transform;
        for(int i=root.childCount-1;i>=0;i--)
            if(root.GetChild(i).name.StartsWith("White frame - ")||root.GetChild(i).name=="White automatic portal")Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
        FinishDoors();FitPowerAndContainers();
    }

    static void ReplaceLegacyWalls()
    {
        var legacy=GameObject.Find("Layout_15x15/Walls");if(legacy==null)return;
        foreach(var renderer in legacy.GetComponentsInChildren<MeshRenderer>())
        {
            var b=renderer.bounds;
            Box(root,"Finished wall - "+renderer.name,b.center,b.size,wall,true);
            Undo.RecordObject(renderer,"Retire prototype wall rendering");renderer.enabled=false;
            foreach(var collider in renderer.GetComponents<Collider>()){Undo.RecordObject(collider,"Retire prototype wall collision");collider.enabled=false;}
        }
    }

    static void FinishDoors()
    {
        foreach(var door in Object.FindObjectsByType<HingeDoor>())
        {
            if(door.name.StartsWith("Door",StringComparison.Ordinal))
            {
                foreach(Transform child in door.transform)
                    if(child.name=="Wall"||child.name=="Handle")
                    {foreach(var r in child.GetComponentsInChildren<Renderer>())r.enabled=false;foreach(var c in child.GetComponentsInChildren<Collider>())c.enabled=false;child.gameObject.SetActive(false);}
                var prior=door.transform.Find("Finished door leaf");if(prior!=null)Undo.DestroyObjectImmediate(prior.gameObject);
                var leaf=Box(door.transform,"Finished door leaf",new Vector3(0,1.23f,1),new Vector3(.065f,2.4f,1.84f),sage,true);
                foreach(float side in new[]{-1f,1f})
                {
                    Box(leaf.transform,"Handle plate",new Vector3(side*.045f/.065f,-.11f/2.4f,.7f/1.84f),new Vector3(.02f/.065f,.36f/2.4f,.14f/1.84f),metal,false);
                    Box(leaf.transform,"Horizontal lever",new Vector3(side*.09f/.065f,-.11f/2.4f,.7f/1.84f),new Vector3(.065f/.065f,.035f/2.4f,.22f/1.84f),metal,false);
                }
                var use=door.GetComponent<FurnitureDoor>()??door.gameObject.AddComponent<FurnitureDoor>();
                use.roomDoor=door;use.panel=leaf.GetComponent<BoxCollider>();use.label="door";door.aimedInteraction=true;
                var outline=door.GetComponent<OutlineHighlight>();AssignOutline(outline);
                leaf.layer=LayerMask.NameToLayer("Interactable");
                var frame=new GameObject("White frame - "+door.name).transform;frame.SetParent(root,false);frame.SetPositionAndRotation(door.transform.position,door.transform.rotation);
                foreach(float z in new[]{.02f,1.98f})Box(frame,"White jamb",new Vector3(0,1.25f,z),new Vector3(.20f,2.5f,.08f),white,true);
                Box(frame,"White header",new Vector3(0,2.54f,1),new Vector3(.20f,.08f,2.08f),white,true);
            }
        }
        foreach(var door in Object.FindObjectsByType<AutoDoubleDoor>())
        {
            var frame=new GameObject("White automatic portal").transform;frame.SetParent(root,false);frame.SetPositionAndRotation(door.transform.position,door.transform.rotation);
            foreach(float x in new[]{-1.6f,1.6f})Box(frame,"White jamb",new Vector3(x,1.3f,0),new Vector3(.12f,2.6f,.24f),white,true);
            Box(frame,"White header",new Vector3(0,2.6f,0),new Vector3(3.32f,.14f,.24f),white,true);
            foreach(var renderer in door.GetComponentsInChildren<MeshRenderer>())
            {
                if(renderer.name=="LeftDoor"||renderer.name=="RightDoor")continue;
                if(renderer.transform.parent==door.transform)
                {renderer.enabled=false;foreach(var collider in renderer.GetComponents<Collider>())collider.enabled=false;}
                else renderer.sharedMaterial=white;
            }
        }
    }

    static void FitPowerAndContainers()
    {
        foreach(var power in Object.FindObjectsByType<ElectricBox>())
        {
            var t=power.transform.Find("New power enclosure");if(t==null)continue;
            var scale=power.transform.lossyScale;t.localScale=new Vector3(1/scale.x,1/scale.y,1/scale.z);
            var b=StaffRoomBuilder.BoundsOf(t.gameObject);t.position+=new Vector3(24.9f-b.max.x,-b.min.y,0);
            foreach(var c in power.GetComponentsInChildren<Collider>())if(!c.transform.IsChildOf(t))c.enabled=false;
            if(t.GetComponentsInChildren<Collider>().Length==0)
            {var c=t.gameObject.AddComponent<BoxCollider>();c.center=t.GetComponent<MeshFilter>().sharedMesh.bounds.center;c.size=t.GetComponent<MeshFilter>().sharedMesh.bounds.size;}
            foreach(var c in t.GetComponentsInChildren<Collider>())c.gameObject.layer=LayerMask.NameToLayer("Interactable");
            FitPowerControls(power,t);
        }
        foreach(var container in Object.FindObjectsByType<TrashContainer>())
        {
            var t=container.transform.parent;if(t==null||!t.name.EndsWith("collection container"))continue;
            t.localScale=new Vector3(2,1,1.5f);t.position=new Vector3(23.86f,0,-157f-(int)container.stream*6f);
            var backing=t.Find("Label backing");if(backing!=null)Undo.DestroyObjectImmediate(backing.gameObject);
            foreach(var text in t.GetComponentsInChildren<TMP_Text>())
            {WhiteLabel(text);text.transform.localPosition=new Vector3(0,.95f,-.782f);text.transform.localScale=new Vector3(.5f,1,1f/1.5f);text.rectTransform.sizeDelta=new Vector2(2.5f,.35f);}
        }
        foreach(var bin in Object.FindObjectsByType<Trashcan>())
        {
            if(bin.transform.Find("Stream colour")==null)continue;
            var backing=bin.transform.Find("Label backing");if(backing!=null)Undo.DestroyObjectImmediate(backing.gameObject);
            foreach(var text in bin.GetComponentsInChildren<TMP_Text>()){text.transform.localPosition=new Vector3(0,.73f,-.345f);WhiteLabel(text);}
        }
    }

    public static void FitPowerControls(ElectricBox power,Transform enclosure)
    {
        var front=enclosure.Find("Control face");if(front==null){front=new GameObject("Control face").transform;front.SetParent(enclosure,false);}
        var mesh=enclosure.GetComponent<MeshFilter>().sharedMesh;
        front.localPosition=new Vector3(mesh.bounds.center.x,0,mesh.bounds.min.z-.055f);front.localRotation=Quaternion.Euler(0,180,0);
        power.controlFace=front;EditorUtility.SetDirty(power);
    }

    public static void MoveMopToStock()
    {
        var mop=GameObject.Find("Mop");var stock=GameObject.Find("Stock_def");if(mop==null||stock==null)return;
        Undo.RecordObject(mop.transform,"Place mop beside testing crate");
        mop.transform.SetPositionAndRotation(new Vector3(stock.transform.position.x-1.35f,1,stock.transform.position.z-.32f),Quaternion.identity);
        var visual=mop.transform.Find("Finished model");
        var b=StaffRoomBuilder.BoundsOf(visual!=null?visual.gameObject:mop);mop.transform.position+=Vector3.up*(.025f-b.min.y);
        var home=mop.GetComponent<ItemHome>();if(home!=null&&home.snapPoint!=null)home.snapPoint.SetPositionAndRotation(mop.transform.position,mop.transform.rotation);
    }

    static void RaiseSunkenProps()
    {
        var group=GameObject.Find("Layout_15x15");if(group==null)return;
        foreach(var r in group.GetComponentsInChildren<MeshRenderer>())
        {
            if(!r.enabled||!r.name.StartsWith("CarboardBox",StringComparison.Ordinal))continue;
            if(r.bounds.min.y>=.015f)continue;Undo.RecordObject(r.transform,"Lift box above floor");r.transform.position+=Vector3.up*(.025f-r.bounds.min.y);
        }
    }

    static void ParkingLights()
    {
        var parking=GameObject.Find(ParkingLotBuilder.RootName);if(parking==null)return;
        var old=new List<GameObject>();foreach(Transform t in parking.transform)if(t.name=="Parking lighting")old.Add(t.gameObject);
        foreach(var go in old)Undo.DestroyObjectImmediate(go);
        foreach(var at in ParkingLotBuilder.LightPositions())
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(StoreFinishModels.Prefab("ParkingLamp"),parking.transform);go.name="Parking lighting";go.transform.position=at;go.GetComponent<InteriorLightAnchor>().outdoors=true;
            var t=new GameObject("Downlight").transform;t.SetParent(go.transform,false);t.localPosition=new Vector3(0,5.8f,0);t.localRotation=Quaternion.Euler(90,0,0);
            var light=t.gameObject.AddComponent<Light>();light.type=LightType.Spot;light.spotAngle=125;light.range=20;light.intensity=55;light.color=new Color(1,.9f,.73f);
        }
    }

    public static void Midnight()
    {
        RenderSettings.skybox=null;RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.004f,.006f,.012f);RenderSettings.ambientEquatorColor=new Color(.002f,.003f,.006f);RenderSettings.ambientGroundColor=new Color(.001f,.0015f,.003f);RenderSettings.reflectionIntensity=.035f;
        foreach(var light in Object.FindObjectsByType<Light>())if(light.type==LightType.Directional)
        {Undo.RecordObject(light,"Midnight moonlight");light.color=new Color(.38f,.52f,.78f);light.intensity=.012f;light.transform.rotation=Quaternion.Euler(25,-35,0);}
        foreach(var camera in Object.FindObjectsByType<Camera>()){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.006f,.01f,.022f);}
    }

    public static void RefineWC()
    {
        var wc=GameObject.Find(PublicInteriorBuilder.WCRoot);if(wc==null)return;
        var remove=new List<GameObject>();
        foreach(Transform t in wc.transform)
            if(t.name=="WC finishing"||t.name=="Cleaning supplies"||t.name=="Label - CLEANING"||t.name.StartsWith("WC hand dryer")||t.name.StartsWith("WC paper")||t.name.StartsWith("Label - WASH")||t.name=="WC west plant")remove.Add(t.gameObject);
        foreach(var go in remove)Undo.DestroyObjectImmediate(go);
        foreach(var t in Object.FindObjectsByType<Transform>())if(t.name=="WC recycling")Undo.DestroyObjectImmediate(t.gameObject);
        var parent=new GameObject("WC finishing").transform;parent.SetParent(wc.transform,false);
        var stocked=Place(parent,PublicInteriorModels.Prefab("CleaningStation"),"Cleaning supplies",new Vector3(24.175f,.025f,-120.55f),180);
        var empty=Place(parent,PublicInteriorModels.Prefab("CleaningHideCabinet"),"Empty hiding cupboard",new Vector3(24.175f,.025f,-112.775f),0);
        foreach(var cabinet in new[]{stocked,empty})
        {
            var hinge=cabinet.GetComponentInChildren<FurnitureDoor>().transform;
            ColdStoreBuilder.Label(hinge,cabinet==stocked?"CLEANING":"EMPTY",new Vector3(.66f,1.95f,-.041f),0,.85f,.1f,false);
            foreach(var label in hinge.GetComponentsInChildren<TMP_Text>())WhiteLabel(label);
        }
        var mop=GameObject.Find("Mop");
        if(mop!=null)
        {
            var copy=Object.Instantiate(mop,parent);copy.name="WC cleaning mop";
            copy.transform.SetPositionAndRotation(stocked.transform.TransformPoint(new Vector3(-.38f,1.1f,-.05f)),stocked.transform.rotation);
            var visual=copy.transform.Find("Finished model");
            var b=StaffRoomBuilder.BoundsOf(visual!=null?visual.gameObject:copy);copy.transform.position+=Vector3.up*(.105f-b.min.y);
            var home=new GameObject("WC mop home").transform;home.SetParent(parent,false);home.SetPositionAndRotation(copy.transform.position,copy.transform.rotation);
            copy.GetComponent<ItemHome>().snapPoint=home;
        }
        Place(parent,PublicInteriorModels.Prefab("DryingStation"),"Combined towels and hand dryer",new Vector3(24.8f,1.12f,-115.15f),90);
        Place(parent,StaffRoomModels.Prefab("TallPlant"),"West wall centre pair - north",new Vector3(10.65f,.025f,-115.4167f),-90);
        Place(parent,StaffRoomModels.Prefab("TallPlant"),"West wall centre pair - south",new Vector3(10.65f,.025f,-117.9167f),-90);
        var bag=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/!_Project/_Game/Level/Prefabs/TrashBag.prefab");
        ServiceYardBuilder.Station(parent,"WC recycling west",new Vector3(15.2f,.025f,-116.6667f),90,false,bag);
        ServiceYardBuilder.Station(parent,"WC recycling east",new Vector3(19.5f,.025f,-116.6667f),-90,false,bag);
        var plate=StaffRoomModels.Material("WC Label Backing",new Color(.92f,.94f,.91f));
        foreach(float side in new[]{-1f,1f})
        {
            ColdStoreBuilder.Primitive(parent,"Handwashing sign backing",new Vector3(17.35f,2.18f,-116.6667f+side*.071f),new Vector3(3.25f,.18f,.015f),plate,false);
            ColdStoreBuilder.Label(parent,"WASH YOUR HANDS",new Vector3(17.35f,2.18f,-116.6667f+side*.082f),side<0?0:180,3,.10f);
        }
        foreach(var text in wc.GetComponentsInChildren<TMP_Text>())
        {text.enableAutoSizing=true;text.fontSizeMax=text.fontSize;text.fontSizeMin=text.fontSize*.65f;text.textWrappingMode=TextWrappingModes.NoWrap;}
    }

    static GameObject Place(Transform parent,GameObject prefab,string name,Vector3 at,float yaw)
    {var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=name;go.transform.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));return go;}
    static GameObject Box(Transform parent,string name,Vector3 at,Vector3 size,Material material,bool solid)
        =>ColdStoreBuilder.Primitive(parent,name,at,size,material,solid);
    static void WhiteLabel(TMP_Text label){label.color=Color.white;label.fontStyle=FontStyles.Bold;label.enableAutoSizing=true;label.fontSizeMax=label.fontSize;label.fontSizeMin=label.fontSize*.75f;}
    static void AssignOutline(OutlineHighlight outline)
    {foreach(string guid in AssetDatabase.FindAssets("CustomerOutline t:Material")){outline.outlineMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));break;}}
}
