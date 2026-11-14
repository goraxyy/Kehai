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
        }
        void Release()
        {
            if (volume != null) volume.sharedProfile = null;
            foreach (var component in owned.components)
                if (component != null) { if (Application.isPlaying) Destroy(component); else DestroyImmediate(component); }
            if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
            owned = null;
        }
        void OnDisable() { if (owned != null) Release(); }
    }
}
