using System;
using System.IO;
using System.Linq;
using Kehai.Look;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Scene/art output is ALWAYS external and private. This script does not create another
// project, duplicate Library, download assets, or open a second Unity editor.
public static class LookLabBuilder
{
    [MenuItem("Kehai/Look Lab/Export selected slice privately")]
    public static void ExportSlice()
    {
        GameObject[] selected = Selection.gameObjects;
        if (selected.Length == 0) throw new InvalidOperationException("Select one location slice: rooms, parking, backstreet or maze.");
        string path = EditorUtility.SaveFilePanel("Export private Look Lab slice", "", "KehaiSlice", "unitypackage");
        if (string.IsNullOrEmpty(path)) return;
        if (InsidePublicProject(path)) throw new InvalidOperationException("Art exports must be outside the public project.");
        // A temporary additive scene captures the selection without saving the store.
        Scene original = SceneManager.GetActiveScene();
        Scene temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        string folder = "Assets/__KehaiPrivateExport_" + Guid.NewGuid().ToString("N");
        string scenePath = folder + "/Slice.unity";
        try
        {
            AssetDatabase.CreateFolder("Assets",Path.GetFileName(folder));
            SceneManager.SetActiveScene(temporary);
            foreach (GameObject go in selected)
            {
                // Avoid doubling a selected parent and its selected child.
                if (selected.Any(other => other != go && go.transform.IsChildOf(other.transform))) continue;
                var copy = UnityEngine.Object.Instantiate(go);
                copy.name = go.name;
                SceneManager.MoveGameObjectToScene(copy,temporary);
            }
            EditorSceneManager.SaveScene(temporary,scenePath);
            AssetDatabase.ExportPackage(scenePath,path,ExportPackageOptions.IncludeDependencies);
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            EditorSceneManager.CloseScene(temporary,true);
            AssetDatabase.DeleteAsset(folder);
        }
        Debug.Log("Private slice exported: " + path + ". Import only into the external Look Lab, with the same Unity/URP versions.");
    }
    static bool InsidePublicProject(string path)
    {
        string repo=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string full=Path.GetFullPath(path);
        return full.Equals(repo,StringComparison.OrdinalIgnoreCase) || full.StartsWith(repo+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }

    [MenuItem("Kehai/Look Lab/Add style controls to external lab")]
    public static void AddControls()
    {
        if (!File.Exists(Path.Combine(Application.dataPath,"..","KEHAI_PRIVATE_LOOK_LAB")))
            throw new InvalidOperationException("Use the external Look Lab project. Create its KEHAI_PRIVATE_LOOK_LAB marker after checking storage.");
        var go=new GameObject("Look Lab Style");
        Undo.RegisterCreatedObjectUndo(go,"Add lab style"); go.AddComponent<LookLabStyle>();
        Selection.activeGameObject=go;
    }

    [MenuItem("Kehai/Look Lab/Capture camera comparison privately")]
    public static void Capture()
    {
        Camera camera=Camera.main;
        if (camera==null) throw new InvalidOperationException("Place a camera at the fixed comparison position.");
        string folder=EditorUtility.OpenFolderPanel("Save private style comparison","","");
        if(string.IsNullOrEmpty(folder)) return;
        if(InsidePublicProject(folder)) throw new InvalidOperationException("Screenshots must stay outside the public repository.");
        var go=new GameObject("Temporary style capture");
        var look=go.AddComponent<LookLabStyle>();
        var target=new RenderTexture(1280,720,24);
        var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        var data=camera.GetUniversalAdditionalCameraData();bool oldPost=data.renderPostProcessing;
        try
        {
            data.renderPostProcessing=true;
            foreach(StylePresets.Style style in Enum.GetValues(typeof(StylePresets.Style)))
            {
                look.style=style; look.Apply();
                camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder,style+".png"),pixels.EncodeToPNG());
            }
            File.WriteAllText(Path.Combine(folder,"comparison.html"),"<!doctype html><meta charset='utf-8'><title>Kehai look comparison</title><style>body{background:#171a1a;color:white;font:16px sans-serif}img{width:100%;max-width:900px}</style>"+string.Join("",Enum.GetNames(typeof(StylePresets.Style)).Select(s=>"<h2>"+s+"</h2><img src='"+s+".png'>")));
        }
        finally
        {
            camera.targetTexture=oldTarget;RenderTexture.active=oldActive;data.renderPostProcessing=oldPost;
            UnityEngine.Object.DestroyImmediate(go);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
}
