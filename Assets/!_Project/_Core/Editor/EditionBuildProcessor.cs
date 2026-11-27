using System;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Processes the build's scene copy only. No authored scene, asset or project setting is changed.
public sealed class EditionBuildProcessor : IProcessSceneWithReport
{
    public int callbackOrder => -1000;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null || !KehaiBuild.BuildingEdition.HasValue) return;
        StripScene(scene, KehaiBuild.BuildingEdition.Value);
    }
    public static void StripScene(Scene scene, KehaiEdition edition, Material trainingPreview = null)
    {
        bool training = edition == KehaiEdition.Training;
        foreach (var root in scene.GetRootGameObjects())
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            // Remove behaviours before their required Canvas/CanvasRenderer components. Again until
            // nothing more goes: a component another one requires (the EventSystem, which the
            // input module requires) can't be removed until that one is gone (0.5.2, C8.1).
            for (int pass = 0; pass < 4; pass++)
            {
                int removed = 0;
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || RequiredByAnother(component)) continue;   // its turn comes next pass
                    if (IsDebugComponent(component.GetType().Name) && (edition == KehaiEdition.Gameplay || component.GetType().Name != "TrickCheck") && edition != KehaiEdition.Testing ||
                        training && (component is UnityEngine.EventSystems.UIBehaviour || component is TMP_Text ||
                        component is TMP_SubMesh || component is AudioSource || component is AudioListener ||
                        IsPresentation(component.GetType().Name)))
                    {
                        Object.DestroyImmediate(component);
                        if (component == null) removed++;
                    }
                }
                if (removed == 0) break;
            }
            components = root.GetComponentsInChildren<Component>(true);
            if (!training) continue;
            foreach (var component in components)
            {
                if (component == null) continue;
                if (component is Canvas || component is CanvasRenderer || component is CanvasGroup)
                { Object.DestroyImmediate(component); continue; }
                // Keep collision, navigation and perceptual lights; remove presentation references.
                if (component is Renderer renderer)
                {
                    var materials = trainingPreview != null ? new Material[renderer.sharedMaterials.Length] : Array.Empty<Material>();
                    Array.Fill(materials, trainingPreview);
                    renderer.sharedMaterials = materials;
                }
                if (trainingPreview == null && component is Camera camera) camera.enabled = false;
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    Object value = property.objectReferenceValue;
                    // The preview material just given to the renderers stays (it was wiped too, 0.5.2).
                    if (value is AudioClip || value is Texture || value is Material && value != trainingPreview || value is TMP_FontAsset)
                        property.objectReferenceValue = null;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        if (training)
        {
            var gate = new GameObject("Edition scene gate");
            SceneManager.MoveGameObjectToScene(gate, scene);
            gate.AddComponent<EditionSceneGate>().trainingPreview = trainingPreview != null;
        }
    }
    // True when another component on the same object requires this one ([RequireComponent]):
    // Unity refuses to remove it, with an error, until that one is gone.
    static bool RequiredByAnother(Component component)
    {
        Type type = component.GetType();
        foreach (Component other in component.GetComponents<Component>())
        {
            if (other == null || other == component) continue;
            foreach (RequireComponent need in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if (need.m_Type0 != null && need.m_Type0.IsAssignableFrom(type) ||
                    need.m_Type1 != null && need.m_Type1.IsAssignableFrom(type) ||
                    need.m_Type2 != null && need.m_Type2.IsAssignableFrom(type))
                    return true;
        }
        return false;
    }

    public static bool IsDebugComponent(string name) => name == "KarenDebugOverlay" || name == "TrickCheck" || name == "VisualCheck";
    static bool IsPresentation(string name) => name == "TaskListUI" || name == "InventoryUI" || name == "BurnoutUI" ||
        name == "MainMenu" || name == "SettingsMenu" || name == "InteractHud" || name == "FirstShiftGuide" || name == "CareerCompletionScreen" ||
        name == "BlinkTestPanel" ||
        name == "PlayerScreenFx" || name == "Eyelids" || name == "BlinkLids" || name == "SpeechBubble";
}
