// The simulated players' trained mind, through Unity ML-Agents (0.4.0, build plan §7, C2.4).
//
// Compiled only with KAREN_MLAGENTS, like Karen's own bridge (MlAgentsTacticPolicy.cs). One agent
// for the whole process, on an object that outlives the eval's scene reloads; each SimulatedPlayer
// with -bot-mind trainer asks it at every decision, synchronously: RequestDecision, then one
// EnvironmentStep, so the player has its answer now.
//
// Behaviour "Employee" (tools/rl/employee_ppo.yaml): the observation is EmployeeObservation, the
// actions three discrete branches (what 14, how 3, which 3), masked. Where choices come from:
//   a trainer connected (mlagents-learn)              → the policy being trained
//   -bot-record-demos <folder>                        → none: the scripted heuristic plays, and
//                                                       each of its choices is recorded (C2.5)
//   else                                              → Heuristic(): the last scripted choice
// Nothing here reads Karen: it sees what the player's observation says (Players_NeverReadKaren).
#if KAREN_MLAGENTS
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Kehai.Eval
{
    public sealed class MlAgentsEmployee : Agent, IEmployeeMind, IEmployeeDemonstrator
    {
        public const string Behavior = "Employee";
        static MlAgentsEmployee instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            instance = null;
            EmployeeMinds.MlAgentsFactory = () => instance != null ? instance : instance = Build();
        }

        BehaviorParameters behavior;
        DemonstrationRecorder recorder;

        static MlAgentsEmployee Build()
        {
            Academy.Instance.AutomaticSteppingEnabled = false;
            var go = new GameObject("Employee mind (ML-Agents)");
            go.SetActive(false);
            DontDestroyOnLoad(go);

            var behavior = go.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = Behavior;
            behavior.BrainParameters.VectorObservationSize = EmployeeObservation.Size;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(EmployeeOptions.Whats, EmployeeOptions.Hows, EmployeeOptions.Whiches);
            behavior.InferenceDevice = InferenceDevice.Default;

            var agent = go.AddComponent<MlAgentsEmployee>();
            agent.behavior = behavior;
            agent.MaxStep = 0;

            string demos = Arg("-bot-record-demos");
            if (demos != null)
            {
                DemonstrationRecorder r = go.AddComponent<DemonstrationRecorder>();
                r.Record = true;
                r.DemonstrationName = Behavior;
                r.DemonstrationDirectory = Path.GetFullPath(demos);
                r.NumStepsToRecord = int.TryParse(Arg("-bot-demo-steps"), out int steps) ? steps : 0;
                behavior.BehaviorType = BehaviorType.HeuristicOnly;
                agent.recorder = r;
            }
            go.SetActive(true);
            Application.quitting += () => { if (agent != null && agent.recorder != null) agent.recorder.Close(); };
            return agent;
        }

        public string Name => recorder != null ? "the heuristic, recorded" : Academy.Instance.IsCommunicatorOn ? "the Employee policy being trained" : "the Employee heuristic";
        public bool Recording => recorder != null;

        float[] inputs;
        bool[] what, how, which;
        int[] action = { -1, -1, -1 };
        EmployeeChoice shown;
        bool showing;

        public bool TryChoose(float[] inputs, bool[] what, bool[] how, bool[] which, Persona persona, System.Random dice, out EmployeeChoice choice)
        {
            choice = default;
            if (Recording || !Academy.Instance.IsCommunicatorOn) return false;   // the heuristic plays
            Step(inputs, what, how, which);
            if (action[0] < 0) return false;
            choice = new EmployeeChoice { What = (EmployeeWhat)action[0], How = (EmployeeHow)action[1], Which = (EmployeeWhich)action[2], Chance = 1f, By = Name };
            return true;
        }

        public void Demonstrate(float[] inputs, bool[] what, bool[] how, bool[] which, EmployeeChoice choice)
        {
            shown = choice;
            showing = true;
            what = (bool[])what.Clone();
            what[(int)choice.What] = true;   // a demonstration is never masked out
            Step(inputs, what, how, which);
            showing = false;
        }

        void Step(float[] inputs, bool[] what, bool[] how, bool[] which)
        {
            this.inputs = inputs;
            this.what = what;
            this.how = how;
            this.which = which;
            action[0] = action[1] = action[2] = -1;
            RequestDecision();
            Academy.Instance.EnvironmentStep();
        }

        public override void CollectObservations(VectorSensor sensor) =>
            sensor.AddObservation(inputs ?? new float[EmployeeObservation.Size]);

        public override void WriteDiscreteActionMask(IDiscreteActionMask mask)
        {
            Mask(mask, 0, what);
            Mask(mask, 1, how);
            Mask(mask, 2, which);
        }

        static void Mask(IDiscreteActionMask mask, int branch, bool[] open)
        {
            if (open == null) return;
            bool any = false;
            foreach (bool b in open) any |= b;
            if (!any) return;   // a branch with nothing open would stall the trainer
            for (int i = 0; i < open.Length; i++) mask.SetActionEnabled(branch, i, open[i]);
        }

        public override void OnActionReceived(ActionBuffers a)
        {
            for (int i = 0; i < 3; i++) action[i] = a.DiscreteActions[i];
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<int> d = actionsOut.DiscreteActions;
            d[0] = showing ? (int)shown.What : (int)EmployeeWhat.Wait;
            d[1] = showing ? (int)shown.How : (int)EmployeeHow.Walk;
            d[2] = showing ? (int)shown.Which : (int)EmployeeWhich.Nearest;
        }

        void IEmployeeMind.AddReward(float reward) => AddReward(reward);

        public void EndShift(bool careerOver)
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
