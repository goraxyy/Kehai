using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Kehai.Replay
{
    // The player's body for third-person replay shots. Loads the owner's model from
    //   Assets/!_Project/_Game/Replay/Resources/ReplayBody/PlayerBody.fbx   (Resources "ReplayBody/PlayerBody")
    // — Humanoid rig, clips named with idle, walk, crouch (walking crouched), run and carry — and
    // mixes them with the Playables API, so no Animator Controller asset is needed. Until the
    // model is there, a 1.8 m capsule stands in (lower while crouching).
    public sealed class PlayerBodySlot : MonoBehaviour
    {
        public const string ResourcePath = "ReplayBody/PlayerBody";
        public const float CrossFadeSeconds = 0.2f;

        // The capsule's colour: the report's blue for "you", toned down to a uniform.
        static readonly Color Placeholder = new Color(0.36f, 0.62f, 0.74f);

        public enum Clip { Idle, Walk, CrouchWalk, Run, Carry }
        const int ClipCount = 5;

        public bool IsPlaceholder { get; private set; }
        public GameObject Body { get; private set; }
        public Clip Playing { get; private set; } = Clip.Idle;

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly bool[] has = new bool[ClipCount];
        readonly float[] weight = new float[ClipCount];

        public static PlayerBodySlot Create(Transform parent = null)
        {
            var go = new GameObject("PlayerBody (replay)");
            if (parent != null) go.transform.SetParent(parent, false);
            var slot = go.AddComponent<PlayerBodySlot>();
            slot.Build();
            return slot;
        }

        void Build()
        {
            GameObject model = Resources.Load<GameObject>(ResourcePath);
            if (model == null)
            {
                IsPlaceholder = true;
                Body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Body.name = "PlayerBody (placeholder)";
                Body.GetComponent<Renderer>().sharedMaterial = Kehai.Karen.KarenProps.Lit(Placeholder, 0.35f);
                Remove(Body.GetComponent<Collider>());
                Body.transform.SetParent(transform, false);
                Stand(false);
                return;
            }

            Body = Instantiate(model, transform);
            Body.name = "PlayerBody";
            foreach (Collider c in Body.GetComponentsInChildren<Collider>()) Remove(c);
            Animator animator = Body.GetComponentInChildren<Animator>();
            if (animator == null) animator = Body.AddComponent<Animator>();

            graph = PlayableGraph.Create("PlayerBody");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            mixer = AnimationMixerPlayable.Create(graph, ClipCount);
            AnimationPlayableOutput.Create(graph, "Body", animator).SetSourcePlayable(mixer);
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(ResourcePath))
            {
                if (clip.name.StartsWith("__preview__")) continue;
                int slot = (int)Classify(clip.name);
                if (slot < 0 || has[slot]) continue;
                graph.Connect(AnimationClipPlayable.Create(graph, clip), 0, mixer, slot);
                has[slot] = true;
            }
            weight[(int)Clip.Idle] = 1f;
            Apply();
            graph.Play();
        }

        // Which of the five a clip is, from its name; -1 if none.
        public static Clip Classify(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("carry")) return Clip.Carry;
            if (n.Contains("crouch")) return Clip.CrouchWalk;
            if (n.Contains("run") || n.Contains("sprint")) return Clip.Run;
            if (n.Contains("walk")) return Clip.Walk;
            if (n.Contains("idle")) return Clip.Idle;
            return (Clip)(-1);
        }

        // What the body should be doing for a recorded player state (KrecState): motion 0 still,
        // 1 crouching, 2 walking, 3 sprinting; carrying something in hand.
        public static Clip ClipFor(int motion, bool carrying)
        {
            if (motion == 1) return Clip.CrouchWalk;
            if (motion == 3) return Clip.Run;
            if (carrying) return Clip.Carry;
            return motion == 2 ? Clip.Walk : Clip.Idle;
        }

        // Puts the body where the player was, facing their way, doing what they were doing.
        public void Show(Vector3 position, float yaw, int motion, bool carrying, float deltaTime)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Playing = ClipFor(motion, carrying);
            if (IsPlaceholder)
            {
                Stand(motion == 1);
                return;
            }

            Clip target = Available(Playing);
            float step = CrossFadeSeconds > 0f ? deltaTime / CrossFadeSeconds : 1f;
            for (int i = 0; i < ClipCount; i++)
                weight[i] = Mathf.MoveTowards(weight[i], i == (int)target ? 1f : 0f, step);
            Apply();
        }

        // A clip the model lacks falls back to the nearest one it has.
        Clip Available(Clip c)
        {
            if (has[(int)c]) return c;
            if ((c == Clip.Run || c == Clip.Carry || c == Clip.CrouchWalk) && has[(int)Clip.Walk]) return Clip.Walk;
            return Clip.Idle;
        }

        void Apply()
        {
            float total = 0f;
            for (int i = 0; i < ClipCount; i++) if (has[i]) total += weight[i];
            for (int i = 0; i < ClipCount; i++)
                if (has[i]) mixer.SetInputWeight(i, total > 0f ? weight[i] / total : 0f);
        }

        void Stand(bool crouching)
        {
            float height = crouching ? 1.1f : 1.8f;
            Body.transform.localScale = new Vector3(0.5f, height / 2f, 0.5f);
            Body.transform.localPosition = Vector3.up * (height / 2f);
        }

        static void Remove(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
