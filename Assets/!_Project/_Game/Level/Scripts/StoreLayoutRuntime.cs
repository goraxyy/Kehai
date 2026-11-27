using System;
using System.Collections.Generic;
using Kehai.Karen;
using Kehai.Store;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

// Replaces only ambient sales-floor bays. Doors, tills, floor and cold chain remain authored.
// No prefab/model is written to disk. All generated meshes use Unity's built-in cube.
public static class StoreLayoutRuntime
{
    public static string CurrentId { get; private set; } = "today";
    static GameObject root;
    static readonly List<GameObject> hidden = new List<GameObject>();
    static readonly Dictionary<CustomerSpawner, Transform[]> browsing = new Dictionary<CustomerSpawner, Transform[]>();
    static Scene activeScene;
    static Material finish, outlineMaterial;
    // Set by the eval harness before scene reload, never inferred from a learner's split.
    public static string EpisodeLayout { get; set; }
    public static bool EvaluationLayouts { get; set; }
    public static bool TrainingSchedule { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Subscribe()
    {
        CareerRuntime.CareerPreparing -= OnPreparing;
        CareerRuntime.CareerPreparing += OnPreparing;
    }
    static void OnPreparing(int shift, int seed, bool training)
    {
        // Static subscriptions survive leaving Play mode. Editor/unit-test clock changes must
        // never replace shelves or bake navigation in the owner's authored scene.
        if(Application.isPlaying) Prepare(shift,seed,(training && !CareerRuntime.GameplaySimulation) || TrainingSchedule,EvaluationLayouts);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        root = null; hidden.Clear(); browsing.Clear(); activeScene = default; finish = null; outlineMaterial = null; CurrentId = "today"; EpisodeLayout = null; EvaluationLayouts = TrainingSchedule = false;
    }

    public static void EnsureInitial()
    {
        if (activeScene == SceneManager.GetActiveScene()) return;
        activeScene = SceneManager.GetActiveScene();
        root = null; hidden.Clear(); browsing.Clear(); CurrentId = "today";
        string id = Choose(CareerRuntime.UpcomingShiftNumber, CareerRuntime.Seed, (Edition.Training && !CareerRuntime.GameplaySimulation) || TrainingSchedule, EvaluationLayouts);
        if (id != "today") Apply(id);
    }

    public static string Choose(int shift, int seed, bool training, bool heldout = false)
    {
        string requested = EpisodeLayout ?? Argument("-kehai-layout");
        string id = string.IsNullOrEmpty(requested)
            ? training ? StoreLayouts.ForTraining(seed, shift) : StoreLayouts.ForShift(shift)
            : requested.ToLowerInvariant();
        if (id != "today" && StoreLayouts.Find(id).HeldOut && (!heldout || training))
            throw new InvalidOperationException("Held-out layouts are reserved for explicit final evaluation; they cannot train or tune.");
        return id;
    }

    public static bool Prepare(int shift, int seed, bool training, bool heldout = false)
    {
        EnsureInitial();
        string id = Choose(shift, seed, training, heldout);
        if (id == CurrentId) return false;
        Apply(id);
        RebuildNavigation();
        StoreLayout.ApplyToScene();
        if (Edition.ShowUI) AisleSigns.Build();
        if (KarenBrain.Instance != null) KarenBrain.Instance.MazeChanged();
        else StoreMap.Rebuild();
        return true;
    }

    static void RebuildNavigation()
    {
        Physics.SyncTransforms();
        var surface = KarenWorld.StoreSurface();
        if (surface == null) return;
        using (new StoreRebake(surface))
        {
            surface.BuildNavMesh();
            NavigationReport(surface);
        }
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // -kehai-navmesh-report <file> (0.5.2, C6.6): what one rebake used, to compare an editor's bake
    // with a player's: the NavMesh's extent, then every collider the bake could read (layer, kind,
    // enabled, bounds), sorted, one per line. Off unless asked; appends one block per rebake, from
    // RebuildNavigation and from KarenArea.Install (the one every load runs, 0.5.2 C8.1).
    public static void NavigationReport(Unity.AI.Navigation.NavMeshSurface surface)
    {
        string path = Argument("-kehai-navmesh-report");
        if (string.IsNullOrEmpty(path)) return;
        var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
        var b = tri.vertices.Length > 0 ? new Bounds(tri.vertices[0], Vector3.zero) : new Bounds();
        foreach (Vector3 v in tri.vertices) b.Encapsulate(v);
        var lines = new List<string>();
        foreach (Collider c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if ((surface.layerMask & (1 << c.gameObject.layer)) == 0 || c.isTrigger) continue;
            Bounds cb = c.bounds;
            lines.Add($"{PathOf(c.transform)}\t{c.GetType().Name}\t{(c.enabled ? "on" : "off")}\t" +
                      $"{cb.min.x:0.0},{cb.min.y:0.0},{cb.min.z:0.0}\t{cb.max.x:0.0},{cb.max.y:0.0},{cb.max.z:0.0}");
        }
        lines.Sort(string.CompareOrdinal);
        System.IO.File.AppendAllText(path, $"# rebake {CurrentId}: navmesh {tri.vertices.Length} vertices, " +
            $"min {b.min.x:0.00},{b.min.y:0.00},{b.min.z:0.00} max {b.max.x:0.00},{b.max.y:0.00},{b.max.z:0.00}; {lines.Count} colliders\n" +
            string.Join("\n", lines) + "\n");
    }

#if UNITY_EDITOR
    // Batch placement checks use the store's real geometry, stock and bake without starting a shift.
    public static void PreparePlacementAudit(string id)
    {
        if (!Application.isBatchMode || !Application.isPlaying)
            throw new InvalidOperationException("Placement layout changes require batch Play mode.");
        if (CurrentId != id)
        {
            Apply(id);
            RebuildNavigation();
        }
        StoreLayout.ApplyToScene();
    }
#endif

    // Replays restore recorded geometry, never choose a training or career layout. This
    // performs no agent navigation/training and permits viewing a reserved layout's replay.
    public static void PrepareReplay(string layout)
    {
        if (activeScene != SceneManager.GetActiveScene())
        {
            activeScene=SceneManager.GetActiveScene();
            root=null;hidden.Clear();browsing.Clear();CurrentId="today";
        }
        string id=string.IsNullOrEmpty(layout)?"today":layout;
        if (id!=CurrentId) Apply(id);
        Physics.SyncTransforms();
        StoreLayout.ApplyToScene();
    }

    static string Argument(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i=0; i+1<args.Length; i++) if (args[i] == flag) return args[i+1];
        return null;
    }

    static void Apply(string id)
    {
        StoreLayouts.Layout layout = id == "today" ? null : StoreLayouts.Find(id);
        if (root != null) { root.SetActive(false); Drop(root); root = null; }
        foreach (var go in hidden) if (go != null) go.SetActive(true);
        hidden.Clear();
        foreach (var pair in browsing) if (pair.Key != null) pair.Key.shelfPoints = pair.Value;
        CurrentId = id;
        if (layout == null) return;
        foreach (ShelfUnit shelf in UnityEngine.Object.FindObjectsByType<ShelfUnit>())
        {
            Vector3 p = shelf.transform.position;
            if (StoreLayout.SectionAt(p) == ItemType.Confectionery) continue;
            if (p.x < 25 || p.x > 85 || p.z > -120 || p.z < -175 || shelf.GetComponentInParent<ColdFixture>() != null) continue;
            if (outlineMaterial == null) outlineMaterial = shelf.GetComponent<OutlineHighlight>()?.outlineMaterial;
            hidden.Add(shelf.gameObject);
            shelf.gameObject.SetActive(false);
        }
        root = new GameObject("StoreLayout_" + id);
        NavMeshSurface surface = KarenWorld.StoreSurface();
        if (surface != null && surface.collectObjects == CollectObjects.Children) root.transform.SetParent(surface.transform, true);
        var stands = new List<Transform>();
        for (int r=1; r<layout.Height-1; r++)
            for (int c=1; c<layout.Width-1; c++)
                if (layout.Shelf(r,c)) MakeCell(layout,r,c,stands);
        foreach (CustomerSpawner spawner in UnityEngine.Object.FindObjectsByType<CustomerSpawner>())
        {
            if (!browsing.ContainsKey(spawner)) browsing.Add(spawner, spawner.shelfPoints);
            spawner.shelfPoints = stands.ToArray();
        }
    }

    static void MakeCell(StoreLayouts.Layout layout, int row, int col, List<Transform> stands)
    {
        bool west=layout.Shelf(row,col-1), east=layout.Shelf(row,col+1), north=layout.Shelf(row-1,col), south=layout.Shelf(row+1,col);
        bool tall=layout.At(row,col)!='+';
        var bay = new GameObject($"Bay_{row:00}_{col:00}");
        bay.transform.SetParent(root.transform,false);
        bay.transform.position=layout.Position(row,col);
        // Straight modules meet at tile boundaries. Junctions are separate, nonoverlapping
        // arms around a 1 m centre; upper arms stop where a neighbouring low module begins.
        bool vertical=(north||south)&&!west&&!east;
        bool horizontal=(west||east)&&!north&&!south;
        if (vertical || horizontal)
        {
            if(vertical) bay.transform.localRotation=Quaternion.Euler(0,90,0);
            BuildModule(bay.transform,Vector3.zero,StoreLayouts.Pitch,1f,tall,true,layout.At(row,col)!='|');
        }
        else
        {
            BuildModule(bay.transform,Vector3.zero,1f,1f,tall,!south,!north,!west,!east);
            float arm=(StoreLayouts.Pitch-1f)*.5f;
            if(west) BuildModule(bay.transform,new Vector3(-(.5f+arm*.5f),0,0),arm,1f,tall,true,true);
            if(east) BuildModule(bay.transform,new Vector3(.5f+arm*.5f,0,0),arm,1f,tall,true,true);
            if(north) BuildModule(bay.transform,new Vector3(0,0,.5f+arm*.5f),1f,arm,tall,false,false,true,true);
            if(south) BuildModule(bay.transform,new Vector3(0,0,-(.5f+arm*.5f)),1f,arm,tall,false,false,true,true);
        }
        // Same area contract as the authored shelf kit: the boards and their tops are not
        // pedestrian floor, even when a low board could be reached by an agent step.
        var modifier=bay.AddComponent<NavMeshModifier>();modifier.overrideArea=true;modifier.area=1;
        bay.AddComponent<ShelfUnit>();
        var outline = bay.GetComponent<OutlineHighlight>();
        outline.outlineMaterial = outlineMaterial; outline.shape = OutlineHighlight.OutlineShape.BoundingBox;
        var point = new GameObject("ShelfPoint"); point.transform.SetParent(bay.transform,false);
        // Pick an exposed side, never an arm leading straight into a connected shelf.
        Vector3 direction = !south?Vector3.back:!north?Vector3.forward:!west?Vector3.left:Vector3.right;
        Vector3 world = layout.Position(row,col)+direction*1.15f;
        point.transform.position=world; stands.Add(point.transform);
    }

    static void BuildModule(Transform parent, Vector3 centre, float width, float depth, bool tall,
                            bool front, bool back, bool left=false, bool right=false)
    {
        float height=tall?2f:1.25f;
        // Vertical panel is thin; stock fills the exposed board edges, never the joint seam.
        Part(parent,"Shelf_Back",centre+Vector3.up*height*.5f,new Vector3(width,height,.04f));
        float[] levels=tall?new[]{StoreLayouts.BottomBoard,StoreLayouts.SharedBoard,StoreLayouts.UpperBoard}:new[]{StoreLayouts.BottomBoard,StoreLayouts.SharedBoard};
        foreach(float y in levels)
        {
            Part(parent,"Shelf_Deck_"+y.ToString("F3",System.Globalization.CultureInfo.InvariantCulture),centre+Vector3.up*y,new Vector3(width,.02f,depth));
            if(front) Strip(parent,centre+new Vector3(0,y,-depth*.5f),width,.025f);
            if(back) Strip(parent,centre+new Vector3(0,y,depth*.5f),width,.025f);
            if(left) Strip(parent,centre+new Vector3(-width*.5f,y,0),.025f,depth);
            if(right) Strip(parent,centre+new Vector3(width*.5f,y,0),.025f,depth);
        }
        // End caps close each module, including height transitions; no upper overhang.
        Part(parent,"Shelf_Cap",centre+new Vector3(-width*.5f+.015f,height*.5f,0),new Vector3(.03f,height,depth));
        Part(parent,"Shelf_Cap",centre+new Vector3(width*.5f-.015f,height*.5f,0),new Vector3(.03f,height,depth));
    }
    static void Strip(Transform p,Vector3 at,float x,float z)
    {
        var strip=Part(p,"Pricetag",at,new Vector3(x,.025f,z));
        var collider=strip.GetComponent<Collider>(); if(collider!=null) Drop(collider);
    }
    static void Drop(UnityEngine.Object value)
    {
        if(value==null)return;
        if(Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
    static GameObject Part(Transform parent,string name,Vector3 position,Vector3 size)
    {
        var part=GameObject.CreatePrimitive(PrimitiveType.Cube); part.name=name;
        part.transform.SetParent(parent,false); part.transform.localPosition=position; part.transform.localScale=size;
        if(finish==null)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            if(shader!=null) { finish=new Material(shader); finish.color=new Color(.42f,.44f,.43f); }
        }
        if(finish!=null) part.GetComponent<Renderer>().sharedMaterial=finish;
        return part;
    }
}
