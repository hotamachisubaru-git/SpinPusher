using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Bounded Play fixture exercising production slot outcomes and native lottery contacts.</summary>
[InitializeOnLoad]
public static class MedalSlotMotionPlayCheck
{
    private const string Key = "MedalPusher.SlotMotionCheck";
    private const string StartKey = Key + ".Started";
    private const string BackgroundKey = Key + ".Background";
    private const string UtcKey = Key + ".Utc";
    private const double MaximumSeconds = 80;
    private enum Phase { Boot, RefundWait, RefundSpawn, DirectWait, Guard, Stall, Motion, Port, Pause, Result, Out, QueuedDirect, SecondOut, LostStart, LostWait, Payout, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalBallLotteryStation upper, lower;
    private static MedalArcadeUI ui;
    private static MedalArcadeSettings settings;
    private static LotteryBallToken originalBall, lowerBall;
    private static MedalLotteryPocket chosenPocket;
    private static readonly Dictionary<string, object> checks = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> outcomes = new List<object>();
    private static readonly List<object> contacts = new List<object>();
    private static readonly List<object> visits = new List<object>();
    private static readonly Dictionary<Behaviour, bool> enabledStates = new Dictionary<Behaviour, bool>();
    private static readonly Dictionary<Collider, bool> pocketStates = new Dictionary<Collider, bool>();
    private static bool initialized, originalPersistence;
    private static string realSettingsPath, realSettingsHash, originalOverride, overridePath, ballId;
    private static float stageAt, fixedAt, pausedElapsed;
    private static double phaseAt;
    private static int ticket, wallet, visitWallet, recoveryBaseline, visitRecovery, upperEvents, colorEvents, upperWin, colorPayout, visitIndex, expectedPrize;
    private static bool colorJackpot;
    private static MedalJackpotKind finishedKind;
    private static Quaternion[] startAngles;
    private static Vector3 recoveryPosition;
    private static UnityEngine.Random.State randomState;

