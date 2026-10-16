using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Repairs the measured fixture fit without rebuilding authored rooms or their furniture.
public static class EnvironmentFitBuilder
{
    const string RootName="Environment - Fit corrections";

    [MenuItem("Kehai/Store Finish/Apply Compact Cold Room and Environment Fit")]
    public static void Rebuild()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Apply in edit mode.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(GameObject.Find("Layout_15x15")==null)throw new InvalidOperationException("Open the supermarket scene first.");
        System.IO.Directory.CreateDirectory("Library/Kehai/SceneBackups");
        EditorSceneManager.SaveScene(scene,"Library/Kehai/SceneBackups/Full-fit-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity",true);
        StaffRoomModels.BuildSelected("Locker","RoomDoor","ChangingCabin","Fridge");PublicInteriorModels.BuildLibrary();
        ColdStoreBuilder.Build();Apply();RetailShelfFinish.Apply();ModelIslandLayout.Organize();
        StoreLayout.ApplyToScene();StorageBoxesPreview.Refresh(true);ShelfDrawer.Ensure();
        StaffRoomBuilder.BakeNavigation();StaffRoomBuilder.RefreshLighting();EnvironmentPolishBuilder.Midnight();
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Kehai/Store Finish/Apply Door, Locker and Exterior Fit")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Apply in edit mode.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(GameObject.Find("Layout_15x15")==null)throw new InvalidOperationException("Open the supermarket scene first.");
        System.IO.Directory.CreateDirectory("Library/Kehai/SceneBackups");
        EditorSceneManager.SaveScene(scene,"Library/Kehai/SceneBackups/Fit-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".unity",true);
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Fit environment fixtures");
        var prior=GameObject.Find(RootName);if(prior!=null)Undo.DestroyObjectImmediate(prior);
        var root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Fit environment fixtures");
        RemoveRetiredProps();
        EnvironmentPolishBuilder.RefitDoorsAndUtilities();
        AlignSharedWall(root);FitLockersAndWhiteboard();FinishWallSurfaces();
        foreach(var lamp in Object.FindObjectsByType<Light>())
            if(lamp.transform.root.name==ParkingLotBuilder.RootName&&lamp.name=="Downlight")
            {Undo.RecordObject(lamp,"Brighten parking lamps");lamp.intensity=55;}
        EnvironmentPolishBuilder.Midnight();
        EditorSceneManager.MarkSceneDirty(scene);Undo.CollapseUndoOperations(undo);
        Debug.Log("Door fit, shared wall, locker positions, whiteboard, recycling and exterior corrected.");
    }

    static void RemoveRetiredProps()
    {
        var extra=GameObject.Find("Layout_15x15/Backstreet/ElectricBox_02");
        var keep=GameObject.Find("Layout_15x15/Backstreet/ElectricBox_01");
        if(extra!=null&&keep!=null)
        {
            foreach(var panel in Object.FindObjectsByType<Kehai.Karen.BreakerPanel>(FindObjectsInactive.Include))
                if(panel.box==extra.GetComponent<ElectricBox>())
                {Undo.RecordObject(panel,"Retain working breaker reference");panel.box=keep.GetComponent<ElectricBox>();}
            // Retire the prototype with its references recoverable through Undo.
            Undo.RecordObject(extra,"Remove duplicate power box");extra.SetActive(false);
        }
        foreach(string path in new[]{
            "Store - Finished/Outdoor maintenance cabinet", "WC - Furnished/WC extract east",
            "Store - Finished/East exterior recycling",
            "Store - Finished/Rear exterior recycling", "Store - Finished/Service-yard recycling"})
        {var go=GameObject.Find(path);if(go!=null)Undo.DestroyObjectImmediate(go);}
        if(GameObject.Find(ParkingLotBuilder.RootName)!=null)
        {var forecourt=GameObject.Find(StoreFinishBuilder.RootName+"/Forecourt floor");if(forecourt!=null)Undo.DestroyObjectImmediate(forecourt);}
        var store=GameObject.Find(StoreFinishBuilder.RootName);
        if(store!=null)for(int i=store.transform.childCount-1;i>=0;i--)
        {
            var t=store.transform.GetChild(i);
            if(t.name=="West exterior planter"||t.name=="East exterior planter"||t.name=="Rear exterior planter")Undo.DestroyObjectImmediate(t.gameObject);
        }
        // The staff supply shelf and WC east extractor were deliberately removed by the author.
        var oldShelves=GameObject.Find("Layout_15x15/Fridges");
        if(oldShelves!=null)foreach(Transform t in oldShelves.transform)
            if(t.name.StartsWith("Prop_StorageShelve")&&t.position.x<25&&t.position.z<-136.66f&&t.position.z>-150)
            {Undo.RecordObject(t.gameObject,"Retire removed staff supply shelf");t.gameObject.SetActive(false);}
    }

