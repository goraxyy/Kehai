using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Kehai.Karen
{
    // The model pack (0.4.0, build plan §6.5, D15, C3.7): the general model's numbers as one data
    // file, so a better "everyone" can reach players between builds.
    //
    //   line 1   the body: JSON {"format": "kehai-model-pack", "pack_version": 7, "inputs_version": 1,
    //            "created": "…", "parts": [ {"kind": "start"|"brain"|"weights", "name": …, "card": {…}, …} ]}
    //   line 2   the signature: RSA PKCS#1 v1.5 over SHA-256 of line 1's UTF-8 bytes, base64
    //
    // tools/learn/pack.py makes and signs one; tools/learn/publish.py is the gate. The game's checks,
    // each of which refuses the whole pack and logs why:
    //   the signature, against the public key in the build (PackKeys; the private key is never in git)
    //   the inputs' version (a brain made for other inputs can't read these)
    //   every trick name known (a pack naming a trick this build doesn't have is for another build)
    //   a size limit
    // What a pack can't do: the fairness rules, the mask and the Director are code. A bad pack can
    // make her dull or too deadly, never unfair. Used from the next shift, never during one.
    //
    // Which numbers she uses: -karen-pack <file> if it passes; else the newest good pack in
    // <persistent data>/model_packs/; else the build's own (Current is null).
    public sealed class ModelPack
    {
        public const string Format = "kehai-model-pack";
        public const int MaxBytes = 16 * 1024 * 1024;

        public static ModelPack Current { get; private set; }
        public static string LastRefusal { get; private set; }
        // Tests put a key of their own here; the build's own is PackKeys.Public.
        public static RSAParameters? TrustedKeyOverride;

        public int PackVersion { get; private set; }
        public int InputsVersion { get; private set; }
        public string Created { get; private set; } = "";
        public string Source { get; private set; } = "";
        readonly Dictionary<string, BrainNet> brains = new Dictionary<string, BrainNet>();
        readonly Dictionary<string, Dictionary<string, float>> weights = new Dictionary<string, Dictionary<string, float>>();
        public LedgerStart Start { get; private set; }
        public readonly List<string> Parts = new List<string>();

        public BrainNet Brain(string name) => brains.TryGetValue(name, out BrainNet b) ? b : null;
        public IReadOnlyDictionary<string, float> Weights(string name) => weights.TryGetValue(name, out var w) ? w : null;

        public static string Folder => Path.Combine(Application.persistentDataPath, "model_packs");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Current = null;
            LastRefusal = null;
        }

        // Picks the numbers for the next shift (never called while one runs).
        public static void Select(string explicitFile)
        {
            ModelPack chosen = null;
            if (!string.IsNullOrEmpty(explicitFile))
            {
                if (TryLoad(explicitFile, out chosen, out string why)) Debug.Log($"{GameNames.Antagonist}: model pack {chosen.PackVersion} from {explicitFile}");
                else Debug.LogWarning($"{GameNames.Antagonist}: model pack {explicitFile} refused ({why}); the build's own numbers instead");
            }
            else if (Directory.Exists(Folder))
            {
                foreach (string file in Directory.GetFiles(Folder, "*.kpack").OrderByDescending(f => f))
                {
                    if (TryLoad(file, out ModelPack p, out string why) && (chosen == null || p.PackVersion > chosen.PackVersion)) chosen = p;
                    else if (p == null) Debug.LogWarning($"{GameNames.Antagonist}: model pack {Path.GetFileName(file)} refused ({why})");
                }
            }
            Use(chosen);
        }

        public static void Use(ModelPack pack)
        {
            Current = pack;
            BrainNets.Forget();
            LedgerStart.Forget();
            SenseModel.Forget();
        }

        public static bool TryLoad(string path, out ModelPack pack, out string why)
        {
            pack = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { why = LastRefusal = "no such file"; return false; }
                if (info.Length > MaxBytes) { why = LastRefusal = $"{info.Length} bytes, over the {MaxBytes} limit"; return false; }
                return TryParse(File.ReadAllText(path), path, out pack, out why);
            }
            catch (System.Exception e)
            {
                why = LastRefusal = e.Message;
                return false;
            }
        }

        public static bool TryParse(string text, string source, out ModelPack pack, out string why)
        {
            pack = null;
            why = null;
            if (text == null || Encoding.UTF8.GetByteCount(text) > MaxBytes) { why = LastRefusal = "too big"; return false; }
            string[] lines = text.Replace("\r", "").Split('\n');
            if (lines.Length < 2 || lines[0].Length == 0) { why = LastRefusal = "not a pack: a body and a signature line"; return false; }
            if (!Verify(lines[0], lines[1].Trim(), out why)) { LastRefusal = why; return false; }

            Dictionary<string, object> body = MiniJson.ParseObject(lines[0]);
            if (body == null || body.GetString("format") != Format) { why = LastRefusal = "not a Kehai model pack"; return false; }
            var p = new ModelPack
            {
                PackVersion = (int)body.GetNumber("pack_version", 0),
                InputsVersion = (int)body.GetNumber("inputs_version", -1),
                Created = body.GetString("created", ""),
                Source = source
            };
            if (p.InputsVersion != BrainObservation.Version) { why = LastRefusal = $"made for inputs version {p.InputsVersion}, this build reads {BrainObservation.Version}"; return false; }

            var parts = body.TryGetValue("parts", out object po) ? po as List<object> : null;
            if (parts == null) { why = LastRefusal = "no parts"; return false; }
            foreach (Dictionary<string, object> part in parts.Cast<Dictionary<string, object>>())
            {
                string kind = part.GetString("kind", ""), name = part.GetString("name", "");
                switch (kind)
                {
                    case "brain":
                        BrainNet net = ReadBrain(part, out why);
                        if (net == null) { LastRefusal = why; return false; }
                        p.brains[name] = net;
                        break;
                    case "start":
                        LedgerStart start = ReadStart(part, out why);
                        if (start == null) { LastRefusal = why; return false; }
                        p.Start = start;
                        break;
                    case "weights":
                        var values = new Dictionary<string, float>();
                        foreach (var kv in part.GetObject("values") ?? new Dictionary<string, object>())
                            values[kv.Key] = System.Convert.ToSingle(kv.Value, CultureInfo.InvariantCulture);
                        p.weights[name] = values;
                        break;
                    default:
                        why = LastRefusal = $"a part of a kind this build doesn't know: {kind}";
                        return false;
                }
                p.Parts.Add(kind + ":" + name);
            }
            pack = p;
            return true;
        }

        // ---- the signature -------------------------------------------------------------------------

        public static bool Verify(string body, string signature, out string why)
        {
            why = null;
            RSAParameters? key = TrustedKeyOverride ?? PackKeys.Public;
            if (key == null) { why = "this build trusts no pack key yet (tools/learn/pack.py keygen)"; return false; }
            byte[] sig;
            try { sig = System.Convert.FromBase64String(signature); }
            catch (System.FormatException) { why = "the signature isn't base64"; return false; }
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(key.Value);
                if (!rsa.VerifyData(Encoding.UTF8.GetBytes(body), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    why = "the signature doesn't match: tampered with, or signed by another key";
                    return false;
                }
            }
            return true;
        }

        // For tests and tools: a body signed with a private key (never the build's: it isn't in git).
        public static string Sign(string body, RSAParameters privateKey)
        {
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(privateKey);
                return System.Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
        }

        // ---- parts ---------------------------------------------------------------------------------

        static float[] Floats(object o) => o is List<object> l ? l.Select(x => x == null ? float.NaN : System.Convert.ToSingle(x, CultureInfo.InvariantCulture)).ToArray() : new float[0];

        static BrainNet ReadBrain(Dictionary<string, object> part, out string why)
        {
            why = null;
            var card = new BrainCard { Name = part.GetString("name", "") };
            Dictionary<string, object> c = part.GetObject("card") ?? new Dictionary<string, object>();
            card.Version = c.GetString("version", "");
            card.InputsVersion = (int)c.GetNumber("inputs_version", 0);
            card.Trained = c.GetBool("trained", true);
            card.TrainedAgainst = c.GetString("trained_against", "");
            card.Steps = (long)c.GetNumber("steps", 0);
            card.Validation = c.GetString("validation", "");
            card.Command = c.GetString("command", "");
            if (c.TryGetValue("outputs", out object outs) && outs is List<object> heads)
                card.OutputNames = heads.Select(h => ((List<object>)h).Select(x => x as string).ToArray()).ToArray();
            if (card.InputsVersion != BrainObservation.Version) { why = $"{card.Name} reads inputs version {card.InputsVersion}, this build writes {BrainObservation.Version}"; return null; }
            if (card.OutputNames != null)
                foreach (string name in card.OutputNames.SelectMany(h => h))
                    if (!BrainSeam.KnownOutput(name)) { why = $"{card.Name} names {name}, which this build doesn't have"; return null; }
            try
            {
                var hidden = ((List<object>)part["hidden"]).Cast<Dictionary<string, object>>().ToList();
                var heads2 = ((List<object>)part["heads"]).Cast<Dictionary<string, object>>().ToList();
                return new BrainNet(card, Floats(part["mean"]), Floats(part["std"]),
                    hidden.Select(h => (int)h.GetNumber("out")).ToArray(), hidden.Select(h => Floats(h["w"])).ToArray(), hidden.Select(h => Floats(h["b"])).ToArray(),
                    heads2.Select(h => (int)h.GetNumber("out")).ToArray(), heads2.Select(h => Floats(h["w"])).ToArray(), heads2.Select(h => Floats(h["b"])).ToArray());
            }
            catch (System.Exception e)
            {
                why = $"{card.Name}: {e.Message}";
                return null;
            }
        }

        static LedgerStart ReadStart(Dictionary<string, object> part, out string why)
        {
            why = null;
            Dictionary<string, object> c = part.GetObject("card") ?? new Dictionary<string, object>();
            var s = new LedgerStart { Version = c.GetString("version", "pack"), Fitted = c.GetBool("trained", true) };
            s.SprintShare = (float)part.GetNumber("sprint_share", s.SprintShare);
            foreach (var kv in part.GetObject("tricks") ?? new Dictionary<string, object>())
            {
                if (TacticLibrary.Get(kv.Key) == null) { why = $"the starting Ledger names a trick this build doesn't have: {kv.Key}"; return null; }
                float[] row = Floats(kv.Value);
                for (int i = 0; i < row.Length; i++) if (!float.IsNaN(row[i])) s.SetTrick(kv.Key, i - 1, row[i]);
            }
            foreach (var kv in part.GetObject("kinds") ?? new Dictionary<string, object>())
            {
                float[] row = Floats(kv.Value);
                for (int i = 0; i < row.Length; i++) if (!float.IsNaN(row[i])) s.SetKind(kv.Key, i - 1, row[i]);
            }
            foreach (var kv in part.GetObject("answers") ?? new Dictionary<string, object>())
                s.SetAnswer(kv.Key, System.Convert.ToSingle(kv.Value, CultureInfo.InvariantCulture));
            if (part.TryGetValue("hides", out object h) && h is List<object> hides)
                foreach (List<object> pair in hides.Cast<List<object>>())
                    s.Hides.Add(new KeyValuePair<string, float>(pair[0] as string, System.Convert.ToSingle(pair[1], CultureInfo.InvariantCulture)));
            return s;
        }
    }

    // The public half of the pack-signing key, in the build (§6.5). Null until the owner makes a
    // pair with `python3 tools/learn/pack.py keygen`, which keeps the private half outside the repo
    // (~/.kehai/model_pack_key.json) and prints the two numbers to paste here.
    public static class PackKeys
    {
        public const string Modulus = "";       // base64
        public const string Exponent = "AQAB";

        public static RSAParameters? Public =>
            string.IsNullOrEmpty(Modulus) ? (RSAParameters?)null
                : new RSAParameters { Modulus = System.Convert.FromBase64String(Modulus), Exponent = System.Convert.FromBase64String(Exponent) };
    }
}
