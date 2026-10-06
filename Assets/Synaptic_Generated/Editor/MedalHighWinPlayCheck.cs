using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Production slot payout and native-contact upper WIN carry regression.</summary>
[InitializeOnLoad]
public static class MedalHighWinPlayCheck
{
    private const string Key = "MedalPusher.HighWinCheck";
    private const string StartKey = Key + ".Started";
    private const string BackgroundKey = Key + ".Background";
    private const double MaximumSeconds = 80;
    private enum Phase { Boot, FirstStart, CollectFirst, FirstOut, SecondStart, BumperOne, Guard, GateReady, Port, Pause, Result, CollectThird, BumperTwo, SecondOut, ThirdStart, CollectFourth, BumperThree, ThirdOut, FourthStart, FourthOut, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalBallLotteryStation upper, lower;
    private static MedalArcadeUI ui;
    private static MedalArcadeSettings settings;
    private static LotteryBallToken upperBall, lowerBall;
    private static MedalLotteryPocket chosenPocket;
    private static MedalLotteryBumper targetBumper;
    private static readonly Dictionary<string, object> checks = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> slots = new List<object>();
    private static readonly List<object> rounds = new List<object>();
    private static readonly List<object> contacts = new List<object>();
    private static readonly List<object> visits = new List<object>();
    private static readonly Dictionary<Behaviour, bool> enabledStates = new Dictionary<Behaviour, bool>();
    private static readonly Dictionary<Collider, bool> pocketStates = new Dictionary<Collider, bool>();
    private static bool initialized, originalPersistence;
    private static string originalOverride, realSettingsPath, realSettingsHash, ballId;
    private static float stageTime, pausedElapsed;
    private static double phaseTime;
    private static int ticket, walletBefore, upperEvents, colorEvents, lastUpperPayout, upperTotal, lastColorPayout, visitIndex, expectedPrize;
    private static int roundBaseline, colorBaseline, bumperBefore;
    private static long pendingBefore;
    private static bool lastColorJackpot;
    private static MedalJackpotKind lastColorKind;
    private static UnityEngine.Random.State originalRandom;

