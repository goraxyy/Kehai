using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Kehai.Karen
{
    // Where a batch shift's wall time goes (0.4.0, build plan C0.2: the batch slowdown). Off unless
    // -kehai-timing is given; then each named part's time is summed, with the frames and the log
    // lines the shift wrote, and the eval prints it at the shift's end. Costs nothing when off.
    //
    //   using (ShiftTiming.Measure("believe")) { … }
    public static class ShiftTiming
    {
        public static bool Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-kehai-timing") >= 0;

        static readonly Dictionary<string, (double ms, long calls)> parts = new Dictionary<string, (double, long)>();
        static readonly Stopwatch wall = new Stopwatch();
        static long frames, logs, warnings;
        static readonly Dictionary<string, int> repeated = new Dictionary<string, int>();
        static bool listening;

        public readonly struct Scope : System.IDisposable
        {
            readonly string name;
            readonly long start;
            public Scope(string name) { this.name = name; start = Enabled ? Stopwatch.GetTimestamp() : 0; }
            public void Dispose()
            {
                if (!Enabled || name == null) return;
                double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                parts.TryGetValue(name, out var p);
                parts[name] = (p.ms + ms, p.calls + 1);
            }
        }

        public static Scope Measure(string name) => new Scope(Enabled ? name : null);

        // Unity's own frame markers (C0.2): where the rest of the frame goes. Each recorder gives the
        // last frame's time; summed per shift.
        static readonly string[] markers =
        {
            "PlayerLoop", "FixedUpdate.PhysicsFixedUpdate", "Physics.Simulate", "Update.ScriptRunBehaviourUpdate",
            "Update.ScriptRunDelayedDynamicFrameRate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate",
            "PreUpdate.AIUpdatePostScript", "PostLateUpdate.UpdateAllRenderers", "PostLateUpdate.FinishFrameRendering",
            "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.ParticleSystemBeginUpdateAll", "GC.Collect", "Gfx.WaitForPresentOnGfxThread"
        };
        static UnityEngine.Profiling.Recorder[] recorders;

        static void SampleMarkers()
        {
            if (recorders == null)
            {
                recorders = new UnityEngine.Profiling.Recorder[markers.Length];
                for (int i = 0; i < markers.Length; i++)
                {
                    recorders[i] = UnityEngine.Profiling.Recorder.Get(markers[i]);
                    if (recorders[i].isValid) recorders[i].enabled = true;
                }
                return;
            }
            for (int i = 0; i < markers.Length; i++)
            {
                var r = recorders[i];
                if (r == null || !r.isValid) continue;
                double ms = r.elapsedNanoseconds / 1e6;
                parts.TryGetValue("unity:" + markers[i], out var p);
                parts["unity:" + markers[i]] = (p.ms + ms, p.calls + r.sampleBlockCount);
            }
        }

        public static void Frame()
        {
            if (!Enabled) return;
            SampleMarkers();
            if (!listening)
            {
                listening = true;
                UnityEngine.Application.logMessageReceived += (text, stack, type) =>
                {
                    logs++;
                    if (type == UnityEngine.LogType.Warning) warnings++;
                    string key = text.Length > 60 ? text.Substring(0, 60) : text;
                    repeated[key] = repeated.TryGetValue(key, out int n) ? n + 1 : 1;
                };
            }
            if (!wall.IsRunning) wall.Start();
            frames++;
        }

        public static void Reset()
        {
            parts.Clear();
            repeated.Clear();
            frames = logs = warnings = 0;
            wall.Reset();
        }

        public static Dictionary<string, object> Summary()
        {
            var d = new Dictionary<string, object> { ["wall_ms"] = wall.Elapsed.TotalMilliseconds, ["frames"] = frames, ["logs"] = logs, ["warnings"] = warnings };
            foreach (var p in parts) d["ms_" + p.Key] = p.Value.ms;
            return d;
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            double total = wall.Elapsed.TotalMilliseconds;
            sb.Append($"timing: {frames} frames in {total / 1000.0:0.0} s wall ({(frames > 0 ? total / frames : 0):0.00} ms a frame), {logs} log lines ({warnings} warnings)");
            foreach (var p in parts.OrderByDescending(p => p.Value.ms))
                sb.Append($"\n  {p.Key,-14} {p.Value.ms,9:0} ms  {100.0 * p.Value.ms / System.Math.Max(1.0, total),5:0.0}%  {p.Value.calls} calls");
            foreach (var r in repeated.OrderByDescending(r => r.Value).Take(3))
                sb.Append($"\n  log ×{r.Value}: {r.Key}");
            return sb.ToString();
        }
    }
}
