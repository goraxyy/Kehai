using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Open-air operations yard: power, returns, pallets and three separate waste streams.
public static class ServiceYardBuilder
{
    static Material concrete,metal;
    public static void Build(Transform root,GameObject bag)
    {
        concrete=StaffRoomModels.Material("Yard Concrete",new Color(.32f,.35f,.34f));
        metal=StaffRoomModels.Material("Yard Galvanized",new Color(.43f,.48f,.48f));
        OpenRoof();
        OpenYardWall(root);
        var back=GameObject.Find("Layout_15x15/Backstreet");
        if(back!=null)foreach(Transform t in back.transform)
        {
            if(t.name.StartsWith("GarbageCan")||t.GetComponentInChildren<TrashContainer>(true)!=null)
            {Undo.RecordObject(t.gameObject,"Replace service yard props");t.gameObject.SetActive(false);}
            if(!t.name.StartsWith("ElectricBox"))continue;
            // Keep power-gameplay components and references, replace only their old art.
            var old=t.Find("New power enclosure");if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);
            foreach(var r in t.GetComponentsInChildren<Renderer>()){Undo.RecordObject(r,"Remodel electrical enclosure");r.enabled=false;}
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(StoreFinishModels.Prefab("UtilityCabinet"),t);
            visual.name="New power enclosure";visual.transform.SetPositionAndRotation(new Vector3(24.55f,0,t.position.z),Quaternion.Euler(0,90,0));
            // Existing electrical interaction remains the owner of the new enclosure.
            foreach(var c in visual.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
        }
        foreach(var light in Object.FindObjectsByType<Light>())
        {var p=light.transform.position;if(p.x>10&&p.x<25&&p.z>-175&&p.z<-150&&p.y>4){Undo.RecordObject(light.gameObject,"Remove indoor yard lights");light.gameObject.SetActive(false);}}
        foreach(var r in Object.FindObjectsByType<MeshRenderer>())
        {var p=r.bounds.center;if(p.x>10&&p.x<25&&p.z>-175&&p.z<-150&&p.y>4&&r.transform.root.name=="CeilingLamps")r.gameObject.SetActive(false);}
        for(int i=0;i<3;i++)Container(root,(WasteStream)i,new Vector3(23.97f,0,-161f-i*3.2f),90);
        Place(root,"UtilityCabinet","Outdoor maintenance cabinet",new Vector3(10.46f,0,-153.7f),-90);
        Place(root,"ServiceCage","Returns roll-cage bay",new Vector3(10.71f,0,-157f),-90);
        for(int i=0;i<3;i++)Place(root,"PalletStack","Sorted empty pallets",new Vector3(10.71f,0,-160.4f-i*1.3f),-90);
        Place(root,"CardboardBales","Baled cardboard awaiting collection",new Vector3(10.56f,0,-167f),-90);
        Place(root,"ServiceCage","Empty returns cage",new Vector3(10.71f,0,-171.2f),-90);
        ColdStoreBuilder.Label(root,"SERVICE YARD / STAFF ONLY",new Vector3(17.5f,2.9f,-150.12f),0,6,.4f);
        ColdStoreBuilder.Label(root,"SORT BAGS BEFORE DISPOSAL",new Vector3(24.82f,2.7f,-164.2f),90,7,.38f);
        foreach(float z in new[]{-154f,-161f,-169f})
        {
            Place(root,"WallLamp","Weatherproof service light",new Vector3(24.84f,3.4f,z),90);
            var l=new GameObject("Service-yard light");l.transform.SetParent(root,false);l.transform.SetPositionAndRotation(new Vector3(23.9f,3.35f,z),Quaternion.Euler(90,0,0));
            var light=l.AddComponent<Light>();light.type=LightType.Spot;light.range=9;light.spotAngle=110;light.intensity=5;light.color=new Color(.83f,.9f,1);
        }
        // Drain and painted working boundaries, with a continuous central cart lane.
        var drain=ColdStoreBuilder.Primitive(root,"Yard drain",new Vector3(17.5f,.026f,-162.5f),new Vector3(.18f,.018f,22),metal,false);drain.AddComponent<InteriorLightAnchor>().outdoors=true;
        foreach(float x in new[]{13.4f,21.9f})ColdStoreBuilder.Primitive(root,"Keep-clear lane marking",new Vector3(x,.027f,-162.5f),new Vector3(.08f,.008f,22),StaffRoomModels.Material("Safety Ochre",new Color(.75f,.55f,.12f)),false);
        foreach(float z in new[]{-123f,-145f,-179f})
        {Place(root,"TallPlant","West exterior planter",new Vector3(9.4f,0,z),90);Place(root,"TallPlant","East exterior planter",new Vector3(85.6f,0,z),-90);}
        foreach(float x in new[]{29f,53f,78f})Place(root,"LobbyPlanter","Rear exterior planter",new Vector3(x,0,-190.6f),0);
        Station(root,"Service-yard recycling",new Vector3(21,0,-150.38f),0,false,bag);
    }
    static GameObject Place(Transform p,string model,string name,Vector3 at,float yaw)
    {
        var prefab=StoreFinishModels.Prefab(model)??PublicInteriorModels.Prefab(model)??StaffRoomModels.Prefab(model);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,p);go.name=name;go.transform.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));
        var anchor=go.GetComponent<InteriorLightAnchor>();if(anchor!=null)anchor.outdoors=true;return go;
    }
    static void Container(Transform p,WasteStream stream,Vector3 at,float yaw)
    {
        var go=Place(p,"OutdoorWasteContainer",stream+" collection container",at,yaw);
        var trigger=new GameObject("Deposit "+stream);trigger.transform.SetParent(go.transform,false);
        var volume=trigger.AddComponent<BoxCollider>();volume.isTrigger=true;volume.center=new Vector3(0,.85f,0);volume.size=new Vector3(1.9f,1.5f,1.1f);
        trigger.AddComponent<TrashContainer>().stream=stream;
        var mat=StaffRoomModels.Material(stream+" recycling",WasteStreams.ColorOf(stream));
        ColdStoreBuilder.Primitive(go.transform,"Stream panel",new Vector3(0,.95f,-.75f),new Vector3(1.4f,.5f,.035f),mat,false);
        ColdStoreBuilder.Label(go.transform,stream.ToString().ToUpperInvariant(),new Vector3(0,.95f,-.775f),0,1.25f,.22f,false);
    }
    public static void Station(Transform p,string name,Vector3 at,float yaw,bool customers,GameObject bag)
    {
        var root=new GameObject(name).transform;root.SetParent(p,false);root.SetPositionAndRotation(at,Quaternion.Euler(0,yaw,0));
        for(int i=0;i<3;i++)
        {
            var stream=(WasteStream)i;var go=(GameObject)PrefabUtility.InstantiatePrefab(StoreFinishModels.Prefab("RecyclingBin"),root);
            go.name=stream+" bin";go.transform.localPosition=new Vector3((i-1)*.54f,0,0);
            var can=go.AddComponent<Trashcan>();can.stream=stream;can.capacity=8;can.fullBulge=0;can.customersAllowed=customers;can.trashBagPrefab=bag;
            var point=new GameObject("Customer standing point").transform;point.SetParent(go.transform,false);point.localPosition=new Vector3(0,0,-1.25f);can.customerStandPoint=point;
            int layer=LayerMask.NameToLayer("Interactable");foreach(var c in go.GetComponentsInChildren<Collider>())c.gameObject.layer=layer;
            var outline=go.GetComponent<OutlineHighlight>();outline.shape=OutlineHighlight.OutlineShape.PerMesh;
            foreach(string guid in AssetDatabase.FindAssets("CustomerOutline t:Material")){outline.outlineMaterial=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));break;}
            var obstacle=go.AddComponent<UnityEngine.AI.NavMeshObstacle>();obstacle.shape=UnityEngine.AI.NavMeshObstacleShape.Box;obstacle.center=new Vector3(0,.5f,0);obstacle.size=new Vector3(.5f,1,.6f);obstacle.carving=true;
            ColdStoreBuilder.Primitive(go.transform,"Stream colour",new Vector3(0,.73f,-.322f),new Vector3(.42f,.24f,.02f),StaffRoomModels.Material(stream+" recycling",WasteStreams.ColorOf(stream)),false);
            ColdStoreBuilder.Label(go.transform,stream.ToString().ToUpperInvariant(),new Vector3(0,.73f,-.337f),0,.4f,.065f,false);
        }
    }
    static void OpenRoof()
    {
        var roof=GameObject.Find("Roof");if(roof==null)return;
        Undo.RegisterFullObjectHierarchyUndo(roof,"Open the service yard to the sky");
        var filter=roof.GetComponent<MeshFilter>();var renderer=roof.GetComponent<MeshRenderer>();if(filter==null||renderer==null)return;
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);Mesh unit=cube.GetComponent<MeshFilter>().sharedMesh;
        var pieces=new[]{new Bounds(new Vector3(55.75f,8.3f,-150),new Vector3(61.5f,.6f,83)),new Bounds(new Vector3(16.75f,8.3f,-129.25f),new Vector3(16.5f,.6f,41.5f)),new Bounds(new Vector3(16.75f,8.3f,-183.25f),new Vector3(16.5f,.6f,16.5f))};
        var combine=new CombineInstance[pieces.Length];
        var prior=roof.transform.Find("Roof collision pieces");if(prior!=null)Object.DestroyImmediate(prior.gameObject);
        var collisions=new GameObject("Roof collision pieces").transform;collisions.SetParent(roof.transform,false);
        for(int i=0;i<pieces.Length;i++)
        {
            Bounds b=pieces[i];combine[i]=new CombineInstance{mesh=unit,transform=roof.transform.worldToLocalMatrix*Matrix4x4.TRS(b.center,Quaternion.identity,b.size)};
            var c=new GameObject("Roof slab");c.transform.SetParent(collisions,false);c.transform.position=b.center;c.transform.localScale=Vector3.one;
            var box=c.AddComponent<BoxCollider>();box.size=Vector3.Scale(b.size,new Vector3(1/roof.transform.lossyScale.x,1/roof.transform.lossyScale.y,1/roof.transform.lossyScale.z));
        }
        Object.DestroyImmediate(cube);var mesh=new Mesh{name="Roof with open service yard"};mesh.CombineMeshes(combine,true,true);
        string path=StoreFinishModels.Folder+"/Meshes/Open yard roof.asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);}
        filter.sharedMesh=saved;renderer.sharedMaterial=StaffRoomModels.Material("Roof Graphite",new Color(.22f,.25f,.25f));
        foreach(var c in roof.GetComponents<Collider>())c.enabled=false;
    }
    static void OpenYardWall(Transform root)
    {
        // Staff room, storage and sales floor enclose three sides. The west side opens to service access.
        foreach(var r in Object.FindObjectsByType<MeshRenderer>())
        {
            Bounds b=r.bounds;
            if(b.center.x<9.5f||b.center.x>10.25f||b.size.x>.5f||b.size.z<2||b.max.z<=-175||b.min.z>=-150)continue;
            if(r.transform.root.name!="Layout_15x15"&&!r.transform.IsChildOf(root))continue;
            Undo.RecordObject(r.gameObject,"Open west service access");r.gameObject.SetActive(false);
            foreach(var range in new[]{new Vector2(b.min.z,Mathf.Min(-175,b.max.z)),new Vector2(Mathf.Max(-150,b.min.z),b.max.z)})
            {
                if(range.y-range.x<.01f)continue;
                var size=b.size;size.z=range.y-range.x;var pos=b.center;pos.z=(range.x+range.y)*.5f;
                var copy=ColdStoreBuilder.Primitive(root,"Wall beside open service access",pos,size,r.sharedMaterial,true);copy.AddComponent<InteriorLightAnchor>().outdoors=true;
            }
        }
    }
}
