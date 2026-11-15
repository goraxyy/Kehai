using Kehai;
using Kehai.Karen;
using UnityEngine;

// The fourth clock-out is a finish, rather than a time clock silently refusing night five.
public sealed class CareerCompletionScreen : MonoBehaviour
{
    ShiftManager shift;
    bool shown;
    void Awake() => shift = GetComponent<ShiftManager>();
    void OnEnable() { if (shift != null) shift.CareerCompleted += Show; }
    void OnDisable()
    {
        if (shift != null) shift.CareerCompleted -= Show;
        FullScreenPanel.Set(this, false);
    }
    void Show()
    {
        if (!Edition.ShowUI || Application.isBatchMode || KarenBootstrap.Override != null) return;
        shown = true;
        if (KarenBrain.Instance != null && KarenBrain.Instance.Review != null) KarenBrain.Instance.Review.Visible = false;
        FullScreenPanel.Set(this, true);
        GamePause.Set(true);
    }
    void Update()
    {
        if (shown && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            shown = false;
            FullScreenPanel.Set(this, false);
            MainMenu.ReturnToMenu();
        }
    }
    void OnGUI()
    {
        if (!shown) return;
        float w = Mathf.Min(700f, Screen.width - 40f);
        var card = new Rect((Screen.width - w) * .5f, Screen.height * .35f, w, 180f);
        UiTheme.Fill(card, UiTheme.Panel, 16f);
        var style = UiTheme.Label(28, UiTheme.Text, TextAnchor.MiddleCenter, false);
        GUI.Label(card, "Four nights done.\nCareer complete\n\n[Enter] main menu", style);
    }
}
