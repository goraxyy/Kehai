using System;
using System.Collections.Generic;
using Kehai.Aiko;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kehai.Playtest
{
    // The playtest's screens, over everything else (PLAYTEST.md):
    //   first launch      the tester code, then whether to send sessions
    //   after shift 3     a suggestion to stop here (once per tester)
    //   finishing         four one-tap questions and one optional line (once per tester),
    //                     sending, thanks, and the link to the longer questions
    //   Shift+F7          a one-line note for the bug just marked
    // Built in code, in the main menu's style. The game is paused while one is open.
    public sealed class PlaytestScreens : MonoBehaviour
    {
        public static readonly (string id, string text, string[] options)[] Questions =
        {
            ("aiko", "Aiko felt…", new[] { "Scary", "Unfair", "Annoying", "I didn't notice her" }),
            ("knew", "Did you know what to do?", new[] { "Yes", "Mostly", "No" }),
            ("lost", "Did you get lost in the store?", new[] { "Never", "Sometimes", "Often" }),
            ("more", "Would you play more?", new[] { "Yes", "Maybe", "No" }),
        };
        public const string FreeQuestion = "Anything break or confuse you?";

        // Any playtest screen is up (the main menu stops answering underneath).
        public static bool Covering { get; private set; }

        public bool Showing => canvas != null && canvas.enabled;
        public string Page { get; private set; } = "";

        static readonly Color Crimson = new Color32(0xDC, 0x14, 0x3C, 0xFF);
        static readonly Color SoftBlack = new Color32(0x15, 0x15, 0x18, 0xFF);
        static readonly Color Paper = new Color(0.96f, 0.96f, 0.95f);
        static readonly Color Grey = new Color(0.66f, 0.66f, 0.71f);
        static readonly Color DimGrey = new Color(0.5f, 0.5f, 0.55f);

        const float Width = 900f, ControlsTop = -400f, Row = 66f;

        PlaytestSession session;
        Canvas canvas;
        RectTransform column;
        TextMeshProUGUI overline, title, body;
        readonly List<MenuButton> buttons = new List<MenuButton>();
        TMP_InputField input;
        TextMeshProUGUI inputError;
        EventSystem ownEvents;
        bool pausedByUs, quitAfter;
        int question;
        Dictionary<string, object> answers;
        float sendingSince;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Covering = false;

        void Awake()
        {
            session = GetComponent<PlaytestSession>();
            Build();
        }

        void OnDestroy() => Covering = false;

        // ---- when they open ---------------------------------------------------------------

        void Update()
        {
            Covering = Showing;
            if (!Showing)
            {
                if (!Ready) return;
                if (TesterState.Code.Length == 0) ShowCode();
                else if (!TesterState.ConsentAsked) ShowConsent();
                else if (ShouldSuggest()) ShowSuggest();
                else if (Input.GetKeyDown(KeyCode.F7) && Input.GetKey(KeyCode.LeftShift) && !MainMenu.Visible) ShowBugNote();
                return;
            }

            if (Page == "sending") UpdateSending();
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null && Input.anyKeyDown) SelectFirst();
        }

        // The store is up and nothing else has it (the eval harness, a replay).
        bool Ready =>
            Time.frameCount > 5 && session != null && session.Open && FindAnyObjectByType<ShiftManager>() != null &&
            Kehai.Replay.ReplayPlayer.Instance == null && Kehai.Eval.KehaiEnv.Instance == null;

        bool ShouldSuggest()
        {
            if (TesterState.Suggested || session.CareerShifts < session.Config.suggestFinishAfter) return false;
            ShiftManager shift = FindAnyObjectByType<ShiftManager>();
            if (shift == null || shift.IsShiftActive || MainMenu.Visible || FullScreenPanel.AnyOpen || Consequences.CareerOver) return false;
            return AikoBrain.Instance == null || AikoBrain.Instance.Review == null || !AikoBrain.Instance.Review.Visible;
        }

        // ---- the pages --------------------------------------------------------------------

        void ShowCode()
        {
            Open("tester code", $"PLAYTEST · {session.Config.round.ToUpperInvariant()}", "Welcome.",
                "Type the tester code you were given, like T07. It keeps your sessions together; your name never goes into the game.");
            input = MakeInput(ControlsTop, "T07", 16);
            input.onSubmit.AddListener(_ => SubmitCode());
            inputError = MainMenu.MakeText(column, "Error", GameFonts.Body, 24f, Crimson, new Vector2(0f, ControlsTop - 84f), new Vector2(Width, 34f));
            AddButton("Continue", ControlsTop - 140f, SubmitCode);
            Select(input.gameObject);
        }

        void SubmitCode()
        {
            string code = TesterCode.Normalize(input != null ? input.text : null);
            if (code == null)
            {
                inputError.text = "Letters, digits and dashes, 2 to 16 of them.";
                Select(input.gameObject);
                return;
            }
            session.SetCode(code);
            ShowConsent();
        }

        void ShowConsent()
        {
            Open("consent", "BEFORE YOU PLAY", "What gets sent",
                "This build records what happens in the game while you play: where you go, what you press, what " + GameNames.Antagonist +
                " does, and how smoothly it runs. Not your camera, microphone or screen. When you finish, it sends that to the developer, " +
                "who watches your session as a 3D replay to see what was confusing.\n\nYou can play either way.");
            AddButton("Send my sessions", ControlsTop, () => { session.SetConsent(true); Close(); });
            AddButton("Keep them on this computer", ControlsTop - Row, () => { session.SetConsent(false); Close(); });
            Link();
            SelectFirst();
        }

        void ShowSuggest()
        {
            TesterState.MarkSuggested();
            int n = session.CareerShifts;
            Open("suggest stopping", "PLAYTEST", $"That's {n} shifts. Thank you!",
                "You can stop here and answer four quick questions, or keep playing as long as you like.");
            AddButton("Finish and answer", ControlsTop, () => Finish(quitAfter: false));
            AddButton("Keep playing", ControlsTop - Row, Close);
            Link();
            SelectFirst();
        }

        // Questions (once per tester), then pack, send and thank. From the suggestion, or from quitting.
        public void Finish(bool quitAfter)
        {
            this.quitAfter = quitAfter;
            answers = new Dictionary<string, object>();
            if (TesterState.Answered) Send();
            else ShowQuestion(0);
        }

        void ShowQuestion(int index)
        {
            question = index;
            int total = Questions.Length + 1;
            if (index < Questions.Length)
            {
                (string id, string text, string[] options) q = Questions[index];
                Open("question " + q.id, $"QUESTION {index + 1} OF {total}", q.text, "");
                for (int i = 0; i < q.options.Length; i++)
                {
                    string option = q.options[i];
                    AddButton(option, ControlsTop + 160f - i * Row, () => { answers[q.id] = option; ShowQuestion(index + 1); });
                }
                AddButton("<color=#8A8A93>Skip</color>", ControlsTop + 160f - q.options.Length * Row - 16f, () => { answers[q.id] = "skipped"; ShowQuestion(index + 1); });
                Link();
                SelectFirst();
                return;
            }
            Open("question broke", $"QUESTION {total} OF {total}", FreeQuestion, "Optional. One line is plenty; Enter sends it.");
            input = MakeInput(ControlsTop, "Nothing, it was fine", 300);
            input.onSubmit.AddListener(_ => AnswerLast(input.text));
            AddButton("Send", ControlsTop - 100f, () => AnswerLast(input.text));
            AddButton("<color=#8A8A93>Skip</color>", ControlsTop - 100f - Row, () => AnswerLast(""));
            Select(input.gameObject);
        }

        void AnswerLast(string text)
        {
            answers["broke"] = (text ?? "").Trim();
            session.Answers(answers);
            Send();
        }

        void Send()
        {
            session.WrapUp(quitAfter ? "quit" : "finished");
            if (TesterState.Sends && session.Config.Uploads)
            {
                Open("sending", "PLAYTEST", "Sending your session…", "");
                sendingSince = Time.realtimeSinceStartup;
                AddButton("<color=#8A8A93>Stop, and send it next time</color>", ControlsTop - Row, () => { StopAllCoroutines(); PlaytestUploader.Abandon(); ShowThanks(sent: false); });
                Link();
                SelectFirst();
                StartCoroutine(PlaytestUploader.SendAll(session.Config, (sent, left) => ShowThanks(sent > 0 && left == 0)));
            }
            else ShowThanks(sent: false);
        }

        void UpdateSending()
        {
            body.text = $"{PlaytestUploader.Progress * 100f:0}%";
        }

        void ShowThanks(bool sent)
        {
            string code = TesterState.Code;
            string where = !TesterState.Sends ? "Your session is saved on this computer."
                : !session.Config.Uploads ? "Your session is saved on this computer; this build doesn't send."
                : sent ? "Your session is on its way."
                : "It couldn't be sent just now; the game will send it the next time you open it.";
            string form = session.Config.FormFor(code);
            Open("thanks", "PLAYTEST", $"Thank you, {code}.",
                where + (form != null ? "\n\nThe longer questions take about five minutes." : ""));
            float y = ControlsTop;
            if (form != null) { AddButton("Open the longer questions", y, () => Application.OpenURL(form)); y -= Row; }
            if (!quitAfter) { AddButton("Keep playing", y, KeepPlaying); y -= Row; }
            AddButton(Application.isEditor ? "Stop playing" : "Quit", y, () => session.QuitNow());
            Link();
            SelectFirst();
        }

        void KeepPlaying()
        {
            session.BeginSession(continuing: session.Stamp);
            Close();
        }

        void ShowBugNote()
        {
            Open("bug note", "BUG MARKED", "What went wrong?", "One line is plenty. Enter saves it; Esc skips.");
            input = MakeInput(ControlsTop + 120f, "", 300);
            input.onSubmit.AddListener(_ => SaveBug(input.text));
            AddButton("Save", ControlsTop + 20f, () => SaveBug(input.text));
            AddButton("<color=#8A8A93>Skip</color>", ControlsTop + 20f - Row, () => SaveBug(""));
            Select(input.gameObject);
        }

        void SaveBug(string note)
        {
            session.BugNote((note ?? "").Trim());
            Close();
        }

        // Esc: skip what can be skipped. Quitting can be called off before anything is packed.
        void Back()
        {
            if (Page == "bug note") SaveBug("");
            else if (Page == "suggest stopping") Close();
            else if (Page.StartsWith("question") && quitAfter && question == 0) { session.CancelQuit(); Close(); }
            else if (Page.StartsWith("question") && question < Questions.Length) { answers[Questions[question].id] = "skipped"; ShowQuestion(question + 1); }
        }

        // ---- opening and closing ----------------------------------------------------------

        void Open(string page, string over, string heading, string text)
        {
            if (!Showing)
            {
                FullScreenPanel.Set(this, true);   // before our canvas is on, so it isn't put away with the HUD
                if (!GamePause.Paused) { GamePause.Set(true); pausedByUs = true; }
                EnsureEventSystem();
                canvas.enabled = true;
            }
            Covering = true;
            Page = page;
            Clear();
            overline.text = over;
            title.text = heading;
            body.text = text;
        }

        void Close()
        {
            Clear();
            Page = "";
            canvas.enabled = false;
            Covering = false;
            FullScreenPanel.Set(this, false);
            if (pausedByUs && !MainMenu.Visible) GamePause.Set(false);
            pausedByUs = false;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void Clear()
        {
            buttons.Clear();
            input = null;
            inputError = null;
            for (int i = column.childCount - 1; i >= 0; i--)
            {
                Transform child = column.GetChild(i);
                if (child != overline.transform && child != title.transform && child != body.transform) Destroy(child.gameObject);
            }
        }

        void AddButton(string label, float y, Action onClick) =>
            buttons.Add(MenuButton.Make(column, label, new Vector2(0f, y), new Vector2(Width, Row - 8f), onClick));

        void Link()
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i - 1 + buttons.Count) % buttons.Count],
                    selectOnDown = buttons[(i + 1) % buttons.Count],
                };
        }

        void SelectFirst()
        {
            if (input != null) Select(input.gameObject);
            else if (buttons.Count > 0) Select(buttons[0].gameObject);
        }

        static void Select(GameObject go)
        {
            if (EventSystem.current != null && go != null) EventSystem.current.SetSelectedGameObject(go);
        }

        void EnsureEventSystem()
        {
            if (EventSystem.current != null && EventSystem.current.isActiveAndEnabled) return;
            if (ownEvents == null)
            {
                var go = new GameObject("Event system", typeof(EventSystem), typeof(StandaloneInputModule));
                go.transform.SetParent(transform, false);
                ownEvents = go.GetComponent<EventSystem>();
            }
            ownEvents.gameObject.SetActive(true);
        }

        // ---- building ---------------------------------------------------------------------

        void Build()
        {
            var root = new GameObject("Playtest screens", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100;   // over the main menu (1000)
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            root.AddComponent<GraphicRaycaster>();
            canvas.enabled = false;

            var shade = new GameObject("Shade", typeof(RectTransform)).AddComponent<Image>();
            shade.transform.SetParent(root.transform, false);
            Stretch(shade.rectTransform);
            shade.color = new Color(SoftBlack.r, SoftBlack.g, SoftBlack.b, 0.94f);
            shade.raycastTarget = true;   // nothing underneath can be clicked

            column = (RectTransform)new GameObject("Column", typeof(RectTransform)).transform;
            column.SetParent(root.transform, false);
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 0.5f);
            column.pivot = new Vector2(0.5f, 0.5f);
            column.sizeDelta = new Vector2(Width, 900f);

            overline = MainMenu.MakeText(column, "Overline", GameFonts.Body, 22f, Crimson, new Vector2(0f, -40f), new Vector2(Width, 34f));
            overline.characterSpacing = 6f;
            title = MainMenu.MakeText(column, "Title", GameFonts.Heading, 64f, Paper, new Vector2(-2f, -84f), new Vector2(Width, 90f));
            body = MainMenu.MakeText(column, "Body", GameFonts.Body, 28f, Grey, new Vector2(0f, -190f), new Vector2(Width, 200f));
        }

        TMP_InputField MakeInput(float y, string placeholder, int limit)
        {
            var rt = (RectTransform)new GameObject("Input", typeof(RectTransform)).transform;
            rt.SetParent(column, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(Width, 72f);
            rt.gameObject.SetActive(false);   // assembled before TMP_InputField wakes up
            var back = rt.gameObject.AddComponent<Image>();
            back.color = new Color(1f, 1f, 1f, 0.07f);

            var area = (RectTransform)new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).transform;
            area.SetParent(rt, false);
            Stretch(area);
            area.offsetMin = new Vector2(22f, 8f);
            area.offsetMax = new Vector2(-22f, -8f);

            TextMeshProUGUI hint = MainMenu.MakeText(area, "Placeholder", GameFonts.Body, 36f, DimGrey, Vector2.zero, Vector2.zero);
            TextMeshProUGUI text = MainMenu.MakeText(area, "Text", GameFonts.Body, 36f, Paper, Vector2.zero, Vector2.zero);
            foreach (TextMeshProUGUI t in new[] { hint, text })
            {
                Stretch(t.rectTransform);
                t.alignment = TextAlignmentOptions.Left;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.richText = false;
            }
            hint.text = placeholder;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = back;
            field.fontAsset = text.font;
            field.pointSize = 36f;
            field.characterLimit = limit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.caretWidth = 3;
            field.customCaretColor = true;
            field.caretColor = Crimson;
            field.selectionColor = new Color(Crimson.r, Crimson.g, Crimson.b, 0.35f);
            rt.gameObject.SetActive(true);
            return field;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
