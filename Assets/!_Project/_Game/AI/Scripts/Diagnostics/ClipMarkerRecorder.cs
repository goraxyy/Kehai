using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Listens to the shift for moments worth a clip and hands them to a ClipWatch, which
    // writes the markers into the shift recording. Only listens: nothing here changes what
    // anyone does. At clock-out the shift recorder merges them into moments and writes
    // <stem>.markers.json next to the shift record.
    //
    //   F7               mark this moment for a clip (always kept)
    //   Left Shift + F7  mark a bug at this moment (always kept, tagged "bug")
    public sealed class ClipMarkerRecorder : MonoBehaviour
    {
        public KeyCode markKey = KeyCode.F7;

        ShiftRecorder recorder;
        ShiftRecording target;
        ClipWatch watch;
        KarenBrain brain;
        PaSystem pa;
        bool dark;
        float lastLive = -1f;
        float tickUntil;
        string tickText;
        GUIStyle tickStyle;

        void Awake() => recorder = GetComponent<ShiftRecorder>();

        void OnEnable()
        {
            KarenNarrator.Said += OnStory;
            NoiseBus.Emitted += OnNoise;
            GameEvents.ShelfRestocked += OnRestocked;
            GameEvents.SpillCleaned += OnMopped;
            GameEvents.PunchAttempted += OnPunch;
            ShiftRecorder.Recorded += OnRecorded;
            ShiftRecorder.Finishing += OnFinishing;
        }

        void OnDisable()
        {
            KarenNarrator.Said -= OnStory;
            NoiseBus.Emitted -= OnNoise;
            GameEvents.ShelfRestocked -= OnRestocked;
            GameEvents.SpillCleaned -= OnMopped;
            GameEvents.PunchAttempted -= OnPunch;
            ShiftRecorder.Recorded -= OnRecorded;
            ShiftRecorder.Finishing -= OnFinishing;
            if (brain != null)
            {
                brain.Log.Written -= OnThought;
                brain.ShelfSabotaged -= OnSwept;
            }
            if (pa != null) pa.SpeechStarted -= OnPaSpeech;
            brain = null;
            pa = null;
        }

        // Karen and her PA come up with the store; take them as soon as they exist.
        void Hook()
        {
            if (brain == null && KarenBrain.Instance != null)
            {
                brain = KarenBrain.Instance;
                brain.Log.Written += OnThought;
                brain.ShelfSabotaged += OnSwept;
            }
            if (pa == null && KarenWorld.Instance != null && KarenWorld.Instance.Pa != null)
            {
                pa = KarenWorld.Instance.Pa;
                pa.SpeechStarted += OnPaSpeech;
            }
        }

        void Update()
        {
            if (recorder == null) return;
            Hook();

            if (recorder.Current != target)
            {
                target = recorder.Current;
                watch = target != null ? new ClipWatch(target.Markers, target.ChaseSpans) : null;
                lastLive = -1f;
                dark = false;
            }

            if (Input.GetKeyDown(markKey))
            {
                bool bug = Input.GetKey(KeyCode.LeftShift);
                if (watch != null)
                {
                    watch.Manual(Now, bug, recorder.Live.Player);
                    Tick(bug ? "● Bug marked" : "● Clip marked");
                }
                else Tick("Clock in first: nothing is being recorded");
            }

            if (watch == null) return;

            bool nowDark = !PowerSystem.PowerOn || (BreakerPanel.Instance != null && BreakerPanel.Instance.BlackoutStarted >= 0f);
            if (nowDark != dark)
            {
                dark = nowDark;
                watch.Lights(Now, !dark);
            }

            if (recorder.Live.T != lastLive)
            {
                lastLive = recorder.Live.T;
                watch.Frame(recorder.Live);
            }
        }

        float Now => recorder.ShiftTime;

        static Vector2 Flat(Vector3 p) => StoreFloorPlan.Flat(p);

        // ---- what it hears ------------------------------------------------------------------

        void OnStory(StoryLine line) => watch?.Story(Now, line.Kind, line.Text, Flat(line.At), line.HasPlace);
        void OnNoise(NoiseEvent n) => watch?.Noise(Now, n.Kind, n.Author, Flat(n.Position));
        void OnThought(ThoughtRecord r) => watch?.Record(Now, r.Kind, r.Chose, r.Text, Flat(r.BodyPosition));
        void OnRestocked(ShelfUnit unit, int filled) { if (unit != null) watch?.Restocked(Now, ShelfId(unit)); }
        void OnSwept(ShelfUnit unit) { if (unit != null) watch?.Swept(Now, ShelfId(unit), Flat(unit.transform.position)); }

        readonly System.Collections.Generic.Dictionary<ShelfUnit, int> shelfIds = new System.Collections.Generic.Dictionary<ShelfUnit, int>();

        int ShelfId(ShelfUnit unit)
        {
            if (!shelfIds.TryGetValue(unit, out int id)) shelfIds[unit] = id = shelfIds.Count + 1;
            return id;
        }
        void OnMopped(Dirt dirt) => watch?.Mopped(Now, dirt != null ? Flat(dirt.transform.position) : recorder.Live.Player);
        void OnPunch(bool accepted) { if (!accepted) watch?.PunchRefused(Now, recorder.Live.Player); }
        void OnPaSpeech(PaAnnouncement a) => watch?.Pa(Now, a.Text);

        void OnRecorded(ShiftEvent e)
        {
            if (e.Kind == "customer" && e.Who == "gave up at the till") watch?.CustomerGaveUp(Now, e.At);
        }

        void OnFinishing(ShiftRecording r)
        {
            if (watch != null && r == target) watch.Finish(r.Length, r.ClockedOut, recorder.Live.Player);
        }

        // ---- the one-second tick ----------------------------------------------------------

        void Tick(string text)
        {
            tickText = text;
            tickUntil = Time.unscaledTime + 1f;
        }

        void OnGUI()
        {
            if (Time.unscaledTime >= tickUntil) return;
            if (tickStyle == null)
                tickStyle = new GUIStyle(GUI.skin.box) { fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 45f, 14f, 36f)), alignment = TextAnchor.MiddleCenter, richText = false };
            var size = tickStyle.CalcSize(new GUIContent(tickText)) + new Vector2(24f, 12f);
            GUI.Box(new Rect(Screen.width - size.x - 16f, 16f, size.x, size.y), tickText, tickStyle);
        }
    }
}
