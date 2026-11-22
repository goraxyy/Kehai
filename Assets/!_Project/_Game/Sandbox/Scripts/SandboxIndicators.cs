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
    //   - her floor cone (KarenFloorCone, the developer aid: V on the F1 map), switched on: grey, amber, red with her mood
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
        Renderer sawLight;   // the "saw you go in" light: red over her head while her eye holds a witnessed entry
        readonly LineRenderer[] eyeLines = new LineRenderer[3];   // to your eyes, chest and feet
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

        // F4 (C11.4): every indicator off, or all back on. Her floor cone is V on the F1 map, as in the store.
        public void ToggleAll()
        {
            bool on = !(showReadout || showEyeLine || showMeters);
            showReadout = showEyeLine = showMeters = on;
            KarenFx.SetEnabled(on);   // her floor cone with them (the sight sandbox has no F1 map)
        }

        public bool AnyShown => showReadout || showEyeLine || showMeters;

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
            float top = (body.Head != null ? body.Head.localPosition.y : 1.85f) + 0.9f;
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = "Saw you go in";
            Object.Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(body.transform, false);
            lamp.transform.localPosition = new Vector3(0f, top, 0f);
            lamp.transform.localScale = Vector3.one * 0.16f;
            sawLight = lamp.GetComponent<Renderer>();
            sawLight.sharedMaterial = KarenProps.Emissive(new Color(1f, 0.15f, 0.1f), 3f);
            sawLight.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sawLight.enabled = false;
            string[] to = { "eyes", "chest", "feet" };
            for (int i = 0; i < eyeLines.Length; i++)
            {
                var line = new GameObject("Her sight line to your " + to[i]);
                line.transform.SetParent(transform, false);
                LineRenderer r = line.AddComponent<LineRenderer>();
                r.positionCount = 2;
                r.widthMultiplier = i == 0 ? 0.02f : 0.012f;
                r.useWorldSpace = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sharedMaterial = KarenProps.Emissive(Color.white, 1f);
                eyeLines[i] = r;
            }
        }

        // One of her three sight lines (her eye tests your eyes, chest and feet, SightSensor.ExposureTo):
        // green when nothing stands between and you're where she can see (in her cone, or within
        // arm's length), red when it's blocked or you're outside her cone.
        public static bool LineSees(SightSensor eye, Vector3 point)
        {
            Vector3 to = point - eye.Eye;
            bool where = to.magnitude <= SightSensor.CloseRange || Vector3.Angle(eye.Forward, to) <= eye.HalfAngle;
            return where && LineOfSight.Clear(eye.Eye, point);
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
            if (sawLight != null) sawLight.enabled = showMeters && eye.HiddenEntry.Active;
            for (int i = 0; i < eyeLines.Length; i++)
            {
                LineRenderer line = eyeLines[i];
                if (line == null) continue;
                bool show = showEyeLine && player != null;
                line.enabled = show;
                if (!show) continue;
                Vector3 point = i == 0 ? player.Head : i == 1 ? player.Chest : player.Feet;
                line.SetPosition(0, eye.Eye);
                line.SetPosition(1, point);
                Color c = LineSees(eye, point) ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.25f, 0.25f);
                line.startColor = line.endColor = c;
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
