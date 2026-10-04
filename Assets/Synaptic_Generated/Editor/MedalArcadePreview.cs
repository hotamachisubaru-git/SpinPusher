using UnityEditor;
using UnityEngine;

/// <summary>Read-only Play-mode views for inspecting the generated cabinet.</summary>
[InitializeOnLoad]
public static class MedalArcadePreview
{
    private const string Selection = "MedalArcade.PreviewSelection";
    private const string Active = "MedalArcade.PreviewActive";
    private const string Background = "MedalArcade.PreviewBackground";
    static MedalArcadePreview() { EditorApplication.update += Tick; }

    [MenuItem("Tools/Medal Pusher/Preview/Game")]
    public static void Game() => Select("Game");
    [MenuItem("Tools/Medal Pusher/Preview/Cabinet")]
    public static void Cabinet() => Select("Cabinet");
    [MenuItem("Tools/Medal Pusher/Preview/Ruby")]
    public static void Ruby() => Select("Ruby");
    [MenuItem("Tools/Medal Pusher/Preview/Sapphire")]
    public static void Sapphire() => Select("Sapphire");
    [MenuItem("Tools/Medal Pusher/Preview/Amber")]
    public static void Amber() => Select("Amber");
    [MenuItem("Tools/Medal Pusher/Preview/Upper")]
    public static void Upper() => Select("Upper");
    [MenuItem("Tools/Medal Pusher/Preview/Settings")]
    public static void Settings() => Select("Settings");
    [MenuItem("Tools/Medal Pusher/Preview/Stop")]
    public static void Stop()
    {
        SessionState.SetBool(Active, false);
        SessionState.SetString(Selection, "");
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = false;
        Application.runInBackground = SessionState.GetBool(Background, false);
    }
    private static void Select(string selection)
    {
        if (!SessionState.GetBool(Active, false)) SessionState.SetBool(Background, Application.runInBackground);
        Application.runInBackground = true;
        SessionState.SetString(Selection, selection);
        SessionState.SetBool(Active, true);
        EditorApplication.isPaused = false;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = true;
    }
    private static void Tick()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
        Application.runInBackground = true;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        string selection = SessionState.GetString(Selection, "");
        if (selection.Length == 0 || Time.time < .5f) return;
        var view = Object.FindFirstObjectByType<MedalPusherCameraView>();
        var controller = Object.FindFirstObjectByType<MedalSlotJackpotController>();
        if (view == null || controller == null) return;
        controller.enabled = false;
        var input = controller.game.GetComponent<MedalInputHandler>();
        if (input != null) input.enabled = false;
        controller.game.StopThrowing();
        view.ReleaseStation();
        view.overview = selection == "Cabinet";
        view.ApplyView();
        foreach (var station in controller.stations)
            if (station.kind.ToString() == selection) view.FocusStation(station);
        if (selection == "Upper") view.FocusStation(controller.upperStation);
        if (selection == "Settings") Object.FindFirstObjectByType<MedalArcadeSettingsUI>()?.Open();
        SessionState.SetString(Selection, "");
    }
}
