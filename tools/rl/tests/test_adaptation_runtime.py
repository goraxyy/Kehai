"""Execute the actual bot memory/observation and escape arithmetic without an editor."""
from pathlib import Path
import re
import shutil
import sys

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import offline_csharp

ROOT = Path(__file__).resolve().parents[3]
GAME = ROOT / "Assets/!_Project/_Game"


def test_memory_observation_and_escape_runtime(tmp_path):
    if not shutil.which("dotnet"):
        pytest.skip("offline .NET SDK unavailable")
    support = tmp_path / "Support.cs"
    support.write_text('''
using System;
using System.Collections.Generic;
using System.Linq;
using Kehai.Eval;
using Kehai.Karen;
namespace UnityEngine {
 public static class Mathf {
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp01(float a)=>Math.Max(0,Math.Min(1,a));
 }
 public struct Vector2 {
  public float x,y; public Vector2(float a,float b){x=a;y=b;}
  public static Vector2 zero=>new Vector2();
  public Vector2 normalized {get {float d=(float)Math.Sqrt(x*x+y*y); return d>0?new Vector2(x/d,y/d):zero;}}
 }
}
namespace Kehai.Eval {
 public class Persona { public const int Count=14; public float[] Traits=new float[14]; }
 public class EnvAction { public string verb,target; public bool sprint,crouch; }
}
class Probe {
 static int checks;
 static void Assert(bool ok,string why) { checks++; if(!ok) throw new Exception(why); }
 static bool Near(float a,float b)=>Math.Abs(a-b)<0.00001;
 static Dictionary<string,object> Obs(bool visible=false, bool heard=false, string region="clock", string chore=null) {
  return new Dictionary<string,object> {
   ["you"]=new Dictionary<string,object>{{"region",region},{"at",new float[]{0,0}}},
   ["karen"]=new Dictionary<string,object>{{"visible",visible},{"heard",heard},{"region","clock"},{"eye_colour","hunt"}},
   ["karen_chores"]=chore==null?new List<object>():new List<object>{new Dictionary<string,object>{{"kind",chore}}},
   ["checkout_queue"]=new List<object>{new Dictionary<string,object>{{"region","clock"},{"walk_m",1f}}}
  };
 }
 static void Main() {
  var a=new BotMemory(); var b=new BotMemory(); a.BeginShift(1); b.BeginShift(1);
  a.ObserveFair(Obs(true,chore:"flash_sale"),0);
  a.ObserveFair(Obs(true,chore:"flash_sale"),1);
  Assert(a.SeenAt("clock")==1,"continuous sighting counted twice");
  Assert(a.TrickCount("flash_sale")==1,"continuous chore counted twice");
  Assert(b.SeenAt("clock")==0 && b.TrickCount("flash_sale")==0,"another bot inherited history");
  a.ObserveFair(Obs(false,true),2);
  a.ObserveFair(Obs(false,true),15);
  Assert(a.EscapedAt("clock")==0,"heard threat incorrectly counted as escape");
  a.ObserveFair(Obs(region:"bins"),16);
  a.ObserveFair(Obs(region:"bins"),26);
  Assert(a.EscapedAt("bins")==1,"perceived escape missing");
  a.EndShift(true,"clock"); a.EndShift(true,"clock");
  Assert(a.Catches==1,"double episode settlement");
  a.BeginShift(2);
  Assert(a.LastSensedRegion(out float time)==null,"within-shift state survived boundary");
  Assert(Near(a.CaughtAt("clock"),.8f) && Near(a.SeenAt("clock"),.8f),"history did not decay/carry");
  var obs=Obs(); var known=EmployeeObservation.Build(obs,0,a,null,null,30);
  var fresh=EmployeeObservation.Build(obs,0,b,null,null,30);
  Assert(known.Length==EmployeeObservation.Size,"wrong observation shape");
  Assert(known.Skip(known.Length-EmployeeObservation.HistorySize).Zip(fresh.Skip(fresh.Length-EmployeeObservation.HistorySize),(x,y)=>x!=y).Any(x=>x),"network cannot read history");
  Assert(known.All(x=>!float.IsNaN(x)&&!float.IsInfinity(x)),"nonfinite observation");
  foreach(string kind in EmployeeObservation.ChoreKinds) {
   var v=EmployeeObservation.Build(Obs(chore:kind),0,new BotMemory(),null,null,0);
   int start=7+11;
   for(int i=0;i<EmployeeObservation.ChoreKinds.Length;i++)
    Assert(v[start+i]==(EmployeeObservation.ChoreKinds[i]==kind?1f:0f),"chore channels overlap "+kind);
  }
  Assert(EmployeeObservation.ChoreKinds.Length==13,"missing chore kinds");
  foreach(string verb in new[]{"call_off_sale","reset_screen","restore_room_lights","close_freezer","hear_complaint","announce_closing","fix_cameras"})
   Assert(EmployeeOptions.WhatOf(new EnvAction{verb=verb},false)==EmployeeWhat.AnswerChore,"unmapped verb "+verb);
  var hidden=Obs(); var her=(Dictionary<string,object>)hidden["karen"];
  her["at"]=new float[]{999,999}; her["region"]="secret";
  var c=new BotMemory(); c.ObserveFair(hidden,1);
  Assert(c.SeenAt("secret")==0,"hidden location read into memory");

  var recorder=new EscapeRecorder(); float[] pos={1,2};
  recorder.Observe(true,"origin","origin",0,pos,"today",1,9);
  recorder.Observe(false,"origin","left",1,pos,"today",1,9,new[]{"left","right"});
  recorder.Observe(false,"origin","left",2,pos,"today",1,9);
  recorder.Observe(false,"origin","clock",3,pos,"today",1,9);
  recorder.Observe(false,"origin","bins",4,pos,"today",1,9);
  var records=recorder.Snapshot();
  Assert(records.Count==1 && records[0].path.SequenceEqual(new[]{"left","clock","bins"}),"not one three-place loss");
  records[0].path[0]="tampered";
  Assert(recorder.Snapshot()[0].path[0]=="left","mutable truth snapshot");
  var saved=new List<EscapeStat>();
  Assert(EscapeHabits.Chance(saved,"today","origin","left",.5f)==.5f,"pending truth leaked");
  recorder.EndShift(); recorder.EndShift();
  EscapeHabits.Commit(saved,recorder.Snapshot());
  Assert(Near(EscapeHabits.Chance(saved,"today","origin","left",.5f),2f/3f),"wrong shrinkage");
  Assert(EscapeHabits.Chance(saved,"labyrinth","origin","left",.5f)==.5f,"layout contaminated");
  EscapeHabits.Commit(saved,new EscapeRecord[0]);
  Assert(Near(saved[0].count,.7f),"evidence did not fade");
  recorder.Reset();
  recorder.Observe(true,"origin","origin",0,pos,"today",2,9);
  recorder.Observe(false,"origin","origin",1,pos,"today",2,9);
  recorder.Observe(true,"origin","origin",2,pos,"today",2,9);
  var censored=recorder.Snapshot();
  Assert(censored.Count==1 && censored[0].end=="reacquired" && censored[0].path.Length==0,"censoring incorrect");
  EscapeHabits.Commit(saved,censored);
  Assert(saved.Count==1 && Near(saved[0].count,.49f),"censored loss fabricated a direction");
  Assert(LegacyDoorKeys.Contains("door_2") && LegacyDoorKeys.Contains("door_2:left") && LegacyDoorKeys.Contains("1_2_0|door_2|3_4_0"),"missed legacy ordinals");
  Assert(!LegacyDoorKeys.Contains("1_2_0") && !LegacyDoorKeys.Contains("room_door_2"),"ordinary key lost");
  var keys=new List<string>{"1_2_0","1_2_0|2_2_0","door_2","tile:door_2:left"};
  Assert(LegacyDoorKeys.Remove(keys,k=>k)==2 && keys.Count==2,"wrong migration entries removed");
  var timers=new RepairTiming(); var one=new object(); var two=new object();
  foreach(string kind in new[]{"flash_sale","screen_loop","room_lights","freezer_alarm","complaint"}) {
   timers.Begin(kind,one,10); timers.Begin(kind,one,12); timers.Begin(kind,two,15);
   Assert(timers.Finish(kind,one,30,true,out float duration) && duration==20,"duplicate notification reset repair time");
   Assert(!timers.Finish(kind,one,31,true,out _),"repair counted twice");
   Assert(timers.Finish(kind,two,40,true,out duration) && duration==25,"fixture times mixed");
   timers.Begin(kind,one,50);
   Assert(!timers.Finish(kind,one,60,false,out _),"cancellation/timeout counted as a repair");
   Assert(!timers.Finish(kind,one,61,true,out _),"cancelled timer reused");
  }
  timers.Begin("room_lights",one,1); timers.Clear();
  Assert(!timers.Finish("room_lights",one,10,true,out _),"repair timer survived shift boundary");
  Console.WriteLine("passed "+checks+" runtime assertions");
 }
}
''')
    sources = [support, GAME / "Eval/Scripts/BotMemory.cs", GAME / "Eval/Scripts/EmployeeObservation.cs",
               GAME / "Eval/Scripts/EmployeeOptions.cs", GAME / "AI/Scripts/Diagnostics/EscapeRecorder.cs",
               GAME / "AI/Scripts/Core/EscapeHabits.cs", GAME / "AI/Scripts/Core/LegacyDoorKeys.cs", GAME / "AI/Scripts/Core/RepairTiming.cs"]
    assert "runtime assertions" in offline_csharp.run(sources, tmp_path)


