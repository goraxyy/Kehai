#if UNITY_EDITOR
using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // What you chose on the mini store's panel (C11.17). Kept here, not on the panel, so a restart
    // (which reloads the store) keeps it.
    public static class MiniStoreSettings
    {
        public enum Tricks { None, OnlyOne, All }

        public static Tricks Allowed = Tricks.None;
        public static string Chosen = "blackout";
        // Her senses beyond her eyes (Sensorium): each on or off.
        public static bool Hearing = true, Building = true, Shoppers = true, Traces = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { }   // kept across the session's reloads on purpose

        public static void ApplyTo(KarenConfig config)
        {
            config.sandboxNoTricks = false;
            config.sandboxPacingOff = true;
            config.sandboxTrickIds = new HashSet<string>(TricksSandbox.Catalogue);
            config.sandboxTricks = Allowed == Tricks.All ? null
                : Allowed == Tricks.OnlyOne ? new HashSet<string> { Chosen } : new HashSet<string>();
        }

        public static void ApplySenses(KarenBrain brain)
        {
            Sensorium senses = brain != null ? brain.Sensorium : null;
            if (senses == null) return;
            senses.UseHearing = Hearing;
            senses.UseInfrastructure = Building;
            senses.UseTestimony = Shoppers;
            senses.UseTraces = Traces;
        }

        public static string Describe() => Allowed switch
        {
            Tricks.None => "no tricks: she only walks, looks, hunts and searches",
            Tricks.OnlyOne => "only " + Title(Chosen) + ", when it fits",
            _ => "any trick",
        };

        public static string Title(string id) => TacticLibrary.Get(id)?.Title ?? id;
    }

    // The mini store's panel (C11.17): Tab opens it and pauses the game, the mouse free; Tab again
    // closes it. Plain keys, no F keys: P starts or freezes her, Backspace restarts, [ and ] pick a
    // trick, Return fires it now.
    public sealed class MiniStorePanel
    {
        readonly TricksSandbox sandbox;
        Vector2 scroll;
        public bool Open { get; private set; }

        public MiniStorePanel(TricksSandbox sandbox) => this.sandbox = sandbox;

        public void SetOpen(bool open)
        {
            Open = open;
            GamePause.Set(open);
            if (eye != null) eye.enabled = open;
        }

        // Karen's view (the owner's ask, 25 November): a camera on her eye, drawn in the panel.
        Camera eye;
        RenderTexture view;
        const int ViewWidth = 400, ViewHeight = 225;

        void KarenView(KarenBrain brain)
        {
            Transform at = brain != null && brain.Body != null && brain.Body.Sight != null ? brain.Body.Sight.transform : null;
            if (at == null) return;
            if (eye == null)
            {
                view = new RenderTexture(ViewWidth, ViewHeight, 16);
                var go = new GameObject("Karen's view (sandbox panel)");
                eye = go.AddComponent<Camera>();
                if (Camera.main != null) eye.CopyFrom(Camera.main);
                eye.targetTexture = view;
                eye.fieldOfView = 70f;
                eye.nearClipPlane = 0.2f;
            }
            eye.transform.SetPositionAndRotation(at.position, at.rotation);
            eye.enabled = true;
            eye.Render();   // the game is paused while the panel is open: draw it now
            float w = Mathf.Min(Screen.width * 0.35f, ViewWidth), h = w * ViewHeight / ViewWidth;
            var r = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);
            GUI.Box(new Rect(r.x - 4f, r.y - 22f, w + 8f, h + 26f), "Karen's view");
            GUI.DrawTexture(r, view, ScaleMode.ScaleToFit);
        }

        public void Draw(KarenBrain brain, string feedback)
        {
            GUI.skin.label.wordWrap = true;
            if (!Open)
            {
                float w = Mathf.Min(520f, Screen.width - 16f);
                GUILayout.BeginArea(new Rect(8f, 8f, w, 120f));
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Mini store  ·  Karen " + State(brain) + "  ·  " + MiniStoreSettings.Describe());
                GUILayout.Label("Tab panel   P start or freeze   Backspace restart   [ ] pick a trick   Return fire it now");
                if (!string.IsNullOrEmpty(feedback)) GUILayout.Label(feedback);
                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            float width = Mathf.Min(560f, Screen.width - 16f);
            GUILayout.BeginArea(new Rect(8f, 8f, width, Screen.height - 16f));
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Mini store sandbox</b>  ·  paused while this is open (Tab closes it)", Rich());
            GUILayout.Label("Karen: " + State(brain) + (brain != null && brain.CurrentPlan != null ? "  ·  doing: " + brain.CurrentPlan.Name : ""));
            if (!string.IsNullOrEmpty(feedback)) GUILayout.Label(feedback);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start")) sandbox.StartRunning();
            if (GUILayout.Button("Freeze")) sandbox.FreezeNow();
            if (GUILayout.Button("Restart (fresh shift)")) { SetOpen(false); sandbox.RestartNow(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("<b>Tricks she may use</b>", Rich());
            int allowed = GUILayout.Toolbar((int)MiniStoreSettings.Allowed, new[] { "None", "Only one", "All" });
            if (allowed != (int)MiniStoreSettings.Allowed)
            {
                MiniStoreSettings.Allowed = (MiniStoreSettings.Tricks)allowed;
                if (brain != null) MiniStoreSettings.ApplyTo(brain.config);
            }
            GUILayout.Label(MiniStoreSettings.Describe());

            GUILayout.Label("Pick a trick (for \"Only one\" and for Fire now):");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(230f, Screen.height * 0.32f)));
            IReadOnlyList<string> all = TricksSandbox.Catalogue;
            for (int i = 0; i < all.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 3, all.Count); j++)
                {
                    bool on = all[j] == MiniStoreSettings.Chosen;
                    if (GUILayout.Toggle(on, MiniStoreSettings.Title(all[j]), GUI.skin.button, GUILayout.Width((width - 40f) / 3f)) && !on)
                        Choose(brain, all[j]);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Chosen: " + MiniStoreSettings.Title(MiniStoreSettings.Chosen) + "  ·  " + Readiness(brain, MiniStoreSettings.Chosen));
            if (GUILayout.Button("Fire now: " + MiniStoreSettings.Title(MiniStoreSettings.Chosen)))
            {
                SetOpen(false);
                sandbox.TryFire(MiniStoreSettings.Chosen);
            }

            GUILayout.Space(6f);
            GUILayout.Label("<b>What she can sense</b>  (her eyes are always on)", Rich());
            bool changed = false;
            changed |= Toggle(ref MiniStoreSettings.Hearing, "Hearing: your footsteps, sprinting, doors, things you drop");
            changed |= Toggle(ref MiniStoreSettings.Building, "Building sensors: cameras, door chime, till, breaker box");
            changed |= Toggle(ref MiniStoreSettings.Shoppers, "Shoppers' reports: what a shopper saw, when she asks");
            changed |= Toggle(ref MiniStoreSettings.Traces, "Traces: footprints, spills, your torch's light");
            if (changed) MiniStoreSettings.ApplySenses(brain);

            GUILayout.FlexibleSpace();
            GUILayout.Label("Keys: Tab panel · P start or freeze · Backspace restart · [ ] pick · Return fire now · E doors and things");
            GUILayout.EndVertical();
            GUILayout.EndArea();
            KarenView(brain);   // in the screen's bottom-right corner
        }

        public void Choose(KarenBrain brain, string id)
        {
            MiniStoreSettings.Chosen = id;
            if (brain != null) MiniStoreSettings.ApplyTo(brain.config);
        }

        static bool Toggle(ref bool value, string label)
        {
            bool now = GUILayout.Toggle(value, " " + label);
            if (now == value) return false;
            value = now;
            return true;
        }

        static string State(KarenBrain brain) =>
            brain == null ? "not here" : brain.KilledThisShift ? "CAUGHT you (Backspace restarts)" : brain.enabled ? "moving" : "frozen";

        // Whether she'd use it now, and if not, why: her own gate and the trick's own conditions.
        public static string Readiness(KarenBrain brain, string id)
        {
            Tactic t = TacticLibrary.Get(id);
            if (t == null || brain == null || brain.Ctx == null) return "waiting for her";
            if (!brain.ShiftActive) return "the shift hasn't started (Start)";
            if (!HtnPlanner.Gated(t, brain.Ctx, out string why)) return "she won't now: " + why;
            if (!t.Available(brain.Ctx, out why)) return "she can't now: " + (why ?? "not available");
            return "she can use it now";
        }

        static GUIStyle rich;
        static GUIStyle Rich() => rich ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
    }
}
#endif
