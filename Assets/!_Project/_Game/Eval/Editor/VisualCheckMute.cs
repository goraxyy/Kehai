using UnityEditor;

// VisualCheck in the editor's window (-kehai-shots without -batchmode): the editor is muted
// before anything plays, since nothing an agent runs may make a sound.
[InitializeOnLoad]
public static class VisualCheckMute
{
    static VisualCheckMute()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-kehai-shots") >= 0)
            EditorUtility.audioMasterMute = true;
    }
}
