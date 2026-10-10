using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Kehai.Replay
{
    // Renders one shot of a recorded shift, unattended, then quits. Started by
    // tools/marketing/render_shot.sh (which checks the editor is closed and takes the heavy-job
    // lock) through ReplayRenderBatch:
    //
    //   -krec <file.krec>                    the recording
    //   -moment <rank> | -from <s> -to <s>   which part: a clip moment from <stem>.markers.json (default 1)
    //   -shot <preset | path.json | path>    pov, cctv, chase (default), orbit, topdown, or a keyframed path
    //   -subject karen | you                  who the presets follow (default karen)
    //   -layers <list>                       her mind over the store: belief,guess,cone,sound,thoughts,actors | all | none
    //   -alpha                               her mind alone, on a transparent background (.webm / .mov / PNG)
    //   -dof                                 depth of field on the subject
    //   -size <w>x<h>  -fps <n>              default 1920x1080 at 60
    //   -out <file.mp4 | .webm | .mov | folder/>
    //   -dry-run                             check everything, say what would be rendered, render nothing
    //
    // Every frame is a fixed step of the recording (1/fps), rendered off screen, encoded as PNG
    // on worker threads and piped to ffmpeg in order (or written into a folder). PNG rather
    // than raw pixels because the ffmpeg that ships with the editor (Remotion's) reads PNGs from
    // a pipe but not raw video. The sound is mixed from the
    // recording's events as heard at the camera, written as a WAV beside the video and muxed
    // in. A <out>.json beside it says what was rendered, for the editor (Phase 5), and where
    // Karen and you are in the picture through it (ShotTrack, Phase 6).
    public sealed class ReplayRender
    {
        public const int WarmUpFrames = 8;

        public sealed class Settings
        {
            public string Krec, Out, Shot = "chase", Subject = "karen";
            public float From, To;
            public int Moment;
            public MindLayer Layers;
            public bool Alpha, Dof, DryRun;
            public int Width = 1920, Height = 1080, Fps = 60;
        }

        enum Output { Frames, Mp4, Webm, Mov }

        readonly ReplayPlayer player;
        readonly Settings s;
        readonly Output output;
        readonly string video, audio, sidecar, ffmpeg;
        readonly List<Pose> ears = new List<Pose>();
        readonly ShotTrack track = new ShotTrack();
        RenderTexture target, resolved;
        Texture2D readback;
        readonly ConcurrentBag<byte[]> buffers = new ConcurrentBag<byte[]>();
        readonly Queue<(int index, Task<byte[]> png)> encoding = new Queue<(int, Task<byte[]>)>();
        int maxEncoding;
        Camera sceneCam, mindCam;
        Process encoder;
        Stream pipe;
        readonly Queue<string> encoderLog = new Queue<string>();
        int frame = -WarmUpFrames, frames;
        float wallStart, nextReport;

        // ---- setting up ------------------------------------------------------------------------

        public static ReplayRender FromCommandLine(ReplayPlayer player)
        {
            Settings s;
            ReplayRender r;
            try
            {
                s = Parse(player);
                r = new ReplayRender(player, s);
            }
            catch (System.Exception e)
            {
                Debug.LogError("ReplayRender: " + e.Message);
                Quit(1);
                return null;
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogError("ReplayRender: there's no graphics device. Renders need one: run Unity without -nographics.");
                Quit(3);
                return null;
            }

            if (r.output != Output.Frames && r.ffmpeg == null)
            {
                Debug.LogError("ReplayRender: ffmpeg isn't installed (brew install ffmpeg), so only PNG frames can be rendered: give -out a folder ending in /.");
                Quit(3);
                return null;
            }
            Debug.Log($"ReplayRender: {Path.GetFileName(s.Krec)} {s.From:0.00}–{s.To:0.00} s ({r.frames} frames at {s.Fps} fps), shot {s.Shot}, " +
                      $"subject {s.Subject}, her mind {MindLayers.Describe(s.Layers)}{(s.Alpha ? " alone (alpha)" : "")}{(s.Dof ? ", depth of field" : "")}, " +
                      $"{s.Width}x{s.Height} → {s.Out}{(r.ffmpeg != null ? " via " + r.ffmpeg : "")}");
            if (s.DryRun)
            {
                Debug.Log("ReplayRender: dry run, nothing rendered.");
                Quit(0);
                return null;
            }
            try
            {
                r.Begin();
            }
            catch (System.Exception e)
            {
                Debug.LogError("ReplayRender: couldn't start: " + e);
                r.Abort();
                Quit(2);
                return null;
            }
            return r;
        }

        static Settings Parse(ReplayPlayer player)
        {
            var s = new Settings { Krec = player.File, DryRun = ReplayMode.Flag("-dry-run") };
            ReplayStage stage = player.Stage;

            string moment = ReplayMode.Arg("-moment"), from = ReplayMode.Arg("-from"), to = ReplayMode.Arg("-to");
            if (from != null || to != null)
            {
                s.From = from != null ? Number(from, "-from") : stage.Start;
                s.To = to != null ? Number(to, "-to") : stage.End;
            }
            else
            {
                s.Moment = moment != null ? (int)Number(moment, "-moment") : 1;
                ReplayPlayer.Moment? found = null;
                foreach (ReplayPlayer.Moment m in player.Moments) if (m.Rank == s.Moment) found = m;
                if (found == null)
                    throw new System.ArgumentException($"no clip moment {s.Moment} in this shift's markers ({player.Moments.Count} moments); give -from and -to instead");
                s.From = found.Value.Start;
                s.To = found.Value.End;
            }
            s.From = Mathf.Max(s.From, stage.Start);
            s.To = Mathf.Min(s.To, stage.End);
            if (s.To - s.From < 0.1f) throw new System.ArgumentException($"nothing to render between {s.From:0.00} and {s.To:0.00} s (the recording runs {stage.Start:0.0}–{stage.End:0.0} s)");

            s.Shot = ReplayMode.Arg("-shot") ?? "chase";
            if (s.Shot == "path" || s.Shot.EndsWith(".json"))
            {
                string file = s.Shot == "path" ? ShotPath.PathFor(s.Krec) : s.Shot;
                if (!File.Exists(file)) throw new System.ArgumentException($"-shot {s.Shot}: no camera path at {file} (K in the replay saves one)");
                if (ShotPath.Load(file).Keys.Count == 0) throw new System.ArgumentException($"-shot {s.Shot}: the camera path has no keyframes");
            }
            else if (!ReplayCameras.TryParse(s.Shot, out ShotPreset preset) || preset == ShotPreset.Free)
                throw new System.ArgumentException($"-shot {s.Shot}: pov, cctv, chase, orbit, topdown, path, or a camera path .json");
            s.Subject = (ReplayMode.Arg("-subject") ?? "karen").ToLowerInvariant();
            if (s.Subject != "karen" && s.Subject != "you" && s.Subject != "player") throw new System.ArgumentException("-subject is karen or you");
            s.Layers = MindLayers.Parse(ReplayMode.Arg("-layers"));
            s.Alpha = ReplayMode.Flag("-alpha");
            if (s.Alpha && s.Layers == MindLayer.None) s.Layers = MindLayer.All;
            s.Dof = ReplayMode.Flag("-dof");

            string size = ReplayMode.Arg("-size");
            if (size != null)
            {
                string[] wh = size.ToLowerInvariant().Split('x');
                if (wh.Length != 2 || !int.TryParse(wh[0], out s.Width) || !int.TryParse(wh[1], out s.Height) || s.Width < 16 || s.Height < 16 || s.Width > 4096 || s.Height > 4096)
                    throw new System.ArgumentException("-size is <width>x<height>, e.g. 1080x1920");
                s.Width &= ~1;    // even, for yuv420p
                s.Height &= ~1;
            }
            string fps = ReplayMode.Arg("-fps");
            if (fps != null && (!int.TryParse(fps, out s.Fps) || s.Fps < 1 || s.Fps > 120)) throw new System.ArgumentException("-fps is 1–120");

            s.Out = ReplayMode.Arg("-out");
            if (string.IsNullOrEmpty(s.Out)) throw new System.ArgumentException("-out is needed: a .mp4, .webm or .mov file, or a folder/ for PNG frames");
            return s;
        }

        static float Number(string text, string what)
        {
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                throw new System.ArgumentException($"{what} isn't a number: {text}");
            return v;
        }

        ReplayRender(ReplayPlayer player, Settings s)
        {
            this.player = player;
            this.s = s;
            frames = Mathf.FloorToInt((s.To - s.From) * s.Fps + 1e-3f) + 1;

            string ext = Path.GetExtension(s.Out).ToLowerInvariant();
            bool folder = s.Out.EndsWith("/") || s.Out.EndsWith("\\") || Directory.Exists(s.Out) || ext.Length == 0;
            output = folder ? Output.Frames : ext == ".webm" ? Output.Webm : ext == ".mov" ? Output.Mov : Output.Mp4;
            if (output == Output.Mp4 && ext != ".mp4") throw new System.ArgumentException($"-out {s.Out}: a video is .mp4, .webm or .mov");
            if (output == Output.Mp4 && s.Alpha) throw new System.ArgumentException("-alpha needs .webm, .mov or a folder of PNGs: .mp4 has no transparency");

            if (folder)
            {
                video = s.Out.TrimEnd('/', '\\');
                audio = s.Alpha ? null : Path.Combine(video, "audio.wav");
                sidecar = Path.Combine(video, "shot.json");
            }
            else
            {
                string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(s.Out)) ?? "", Path.GetFileNameWithoutExtension(s.Out));
                video = Path.GetFullPath(s.Out);
                audio = s.Alpha ? null : stem + ".wav";
                sidecar = stem + ".json";
                ffmpeg = FindFfmpeg();
            }
        }

        // Unity's environment doesn't carry the shell's PATH, so Homebrew's places are tried first.
        public static string FindFfmpeg()
        {
            var places = new List<string>();
            string env = System.Environment.GetEnvironmentVariable("KEHAI_FFMPEG");
            if (!string.IsNullOrEmpty(env)) places.Add(env);
            places.Add("/opt/homebrew/bin/ffmpeg");
            places.Add("/usr/local/bin/ffmpeg");
            foreach (string dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                if (dir.Length > 0) places.Add(Path.Combine(dir, "ffmpeg"));
            // The one that comes with the editor's Remotion (tools/marketing/editor, npm install).
            string remotion = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "tools", "marketing", "editor", "node_modules", "@remotion");
            if (Directory.Exists(remotion))
                foreach (string dir in Directory.GetDirectories(remotion, "compositor-*")) places.Add(Path.Combine(dir, "ffmpeg"));
            foreach (string p in places) if (File.Exists(p)) return p;
            return null;
        }

        void Begin()
        {
            wallStart = Time.realtimeSinceStartup;
            Time.captureFramerate = s.Fps;

            ReplayCameras cams = player.Cameras;
            if (s.Shot.EndsWith(".json") || s.Shot == "path")
            {
                cams.Path = ShotPath.Load(s.Shot == "path" ? ShotPath.PathFor(s.Krec) : s.Shot);
                cams.Use(ShotPreset.Path);
            }
            else
            {
                ReplayCameras.TryParse(s.Shot, out ShotPreset preset);
                cams.Use(preset);
            }
            cams.Subject = s.Subject == "karen" ? KrecKind.Karen : KrecKind.Player;
            cams.DepthOfFieldOn = s.Dof;
            player.Mind.Shown = s.Layers;

            sceneCam = player.Stage.Camera;
            sceneCam.enabled = false;
            target = new RenderTexture(s.Width, s.Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = s.Alpha ? 1 : 4, name = "Replay frame" };
            target.Create();
            resolved = new RenderTexture(s.Width, s.Height, 0, RenderTextureFormat.ARGB32) { name = "Replay frame (resolved)" };
            resolved.Create();
            readback = new Texture2D(s.Width, s.Height, TextureFormat.RGBA32, false);
            maxEncoding = Mathf.Clamp(System.Environment.ProcessorCount - 2, 2, 6);
            // The target stays on the camera, so everything placed by the picture's shape (her
            // thought log, the eyelids) sees the frame's aspect, not the screen's.
            sceneCam.targetTexture = target;

            if (s.Alpha)
            {
                mindCam = new GameObject("Mind camera").AddComponent<Camera>();
                mindCam.enabled = false;
                mindCam.clearFlags = CameraClearFlags.SolidColor;
                mindCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                mindCam.cullingMask = 1 << ReplayLook.MindLayer;
                mindCam.targetTexture = target;
                mindCam.allowHDR = false;
                mindCam.allowMSAA = false;
                UniversalAdditionalCameraData data = mindCam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
            }

            if (output == Output.Frames) Directory.CreateDirectory(video);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(video) ?? ".");
                StartEncoder();
            }
        }

        void StartEncoder()
        {
            string codec;
            switch (output)
            {
                case Output.Webm:
                    codec = s.Alpha ? "-c:v libvpx-vp9 -pix_fmt yuva420p -auto-alt-ref 0 -b:v 0 -crf 30 -row-mt 1 -deadline good -cpu-used 4"
                                    : "-c:v libvpx-vp9 -pix_fmt yuv420p -b:v 0 -crf 28 -row-mt 1 -deadline good -cpu-used 4";
                    break;
                case Output.Mov:
                    codec = s.Alpha ? "-c:v prores_ks -profile:v 4444 -pix_fmt yuva444p10le" : "-c:v prores_ks -profile:v 3 -pix_fmt yuv422p10le";
                    break;
                default:
                    codec = "-c:v libx264 -preset medium -crf 17 -pix_fmt yuv420p -movflags +faststart";
                    break;
            }
            string args = $"-y -hide_banner -loglevel error -f image2pipe -framerate {s.Fps} -c:v png -i - {codec} \"{VideoOnly}\"";
            encoder = Run(ffmpeg, args, stdin: true);
            pipe = encoder.StandardInput.BaseStream;
        }

        string VideoOnly => audio == null ? video : video + ".video" + Path.GetExtension(video);

        Process Run(string exe, string args, bool stdin)
        {
            // From its own folder: the ffmpeg that comes with Remotion finds its libraries there
            // (every path it's given is absolute).
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, RedirectStandardInput = stdin, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            };
            var p = new Process { StartInfo = psi };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (encoderLog) { encoderLog.Enqueue(e.Data); if (encoderLog.Count > 40) encoderLog.Dequeue(); } };
            p.OutputDataReceived += (_, e) => { };
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            return p;
        }

        // ---- each frame ------------------------------------------------------------------------

        public void Step()
        {
            try
            {
                if (frame >= frames)
                {
                    Finish();
                    return;
                }
                float t = s.From + Mathf.Max(0, frame) / (float)s.Fps;
                player.ApplyAt(t, 1f / s.Fps, false);

                Camera cam = sceneCam;
                if (mindCam != null)
                {
                    mindCam.transform.SetPositionAndRotation(sceneCam.transform.position, sceneCam.transform.rotation);
                    mindCam.fieldOfView = sceneCam.fieldOfView;
                    mindCam.nearClipPlane = sceneCam.nearClipPlane;
                    mindCam.farClipPlane = sceneCam.farClipPlane;
                    cam = mindCam;
                }
                cam.Render();

                if (frame >= 0)
                {
                    ears.Add(new Pose(sceneCam.transform.position, sceneCam.transform.rotation));
                    track.SampleIfDue(frame, s.Fps, sceneCam, player.Data, player.Stage, t);
                    Graphics.Blit(target, resolved);
                    RenderTexture was = RenderTexture.active;
                    RenderTexture.active = resolved;
                    readback.ReadPixels(new Rect(0, 0, s.Width, s.Height), 0, 0, false);
                    RenderTexture.active = was;
                    Write(frame);
                }
                frame++;

                if (Time.realtimeSinceStartup > nextReport)
                {
                    nextReport = Time.realtimeSinceStartup + 10f;
                    Debug.Log($"ReplayRender: frame {Mathf.Max(0, frame)}/{frames} ({100f * Mathf.Max(0, frame) / frames:0}%)");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"ReplayRender failed at frame {frame}: {e}\n{EncoderLog()}");
                Abort();
                Quit(2);
            }
        }

        // The frame goes to a worker to be encoded; finished frames leave in order, a few frames
        // behind the renderer, so the encoding runs alongside the next frames' rendering.
        void Write(int index)
        {
            Unity.Collections.NativeArray<byte> pixels = readback.GetRawTextureData<byte>();
            if (!buffers.TryTake(out byte[] raw) || raw.Length != pixels.Length) raw = new byte[pixels.Length];
            pixels.CopyTo(raw);
            bool alpha = s.Alpha;
            uint w = (uint)s.Width, h = (uint)s.Height;
            encoding.Enqueue((index, Task.Run(() =>
            {
                if (alpha) Unpremultiply(raw);
                byte[] png = ImageConversion.EncodeArrayToPNG(raw, GraphicsFormat.R8G8B8A8_UNorm, w, h);
                buffers.Add(raw);
                return png;
            })));
            while (encoding.Count > maxEncoding) Flush();
        }

        void Flush()
        {
            (int index, Task<byte[]> task) = encoding.Dequeue();
            byte[] png = task.Result;
            if (output == Output.Frames) File.WriteAllBytes(Path.Combine(video, $"frame_{index + 1:000000}.png"), png);
            else pipe.Write(png, 0, png.Length);
        }

        // Blending over a transparent background leaves colour multiplied by alpha; video with
        // alpha wants it straight.
        static void Unpremultiply(byte[] p)
        {
            for (int i = 0; i < p.Length; i += 4)
            {
                int a = p[i + 3];
                if (a == 0 || a == 255) continue;
                p[i] = (byte)Mathf.Min(255, p[i] * 255 / a);
                p[i + 1] = (byte)Mathf.Min(255, p[i + 1] * 255 / a);
                p[i + 2] = (byte)Mathf.Min(255, p[i + 2] * 255 / a);
            }
        }

        // ---- the end ---------------------------------------------------------------------------

        void Finish()
        {
            while (encoding.Count > 0) Flush();
            if (encoder != null)
            {
                pipe.Flush();
                pipe.Close();
                if (!encoder.WaitForExit(300000) || encoder.ExitCode != 0)
                    throw new System.Exception($"ffmpeg failed encoding the frames (exit {(encoder.HasExited ? encoder.ExitCode : -1)})");
                encoder = null;
            }

            if (audio != null)
            {
                float[] mix = ReplaySound.Mix(player.Data, s.From, s.From + ears.Count / (float)s.Fps, Ear);
                ReplaySound.WriteWav(audio, mix);
                if (output != Output.Frames) Mux();
            }

            WriteSidecar();
            Debug.Log($"ReplayRender: done — {ears.Count} frames in {Time.realtimeSinceStartup - wallStart:0.0} s → {video}" +
                      (audio != null ? $" (+ {Path.GetFileName(audio)})" : ""));
            Quit(0);
        }

        Pose Ear(float t)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt((t - s.From) * s.Fps), 0, ears.Count - 1);
            return ears[i];
        }

        // The rendered frames and the mixed sound, in one file.
        void Mux()
        {
            string audioCodec = output == Output.Webm ? "-c:a libopus -b:a 160k" : output == Output.Mov ? "-c:a pcm_s16le" : "-c:a aac -b:a 192k";
            Process p = Run(ffmpeg, $"-y -hide_banner -loglevel error -i \"{VideoOnly}\" -i \"{audio}\" -map 0:v -map 1:a -c:v copy {audioCodec} -shortest \"{video}\"", stdin: false);
            if (p.WaitForExit(300000) && p.ExitCode == 0)
            {
                File.Delete(VideoOnly);
                return;
            }
            Debug.LogWarning($"ReplayRender: couldn't add the sound to the video ({EncoderLog()}); the video is silent and the sound is in {Path.GetFileName(audio)}");
            if (File.Exists(video)) File.Delete(video);
            File.Move(VideoOnly, video);
        }

        void WriteSidecar()
        {
            var layers = new List<object>();
            foreach (MindLayer one in new[] { MindLayer.Belief, MindLayer.Guess, MindLayer.Cone, MindLayer.Sound, MindLayer.Thoughts, MindLayer.Actors })
                if ((s.Layers & one) != 0) layers.Add(one.ToString().ToLowerInvariant());
            var o = new Dictionary<string, object>
            {
                ["version"] = 1,
                ["game"] = GameNames.Game,
                ["krec"] = Path.GetFileName(s.Krec),
                ["stem"] = player.Data.Header.Stem,
                ["shift"] = player.Data.Header.Shift,
                ["from"] = Round(s.From),
                ["to"] = Round(s.From + ears.Count / (float)s.Fps),
                ["moment"] = s.Moment > 0 ? (object)s.Moment : null,
                ["shot"] = s.Shot,
                ["subject"] = s.Subject == "player" ? "you" : s.Subject,
                ["layers"] = layers,
                ["alpha"] = s.Alpha,
                ["dof"] = s.Dof,
                ["width"] = s.Width,
                ["height"] = s.Height,
                ["fps"] = s.Fps,
                ["frames"] = ears.Count,
                ["video"] = output == Output.Frames ? "frame_%06d.png" : Path.GetFileName(video),
                ["audio"] = audio != null ? Path.GetFileName(audio) : null,
                ["track"] = track.ToJson(),
                ["rendered"] = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["renderSeconds"] = Round(Time.realtimeSinceStartup - wallStart)
            };
            File.WriteAllText(sidecar, MiniJson.Serialize(o));
        }

        static double Round(float v) => System.Math.Round(v, 3);

        string EncoderLog()
        {
            lock (encoderLog) return string.Join("\n", encoderLog);
        }

        void Abort()
        {
            try
            {
                pipe?.Close();
                if (encoder != null && !encoder.HasExited) encoder.Kill();
            }
            catch (System.Exception) { }
        }

        public static void Quit(int code)
        {
            Time.captureFramerate = 0;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
