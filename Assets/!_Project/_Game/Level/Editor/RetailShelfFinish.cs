using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Keep the original transforms, board heights, price strips, stock slots and colliders.
// All new metalwork fits inside the original local bounds, including pillar variants.
public static class RetailShelfFinish
{
    const string Detail="Retail shelf details";
    static Material board,frame,slot;
    static Mesh cube;
    static readonly Dictionary<string,GameObject> details=new Dictionary<string,GameObject>();

    public static void Apply()
    {
        board=StaffRoomModels.Material("Shelf Enamel",new Color(.65f,.67f,.66f));
        frame=StaffRoomModels.Material("Retail Grey",new Color(.48f,.5f,.51f));
        slot=StaffRoomModels.Material("Shelf Slot Grey",new Color(.24f,.27f,.28f));
        StaffRoomModels.BuildCustom("ShelfEdgeCube",StoreFinishModels.Folder,p=>
            StaffRoomModels.Box(p,"Edge cube",Vector3.zero,Vector3.one,board,false,.006f));
        cube=AssetDatabase.LoadAssetAtPath<Mesh>(StoreFinishModels.Folder+"/Meshes/ShelfEdgeCube Body.asset");
        details.Clear();int count=0;
        var layout=GameObject.Find("Layout_15x15");
        if(layout!=null)foreach(var t in layout.GetComponentsInChildren<Transform>())
        {
            if(t==null||!t.name.StartsWith("Shelf",System.StringComparison.OrdinalIgnoreCase))continue;
            bool hasDeck=false;foreach(Transform child in t)if(ShelfGrid.IsDeck(child))hasDeck=true;
            if(!hasDeck)continue;
            if(t.GetComponent<ShelfUnit>()==null)Undo.AddComponent<ShelfUnit>(t.gameObject);
            Normalize(t);Style(t.gameObject);count++;
        }
        var fixtures=GameObject.Find("Models_Island/Shop_Fixtures_Showcase");
        if(fixtures!=null)foreach(Transform t in fixtures.transform)if(t.name.StartsWith("Shelf")){Normalize(t);Style(t.gameObject);}
        AssetDatabase.SaveAssets();Debug.Log("Finished "+count+" shelf units without changing their stock geometry or dimensions.");
    }