    static MedalSlotMotionPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlay;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Slot And Upper Motion Play Check")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play mode before the slot/motion check.");
        Reset();
        foreach (string other in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck", "MedalPusher.UpperFlowCheck", "MedalPusher.IntegratedUpperFlowCheck", "MedalPusher.ClickBallOnlyCheck" })
            SessionState.SetBool(other, false);
        SessionState.SetFloat(StartKey, (float)Now);
        SessionState.SetString(UtcKey, DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(BackgroundKey, Application.runInBackground);
        Application.runInBackground = true;
        SessionState.SetBool(Key, true);
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Medal Pusher/Inspect Slot And Upper Motion Play Check")]
    public static void Inspect() => Write("slot-motion-play-state.json", new {
        timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false),
        playing = EditorApplication.isPlaying, phase = phase.ToString(), elapsedSeconds = Elapsed,
        upperEvents, colorEvents, visitIndex, errors,
        controller = controller == null ? null : new { controller.LiveUpperWin, controller.IsUpperDrawing, controller.IsColorRoundActive,
            controller.PendingBallDraws, controller.PendingBallRefunds, controller.PendingDirectJpcDraws, controller.IsHighProbability },
        station = upper == null ? null : new { upper.RecoveryCount, upper.UpperStagnantSeconds, upper.UpperHorizontalSpeed,
            upper.ColorGatesOpen, upper.BumperWin, upper.TotalBumperHits, upper.IsUpperSuspended, upper.UsedOutBlockCount }
    });

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartKey, (float)Now);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Check(string name, bool value)
    {
        checks[name] = value;
        if (!value) errors.Add("Failed: " + name);
    }
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void SetHigh(bool high) => Set(controller, "<IsHighProbability>k__BackingField", high);
    private static void Disable(Behaviour component)
    {
        if (component == null) return;
        if (!enabledStates.ContainsKey(component)) enabledStates[component] = component.enabled;
        component.enabled = false;
    }
    private static void Reset()
    {
        phase = Phase.Boot; initialized = false; game = null; controller = null; upper = lower = null; ui = null; settings = null;
        originalBall = lowerBall = null; upperEvents = colorEvents = visitIndex = 0;
        checks.Clear(); errors.Clear(); outcomes.Clear(); contacts.Clear(); visits.Clear(); enabledStates.Clear(); pocketStates.Clear();
        realSettingsPath = realSettingsHash = originalOverride = overridePath = null;
    }
    private static void OnPlay(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) IsolateSettings();
        if (state == PlayModeStateChange.ExitingPlayMode) { errors.Add("Play mode stopped before completion."); Finish(false); }
    }
    private static void IsolateSettings()
    {
        if (realSettingsPath != null) return;
        settings = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettings>();
        if (settings == null) return;
        originalOverride = settings.SettingsFileOverride; originalPersistence = settings.persistSettings;
        realSettingsPath = Path.Combine(Application.persistentDataPath, "medal-arcade-settings.json");
        realSettingsHash = Hash(realSettingsPath);
        overridePath = Path.Combine(Path.GetTempPath(), "medal-slot-motion-" + Guid.NewGuid().ToString("N") + ".json");
        settings.SettingsFileOverride = overridePath; settings.persistSettings = false;
    }
    private static bool Ready()
    {
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>();
        controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        ui = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
        return game != null && controller != null && ui != null && controller.upperStation != null &&
            game.OnMedalInserted != null && game.OnMedalInserted.GetInvocationList().Any(d => ReferenceEquals(d.Target, controller)) &&
            controller.OnStateChanged != null && controller.OnStateChanged.GetInvocationList().Any(d => ReferenceEquals(d.Target, ui));
    }
    private static void Setup()
    {
        IsolateSettings(); Require(settings != null, "Settings fixture missing.");
        randomState = UnityEngine.Random.state;
        Disable(game); Disable(controller);
        foreach (var input in UnityEngine.Object.FindObjectsByType<MedalInputHandler>(FindObjectsSortMode.None)) Disable(input);
        foreach (var pocket in UnityEngine.Object.FindObjectsByType<MedalSlotPocket>(FindObjectsSortMode.None)) Disable(pocket);
        foreach (var manager in UnityEngine.Object.FindObjectsByType<PrizeDropManager>(FindObjectsSortMode.None)) Disable(manager);
        ClearBoard(); ClearDraws(); ClearPayouts();
        Set(game, "<TotalPaidMedals>k__BackingField", 0L); Set(game, "<TotalReturnedMedals>k__BackingField", 0L);
        controller.targetPayoutPercent = 90; controller.rescueSpins = 0;
        Set(controller, "<SpinCredits>k__BackingField", 0); Set(controller, "nextLotteryAt", float.PositiveInfinity);
        upper = controller.upperStation;
        controller.OnUpperLotteryFinished += OnUpperFinished;
        controller.OnLotteryFinished += OnColorFinished;
        var basic = UnityEngine.Object.FindAnyObjectByType<MedalPusherUI>();
        var camera = UnityEngine.Object.FindAnyObjectByType<MedalPusherCameraView>();
        Check("playStartsWithFrontCamera", camera != null && !camera.overview && Camera.main != null && Mathf.Abs(Camera.main.transform.position.x) < .001f);
        Check("comboNotificationRemoved", basic != null && (basic.comboPanel == null || !basic.comboPanel.activeSelf) &&
            (basic.comboText == null || string.IsNullOrEmpty(basic.comboText.text)) &&
            !UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None).Any(t => t.text.Contains("連続獲得")));
        Check("headerReelsAndPayoutAssigned", ui.reelsText != null && ui.payoutRemainingText != null &&
            ui.reelsText.GetComponent<RectTransform>().anchorMin.y >= .91f);
        Check("worldWinCaptionUsesRequestedFormat", upper.upperWinText != null && upper.upperWinText.text == "00WIN（枚獲得）");
        Check("redBlueGreenSymbolPalette", new[] { 1, 3, 5, 9 }.All(n => MedalArcadeUI.FormatSlotSymbol(n).Contains("#FF526C")) &&
            new[] { 2, 4, 6, 8 }.All(n => MedalArcadeUI.FormatSlotSymbol(n).Contains("#55C4FF")) && MedalArcadeUI.FormatSlotSymbol(0).Contains("#57FF76"));
        TestSlotOutcomes();
        initialized = true;
        controller.slotBallChancePercent = 100; controller.slotMedalChancePercent = 0; SetHigh(false);
        game.maxPrizesOnBoard = 0; wallet = game.medals;
        int seed = FindSeed("ball", controller.EffectiveSlotBallChancePercent, controller.EffectiveSlotMedalChancePercent, 0);
        Spin(seed);
        Check("ballWinOnFullBoardKeepsPhysicalRefundAndNoDrawTicket", controller.PendingBallRefunds == 1 && controller.PendingBallDraws == 0 &&
            controller.PendingDirectJpcDraws == 0 && game.medals == wallet && !controller.LastSlotWasDirectJpc && controller.LastSlotSymbols.All(n => n == 0));
        controller.enabled = true;
        Enter(Phase.RefundWait);
    }
    private static void TestSlotOutcomes()
    {
        controller.slotBallChancePercent = 0; controller.slotMedalChancePercent = 40; SetHigh(false);
        float normal = controller.EffectiveSlotWinChancePercent;
        foreach (int digit in new[] { 1, 2, 3, 4, 5, 6, 9, 8 })
        {
            int seed = FindSeed("digit", 0, controller.EffectiveSlotMedalChancePercent, digit);
            int before = game.medals;
            bool wasHigh = controller.IsHighProbability;
            Spin(seed);
            bool odd = (digit & 1) == 1;
            int expected = digit * 10 + (wasHigh && odd ? 5 : 0);
            Check("matching" + digit + (odd ? "EntersHighProbability" : "EndsHighProbability"), controller.LastSlotWasWin &&
                controller.LastSlotSymbols.All(n => n == digit) && controller.IsHighProbability == odd &&
                controller.LastSlotPayout == expected && game.medals == before + expected &&
                Mathf.Abs(controller.EffectiveSlotWinChancePercent - (odd ? normal * 1.5f : normal)) < .001f);
        }
        SetHigh(true); int miss = FindSeed("miss", 0, controller.EffectiveSlotMedalChancePercent, 0); int missWallet = game.medals;
        Spin(miss);
        Check("missDoesNotChangeHighProbabilityOrReward", !controller.LastSlotWasWin && controller.IsHighProbability && game.medals == missWallet &&
            controller.LastSlotSymbols.Distinct().Count() > 1);
        controller.slotBallChancePercent = 60; controller.slotMedalChancePercent = 40; SetHigh(false);
        Check("normalTotalChanceCapsAt95", Mathf.Abs(controller.EffectiveSlotWinChancePercent - 95) < .001f);
        SetHigh(true); Check("highTotalChanceCapsAt99", Mathf.Abs(controller.EffectiveSlotWinChancePercent - 99) < .001f);
        controller.targetPayoutPercent = 0; controller.rescueSpins = 1; int beforeZero = game.medals;
        for (int i = 0; i < 4; i++) Spin(8400 + i);
        Check("payoutZeroDisablesWinsAndRescueEvenDuringHighProbability", !controller.LastSlotWasWin && game.medals == beforeZero &&
            controller.PendingBallDraws == 0 && controller.PendingBallRefunds == 0 && controller.EffectiveSlotWinChancePercent == 0);
        controller.targetPayoutPercent = 90; controller.rescueSpins = 0; SetHigh(false);
    }
    private static int FindSeed(string result, float ballChance, float medalChance, int digit)
    {
        var saved = UnityEngine.Random.state;
        try
        {
            for (int seed = 1; seed < 200000; seed++)
            {
                UnityEngine.Random.InitState(seed); float outcome = UnityEngine.Random.Range(0f, 100f);
                bool ball = outcome < ballChance; bool seven = ball && UnityEngine.Random.value < 9f / 49f;
                if (result == "seven" && seven || result == "ball" && ball && !seven) return seed;
                if (result == "miss" && outcome >= ballChance + medalChance) return seed;
                if (result == "digit" && !ball && outcome < ballChance + medalChance)
                {
                    int n = UnityEngine.Random.Range(1, 9); if (n >= 7) n++;
                    if (n == digit) return seed;
                }
            }
            throw new InvalidOperationException("No deterministic production seed found: " + result + " / " + digit);
        }
        finally { UnityEngine.Random.state = saved; }
    }
    private static void Spin(int seed)
    {
        UnityEngine.Random.InitState(seed); Invoke(controller, "CompleteSpin"); ui.Refresh();
        outcomes.Add(new { seed, symbols = controller.LastSlotSymbols.ToArray(), controller.LastSlotWasWin, controller.LastSlotWasDirectJpc,
            controller.LastSlotPayout, controller.IsHighProbability, chance = controller.EffectiveSlotWinChancePercent });
    }
    private static void StartDirect()
    {
        wallet = game.medals; int board = game.CountBoardItems(true);
        int expectedSlotPayout = controller.IsHighProbability ? 75 : 70;
        Spin(FindSeed("seven", controller.EffectiveSlotBallChancePercent, controller.EffectiveSlotMedalChancePercent, 7));
        Check("sevenPaysItsDigitPrizeAndQueuesDirectJpcWithoutBoardBall", controller.LastSlotWasDirectJpc && controller.IsHighProbability &&
            controller.LastSlotSymbols.All(n => n == 7) && game.medals == wallet + expectedSlotPayout && controller.LastSlotPayout == expectedSlotPayout && game.CountBoardItems(true) == board &&
            controller.PendingBallRefunds == 0 && controller.PendingDirectJpcDraws == 1);
        wallet = game.medals;
        var character = ui.reelsText.textInfo.characterInfo.First(c => c.character == '7' && c.isVisible);
        var colors = ui.reelsText.textInfo.meshInfo[character.materialReferenceIndex].colors32;
        Check("sevenHasRainbowVertexColors", Enumerable.Range(0, 4).Select(i => colors[character.vertexIndex + i].ToString()).Distinct().Count() >= 3);
        Set(controller, "nextLotteryAt", 0f); Enter(Phase.DirectWait);
    }
    private static void Enter(Phase value) { phase = value; phaseAt = Now; stageAt = Time.time; }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            Require(Elapsed < MaximumSeconds, "Slot/motion check exceeded 80 seconds.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (!initialized) { if (!Ready()) { Require(Elapsed < 20, "Game Start readiness timeout."); return; } Setup(); return; }
            Require(Now - phaseAt < 12, "Phase timed out: " + phase);
            switch (phase)
            {
                case Phase.RefundWait:
                    if (Time.time - stageAt < .4f) return;
                    Check("fullBoardRefundNeverAutoStartsUpper", controller.PendingBallRefunds == 1 && controller.PendingBallDraws == 0 && !controller.IsUpperDrawing);
                    game.maxPrizesOnBoard = 20; Enter(Phase.RefundSpawn); break;
                case Phase.RefundSpawn:
                    if (controller.PendingBallRefunds != 0) return;
                    var physical = game.itemsRoot.GetComponentsInChildren<MedalItem>().Where(i => i.isBall && !i.collected).ToArray();
                    Check("releasedCapacitySpawnsOnePhysicalGreenRefund", physical.Length == 1 && Green(physical[0].gameObject) &&
                        controller.PendingBallDraws == 0 && !controller.IsUpperDrawing && game.medals == wallet);
                    ClearBoard(); StartDirect(); break;
                case Phase.DirectWait:
                    if (upper.ActiveBall == null) return;
                    originalBall = upper.ActiveBall; ticket = upper.CurrentTicket; ballId = originalBall.GetEntityId().ToString();
                    originalBall.gameObject.AddComponent<MedalSlotMotionContactProbe>(); Park();
                    Check("directJpcStarts100WinWithOpenGatesWithoutHits", controller.LiveUpperWin == 100 && upper.BumperWin == 100 && upper.InitialWin == 100 &&
                        upper.TotalBumperHits == 0 && upper.DirectJpcGatesUnlocked && upper.ColorGatesOpen && controller.AreColorGatesUnlocked &&
                        controller.IsDirectJpcUpperRound && upper.UsedOutBlockCount == 0 && game.medals == wallet);
                    Check("liveWorldAndHudWinCaptionsShow100", upper.upperWinText.text == "100WIN（枚獲得）" && ui.upperWinText.text.Contains("100WIN（枚獲得）"));
                    Check("physicalCabinetReelsShowCurrentSlotSymbols", ui.worldReelsText != null && ui.worldReelsText.text.Contains("7"));
                    Spin(FindSeed("seven", controller.EffectiveSlotBallChancePercent, controller.EffectiveSlotMedalChancePercent, 7));
                    Check("busyUpperKeepsQueuedDirectJpcPromise", controller.PendingBallDraws == 2 && controller.PendingDirectJpcDraws == 1 &&
                        SameBall() && upper.BumperWin == 100 && game.medals == wallet + 75 && controller.LastSlotPayout == 75);
                    wallet = game.medals;
                    startAngles = controller.stations.Select(s => s.rotor.rotation).ToArray();
                    IncomingGuard(); Enter(Phase.Guard); break;
                case Phase.Guard:
                    if (!upper.IsOutBlockUsed(0)) return;
                    Check("nativeGuardContactUsesOnePlate", contacts.Any(c => c.ToString().Contains("guard")) && upper.UsedOutBlockCount == 1 && SameBall() && upper.BumperWin == 100);
                    Freeze(); recoveryBaseline = upper.RecoveryCount; Set(upper, "startedAt", Time.time - 70f);
                    Enter(Phase.Stall); break;
                case Phase.Stall:
                    if (upper.RecoveryCount == recoveryBaseline) return;
                    Check("stalledBallRelaunchesAtCenterWithWinTicketGateAndGuardIntact", SameBall() && upper.RecoveryCount == recoveryBaseline + 1 &&
                        upper.LastRecoveryWin == 100 && upper.LastRecoveryTicket == ticket && upper.LastRecoveryBallInstanceId == ballId &&
                        upper.LastRecoveryUsedOutBlockCount == 1 && upper.LastRecoveryColorGatesOpen && upper.UsedOutBlockCount == 1 &&
                        upper.BumperWin == 100 && controller.LiveUpperWin == 100 && upperEvents == 0 && game.medals == wallet);
                    Check("upperLongElapsedDoesNotCauseTimeout", upper.UpperElapsedSeconds >= 70 && upper.IsDrawing && !upper.LastTimedOut);
                    recoveryPosition = originalBall.transform.position; Enter(Phase.Motion); break;
                case Phase.Motion:
                    if (Time.time - stageAt < .10f) return;
                    Check("recoveredBallMovesDynamically", SameBall() && !originalBall.GetComponent<Rigidbody>().isKinematic &&
                        Vector3.Distance(recoveryPosition, originalBall.transform.position) > .025f && upper.UpperHorizontalSpeed > .3f);
                    Check("allThreeColoredStationsRotateForDirect100", controller.stations.All(s => s.IsRotating) &&
                        controller.stations.Select((s, i) => Quaternion.Angle(startAngles[i], s.rotor.rotation) > 2).All(v => v));
                    BeginVisit(); break;
                case Phase.Port:
                    if (!upper.IsUpperSuspended || lower.ActiveBall == null) return;
                    lowerBall = lower.ActiveBall; lowerBall.gameObject.AddComponent<MedalSlotMotionContactProbe>();
                    Hold(lowerBall.GetComponent<Rigidbody>(), lower.transform.TransformPoint(lower.guideLocalCenter + Vector3.up * 3));
                    pausedElapsed = upper.UpperElapsedSeconds; visitRecovery = upper.RecoveryCount;
                    Check("actualColorPortSuspendsOriginalDirectBall", SameBall() && controller.ActiveKind == lower.kind &&
                        upper.LastColorPocket != null && upper.LastColorPocket.kind == lower.kind && upper.BumperWin == 100 && upper.UsedOutBlockCount == 1);
                    Enter(Phase.Pause); break;
                case Phase.Pause:
                    if (Time.time - stageAt < 1.8f) return;
                    Check("suspendedUpperDoesNotRecoverDuringColorVisit_" + visitIndex, SameBall() && upper.IsUpperSuspended &&
                        upper.RecoveryCount == visitRecovery && Mathf.Abs(upper.UpperElapsedSeconds - pausedElapsed) < .005f && upper.BumperWin == 100);
                    chosenPocket.GetComponent<Collider>().enabled = true;
                    Place(lowerBall.GetComponent<Rigidbody>(), chosenPocket.GetComponent<Collider>().bounds.center);
                    Enter(Phase.Result); break;
                case Phase.Result:
                    if (colorEvents < visitIndex + 1 || upper.IsUpperSuspended) return;
                    Check("actual" + (visitIndex == 1 ? "Jackpot" : "Medal") + "ResultReturnsSame100WinBall", SameBall() && colorPayout == expectedPrize &&
                        colorJackpot == (visitIndex == 1) && finishedKind == lower.kind && lower.LastPocket == chosenPocket &&
                        upper.BumperWin == 100 && controller.LiveUpperWin == 100 && upper.UsedOutBlockCount == 1 &&
                        upper.ColorGatesOpen && controller.AreColorGatesUnlocked && game.medals == visitWallet + expectedPrize && upperEvents == 0);
                    visits.Add(new { index = visitIndex, kind = lower.kind.ToString(), jackpot = colorJackpot, payout = colorPayout, upperTicket = ticket, tokenId = ballId,
                        win = upper.BumperWin, usedGuards = upper.OutBlockUsedStates.ToArray(), recoveryCount = upper.RecoveryCount });
                    RestorePockets(); visitIndex++;
                    if (visitIndex < 2) BeginVisit(); else { wallet = game.medals; RouteOut(); Enter(Phase.Out); }
                    break;
                case Phase.Out:
                    if (upperEvents < 1) return;
                    Check("actualOutPays100OnceAndNeverRecovers", upperEvents == 1 && upperWin == 100 && game.medals == wallet + 100 &&
                        !controller.IsUpperDrawing && upper.LastExitedBowl && !upper.LastTimedOut && upper.RecoveryCount == recoveryBaseline + 1);
                    int paid = game.medals; controller.FinishLottery(upper, ticket, false, 100);
                    Check("duplicateEndedUpperCannotPayAgain", game.medals == paid && upperEvents == 1);
                    Enter(Phase.QueuedDirect); break;
                case Phase.QueuedDirect:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null || ReferenceEquals(upper.ActiveBall, originalBall)) return;
                    Check("queuedSevenStartsNew100WinWithFreshGuards", upper.BumperWin == 100 && upper.TotalBumperHits == 0 && upper.UsedOutBlockCount == 0 &&
                        upper.ColorGatesOpen && controller.AreColorGatesUnlocked && upper.CurrentTicket != ticket && controller.PendingDirectJpcDraws == 0 &&
                        controller.UpperWinCarriedAtStart == 100 && controller.UpperWinEarnedThisRound == 0);
                    originalBall = upper.ActiveBall; originalBall.gameObject.AddComponent<MedalSlotMotionContactProbe>();
                    wallet = game.medals; recoveryBaseline = upper.RecoveryCount; RouteOut(); Enter(Phase.SecondOut); break;
                case Phase.SecondOut:
                    if (upperEvents < 2) return;
                    Check("secondActualOutDoesNotRecoverOrPayCarriedWinTwice", upperEvents == 2 && upperWin == 0 && game.medals == wallet &&
                        upper.RecoveryCount == recoveryBaseline && !controller.IsUpperDrawing);
                    Spin(FindSeed("seven", controller.EffectiveSlotBallChancePercent, controller.EffectiveSlotMedalChancePercent, 7));
                    Set(controller, "nextLotteryAt", 0f); Enter(Phase.LostStart); break;
                case Phase.LostStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    wallet = game.medals; ticket = upper.CurrentTicket;
                    UnityEngine.Object.Destroy(upper.ActiveBall.gameObject); Enter(Phase.LostWait); break;
                case Phase.LostWait:
                    if (controller.IsUpperDrawing) return;
                    Check("externallyLostUpperBallEndsOnceWithoutRepayingCarriedWinOrPermanentBusy", upperEvents == 3 && upperWin == 0 &&
                        game.medals == wallet && upper.LastTimedOut && !controller.AreColorGatesUnlocked &&
                        controller.stations.All(s => !s.IsDrawing && !s.IsRotating));
                    int lostPaid = game.medals; controller.FinishLottery(upper, ticket, false, 100);
                    Check("lostUpperDuplicateCallbackCannotPayAgain", game.medals == lostPaid && upperEvents == 3);
                    ClearPayouts(); ClearBoard(); game.QueuePayoutMedals(7, false); ui.Refresh();
                    Check("payoutShowsRemainingQueueAboveMedals", ui.payoutRemainingText.text.Contains("07") &&
                        ui.payoutRemainingText.GetComponent<RectTransform>().anchorMin.y > 0 && game.PendingPayoutMedals == 7);
                    game.enabled = true; Set(game, "bonusSpawnTimer", 0f); Enter(Phase.Payout); break;
                case Phase.Payout:
                    if (game.PendingPayoutMedals >= 7) return;
                    game.enabled = false; ui.Refresh();
                    Check("payoutUpdatesWhenRealMedalSpawns", game.PendingPayoutMedals < 7 && ui.payoutRemainingText.text.Contains(game.PendingPayoutMedals.ToString("D2")) &&
                        game.itemsRoot.GetComponentsInChildren<MedalItem>().Any(i => !i.isBall && i.payoutAlreadyCredited));
                    phase = Phase.Complete; Finish(true); break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }
    private static bool SameBall() => originalBall != null && ReferenceEquals(originalBall, upper.ActiveBall) &&
        originalBall.ticket == ticket && upper.CurrentTicket == ticket && !originalBall.IsConsumed;
    private static void Place(Rigidbody body, Vector3 position)
    {
        body.isKinematic = false; body.detectCollisions = true; body.useGravity = true;
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.position = position;
        Physics.SyncTransforms(); fixedAt = Time.fixedTime;
    }
    private static void Hold(Rigidbody body, Vector3 position) { Place(body, position); body.isKinematic = true; }
    private static void Park() { if (originalBall != null) Hold(originalBall.GetComponent<Rigidbody>(), upper.transform.TransformPoint(upper.guideLocalCenter + Vector3.up * 3)); }
    private static void Freeze() => Hold(originalBall.GetComponent<Rigidbody>(), upper.transform.TransformPoint(new Vector3(1.55f, .75f, upper.guideLocalCenter.z)));
    private static void IncomingGuard()
    {
        var c = upper.outBlockBodies[0].GetComponent<Collider>(); Vector3 normal = c.transform.forward.normalized;
        float radius = originalBall.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = c.ClosestPoint(c.bounds.center + normal * (c.bounds.extents.magnitude + 1));
        Place(originalBall.GetComponent<Rigidbody>(), face + normal * (radius + .025f));
        originalBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
    }
    private static void BeginVisit()
    {
        Park(); lower = controller.stations[0]; visitWallet = game.medals;
        controller.jackpotPools[0] = 123;
        chosenPocket = lower.GetComponentsInChildren<MedalLotteryPocket>().First(p => p.jackpot == (visitIndex == 1));
        expectedPrize = visitIndex == 1 ? controller.jackpotPools[0] : chosenPocket.smallReward;
        foreach (var pocket in lower.GetComponentsInChildren<MedalLotteryPocket>())
        {
            var collider = pocket.GetComponent<Collider>(); if (!pocketStates.ContainsKey(collider)) pocketStates[collider] = collider.enabled; collider.enabled = false;
        }
        var port = upper.colorRoutePockets.First(p => p.kind == lower.kind); var c = port.GetComponent<Collider>();
        Vector3 normal = upper.transform.TransformPoint(upper.guideLocalCenter) - c.bounds.center; normal.y = 0; normal.Normalize();
        float radius = originalBall.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = c.ClosestPoint(c.bounds.center + normal * (c.bounds.extents.magnitude + 1));
        Place(originalBall.GetComponent<Rigidbody>(), face + normal * (radius + .025f));
        originalBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
        Enter(Phase.Port);
    }
    private static void RouteOut() => Place(originalBall.GetComponent<Rigidbody>(), upper.upperOutflow.GetComponent<Collider>().bounds.center);
    private static void OnUpperFinished(int payout) { upperEvents++; upperWin = payout; }
    private static void OnColorFinished(MedalJackpotKind kind, int payout, bool jackpot)
    { colorEvents++; colorPayout = payout; colorJackpot = jackpot; finishedKind = kind; Park(); }
    internal static void ObserveCollision(Collision collision)
    {
        if (!SessionState.GetBool(Key, false)) return;
        var guard = collision.collider.GetComponent<MedalOutBlock>();
        if (guard != null) contacts.Add(new { type = "guard", index = guard.guardIndex, fixedTime = Time.fixedTime });
    }
    internal static void ObserveTrigger(Collider collider)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (collider.GetComponent<MedalColorRoutePocket>() != null || collider.GetComponent<MedalLotteryPocket>() != null || collider.GetComponent<MedalLotteryOutflow>() != null)
            contacts.Add(new { type = "trigger", name = collider.name, fixedTime = Time.fixedTime });
    }
    private static bool Green(GameObject ball)
    {
        return ball != null && ball.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial != null &&
            r.sharedMaterial.HasProperty("_BaseColor") && r.sharedMaterial.GetColor("_BaseColor").g > .6f &&
            r.sharedMaterial.GetColor("_BaseColor").g > r.sharedMaterial.GetColor("_BaseColor").r * 1.5f);
    }
    private static void ClearBoard()
    {
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
        { item.collected = true; game.UnregisterItem(item); item.gameObject.SetActive(false); UnityEngine.Object.Destroy(item.gameObject); }
    }
    private static void ClearDraws()
    {
        foreach (string name in new[] { "ballDraws", "ballDrawStarts", "ballRefunds" })
        { object queue = Field(controller, name).GetValue(controller); queue.GetType().GetMethod("Clear").Invoke(queue, null); }
        Set(controller, "deferredDirectJpcDraws", 0L);
    }
    private static void ClearPayouts()
    {
        foreach (string name in new[] { "payoutQueue", "jackpotPayoutQueue" })
        { object queue = Field(game, name).GetValue(game); queue.GetType().GetMethod("Clear").Invoke(queue, null); }
        Set(game, "lastPayoutBatch", null); Set(game, "lastJackpotBatch", null); Set(game, "<PendingPayoutMedals>k__BackingField", 0L);
    }
    private static void RestorePockets() { foreach (var pair in pocketStates) if (pair.Key != null) pair.Key.enabled = pair.Value; pocketStates.Clear(); }
    private static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (SessionState.GetBool(Key, false) && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) &&
            !(stack ?? "").Contains("Unity.AI.ModelSelector") && !(stack ?? "").Contains("UnityEditor.Search.SearchDatabase")) errors.Add(message);
    }
    private static void Finish(bool stop)
    {
        if (!SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false); RestorePockets();
        if (controller != null) { controller.OnUpperLotteryFinished -= OnUpperFinished; controller.OnLotteryFinished -= OnColorFinished; }
        foreach (var pair in enabledStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (settings != null) { settings.SettingsFileOverride = originalOverride; settings.persistSettings = originalPersistence; }
        string finalHash = realSettingsPath == null ? "not-initialized" : Hash(realSettingsPath);
        Check("realUserSettingsFileRemainedUntouched", realSettingsHash != null && realSettingsHash == finalHash);
        if (initialized) UnityEngine.Random.state = randomState;
        Application.runInBackground = SessionState.GetBool(BackgroundKey, false);
        Write("slot-motion-play-validation.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), startedUtc = SessionState.GetString(UtcKey, ""),
            success = errors.Count == 0 && phase == Phase.Complete, completed = phase == Phase.Complete, phase = phase.ToString(),
            elapsedWallSeconds = Elapsed, maximumWallSeconds = MaximumSeconds, cases = checks.Count, checks, errors, outcomes, contacts, visits,
            upperEvents, colorEvents, settingsHashes = new { before = realSettingsHash, after = finalHash },
            fixtureNotes = new[] { "Production CompleteSpin is called with deterministic Unity random seeds.",
                "The stopped ball is held kinematic inside the deck until the station's native FixedUpdate recovers it.",
                "Seventy seconds of upper elapsed time is injected to test absence of an upper timeout without waiting seventy seconds.",
                "Native physical contacts resolve guard, color ports, medal/JP pockets and OUT; no positive reward callback is fabricated.",
                "Payout queue is isolated before the seven-coin HUD fixture; this check does not claim full JP payout drain." }
        });
        if (stop && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    private static void Write(string name, object report)
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string pointer = Path.Combine(root, ".codex-backups", "current-medal-slot-motion.txt");
        string directory = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : Path.Combine(root, ".codex-backups", "medal-slot-motion");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
    }
}

public sealed class MedalSlotMotionContactProbe : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision) => MedalSlotMotionPlayCheck.ObserveCollision(collision);
    private void OnTriggerEnter(Collider collider) => MedalSlotMotionPlayCheck.ObserveTrigger(collider);
}
