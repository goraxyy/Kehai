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
            // Remove behaviours before their required Canvas/CanvasRenderer components.
            foreach (var component in components)
            {
                if (component == null) continue;
                if (IsDebugComponent(component.GetType().Name) && (edition == KehaiEdition.Gameplay || component.GetType().Name != "TrickCheck") && edition != KehaiEdition.Testing ||
                    training && (component is UnityEngine.EventSystems.UIBehaviour || component is TMP_Text ||
                    component is TMP_SubMesh || component is AudioSource || component is AudioListener ||
                    IsPresentation(component.GetType().Name))) Object.DestroyImmediate(component);
            }
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
                    if (value is AudioClip || value is Texture || value is Material || value is TMP_FontAsset)
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
    public static bool IsDebugComponent(string name) => name == "KarenDebugOverlay" || name == "TrickCheck" || name == "VisualCheck";
    static bool IsPresentation(string name) => name == "TaskListUI" || name == "InventoryUI" || name == "BurnoutUI" ||
        name == "MainMenu" || name == "SettingsMenu" || name == "InteractHud" || name == "FirstShiftGuide" || name == "CareerCompletionScreen" ||
        name == "BlinkTestPanel" ||
        name == "PlayerScreenFx" || name == "Eyelids" || name == "BlinkLids" || name == "SpeechBubble";
}
