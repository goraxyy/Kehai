// The two Karens' bridge to Unity ML-Agents (0.4.0, build plan §5.5, §10, C5.5). Compiled only with
// KAREN_MLAGENTS, like rung F's (MlAgentsTacticPolicy.cs), which it replaces for the brains.
//
// Two behaviours, "KarenVital" and "KarenBasic" (tools/rl/karen_vital.yaml, karen_basic.yaml): one
// agent each, for the whole process, on objects that outlive the eval's scene reloads. Decisions
// are synchronous: RequestDecision + EnvironmentStep, so the planner has its answer now.
//   observation   BrainObservation (version 2)
//   action        one discrete branch over every (action, trick) pair (BrainSeam.AllPairs), masked
//                 to the legal ones; tied to names when exported (export_brain.py --outputs)
//   reward        BrainPay (Vital: the catch; Basic: pacing, the catch dial, sameness, stuck)
//   heuristic     the planner's own pick: the demonstrations (record_demos.sh vital|basic) and the
//                 warm start copy it (D3)
// At startup it writes <persistent data>/kehai_eval/brain_spec_<behaviour>.json: the input version
// and size and the output names, for export_brain.py.
#if KAREN_MLAGENTS
using System.Collections.Generic;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Kehai.Karen
{
    public sealed class MlAgentsBrain : Agent, IBrainPolicy
    {
        static readonly Dictionary<KarenBrainKind, MlAgentsBrain> agents = new Dictionary<KarenBrainKind, MlAgentsBrain>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            agents.Clear();
            BrainPolicies.TrainerFactory = (kind, config) => agents.TryGetValue(kind, out MlAgentsBrain a) && a != null ? a : agents[kind] = Build(kind);
        }

        public static string BehaviorOf(KarenBrainKind kind) => kind == KarenBrainKind.Vital ? "KarenVital" : "KarenBasic";

        KarenBrainKind kind;
        List<string> outputs;
        BehaviorParameters behavior;
        DemonstrationRecorder recorder;

        static MlAgentsBrain Build(KarenBrainKind kind)
        {
            Academy.Instance.AutomaticSteppingEnabled = false;
            var go = new GameObject($"{GameNames.Antagonist} brain {kind} (ML-Agents)");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            List<string> outputs = BrainSeam.AllPairs();

            var behavior = go.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = BehaviorOf(kind);
            behavior.BrainParameters.VectorObservationSize = BrainObservation.Size;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(outputs.Count);
            behavior.InferenceDevice = InferenceDevice.Default;

            var agent = go.AddComponent<MlAgentsBrain>();
            agent.kind = kind;
            agent.outputs = outputs;
            agent.behavior = behavior;
            agent.MaxStep = 0;

            string demos = Arg("-karen-record-demos");
            if (demos != null)
            {
                DemonstrationRecorder r = go.AddComponent<DemonstrationRecorder>();
                r.Record = true;
                r.DemonstrationName = behavior.BehaviorName;
                r.DemonstrationDirectory = Path.GetFullPath(demos);
                r.NumStepsToRecord = int.TryParse(Arg("-karen-demo-steps"), out int steps) ? steps : 0;
                behavior.BehaviorType = BehaviorType.HeuristicOnly;
                agent.recorder = r;
            }
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "kehai_eval");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"brain_spec_{behavior.BehaviorName}.json"), BrainSeam.SpecJson());
            }
            catch (System.Exception e) { Debug.LogWarning("brain spec not written: " + e.Message); }
            go.SetActive(true);
            Application.quitting += () => { if (agent != null && agent.recorder != null) agent.recorder.Close(); };
            return agent;
        }

        public string Name => recorder != null ? $"the planner's picks, recorded ({BehaviorOf(kind)})" : $"{BehaviorOf(kind)} being trained";
        public string Version => "training";

        float[] inputs;
        bool[] mask;
        int plannerAction = -1, action = -1;
        IReadOnlyList<BrainSeam.Pair> legal;

        public bool Choose(float[] inputs, IReadOnlyList<BrainSeam.Pair> legal, int plannerPick, out int index, out float chance)
        {
            index = -1;
            chance = 1f;
            this.inputs = inputs;
            this.legal = legal;
            mask = new bool[outputs.Count];
            var at = new Dictionary<string, int>();
            for (int i = 0; i < outputs.Count; i++) at[outputs[i]] = i;
            foreach (BrainSeam.Pair p in legal) if (at.TryGetValue(p.Key, out int k)) mask[k] = true;
            plannerAction = plannerPick >= 0 && at.TryGetValue(legal[plannerPick].Key, out int pk) ? pk : -1;
            action = -1;
            RequestDecision();
            Academy.Instance.EnvironmentStep();
            if (action < 0) return false;
            string name = outputs[action];
            for (int i = 0; i < legal.Count; i++) if (legal[i].Key == name) index = i;
            return index >= 0;
        }

        public override void CollectObservations(VectorSensor sensor) => sensor.AddObservation(inputs ?? new float[BrainObservation.Size]);

        public override void WriteDiscreteActionMask(IDiscreteActionMask m)
        {
            if (mask == null) return;
            bool any = false;
            foreach (bool b in mask) any |= b;
            if (!any) return;
            for (int i = 0; i < mask.Length; i++) m.SetActionEnabled(0, i, mask[i]);
        }

        public override void OnActionReceived(ActionBuffers a) => action = a.DiscreteActions[0];

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<int> d = actionsOut.DiscreteActions;
            int first = -1;
            if (mask != null) for (int i = 0; i < mask.Length && first < 0; i++) if (mask[i]) first = i;
            d[0] = plannerAction >= 0 ? plannerAction : System.Math.Max(0, first);
        }

        void IBrainPolicy.AddReward(float r) => AddReward(r);

        public void EndShift()
        {
            EndEpisode();
            Academy.Instance.EnvironmentStep();
        }

        static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
#endif
