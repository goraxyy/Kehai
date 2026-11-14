// Offline boundary adapters only. Ledger/placement/escape code and test cases are real.
// System.Text.Json stands in for Unity's native JsonUtility; Unity serialization still
// requires the deferred EditMode run.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine {
 public static class Mathf {
  public static float Min(float a,float b)=>Math.Min(a,b);
  public static float Max(float a,float b)=>Math.Max(a,b);
  public static float Clamp01(float a)=>Math.Clamp(a,0,1);
  public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
  public static float Sqrt(float a)=>(float)Math.Sqrt(a);
  public static float Log(float a)=>(float)Math.Log(a);
  public static float Exp(float a)=>(float)Math.Exp(a);
 }
 public struct Vector3 { public static float Distance(Vector3 a,Vector3 b)=>0; }
 public static class Application { public static string persistentDataPath=>System.IO.Path.GetTempPath(); }
 public static class Debug { public static void LogWarning(object value){} }
 public static class JsonUtility {
  static readonly System.Text.Json.JsonSerializerOptions Options=new System.Text.Json.JsonSerializerOptions{IncludeFields=true, IgnoreReadOnlyProperties=true};
  public static T FromJson<T>(string json)=>System.Text.Json.JsonSerializer.Deserialize<T>(json,Options);
  public static string ToJson(object value,bool pretty=false)=>System.Text.Json.JsonSerializer.Serialize(value,Options);
 }
}
namespace Kehai.Store { }
public static class StoreLayoutRuntime { public static string CurrentId=>"today"; }
public static class GameNames { public const string Antagonist="Karen"; }
namespace Kehai.Karen {
 public class KarenConfig { public string ledgerPath; public bool thompson,comboContext,careerTable; public float ucbC,habituationLambda,habituationTau=1,priorWeight,learningRate; }
 public static class PanicIndex { public const int Features=8; }
 public class Tactic { public string Id,Attacks; public int IntroducedShift; public float PanicPrior; public bool Learnable; }
 public static class TacticLibrary { public static Tactic Get(string id)=>string.IsNullOrEmpty(id)?null:new Tactic{Id=id}; }
 public class KarenRng { public float Value=>0; public float Gaussian()=>0; public bool Chance(float p)=>false; public int Range(int a,int b)=>a; }
 public class LedgerStart { public float SprintShare; public float Kind(string k,int p)=>0; public float Trick(string k,int p)=>0; public float AnswerSeconds(string k)=>0; }
 public enum MotionState { Still,Crouching,Walking,Sprinting }
 public class Region { public string Key,Name,Area; public int Id; public bool IsDoor,IsChokepoint; public UnityEngine.Vector3 Centroid; public List<int> Cells=new List<int>(); }
 public class StoreMap { public List<Region> Regions=new List<Region>(); }
 public class Features { public bool Bandit,Persistent; }
 public class Brain { public int LikelyNextJobRegion; }
 public class Belief { public int PeakRegion; }
 public class KarenContext {
  public Features Features=new Features(); public Brain Brain; public Belief Belief=new Belief(); public LastTrick Last;
  public KarenConfig Config; public int ShiftNumber; public float Now; public KarenRng Rng=new KarenRng(); public StoreMap Map;
  public void Think(string a,string b){} public string RegionName(int id)=>id.ToString();
 }
}
namespace NUnit.Framework {
 public class TestAttribute:Attribute{} public class SetUpAttribute:Attribute{} public class TearDownAttribute:Attribute{}
 public static class Does { public static string Contain(string text)=>text; }
 public static class Assert {
  public static void AreEqual(object expected,object actual,string message=null) {
   if(!Equals(expected,actual))throw new Exception(message??$"Expected {expected}, got {actual}");
  }
  public static void AreEqual(double expected,double actual,double tolerance) {
   if(Math.Abs(expected-actual)>tolerance)throw new Exception($"Expected {expected}, got {actual}");
  }
  public static void IsEmpty(IEnumerable actual) { if(actual.Cast<object>().Any())throw new Exception("Expected empty collection"); }
  public static void That(string actual,string contains) { if(!actual.Contains(contains))throw new Exception("Missing "+contains); }
 }
}
class LedgerProbe {
 static void Main() {
  int count=0;
  foreach(var test in typeof(Karen050LearningTests).GetMethods().Where(m=>m.IsDefined(typeof(NUnit.Framework.TestAttribute),false))) {
   var suite=new Karen050LearningTests(); suite.SetUp();
   try { test.Invoke(suite,null); count++; }
   catch(System.Reflection.TargetInvocationException e){throw new Exception(test.Name,e.InnerException);}
   finally { suite.TearDown(); }
  }
  Console.WriteLine($"passed {count} ledger tests (offline JSON adapter)");
 }
}