    static MedalHighWinPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlay;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run High Probability WIN Carry Play Check")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before the high-probability WIN carry check.");
        Reset();
        foreach (string key in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck", "MedalPusher.UpperFlowCheck", "MedalPusher.IntegratedUpperFlowCheck", "MedalPusher.ClickBallOnlyCheck", "MedalPusher.SlotMotionCheck" })
            SessionState.SetBool(key, false);
        SessionState.SetFloat(StartKey, (float)Now);
        SessionState.SetBool(BackgroundKey, Application.runInBackground);
        SessionState.SetString(Key + ".Utc", DateTime.UtcNow.ToString("O"));
        Application.runInBackground = true; SessionState.SetBool(Key, true);
        EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Medal Pusher/Inspect High Probability WIN Carry Play Check")]
    public static void Inspect() => Write("high-win-play-state.json", new {
        timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false), playing = EditorApplication.isPlaying,
        phase = phase.ToString(), elapsedWallSeconds = Elapsed, upperEvents, colorEvents, visitIndex, errors,
        controller = controller == null ? null : new { controller.IsHighProbability, controller.LiveUpperWin, controller.CarriedUpperWin,
            controller.UpperWinCarriedAtStart, controller.InitialUpperWin, controller.UpperWinEarnedThisRound, controller.PendingBallDraws,
            controller.IsUpperDrawing, controller.IsColorRoundActive }, contacts
    });

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartKey, (float)Now);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Check(string name, bool value) { checks[name] = value; if (!value) errors.Add("Failed: " + name); }
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Disable(Behaviour component)
    {
        if (component == null) return; if (!enabledStates.ContainsKey(component)) enabledStates[component] = component.enabled; component.enabled = false;
    }
    private static void Reset()
    {
        phase = Phase.Boot; initialized = false; game = null; controller = null; upper = lower = null; ui = null; settings = null;
        upperBall = lowerBall = null; upperEvents = colorEvents = visitIndex = 0;
        checks.Clear(); errors.Clear(); slots.Clear(); rounds.Clear(); contacts.Clear(); visits.Clear(); enabledStates.Clear(); pocketStates.Clear();
        realSettingsPath = realSettingsHash = originalOverride = null;
    }
    private static void IsolateSettings()
    {
        if (realSettingsPath != null) return;
        settings = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettings>(); if (settings == null) return;
        originalPersistence = settings.persistSettings; originalOverride = settings.SettingsFileOverride;
        realSettingsPath = Path.Combine(Application.persistentDataPath, "medal-arcade-settings.json"); realSettingsHash = Hash(realSettingsPath);
        settings.SettingsFileOverride = Path.Combine(Path.GetTempPath(), "medal-high-win-" + Guid.NewGuid().ToString("N") + ".json");
        settings.persistSettings = false;
    }
    private static void OnPlay(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) IsolateSettings();
        if (state == PlayModeStateChange.ExitingPlayMode) { errors.Add("Play stopped before completion."); Finish(false); }
    }
    private static bool Ready()
    {
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>(); controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        ui = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
        return game != null && controller != null && ui != null && controller.upperStation != null &&
            game.OnMedalInserted != null && game.OnMedalInserted.GetInvocationList().Any(d => ReferenceEquals(d.Target, controller)) &&
            controller.OnStateChanged != null && controller.OnStateChanged.GetInvocationList().Any(d => ReferenceEquals(d.Target, ui));
    }
    private static void Setup()
    {
        IsolateSettings(); Require(settings != null, "Settings fixture missing."); originalRandom = UnityEngine.Random.state;
        Disable(game); Disable(controller);
        foreach (var input in UnityEngine.Object.FindObjectsByType<MedalInputHandler>(FindObjectsSortMode.None)) Disable(input);
        foreach (var pocket in UnityEngine.Object.FindObjectsByType<MedalSlotPocket>(FindObjectsSortMode.None)) Disable(pocket);
        foreach (var manager in UnityEngine.Object.FindObjectsByType<PrizeDropManager>(FindObjectsSortMode.None)) Disable(manager);
        ClearBoard(); ClearQueues();
        Set(game, "<TotalPaidMedals>k__BackingField", 0L); Set(game, "<TotalReturnedMedals>k__BackingField", 0L);
        Set(controller, "<SpinCredits>k__BackingField", 0); Set(controller, "nextLotteryAt", float.PositiveInfinity);
        controller.targetPayoutPercent = 90; controller.rescueSpins = 0; controller.slotBallChancePercent = 0; controller.slotMedalChancePercent = 40;
        upper = controller.upperStation;
        controller.OnUpperLotteryFinished += UpperFinished; controller.OnLotteryFinished += ColorFinished;
        foreach (bool wasHigh in new[] { false, true })
            foreach (int digit in new[] { 1, 2, 3, 4, 5, 6, 8, 9 })
            {
                Set(controller, "<IsHighProbability>k__BackingField", wasHigh);
                int before = game.medals; long pending = game.PendingPayoutMedals; Spin("digit", digit);
                bool odd = digit % 2 == 1; int baseReward = digit * 10, bonus = wasHigh && odd ? 5 : 0;
                Check((wasHigh ? "alreadyHigh" : "normal") + "Digit" + digit + "ExactPayoutAndNextMode", controller.LastSlotSymbols.All(n => n == digit) &&
                    controller.LastSlotWasWin && !controller.LastSlotWasDirectJpc && controller.LastSlotBasePayout == baseReward && controller.LastSlotBonusPayout == bonus &&
                    controller.LastSlotPayout == baseReward + bonus && game.medals == before + baseReward + bonus &&
                    game.PendingPayoutMedals == pending + baseReward + bonus && controller.IsHighProbability == odd);
            }
        controller.slotBallChancePercent = 100; controller.slotMedalChancePercent = 0;
        foreach (bool wasHigh in new[] { false, true })
        {
            Set(controller, "<IsHighProbability>k__BackingField", wasHigh);
            int before = game.medals; long pending = game.PendingPayoutMedals; int draws = controller.PendingDirectJpcDraws > int.MaxValue ? int.MaxValue : (int)controller.PendingDirectJpcDraws;
            Spin("seven"); int expected = wasHigh ? 75 : 70;
            Check((wasHigh ? "alreadyHigh" : "normal") + "777PaysDigitPrizeAndKeepsDirectJpc", controller.LastSlotWasDirectJpc &&
                controller.LastSlotBasePayout == 70 && controller.LastSlotBonusPayout == (wasHigh ? 5 : 0) && controller.LastSlotPayout == expected &&
                game.medals == before + expected && game.PendingPayoutMedals == pending + expected && controller.IsHighProbability &&
                controller.PendingDirectJpcDraws == draws + 1 && controller.PendingBallRefunds == 0 && game.CountBoardItems(true) == 0);
        }
        ClearQueues();
        controller.slotBallChancePercent = 0; controller.slotMedalChancePercent = 40; Spin("digit", 2);
        Check("evenWinClearsCarryBeforePhysicalFixture", !controller.IsHighProbability && controller.CarriedUpperWin == 0);
        controller.slotBallChancePercent = 100; controller.slotMedalChancePercent = 0;
        Spin("seven"); walletBefore = game.medals; pendingBefore = game.PendingPayoutMedals;
        initialized = true; controller.enabled = true; Set(controller, "nextLotteryAt", 0f); Enter(Phase.FirstStart);
    }
    private static void Spin(string result, int digit = 0)
    {
        int seed = FindSeed(result, digit); UnityEngine.Random.InitState(seed); Invoke(controller, "CompleteSpin"); ui.Refresh();
        slots.Add(new { result, seed, symbols = controller.LastSlotSymbols.ToArray(), controller.LastSlotBasePayout, controller.LastSlotBonusPayout,
            controller.LastSlotPayout, controller.IsHighProbability, controller.CarriedUpperWin, pending = game.PendingPayoutMedals });
    }
    private static int FindSeed(string result, int digit)
    {
        var saved = UnityEngine.Random.state;
        float ballChance = controller.EffectiveSlotBallChancePercent, medalChance = controller.EffectiveSlotMedalChancePercent;
        try
        {
            for (int seed = 1; seed < 200000; seed++)
            {
                UnityEngine.Random.InitState(seed); float outcome = UnityEngine.Random.Range(0f, 100f);
                bool ball = outcome < ballChance; bool seven = ball && UnityEngine.Random.value < 9f / 49f;
                if (result == "seven" && seven || result == "ball" && ball && !seven || result == "miss" && outcome >= ballChance + medalChance) return seed;
                if (result == "digit" && !ball && outcome < ballChance + medalChance)
                { int n = UnityEngine.Random.Range(1, 9); if (n >= 7) n++; if (n == digit) return seed; }
            }
            throw new InvalidOperationException("No production outcome seed found: " + result + " " + digit);
        }
        finally { UnityEngine.Random.state = saved; }
    }
    private static void Enter(Phase value) { phase = value; phaseTime = Now; stageTime = Time.time; }
    private static bool SameBall() => upperBall != null && ReferenceEquals(upper.ActiveBall, upperBall) && upperBall.ticket == ticket && upper.CurrentTicket == ticket && !upperBall.IsConsumed;
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            Require(Elapsed < MaximumSeconds, "High-WIN check exceeded 80 seconds.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;
            if (!initialized) { if (!Ready()) { Require(Elapsed < 20, "Game Start readiness timeout."); return; } Setup(); return; }
            Require(Now - phaseTime < 12, "Phase timed out: " + phase);
            switch (phase)
            {
                case Phase.FirstStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    CaptureBall();
                    Check("first777Starts100WithUnpaidBaselineZero", controller.InitialUpperWin == 100 && controller.UpperWinCarriedAtStart == 0 &&
                        controller.UpperWinEarnedThisRound == 100 && upper.BumperWin == 100 && upper.ColorGatesOpen && upper.DirectJpcGatesUnlocked);
                    RefundToRealCollector(); Enter(Phase.CollectFirst); break;
                case Phase.CollectFirst:
                    if (controller.PendingBallDraws < 2) return;
                    Check("physicalRefundIsCollectedIntoOneBusyNormalTicket", SameBall() && contacts.Any(c => c.ToString().Contains("collector")) &&
                        controller.PendingBallDraws == 2 && game.CountBoardItems(true) == 0 && game.medals == walletBefore && controller.LastSlotPayout == 0);
                    CaptureLedger(); RouteOut(); Enter(Phase.FirstOut); break;
                case Phase.FirstOut:
                    if (upperEvents <= roundBaseline) return;
                    CheckUpperPayment("firstActualOutPays100AndBanks100", 100, 100, 100);
                    ui.Refresh(); Check("idleWinCaptionShowsNextBallCarry100", ui.upperWinText != null && ui.upperWinText.text.Contains("次球へ100WIN"));
                    Enter(Phase.SecondStart); break;
                case Phase.SecondStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    CaptureBall();
                    Check("normalCollectedBallStartsCarried100WithClosedGates", controller.LiveUpperWin == 100 && controller.InitialUpperWin == 100 &&
                        controller.UpperWinCarriedAtStart == 100 && controller.UpperWinEarnedThisRound == 0 && upper.InitialWin == 100 &&
                        !upper.DirectJpcGatesUnlocked && !upper.ColorGatesOpen && !controller.AreColorGatesUnlocked && upper.UsedOutBlockCount == 0);
                    ui.Refresh(); Check("carried100CaptionShowsPaidBaselineAndZeroNewPayout", ui.upperWinText != null &&
                        ui.upperWinText.text.Contains("引継100WIN") && ui.upperWinText.text.Contains("今回+0枚"));
                    Bumper(); Enter(Phase.BumperOne); break;
                case Phase.BumperOne:
                    if (upper.TotalBumperHits <= bumperBefore) return;
                    Check("actualBumperRaisesCarry100To102AndOnlyTwoUnpaid", SameBall() && upper.TotalBumperHits == 1 && upper.BumperWin == 102 &&
                        controller.LiveUpperWin == 102 && controller.UpperWinEarnedThisRound == 2 && upper.ColorGatesOpen && controller.AreColorGatesUnlocked &&
                        upper.LastBumper == targetBumper && game.medals == walletBefore);
                    ui.Refresh(); Check("102CaptionShowsOnlyTwoNewMedals", ui.upperWinText != null && ui.upperWinText.text.Contains("今回+2枚"));
                    IncomingGuard(); Enter(Phase.Guard); break;
                case Phase.Guard:
                    if (!upper.IsOutBlockUsed(0)) return;
                    Check("carriedRoundNativeGuardIsUsedOnce", SameBall() && upper.UsedOutBlockCount == 1 && upper.BumperWin == 102 &&
                        contacts.Any(c => c.ToString().Contains("guard")));
                    Park(); Enter(Phase.GateReady); break;
                case Phase.GateReady:
                    Require(SameBall() && upper.IsDrawing && upper.ColorGatesOpen, "Gate wait lost the original carried upper ball.");
                    if (Time.time - stageTime < .65f || !GatesAtTarget()) { Park(); return; }
                    Check("physicalGatesFinishRaisingBeforeNativePortEntry", GatesAtTarget() && upper.BumperWin == 102 && SameBall());
                    BeginVisit(); break;
                case Phase.Port:
                    Require(SameBall() && upper.IsDrawing, "Color-port fixture lost its original upper ball before entry.");
                    Require(Now - phaseTime < 1.25 || upper.IsUpperSuspended, "Incoming original ball did not enter its actual color port.");
                    if (!upper.IsUpperSuspended || lower.ActiveBall == null) return;
                    lowerBall = lower.ActiveBall; lowerBall.gameObject.AddComponent<MedalHighWinContactProbe>();
                    Hold(lowerBall.GetComponent<Rigidbody>(), lower.transform.TransformPoint(lower.guideLocalCenter + Vector3.up * 3));
                    pausedElapsed = upper.UpperElapsedSeconds;
                    Check("carriedUpperUsesActualColorPort_" + visitIndex, SameBall() && upper.LastColorPocket != null && upper.LastColorPocket.kind == lower.kind &&
                        controller.ActiveKind == lower.kind && upper.BumperWin == 102 && controller.UpperWinCarriedAtStart == 100);
                    Enter(Phase.Pause); break;
                case Phase.Pause:
                    if (Time.time - stageTime < .35f) return;
                    Check("pausedCarryAndPaidBaselineRemainUnchanged_" + visitIndex, SameBall() && upper.IsUpperSuspended && upper.UsedOutBlockCount == 1 &&
                        controller.LiveUpperWin == 102 && controller.UpperWinCarriedAtStart == 100 && controller.UpperWinEarnedThisRound == 2 &&
                        Mathf.Abs(upper.UpperElapsedSeconds - pausedElapsed) < .005f);
                    chosenPocket.GetComponent<Collider>().enabled = true; Place(lowerBall.GetComponent<Rigidbody>(), chosenPocket.GetComponent<Collider>().bounds.center);
                    Enter(Phase.Result); break;
                case Phase.Result:
                    if (colorEvents <= colorBaseline || upper.IsUpperSuspended) return;
                    Check("native" + (visitIndex == 1 ? "Jackpot" : "Medal") + "PaysOnlyColorPrizeAndReturnsSameCarryBall", SameBall() && colorEvents == colorBaseline + 1 &&
                        lastColorPayout == expectedPrize && lastColorJackpot == (visitIndex == 1) && lastColorKind == lower.kind && lower.LastPocket == chosenPocket &&
                        game.medals == walletBefore + expectedPrize && game.PendingPayoutMedals == pendingBefore + expectedPrize && upper.UsedOutBlockCount == 1 &&
                        controller.LiveUpperWin == 102 && controller.UpperWinCarriedAtStart == 100 && controller.UpperWinEarnedThisRound == 2 && upper.ColorGatesOpen);
                    visits.Add(new { index = visitIndex, kind = lower.kind.ToString(), jackpot = lastColorJackpot, payout = lastColorPayout, ticket, ballId,
                        controller.LiveUpperWin, controller.UpperWinCarriedAtStart, controller.UpperWinEarnedThisRound, guards = upper.OutBlockUsedStates.ToArray() });
                    RestorePockets(); visitIndex++;
                    if (visitIndex < 2) BeginVisit(); else { RefundToRealCollector(); Enter(Phase.CollectThird); }
                    break;
                case Phase.CollectThird:
                    if (controller.PendingBallDraws < 2) return;
                    Check("nextNormalBallQueuedWhileLiveWinIs102", SameBall() && controller.LiveUpperWin == 102 && controller.PendingBallDraws == 2 &&
                        controller.CarriedUpperWin == 100 && game.CountBoardItems(true) == 0);
                    Bumper(); Enter(Phase.BumperTwo); break;
                case Phase.BumperTwo:
                    if (upper.TotalBumperHits <= bumperBefore) return;
                    Check("busyQueueDoesNotFreezeCarryAtCollectionTime", SameBall() && upper.BumperWin == 104 && upper.TotalBumperHits == 2 &&
                        controller.UpperWinEarnedThisRound == 4 && controller.PendingBallDraws == 2);
                    CaptureLedger(); RouteOut(); Enter(Phase.SecondOut); break;
                case Phase.SecondOut:
                    if (upperEvents <= roundBaseline) return;
                    CheckUpperPayment("carried100To104PaysOnlyFourOnce", 104, 4, 104);
                    ui.Refresh(); Check("104OutBannerShowsTotalWinAndOnlyFourPaid", ui.payoutBanner != null &&
                        ui.payoutBanner.text.Contains("104WIN") && ui.payoutBanner.text.Contains("+4枚"));
                    Enter(Phase.ThirdStart); break;
                case Phase.ThirdStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    CaptureBall();
                    Check("busyNormalQueueReadsLatestFinal104AtStart", controller.LiveUpperWin == 104 && controller.InitialUpperWin == 104 &&
                        controller.UpperWinCarriedAtStart == 104 && controller.UpperWinEarnedThisRound == 0 && upper.ColorGatesOpen &&
                        !upper.DirectJpcGatesUnlocked && upper.UsedOutBlockCount == 0);
                    controller.slotBallChancePercent = 0; controller.slotMedalChancePercent = 40;
                    int evenWallet = game.medals; long evenQueue = game.PendingPayoutMedals; Spin("digit", 2);
                    Check("evenWinEndsCarryForNextBallWithoutReducingCurrentWinOrBaseline", !controller.IsHighProbability && controller.CarriedUpperWin == 0 &&
                        SameBall() && controller.LiveUpperWin == 104 && controller.UpperWinCarriedAtStart == 104 && controller.UpperWinEarnedThisRound == 0 &&
                        game.medals == evenWallet + 20 && game.PendingPayoutMedals == evenQueue + 20);
                    RefundToRealCollector(); Enter(Phase.CollectFourth); break;
                case Phase.CollectFourth:
                    if (controller.PendingBallDraws < 2) return;
                    Bumper(); Enter(Phase.BumperThree); break;
                case Phase.BumperThree:
                    if (upper.TotalBumperHits <= bumperBefore) return;
                    Check("postEvenCurrentBallCanEarnTwoAboveOldPaidBaseline", SameBall() && controller.LiveUpperWin == 106 &&
                        controller.UpperWinCarriedAtStart == 104 && controller.UpperWinEarnedThisRound == 2 && controller.CarriedUpperWin == 0 && !controller.IsHighProbability);
                    controller.slotBallChancePercent = 0; controller.slotMedalChancePercent = 40;
                    int oddWallet = game.medals; long oddQueue = game.PendingPayoutMedals; Spin("digit", 1);
                    Check("newOddReentryDoesNotRestorePreviousHighChainCarry", controller.IsHighProbability && controller.CarriedUpperWin == 0 &&
                        SameBall() && controller.LiveUpperWin == 106 && controller.UpperWinCarriedAtStart == 104 && controller.UpperWinEarnedThisRound == 2 &&
                        controller.LastSlotPayout == 10 && controller.LastSlotBonusPayout == 0 && game.medals == oddWallet + 10 && game.PendingPayoutMedals == oddQueue + 10);
                    CaptureLedger(); RouteOut(); Enter(Phase.ThirdOut); break;
                case Phase.ThirdOut:
                    if (upperEvents <= roundBaseline) return;
                    CheckUpperPayment("oldRoundPaysOnlyTwoWithoutRebankAfterEvenThenNewOdd", 106, 2, 0);
                    Enter(Phase.FourthStart); break;
                case Phase.FourthStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    CaptureBall();
                    Check("nextBallAfterEvenStartsZeroAndFreshGuards", controller.LiveUpperWin == 0 && controller.InitialUpperWin == 0 &&
                        controller.UpperWinCarriedAtStart == 0 && upper.UsedOutBlockCount == 0 && !upper.ColorGatesOpen && !controller.AreColorGatesUnlocked);
                    CaptureLedger(); RouteOut(); Enter(Phase.FourthOut); break;
                case Phase.FourthOut:
                    if (upperEvents <= roundBaseline) return;
                    CheckUpperPayment("zeroWinActualOutPaysZeroOnce", 0, 0, 0);
                    Check("completedFourPhysicalRoundsAndTwoColorReturns", upperEvents == 4 && colorEvents == 2 && !controller.IsUpperDrawing &&
                        controller.PendingBallDraws == 0 && controller.PendingBallRefunds == 0 && controller.stations.All(s => !s.IsRotating && !s.IsDrawing));
                    phase = Phase.Complete; Finish(true); break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }
    private static void CaptureBall()
    {
        upperBall = upper.ActiveBall; ticket = upper.CurrentTicket; ballId = upperBall.GetEntityId().ToString();
        upperBall.gameObject.AddComponent<MedalHighWinContactProbe>(); Park(); walletBefore = game.medals; pendingBefore = game.PendingPayoutMedals;
    }
    private static void CaptureLedger() { walletBefore = game.medals; pendingBefore = game.PendingPayoutMedals; roundBaseline = upperEvents; }
    private static void CheckUpperPayment(string name, int total, int payout, int bank)
    {
        Check(name, upperEvents == roundBaseline + 1 && lastUpperPayout == payout && upperTotal == total && controller.LastUpperWin == total &&
            controller.LastPayout == payout && game.medals == walletBefore + payout && game.PendingPayoutMedals == pendingBefore + payout &&
            controller.CarriedUpperWin == bank && !controller.IsUpperDrawing && upper.LastExitedBowl && !upper.LastTimedOut);
        int before = game.medals; long pending = game.PendingPayoutMedals;
        controller.FinishLottery(upper, ticket, false, total); controller.NotifyUpperBumperHit(upper, ticket, total + 2);
        Check(name + "RejectsDuplicateOrStaleCallbacks", game.medals == before && game.PendingPayoutMedals == pending && upperEvents == roundBaseline + 1);
        rounds.Add(new { total, payout, bank, ticket, ballId, walletBefore, walletAfter = game.medals, queuedBefore = pendingBefore, queuedAfter = game.PendingPayoutMedals });
    }
    private static void RefundToRealCollector()
    {
        Park(); controller.slotBallChancePercent = 100; controller.slotMedalChancePercent = 0; game.maxPrizesOnBoard = 20;
        int wallet = game.medals; long pending = game.PendingPayoutMedals; bool high = controller.IsHighProbability; Spin("ball");
        var ball = game.itemsRoot.GetComponentsInChildren<MedalItem>().Single(i => i.isBall && !i.collected);
        Check("physicalBallRefundHasNoNumericOrHighBonus_" + phase, controller.LastSlotPayout == 0 && controller.LastSlotBasePayout == 0 &&
            controller.LastSlotBonusPayout == 0 && game.medals == wallet && game.PendingPayoutMedals == pending && controller.IsHighProbability == high);
        ball.gameObject.AddComponent<MedalHighWinContactProbe>();
        var collector = UnityEngine.Object.FindAnyObjectByType<MedalDropDetector>(); Require(collector != null, "Physical collector missing.");
        Place(ball.GetComponent<Rigidbody>(), collector.GetComponent<Collider>().bounds.center);
    }
    private static void Bumper()
    {
        Park(); bumperBefore = upper.TotalBumperHits; targetBumper = upper.bumpers[bumperBefore % upper.bumpers.Length];
        var collider = targetBumper.GetComponent<SphereCollider>();
        Vector3 normal = collider.bounds.center - upper.transform.TransformPoint(upper.guideLocalCenter); normal.y = 0; normal.Normalize();
        float radius = upperBall.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = collider.ClosestPoint(collider.bounds.center + normal * (collider.bounds.extents.magnitude + 1));
        Place(upperBall.GetComponent<Rigidbody>(), face + normal * (radius + .025f)); upperBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
    }
    private static void IncomingGuard()
    {
        Park(); var collider = upper.outBlockBodies[0].GetComponent<Collider>(); Vector3 normal = collider.transform.forward.normalized;
        float radius = upperBall.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = collider.ClosestPoint(collider.bounds.center + normal * (collider.bounds.extents.magnitude + 1));
        Place(upperBall.GetComponent<Rigidbody>(), face + normal * (radius + .025f)); upperBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
    }
    private static void BeginVisit()
    {
        Park(); lower = controller.stations[visitIndex]; controller.jackpotPools[visitIndex] = 123;
        chosenPocket = lower.GetComponentsInChildren<MedalLotteryPocket>().First(p => p.jackpot == (visitIndex == 1));
        expectedPrize = visitIndex == 1 ? 123 : chosenPocket.smallReward;
        walletBefore = game.medals; pendingBefore = game.PendingPayoutMedals; colorBaseline = colorEvents;
        foreach (var pocket in lower.GetComponentsInChildren<MedalLotteryPocket>())
        { var collider = pocket.GetComponent<Collider>(); pocketStates[collider] = collider.enabled; collider.enabled = false; }
        var port = upper.colorRoutePockets.First(p => p.kind == lower.kind); var c = port.GetComponent<Collider>();
        Vector3 normal = upper.transform.TransformPoint(upper.guideLocalCenter) - c.bounds.center; normal.y = 0; normal.Normalize();
        float radius = upperBall.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = c.ClosestPoint(c.bounds.center + normal * (c.bounds.extents.magnitude + 1));
        Place(upperBall.GetComponent<Rigidbody>(), face + normal * (radius + .025f)); upperBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
        Enter(Phase.Port);
    }
    private static void RouteOut() => Place(upperBall.GetComponent<Rigidbody>(), upper.upperOutflow.GetComponent<Collider>().bounds.center);
    private static void Park() { if (upperBall != null) Place(upperBall.GetComponent<Rigidbody>(), upper.transform.TransformPoint(upper.guideLocalCenter + Vector3.up * 10)); }
    private static bool GatesAtTarget() => upper.colorGateBodies != null && upper.colorGateClosedPositions != null && upper.colorGateBodies.Length == 3 &&
        Enumerable.Range(0, 3).All(i => Vector3.Distance(upper.colorGateBodies[i].position,
            upper.colorGateBodies[i].transform.parent.TransformPoint(upper.colorGateClosedPositions[i] + Vector3.up * upper.colorGateRaiseHeight)) < .025f);
    private static void Place(Rigidbody body, Vector3 position)
    { body.isKinematic = false; body.detectCollisions = true; body.useGravity = true; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.position = position; Physics.SyncTransforms(); }
    private static void Hold(Rigidbody body, Vector3 position) { Place(body, position); body.isKinematic = true; }
    private static void UpperFinished(int payout) { upperEvents++; lastUpperPayout = payout; upperTotal = controller.LastUpperWin; }
    private static void ColorFinished(MedalJackpotKind kind, int payout, bool jackpot)
    { colorEvents++; lastColorKind = kind; lastColorPayout = payout; lastColorJackpot = jackpot; Park(); }
    internal static void ObserveCollision(Collision collision)
    {
        if (!SessionState.GetBool(Key, false)) return;
        var bumper = collision.collider.GetComponent<MedalLotteryBumper>(); var guard = collision.collider.GetComponent<MedalOutBlock>();
        if (bumper != null || guard != null) contacts.Add(new { type = guard != null ? "guard" : "bumper", name = collision.collider.name, fixedTime = Time.fixedTime });
    }
    internal static void ObserveTrigger(Collider collider)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (collider.GetComponent<MedalDropDetector>() != null || collider.GetComponent<MedalColorRoutePocket>() != null || collider.GetComponent<MedalLotteryPocket>() != null || collider.GetComponent<MedalLotteryOutflow>() != null)
            contacts.Add(new { type = collider.GetComponent<MedalDropDetector>() != null ? "collector" : "trigger", name = collider.name, fixedTime = Time.fixedTime });
    }
    private static void ClearBoard()
    {
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
        { item.collected = true; game.UnregisterItem(item); item.gameObject.SetActive(false); UnityEngine.Object.Destroy(item.gameObject); }
    }
    private static void ClearQueues()
    {
        foreach (string name in new[] { "ballDraws", "ballDrawStarts", "ballRefunds" })
        { object queue = Field(controller, name).GetValue(controller); queue.GetType().GetMethod("Clear").Invoke(queue, null); }
        Set(controller, "deferredDirectJpcDraws", 0L);
        foreach (string name in new[] { "payoutQueue", "jackpotPayoutQueue" })
        { object queue = Field(game, name).GetValue(game); queue.GetType().GetMethod("Clear").Invoke(queue, null); }
        Set(game, "lastPayoutBatch", null); Set(game, "lastJackpotBatch", null); Set(game, "<PendingPayoutMedals>k__BackingField", 0L);
    }
    private static void RestorePockets() { foreach (var pair in pocketStates) if (pair.Key != null) pair.Key.enabled = pair.Value; pocketStates.Clear(); }
    private static string Hash(string path)
    { if (!File.Exists(path)) return "missing"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (SessionState.GetBool(Key, false) && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) &&
            !(stack ?? "").Contains("Unity.AI.ModelSelector") && !(stack ?? "").Contains("UnityEditor.Search.SearchDatabase")) errors.Add(message);
    }
    private static void Finish(bool stop)
    {
        if (!SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false); RestorePockets();
        if (controller != null) { controller.OnUpperLotteryFinished -= UpperFinished; controller.OnLotteryFinished -= ColorFinished; }
        foreach (var pair in enabledStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (settings != null) { settings.SettingsFileOverride = originalOverride; settings.persistSettings = originalPersistence; }
        string finalHash = realSettingsPath == null ? "not-initialized" : Hash(realSettingsPath);
        Check("realUserSettingsFileRemainedUntouched", realSettingsHash != null && realSettingsHash == finalHash);
        if (initialized) UnityEngine.Random.state = originalRandom;
        Application.runInBackground = SessionState.GetBool(BackgroundKey, false);
        Write("high-win-play-validation.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), startedUtc = SessionState.GetString(Key + ".Utc", ""),
            success = errors.Count == 0 && phase == Phase.Complete, completed = phase == Phase.Complete, phase = phase.ToString(),
            elapsedWallSeconds = Elapsed, maximumWallSeconds = MaximumSeconds, cases = checks.Count, checks, errors, slots, rounds, visits, contacts,
            upperEvents, colorEvents, settingsHashes = new { before = realSettingsHash, after = finalHash },
            fixtureNotes = new[] { "Deterministic Unity seeds call production CompleteSpin; no payout result is injected.",
                "All upper bumper, guard, color route, lower result, collector and OUT transitions use native Rigidbody contacts.",
                "Game payout spawning is held during exact wallet/queue ledger comparisons; this test does not claim full JP physical drain.",
                "Carry is observed from the first 100WIN draw into real collected-ball starts; no positive carry value is injected." }
        });
        if (stop && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    private static void Write(string name, object report)
    {
        string root = Path.GetDirectoryName(Application.dataPath), pointer = Path.Combine(root, ".codex-backups", "current-medal-high-win.txt");
        string directory = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : Path.Combine(root, ".codex-backups", "medal-high-win");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
    }
}

public sealed class MedalHighWinContactProbe : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision) => MedalHighWinPlayCheck.ObserveCollision(collision);
    private void OnTriggerEnter(Collider collider) => MedalHighWinPlayCheck.ObserveTrigger(collider);
}
