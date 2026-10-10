using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    [System.Serializable]
    public class ArmStat
    {
        public string id;
        public float q;          // Q̂: learned mean panic delta
        public float n;          // pulls (fractional after decay)
        public float lastUsed = -9999f;
    }

    [System.Serializable]
    public class RegionStat
    {
        public string key;       // StoreMap region key, stable across runs
        public float dwell;      // seconds, decayed per shift
        public float concealment;
        public int lastConcealShift;
    }

    [System.Serializable]
    public class RouteStat
    {
        public string destination;   // landmark kind the route ends at
        public string path;          // region keys, '|' separated
        public float count;
        public int lastShift;
    }

    [System.Serializable]
    public class CounterStat
    {
        public string kind;
        public int count;
        public int lastShift;
    }

    // The player model (Karen.md §7.1), serialised. Roughly forty numbers and a few
    // histograms; survives death, quitting and new shifts.
    [System.Serializable]
    public class LedgerData
    {
        public int version = 1;
        public string playerName = "Employee #0417";
        public int shiftsWorked;
        public int shiftsClockedOut;
        public int caughtCount;
        public int warnings;
        public int burnouts;
        public bool endingReached;

        public bool baselineSet;
        public float[] baselineMean = new float[PanicIndex.Features];
        public float[] baselineStd = new float[PanicIndex.Features];

        // movement
        public float sprintFraction, crouchFraction, meanSpeed, lookBehindRate, timeToFlee = 2f;
        // economy
        public float coffeePerShift, energyAtClockOut = 1f;
        // work
        public float meanShiftSeconds;
        public string lastCompletionOrder = string.Empty;

        public List<ArmStat> arms = new List<ArmStat>();
        public List<RegionStat> regions = new List<RegionStat>();
        public List<RouteStat> routes = new List<RouteStat>();
        public List<CounterStat> counterplay = new List<CounterStat>();
    }

    // The Ledger (Karen.md §2.3, §7). It does not decide what happens; it decides what Karen
    // is inclined to try, and what she expects of this particular employee.
    //
    //   Bandit    — every tactic is an arm. UCB (or Thompson) with a habituation penalty
    //               and an authored prior that fades as evidence arrives (§7.2).
    //   Habits    — where you dwell, where you hide, the routes you take between jobs.
    //   Decay     — every learned prior fades without reinforcement, so changing your
    //               behaviour visibly works within two shifts (fairness rule 7).
    public sealed class KarenLedger
    {
        public LedgerData Data { get; private set; } = new LedgerData();
        public bool Persistent { get; set; }
        public string PlayerName => Data.playerName;

        readonly KarenConfig config;
        readonly Dictionary<string, ArmStat> arms = new Dictionary<string, ArmStat>();
        readonly Dictionary<string, RegionStat> regions = new Dictionary<string, RegionStat>();
        int shift = 1;

        // Route tracking within a shift.
        readonly List<string> currentRoute = new List<string>();
        string lastRegionKey;

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "karen_ledger.json");
        public string SavePath => string.IsNullOrEmpty(config?.ledgerPath) ? DefaultPath : config.ledgerPath;

        public KarenLedger(KarenConfig config)
        {
            this.config = config;
        }

        // ---- persistence ------------------------------------------------------------------

        public void Load()
        {
            if (!Persistent || !File.Exists(SavePath)) { Index(); return; }
            try
            {
                Data = JsonUtility.FromJson<LedgerData>(File.ReadAllText(SavePath)) ?? new LedgerData();
                if (Data.baselineMean == null || Data.baselineMean.Length != PanicIndex.Features)
                {
                    Data.baselineMean = new float[PanicIndex.Features];
                    Data.baselineStd = new float[PanicIndex.Features];
                    Data.baselineSet = false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(GameNames.Antagonist + ": couldn't read the ledger, starting fresh. " + e.Message);
                Data = new LedgerData();
            }
            Index();
        }

        public void Save()
        {
            if (!Persistent) return;
            Data.arms = arms.Values.ToList();
            Data.regions = regions.Values.ToList();
            string dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SavePath, JsonUtility.ToJson(Data, true));
        }

        public void Wipe()
        {
            Data = new LedgerData();
            Index();
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

        void Index()
        {
            arms.Clear();
            foreach (ArmStat a in Data.arms) arms[a.id] = a;
            regions.Clear();
            foreach (RegionStat r in Data.regions) regions[r.key] = r;
        }

        // ---- shift boundaries -----------------------------------------------------------

        public void BeginShift(int shiftNumber)
        {
            shift = shiftNumber;
            Data.shiftsWorked++;
            currentRoute.Clear();
            lastRegionKey = null;

            // Below rung E nothing is carried between shifts: the within-shift bandit (rung D)
            // starts from the prior every shift, and so do the habits.
            if (!Persistent)
            {
                arms.Clear();
                regions.Clear();
                Data.routes.Clear();
            }

            foreach (ArmStat a in arms.Values) a.lastUsed = -9999f;
        }

        // Rule 7: everything learned fades unless it is reinforced.
        public void EndShift(float shiftSeconds, float energyAtEnd, bool clockedOut)
        {
            if (clockedOut) Data.shiftsClockedOut++;
            Data.energyAtClockOut = energyAtEnd;
            Data.meanShiftSeconds = Mathf.Lerp(Data.meanShiftSeconds <= 0f ? shiftSeconds : Data.meanShiftSeconds, shiftSeconds, 0.3f);

            foreach (RegionStat r in regions.Values)
            {
                r.dwell *= 0.7f;
                r.concealment *= r.lastConcealShift == shift ? 0.8f : 0.35f;
            }
            foreach (RouteStat route in Data.routes)
                route.count *= route.lastShift == shift ? 0.9f : 0.6f;
            Data.routes.RemoveAll(r => r.count < 0.3f);

            foreach (ArmStat a in arms.Values)
            {
                Tactic t = TacticLibrary.Get(a.id);
                float prior = t != null ? t.PanicPrior : 0f;
                a.q = Mathf.Lerp(a.q, prior, 0.15f);
                a.n *= 0.8f;
            }

            if (energyAtEnd <= 0.01f) Data.burnouts++;
            Save();
        }

        // ---- the bandit (§7.2) ------------------------------------------------------------

        ArmStat Arm(string id)
        {
            if (!arms.TryGetValue(id, out ArmStat a))
            {
                Tactic t = TacticLibrary.Get(id);
                a = new ArmStat { id = id, q = t != null ? t.PanicPrior : 0f };
                arms[id] = a;
            }
            return a;
        }

        public float ExpectedPanicDelta(Tactic t, KarenContext c)
        {
            if (!c.Features.Bandit) return t.PanicPrior;
            return arms.TryGetValue(t.Id, out ArmStat a) && a.n > 0.5f ? a.q : t.PanicPrior;
        }

        // The authored escalation curve: a tactic just introduced this shift gets a push.
        public float PriorScore(Tactic t, KarenContext c)
        {
            float fresh = t.IntroducedShift == c.ShiftNumber ? 0.1f : 0f;
            return t.PanicPrior + fresh;
        }

        //   score(a) = Q̂(a) + c·sqrt(ln t / n(a)) − λ·exp(−(t − tLast)/τ) + prior(a, shift)
        public float BanditScore(Tactic t, KarenContext c, out string why)
        {
            ArmStat a = Arm(t.Id);
            float total = 1f;
            foreach (ArmStat s in arms.Values) total += s.n;

            float value;
            string how;
            if (config.thompson)
            {
                float sigma = 0.15f / Mathf.Sqrt(a.n + 1f);
                value = a.q + sigma * c.Rng.Gaussian();
                how = $"θ~N({a.q:+0.00;-0.00}, {sigma:0.00})";
            }
            else
            {
                float explore = a.n < 0.5f ? config.ucbC * 3f : config.ucbC * Mathf.Sqrt(Mathf.Log(total) / a.n);
                value = a.q + explore;
                how = $"Q̂={a.q:+0.00;-0.00} n={a.n:0.#} ucb+{explore:0.00}";
            }

            float habituation = config.habituationLambda * Mathf.Exp(-(c.Now - a.lastUsed) / config.habituationTau);
            float prior = PriorScore(t, c) * config.priorWeight / (1f + a.n / 3f);
            float score = value - habituation + prior;

            why = $"{how} hab-{habituation:0.00} prior+{prior:0.00}";
            return score;
        }

        public void MarkUsed(Tactic t, float now)
        {
            ArmStat a = Arm(t.Id);
            a.lastUsed = now;
        }

        // Q̂ ← Q̂ + α(observed panic delta − Q̂)
        public void Reward(string tacticId, float delta)
        {
            ArmStat a = Arm(tacticId);
            a.q += config.learningRate * (delta - a.q);
            a.n += 1f;
        }

        public IEnumerable<ArmStat> Arms => arms.Values;

        // ---- habits ------------------------------------------------------------------------

        RegionStat RegionStatFor(string key)
        {
            if (!regions.TryGetValue(key, out RegionStat r))
            {
                r = new RegionStat { key = key };
                regions[key] = r;
            }
            return r;
        }

        // Fed by the Director, which is allowed to know where the employee really is.
        public void ObservePlayer(StoreMap map, int region, float dt, bool hiding, MotionState motion, float speed)
        {
            if (region < 0) return;
            Region r = map.Regions[region];
            RegionStat stat = RegionStatFor(r.Key);
            stat.dwell += dt;
            if (hiding)
            {
                stat.concealment += dt;
                stat.lastConcealShift = shift;
            }

            float k = Mathf.Clamp01(dt / 60f);
            Data.sprintFraction = Mathf.Lerp(Data.sprintFraction, motion == MotionState.Sprinting ? 1f : 0f, k);
            Data.crouchFraction = Mathf.Lerp(Data.crouchFraction, motion == MotionState.Crouching ? 1f : 0f, k);
            Data.meanSpeed = Mathf.Lerp(Data.meanSpeed, speed, k);

            // The route since the last job, as a list of region keys without repeats.
            if (r.Key != lastRegionKey && !r.IsDoor || (r.IsDoor && r.Key != lastRegionKey))
            {
                lastRegionKey = r.Key;
                currentRoute.Add(r.Key);
                if (currentRoute.Count > 60) currentRoute.RemoveAt(0);
            }
        }

        // A job was done at a landmark: close the route that led there.
        public void CompleteRoute(string destination)
        {
            if (currentRoute.Count < 2) { currentRoute.Clear(); return; }
            string path = string.Join("|", currentRoute);
            RouteStat existing = Data.routes.FirstOrDefault(x => x.destination == destination && Similar(x.path, path));
            if (existing != null)
            {
                existing.count += 1f;
                existing.path = path;
                existing.lastShift = shift;
            }
            else
            {
                Data.routes.Add(new RouteStat { destination = destination, path = path, count = 1f, lastShift = shift });
            }
            currentRoute.Clear();
        }

        static bool Similar(string a, string b)
        {
            var sa = new HashSet<string>(a.Split('|'));
            var sb = new HashSet<string>(b.Split('|'));
            int common = sa.Count(sb.Contains);
            return common >= Mathf.Max(2, Mathf.Min(sa.Count, sb.Count) * 0.6f);
        }

        public void RecordCounterplay(string kind)
        {
            CounterStat c = Data.counterplay.FirstOrDefault(x => x.kind == kind);
            if (c == null) Data.counterplay.Add(c = new CounterStat { kind = kind });
            c.count++;
            c.lastShift = shift;
        }

        public int CounterplayKindsThisShift => Data.counterplay.Count(x => x.lastShift == shift);

        // The employee's favourite hiding places, as region ids (§7.5).
        public List<int> ConcealmentRegions(KarenContext c, int n)
        {
            var result = new List<int>();
            if (!c.Features.Bandit && !c.Features.Persistent) return result;
            foreach (RegionStat r in regions.Values.Where(x => x.concealment > 4f).OrderByDescending(x => x.concealment).Take(n))
            {
                int id = RegionId(c.Map, r.key);
                if (id >= 0) result.Add(id);
            }
            return result;
        }

        // Where this employee tends to be, as a 0..1 weight per cell — the habit layer of the
        // belief prior (§4.2).
        public void FillHabitPrior(StoreMap map, float[] cells)
        {
            System.Array.Clear(cells, 0, cells.Length);
            if (regions.Count == 0) return;
            float max = 0.001f;
            foreach (RegionStat r in regions.Values) max = Mathf.Max(max, r.dwell + r.concealment * 3f);

            var byKey = new Dictionary<string, float>();
            foreach (RegionStat r in regions.Values) byKey[r.key] = (r.dwell + r.concealment * 3f) / max;

            foreach (Region region in map.Regions)
            {
                if (!byKey.TryGetValue(region.Key, out float w)) continue;
                foreach (int cell in region.Cells) cells[cell] = w;
            }
        }

        public int LeastVisitedRegion(KarenContext c)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            foreach (Region r in c.Map.Regions)
            {
                if (r.IsDoor || r.Area != "Sales floor" || r.Cells.Count < 4) continue;
                float dwell = regions.TryGetValue(r.Key, out RegionStat s) ? s.dwell : 0f;
                float score = dwell + c.Rng.Value * 2f;
                if (score < bestScore) { bestScore = score; best = r.Id; }
            }
            return best >= 0 ? best : c.Belief.PeakRegion;
        }

        // Route prior between two regions: which region on the employee's habitual way there
        // is the best place to wait (§7.5 route denial, §8.6 ambush).
        public bool RoutePrior(KarenContext c, int from, int to, out int waitRegion, out float confidence)
        {
            waitRegion = -1;
            confidence = 0f;
            if (to < 0 || !c.Features.Persistent && !c.Features.Bandit) return false;
            string toKey = c.Map.Regions[to].Key;

            RouteStat best = null;
            foreach (RouteStat route in Data.routes)
            {
                string[] keys = route.path.Split('|');
                if (keys[keys.Length - 1] != toKey && !keys.Contains(toKey)) continue;
                if (best == null || route.count > best.count) best = route;
            }
            if (best == null) return false;

            float total = Data.routes.Where(r => r.destination == best.destination).Sum(r => r.count);
            confidence = total > 0f ? best.count / (total + 1f) : 0f;

            // Wait at a doorway or chokepoint on the way, not too close to the destination.
            string[] path = best.path.Split('|');
            for (int i = path.Length - 2; i >= 0; i--)
            {
                int id = RegionId(c.Map, path[i]);
                if (id < 0) continue;
                Region r = c.Map.Regions[id];
                if (Vector3.Distance(r.Centroid, c.Map.Regions[to].Centroid) < 6f) continue;
                if (r.IsDoor || r.IsChokepoint) { waitRegion = id; return true; }
                if (waitRegion < 0) waitRegion = id;
            }
            return waitRegion >= 0;
        }

        public float BestRouteConfidence(KarenContext c, out string why)
        {
            why = "no route prior";
            int target = c.Brain.LikelyNextJobRegion;
            if (target < 0) return 0f;
            if (!RoutePrior(c, c.Belief.PeakRegion, target, out int wait, out float conf)) return 0f;
            RouteStat route = Data.routes.OrderByDescending(r => r.count).FirstOrDefault();
            why = $"route prior: {c.RegionName(target)} reached via {c.RegionName(wait)} (conf {conf:0.00})";
            return conf;
        }

        public int AmbushRegion(KarenContext c, out string reason)
        {
            reason = null;
            int target = c.Brain.LikelyNextJobRegion;
            if (target < 0) return -1;
            if (!RoutePrior(c, c.Belief.PeakRegion, target, out int wait, out float conf) || conf < 0.35f) return -1;
            float count = Data.routes.Where(r => r.path.Contains(c.Map.Regions[wait].Key)).Sum(r => r.count);
            reason = $"route prior: {c.RegionName(target)} via {c.RegionName(wait)} in {count:0.#} logged trips (conf {conf:0.00})";
            return wait;
        }

        static int RegionId(StoreMap map, string key)
        {
            foreach (Region r in map.Regions) if (r.Key == key) return r.Id;
            return -1;
        }

        public string FavouriteHidingPlace(StoreMap map)
        {
            RegionStat best = regions.Values.OrderByDescending(r => r.concealment).FirstOrDefault();
            if (best == null || best.concealment < 3f) return null;
            int id = RegionId(map, best.key);
            return id >= 0 ? map.Regions[id].Name : null;
        }
    }
}
