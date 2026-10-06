using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MedalUpperLayoutRevision
{
    [MenuItem("Tools/Medal Pusher/Apply Upper Tuning And Display Settings")]
    public static void ApplyUpperTuning() => Apply();

    [MenuItem("Tools/Medal Pusher/Validate JP Growth Rates")]
    public static void ValidateGrowth()
    {
        var obj = new GameObject("TemporaryJPGrowthCheck");
        obj.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var c = obj.AddComponent<MedalSlotJackpotController>();
            c.enabled = false;
            var method = typeof(MedalSlotJackpotController).GetMethod("OnMedalInserted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int red = c.jackpotPools[0], blue = c.jackpotPools[1], yellow = c.jackpotPools[2];
            for (int i = 0; i < 99; i++) method.Invoke(c, null);
            if (c.jackpotPools[0] != red) throw new InvalidOperationException("Red rose before 100 paid medals.");
            method.Invoke(c, null);
            if (c.jackpotPools[0] != red + 1) throw new InvalidOperationException("Red failed at 100 paid medals.");
            for (int i = 0; i < 100; i++) method.Invoke(c, null);
            if (c.jackpotPools[0] != red + 2 || c.jackpotPools[1] != blue || c.jackpotPools[2] != yellow)
                throw new InvalidOperationException("Paid-medal JP rates incorrect.");
            Debug.Log("[MedalPusher] JP growth paid-input boundaries 99/100/200: PASS.");
        }
        finally { UnityEngine.Object.DestroyImmediate(obj); }
    }

    [MenuItem("Tools/Medal Pusher/Apply Requested Upper Layout")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before changing the upper layout.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.path != MedalPusherSceneBuilder.ScenePath)
            throw new InvalidOperationException("Open the medal pusher scene first.");
        var controller = UnityEngine.Object.FindFirstObjectByType<MedalSlotJackpotController>();
        if (controller == null || controller.game == null)
            throw new InvalidOperationException("Medal pusher controller missing.");
        var field = controller.game.transform.Find("GeneratedPlayfield");
        controller.upperStation = MedalLotteryStationBuilder.BuildUpperStation(field);
        MedalPusherExpansionBuilder.ApplyClickPlacementAndBallOnlyToScene(scene, controller.game);
        EditorUtility.SetDirty(controller);
        MedalPusherExpansionBuilder.Validate();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[MedalPusher] Requested upper layout applied and validated.");
    }
}
