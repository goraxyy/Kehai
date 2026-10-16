using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// All geometry is built here at metre scale. No third-party fridge meshes are used.
public static class ColdStoreBuilder
{
    public const string RootName="Cold Chain - Retail and Storage";
    public static readonly string[] Models={"RetailChiller","StorageChiller","HideChiller","OutdoorWasteContainer","RecyclingBin"};
    static Material shell,trim,steel,glass;
    static Transform root;
    public static Material Glass(string name,float alpha)
    {
        var m=StaffRoomModels.Material(name,new Color(.64f,.82f,.85f,alpha));
        m.SetFloat("_Surface",1);m.SetFloat("_Blend",0);m.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);m.SetFloat("_ZWrite",0);
        m.SetFloat("_Cull",0);m.SetFloat("_Smoothness",.65f);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType","Transparent");m.renderQueue=3000;m.SetShaderPassEnabled("ShadowCaster",false);return m;
    }
    public static void BuildModels()
    {
        shell=StaffRoomModels.Material("Chiller Enamel",new Color(.72f,.77f,.75f));
        trim=StaffRoomModels.Material("Chiller Charcoal",new Color(.13f,.18f,.18f));
        steel=StaffRoomModels.Material("Chiller Steel",new Color(.5f,.57f,.58f));glass=Glass("Clear chilled glass",.16f);
        foreach(string name in Models)
        {
            StaffRoomModels.BuildCustom(name,StoreFinishModels.Folder,p=>
            {
                if(name=="OutdoorWasteContainer"){OutdoorContainer(p);return;}
                if(name=="RecyclingBin"){RecyclingBin(p);return;}
                bool hide=name=="HideChiller",retail=name=="RetailChiller";float depth=hide?2.1f:1.2f;
                B(p,"Left insulated wall",new Vector3(-.86f,1.3f,0),new Vector3(.08f,2.6f,depth),shell,true);
                B(p,"Right insulated wall",new Vector3(.86f,1.3f,0),new Vector3(.08f,2.6f,depth),shell,true);
                B(p,"Insulated back",new Vector3(0,1.3f,depth*.5f-.04f),new Vector3(1.64f,2.6f,.08f),shell,true);
                B(p,"Top",new Vector3(0,2.55f,0),new Vector3(1.64f,.1f,depth),shell,true);
                // A low threshold admits the character controller without a jump.
                B(p,"Base",new Vector3(0,.035f,0),new Vector3(1.64f,.07f,depth),steel,true);
                B(p,"Compressor header",new Vector3(0,2.38f,-depth*.5f+.08f),new Vector3(1.64f,.24f,.16f),trim);
                for(int i=0;i<12;i++)B(p,"Vent slit",new Vector3(-.7f+i*.125f,2.38f,-depth*.5f-.005f),new Vector3(.045f,.15f,.012f),steel);
                var hinge=StaffRoomModels.Door(p,retail?"retail chiller":"cold-storage cabinet",new Vector3(-.8f,0,-depth*.5f-.01f),1.6f,2.16f,.09f,retail?glass:shell,100);
                if(!hide&&!retail)for(int i=0;i<3;i++)B(p,"Box deck",new Vector3(0,.45f+i*.5f,0),new Vector3(1.58f,.04f,1.04f),steel,true);
            });
            if(name=="OutdoorWasteContainer"||name=="RecyclingBin")continue;
            string path=StoreFinishModels.Folder+"/Prefabs/"+name+".prefab";
            var go=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var marker=go.AddComponent<ColdFixture>();marker.retail=name=="RetailChiller";marker.hidingSpace=name=="HideChiller";
                var door=go.GetComponentInChildren<FurnitureDoor>();
                var frameMeshes=new List<MeshFilter>();
                foreach(float x in new[]{.04f,1.56f})frameMeshes.Add(Primitive(door.transform,"Door frame side",new Vector3(x,1.17f,0),new Vector3(.08f,2.16f,.085f),trim,false).GetComponent<MeshFilter>());
                foreach(float y in new[]{.13f,2.21f})frameMeshes.Add(Primitive(door.transform,"Door frame rail",new Vector3(.8f,y,0),new Vector3(1.6f,.08f,.085f),trim,false).GetComponent<MeshFilter>());
                door.GetComponent<OutlineHighlight>().targetMeshes=frameMeshes.ToArray();
                if(marker.retail)
                {
                    go.AddComponent<ShelfUnit>();
                    var shelfOutline=go.GetComponent<OutlineHighlight>();
                    shelfOutline.targetMeshes=frameMeshes.ToArray();
                    shelfOutline.shape=OutlineHighlight.OutlineShape.PerMesh;
                    shelfOutline.outlineMaterial=door.GetComponent<OutlineHighlight>().outlineMaterial;
                    for(int i=0;i<3;i++)
                    {
                        float y=.4f+i*.55f;
                        Primitive(go.transform,"Shelf deck "+(i+1),new Vector3(0,y,0),new Vector3(1.5f,.035f,1),steel,true);
                        PriceRail(go.transform,"Pricetag "+(i+1),new Vector3(0,y-.025f,-.515f),1.5f,.10f,shell);
                    }
                }
                if(marker.hidingSpace)go.AddComponent<ColdHideInterior>().insideMaterial=Glass("Chiller interior view",.23f);
                PrefabUtility.SaveAsPrefabAsset(go,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(go);}
        }
    }
    public static void Build()
    {
        BuildModels();
        var old=GameObject.Find(RootName);if(old!=null)Undo.DestroyObjectImmediate(old);
        var legacy=GameObject.Find("Layout_15x15/Fridges");if(legacy!=null)
            foreach(Transform t in legacy.transform)if(t.name.StartsWith("Prop_Fridge")){Undo.RecordObject(t.gameObject,"Replace old refrigeration");t.gameObject.SetActive(false);}
        root=new GameObject(RootName).transform;Undo.RegisterCreatedObjectUndo(root.gameObject,"Build cold chain");
        var retail=Group("Retail Chillers");var storage=Group("Cold Storage Cabinets");
        var products=StorageBoxes.ProductsKept(StorageKind.Cold);int next=0;
        // Destination cold goods line the rear sales wall; leave both staff door approaches open.
        foreach(float x in new[]{27f,29f,31f,33f,35f,37f,39f,46f,48f,50f,52f,54f,56f,58f,60f,62f,64f,71f,73f,75f,77f,79f,81f,83f})
            Cabinet(retail,"RetailChiller",new Vector3(x,0,-174.28f),180,products,ref next);
        // Finish the sales-floor south-west corner without projecting into its cross aisle.
        foreach(float z in new[]{-171.3f,-168.9f})Cabinet(retail,"RetailChiller",new Vector3(25.72f,0,z),-90,products,ref next);
        next=0;
        // Fill usable wall runs exactly. Square corner closures stop cabinets interpenetrating.
        WallRun(storage,56.275f,66.1f,-175.075f,0,products,ref next,new[]{1,4});
        WallRun(storage,68.9f,83.725f,-175.075f,0,products,ref next,new[]{1,5});
        WallRun(storage,56.275f,83.725f,-189.925f,180,products,ref next,new[]{1,5,8,12});
        WallRun(storage,-188.725f,-183.9f,55.075f,-90,products,ref next,new int[0]);
        WallRun(storage,-181.1f,-176.275f,55.075f,-90,products,ref next,new int[0]);
        WallRun(storage,-188.725f,-176.275f,84.925f,90,products,ref next,new int[0]);
        foreach(float x in new[]{55.675f,84.325f})foreach(float z in new[]{-175.675f,-189.325f})
            Primitive(storage,"Insulated corner closure",new Vector3(x,1.3f,z),new Vector3(1.2f,2.6f,1.2f),shell,true);
        int islandNumber=0;
        foreach(float centre in new[]{60.1f,66.1f,73.9f,79.9f})
        {
            var island=new GameObject("Back-to-back island "+(++islandNumber)).transform;island.SetParent(storage,false);
            foreach(float x in new[]{centre-.9f,centre+.9f})foreach(float z in new[]{-181.9f,-183.1f})
                Cabinet(island,"StorageChiller",new Vector3(x,0,z),z>-182.5f?180:0,products,ref next);
        }
        // The original hinge is at z=-183.5; its 2m leaf opens across -183.5 .. -181.5.
        foreach(var b in new[]{new Vector3(-178.25f,6.5f,0),new Vector3(-186.75f,6.5f,0)})
            Primitive(root,"Storage / cold-room partition",new Vector3(55,4,b.x),new Vector3(.15f,8,b.y),shell,true);
        Primitive(root,"Partition door header",new Vector3(55,5.25f,-182.5f),new Vector3(.15f,5.5f,2),shell,true);
        Label(root,"COLD STORAGE  /  KEEP CLOSED",new Vector3(68.5f,3,-175.14f),0,4,.32f);
        Label(root,"CHILLED + FROZEN",new Vector3(56,3,-173.9f),180,7,.4f);
        foreach(float x in new[]{61f,74f,81f})
        {
            var p=(GameObject)PrefabUtility.InstantiatePrefab(StoreFinishModels.Prefab("HVAC"),root);
            p.name="Cold-room evaporator";p.transform.position=new Vector3(x,4.5f,-186.7f);
            p.AddComponent<Unity.AI.Navigation.NavMeshModifier>().ignoreFromBuild=true;
        }
        Primitive(root,"Cold-room drain",new Vector3(68.5f,.025f,-182.7f),new Vector3(.25f,.02f,10),trim,false);
        for(int i=0;i<12;i++)Primitive(root,"Drain grille",new Vector3(68.5f,.037f,-187.2f+i*.8f),new Vector3(.26f,.006f,.025f),steel,false);
        StorageBoxesPreview.Refresh(true);StoreLayout.ApplyToScene();ShelfDrawer.Ensure();
    }
    static Transform Group(string name){var t=new GameObject(name).transform;t.SetParent(root,false);return t;}
    static void WallRun(Transform parent,float from,float to,float wall,float yaw,List<ProductDef> products,ref int next,int[] empty)
    {
        int count=Mathf.FloorToInt((to-from)/1.8f);float width=(to-from)/count;
        for(int i=0;i<count;i++)
        {
            bool hide=Array.IndexOf(empty,i)>=0;float depth=hide?2.1f:1.2f;
            float along=from+(i+.5f)*width;
            Vector3 at=Mathf.Abs(yaw)==90?new Vector3(wall+(yaw<0?depth*.5f:-depth*.5f),0,along):new Vector3(along,0,wall+(yaw==0?-depth*.5f:depth*.5f));
            var go=Cabinet(parent,hide?"HideChiller":"StorageChiller",at,yaw,products,ref next);
            go.transform.localScale=new Vector3(width/1.8f,1,1);
        }
    }
    static GameObject Cabinet(Transform parent,string model,Vector3 position,float yaw,List<ProductDef> products,ref int next)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(StoreFinishModels.Prefab(model),parent);
        go.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));var marker=go.GetComponent<ColdFixture>();
        marker.frozen=marker.retail?position.x<40:position.x<67;
        if(!marker.hidingSpace)
        {
            var own=products.FindAll(p=>(p.Category==ItemType.Frozen)==marker.frozen);
            marker.products=new string[3];for(int i=0;i<3;i++)marker.products[i]=own[next++%own.Count].Id;
        }
        go.name=(marker.hidingSpace?"Empty cabinet - hiding space":(marker.retail?"Retail ":"Storage ")+(marker.frozen?"freezer":"chiller"))+" "+parent.childCount.ToString("00");
        float labelFront=marker.hidingSpace?-1.095f:-.645f;
        Primitive(go.transform,"Temperature label plate",new Vector3(0,2.38f,labelFront),new Vector3(1.45f,.18f,.008f),shell,false);
        Label(go.transform,marker.hidingSpace?"EMPTY / OUT OF SERVICE":marker.frozen?"FROZEN  -18 C":"CHILLED  +4 C",new Vector3(0,2.38f,labelFront-.008f),0,1.3f,.14f,false);
        return go;
    }
    static void OutdoorContainer(Transform p)
    {
        B(p,"Bottom",new Vector3(0,.25f,0),new Vector3(2.2f,.16f,1.4f),trim,true);
        foreach(float x in new[]{-1.04f,1.04f})B(p,"Side",new Vector3(x,.85f,0),new Vector3(.12f,1.2f,1.4f),steel,true);
        foreach(float z in new[]{-.64f,.64f})B(p,"End",new Vector3(0,.85f,z),new Vector3(2,.12f+1.08f,.12f),steel,true);
        foreach(float x in new[]{-.8f,.8f})foreach(float z in new[]{-.5f,.5f})StaffRoomModels.Cylinder(p,"Wheel",new Vector3(x,.14f,z),.14f,.1f,trim,new Vector3(0,0,90));
        // Open top accepts separately bagged recycling without clipping through a lid.
        foreach(float x in new[]{-.9f,-.45f,0,.45f,.9f})B(p,"Pressed rib",new Vector3(x,.85f,-.715f),new Vector3(.05f,1,.04f),trim);
    }
    static void RecyclingBin(Transform p)
    {
        B(p,"Rounded body",new Vector3(0,.48f,0),new Vector3(.5f,.96f,.6f),shell,true);
        B(p,"Door inset",new Vector3(0,.45f,-.307f),new Vector3(.4f,.77f,.018f),trim);
        B(p,"Opening",new Vector3(0,.975f,0),new Vector3(.34f,.018f,.37f),trim);
        foreach(float x in new[]{-.22f,.22f})B(p,"Top rim",new Vector3(x,1,0),new Vector3(.06f,.08f,.6f),steel);
        foreach(float z in new[]{-.27f,.27f})B(p,"Top rim",new Vector3(0,1,z),new Vector3(.4f,.08f,.06f),steel);
    }
    static void B(Transform p,string name,Vector3 at,Vector3 size,Material m,bool solid=false)=>StaffRoomModels.Box(p,name,at,size,m,solid,.015f);
    public static GameObject Primitive(Transform p,string name,Vector3 at,Vector3 size,Material m,bool solid)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(p,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=m;
        if(!solid)Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    public static GameObject PriceRail(Transform p,string name,Vector3 at,float width,float height,Material material)
    {
        string path=StoreFinishModels.Folder+"/Meshes/Price rail face.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null)
        {
            mesh=new Mesh{name="Price rail face"};
            mesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
        }
        var go=new GameObject(name);go.transform.SetParent(p,false);go.transform.localPosition=at;go.transform.localScale=new Vector3(width,height,1);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;
    }
    public static void Label(Transform p,string text,Vector3 at,float yaw,float width,float height,bool world=true)
    {
        var go=new GameObject(text);go.transform.SetParent(p,false);if(world)go.transform.position=at;else go.transform.localPosition=at;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
        var tmp=go.AddComponent<TextMeshPro>();tmp.font=TMP_Settings.defaultFontAsset;tmp.text=text;tmp.fontSize=height*8;tmp.color=new Color(.12f,.18f,.18f);tmp.alignment=TextAlignmentOptions.Center;tmp.rectTransform.sizeDelta=new Vector2(width,height*1.4f);tmp.textWrappingMode=TextWrappingModes.NoWrap;
    }
}