def test_players_and_live_predictors_do_not_read_escape_truth():
    for name in ("BotMemory", "EmployeeObservation", "EmployeeOptions", "EmployeeMind", "MlAgentsEmployee"):
        lines = (GAME / f"Eval/Scripts/{name}.cs").read_text().splitlines()
        code = "\n".join(line.split("//")[0] for line in lines)
        assert not re.search(r"\b(KarenBrain|KarenBody|KarenDirector|KarenLedger)\b", code)
    for category in ("Belief", "Perception", "Decision"):
        for path in (GAME / "AI/Scripts" / category).rglob("*.cs"):
            code = "\n".join(line.split("//")[0] for line in path.read_text().splitlines())
            assert not re.search(r"\b(EscapeRecord|EscapeRecording|EscapeRecorder|QueueEscapesAtShiftEnd)\b", code), path


def test_absent_karen_senses_and_training_bootstrap_are_gated():
    brain = (GAME / "AI/Scripts/Core/KarenBrain.cs").read_text()
    start = brain.split("void Start()", 1)[1].split("void OnSpillRoute", 1)[0]
    assert "Edition.ShouldKarenAppear(Shift != null ? Shift.UpcomingShiftNumber : 1)" in start
    assert start.index("Body.gameObject.SetActive(PresentThisShift)") < start.index("RebuildBelief()")
    rung = brain.split("void ApplyRungToSenses()", 1)[1].split("public void SetRung", 1)[0]
    assert "PresentThisShift && config.Features.Belief" in rung
    update = brain.split("void Update()", 1)[1].split("void TickDirector", 1)[0]
    assert update.index("if (!PresentThisShift)") < update.index("Body.Sight.Tick(dt)")
    absent = update.split("if (!PresentThisShift)", 1)[1].split('using (ShiftTiming.Measure("sight"))', 1)[0]
    assert "if (ShiftActive) TickDirector(dt);" in absent and "return;" in absent
    senses = (GAME / "AI/Scripts/Diagnostics/PlayerSenses.cs").read_text().split("public static void Sense", 1)[1]
    assert senses.index("!brain.PresentThisShift") < senses.index("if (BotPlaying)")
    bootstrap = (GAME / "AI/Scripts/Core/KarenBootstrap.cs").read_text()
    assert "if (Edition.ShowUI) root.AddComponent<ReviewScreen>();" in bootstrap
    assert "if (Edition.AllowDebugTools) root.AddComponent<KarenDebugOverlay>();" in bootstrap
    assert "if (Edition.RecordReplays)" in bootstrap
