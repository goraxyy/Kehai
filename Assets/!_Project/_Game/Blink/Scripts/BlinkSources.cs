using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Kehai.Blink
{
    // The clock blinks are timed on. A real face blinks in wall-clock time; a synthetic or
    // replayed one in a fixed-step eval must blink in simulation time, or a run that goes
    // five times faster than real time would hand Karen blinks five times as long.
    public static class BlinkClock
    {
        public static bool Simulated;
        public static double Now => Simulated ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Simulated = false;
    }

    // One reading of the player's eyes.
    public struct BlinkSample
    {
        public float Closed;        // raw 0 = open, 1 = shut (before calibration)
        public float Confidence;    // 0..1 — how much the source trusts this reading
        public double Captured;     // when the camera took the frame (BlinkClock.Now clock)
        public string Source;
    }

    // IDEAS.md "Architecture": everything that can tell whether the eyes are shut.
    //   Keyboard — dev and accessibility fallback
    //   Replay   — recorded or synthetic traces, so Karen's blink behaviour can be tested
    //              without sitting in front of a camera blinking on cue
    //   Udp      — the webcam path, via the Python sidecar in tools/blink
    //   Sentis   — the webcam path in-engine, once a trained eye model is in the project
    public interface IBlinkSource : IDisposable
    {
        string Name { get; }
        bool IsLive { get; }                 // producing fresh samples right now
        bool TryRead(out BlinkSample sample);
        float MeasuredLatencyMs { get; }     // capture → available here; 0 if unknown
    }

    // ---- keyboard -------------------------------------------------------------------------

    // Hold the key to keep your eyes shut; tap it to blink. The first source to build, since
    // it proves the whole chain including Karen's reactions, and the one that makes the
    // mechanic playable without a camera.
    public sealed class KeyboardBlinkSource : IBlinkSource
    {
        readonly KeyCode key;
        float tapUntil = -1f;
        bool held;

        public KeyboardBlinkSource(KeyCode key) => this.key = key;

        public string Name => $"keyboard ({key})";
        public bool IsLive => true;
        public float MeasuredLatencyMs => 0f;

        public bool TryRead(out BlinkSample sample)
        {
            if (Input.GetKeyDown(key)) tapUntil = Time.unscaledTime + 0.16f;
            held = Input.GetKey(key);
            float closed = held || Time.unscaledTime < tapUntil ? 1f : 0f;
            sample = new BlinkSample { Closed = closed, Confidence = 1f, Captured = BlinkClock.Now, Source = Name };
            return true;
        }

        public void Dispose() { }
    }

    // ---- replay -----------------------------------------------------------------------------

    // Plays back a recorded trace (CSV "t,closed" or the sidecar's JSONL), or generates a
    // synthetic one: blinks at a given rate with the ~300 ms human shape. The simulated
    // players in the ablation runs use the synthetic mode for rung F.
    public sealed class ReplayBlinkSource : IBlinkSource
    {
        readonly List<(double t, float closed)> trace;
        readonly bool loop;
        readonly double duration;
        readonly double started;

        // Synthetic generator state.
        readonly System.Random random;
        readonly float blinksPerMinute;
        double nextBlink = -1;
        double blinkStart = -1;
        double blinkLength;

        public string Name { get; }
        public bool IsLive => true;
        public float MeasuredLatencyMs => 0f;

        public static ReplayBlinkSource FromFile(string path, bool loop = true)
        {
            var points = new List<(double, float)>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("t,")) continue;
                if (line.StartsWith("{"))
                {
                    var o = MiniJson.ParseObject(line);
                    points.Add((o.GetNumber("t"), (float)o.GetNumber("closed")));
                }
                else
                {
                    string[] parts = line.Split(',');
                    if (parts.Length < 2) continue;
                    points.Add((double.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture)));
                }
            }
            return new ReplayBlinkSource(points, loop, "replay " + Path.GetFileName(path));
        }

        public static ReplayBlinkSource Synthetic(float blinksPerMinute, int seed) =>
            new ReplayBlinkSource(blinksPerMinute, seed);

        ReplayBlinkSource(List<(double, float)> points, bool loop, string name)
        {
            trace = points;
            trace.Sort((a, b) => a.t.CompareTo(b.t));
            this.loop = loop;
            duration = trace.Count > 0 ? trace[trace.Count - 1].t - trace[0].t : 0;
            started = BlinkClock.Now;
            Name = name;
        }

        ReplayBlinkSource(float blinksPerMinute, int seed)
        {
            random = new System.Random(seed);
            this.blinksPerMinute = Mathf.Max(1f, blinksPerMinute);
            started = BlinkClock.Now;
            Name = $"synthetic ({blinksPerMinute:0}/min)";
        }

        public bool TryRead(out BlinkSample sample)
        {
            double now = BlinkClock.Now;
            sample = new BlinkSample { Confidence = 1f, Captured = now, Source = Name };

            if (random != null)
            {
                sample.Closed = Synthetic(now - started);
                return true;
            }

            if (trace.Count == 0) return false;
            double t = now - started;
            if (loop && duration > 0) t %= duration;
            t += trace[0].t;
            int i = trace.BinarySearch((t, 0f), Comparer<(double t, float closed)>.Create((a, b) => a.t.CompareTo(b.t)));
            if (i < 0) i = Mathf.Max(0, ~i - 1);
            sample.Closed = trace[Mathf.Min(i, trace.Count - 1)].closed;
            return true;
        }

        // A blink: ~100 ms closing, ~200 ms opening (Stern et al.'s asymmetric shape).
        float Synthetic(double t)
        {
            if (nextBlink < 0) nextBlink = t + Exponential();
            if (blinkStart < 0 && t >= nextBlink)
            {
                blinkStart = t;
                blinkLength = 0.22 + random.NextDouble() * 0.16;
            }
            if (blinkStart < 0) return 0f;

            double local = t - blinkStart;
            if (local > blinkLength)
            {
                blinkStart = -1;
                nextBlink = t + Exponential();
                return 0f;
            }
            double close = blinkLength * 0.33;
            return local < close ? (float)(local / close) : (float)(1.0 - (local - close) / (blinkLength - close));
        }

        double Exponential() => -Math.Log(1.0 - random.NextDouble()) * 60.0 / blinksPerMinute;

        public void Dispose() { }
    }

    // ---- webcam, via the sidecar --------------------------------------------------------------

    // Listens for the Python sidecar (tools/blink/blink_server.py), which owns the camera and
    // the model: MediaPipe's blink blendshapes, eye-aspect-ratio, or your own trained CNN.
    // Each packet is one JSON line:
    //
    //   {"seq": 1234, "closed": 0.93, "conf": 0.88, "src": "cnn",
    //    "capture": 1727000000.1234, "sent": 1727000000.1391, "fps": 30.0}
    //
    // capture/sent are the sidecar's wall clock; the gap between them is the pipeline's own
    // latency, which is reported back so the tracker can place a blink's true start.
    // Local only: binds to 127.0.0.1, never records, never sends anything anywhere.
    public sealed class UdpBlinkSource : IBlinkSource
    {
        readonly UdpClient client;
        readonly Thread thread;
        readonly object gate = new object();
        volatile bool running = true;
        BlinkSample latest;
        bool fresh;
        long lastPacketTicks;   // Stopwatch ticks, written by the receive thread
        float pipelineMs;
        string src = "webcam";

        public int Port { get; }
        public int Packets { get; private set; }
        public float SidecarFps { get; private set; }
        public string Src => src;
        // The Mac helper also sends the eye height it measured and your usual one.
        public float Ratio { get; private set; }
        public float OpenRatio { get; private set; }
        public string Name => $"webcam ({src})";
        // Live means packets are arriving, whether or not anything has read them yet — the
        // tracker only switches to the webcam once it's live, so this can't wait for a read.
        public bool IsLive
        {
            get
            {
                long last = Interlocked.Read(ref lastPacketTicks);
                return last > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - last) < System.Diagnostics.Stopwatch.Frequency;
            }
        }
        public float MeasuredLatencyMs => pipelineMs;

        public UdpBlinkSource(int port)
        {
            Port = port;
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            client.Client.ReceiveTimeout = 500;
            thread = new Thread(Receive) { IsBackground = true, Name = "KarenBlinkUdp" };
            thread.Start();
        }

        void Receive()
        {
            var from = new IPEndPoint(IPAddress.Loopback, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref from);
                    var o = MiniJson.ParseObject(System.Text.Encoding.UTF8.GetString(data));
                    if (o == null) continue;

                    double capture = o.GetNumber("capture");
                    double sent = o.GetNumber("sent");
                    float ms = capture > 0 && sent >= capture ? (float)((sent - capture) * 1000.0) : 0f;

                    lock (gate)
                    {
                        latest = new BlinkSample
                        {
                            Closed = Mathf.Clamp01((float)o.GetNumber("closed")),
                            Confidence = Mathf.Clamp01((float)o.GetNumber("conf", 1)),
                            Source = o.GetString("src", "webcam")
                        };
                        src = latest.Source;
                        pipelineMs = Mathf.Lerp(pipelineMs <= 0 ? ms : pipelineMs, ms, 0.1f);
                        SidecarFps = (float)o.GetNumber("fps", SidecarFps);
                        Ratio = (float)o.GetNumber("ratio", 0);
                        OpenRatio = (float)o.GetNumber("open", 0);
                        fresh = true;
                        Packets++;
                    }
                    Interlocked.Exchange(ref lastPacketTicks, System.Diagnostics.Stopwatch.GetTimestamp());
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
                catch (Exception e) { Debug.LogWarning("Blink sidecar packet ignored: " + e.Message); }
            }
        }

        public bool TryRead(out BlinkSample sample)
        {
            lock (gate)
            {
                sample = latest;
                if (!fresh) return false;
                fresh = false;
            }
            // The frame was taken pipelineMs before the sidecar sent it.
            sample.Captured = BlinkClock.Now - pipelineMs / 1000.0;
            return true;
        }

        public void Dispose()
        {
            running = false;
            try { client.Close(); } catch { }
        }
    }
}
