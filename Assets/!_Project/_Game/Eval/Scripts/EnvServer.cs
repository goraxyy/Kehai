using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // The eval harness over the wire: newline-delimited JSON on a local TCP port, one
    // request, one reply. tools/eval/kehai_env.py is the client.
    //
    //   {"cmd":"reset","config":{"seed":7,"rung":"F","shift_seconds":180,"fps":20,"render":false}}
    //   {"cmd":"step","action":{"verb":"mop","target":"spill_2"}}
    //   {"cmd":"observe"}          {"cmd":"map"}          {"cmd":"metrics"}          {"cmd":"close"}
    //
    // Replies carry the observation as data ("obs") and as prose ("text"), whether the
    // episode is over ("done"), and the result of the last action.
    // Local only: binds to 127.0.0.1.
    public sealed class EnvServer : MonoBehaviour
    {
        public int port = 5555;
        public bool quitOnClose;

        TcpListener listener;
        TcpClient connectedClient;
        Thread thread;
        volatile bool running;
        readonly ConcurrentQueue<(string line, StreamWriter reply, ManualResetEventSlim answered)> inbox =
            new ConcurrentQueue<(string, StreamWriter, ManualResetEventSlim)>();
        KehaiEnv env;

        public static EnvServer Start(int port, bool quitOnClose)
        {
            if(!EditionRules.AllowEvaluation(Edition.Current)) throw new System.InvalidOperationException("Evaluation is disabled in Gameplay builds");
            // Configure before OnEnable opens the socket, including nondefault worker ports.
            var go=new GameObject("~KehaiEnvServer");go.SetActive(false);DontDestroyOnLoad(go);
            var server = go.AddComponent<EnvServer>();
            server.port = port;
            server.quitOnClose = quitOnClose;
            go.SetActive(true);
            return server;
        }

        void Awake() => env = KehaiEnv.Ensure();

        void OnEnable()
        {
            if(!EditionRules.AllowEvaluation(Edition.Current)) { enabled=false;return; }
            running = true;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            thread = new Thread(Listen) { IsBackground = true, Name = "KehaiEnvServer" };
            thread.Start();
            Debug.Log($"Kehai env server listening on 127.0.0.1:{port}");
            StartCoroutine(Pump());
        }

        void OnDisable()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { connectedClient?.Close(); } catch { }
            StopAllCoroutines();
        }

        void Listen()
        {
            while (running)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        connectedClient=client;
                        string line;
                        while (running && (line = reader.ReadLine()) != null)
                        {
                            if (line.Trim().Length == 0) continue;
                            // One request at a time: wait until the main thread has answered.
                            // An event rather than Monitor.Wait/Pulse, which can lose a wake-up
                            // that arrives before the wait begins.
                            using (var answered = new ManualResetEventSlim(false))
                            {
                                inbox.Enqueue((line, writer, answered));
                                while (running && !answered.Wait(250)) { }
                            }
                        }
                    }
                }
                catch (SocketException) { }
                catch (IOException) { }
                catch (System.ObjectDisposedException) { return; }
            }
        }

        IEnumerator Pump()
        {
            while (enabled)
            {
                if (inbox.TryDequeue(out var item))
                {
                    Dictionary<string, object> reply = null;
                    IEnumerator handle = Handle(item.line, r => reply = r);
                    // Flatten nested coroutines so a failed validation/action replies with an
                    // error instead of silently killing the TCP pump and hanging the client.
                    var stack=new Stack<IEnumerator>();stack.Push(handle);
                    try
                    {
                        while(running&&enabled&&stack.Count>0)
                        {
                            IEnumerator top=stack.Peek();bool more;
                            try { more=top.MoveNext(); }
                            catch(System.Exception e) { reply=Error(e.GetType().Name+": "+e.Message);break; }
                            if(!more) { stack.Pop();continue; }
                            if(top.Current is IEnumerator nested) stack.Push(nested);
                            else yield return top.Current;
                        }
                    }
                    finally
                    {
                        while(stack.Count>0)
                        {
                            try { (stack.Pop() as System.IDisposable)?.Dispose(); }
                            catch(System.Exception e) { reply=Error(e.GetType().Name+": "+e.Message); }
                        }
                        Send(item.reply, reply ?? Error("request cancelled"));
                        // Shutdown can release and dispose the listener's waiting event first.
                        try { item.answered.Set(); } catch(System.ObjectDisposedException) { }
                    }
                }
                yield return null;
            }
        }

        void Send(StreamWriter writer, Dictionary<string, object> reply)
        {
            try { writer.WriteLine(MiniJson.Serialize(reply)); }
            catch (System.Exception e) { Debug.LogWarning("env server: " + e.Message); }
        }

        static Dictionary<string, object> Error(string message) => new Dictionary<string, object> { ["ok"] = false, ["error"] = message };

        IEnumerator Handle(string line, System.Action<Dictionary<string, object>> reply)
        {
            Dictionary<string, object> request;
            try { request = MiniJson.ParseObject(line); }
            catch (System.Exception e) { reply(Error("bad json: " + e.Message)); yield break; }
            if (request == null) { reply(Error("expected an object")); yield break; }

            string cmd = request.GetString("cmd", string.Empty);
            switch (cmd)
            {
                case "benchmark_capabilities":
                    reply(BenchmarkCareer.Capabilities());
                    break;
                case "benchmark_career":
                    yield return BenchmarkCareer.Play(env,request,reply);
                    break;
                case "reset":
                    yield return env.ResetEpisode(EnvConfig.From(request.GetObject("config")));
                    reply(Observation(true));
                    break;

                case "step":
                    if (!env.Ready) { reply(Error("call reset first")); break; }
                    if (env.Done) { reply(Observation(true)); break; }
                    yield return env.Act(EnvAction.From(request.GetObject("action")));
                    env.CheckDone();
                    reply(Observation(true));
                    break;

                case "observe":
                    reply(Observation(true));
                    break;

                case "map":
                    reply(new Dictionary<string, object>
                    {
                        ["ok"] = true,
                        ["map"] = MiniJson.Parse(StoreMapReport.ToJson(StoreMap.Current, includeAscii: false)),
                        ["ascii"] = StoreMap.Current.ToAscii()
                    });
                    break;

                case "metrics":
                    reply(new Dictionary<string, object> { ["ok"] = true, ["metrics"] = env.Metrics.ToDictionary() });
                    break;

                case "close":
                    reply(new Dictionary<string, object> { ["ok"] = true });
                    if (quitOnClose) Application.Quit();
                    break;

                default:
                    reply(Error($"unknown cmd '{cmd}'"));
                    break;
            }
        }

        Dictionary<string, object> Observation(bool withText)
        {
            if (!env.Ready) return Error("not ready");
            var obs = env.World.Observe(env);
            var reply = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["done"] = env.Done,
                ["obs"] = obs
            };
            if (withText) reply["text"] = EnvWorld.AsText(obs);
            if (env.Done) reply["metrics"] = env.Metrics.ToDictionary();
            return reply;
        }
    }
}
