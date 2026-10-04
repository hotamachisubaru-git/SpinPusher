using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Runs a real Play-mode integration check and restores edit mode.</summary>
[InitializeOnLoad]
public static class MedalPusherPlayCheck
{
    private const string Key = "MedalPusher.PlayCheck";
    private static int stage;
    private static double started;
    private static float firstZ, minZ, maxZ;
    private static MedalPusherGame game;
    private static PrizeDropManager manager;
    private static MedalItem triggerMedal, triggerPrize, sampleMedal;
    private static Vector3 sampleStart;
    private static Keyboard testKeyboard;
    private static int inputBalance;
    private static float normalDropHeight;
    private static readonly Dictionary<string, object> results = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<string> editorIssues = new List<string>();

    static MedalPusherPlayCheck()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Inspect Play Check")]
    public static void Inspect()
    {
        MedalPusherSceneBuilder.WriteReport("play-state.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused,
            pending = EditorApplication.isPlayingOrWillChangePlaymode, compiling = EditorApplication.isCompiling,
            requested = SessionState.GetBool(Key, false), stage, gameTime = Time.time,
            items = UnityEngine.Object.FindObjectsByType<MedalItem>(FindObjectsSortMode.None).Length,
            score = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>()?.score
        }, Newtonsoft.Json.Formatting.Indented));
    }

    [MenuItem("Tools/Medal Pusher/Stop Play Mode")]
    public static void Stop()
    {
        SessionState.SetBool(Key, false);
        if (testKeyboard != null) { InputSystem.RemoveDevice(testKeyboard); testKeyboard = null; }
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = false;
    }

    [MenuItem("Tools/Medal Pusher/Run Play Check")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
        errors.Clear(); editorIssues.Clear(); results.Clear(); stage = 0;
        MedalPusherSceneBuilder.Validate();
        SessionState.SetBool(Key, true);
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (stack.Contains("Unity.AI.ModelSelector")) { editorIssues.Add(message); return; }
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            if (!message.StartsWith("[Nexus") && !message.StartsWith("[Synaptic") && !message.StartsWith("[MCP")) errors.Add(message);
    }

    private static void Check(string name, bool passed)
    { results[name] = passed; if (!passed) errors.Add("Failed: " + name); }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        // Console Error Pause can be triggered by unrelated editor service failures.
        // Keep the bounded test running while retaining game errors in the report.
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        try
        {
            if (stage == 0)
            {
                game = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>();
                if (game == null || Time.time < .5f) return;
                results.Clear();
                Application.runInBackground = true;
                manager = game.GetComponent<PrizeDropManager>();
                game.GetComponent<MedalInputHandler>().enabled = false;
                manager.enabled = false;
                manager.bonusMedalChance = 0;
                normalDropHeight = game.dropHeight;
                // Disable distant-fall cleanup until the real collection trigger is checked.
                game.dropHeight = -100f;
                firstZ = minZ = maxZ = game.pusherBody.position.z;
                started = Time.time;
                sampleMedal = game.itemsRoot.GetComponentsInChildren<MedalItem>().FirstOrDefault(i => !i.isPrize && i.name == "Medal_6_5");
                if (sampleMedal != null) sampleStart = sampleMedal.transform.position;

                int medals = game.medals, count = game.CountBoardItems(false);
                game.ThrowSingleMedal();
                Check("oneThrowConsumesOne", game.medals == medals - 1 && game.CountBoardItems(false) == count + 1);
                Check("dropSoundPlaying", game.GetComponent<AudioSource>().isPlaying);
                var ui = UnityEngine.Object.FindFirstObjectByType<MedalPusherUI>();
                Check("balanceUIUpdated", ui != null && ui.medalsText.text == game.medals.ToString("D4"));

                // Call the same award twice in one frame to verify compound-collider safety.
                var duplicate = UnityEngine.Object.Instantiate(game.medalPrefab, new Vector3(0, 1, -5), Quaternion.identity, game.itemsRoot);
                game.RegisterItem(duplicate.GetComponent<MedalItem>());
                int beforeScore = game.score, beforeMedals = game.medals;
                manager.CheckMedalDrop(duplicate, duplicate.transform.position);
                manager.CheckMedalDrop(duplicate, duplicate.transform.position);
                Check("medalAwardExactlyOnce", game.score == beforeScore + manager.scorePerMedal && game.medals == beforeMedals + 1);
                var prize = UnityEngine.Object.Instantiate(game.prizePrefabs[1], new Vector3(0, 1, -5), Quaternion.identity, game.itemsRoot);
                var prizeItem = prize.GetComponent<MedalItem>();
                game.RegisterItem(prizeItem);
                beforeScore = game.score; beforeMedals = game.medals;
                manager.CheckPrizeDrop(prize, prize.transform.position);
                manager.CheckPrizeDrop(prize, prize.transform.position);
                Check("prizeAwardExactlyOnce", game.score == beforeScore + prizeItem.pointValue && game.medals == beforeMedals + manager.medalsPerPrize);
                Check("prizeSoundPlaying", game.GetComponent<AudioSource>().isPlaying);
                Check("prizeUIUpdated", ui != null && ui.prizeDisplay.text.Contains("Sapphire"));

                // Real gravity/trigger path, beyond the board's open front edge.
                var fallingMedal = UnityEngine.Object.Instantiate(game.medalPrefab, new Vector3(1.5f, 1, -5.1f), Quaternion.identity, game.itemsRoot);
                triggerMedal = fallingMedal.GetComponent<MedalItem>();
                game.RegisterItem(triggerMedal);
                var fallingPrize = UnityEngine.Object.Instantiate(game.prizePrefabs[0], new Vector3(-1.5f, 1.4f, -5.1f), Quaternion.identity, game.itemsRoot);
                triggerPrize = fallingPrize.GetComponent<MedalItem>();
                game.RegisterItem(triggerPrize);
                results["initialBoardMedals"] = game.CountBoardItems(false);
                results["initialBoardPrizes"] = game.CountBoardItems(true);
                stage = 1;
            }
            if (stage == 1)
            {
                minZ = Mathf.Min(minZ, game.pusherBody.position.z);
                maxZ = Mathf.Max(maxZ, game.pusherBody.position.z);
                double elapsed = Time.time - started;
                if (elapsed > 3 && !results.ContainsKey("realTriggerCollectedMedal"))
                {
                    Check("realTriggerCollectedMedal", triggerMedal == null || triggerMedal.collected);
                    Check("realTriggerCollectedPrize", triggerPrize == null || triggerPrize.collected);
                    game.dropHeight = normalDropHeight;
                    game.StartThrowing();
                }
                if (elapsed > 5 && !results.ContainsKey("keyboardHoldStarted"))
                {
                    game.StopThrowing();
                    game.GetComponent<MedalInputHandler>().enabled = true;
                    testKeyboard = InputSystem.AddDevice<Keyboard>("MedalPlayCheckKeyboard");
                    testKeyboard.MakeCurrent();
                    InputState.Change(testKeyboard, new KeyboardState(UnityEngine.InputSystem.Key.Space), InputUpdateType.Dynamic);
                    inputBalance = game.medals;
                    results["keyboardHoldStarted"] = true;
                }
                if (elapsed > 7 && !results.ContainsKey("keyboardHoldDropsMedals"))
                {
                    Check("keyboardHoldDropsMedals", game.medals < inputBalance);
                    testKeyboard.MakeCurrent();
                    InputState.Change(testKeyboard, new KeyboardState(), InputUpdateType.Dynamic);
                    inputBalance = game.medals;
                }
                if (elapsed > 8 && !results.ContainsKey("keyboardReleaseStopsThrowing"))
                {
                    // Natural awards can increase the balance during this interval.
                    bool throwing = (bool)typeof(MedalPusherGame).GetField("isThrowing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(game);
                    results["releaseKeyboardIsTestDevice"] = Keyboard.current == testKeyboard;
                    results["releaseKeyboardSpacePressed"] = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
                    results["releaseMousePressed"] = Mouse.current != null && Mouse.current.leftButton.isPressed;
                    results["releaseStillThrowing"] = throwing;
                    Check("keyboardReleaseStopsThrowing", !throwing && game.medals >= inputBalance - 1);
                    InputSystem.RemoveDevice(testKeyboard); testKeyboard = null;
                    game.GetComponent<MedalInputHandler>().enabled = false;
                    var button = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).First(b => b.name == "DropMedalButton");
                    int before = game.medals, itemCount = game.CountBoardItems(false);
                    button.onClick.Invoke();
                    Check("uiButtonConnected", game.medals == before - 1 && game.CountBoardItems(false) == itemCount + 1);
                    var view = UnityEngine.Object.FindFirstObjectByType<MedalPusherCameraView>();
                    view.ToggleView(); Check("cabinetViewSwitches", view.overview); view.ToggleView();
                    game.StartThrowing();
                }
                if (elapsed < 14) return;
                game.StopThrowing();
                Check("pusherMovesBothDirections", maxZ - minZ > game.pusherRange * 1.8f);
                Check("boardItemLimit", game.CountBoardItems(false) <= game.maxMedalsOnBoard && game.CountBoardItems(true) <= game.maxPrizesOnBoard);
                Check("physicalMedalPushedForward", sampleMedal == null || sampleMedal.collected || sampleMedal.transform.position.z < sampleStart.z - .06f);
                int limit = game.maxMedalsOnBoard, balance = game.medals, boardCount = game.CountBoardItems(false);
                typeof(MedalPusherGame).GetField("throwTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(game, 0f);
                game.maxMedalsOnBoard = boardCount;
                game.ThrowSingleMedal();
                Check("fullBoardDoesNotConsumeMedals", game.medals == balance && game.CountBoardItems(false) == boardCount);
                game.maxMedalsOnBoard = limit;
                game.medals = 0; game.ThrowSingleMedal();
                Check("zeroBalanceDoesNotSpawn", game.medals == 0 && game.CountBoardItems(false) == boardCount);
                game.AddMedals(balance);
                results["pusherMinZ"] = minZ; results["pusherMaxZ"] = maxZ;
                results["score"] = game.score; results["balance"] = game.medals;
                results["remainingMedals"] = game.CountBoardItems(false);
                results["remainingPrizes"] = game.CountBoardItems(true);
                results["playSeconds"] = Time.time;
                results["success"] = errors.Count == 0;
                results["errors"] = errors.ToArray();
                results["editorServiceIssues"] = editorIssues.Distinct().ToArray();
                MedalPusherSceneBuilder.WriteReport("play-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
                SessionState.SetBool(Key, false);
                stage = 0;
                Debug.Log("[MedalPusher] Play check complete: " + (errors.Count == 0 ? "PASS" : "FAIL"));
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception ex)
        {
            if (testKeyboard != null) { InputSystem.RemoveDevice(testKeyboard); testKeyboard = null; }
            errors.Add(ex.ToString());
            results["success"] = false; results["errors"] = errors.ToArray();
            MedalPusherSceneBuilder.WriteReport("play-validation.json", Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
            SessionState.SetBool(Key, false); stage = 0; EditorApplication.isPlaying = false;
            Debug.LogException(ex);
        }
    }
}
