// Rung G's bridge to Unity ML-Agents (tools/rl/README.md).
//
// Compiled only when the project defines KAREN_MLAGENTS, because it needs the ML-Agents
// package (com.unity.ml-agents 4.1.0, which brings the Inference Engine with it), and this
// repository carries no packages. Kehai → RL → Set Up ML-Agents adds both.
//
// One agent for the whole process, on an object that outlives scene loads: the eval harness
// reloads the store for every episode, and Karen with it, but the trainer must keep talking
// to the same agent. KarenBrain hands it each new Karen's context through TacticPolicy.Create.
//
// Decisions are synchronous. ML-Agents normally steps in FixedUpdate and delivers actions a
// step later; the planner needs its answer now, so automatic stepping is off and every
// decision is one RequestDecision + EnvironmentStep. A step with no decision in it never talks
// to the trainer, so the game runs at full speed between decisions.
//
// Where the choice comes from (ML-Agents' "Default" behaviour type):
//   a trainer connected (mlagents-learn passes --mlagents-port)  → the policy being trained
//   else a model under Resources/KarenPolicy/                     → that model, argmax
//   else                                                          → Heuristic(): the bandit's pick
//
// Command line, besides KarenBootstrap's -karen-* flags:
//   -karen-record-demos <folder>   record what the heuristic does, for behavioural cloning
//   -karen-demo-steps <n>          stop the game after n recorded decisions
#if KAREN_MLAGENTS
using System.Collections.Generic;
using System.IO;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Demonstrations;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Kehai.Karen
{
    public sealed class MlAgentsTacticPolicy : Agent, ITacticPolicy
    {
        public const string Behavior = "Karen";       // the key under behaviors: in tools/rl/karen_ppo.yaml
        public const string ModelFolder = "KarenPolicy";
        const string DefaultModel = "karen";

        static MlAgentsTacticPolicy instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            instance = null;
            TacticPolicy.Factory = Create;
        }

        static ITacticPolicy Create(KarenConfig config)
        {
            if (instance == null) instance = Build(config);
            else instance.Configure(config);
            return instance;
        }

        // ---- the agent's object -----------------------------------------------------------

        BehaviorParameters behavior;
        DemonstrationRecorder recorder;
        string modelName;

        // Built inactive, so every component is set up before Agent.OnEnable reads them.
        static MlAgentsTacticPolicy Build(KarenConfig config)
        {
            Academy.Instance.AutomaticSteppingEnabled = false;

            var go = new GameObject(GameNames.Antagonist + " policy (ML-Agents)");
            go.SetActive(false);
            DontDestroyOnLoad(go);

            var behavior = go.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = Behavior;
            behavior.BrainParameters.VectorObservationSize = PolicyObservation.Size;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(PolicyMask.Size);
            behavior.InferenceDevice = InferenceDevice.Default;
            behavior.DeterministicInference = true;     // a trained model is evaluated on its best guess

            go.AddComponent<BeliefSensorComponent>();

            string demos = Arg("-karen-record-demos");
            DemonstrationRecorder recorder = null;
            if (demos != null)
            {
                recorder = go.AddComponent<DemonstrationRecorder>();
                recorder.Record = true;
                recorder.DemonstrationName = Behavior;
                recorder.DemonstrationDirectory = Path.GetFullPath(demos);
                recorder.NumStepsToRecord = int.TryParse(Arg("-karen-demo-steps"), out int steps) ? steps : 0;
                behavior.BehaviorType = BehaviorType.HeuristicOnly;
            }

            var agent = go.AddComponent<MlAgentsTacticPolicy>();
            agent.behavior = behavior;
            agent.recorder = recorder;
            agent.MaxStep = 0;
            agent.Configure(config);
            go.SetActive(true);
            Application.quitting += () => { if (agent != null && agent.recorder != null) agent.recorder.Close(); };
            return agent;
        }

        // A new Karen (every episode) may name a different model.
        void Configure(KarenConfig config)
        {
            string wanted = string.IsNullOrEmpty(config.policyModel) ? DefaultModel : config.policyModel;
            if (wanted == modelName && behavior.Model != null) return;
            modelName = wanted;
            ModelAsset model = Resources.Load<ModelAsset>(ModelFolder + "/" + wanted);
            if (model == null && !string.IsNullOrEmpty(config.policyModel))
                Debug.LogWarning($"{GameNames.Antagonist} policy: no model Resources/{ModelFolder}/{wanted} — using the trainer if there is one, else the bandit's choice.");
            // BehaviorParameters reloads the agent's policy itself (once the agent is running);
            // Agent.SetModel would also end the episode, and it isn't usable before OnEnable.
            if (behavior.BehaviorType != BehaviorType.HeuristicOnly) behavior.Model = model;
        }

        public string Name
        {
            get
            {
                if (Academy.Instance.IsCommunicatorOn && behavior.BehaviorType != BehaviorType.HeuristicOnly) return "the policy being trained (ML-Agents trainer connected)";
                if (behavior.Model != null && behavior.BehaviorType != BehaviorType.HeuristicOnly) return $"trained model {modelName}";
                return recorder != null ? "the bandit's pick, recorded as a demonstration" : "the bandit's pick (no model, no trainer)";
            }
        }

        // ---- one decision -----------------------------------------------------------------

        KarenContext context;
        GoalId goal;
        bool[] mask;
        int heuristic = -1;
        int action = -1;
        int decisionsThisShift, agreedThisShift;
        float rewardThisShift;

        public KarenContext Context => context;

        public bool TryChoose(GoalId goal, IReadOnlyList<Tactic> candidates, Tactic bandit, KarenContext c, out int chosen, out string why)
        {
            chosen = -1;
            context = c;
            this.goal = goal;
            mask = PolicyMask.Build(candidates);
            heuristic = TacticLibrary.IndexOf(bandit);
            action = -1;

            RequestDecision();
            Academy.Instance.EnvironmentStep();
            decisionsThisShift++;
            if (action == heuristic) agreedThisShift++;

            if (action < 0 || action >= PolicyMask.Size)
            {
                why = "no action came back";
                return false;
            }
            Tactic picked = TacticLibrary.All[action];
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] == picked) chosen = i;
            if (chosen < 0)
            {
                why = $"{picked.Id} isn't a candidate — the action mask leaked";
                return false;
            }
            why = action == heuristic ? "agrees with the bandit" : $"the bandit would have picked {TacticLibrary.All[heuristic].Id}";
            return true;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            if (context != null) sensor.AddObservation(PolicyObservation.Build(goal, context));
            else sensor.AddObservation(new float[PolicyObservation.Size]);
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            if (mask == null) return;
            for (int i = 0; i < mask.Length; i++) actionMask.SetActionEnabled(0, i, mask[i]);
        }

        public override void OnActionReceived(ActionBuffers actions) => action = actions.DiscreteActions[0];

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<int> discrete = actionsOut.DiscreteActions;
            discrete[0] = heuristic >= 0 ? heuristic : 0;   // the bandit's pick: rung F, the teacher
        }

        // ---- the episode ------------------------------------------------------------------

        void ITacticPolicy.AddReward(float reward)
        {
            AddReward(reward);
            rewardThisShift += reward;
        }

        // The shift is over: hand its last reward to the trainer now, not with the next
        // shift's first decision. A shift with no decision in it has nothing to credit.
        public void EndShift()
        {
            Debug.Log($"{GameNames.Antagonist} policy: {decisionsThisShift} decisions this shift, {agreedThisShift} the bandit's pick too; " +
                      $"reward {rewardThisShift:0.000}; {Name}");
            int decisions = decisionsThisShift;
            decisionsThisShift = agreedThisShift = 0;
            rewardThisShift = 0f;
            if (decisions == 0)
            {
                SetReward(0f);
                return;
            }
            EndEpisode();
            Academy.Instance.EnvironmentStep();
            if (!Mathf.Approximately(Time.timeScale, 1f) && Academy.Instance.IsCommunicatorOn)
                Debug.LogWarning($"{GameNames.Antagonist} policy: the trainer set Time.timeScale to {Time.timeScale}; set engine_settings.time_scale: 1 (tools/rl/karen_ppo.yaml).");
        }

        static string Arg(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }

    // Her belief as a small picture (BeliefRaster), for the policy's convolutional encoder.
    public sealed class BeliefSensorComponent : SensorComponent
    {
        public override ISensor[] CreateSensors() => new ISensor[] { new BeliefSensor(GetComponent<MlAgentsTacticPolicy>()) };
    }

    public sealed class BeliefSensor : ISensor
    {
        readonly MlAgentsTacticPolicy agent;
        readonly BeliefRaster raster = new BeliefRaster();
        readonly float[] buffer = new float[BeliefRaster.Length];

        public BeliefSensor(MlAgentsTacticPolicy agent) => this.agent = agent;

        public ObservationSpec GetObservationSpec() => ObservationSpec.Visual(BeliefRaster.Channels, BeliefRaster.Size, BeliefRaster.Size);

        public int Write(ObservationWriter writer)
        {
            KarenContext c = agent != null ? agent.Context : null;
            if (c != null) raster.Write(c.Map, c.Belief, c.Body != null ? c.Body.Position : Vector3.zero, buffer);
            else System.Array.Clear(buffer, 0, buffer.Length);

            const int s = BeliefRaster.Size;
            for (int ch = 0; ch < BeliefRaster.Channels; ch++)
                for (int h = 0; h < s; h++)
                    for (int w = 0; w < s; w++)
                        writer[ch, h, w] = buffer[ch * s * s + h * s + w];
            return buffer.Length;
        }

        public byte[] GetCompressedObservation() => null;
        public void Update() { }
        public void Reset() { }
        public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
        public string GetName() => "KarenBelief";
    }
}
#endif
