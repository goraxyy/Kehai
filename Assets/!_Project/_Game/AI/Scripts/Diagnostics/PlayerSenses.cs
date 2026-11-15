using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // What you could sense of her (0.4.0, build plan C1.6): on every recorded sample, whether she
    // was in your view and whether you could hear her. It's the answer sheet for the mirror map
    // (§8.8), for judging only. Nothing under Belief/, Perception/ or Decision/ may read it: she
    // doesn't know what you saw, she guesses (KarenTests' source scan holds them to that).
    //
    //   a simulated player: the observation it already gets (EnvWorld's "visible" and "heard"),
    //                       handed over each time it looks (FromBot)
    //   a person:           worked out from where you stand and face, and the walls: she's within
    //                       55° of where you look, inside 35 m and the fog, nothing in the way; and
    //                       you heard her when a noise of hers carried to you in the last 2 s (her
    //                       steps only when they're audible at all, KarenFx.StepsAudible)
    public static class PlayerSenses
    {
        public const float SightRange = 35f, SightHalfAngle = 55f, HeardFor = 2f;

        static bool botSaw, botHeard;
        static float botAt = -999f;
        static long cursor = -1;
        static float heardAt = -999f;
        static readonly List<NoiseEvent> noises = new List<NoiseEvent>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            botAt = -999f;
            heardAt = -999f;
            cursor = -1;
        }

        // A simulated player's own senses, as it just observed them.
        public static void FromBot(bool saw, bool heard)
        {
            botSaw = saw;
            botHeard = heard;
            botAt = Time.time;
        }

        // A bot that looked in the last second is the one playing.
        public static bool BotPlaying => Time.time - botAt <= 1f;

        public static void Sense(PlayerPresence me, KarenBrain brain, out bool saw, out bool heard)
        {
            saw = false;
            heard = false;
            if (brain != null && (!brain.PresentThisShift || brain.Body == null || !brain.Body.gameObject.activeInHierarchy)) return;
            if (BotPlaying)
            {
                saw = botSaw;
                heard = botHeard;
                return;
            }
            if (me == null || brain == null) return;
            saw = CanSee(me.Head, me.Facing, brain.Body.Position + Vector3.up * 1.4f, brain.Body.transform);
            heard = Heard(me.Position);
        }

        // In view: near enough, inside the fog's limit, within the half-angle of where you look,
        // and the line to her clear (or blocked by nothing but her own body).
        public static bool CanSee(Vector3 head, Vector3 facing, Vector3 her, Transform herBody)
        {
            Vector3 d = her - head;
            float distance = d.magnitude;
            if (distance > Mathf.Min(SightRangeAt(her), FogState.SightLimit(head, her)) || Vector3.Angle(facing, d) > SightHalfAngle) return false;
            return !Physics.Linecast(head, her, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                   || (herBody != null && hit.collider.transform.IsChildOf(herBody));
        }

        // Keep the old baseline until a room circuit is switched off. A held torch can
        // illuminate the target within its real beam; a disabled renderer is never a sight.
        public static float SightRangeAt(Vector3 target)
        {
            if(RoomLightSwitch.Allows(target)) return SightRange;
            float illumination=LightProbe.LevelAt(target);
            Item held=PlayerPresence.Current!=null?PlayerPresence.Current.HeldItem:null;
            Flashlight torch=held!=null?held.GetComponent<Flashlight>():null;
            Light beam=torch!=null&&torch.IsOn?torch.beam:null;
            if(beam!=null&&beam.enabled)
            {
                Vector3 d=target-beam.transform.position;
                if(d.magnitude<=beam.range&&Vector3.Angle(beam.transform.forward,d)<=beam.spotAngle*.5f)
                    illumination=1f;
            }
            return SightRange*Mathf.Lerp(.35f,1f,illumination);
        }

        // A noise of hers that carried to where you stand, in the last two seconds.
        public static bool Heard(Vector3 at)
        {
            if (cursor < 0) cursor = NoiseBus.Latest;
            NoiseBus.ReadSince(ref cursor, noises);
            foreach (NoiseEvent n in noises)
            {
                if (n.Author != NoiseAuthor.Karen && n.Author != NoiseAuthor.Director) continue;
                if (n.Kind == NoiseKind.KarenStep && !KarenFx.StepsAudible) continue;
                if (Vector3.Distance(n.Position, at) <= n.Carry) heardAt = Mathf.Max(heardAt, n.Time);
            }
            return Time.time - heardAt <= HeardFor;
        }
    }
}
