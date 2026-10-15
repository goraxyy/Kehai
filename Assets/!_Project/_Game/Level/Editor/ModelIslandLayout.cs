using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// A stable catalogue, not an ever-growing sequence of "place after the last model".
// The island's transform stays put because its NavMeshSurface volume covers the store.
public static class ModelIslandLayout
{
    const string Staff="Staff_Furniture_Showcase",Office="Office_Furniture_Showcase",WC="WC_Furniture_Showcase",Lobby="Lobby_Furniture_Showcase",Fixtures="Shop_Fixtures_Showcase",Labels="Island_Layout";

    [MenuItem("Kehai/Staff Room/3. Organize Model Island")]
    public static void Organize()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Organize in edit mode.");
        var island=GameObject.Find("Models_Island");if(island==null)throw new InvalidOperationException("Models_Island is missing.");
        if(StaffRoomModels.Prefab("Locker")==null)StaffRoomModels.BuildLibrary();
        Undo.RegisterFullObjectHierarchyUndo(island,"Organize model island");
        foreach(string name in new[]{Staff,Office,WC,Lobby,Labels}){var old=island.transform.Find(name);if(old!=null)Undo.DestroyObjectImmediate(old.gameObject);}
        var staff=Group(island.transform,Staff);var labels=Group(island.transform,Labels);
        int index=0;
        foreach(string name in StaffRoomModels.Names)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(StaffRoomModels.Prefab(name),staff);
            go.name=name;
            VisualOnly(go);
            Slot(go,index++,4,4,6,6,labels);
        }
        var open=(GameObject)PrefabUtility.InstantiatePrefab(StaffRoomModels.Prefab("Locker"),staff);
        open.name="Locker - open interior";
        open.GetComponentInChildren<FurnitureDoor>().transform.localRotation=Quaternion.Euler(0,105,0);
        VisualOnly(open);Slot(open,index,4,4,6,6,labels);

        bool hasOffice=OfficeRoomModels.Prefab("ManagerDesk")!=null;
        if(hasOffice)
        {
            var office=Group(island.transform,Office);int officeIndex=0;
            foreach(string name in OfficeRoomModels.Names)
            {
                var go=(GameObject)PrefabUtility.InstantiatePrefab(OfficeRoomModels.Prefab(name),office);
                go.name=name;VisualOnly(go);Slot(go,officeIndex++,4,48,6,6,labels);
            }
        }

        bool hasPublic=PublicInteriorModels.Prefab("ToiletCabin")!=null;
        if(hasPublic)
        {
            PublicGallery(island.transform,WC,PublicInteriorModels.WC,48,labels);
            var wc=island.transform.Find(WC);
            var cabin=(GameObject)PrefabUtility.InstantiatePrefab(PublicInteriorModels.Prefab("ToiletCabin"),wc);
            cabin.name="Toilet cabin - open interior";cabin.GetComponentInChildren<FurnitureDoor>().transform.localRotation=Quaternion.Euler(0,-95,0);
            VisualOnly(cabin);Slot(cabin,PublicInteriorModels.WC.Length,44,48,6,6,labels);
            PublicGallery(island.transform,Lobby,PublicInteriorModels.Lobby,64,labels);
        }
        var fixtures=island.transform.Find(Fixtures);if(fixtures==null)fixtures=Group(island.transform,Fixtures);
        var loose=new List<Transform>();
        foreach(Transform t in island.transform)
            if(t.name!="Plane_test"&&t.name!=Staff&&t.name!=Office&&t.name!=WC&&t.name!=Lobby&&t.name!=Labels&&t.name!=Fixtures&&t.name!="Products_Showcase"&&t.name!="Boxes_Showcase")loose.Add(t);
        foreach(var t in loose)t.SetParent(fixtures,true);
        var sorted=new List<Transform>();foreach(Transform t in fixtures)sorted.Add(t);
        sorted.Sort((a,b)=>string.CompareOrdinal(a.name,b.name));
        for(int i=0;i<sorted.Count;i++)
        {
            var go=sorted[i].gameObject;VisualOnly(go);Slot(go,i,44,4,6,6,labels);
        }
        ArrangeRows(island.transform.Find("Products_Showcase"),false,labels);
        ArrangeRows(island.transform.Find("Boxes_Showcase"),true,labels);

        Heading(labels,"01 / STAFF ROOM",22,2,1);
        Heading(labels,"02 / SHOP FIXTURES",62,2,1);
        Heading(labels,"03 / PRODUCTS",100,2,1);
        Heading(labels,"04 / RESTOCKING BOXES",100,47,1);
        Heading(labels,hasOffice?"05 / OFFICE":"OFFICE / NEXT",22,47,1);
        Heading(labels,hasPublic?"06 / WC":"WC / NEXT",62,47,1);
        Heading(labels,hasPublic?"07 / LOBBY":"LOBBY / NEXT",62,62,1);
        Heading(labels,"EXTERIOR / PARKING / SERVICE / NEXT",42,88,.75f);
        Heading(labels,"REAL SCALE  /  METRES  /  VISUAL MODELS ONLY",42,116,.55f);

        Material border=StaffRoomModels.Material("Island Markings",new Color(.28f,.38f,.35f));
        Material slate=StaffRoomModels.Material("Island Floor",new Color(.56f,.6f,.57f));
        foreach(float x in new[]{2f,42f,82f,118f})Line(labels,new Vector3(x,.006f,60),new Vector3(.07f,.012f,116),border);
        foreach(float z in new[]{0f,45f,84f,114f})Line(labels,new Vector3(60,.006f,z),new Vector3(116,.012f,.07f),border);
        // Ten one-metre marks and a 2m height gauge make intended scale visible.
        for(int i=0;i<=10;i++)
        {
            Line(labels,new Vector3(4+i,.01f,111),new Vector3(.04f,.02f,.4f),border);
            Heading(labels,i.ToString(),4+i,110.45f,.17f);
        }
        Line(labels,new Vector3(4,1,108),new Vector3(.04f,2,.04f),border);
        Heading(labels,"2 m player height",6,108,.22f);
        var plane=island.transform.Find("Plane_test");
        if(plane!=null)
        {
            var filter=plane.GetComponent<MeshFilter>();
            if(filter!=null)
            {
                plane.rotation=Quaternion.identity;
                Vector3 scale=plane.localScale;scale.x=120/filter.sharedMesh.bounds.size.x;scale.z=120/filter.sharedMesh.bounds.size.z;
                plane.localScale=scale;plane.position=new Vector3(60,-.01f,60);
                plane.GetComponent<Renderer>().sharedMaterial=slate;
            }
        }
        EditorSceneManager.MarkSceneDirty(island.scene);
        Debug.Log("Models_Island: 120 x 120m, fixed categories, true-size models, 4m main aisles; future room and exterior plots reserved.");
    }

    static Transform Group(Transform parent,string name)
    {
        var t=new GameObject(name).transform;t.SetParent(parent,false);t.position=Vector3.zero;t.rotation=Quaternion.identity;t.localScale=Vector3.one;return t;
    }

    static void PublicGallery(Transform island,string group,string[] names,float z,Transform labels)
    {
        var parent=Group(island,group);
        for(int i=0;i<names.Length;i++)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(PublicInteriorModels.Prefab(names[i]),parent);
            go.name=names[i];VisualOnly(go);Slot(go,i,44,z,6,6,labels);
        }
    }

    static void Slot(GameObject go,int index,float startX,float startZ,int columns,float cell,Transform labels)
    {
        float x=startX+(index%columns+.5f)*cell,z=startZ+(index/columns+.5f)*cell;
        Bounds b=StaffRoomBuilder.BoundsOf(go);
        go.transform.position+=new Vector3(x-b.center.x,-b.min.y,z-b.center.z);
        string label=go.name+"\n"+b.size.x.ToString("0.##")+" × "+b.size.z.ToString("0.##")+" × "+b.size.y.ToString("0.##")+" m";
        Heading(labels,label,x,z-cell*.39f,.18f);
    }

    static void ArrangeRows(Transform root,bool boxes,Transform labels)
    {
        if(root==null)return;
        int rowIndex=0;
        foreach(Transform row in root)
        {
            float z=(boxes?51f:5f)+rowIndex++*(boxes?4.3f:3f);
            var models=new List<Transform>();var tags=new List<Transform>();
            foreach(Transform t in row)
            {
                if(t.GetComponent<TMP_Text>()!=null)tags.Add(t);else models.Add(t);
            }
            models.Sort((a,b)=>string.CompareOrdinal(a.name,b.name));
            int i=0;
            foreach(var t in models)
            {
                VisualOnly(t.gameObject);Bounds b=StaffRoomBuilder.BoundsOf(t.gameObject);
                float x=boxes?89f+i*3.4f:89f+i*1.15f;i++;
                t.position+=new Vector3(x-b.center.x,-b.min.y,z-b.center.z);
                if(!boxes)
                {
                    Transform tag=row.Find("Tag_"+t.name);
                    if(tag!=null)tag.position=new Vector3(x,.01f,z-.5f);
                }
            }
            foreach(var tag in tags)if(tag.name=="Sign")tag.position=new Vector3(85.8f,.01f,z);
        }
    }

    public static void VisualOnly(GameObject go)
    {
        if(PrefabUtility.IsPartOfPrefabInstance(go))
        {
            var root=PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if(root==go)PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        }
        var behaviours=go.GetComponentsInChildren<MonoBehaviour>(true);
        for(int i=behaviours.Length-1;i>=0;i--)
        {
            var b=behaviours[i];if(b==null||b is TMP_Text||b is TMP_SubMesh)continue;Object.DestroyImmediate(b);
        }
        foreach(var c in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(c);
        foreach(var j in go.GetComponentsInChildren<Joint>(true))Object.DestroyImmediate(j);
        foreach(var r in go.GetComponentsInChildren<Rigidbody>(true))Object.DestroyImmediate(r);
        foreach(var a in go.GetComponentsInChildren<AudioSource>(true))Object.DestroyImmediate(a);
        foreach(var n in go.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true))Object.DestroyImmediate(n);
        foreach(var n in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))Object.DestroyImmediate(n);
        foreach(var l in go.GetComponentsInChildren<Light>(true))Object.DestroyImmediate(l);
        foreach(Transform t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("Ignore Raycast");
    }

    static void Heading(Transform p,string value,float x,float z,float size)
    {
        StaffRoomBuilder.Text(p,value,new Vector3(x,.018f,z),0,size,new Vector2(34,1.5f),true);
        var t=p.GetChild(p.childCount-1);t.rotation=Quaternion.Euler(90,0,0);
    }

    static void Line(Transform p,Vector3 at,Vector3 size,Material mat)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Gallery line";go.transform.SetParent(p,false);
        go.transform.position=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;Object.DestroyImmediate(go.GetComponent<Collider>());
    }
}