    static void Style(GameObject bay)
    {
        var old=bay.transform.Find(Detail);if(old!=null)Object.DestroyImmediate(old.gameObject);
        var parts=new List<Renderer>();Bounds bounds=new Bounds();bool first=true;
        foreach(Transform t in bay.transform)
        {
            var r=t.GetComponent<MeshRenderer>();if(r==null||t.name.StartsWith(ShelfGrid.StripPrefix))continue;
            if(!t.name.StartsWith("Wall",System.StringComparison.OrdinalIgnoreCase)&&!ShelfGrid.IsDeck(t))continue;
            parts.Add(r);Bounds b=LocalBounds(bay.transform,r.bounds);
            if(first){bounds=b;first=false;}else bounds.Encapsulate(b);
            Undo.RecordObject(r,"Finish shelf metal");r.sharedMaterial=ShelfGrid.IsDeck(t)?board:frame;
            var filter=t.GetComponent<MeshFilter>();if(filter!=null){Undo.RecordObject(filter,"Bevel shelf edges");filter.sharedMesh=cube;}
            if(PrefabUtility.IsPartOfPrefabInstance(r))PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            if(filter!=null&&PrefabUtility.IsPartOfPrefabInstance(filter))PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        }
        if(first)return;
        var source=PrefabUtility.GetCorrespondingObjectFromSource(bay);
        string key="ShelfDetail_"+(source!=null?source.name:bay.name);
        foreach(char c in Path.GetInvalidFileNameChars())key=key.Replace(c,'_');
        if(!details.TryGetValue(key,out var prefab))
        {
            StaffRoomModels.BuildCustom(key,StoreFinishModels.Folder,p=>MakeDetail(p,bay.transform,parts,bounds));
            details[key]=prefab=StoreFinishModels.Prefab(key);
        }
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,bay.transform);go.name=Detail;
        // Let the existing shelf's rendering-layer assignment handle both sides of the aisle.
        var anchor=go.GetComponent<InteriorLightAnchor>();if(anchor!=null)Object.DestroyImmediate(anchor);
    }

    static void MakeDetail(Transform p,Transform bay,List<Renderer> parts,Bounds bounds)
    {
        foreach(var r in parts)
        {
            Bounds b=LocalBounds(bay,r.bounds);
            if(ShelfGrid.IsDeck(r.transform))
            {
                if(b.center.y>.4f)continue;
                Box(p,"Recessed plinth",new Vector3(b.center.x,bounds.min.y+.08f,b.center.z),new Vector3(b.size.x*.96f,.16f,b.size.z*.9f),frame,bounds);
                continue;
            }
            bool alongX=b.size.x>b.size.z;float length=alongX?b.size.x:b.size.z;
            foreach(float end in new[]{-1f,1f})
            {
                Vector3 pos=b.center+(alongX?Vector3.right:Vector3.forward)*(length*.5f-.045f)*end;
                Box(p,"Upright",pos,new Vector3(.09f,b.size.y,.09f),frame,bounds);
            }
            Box(p,"Top cap",new Vector3(b.center.x,b.max.y-.025f,b.center.z),alongX?new Vector3(length,.05f,.1f):new Vector3(.1f,.05f,length),frame,bounds);
            int columns=Mathf.Max(1,Mathf.FloorToInt(length/.35f));
            for(int i=0;i<columns;i++)for(int j=0;j<7;j++)foreach(float side in new[]{-1f,1f})
            {
                float along=-length*.5f+(i+.5f)*length/columns;
                var pos=new Vector3(b.center.x,b.min.y+.32f+j*.22f,b.center.z);
                if(alongX){pos.x+=along;pos.z+=side*(b.extents.z+.002f);}
                else{pos.z+=along;pos.x+=side*(b.extents.x+.002f);}
                Box(p,"Slotted back panel",pos,alongX?new Vector3(.014f,.065f,.004f):new Vector3(.004f,.065f,.014f),slot,bounds);
            }
        }
    }
    static void Normalize(Transform bay)
    {
        bool shortBay=bay.name.IndexOf("short",System.StringComparison.OrdinalIgnoreCase)>=0;
        var decks=new List<Transform>();
        foreach(Transform t in bay)
        {
            if(ShelfGrid.IsDeck(t))decks.Add(t);
            if(!t.name.StartsWith("Wall",System.StringComparison.OrdinalIgnoreCase))continue;
            Undo.RecordObject(t,"Standardize shelf backing");var s=t.localScale;
            if(shortBay){s.y=1.25f;var pos=t.localPosition;pos.y=.625f;t.localPosition=pos;}
            if(s.x<s.z)s.x=.04f;else s.z=.04f;t.localScale=s;
        }
        decks.Sort((a,b)=>a.localPosition.y.CompareTo(b.localPosition.y));
        if(bay.name.StartsWith("Shelfpillar_b",System.StringComparison.OrdinalIgnoreCase)&&bay.Find("Solid lower plinth")==null&&decks.Count>=2)
        {
            var bottom=decks[0];float height=decks[1].localPosition.y-decks[1].localScale.y*.5f;
            var obsolete=new List<GameObject>();foreach(Transform t in bay)if(t.name.StartsWith(ShelfGrid.StripPrefix)&&t.localPosition.y<decks[1].localPosition.y-.03f)obsolete.Add(t.gameObject);
            foreach(var t in obsolete)Undo.DestroyObjectImmediate(t);
            var plinth=GameObject.CreatePrimitive(PrimitiveType.Cube);plinth.name="Solid lower plinth";plinth.transform.SetParent(bay,false);
            plinth.transform.localPosition=new Vector3(bottom.localPosition.x,height*.5f,bottom.localPosition.z);plinth.transform.localRotation=bottom.localRotation;
            plinth.transform.localScale=new Vector3(bottom.localScale.x,height,bottom.localScale.z);plinth.GetComponent<Renderer>().sharedMaterial=frame;
            Undo.DestroyObjectImmediate(bottom.gameObject);decks.RemoveAt(0);
        }
        for(int i=0;i<decks.Count;i++){Undo.RecordObject(decks[i].gameObject,"Name shelf decks");decks[i].name="Shelf deck "+(i+1);}
    }
    static void Box(Transform p,string name,Vector3 at,Vector3 size,Material mat,Bounds limit)
    {
        var lo=Vector3.Max(at-size*.5f,limit.min);var hi=Vector3.Min(at+size*.5f,limit.max);
        if(hi.x-lo.x<.001f||hi.y-lo.y<.001f||hi.z-lo.z<.001f)return;
        StaffRoomModels.Box(p,name,(lo+hi)*.5f,hi-lo,mat,false,.006f);
    }
    static Bounds LocalBounds(Transform parent,Bounds world)
    {
        var b=new Bounds(parent.InverseTransformPoint(world.center),Vector3.zero);
        for(int i=0;i<8;i++)b.Encapsulate(parent.InverseTransformPoint(world.center+Vector3.Scale(world.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
        return b;
    }
}
