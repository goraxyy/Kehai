using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // What a sandbox shows you (0.5.2, Phase 11, C11.4), so you can tell whether she is seeing
    // the way you want. Everything here only reads: her eye's own numbers (awareness, the score
    // this frame, whether the eye line to you is clear) and each hiding place's coverage. None of
    // it changes what she knows, and it exists only inside a sandbox.
    //
    //   - her floor cone (KarenFloorCone, the F3 aid), switched on: grey, amber, red with her mood
    //   - a meter over her head: her awareness, and whether she sees you this frame
    //   - her eye line to your eyes: green when clear, red when blocked (only the eye line counts, 0.5.1)
    //   - on each hiding place: how much of you is inside, and HIDDEN at 80%
    //   - a readout in the corner: all of the above in numbers, her state, and the keys
    public sealed class SandboxIndicators : MonoBehaviour
    {
        public KarenBody body;
        // The first line of the readout (her state in this sandbox), any extra lines, and the keys.
        public System.Func<string> status = () => "";
        public System.Func<IEnumerable<string>> extra = () => System.Array.Empty<string>();
        public string keys = "";
        public readonly List<HidingPlace> places = new List<HidingPlace>();
        public readonly List<TextMesh> placeMeters = new List<TextMesh>();
        public string title = "Sandbox";
        public bool showReadout = true, showEyeLine = true, showMeters = true;

        TextMesh headMeter;
        LineRenderer eyeLine;
        GUIStyle style;

        public static SandboxIndicators Build(Transform parent, KarenBody body, string title)
        {
            var go = new GameObject("Sandbox indicators");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<SandboxIndicators>();
            view.body = body;
            view.title = title;
            KarenFx.SetEnabled(true);   // her floor cone
            return view;
        }

        public void Watch(HidingPlace place, Vector3 meterAt)
        {
            places.Add(place);
            TextMesh meter = SandboxButton.Label(transform, "", meterAt, 0.035f);
            meter.transform.rotation = Quaternion.identity;
            placeMeters.Add(meter);
        }

        void Start()
        {
            if (body == null) return;
            headMeter = SandboxButton.Label(body.transform, "", new Vector3(0f, (body.Head != null ? body.Head.localPosition.y : 1.85f) + 0.6f, 0f), 0.04f);
            var line = new GameObject("Her eye line");
            line.transform.SetParent(transform, false);
            eyeLine = line.AddComponent<LineRenderer>();
            eyeLine.positionCount = 2;
            eyeLine.widthMultiplier = 0.02f;
            eyeLine.useWorldSpace = true;
            eyeLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            eyeLine.receiveShadows = false;
            eyeLine.sharedMaterial = KarenProps.Emissive(Color.white, 1f);
        }

        // The numbers, each frame. Public and pure enough for the tests.
        public static string EyeReport(SightSensor eye)
        {
            string state = eye.Awareness >= eye.confirmAt ? "SURE" : eye.Awareness >= eye.seeAt ? "SEES YOU"
                : eye.Awareness >= eye.glimpseAt ? "NOTICING" : "nothing";
            return $"{state}  awareness {eye.Awareness:0.00}  now {eye.DetectNow:0.00}" + (eye.HiddenEntry.Active ? "  SAW YOU GO IN" : "");
        }

        public static string PlaceReport(HidingPlace place, PlayerPresence player)
        {
            if (place == null || player == null) return "";
            float coverage = place.Coverage(player);
            bool hidden = place.Conceals(player, coverage);
            string need = place.requiresCrouch && !player.IsCrouching && coverage > 0.05f ? "  (crouch)" : "";
            return hidden ? $"HIDDEN {coverage * 100f:0}%" : coverage > 0.01f ? $"{coverage * 100f:0}% inside{need}" : "";
        }

        void LateUpdate()
        {
            if (body == null) return;
            SightSensor eye = body.Sight;
            PlayerPresence player = PlayerPresence.Current;
            Camera view = Camera.main;

            if (headMeter != null)
            {
                headMeter.gameObject.SetActive(showMeters);
                headMeter.text = $"{eye.Awareness:0.00}" + (eye.SeesNow ? "  sees" : "");
                headMeter.color = eye.Awareness >= eye.confirmAt ? new Color(1f, 0.3f, 0.25f) : eye.Awareness >= eye.glimpseAt ? new Color(1f, 0.75f, 0.25f) : Color.white;
                if (view != null) headMeter.transform.rotation = Quaternion.LookRotation(headMeter.transform.position - view.transform.position);
            }
            if (eyeLine != null)
            {
                bool show = showEyeLine && player != null;
                eyeLine.enabled = show;
                if (show)
                {
                    eyeLine.SetPosition(0, eye.Eye);
                    eyeLine.SetPosition(1, player.Head);
                    // Exposure is her eye's own answer this frame: 1 when the line to your eyes is
                    // clear and you're in her cone, 0 when it's blocked, you're hidden or behind her.
                    Color c = eye.Exposure > 0f ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.25f);
                    eyeLine.startColor = eyeLine.endColor = c;
                }
            }
            for (int i = 0; i < places.Count; i++)
            {
                TextMesh meter = placeMeters[i];
                meter.gameObject.SetActive(showMeters);
                meter.text = PlaceReport(places[i], player);
                meter.color = meter.text.StartsWith("HIDDEN") ? new Color(0.3f, 1f, 0.4f) : Color.white;
                if (view != null) meter.transform.rotation = Quaternion.LookRotation(meter.transform.position - view.transform.position);
            }
        }

        void OnGUI()
        {
            if (!showReadout || body == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, wordWrap = true };
            SightSensor eye = body.Sight;
            PlayerPresence player = PlayerPresence.Current;
            var lines = new List<string>
            {
                $"{title}   {GameNames.Antagonist}: {status()}",
                "Her eye: " + EyeReport(eye),
                $"Eye line to you: {(eye.Exposure > 0f ? "clear" : "blocked, hidden or out of her cone")}",
            };
            if (player != null)
            {
                float distance = Vector3.Distance(eye.Eye, player.Chest);
                float angle = Vector3.Angle(eye.Forward, player.Chest - eye.Eye);
                lines.Add($"You: {distance:0.0} m away, {angle:0}° off her gaze, light {player.LightLevel:0.00}, {player.Motion.ToString().ToLowerInvariant()}");
                HidingPlace place = HidingPlace.At(player, out float coverage);
                if (place != null && coverage > 0.01f) lines.Add($"Hiding place: {(place.transform.parent != null ? place.transform.parent.name : place.name)}: {PlaceReport(place, player)}");
            }
            lines.AddRange(extra());
            PlayerInteract interact = PlayerInteract.Local;
            if (interact != null && interact.CurrentTarget != null) lines.Add("E: " + interact.CurrentTarget.GetPrompt());
            lines.Add(keys);
            GUI.Box(new Rect(12f, 12f, 640f, 26f + lines.Count * 21f), string.Join("\n", lines), style);
        }
    }
}
