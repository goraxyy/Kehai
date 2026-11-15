using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kehai.Look
{
    // Baselines for the private Look Lab, not claims about measured performance.
    // All effects are optional; defaults preserve sharpness and player comfort.
    public static class StylePresets
    {
        public enum Style { Neutral, FluorescentRealism, SoftStylised }
        public static VolumeProfile Create(Style style, bool cameraEffects = true)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Kehai " + style;
            var colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(0f);
            colour.contrast.Override(style == Style.FluorescentRealism ? 10f : style == Style.SoftStylised ? -5f : 0f);
            colour.saturation.Override(style == Style.FluorescentRealism ? -12f : style == Style.SoftStylised ? 8f : 0f);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.2f);
            bloom.intensity.Override(cameraEffects && style != Style.Neutral ? .12f : 0f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(cameraEffects && style == Style.FluorescentRealism ? .13f : 0f);
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(cameraEffects && style == Style.FluorescentRealism ? .06f : 0f);
            var distortion = profile.Add<LensDistortion>(true);
            distortion.intensity.Override(0f);
            var fringe = profile.Add<ChromaticAberration>(true);
            fringe.intensity.Override(0f);
            var blur = profile.Add<MotionBlur>(true);
            blur.intensity.Override(0f);
            var focus = profile.Add<DepthOfField>(true);
            focus.active = false; // Never blur tasks or threats while walking.
            return profile;
        }
    }

}
