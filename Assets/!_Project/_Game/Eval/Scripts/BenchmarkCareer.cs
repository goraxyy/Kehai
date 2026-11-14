using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // Frozen, nonlearning career transport. Never substitutes a heuristic for a trained mind.
    public static class BenchmarkCareer
    {
        static readonly string[] Scripted = { "efficient", "skittish", "reckless", "cautious" };
        public static Dictionary<string,object> Capabilities()
        {
            var arms=new List<string>{"A","B","C","D","E","planner"};
            if(TrainedBrain("Basic")) arms.Add("Basic");
            if(TrainedBrain("Vital")) arms.Add("Vital");
            var players=new List<string>(Scripted);
            if(GeneratedEmployeeMind.TryCreate(out _) is GeneratedEmployeeMind employee && employee.Trained)
            { players.Add("trained-range"); players.Add("careful-corner"); }
            return new Dictionary<string,object>
            {
                ["ok"]=EditionRules.AllowEvaluation(Edition.Current),["benchmark_protocol"]=1,
                ["build_edition"]=Edition.Current.ToString().ToLowerInvariant(),["career_semantics"]="gameplay",
                ["build_commit"]=BuildCommit(),["arms"]=arms,["players"]=players,
                ["layouts"]=new[]{"today"}.Concat(StoreLayouts.All.Select(l=>l.Id)).ToArray(),
                ["four_shift_careers"]=true,
                ["model_pack_version"]=ModelPack.Current!=null?(object)ModelPack.Current.PackVersion:null,
            };
        }
        static bool TrainedBrain(string arm)
        {
            BrainNet net=BrainNets.Find("KarenBrain"+arm,out _);
            return net!=null&&net.Card.Trained&&BrainSeam.Accepts(net,out _);
        }
        static string BuildCommit()
        {
            // Written beside the built app by KehaiBuild, not supplied by a benchmark request.
            try
            {
                var data=Application.dataPath;
                var root=Application.platform==RuntimePlatform.OSXPlayer
                    ? Directory.GetParent(data)?.Parent?.FullName : Directory.GetParent(data)?.FullName;
                string file=Path.Combine(root??"","build-provenance.json");
                var record=File.Exists(file)?MiniJson.ParseObject(File.ReadAllText(file)):null;
                // A dirty source tree cannot be represented by its HEAD commit alone.
                if(record==null||!record.TryGetValue("dirty",out object dirty)||!(dirty is bool isDirty)||isDirty) return null;
                string commit=record.GetString("commit",null);
                return commit!=null&&commit.Length==40&&commit.All(Uri.IsHexDigit)
                    &&record.GetString("edition",null)==Edition.Current.ToString().ToLowerInvariant()?commit:null;
            }
            catch(IOException) { return null; }
            catch(UnauthorizedAccessException) { return null; }
            catch(FormatException) { return null; }
        }
        public static void Validate(Dictionary<string,object> request)
        {
            if(!EditionRules.AllowEvaluation(Edition.Current)) throw new InvalidOperationException("Evaluation is disabled in Gameplay builds");
            if(request.GetNumber("benchmark_protocol",0)!=1||request.GetString("purpose","")!="evaluate"||request.GetString("career_semantics","")!="gameplay")
                throw new ArgumentException("Only protocol-1 nonlearning Gameplay-semantics evaluation is supported");
            string[] launch=Environment.GetCommandLineArgs();
            if(KehaiEnv.TrainerPort!=0||launch.Contains("-karen-record-demos")||launch.Contains("-bot-record-demos")||launch.Contains("-league"))
                throw new InvalidOperationException("Benchmark requires a nonlearning launch without trainer/demo/league arguments");
            string half=request.GetString("half","");
            if(half!="practice"&&half!="hidden") throw new ArgumentException("Unknown benchmark half");
            string hash=request.GetString("definition_sha256","");
            if(hash.Length!=64||!hash.All(Uri.IsHexDigit)) throw new ArgumentException("Definition SHA-256 required");
            var caps=Capabilities();
            string arm=request.GetString("arm","");
            if(!((List<string>)caps["arms"]).Contains(arm)) throw new ArgumentException("Requested frozen arm is unavailable: "+arm);
            var career=request.GetObject("career")??throw new ArgumentException("Career object required");
            string player=career.GetString("player","");
            if(!((List<string>)caps["players"]).Contains(player)) throw new ArgumentException("Requested player mind unavailable: "+player);
            double rawSeed=career.GetNumber("seed",-1);
            if(double.IsNaN(rawSeed)||double.IsInfinity(rawSeed)||rawSeed!=Math.Truncate(rawSeed)||rawSeed<int.MinValue||rawSeed>int.MaxValue)
                throw new ArgumentException("An integer career seed is required");
            int seed=(int)rawSeed;
            if(half=="hidden" ? seed<30000||seed>=30050 : seed<20000||seed>=20050) throw new ArgumentException("Seed outside declared benchmark half");
            if(career.GetString("edition","")!="gameplay") throw new ArgumentException("Career edition mismatch");
            if(!(career.TryGetValue("shifts",out object raw)&&raw is List<object> shifts&&shifts.Count==4)) throw new ArgumentException("Four explicit shifts required");
            for(int i=0;i<4;i++)
            {
                var shift=shifts[i] as Dictionary<string,object>??throw new ArgumentException("Malformed shift");
                if(shift.GetNumber("shift",0)!=i+1||shift.GetBool("karen",false)!=(i>0)||!shift.GetBool("record_habits",false)) throw new ArgumentException("Invalid Gameplay career schedule");
                string id=shift.GetString("layout","");
                if(id!="today") StoreLayouts.Find(id);
            }
            string ledger=request.GetString("ledger","");
            if(ledger!="retained"&&ledger!="wiped") throw new ArgumentException("Unknown Ledger condition");
            double horizon=request.GetNumber("horizon_seconds",0);
            if(horizon!=150) throw new ArgumentException("Frozen benchmark horizon is 150 seconds");
            MakePersona(player,career.GetObject("traits")??new Dictionary<string,object>());
        }
        static Persona MakePersona(string player, Dictionary<string,object> overrides)
        {
            Persona persona;
            if(Array.IndexOf(Scripted,player)>=0)
            {
                Enum.TryParse(player,true,out PlayerProfile profile); persona=Persona.Of(profile);
                if(overrides.Count!=0) throw new ArgumentException("Scripted profiles have fixed traits");
            }
            else persona=new Persona(player,Enumerable.Repeat(.5f,Persona.Count).ToArray());
            if(Array.IndexOf(Scripted,player)<0 && (overrides.Count!=Persona.Count ||
                Persona.Names.Any(n=>!overrides.ContainsKey(n.ToLowerInvariant()))))
                throw new ArgumentException("All 14 explicit persona traits are required");
            foreach(var pair in overrides)
            {
                int index=Array.FindIndex(Persona.Names,n=>n.Equals(pair.Key,StringComparison.OrdinalIgnoreCase));
                if(index<0) throw new ArgumentException("Unknown persona trait: "+pair.Key);
                double value=Convert.ToDouble(pair.Value,System.Globalization.CultureInfo.InvariantCulture);
                if(double.IsNaN(value)||double.IsInfinity(value)||value<0||value>1) throw new ArgumentException("Invalid persona trait");
                persona.Traits[index]=(float)value;
            }
            if(player=="careful-corner"&&!EvalSplits.InCarefulCorner(persona)) throw new ArgumentException("Persona is outside the careful corner");
            return persona;
        }
        public static IEnumerator Play(KehaiEnv env,Dictionary<string,object> request,Action<Dictionary<string,object>> reply)
        {
            Validate(request);
            var career=request.GetObject("career");
            var shifts=(List<object>)career["shifts"];
            int seed=(int)career.GetNumber("seed",0);
            string player=career.GetString("player",""); string arm=request.GetString("arm","");
            string ledger=request.GetString("ledger","");
            var traits=career.GetObject("traits")??new Dictionary<string,object>();
            Persona persona=MakePersona(player,traits);
            IEmployeeMind mind=Array.IndexOf(Scripted,player)>=0?null:GeneratedEmployeeMind.TryCreate(out _);
            if(Array.IndexOf(Scripted,player)<0&&(mind==null || !((GeneratedEmployeeMind)mind).Trained)) throw new InvalidOperationException("Required frozen player mind disappeared");
            var memory=new BotMemory();var rows=new List<object>();
            string ledgerPath=Path.Combine(KehaiEnv.EvalDirectory,"benchmark-ledger.json");
            var selection=ModelPack.PinSelection();
            var previous=KehaiEnv.KarenSetup;
            bool previousSimulation=CareerRuntime.GameplaySimulation;
            bool ownsEpisode=false;
            try
            {
                KehaiEnv.KarenSetup=cfg=>
                {
                    new KarenSpec {brain=arm=="Vital"?"vital":arm=="Basic"?"basic":"planner",rung=arm.Length==1?arm:"E"}.Apply(cfg);
                    cfg.learningRecords=false; cfg.trapFences=true;
                };
                for(int index=0;index<4;index++)
                {
                    var shift=(Dictionary<string,object>)shifts[index];
                    string layout=shift.GetString("layout","");
                    var config=new EnvConfig {seed=seed,rung=arm.Length==1?arm:"E",agent=player,startShift=index+1,shiftSeconds=150,fps=20,
                        render=false,syntheticBlinks=true,freshLedger=index==0||ledger=="wiped",ledgerPath=ledgerPath,
                        layout=layout,heldoutLayout=EvalSplits.HeldOutLayout(layout),trainingLayouts=false,gameplayCareer=true};
                    ownsEpisode=true;
                    yield return env.ResetEpisode(config);
                    if(!env.Metrics.Started) throw new InvalidOperationException("Scheduled shift never started");
                    var brain=KarenBrain.Instance;
                    if(brain==null||brain.PresentThisShift!=(index>0)||brain.Body==null||
                        brain.Body.gameObject.activeInHierarchy!=(index>0)||StoreLayoutRuntime.CurrentId!=layout||
                        brain.config.seed!=seed||env.Metrics.Seed!=seed||env.Metrics.Shift!=index+1||
                        Path.GetFullPath(brain.config.ledgerPath)!=Path.GetFullPath(ledgerPath))
                        throw new InvalidOperationException("Runtime career differs from request");
                    KarenRung expectedRung;
                    KarenBootstrap.TryParseRung(arm.Length==1?arm:"E",out expectedRung);
                    var expectedBrain=arm=="Basic"?KarenBrainKind.Basic:arm=="Vital"?KarenBrainKind.Vital:KarenBrainKind.Planner;
                    if(brain.config.rung!=expectedRung||brain.config.brain!=expectedBrain||brain.config.brainFromTrainer)
                        throw new InvalidOperationException("Runtime arm differs from request (check launch overrides)");
                    if((arm=="Basic"||arm=="Vital")&&(!(brain.BrainPolicy is GeneratedBrain)||!TrainedBrain(arm)))
                        throw new InvalidOperationException("Frozen trained Karen brain unavailable; refusing trainer/planner substitution");
                    var bot=new SimulatedPlayer(env,persona,seed*31+index,true,memory,mind){CareerOver=index==3};
                    yield return bot.PlayShift();
                    var actualTraits=new Dictionary<string,object>();
                    foreach(var pair in traits)
                    { int n=Array.FindIndex(Persona.Names,name=>name.Equals(pair.Key,StringComparison.OrdinalIgnoreCase)); actualTraits[pair.Key]=(double)persona.Traits[n]; }
                    rows.Add(new Dictionary<string,object>
                    {
                        ["executed"]=new Dictionary<string,object>{["seed"]=env.Metrics.Seed,["player"]=player,["shift"]=env.Metrics.Shift,
                            ["layout"]=StoreLayoutRuntime.CurrentId,["edition"]="gameplay",["ledger"]=ledger,["arm"]=arm,
                            ["traits"]=actualTraits,["karen_present"]=brain.PresentThisShift},
                        ["actual_persona"]=persona.Traits,["metrics"]=env.Metrics.ToDictionary(),
                        ["metric_samples"]=new Dictionary<string,object>{["panic_setpoint_rmse"]=brain.Stats.PacingSamples},
                        ["trick_counts_complete"]=true,["pacing"]=!brain.config.PacingOff,
                    });
                }
                reply(new Dictionary<string,object>{["ok"]=true,["rows"]=rows});
            }
            finally
            {
                try
                {
                    // Failed/cancelled careers must not leave a live shift ticking or an
                    // unsaved Ledger that can overwrite the next request's fresh wipe.
                    if(ownsEpisode&&env.Shift!=null&&env.Shift.IsShiftActive) env.Shift.EndShift(completed:false);
                }
                finally { KehaiEnv.KarenSetup=previous; CareerRuntime.GameplaySimulation=previousSimulation; selection.Dispose(); }
            }
        }
    }
}
