using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Karen
{
    // Puts Karen into the store when the scene loads. Entirely from code — the scene file
    // isn't in version control, so nothing about her may depend on it.
    //
    // Command line (for builds and the eval harness):
    //   -replay <file.krec>  watch a recorded shift instead (ReplayMode)
    //   -nokaren              run the store without her
    //   -karen-rung <A..F>    choose the ablation rung
    //   -karen-seed <n>       seed every decision (0 = clock)
    //   -karen-shield on|off  rung F: keep the policy inside the Director's gates (default on)
    //   -karen-career-table on|off  tricks unlock shift by shift (default off since 8 November)
    //   -karen-policy <name>  rung F: the model under Resources/KarenPolicy/ to run
    //   -kehai-no-records     write no shift records, replays or clip markers (RL training runs
    //                         thousands of shifts)
    // 0.4.0 (build plan §5–§8):
    //   -karen-brain planner|basic|vital   which brain picks her action and trick (default planner)
    //   -karen-mirror k0|k1|k2   what she assumes you know of where she is (default k0)
    //   -karen-senses s0|s1|s2   her senses' numbers (default s0, today's)
    //   -belief-motion p0|p1|p2|p3   her belief's predict step (default p0, today's)
    //   -karen-hunter on|off     the planner with pacing and the hint off (Vital's baseline)
    //   -karen-learning on|off   one learning record per decision
    //   -karen-pack <file>       a model pack to use instead of the build's own numbers
    //   -karen-set field=value   any KarenConfig field, for one test arm (repeatable)
    public static class KarenBootstrap
    {
        // Set by the eval harness before a scene load to configure the next Karen.
        public static KarenConfig Override;
        public static bool Disabled;
        public static bool NoRecords;

        // Every later load of the store gets a Karen too — the eval harness reloads the scene
        // on each reset, and RuntimeInitializeOnLoadMethod only fires for the first one.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) Install();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            ReadCommandLine();
            if (Object.FindAnyObjectByType<ShiftManager>() == null) return;   // not the store
            if (Object.FindAnyObjectByType<Kehai.Replay.ReplayPlayer>() != null) return;

            // A store loaded to watch a recorded shift gets a replay instead of her.
            string replay = Kehai.Replay.ReplayMode.TakePending();
            if (replay != null)
            {
                Kehai.Replay.ReplayPlayer.Install(replay);
                return;
            }

            if (Disabled) return;
            if (Object.FindAnyObjectByType<KarenBrain>() != null) return;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                if (player.GetComponent<PlayerPresence>() == null) player.AddComponent<PlayerPresence>();
                if (player.GetComponent<FootprintTrail>() == null) player.AddComponent<FootprintTrail>();
            }

            foreach (CustomerNPC npc in Object.FindObjectsByType<CustomerNPC>())
                if (npc.GetComponent<CustomerMemory>() == null) npc.gameObject.AddComponent<CustomerMemory>();

            var root = new GameObject(GameNames.Antagonist);
            var brain = root.AddComponent<KarenBrain>();
            if (Override != null) brain.config = Override.Clone();
            ApplyCommandLine(brain.config);
            MatchPlayer(brain.config);
            if (!NoRecords)
            {
                root.AddComponent<ShiftRecorder>();
                root.AddComponent<ClipMarkerRecorder>();
                root.AddComponent<Kehai.Replay.ReplayRecorder>();
            }
            root.AddComponent<KarenDebugOverlay>();
            root.AddComponent<ReviewScreen>();
            root.AddComponent<Kehai.Blink.BlinkTracker>();
            root.AddComponent<Kehai.Blink.BlinkTestPanel>();
        }

        static string rungArg, seedArg, shieldArg, policyArg, comboArg, careerArg;
        static string brainArg, mirrorArg, sensesArg, motionArg, hunterArg, learningArg, packArg;
        static readonly System.Collections.Generic.List<string> setArgs = new System.Collections.Generic.List<string>();

        static void ReadCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            setArgs.Clear();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nokaren") Disabled = true;
                bool next = i + 1 < args.Length;
                if (args[i] == "-karen-brain" && next) brainArg = args[i + 1];
                if (args[i] == "-karen-mirror" && next) mirrorArg = args[i + 1];
                if (args[i] == "-karen-senses" && next) sensesArg = args[i + 1];
                if (args[i] == "-belief-motion" && next) motionArg = args[i + 1];
                if (args[i] == "-karen-hunter" && next) hunterArg = args[i + 1];
                if (args[i] == "-karen-learning" && next) learningArg = args[i + 1];
                if (args[i] == "-karen-pack" && next) packArg = args[i + 1];
                if (args[i] == "-karen-set" && next) setArgs.Add(args[i + 1]);
                if (args[i] == "-karen-rung" && i + 1 < args.Length) rungArg = args[i + 1];
                if (args[i] == "-karen-seed" && i + 1 < args.Length) seedArg = args[i + 1];
                if (args[i] == "-karen-shield" && i + 1 < args.Length) shieldArg = args[i + 1];
                if (args[i] == "-karen-career-table" && i + 1 < args.Length) careerArg = args[i + 1];
                if (args[i] == "-karen-policy" && i + 1 < args.Length) policyArg = args[i + 1];
                if (args[i] == "-karen-combo" && i + 1 < args.Length) comboArg = args[i + 1];   // the combo learner, on or off (0.2.0)
                if (args[i] == "-kehai-no-records") NoRecords = true;
            }
        }

        // She is the player's size, and faster: hurrying she outpaces your walk (fasterThanPlayer)
        // and hunting she outsprints you (sprintsFasterThanPlayer), as the scene actually sets
        // them. In the open a hunt is lost — break her line of sight. On her rounds she walks slower.
        static void MatchPlayer(KarenConfig config)
        {
            PlayerMotor motor = Object.FindAnyObjectByType<PlayerMotor>();
            if (motor == null) return;
            MatchPlayer(config, motor.walkSpeed, motor.sprintSpeed);
            CharacterController body = motor.GetComponent<CharacterController>();
            if (body != null)
            {
                config.bodyHeight = Mathf.Max(1.5f, body.height);
                config.bodyRadius = Mathf.Clamp(body.radius, 0.25f, 0.6f);
            }
        }

        public static void MatchPlayer(KarenConfig config, float walkSpeed, float sprintSpeed)
        {
            config.hurrySpeed = walkSpeed * config.fasterThanPlayer;
            config.runSpeed = sprintSpeed * config.sprintsFasterThanPlayer;
            config.walkSpeed = Mathf.Min(config.walkSpeed, walkSpeed * 0.9f);
            config.sneakSpeed = Mathf.Min(config.sneakSpeed, walkSpeed * 0.6f);
        }

        static void ApplyCommandLine(KarenConfig config)
        {
            if (!string.IsNullOrEmpty(rungArg) && TryParseRung(rungArg, out KarenRung rung)) config.rung = rung;
            if (!string.IsNullOrEmpty(seedArg) && int.TryParse(seedArg, out int seed)) config.seed = seed;
            if (!string.IsNullOrEmpty(shieldArg)) config.shield = ParseSwitch(shieldArg, config.shield);
            if (!string.IsNullOrEmpty(policyArg)) config.policyModel = policyArg;
            if (!string.IsNullOrEmpty(comboArg)) config.comboContext = ParseSwitch(comboArg, config.comboContext);
            if (!string.IsNullOrEmpty(careerArg)) config.careerTable = ParseSwitch(careerArg, config.careerTable);
            if (!string.IsNullOrEmpty(brainArg) && TryParseBrain(brainArg, out KarenBrainKind brain)) config.brain = brain;
            if (!string.IsNullOrEmpty(mirrorArg) && TryParseMirror(mirrorArg, out MirrorMode mirror)) config.mirror = mirror;
            if (!string.IsNullOrEmpty(sensesArg)) config.senses = sensesArg.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(motionArg)) config.motion = motionArg.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(hunterArg)) config.hunter = ParseSwitch(hunterArg, config.hunter);
            if (!string.IsNullOrEmpty(learningArg)) config.learningRecords = ParseSwitch(learningArg, config.learningRecords);
            if (!string.IsNullOrEmpty(packArg)) config.modelPack = packArg;
            foreach (string refused in KarenConfigSet.ApplyAll(config, setArgs))
                Debug.LogError($"{GameNames.Antagonist}: -karen-set refused: {refused}");
            // Playtest builds keep learning records too (C1.1).
            if (Kehai.Playtest.PlaytestSession.Instance != null && string.IsNullOrEmpty(learningArg)) config.learningRecords = true;
        }

        // "planner", "basic", "vital" (any case), or "f" for rung F's old name: Basic.
        public static bool TryParseBrain(string text, out KarenBrainKind brain)
        {
            brain = KarenBrainKind.Planner;
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "planner": case "hand": return true;
                case "basic": case "f": brain = KarenBrainKind.Basic; return true;
                case "vital": brain = KarenBrainKind.Vital; return true;
                default: return false;
            }
        }

        public static bool TryParseMirror(string text, out MirrorMode mirror)
        {
            mirror = MirrorMode.K0;
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "k0": return true;
                case "k1": mirror = MirrorMode.K1; return true;
                case "k2": mirror = MirrorMode.K2; return true;
                default: return false;
            }
        }

        // The -karen-set assignments given on the command line, for the run's own record.
        public static System.Collections.Generic.IReadOnlyList<string> SetArguments => setArgs;

        // "on", "off", "true", "false", "1", "0".
        public static bool ParseSwitch(string text, bool fallback)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "on": case "true": case "1": case "yes": return true;
                case "off": case "false": case "0": case "no": return false;
                default: return fallback;
            }
        }

        // "C", "c", "C_BeliefGrid" and "2" all mean rung C.
        public static bool TryParseRung(string text, out KarenRung rung)
        {
            rung = KarenRung.E_Ledger;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            int last = (int)KarenRung.F_Policy;
            if (int.TryParse(text, out int index) && index >= 0 && index <= last) { rung = (KarenRung)index; return true; }
            char letter = char.ToUpperInvariant(text[0]);
            if (letter >= 'A' && letter <= 'A' + last) { rung = (KarenRung)(letter - 'A'); return true; }
            return false;
        }
    }
}
