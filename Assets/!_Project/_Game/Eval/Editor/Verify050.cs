using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai;
using Kehai.Eval;
using Kehai.Karen;
using Kehai.Store;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

// Native mechanical smoke test, not a model benchmark or training run. Private output only.
// Unity -batchmode -nographics -noaudio -executeMethod Verify050.Run -verify-out <private-dir>
[InitializeOnLoad]
public static class Verify050
{
    const string Pending="Kehai050.NativeVerification";
    static readonly List<object> rows=new List<object>();
    static readonly List<string> errors=new List<string>();
    static string output;
    static Verify050()
    {
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state!=PlayModeStateChange.EnteredPlayMode||!SessionState.GetBool(Pending,false))return;
            SessionState.SetBool(Pending,false);
            EditorApplication.delayCall+=()=>KehaiEnv.Ensure().StartCoroutine(Guard());
        };
    }
    public static void Run()
    {
        EditorUtility.audioMasterMute=true;
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-verify-out");
        if(at<0||at+1>=args.Length)throw new ArgumentException("-verify-out outside git is required");
        string folder=Path.GetFullPath(args[at+1]);
        for(var parent=new DirectoryInfo(folder);parent!=null;parent=parent.Parent)
            if(Directory.Exists(Path.Combine(parent.FullName,".git"))||File.Exists(Path.Combine(parent.FullName,".git")))
                throw new ArgumentException("Verification output must be private and outside git");
        Directory.CreateDirectory(folder);SessionState.SetString(Pending+".output",folder);
        SessionState.SetBool(Pending,true);
        string scene=EditorBuildSettings.scenes.First(s=>s.enabled).path;
        EditorSceneManager.OpenScene(scene,OpenSceneMode.Single);EditorApplication.EnterPlaymode();
    }
    static IEnumerator Guard()
    {
        output=SessionState.GetString(Pending+".output","");rows.Clear();errors.Clear();
        Application.logMessageReceived+=Logged;
        var stack=new Stack<IEnumerator>();stack.Push(Check());
        while(stack.Count>0)
        {
            var top=stack.Peek();bool more;
            try { more=top.MoveNext(); }
            catch(Exception e) { errors.Add(e.ToString());break; }
            if(!more){stack.Pop();continue;}
            if(top.Current is IEnumerator nested)stack.Push(nested);else yield return top.Current;
        }
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        Application.logMessageReceived-=Logged;
        File.WriteAllText(Path.Combine(output,"native-smoke.json"),MiniJson.Serialize(new Dictionary<string,object>
        { ["kind"]="native-mechanical-smoke",["rows"]=rows,["errors"]=errors,["passed"]=errors.Count==0 }));
        Debug.Log($"0.5.0 native smoke: {rows.Count} cases; {errors.Count} errors; {output}");
        EditorApplication.Exit(errors.Count==0?0:2);
    }
    static void Logged(string message,string trace,LogType type)
    { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace); }
    static IEnumerator Check()
    {
        KehaiEnv.KarenSetup=cfg=>{cfg.learningRecords=false;cfg.writeJsonl=false;};
        var cases=new List<(string layout,int shift)> {("today",1),("today",2),("",3),("",4)};
        cases.AddRange(StoreLayouts.All.Select(l=>(l.Id,1)));
        int index=0;
        foreach(var item in cases)
        {
            var env=KehaiEnv.Ensure();
            var config=new EnvConfig {seed=777,agent="native-smoke",rung="E",startShift=item.shift,
                shiftSeconds=90,fps=20,render=false,syntheticBlinks=true,freshLedger=true,
                ledgerPath=Path.Combine(output,"smoke-ledger.json"),layout=item.layout,
                heldoutLayout=!string.IsNullOrEmpty(item.layout)&&EvalSplits.HeldOutLayout(item.layout),
                trainingLayouts=false,gameplayCareer=true};
            yield return env.ResetEpisode(config);
            var brain=KarenBrain.Instance;var map=StoreMap.Current;
            int home=map.FindLandmark(LandmarkKind.TimeClock)?.Cell??-1;
            if(home>=0) env.Driver.Warp(map.CellPosition[home]);
            for(int frame=0;frame<30;frame++)yield return null;
            // The live Ledger indexes habits in a dictionary; Data.regions is the
            // serialized snapshot, refreshed by Save, not on each observation.
            brain.Ledger.Save();
            var savedLedger=JsonUtility.FromJson<LedgerData>(File.ReadAllText(config.ledgerPath));
            string expected=string.IsNullOrEmpty(item.layout)?StoreLayouts.ForShift(item.shift):item.layout;
            if(!env.Metrics.Started||brain==null)throw new InvalidOperationException("Shift failed to start: "+expected);
            if(StoreLayoutRuntime.CurrentId!=expected)throw new InvalidOperationException("Wrong layout schedule");
            bool present=item.shift>1;
            if(brain.PresentThisShift!=present||brain.Body.gameObject.activeInHierarchy!=present)
                throw new InvalidOperationException("Karen presence mismatch at shift "+item.shift);
            if(map.CellCount==0||map.Bays.Count==0||ShelfStock.Current.Count==0)
                throw new InvalidOperationException("Empty navigation or stock: "+expected);
            var activeBays=Object.FindObjectsByType<ShelfUnit>();
            if(map.Bays.Count!=activeBays.Length || map.Bays.Any(b=>b.Unit==null||!b.Unit.gameObject.activeInHierarchy))
                throw new InvalidOperationException("Map contains stale bays after scene/layout reload: "+expected);
            var stocked=new HashSet<string>(Object.FindObjectsByType<ShelfUnit>().SelectMany(s=>s.Slots).Select(s=>s.productId));
            var missing=ProductCatalog.All.Where(p=>!stocked.Contains(p.Id)).Select(p=>p.Id).ToArray();
            int unreachable=0,total=0;
            Vector3 from=env.Driver.transform.position;
            foreach(var shelf in Object.FindObjectsByType<ShelfUnit>())
            {
                Transform point=SceneObjectNames.Child(shelf.transform,"ShelfPoint");if(point==null)continue;total++;
                var path=new NavMeshPath();
                if(!NavMesh.SamplePosition(point.position,out var hit,3f,NavMesh.AllAreas)||
                    !NavMesh.CalculatePath(from,hit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete)unreachable++;
            }
            // Remove the ASCII grid's empty right-hand padding for clean generated diffs.
            string report=string.Join("\n",StoreMapReport.ToMarkdown(map).Split('\n').Select(line=>line.TrimEnd()));
            File.WriteAllText(Path.Combine(output,$"{index:00}-{expected}-map.md"),report);
            rows.Add(new Dictionary<string,object> { ["layout"]=expected,["shift"]=item.shift,["karen_present"]=brain.PresentThisShift,
                ["cells"]=map.CellCount,["regions"]=map.Regions.Count,["bays"]=map.Bays.Count,["active_bays"]=activeBays.Length,["slots"]=ShelfStock.Current.Count,
                ["missing_products"]=missing,["shelf_stands"]=total,["unreachable_stands"]=unreachable,
                ["habit_places"]=savedLedger.regions.Count,["habit_seconds"]=savedLedger.regions.Sum(r=>r.dwell),["player_region"]=map.RegionAt(env.Driver.transform.position),
                ["player_position"]=new[]{env.Driver.transform.position.x,env.Driver.transform.position.y,env.Driver.transform.position.z},
                ["brain_active"]=brain.isActiveAndEnabled,["brain_shift_active"]=brain.ShiftActive,["time_scale"]=Time.timeScale,["delta_time"]=Time.deltaTime });
            Debug.Log($"native smoke {expected} shift {item.shift}: {map.CellCount} cells, {missing.Length} missing products, {unreachable}/{total} unreachable stands");
            if(savedLedger.regions.Count==0||savedLedger.regions.Sum(r=>r.dwell)<=0f)errors.Add(expected+": Director did not save player habits");
            if(missing.Length>0)errors.Add(expected+": missing products: "+string.Join(",",missing));
            if(unreachable>0)errors.Add(expected+": unreachable shelf stands: "+unreachable);
            env.Shift.EndShift(completed:false);index++;
        }
        KehaiEnv.KarenSetup=null;
    }
}