    static void AlignSharedWall(Transform root)
    {
        var material=StaffRoomModels.Material("Room Walls",new Color(.83f,.84f,.79f));
        // One 150mm wall plane replaces inconsistent 25/50/150mm overlapping room linings.
        foreach(var r in Object.FindObjectsByType<MeshRenderer>())
        {
            Bounds b=r.bounds;string name=r.name.ToLowerInvariant();
            if(r.transform.root==root||r.transform.root.name=="Models_Island"||r.GetComponentInParent<HingeDoor>()!=null)continue;
            if(!(name.Contains("wall")||name.Contains("lining")||name.Contains("infill")||name=="east doorway top"||name=="door header"))continue;
            if(Mathf.Abs(b.center.x-25)>.2f||b.size.x>.31f||b.min.z<-150.01f||b.max.z>-109.99f)continue;
            Undo.RecordObject(r.gameObject,"Replace shared room wall surface");r.gameObject.SetActive(false);
        }
        float cursor=-150;
        foreach(float z in new[]{-142.5f,-130f,-116.7f})
        {
            Wall(root,material,cursor,z-.83f,0,8);
            Wall(root,material,z-.83f,z+.83f,2.6f,8);
            cursor=z+.83f;
        }
        Wall(root,material,cursor,-110,0,8);
        foreach(string path in new[]{"Staff Room - Furnished/Staff entrance","Office - Furnished/Office entrance","Office - Furnished/WC entrance"})
        {
            var go=GameObject.Find(path);if(go==null)continue;
            Undo.RecordObject(go.transform,"Align door to shared wall");var p=go.transform.position;p.x=25;go.transform.position=p;
        }
    }
    static void Wall(Transform root,Material material,float south,float north,float bottom,float top)
        =>ColdStoreBuilder.Primitive(root,"Shared room wall",new Vector3(25,(bottom+top)*.5f,(south+north)*.5f),new Vector3(.15f,top-bottom,north-south),material,true);

    static void FitLockersAndWhiteboard()
    {
        var staff=GameObject.Find(StaffRoomBuilder.RootName);if(staff==null)return;
        foreach(Transform t in staff.transform)
        {
            if(!t.name.StartsWith("Locker ",StringComparison.Ordinal))continue;
            Undo.RecordObject(t,"Keep hiding lockers flush to wall");
            var bounds=VisibleBounds(t);t.position+=Vector3.forward*((StaffRoomBuilder.North-.075f)-bounds.max.z);
            var hide=t.GetComponent<ColdHideInterior>()??Undo.AddComponent<ColdHideInterior>(t.gameObject);
            hide.interior=new Bounds(new Vector3(0,1.15f,0),new Vector3(1.08f,2.15f,1.28f));
            hide.insideMaterial=ColdStoreBuilder.Glass("Locker interior view",.23f);
            EditorUtility.SetDirty(hide);
        }
        var board=staff.transform.Find("Shift whiteboard");if(board==null)return;
        Undo.RecordObject(board,"Fit whiteboard right of staff entrance");
        board.localScale=new Vector3(1.8f,1,1);board.SetPositionAndRotation(new Vector3(24.925f,1.25f,-146.3f),Quaternion.Euler(0,90,0));
        var b=VisibleBounds(board);board.position+=Vector3.right*(24.925f-b.max.x);
    }

    static void FinishWallSurfaces()
    {
        var wall=StaffRoomModels.Material("Room Walls",new Color(.83f,.84f,.79f));
        var neutral=StaffRoomModels.Material("Door Neutral Grey",new Color(.68f,.69f,.67f));
        foreach(var r in Object.FindObjectsByType<MeshRenderer>())
        {
            if(!r.enabled||r.transform.root.name=="Models_Island")continue;
            string n=r.name;
            if(n.StartsWith("Finished wall - ")||n=="Storage / cold-room partition"||n=="Partition door header")
            {Undo.RecordObject(r,"Match wall material");r.sharedMaterial=wall;}
            if(r.GetComponent<HingeDoor>()!=null)
            {
                var mats=r.sharedMaterials;bool changed=false;
                for(int i=0;i<mats.Length;i++)if(mats[i]!=null&&(mats[i].name=="Sage Enamel"||mats[i].name.IndexOf("Blue",StringComparison.OrdinalIgnoreCase)>=0)){mats[i]=neutral;changed=true;}
                if(changed){Undo.RecordObject(r,"Neutral door leaf");r.sharedMaterials=mats;}
            }
            if(n!="Cream wall panel"||r.transform.root.name!=StoreFinishBuilder.RootName)continue;
            var b=r.bounds;if(b.max.y<=4.001f)continue;
            Undo.RecordObject(r.transform,"Remove duplicate upper facade skin");
            if(b.min.y>=4){r.gameObject.SetActive(false);continue;}
            var size=r.transform.localScale;size.y=4-b.min.y;r.transform.localScale=size;
            var at=r.transform.position;at.y=(b.min.y+4)*.5f;r.transform.position=at;
        }
    }
    static Bounds VisibleBounds(Transform parent)
    {
        Bounds bounds=new Bounds(parent.position,Vector3.zero);bool first=true;
        foreach(var r in parent.GetComponentsInChildren<Renderer>())
        {if(!r.enabled)continue;if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
        return bounds;
    }
}
