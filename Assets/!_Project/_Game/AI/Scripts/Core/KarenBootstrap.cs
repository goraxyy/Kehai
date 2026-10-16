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
    //   -karen-policy <name>  rung F: the model under Resources/KarenPolicy/ to run
    //   -kehai-no-records     write no shift records, replays or clip markers (RL training runs
    //                         thousands of shifts)
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

        static string rungArg, seedArg, shieldArg, policyArg;

        static void ReadCommandLine()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nokaren") Disabled = true;
                if (args[i] == "-karen-rung" && i + 1 < args.Length) rungArg = args[i + 1];
                if (args[i] == "-karen-seed" && i + 1 < args.Length) seedArg = args[i + 1];
                if (args[i] == "-karen-shield" && i + 1 < args.Length) shieldArg = args[i + 1];
                if (args[i] == "-karen-policy" && i + 1 < args.Length) policyArg = args[i + 1];
                if (args[i] == "-kehai-no-records") NoRecords = true;
            }
        }

        // She is the player's size, and slightly faster: hurrying she outpaces your walk and
        // running your sprint (fasterThanPlayer), as the scene actually sets them. In the open
        // a chase is lost — break her line of sight. On her rounds she still walks slower.
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
            config.runSpeed = sprintSpeed * config.fasterThanPlayer;
            config.walkSpeed = Mathf.Min(config.walkSpeed, walkSpeed * 0.9f);
            config.sneakSpeed = Mathf.Min(config.sneakSpeed, walkSpeed * 0.6f);
        }

        static void ApplyCommandLine(KarenConfig config)
        {
            if (!string.IsNullOrEmpty(rungArg) && TryParseRung(rungArg, out KarenRung rung)) config.rung = rung;
            if (!string.IsNullOrEmpty(seedArg) && int.TryParse(seedArg, out int seed)) config.seed = seed;
            if (!string.IsNullOrEmpty(shieldArg)) config.shield = ParseSwitch(shieldArg, config.shield);
            if (!string.IsNullOrEmpty(policyArg)) config.policyModel = policyArg;
        }

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
