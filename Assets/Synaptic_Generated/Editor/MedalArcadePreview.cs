using UnityEditor;
using UnityEngine;
using System;
using System.Reflection;
using Object = UnityEngine.Object;

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
    [MenuItem("Tools/Medal Pusher/Preview/Slot Odd")]
    public static void SlotOdd() => SlotResult(1);
    [MenuItem("Tools/Medal Pusher/Preview/Slot Even")]
    public static void SlotEven() => SlotResult(2);
    [MenuItem("Tools/Medal Pusher/Preview/Slot 777")]
    public static void SlotSeven() => SlotResult(7);
    [MenuItem("Tools/Medal Pusher/Preview/Slot Ball")]
    public static void SlotBall() => SlotResult(0);

    private static void SlotResult(int wanted)
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Open Preview/Game first.");
        var controller = UnityEngine.Object.FindFirstObjectByType<MedalSlotJackpotController>();
        if (controller == null || controller.enabled) throw new InvalidOperationException("Use the read-only Game preview first.");
        float payout = controller.targetPayoutPercent, ball = controller.slotBallChancePercent, medal = controller.slotMedalChancePercent;
        int rescue = controller.rescueSpins;
        var random = UnityEngine.Random.state;
        try
        {
            controller.targetPayoutPercent = 100;
            controller.slotBallChancePercent = wanted == 0 || wanted == 7 ? 100 : 0;
            controller.slotMedalChancePercent = wanted == 0 || wanted == 7 ? 0 : 100;
            controller.rescueSpins = 0;
            int seed = -1;
            for (int candidate = 1; candidate < 10000; candidate++)
            {
                UnityEngine.Random.InitState(candidate);
                float roll = UnityEngine.Random.Range(0f, 100f);
                bool match;
                if (wanted == 0 || wanted == 7)
                    match = roll < controller.EffectiveSlotBallChancePercent &&
                        (UnityEngine.Random.value < 9f / 49f) == (wanted == 7);
                else
                {
                    int digit = UnityEngine.Random.Range(1, 9); if (digit >= 7) digit++;
                    match = roll < controller.EffectiveSlotMedalChancePercent && digit == wanted;
                }
                if (match) { seed = candidate; break; }
            }
            if (seed < 0) throw new InvalidOperationException("Slot preview seed unavailable.");
            UnityEngine.Random.InitState(seed);
            typeof(MedalSlotJackpotController).GetMethod("CompleteSpin", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            UnityEngine.Object.FindFirstObjectByType<MedalArcadeUI>()?.Refresh();
        }
        finally
        {
            controller.targetPayoutPercent = payout; controller.slotBallChancePercent = ball;
            controller.slotMedalChancePercent = medal; controller.rescueSpins = rescue;
            UnityEngine.Random.state = random;
        }
    }
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
