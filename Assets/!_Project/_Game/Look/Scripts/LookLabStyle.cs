using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kehai.Look
{
    // Attach only inside the private lab. Inspector controls avoid new gameplay keybinds.
    public sealed class LookLabStyle : MonoBehaviour
    {
        public StylePresets.Style style = StylePresets.Style.FluorescentRealism;
        public bool cameraEffects = true;
        Volume volume;
        VolumeProfile owned;
        readonly Dictionary<Light,(Color colour,LightShadows shadows)> lights=new Dictionary<Light,(Color,LightShadows)>();
        bool captured,fog;
        Color fogColour;
        float fogDensity;
        FogMode fogMode;
        void OnEnable() => Apply();
        [ContextMenu("Apply look")]
        public void Apply()
        {
            if (volume == null) volume = GetComponent<Volume>() ?? gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100;
            if (owned != null) Release();
            owned = StylePresets.Create(style,cameraEffects);
            volume.sharedProfile = owned;
            if(!captured)
            {
                captured=true;fog=RenderSettings.fog;fogColour=RenderSettings.fogColor;
                fogDensity=RenderSettings.fogDensity;fogMode=RenderSettings.fogMode;
                foreach(var light in FindObjectsByType<Light>()) lights[light]=(light.color,light.shadows);
            }
            RenderSettings.fog=style==StylePresets.Style.FluorescentRealism;
            RenderSettings.fogMode=FogMode.Exponential;
            RenderSettings.fogColor=new Color(.13f,.16f,.15f);
            RenderSettings.fogDensity=.009f;
            int shadowed=0;
            foreach(var pair in lights)
            {
                if(pair.Key==null)continue;
                pair.Key.color=style==StylePresets.Style.FluorescentRealism?new Color(.86f,.94f,.90f):style==StylePresets.Style.SoftStylised?new Color(1f,.91f,.79f):pair.Value.colour;
                // Compare at a small explicit shadow budget; ceiling fill lights cast none.
                pair.Key.shadows=style==StylePresets.Style.Neutral?pair.Value.shadows:
                    pair.Key.type==LightType.Spot&&shadowed++<4?LightShadows.Soft:LightShadows.None;
            }
        }
        void Release()
        {
            if (volume != null) volume.sharedProfile = null;
            foreach (var component in owned.components)
                if (component != null) { if (Application.isPlaying) Destroy(component); else DestroyImmediate(component); }
            if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
            owned = null;
        }
        void OnDisable()
        {
            if (owned != null) Release();
            if(!captured)return;
            RenderSettings.fog=fog;RenderSettings.fogColor=fogColour;
            RenderSettings.fogDensity=fogDensity;RenderSettings.fogMode=fogMode;
            foreach(var pair in lights) if(pair.Key!=null) {pair.Key.color=pair.Value.colour;pair.Key.shadows=pair.Value.shadows;}
            lights.Clear();captured=false;
        }
    }
}
