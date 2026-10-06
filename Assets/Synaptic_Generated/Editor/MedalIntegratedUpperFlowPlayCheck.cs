using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Bounded Play-only check of integrated upper ports, same-ball returns, independent guards, and actual payout physics.</summary>
[InitializeOnLoad]
public static class MedalIntegratedUpperFlowPlayCheck
{
    private const string Key = "MedalPusher.IntegratedUpperFlowCheck";
    private const string StartedKey = Key + ".Started";
    private const string BackgroundKey = Key + ".Background";
    private const string UtcKey = Key + ".Utc";
    private enum Phase { Boot, Settings, SidePayout, TopPayout, Collector, NormalPayoutBurst, OpenHoles, ClosedHoles, Capacity, Drain, Slots, Rounds, Stop, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalBallLotteryStation upper;
    private static MedalArcadeUI arcade;
    private static MedalArcadeSettings settings;
    private static MedalArcadeSettingsUI settingsUI;
    private static readonly Dictionary<string, object> results = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<string> editorIssues = new List<string>();
    private static readonly List<string> captureIssues = new List<string>();
    private static readonly List<object> payouts = new List<object>();
    private static readonly List<Vector3> sidePositions = new List<Vector3>();
    private static readonly List<Vector3> topPositions = new List<Vector3>();
    private static readonly List<MedalItem> payoutItems = new List<MedalItem>();
    private static readonly List<object> rounds = new List<object>();
    private static readonly List<object> bumperContactSamples = new List<object>();
    private static readonly List<MedalItem> holeItems = new List<MedalItem>();
    private static readonly Dictionary<Collider, bool> routedPocketStates = new Dictionary<Collider, bool>();
    private const int BurstCount = 500;
    private static readonly Dictionary<Collider, bool> burstIgnoredColliderStates = new Dictionary<Collider, bool>();
    private static readonly List<BurstCoin> burstCoins = new List<BurstCoin>();
    private sealed class BurstCoin
    {
        public MedalItem item;
        public Vector3 spawn;
        public float spawnedAt;
        public bool dynamic, credited, hitPlateTop, firstSurfaceIsPlate;
        public bool firstContactEvaluated, firstContactIsUpper;
        public string firstContactObject;
        public Vector3? firstContactPosition, firstContactNormal;
        public float firstContactDelay;
        public string firstSurface;
        public Vector3? firstSurfacePosition, firstPlatePosition;
        public float firstSurfaceDelay;
        public readonly List<object> spawnOverlaps = new List<object>();
        public int coinOverlaps, geometryOverlaps;
    }
    private static long burstPendingBefore, burstReturnedBefore;
    private static int burstWalletBefore, burstMaxBoard, burstMaxFront, burstMaxRear;
    private static float burstLastSpawnAt;
    private static float burstNextSampleAt;
    private static bool burstActive;
    private static MedalItem frontItem, closedHoleItem;
    private static MedalArcadeSettings.Values savedValues;
    private static bool initialized, capturing, savedPersistence;
    private static string originalOverride, realSettingsPath, realSettingsHash, temporarySettingsPath;
    private static float originalTimeScale, nextAt, placedFixedTime, pusherAtOpen;
    private static double phaseStarted;
    private static Quaternion[] idleAngles, roundAngles, stopAngles;
    private static int realCap, walletBefore, upperEvents, colorEvents;
    private static long returnedBefore, paidBefore, queuedBefore;
    private static int roundIndex, ticket, upperBaseline;
    private static int lastUpperWin, lastColorPayout, roundWallet, expectedPool;
    private static int blueBeforeForcedHits, yellowBeforePort;
    private static int naturalUpperCount, spinsBefore;
    private static int naturalUpperPhysicalContactTotal;
    private static int successfulGuardCount;
    private static MedalJackpotKind? finishedKind;
    private static bool finishedJackpot, upperRouted;
    private static double roundStarted;
    private static bool upperTimedOut;
    private static float originalUpperTimeout;
    private static LotteryBallToken upperToken;
    private static MedalLotteryBumper targetBumper;
    private static int actualBumperContacts, finalUpperHits, finalUpperWin;
    private static bool upperExitedBowl;
    private static bool upperEndedWithOriginalToken;
    private static Vector3? finalUpperBallLocalPosition;


    private enum IntegratedStep { WaitBall, PulseTo100, Hold100, PulseTo102, HoldOpen, Guard, Visit, PulseTo104, FinalOut, Natural }
    private static IntegratedStep integratedStep;
    private static readonly List<object> visits = new List<object>();
    private static readonly List<object> guardContacts = new List<object>();
    private static readonly List<object> naturalUpperSamples = new List<object>();
    private static readonly List<object> slotPocketContacts = new List<object>();
    private static readonly List<object> fixtureBoundaries = new List<object>();
    private static readonly Dictionary<MedalSlotPocket, bool> upperFixtureSlotStates = new Dictionary<MedalSlotPocket, bool>();
    private static readonly Dictionary<MedalOutBlock, int> physicalGuardContacts = new Dictionary<MedalOutBlock, int>();
    private static readonly int[] naturalSeeds = { 73101, 73102, 73103 };
    private static int visitIndex, visitStage, guardIndex, guardStage, guardContactBaseline, visitUpperHits, visitUpperWin, visitWallet, visitColorTicket;
    private static string upperObjectId;
    private static int upperStartEvents, mainUpperStartBaseline, visitUpperEvents, visitColorEvents, earnedColorTotal, legacyEvents;
    private static int pulseTargetHits, pulseStage, pulseBefore;
    private static float pulseNextFixed, holdUntilFixed, guardUsedFixed, nextNaturalSample;
    private static double visitStarted, visitFinishedAt;
    private static float suspendedElapsedAt, lowerPocketAt;
    private static bool[] visitGuardSnapshot;
    private static MedalLotteryPocket chosenColorPocket;
    private static MedalBallLotteryStation visitingStation;
    private static LotteryBallToken visitingToken;
    private static IntegratedStep afterGuard;
    private static UnityEngine.Random.State originalRandomState;

    static MedalIntegratedUpperFlowPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Integrated Upper Flow Play Check")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play mode before running the integrated upper-flow check.");
        MedalPusherExpansionBuilder.Validate(); Reset();
        foreach (string other in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck", "MedalPusher.UpperFlowCheck", "MedalPusher.ClickBallOnlyCheck" }) SessionState.SetBool(other, false);
        SessionState.SetFloat(StartedKey, (float)Now); SessionState.SetString(UtcKey, DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(BackgroundKey, Application.runInBackground); Application.runInBackground = true;
        SessionState.SetBool(Key, true); EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
    }

    public static void BatchRun()
    {
        try
        {
            Require(Application.isBatchMode, "BatchRun requires an isolated batch-mode Unity project.");
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None; EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSceneManager.OpenScene(MedalPusherSceneBuilder.ScenePath); MedalPusherExpansionBuilder.Build(); Run();
        }
        catch (Exception exception)
        {
            MedalPusherExpansionBuilder.WriteReport("integrated-upper-play-validation.json", new { timestampUtc = DateTime.UtcNow.ToString("O"), success = false, errors = new[] { exception.ToString() } });
            Debug.LogException(exception); if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Medal Pusher/Inspect Integrated Upper Flow Play Check")]
    public static void Inspect()
    {
        var current = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        MedalPusherExpansionBuilder.WriteReport("integrated-upper-play-state.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false),
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused, elapsedWallSeconds = Elapsed,
            phase = phase.ToString(), roundIndex, upperEvents, colorEvents, legacyEvents,
            integratedUpperMode = true, targetRounds = TargetRounds, maximumWallSeconds = MaximumSeconds, integratedStep = integratedStep.ToString(), visitIndex, guardIndex,
            bootReadiness = Diagnostics(), controller = current == null ? null : new {
                current.IsUpperDrawing, current.IsSelectingColor, current.IsColorRoundActive, activeKind = current.ActiveKind?.ToString(),
                current.PendingBallDraws, current.LastUpperWin, current.LiveUpperWin, current.AreColorGatesUnlocked,
                suspended = current.upperStation != null && current.upperStation.IsUpperSuspended,
                guardsUsed = current.upperStation == null ? null : current.upperStation.OutBlockUsedStates,
                upperBall = current.upperStation == null || current.upperStation.ActiveBall == null ? null : Pos(current.upperStation.ActiveBall.transform.position)
            }
        });
    }

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartedKey, (float)Now);
    private static int TargetRounds => 4;
    private static int MaximumSeconds => 900;
    private static object Pos(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, bool condition)
    {
        results[name] = condition;
        if (!condition)
        {
            errors.Add("Failed: " + name);
            results["failureDiagnostics_" + name] = new {
                fixedTime = Time.fixedTime, gameTime = Time.time, roundIndex, integratedStep = integratedStep.ToString(), visitIndex, guardIndex,
                actualWallet = game == null ? 0 : game.medals, expectedRoundWallet = roundWallet + earnedColorTotal,
                visitWallet, expectedVisitResultWallet = visitWallet + (visitIndex % 2 == 1 ? expectedPool : chosenColorPocket == null ? 0 : chosenColorPocket.smallReward),
                upperEvents, upperBaseline, visitUpperEvents, colorEvents, visitColorEvents, actualBumperContacts, pulseBefore,
                upperWin = upper == null ? 0 : upper.BumperWin, upperHits = upper == null ? 0 : upper.TotalBumperHits,
                liveUpperWin = controller == null ? 0 : controller.LiveUpperWin,
                actualLastBumper = upper == null || upper.LastBumper == null ? null : upper.LastBumper.name,
                expectedBumper = targetBumper == null ? null : targetBumper.name,
                sameOriginalBall = upperToken != null && upper != null && ReferenceEquals(upper.ActiveBall, upperToken),
                originalTokenConsumed = upperToken != null && upperToken.IsConsumed,
                usedGuards = upper == null ? null : upper.OutBlockUsedStates,
                colorActive = controller != null && controller.IsColorRoundActive,
                slotSpinning = controller != null && controller.IsSlotSpinning,
                totalSpins = controller == null ? 0 : controller.TotalSpins, slotCredits = controller == null ? 0 : controller.SpinCredits,
                returnedMedals = game == null ? 0 : game.TotalReturnedMedals, pendingPayout = game == null ? 0 : game.PendingPayoutMedals,
                legacyEvents
            };
        }
    }
    private static void CheckOnce(string name, bool condition) { if (!results.ContainsKey(name)) Check(name, condition); }
    private static FieldInfo Field(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name); return field;
    }
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void ClearCredits() { Set(controller, "<SpinCredits>k__BackingField", 0); Set(controller, "<MedalsTowardSpin>k__BackingField", 0); }
    private static void ClearDrawQueue() => ((Queue<MedalJackpotKind>)Field(controller, "ballDraws").GetValue(controller)).Clear();
    private static bool HasTarget(Delegate action, object target) => action != null && target != null && action.GetInvocationList().Any(d => ReferenceEquals(d.Target, target));
    private static string[] Targets(Delegate action) => action == null ? Array.Empty<string>() : action.GetInvocationList().Select(d => (d.Target == null ? "static" : d.Target.GetType().Name) + "." + d.Method.Name).ToArray();

    private static void Reset()
    {
        phase = Phase.Boot; game = null; controller = null; upper = null; settings = null; settingsUI = null; arcade = null;
        initialized = capturing = false; upperEvents = colorEvents = roundIndex = naturalUpperCount = 0;
        successfulGuardCount = 0;
        naturalUpperPhysicalContactTotal = 0; legacyEvents = upperStartEvents = earnedColorTotal = 0; physicalGuardContacts.Clear();
        bumperContactSamples.Clear();
        temporarySettingsPath = realSettingsPath = realSettingsHash = null;
        results.Clear(); errors.Clear(); editorIssues.Clear(); captureIssues.Clear(); payouts.Clear(); sidePositions.Clear(); topPositions.Clear();
        payoutItems.Clear(); rounds.Clear(); visits.Clear(); guardContacts.Clear(); naturalUpperSamples.Clear(); slotPocketContacts.Clear(); fixtureBoundaries.Clear(); upperFixtureSlotStates.Clear(); holeItems.Clear(); routedPocketStates.Clear();
        burstCoins.Clear(); burstIgnoredColliderStates.Clear(); burstActive = false;
    }
    private static bool Ready()
    {
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>(); controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        settings = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettings>(); settingsUI = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettingsUI>();
        arcade = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
        var basic = UnityEngine.Object.FindAnyObjectByType<MedalPusherUI>();
        return game != null && controller != null && settings != null && settingsUI != null && arcade != null &&
            HasTarget(game.OnMedalInserted, controller) && HasTarget(game.OnMedalsChanged, basic) && HasTarget(controller.OnStateChanged, arcade);
    }
    private static object Diagnostics() => new {
        frameCount = Time.frameCount, gameTime = Time.time, Application.runInBackground,
        options = EditorSettings.enterPlayModeOptions.ToString(), enabled = EditorSettings.enterPlayModeOptionsEnabled,
        paidTargets = Targets(game == null ? null : game.OnMedalInserted),
        walletTargets = Targets(game == null ? null : game.OnMedalsChanged),
        stateTargets = Targets(controller == null ? null : controller.OnStateChanged)
    };
    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Key, false) || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        if (capturing) captureIssues.Add(message);
        else if ((stack ?? "").Contains("Unity.AI.ModelSelector") || message.Contains("Unity.AI.ModelSelector") ||
            ((stack ?? "").Contains("UnityEditor.Search.SearchDatabase") && (stack ?? "").Contains("GetDefaultSearchDatabase") && (stack ?? "").Contains("IndexationOnStartup"))) editorIssues.Add(message);
        else errors.Add(message);
    }
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key, false)) { errors.Add("Play mode exited early."); Finish(false); }
    }
    private static void Enter(Phase next) { phase = next; phaseStarted = Now; }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.runInBackground = true;
        if (Elapsed > MaximumSeconds) { errors.Add("Upper-flow check exceeded its " + MaximumSeconds + " second wall-clock limit."); Finish(true); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        try
        {
            if (phase == Phase.Boot)
            {
                Require(Elapsed < 25, "Runtime Start was not ready in 25 wall-clock seconds.");
                if (Time.time < .5f || !Ready()) { results["bootReadiness"] = Diagnostics(); return; }
                Initialize();
            }
            if (phase == Phase.NormalPayoutBurst) Require(Now - phaseStarted < 90, "Normal 500-medal payout exceeded 90 wall-clock seconds.");
            else if (phase != Phase.Rounds && phase != Phase.Stop) Require(Now - phaseStarted < 15, "Setup stage timed out: " + phase);
            switch (phase)
            {
                case Phase.Settings:
                    if (Time.time < nextAt) return;
                    Check("settingsKeepsPhysicsRunning", Time.timeScale == 1 && Math.Abs(game.pusherBody.position.z - pusherAtOpen) > .001f);
                    Check("idleLowerRouletteAnglesStayStill", AnglesUnchanged(idleAngles));
                    Check("idleLowerPartitionsHaveZeroSpeed", controller.stations.All(s => Mathf.Abs(s.currentDividerSpeed) < .001f));
                    settingsUI.Close(); Check("closingSettingsReleasesPaidInput", !settingsUI.IsOpen && !game.SettingsOpen);
                    game.AddMedals(6); walletBefore = game.medals; returnedBefore = game.TotalReturnedMedals;
                    game.QueuePayoutMedals(6, false); Enter(Phase.SidePayout); break;
                case Phase.SidePayout:
                    if (sidePositions.Count < 6) return;
                    Check("normalPayoutAlternatesThreeLeftThreeRight", sidePositions.Take(6).Count(p => p.x < -3) == 3 && sidePositions.Take(6).Count(p => p.x > 3) == 3);
                    Check("sixSidePayoutPositionsDistinct", sidePositions.Take(6).Select(p => p.ToString("F4")).Distinct().Count() == 6);
                    var plate = game.pusherBody.GetComponent<Collider>();
                    float plateTop = game.transform.InverseTransformPoint(new Vector3(plate.bounds.center.x, plate.bounds.max.y, plate.bounds.center.z)).y;
                    Check("sidePayoutStartsAboveRearUpperPlate", sidePositions.Take(6).All(p => p.y > plateTop + .5f && Math.Abs(p.z - 1.8f) < .2f));
                    game.AddMedals(6); walletBefore = game.medals; returnedBefore = game.TotalReturnedMedals;
                    game.QueuePayoutMedals(6, true); Enter(Phase.TopPayout); break;
                case Phase.TopPayout:
                    if (topPositions.Count < 6) return;
                    Check("sixTopPayoutPositionsDistinct", topPositions.Take(6).Select(p => p.ToString("F4")).Distinct().Count() == 6);
                    Check("jackpotPayoutUsesHigherOutlet", topPositions.Take(6).All(p => p.y > sidePositions.Take(6).Max(s => s.y) + .8f));
                    Check("replicasDoNotAutoDoubleCredit", game.medals == walletBefore && game.TotalReturnedMedals == returnedBefore);
                    frontItem = payoutItems.FirstOrDefault(i => i != null && !i.collected && i.payoutAlreadyCredited);
                    Require(frontItem != null, "No payout coin remains for the actual front collector test.");
                    var collector = UnityEngine.Object.FindAnyObjectByType<MedalDropDetector>(); Require(collector != null, "Front collector missing.");
                    Place(frontItem.GetComponent<Rigidbody>(), collector.GetComponent<Collider>().bounds.center);
                    Enter(Phase.Collector); break;
                case Phase.Collector:
                    if (Time.fixedTime <= placedFixedTime || (frontItem != null && !frontItem.collected)) return;
                    Check("actualFrontCollectorDoesNotDoubleCreditPayout", game.medals == walletBefore && game.TotalReturnedMedals == returnedBefore);
                    StartNormalBurst(); break;
                case Phase.NormalPayoutBurst:
                    TickNormalBurst(); break;
                case Phase.OpenHoles:
                    if (Time.time < nextAt && holeItems.Any(i => i != null && !i.collected)) return;
                    Check("medalsFallThroughBothRealSideOpenings", holeItems.Count == 2 && holeItems.All(i => i == null || i.collected));
                    Check("sideLossPaysNothing", game.medals == walletBefore && game.TotalReturnedMedals == returnedBefore);
                    game.ConfigureSideHoles(0);
                    closedHoleItem = SpawnTestMedal(game.transform.TransformPoint(new Vector3(-4.5f, .6f, -1.3f)));
                    nextAt = Time.time + 1.1f; Enter(Phase.ClosedHoles); break;
                case Phase.ClosedHoles:
                    if (Time.time < nextAt) return;
                    Check("zeroWidthSealsDeckAndDisablesDrains", game.sideHoleTriggers.All(t => !t.gameObject.activeInHierarchy) &&
                        Math.Abs(game.sideHoleMainDeck.localScale.x - 10) < .01f && closedHoleItem != null && !closedHoleItem.collected &&
                        game.transform.InverseTransformPoint(closedHoleItem.transform.position).y > -.25f);
                    if (closedHoleItem != null) Freeze(closedHoleItem.GetComponent<Rigidbody>());
                    game.ConfigureSideHoles(settings.sideHoleWidth); FreezeBoard();
                    realCap = game.maxMedalsOnBoard; game.maxMedalsOnBoard = game.CountBoardItems(false);
                    queuedBefore = game.PendingPayoutMedals; game.QueuePayoutMedals(6, false);
                    nextAt = Time.time + .4f; Enter(Phase.Capacity); break;
                case Phase.Capacity:
                    if (Time.time < nextAt) return;
                    Check("fullBoardPausesPayoutWithoutDroppingQueue", game.PendingPayoutMedals == queuedBefore + 6);
                    int board = game.CountBoardItems(false); game.maxMedalsOnBoard = board + 2;
                    game.DropBonusMedals(2); long pending = game.PendingPayoutMedals; long paid = game.TotalPaidMedals;
                    PaidThrow();
                    Check("bonusReservationsDoNotBlockPaidInput", game.TotalPaidMedals == paid + 1 && game.PendingPayoutMedals == pending);
                    results["capacity"] = new { boardBefore = board, queuedAtFull = queuedBefore + 6, reservedWithBonus = pending, paidAfterReservation = game.TotalPaidMedals - paid };
                    game.maxMedalsOnBoard = realCap; Enter(Phase.Drain); break;
                case Phase.Drain:
                    if (game.PendingPayoutMedals != 0) return;
                    Check("queuedPayoutResumesAfterCapacityReturns", true);
                    ClearCredits(); Set(controller, "nextLotteryAt", float.PositiveInfinity); spinsBefore = controller.TotalSpins;
                    paidBefore = game.TotalPaidMedals; walletBefore = game.medals; returnedBefore = game.TotalReturnedMedals;
                    var paidForSlots = new List<MedalItem>();
                    for (int i = 0; i < 6; i++) paidForSlots.Add(PaidThrow());
                    Check("paidInputAloneDoesNotGrantSlotSpins", controller.TotalSpins == spinsBefore && controller.SpinCredits == 0);
                    controller.enabled = true;
                    var slotPockets = game.GetComponentsInChildren<MedalSlotPocket>().OrderBy(p => p.transform.position.x).ToArray();
                    Require(slotPockets.Length == 3 && paidForSlots.Take(3).All(item => item != null), "Three physical slot pockets and paid coins required.");
                    for (int i = 0; i < 3; i++)
                    {
                        var coin = paidForSlots[i]; var body = coin.GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true;
                        var slotProbe = coin.gameObject.AddComponent<MedalIntegratedSlotPocketProbe>(); slotProbe.ExpectedPocket = slotPockets[i];
                        var trigger = slotPockets[i].GetComponent<SphereCollider>();
                        Place(body, slotPockets[i].transform.TransformPoint(trigger.center));
                    }
                    Enter(Phase.Slots); break;
                case Phase.Slots:
                    Require(Now - phaseStarted < 20, "Three real slot pocket contacts did not finish their spins in 20 seconds.");
                    if (controller.TotalSpins < spinsBefore + 3 || controller.IsSlotSpinning) return;
                    Check("threeActualPaidCoinSlotPocketContactsStartThreeSpins", slotPocketContacts.Count == 3 && controller.TotalSpins == spinsBefore + 3);
                    Check("payoutTargetZeroDisablesSlotWinsAndRescue", controller.TotalSpins == spinsBefore + 3 && controller.SpinCredits == 0 &&
                        game.TotalReturnedMedals == returnedBefore && game.medals == walletBefore - 6 && controller.PendingBallDraws == 0);
                    Check("paidCountersExcludePayoutReplicas", game.TotalPaidMedals == paidBefore + 6);
                    CheckPoolLabels("afterPaidInput");
                    var resume = settings.CaptureValues(); resume.targetPayoutPercent = 90; resume.medalsPerSpin = 3; resume.slotSpinDuration = 1.4f;
                    Require(settings.TrySetValues(resume, out string message), message); settings.ApplyToGame(true); TestAdaptiveFeedback();
                    ClearDrawQueue(); ClearCredits(); CheckPoolLabels("afterSettingsReset"); StartRound(); break;
                case Phase.Rounds: TickRound(); break;
                case Phase.Stop:
                    if (Time.time < nextAt) return;
                    Check("allLowerRotorsStopAndPhysicalGatesReseal_" + roundIndex, AnglesUnchanged(stopAngles) &&
                        controller.stations.All(s => !s.IsDrawing && !s.IsRotating && Mathf.Abs(s.currentDividerSpeed) < .001f) && !controller.IsColorRoundActive && !upper.ColorGatesOpen && GatesAtTarget(false));
                    roundIndex++;
                    if (roundIndex < TargetRounds) StartRound();
                    else
                    {
                        Check("threeNaturalUpperDrawsAreObserved", naturalUpperCount == 3);
                        Check("sixColorVisitsKeepTheOriginalUpperBall", visits.Count == 6);
                        Check("allFourIndependentGuardFacesConsumed", successfulGuardCount == 4);
                        phase = Phase.Complete; Finish(true);
                    }
                    break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }

    private static void Initialize()
    {
        initialized = true; originalTimeScale = Time.timeScale; originalRandomState = UnityEngine.Random.state; Time.timeScale = 1;
        results["bootReadiness"] = Diagnostics();
        upper = controller.upperStation;
        originalUpperTimeout = upper == null ? 60f : upper.drawTimeout;
        Require(upper != null && upper.isUpperStation && controller.stations != null && controller.stations.Length == 3, "Upper/colored stations missing.");
        Require(!controller.IsUpperDrawing && !controller.IsColorRoundActive && !controller.IsSlotSpinning, "An incidental draw is already active.");
        game.StopThrowing(); controller.enabled = false; ClearCredits(); ClearDrawQueue();
        foreach (var input in UnityEngine.Object.FindObjectsByType<MedalInputHandler>()) input.enabled = false;
        var manager = game.GetComponent<PrizeDropManager>(); Require(manager != null, "Prize manager missing."); manager.enabled = false; manager.bonusMedalChance = 0;
        FreezeBoard();
        controller.OnUpperLotteryStarted += OnUpperStarted; controller.OnUpperLotteryFinished += OnUpperFinish; controller.OnColorSelectionStarted += OnLegacySelectorStarted; controller.OnColorSelectionFinished += OnLegacySelectorFinished; controller.OnLotteryFinished += OnColorFinish;
        game.OnPayoutMedalSpawned += OnPayout;
        savedPersistence = settings.persistSettings; originalOverride = settings.SettingsFileOverride;
        realSettingsPath = settings.SavePath; realSettingsHash = FileHash(realSettingsPath);
        temporarySettingsPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-expansion-20261004", "settings-play-" + Guid.NewGuid().ToString("N") + ".json");
        settings.SettingsFileOverride = temporarySettingsPath; settings.persistSettings = true;
        TestSettings(); CheckUpperGeometry(); CheckGreenPrefabs();
        idleAngles = Angles(); pusherAtOpen = game.pusherBody.position.z; nextAt = Time.time + .55f; Enter(Phase.Settings);
    }

    private static void TestSettings()
    {
        settingsUI.Open(); Check("settingsModalBlocksPaidInput", settingsUI.IsOpen && game.SettingsOpen);
        long paid = game.TotalPaidMedals; int wallet = game.medals; Set(game, "throwTimer", 0f); game.ThrowSingleMedal();
        Check("paidThrowIgnoredWhileSettingsOpen", game.TotalPaidMedals == paid && game.medals == wallet);
        string snapshot = JsonUtility.ToJson(settings.CaptureValues()); int[] pools = (int[])controller.jackpotPools.Clone(); float holeWidth = game.sideHoleWidth;
        settingsUI.jackpotInputs[0].text = "123"; settingsUI.slotSpinDurationInput.text = "数値ではない";
        settingsUI.ApplyFromInputs();
        Check("invalidSettingsAreAtomic", JsonUtility.ToJson(settings.CaptureValues()) == snapshot && pools.SequenceEqual(controller.jackpotPools) && game.sideHoleWidth == holeWidth && settingsUI.IsOpen && !File.Exists(temporarySettingsPath));
        settingsUI.Open();
        string[] jp = { "7", "11", "13" }; for (int i = 0; i < 3; i++) settingsUI.jackpotInputs[i].text = jp[i];
        settingsUI.targetPayoutInput.text = "0"; settingsUI.medalsPerSpinInput.text = "2"; settingsUI.slotSpinDurationInput.text = "0.1";
        settingsUI.slotBallChanceInput.text = "49"; settingsUI.slotMedalChanceInput.text = "21";
        settingsUI.rescueSpinsInput.text = "4"; settingsUI.sideHoleWidthInput.text = "0.55";
        settingsUI.ApplyFromInputs(); savedValues = settings.CaptureValues();
        Check("validSettingsApplyPoolsAndTargetImmediately", settingsUI.IsOpen && settings.targetPayoutPercent == 0 && controller.targetPayoutPercent == 0 &&
            controller.medalsPerSpin == 2 && Math.Abs(controller.slotSpinDuration - .1f) < .001f && controller.jackpotPools.SequenceEqual(new[] { 7, 11, 13 }) &&
            Math.Abs(game.sideHoleWidth - .55f) < .001f);
        Check("settingsSaveUsesIsolatedVersionOneJson", File.Exists(temporarySettingsPath) && JsonUtility.FromJson<MedalArcadeSettings.Values>(File.ReadAllText(temporarySettingsPath)).version == 1 &&
            !string.Equals(settings.SavePath, realSettingsPath, StringComparison.OrdinalIgnoreCase));
        var changed = settings.CaptureValues(); changed.targetPayoutPercent = 50; changed.jackpotResetValues = new[] { 22, 33, 44 };
        Require(settings.TrySetValues(changed, out string error), error); settings.ApplyToGame(true);
        bool loaded = settings.Load(); settings.ApplyToGame(true);
        Check("savedJsonReloadRestoresAllValues", loaded && settings.LoadedFromDisk && JsonUtility.ToJson(settings.CaptureValues()) == JsonUtility.ToJson(savedValues) && controller.jackpotPools.SequenceEqual(new[] { 7, 11, 13 }));
        settings.persistSettings = false; settingsUI.Open();
        CheckPoolLabels("afterSettingsApply");
        Capture("upper-settings.png", null);
    }

    private static void Freeze(Rigidbody body)
    {
        if (body == null) return;
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
    }
    private static void FreezeBoard() { foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>()) if (!item.collected) Freeze(item.GetComponent<Rigidbody>()); }
    private static MedalItem PaidThrow()
    {
        Set(game, "throwTimer", 0f);
        var before = new HashSet<MedalItem>(game.itemsRoot.GetComponentsInChildren<MedalItem>());
        game.ThrowSingleMedal();
        MedalItem inserted = null;
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>()) if (!before.Contains(item)) { Freeze(item.GetComponent<Rigidbody>()); inserted = item; }
        return inserted;
    }

    public static void ObserveSlotPocketContact(MedalSlotPocket expected, Collider actual)
    {
        if (!SessionState.GetBool(Key, false) || actual == null || expected == null || actual.GetComponent<MedalSlotPocket>() != expected) return;
        slotPocketContacts.Add(new { pocket = expected.name, pocketId = expected.GetEntityId().ToString(), position = Pos(expected.transform.position), fixedTime = Time.fixedTime });
    }
    private static MedalItem SpawnTestMedal(Vector3 position)
    {
        var go = UnityEngine.Object.Instantiate(game.medalPrefab, position, game.transform.rotation, game.itemsRoot);
        go.name = "UpperFlow_TestMedal"; var item = go.GetComponent<MedalItem>(); item.isPrize = false; item.isBall = false; item.collected = false; item.payoutAlreadyCredited = false;
        var body = go.GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        game.RegisterItem(item); return item;
    }
    private static void Place(Rigidbody body, Vector3 position)
    {
        Require(body != null && !body.isKinematic, "A real dynamic ball/coin is required for routing.");
        body.position = position; body.transform.position = position; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero;
        body.WakeUp(); Physics.SyncTransforms(); placedFixedTime = Time.fixedTime;
    }
    private static void OnPayout(GameObject coin, bool fromTop)
    {
        if (coin == null) return;
        var item = coin.GetComponent<MedalItem>(); var body = coin.GetComponent<Rigidbody>();
        Vector3 local = game.transform.InverseTransformPoint(coin.transform.position);
        payouts.Add(new { name = coin.name, fromTop, position = Pos(local), credited = item != null && item.payoutAlreadyCredited,
            dynamic = body != null && !body.isKinematic, playSeconds = Time.time });
        if (fromTop) topPositions.Add(local); else sidePositions.Add(local);
        if (item != null) { payoutItems.Add(item); if (!item.payoutAlreadyCredited) Freeze(body); }
        if (burstActive && !fromTop) ObserveBurstSpawn(coin, item, body, local);
    }

    private static void StartNormalBurst()
    {
        Require(game.PendingPayoutMedals == 0, "Earlier payout queue must finish before the 500-medal burst.");
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
            foreach (var collider in item.GetComponentsInChildren<Collider>())
            {
                burstIgnoredColliderStates[collider] = collider.enabled;
                collider.enabled = false;
            }
        burstCoins.Clear(); burstActive = true; burstMaxBoard = burstMaxFront = burstMaxRear = 0;
        burstLastSpawnAt = burstNextSampleAt = Time.time;
        burstPendingBefore = game.PendingPayoutMedals;
        game.AddMedals(BurstCount); burstWalletBefore = game.medals; burstReturnedBefore = game.TotalReturnedMedals;
        Enter(Phase.NormalPayoutBurst); game.QueuePayoutMedals(BurstCount, false);
        Check("normalBurstQueuesAllFiveHundred", game.PendingPayoutMedals == burstPendingBefore + BurstCount);
    }

    private static void ObserveBurstSpawn(GameObject coin, MedalItem item, Rigidbody body, Vector3 local)
    {
        var record = new BurstCoin { item = item, spawn = local, spawnedAt = Time.time,
            dynamic = body != null && !body.isKinematic, credited = item != null && item.payoutAlreadyCredited };
        int index = burstCoins.Count; burstCoins.Add(record); burstLastSpawnAt = Time.time;
        var probe = coin.AddComponent<MedalIntegratedUpperFlowPayoutProbe>(); probe.RecordIndex = index;
        var ownCollider = coin.GetComponent<Collider>();
        if (ownCollider == null) return;
        Physics.SyncTransforms();
        var bounds = ownCollider.bounds;
        foreach (var other in Physics.OverlapBox(bounds.center, bounds.extents + Vector3.one * .002f))
        {
            if (other == null || !other.enabled || other.isTrigger || other == ownCollider ||
                other.attachedRigidbody == body || other.transform.IsChildOf(coin.transform)) continue;
            if (!Physics.ComputePenetration(ownCollider, ownCollider.transform.position, ownCollider.transform.rotation,
                other, other.transform.position, other.transform.rotation, out _, out float depth) || depth <= .002f) continue;
            bool otherCoin = other.GetComponentInParent<MedalItem>() != null;
            if (otherCoin) record.coinOverlaps++; else record.geometryOverlaps++;
            record.spawnOverlaps.Add(new { name = other.name, parent = other.transform.parent == null ? null : other.transform.parent.name, otherCoin, depth });
        }
    }

    // This is called only by transient Play-mode probes attached to the burst's real dynamic medals.
    internal static void ObserveBurstCollision(int index, Collision collision)
    {
        if (!burstActive || index < 0 || index >= burstCoins.Count || game == null || collision.collider == null) return;
        var record = burstCoins[index];
        if (!record.firstContactEvaluated && collision.contactCount > 0)
        {
            record.firstContactEvaluated = true; record.firstContactObject = collision.collider.name;
            record.firstContactDelay = Time.time - record.spawnedAt;
            var plateBounds = game.pusherBody.GetComponent<Collider>().bounds;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                bool upperArrival = Vector3.Dot(contact.normal, game.transform.up) > .5f &&
                    contact.point.x >= plateBounds.min.x - .05f && contact.point.x <= plateBounds.max.x + .05f &&
                    contact.point.z >= plateBounds.min.z - .05f && contact.point.z <= plateBounds.max.z + .05f &&
                    contact.point.y >= plateBounds.max.y - .05f;
                if (!record.firstContactPosition.HasValue || upperArrival)
                {
                    record.firstContactPosition = game.transform.InverseTransformPoint(contact.point);
                    record.firstContactNormal = game.transform.InverseTransformDirection(contact.normal);
                }
                record.firstContactIsUpper |= upperArrival;
            }
        }
        if (collision.collider.GetComponentInParent<MedalItem>() != null) return;
        bool plate = collision.collider.attachedRigidbody == game.pusherBody || collision.collider.transform.IsChildOf(game.pusherBody.transform);
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            Vector3 point = game.transform.InverseTransformPoint(contact.point);
            if (record.firstSurface == null)
            {
                record.firstSurface = collision.collider.name; record.firstSurfacePosition = point;
                record.firstSurfaceIsPlate = plate;
                record.firstSurfaceDelay = Time.time - record.spawnedAt;
            }
            if (plate && !record.firstPlatePosition.HasValue) record.firstPlatePosition = point;
            if (plate && Vector3.Dot(contact.normal, game.transform.up) > .5f) record.hitPlateTop = true;
        }
    }

    private static void TickNormalBurst()
    {
        if (game.PendingPayoutMedals + burstCoins.Count != burstPendingBefore + BurstCount)
            CheckOnce("normalBurstPreservesGeneratedPlusQueuedCount", false);
        if (Time.time >= burstNextSampleAt)
        {
            burstNextSampleAt = Time.time + 1f;
            var active = burstCoins.Where(c => c.item != null && !c.item.collected).ToArray();
            burstMaxBoard = Math.Max(burstMaxBoard, active.Length);
            burstMaxFront = Math.Max(burstMaxFront, active.Count(c => game.transform.InverseTransformPoint(c.item.transform.position).z < -1f));
            burstMaxRear = Math.Max(burstMaxRear, active.Count(c => game.transform.InverseTransformPoint(c.item.transform.position).z >= 0));
        }
        if (burstCoins.Count < BurstCount || game.PendingPayoutMedals != burstPendingBefore || Time.time - burstLastSpawnAt < 3f) return;
        var firstThreeHundred = burstCoins.Take(300).ToArray();
        int plateTopCount = firstThreeHundred.Count(c => c.hitPlateTop);
        int firstPlateCount = firstThreeHundred.Count(c => c.firstSurfaceIsPlate && c.hitPlateTop);
        int upperArrivalCount = firstThreeHundred.Count(c => c.firstContactIsUpper);
        int frontFirstContact = firstThreeHundred.Count(c => c.firstContactPosition.HasValue && c.firstContactPosition.Value.z < -1f);
        var upperArrivals = burstCoins.Where(c => c.firstContactIsUpper && c.firstContactPosition.HasValue).Select(c => c.firstContactPosition.Value).ToArray();
        Check("normalBurstPreservesGeneratedPlusQueuedCount", game.PendingPayoutMedals + burstCoins.Count == burstPendingBefore + BurstCount);
        Check("normalBurstSpawnsFiveHundredDynamicCreditedMedals", burstCoins.Count == BurstCount && burstCoins.All(c => c.dynamic && c.credited));
        Check("normalBurstHasNoCoinOrGeometryOverlapAtSpawn", burstCoins.All(c => c.coinOverlaps == 0 && c.geometryOverlaps == 0));
        Check("normalBurstFirstThreeHundredArriveOnUpperPlateOrItsPile", upperArrivalCount >= 240);
        Check("normalBurstAvoidsInitialFrontPile", frontFirstContact <= 30);
        Check("normalBurstSpreadsAcrossUpperPlate", upperArrivals.Length >= 240 &&
            upperArrivals.Max(p => p.x) - upperArrivals.Min(p => p.x) > 5f && upperArrivals.Max(p => p.z) - upperArrivals.Min(p => p.z) > 1.1f);
        Check("normalBurstDoesNotDoubleCreditWallet", game.medals == burstWalletBefore && game.TotalReturnedMedals == burstReturnedBefore);
        results["normalPayoutBurst"] = new { requested = BurstCount, generated = burstCoins.Count, pending = game.PendingPayoutMedals,
            firstThreeHundredUpperPlateContacts = plateTopCount, firstThreeHundredInitialPlateContacts = firstPlateCount,
            firstThreeHundredUpperArrivals = upperArrivalCount,
            firstThreeHundredFrontContacts = frontFirstContact,
            coinSpawnOverlaps = burstCoins.Sum(c => c.coinOverlaps), geometrySpawnOverlaps = burstCoins.Sum(c => c.geometryOverlaps),
            maxActiveBoardMedals = burstMaxBoard, maxFrontMedals = burstMaxFront, maxRearMedals = burstMaxRear,
            elapsedWallSeconds = Now - phaseStarted };
        CleanupNormalBurst();
        CheckSideGeometry(); game.ConfigureSideHoles(1f); walletBefore = game.medals; returnedBefore = game.TotalReturnedMedals;
        foreach (var hole in game.sideHoleTriggers) holeItems.Add(SpawnTestMedal(hole.position + Vector3.up * 1.3f));
        nextAt = Time.time + 3f; Enter(Phase.OpenHoles);
    }

    private static void CleanupNormalBurst()
    {
        burstActive = false;
        foreach (var record in burstCoins)
            if (record.item != null)
            {
                record.item.collected = true; game.UnregisterItem(record.item); UnityEngine.Object.Destroy(record.item.gameObject);
            }
        foreach (var entry in burstIgnoredColliderStates) if (entry.Key != null) entry.Key.enabled = entry.Value;
        burstIgnoredColliderStates.Clear();
    }

    private static void CheckSideGeometry()
    {
        Check("twoPhysicalSideDrainsAndFourDeckStrips", game.sideHoleTriggers != null && game.sideHoleTriggers.Length == 2 &&
            game.sideHoleTriggers.All(t => t != null && t.GetComponent<MedalSideHole>() != null && t.GetComponent<Collider>().isTrigger) &&
            game.sideHoleDeckStrips != null && game.sideHoleDeckStrips.Length == 4 && game.sideHoleDeckStrips.All(t => t.GetComponent<Collider>() != null));
    }
    private static void CheckUpperGeometry()
    {
        Check("upperHasFourPhysicalTwoWinBumpersAndNoWinPockets", upper.usesBumpers && upper.GetComponentsInChildren<MedalLotteryPocket>(true).Length == 0 &&
            upper.bumpers != null && upper.bumpers.Length == 4 && upper.bumpers.Distinct().Count() == 4 &&
            upper.bumpers.All(b => b != null && b.station == upper && b.GetComponent<SphereCollider>() != null && !b.GetComponent<SphereCollider>().isTrigger));
        Check("separateFrontSelectorStageIsAbsent", !upper.GetComponentsInChildren<Transform>(true).Any(t => t.name == "ColorSelectionStage") &&
            upper.colorLaunchPoint == null && !upper.IsSelectingColor && !controller.IsSelectingColor);
        Check("threeIntegratedPortsHaveSolidClosedGates", upper.colorRoutePockets != null && upper.colorRoutePockets.Length == 3 &&
            upper.colorRoutePockets.All(p => p != null && p.station == upper && p.GetComponent<Collider>().isTrigger) &&
            upper.colorRoutePockets.Select(p => p.kind).Distinct().Count() == 3 && upper.colorGateBodies != null && upper.colorGateBodies.Length == 3 &&
            upper.colorGateBodies.All(b => b != null && b.isKinematic && b.GetComponent<Collider>().enabled && !b.GetComponent<Collider>().isTrigger) && !upper.ColorGatesOpen && GatesAtTarget(false));
        Check("fourIndependentWhiteFrontGuardsHaveSolidColliders", upper.outBlocks != null && upper.outBlockBodies != null && upper.outBlockClosedPositions != null &&
            upper.outBlocks.Length == 4 && upper.outBlockBodies.Length == 4 && upper.outBlockClosedPositions.Length == 4 &&
            Enumerable.Range(0, 4).All(i => upper.outBlocks[i] != null && upper.outBlocks[i].station == upper && upper.outBlockBodies[i] != null &&
                upper.outBlockBodies[i].isKinematic && upper.outBlockBodies[i].GetComponent<Collider>() != null && !upper.outBlockBodies[i].GetComponent<Collider>().isTrigger) &&
            upper.UsedOutBlockCount == 0 && !GuardStates().Any(used => used));
        var guards = upper.outBlockBodies.Select(b => upper.transform.InverseTransformPoint(b.GetComponent<Collider>().bounds.center)).ToArray();
        Check("fourFrontGuardsAreEquallySpacedAcrossPurpleFront", guards.Length == 4 && Enumerable.Range(0, 3).All(i => Math.Abs(guards[i + 1].x - guards[i].x - .6f) < .03f) &&
            guards.All(p => p.z < upper.guideLocalCenter.z - 2f) && guards.Max(p => p.z) - guards.Min(p => p.z) < .02f);
        Check("actualFrontOutflowRemainsBehindGuards", upper.upperOutflow != null && upper.upperOutflow.station == upper && upper.upperOutflow.GetComponent<Collider>().isTrigger &&
            upper.transform.InverseTransformPoint(upper.upperOutflow.transform.position).z < guards.Min(p => p.z) - .2f);
        Vector3 red = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Ruby).transform.position);
        Vector3 blue = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Sapphire).transform.position);
        Vector3 yellow = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Amber).transform.position);
        Check("integratedRoutesAreRedLeftBlueRightYellowRear", red.x < 0 && blue.x > 0 && yellow.z > red.z + 1f && yellow.z > blue.z + 1f);
        Check("worldAndHudRemainingGuardTextAreAbsent", upper.outBlockText == null && !upper.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("OutBlockMark", StringComparison.Ordinal)) &&
            !UnityEngine.Object.FindObjectsByType<TMP_Text>().Any(t => t.isActiveAndEnabled && t.text.Contains("ガード")));
        results["integratedGeometry"] = new { red = Pos(red), blue = Pos(blue), yellow = Pos(yellow), guards = guards.Select(Pos).ToArray() };
        CheckPoolLabels("initialPools");
    }

    private static bool Green(GameObject prefab)
    {
        if (prefab == null || prefab.GetComponent<Renderer>() == null) return false;
        var material = prefab.GetComponent<Renderer>().sharedMaterial; if (material == null) return false;
        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        return color.g > color.r * 1.4f && color.g > color.b * 1.1f;
    }
    private static void CheckGreenPrefabs()
    {
        Require(controller.stations.All(s => s.dividerRotor != null && s.dividerBody != null && s.dividerBody.isKinematic), "Colored moving partition rotors are missing.");
        Check("drawBallsAreSteelAndBoardBallsRemainGreen", Steel(upper.lotteryBallPrefab) && controller.stations.All(s => Steel(s.lotteryBallPrefab)) &&
            controller.ballPrefabs != null && controller.ballPrefabs.Length == 3 && controller.ballPrefabs.All(Green));
        Check("legacyNonBallPrizesAndPrefabsAreAbsent", (game.prizePrefabs == null || game.prizePrefabs.Length == 0) &&
            !game.itemsRoot.GetComponentsInChildren<MedalItem>(true).Any(item => item.isPrize && !item.isBall));
        var prizeManager = game.GetComponent<PrizeDropManager>();
        Check("legacyPrizeSpawnerIsEmpty", prizeManager != null && !prizeManager.spawnGenericPrizes && prizeManager.minPrizesOnBoard == 0 &&
            (prizeManager.prizeTypes == null || prizeManager.prizeTypes.Length == 0));
        Check("physicalSlotPocketsRemainWhileLaneSelectorsStayAbsent", game.medalInlets != null && game.medalInlets.Length == 3 &&
            game.GetComponentsInChildren<MedalSlotPocket>(true).Length == 3 && (arcade.inletButtons == null || arcade.inletButtons.Length == 0) && (arcade.inletLights == null || arcade.inletLights.Length == 0) &&
            !UnityEngine.Object.FindObjectsByType<Transform>().Any(t => t.name.StartsWith("InletButton_", StringComparison.Ordinal)));
        Check("coloredPhysicalJackpotLabelsUseJackpot", controller.stations.All(s => s.jackpotPocketText != null && s.jackpotPocketText.text.Contains("JACKPOT") && !s.jackpotPocketText.text.Contains("大当たり")));
    }
    private static bool Steel(GameObject prefab)
    {
        var material = prefab == null ? null : prefab.GetComponent<Renderer>()?.sharedMaterial;
        if (material == null || !material.HasProperty("_Metallic")) return false;
        Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        return material.GetFloat("_Metallic") >= .85f && Mathf.Max(color.r, color.g, color.b) - Mathf.Min(color.r, color.g, color.b) < .2f;
    }
    private static void CheckPoolLabels(string suffix)
    {
        bool physical = Enumerable.Range(0, 3).All(i => controller.stations[i].jackpotPocketText != null && controller.stations[i].jackpotPocketText.text == "JACKPOT\n" + controller.jackpotPools[i] + "枚");
        Check("physicalPocketAmountsMatchPools_" + suffix, physical);
        bool tower = arcade.towerJackpotValues != null && arcade.towerJackpotKinds != null && arcade.towerJackpotValues.Length == 4 && arcade.towerJackpotKinds.SequenceEqual(new[] { -1, 0, 2, 1 });
        if (tower)
            for (int i = 0; i < 4; i++)
            {
                var text = arcade.towerJackpotValues[i]; int kind = arcade.towerJackpotKinds[i];
                tower &= text != null && (kind == -1 ? Enumerable.Range(0, 3).All(k => text.text.Contains(controller.jackpotPools[k] + "枚")) :
                    kind >= 0 && kind < 3 && text.text.Contains(controller.jackpotPools[kind] + "枚"));
            }
        Check("fourTowerAmountsMatchFacePools_" + suffix, tower);
    }
    private static void TestAdaptiveFeedback()
    {
        var method = controller.GetType().GetMethod("AdaptiveSlotFactor", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(method != null, "AdaptiveSlotFactor is unavailable.");
        long oldPaid = game.TotalPaidMedals, oldReturned = game.TotalReturnedMedals;
        try
        {
            Set(game, "<TotalPaidMedals>k__BackingField", 100L);
            Set(game, "<TotalReturnedMedals>k__BackingField", 100000L);
            float aboveTarget = (float)method.Invoke(controller, null);
            Set(game, "<TotalReturnedMedals>k__BackingField", 25L);
            float belowTarget = (float)method.Invoke(controller, null);
            Check("adaptivePayoutFeedbackCanReduceBelowQuarter", aboveTarget >= .004999f && aboveTarget < .01f && belowTarget > aboveTarget && belowTarget <= 1.50001f);
            results["adaptiveFeedback"] = new { simulatedPaid = 100, highReturned = 100000, lowReturned = 25, aboveTarget, belowTarget, ledgerRestored = true };
        }
        finally { Set(game, "<TotalPaidMedals>k__BackingField", oldPaid); Set(game, "<TotalReturnedMedals>k__BackingField", oldReturned); }
    }
    private static Quaternion[] Angles() => controller.stations.SelectMany(s => new[] {
        s.rotorBody != null ? s.rotorBody.rotation : s.rotor.rotation,
        s.dividerBody != null ? s.dividerBody.rotation : s.dividerRotor.rotation
    }).ToArray();
    private static bool AnglesUnchanged(Quaternion[] before)
    {
        var after = Angles(); return before != null && before.Length == after.Length && Enumerable.Range(0, before.Length).All(i => Quaternion.Angle(before[i], after[i]) < .05f);
    }
    private static bool GatesAtTarget(bool open) => upper.colorGateBodies != null && upper.colorGateClosedPositions != null && upper.colorGateBodies.Length == 3 &&
        Enumerable.Range(0, 3).All(i => Vector3.Distance(upper.colorGateBodies[i].position,
            upper.colorGateBodies[i].transform.parent.TransformPoint(upper.colorGateClosedPositions[i] + Vector3.up * (open ? upper.colorGateRaiseHeight : 0))) < .025f);

    private static bool[] GuardStates() => Enumerable.Range(0, 4).Select(upper.IsOutBlockUsed).ToArray();
    private static bool SameUpperIdentity() => upperToken != null && upperToken.GetEntityId().ToString() == upperObjectId && ReferenceEquals(upperToken, upper.ActiveBall) &&
        upperToken.station == upper && upperToken.ticket == ticket && upper.CurrentTicket == ticket && !upperToken.IsConsumed && controller.IsUpperDrawing;
    private static bool GuardTargetsMatch() => Enumerable.Range(0, 4).All(i => Vector3.Distance(upper.outBlockBodies[i].position,
        upper.outBlockBodies[i].transform.parent.TransformPoint(upper.outBlockClosedPositions[i] + Vector3.up * (upper.IsOutBlockUsed(i) ? upper.outBlockRaiseHeight : 0))) < .04f);
    private static void OnUpperStarted() => upperStartEvents++;
    private static void OnLegacySelectorStarted() => legacyEvents++;
    private static void OnLegacySelectorFinished(MedalJackpotKind? kind, bool timedOut) => legacyEvents++;
    private static void OnUpperFinish(int payout)
    {
        upperEvents++; lastUpperWin = payout; upperTimedOut = upper.LastTimedOut; upperExitedBowl = upper.LastExitedBowl;
        finalUpperHits = upper.TotalBumperHits; finalUpperWin = upper.BumperWin;
        upperEndedWithOriginalToken = upperToken != null && upperToken.station == upper && upperToken.ticket == ticket && upperToken.IsConsumed;
        if (upperToken != null) finalUpperBallLocalPosition = upper.transform.InverseTransformPoint(upperToken.GetComponent<Rigidbody>().position);
    }
    private static void OnColorFinish(MedalJackpotKind kind, int payout, bool jackpot)
    {
        colorEvents++; finishedKind = kind; lastColorPayout = payout; finishedJackpot = jackpot; visitFinishedAt = Now;
        if (roundIndex > 0) earnedColorTotal += payout;
        // The forced fixture examines the returned state before requesting its
        // next contact; natural rounds continue without this test placement.
        if (roundIndex == 0 && phase == Phase.Rounds && SameUpperIdentity() && !upper.IsUpperSuspended) ParkUpperBall();
    }
    private static void ClearFixtureBoard()
    {
        int wallet = game.medals; long queued = game.PendingPayoutMedals;
        var removed = new List<object>();
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>(true))
        {
            var body = item.GetComponent<Rigidbody>();
            removed.Add(new { name = item.name, entityId = item.GetEntityId().ToString(), item.isBall, item.isPrize,
                item.payoutAlreadyCredited, item.collected, kinematic = body != null && body.isKinematic,
                position = Pos(game.transform.InverseTransformPoint(item.transform.position)) });
            item.collected = true; game.UnregisterItem(item); UnityEngine.Object.Destroy(item.gameObject);
        }
        Check("fixtureCleanupPreservesWalletAndPayoutQueue_" + roundIndex, game.medals == wallet && game.PendingPayoutMedals == queued &&
            game.CountBoardItems(false) == 0 && game.CountBoardItems(true) == 0);
        fixtureBoundaries.Add(new { round = roundIndex, walletBefore = wallet, walletAfter = game.medals,
            pendingBefore = queued, pendingAfter = game.PendingPayoutMedals, removed = removed.ToArray(),
            slotsSpinning = controller.IsSlotSpinning, slotCredits = controller.SpinCredits, totalSpins = controller.TotalSpins,
            readiness = Diagnostics() });
    }
    private static void StartRound()
    {
        Require(!controller.IsUpperDrawing && !controller.IsColorRoundActive && !controller.ActiveKind.HasValue, "Previous integrated round is still active.");
        RestorePocketStates(); ClearDrawQueue(); ClearCredits(); upper.drawTimeout = originalUpperTimeout;
        // The preceding Slots phase already verifies real pocket entry. During
        // upper fixtures, keep its geometry while preventing credited payout
        // coins from starting independent slot awards against this ledger.
        foreach (var pocket in game.GetComponentsInChildren<MedalSlotPocket>(true))
        {
            if (!upperFixtureSlotStates.ContainsKey(pocket)) upperFixtureSlotStates[pocket] = pocket.enabled;
            pocket.enabled = false;
        }
        Check("upperFixtureDisablesOnlyAuxiliarySlotEntries_" + roundIndex, upperFixtureSlotStates.Count == 3 &&
            upperFixtureSlotStates.Keys.All(p => p != null && !p.enabled && p.GetComponent<Collider>() != null &&
                p.GetComponent<Collider>().enabled && p.GetComponent<Collider>().isTrigger));
        ClearFixtureBoard();
        upperBaseline = upperEvents; mainUpperStartBaseline = upperStartEvents;
        roundWallet = game.medals; earnedColorTotal = 0; roundStarted = Now; ticket = 0; upperToken = null; upperRouted = false;
        actualBumperContacts = finalUpperHits = finalUpperWin = 0; upperExitedBowl = upperTimedOut = upperEndedWithOriginalToken = false;
        finalUpperBallLocalPosition = null; visitIndex = visitStage = guardIndex = guardStage = 0;
        physicalGuardContacts.Clear(); pulseStage = 0; nextNaturalSample = Time.time;
        if (roundIndex > 0) UnityEngine.Random.InitState(naturalSeeds[roundIndex - 1]);
        controller.QueueBallDraw(MedalJackpotKind.Ruby); Set(controller, "nextLotteryAt", 0f); controller.enabled = true;
        integratedStep = IntegratedStep.WaitBall; Enter(Phase.Rounds);
    }
    private static void ParkUpperBall()
        // Keep the forced-contact fixture above the deck during gate/rotor waits.
        // It remains the same dynamic sphere; natural rounds never use this hold.
        => Place(upperToken.GetComponent<Rigidbody>(), upper.transform.TransformPoint(upper.guideLocalCenter + Vector3.up * 10f));
    private static void BeginPulses(int count, IntegratedStep next)
    { pulseTargetHits = count; pulseStage = 0; integratedStep = next; }
    private static bool TickPulses()
    {
        Require(SameUpperIdentity() && upper.IsDrawing && !upper.IsUpperSuspended, "Physical bumper pulse lost the active original upper ball.");
        Require(upper.TotalBumperHits <= pulseTargetHits, "Unexpected extra bumper hits while routing the test ball.");
        if (pulseStage == 1)
        {
            if (upper.TotalBumperHits == pulseBefore)
            { Require(Time.fixedTime - placedFixedTime < .5f, "The incoming original ball did not contact its real bumper."); return false; }
            Check("actualBumperContactAddsExactlyTwo_hit" + upper.TotalBumperHits, Time.fixedTime > placedFixedTime && upper.TotalBumperHits == pulseBefore + 1 &&
                upper.BumperWin == upper.TotalBumperHits * 2 && controller.LiveUpperWin == upper.BumperWin && upper.LastBumper == targetBumper &&
                game.medals == roundWallet + earnedColorTotal && upperEvents == upperBaseline && !controller.IsColorRoundActive && legacyEvents == 0);
            ParkUpperBall(); pulseNextFixed = Time.fixedTime + .04f; pulseStage = 2; return false;
        }
        if (pulseStage == 2 && Time.fixedTime < pulseNextFixed) return false;
        if (upper.TotalBumperHits == pulseTargetHits) return true;
        targetBumper = upper.bumpers[upper.TotalBumperHits % 4]; var collider = targetBumper.GetComponent<SphereCollider>();
        Vector3 normal = collider.bounds.center - upper.transform.TransformPoint(upper.guideLocalCenter); normal.y = 0; normal.Normalize();
        float radius = upperToken.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = collider.ClosestPoint(collider.bounds.center + normal * (collider.bounds.extents.magnitude + 1));
        Place(upperToken.GetComponent<Rigidbody>(), face + normal * (radius + .025f)); upperToken.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
        pulseBefore = upper.TotalBumperHits; pulseStage = 1; return false;
    }
    private static void NegativeUpperChecks()
    {
        int win = upper.BumperWin, wallet = game.medals, tokenTicket = upperToken.ticket; var tokenStation = upperToken.station;
        upperToken.ticket++; bool wrongTicket = !upper.TryHitBumper(upper.bumpers[0], upperToken) && !upper.TryResolveColor(upper.colorRoutePockets[0], upperToken);
        upperToken.ticket = tokenTicket; upperToken.station = controller.stations[0];
        bool wrongStation = !upper.TryHitBumper(upper.bumpers[0], upperToken) && !upper.TryResolveColor(upper.colorRoutePockets[0], upperToken); upperToken.station = tokenStation;
        controller.NotifyUpperBumperHit(controller.stations[0], ticket, win + 2); controller.NotifyUpperBumperHit(upper, ticket + 999, win + 2);
        controller.NotifyUpperBumperHit(upper, ticket, win + 99); controller.FinishLottery(upper, ticket, false, 9999);
        controller.FinishColorSelection(upper, ticket, MedalJackpotKind.Ruby, false);
        Check("wrongUpperTokenAndPrematureCallbacksDoNotPay_" + roundIndex, wrongTicket && wrongStation && upper.BumperWin == win &&
            controller.LiveUpperWin == win && game.medals == wallet && upperEvents == upperBaseline && SameUpperIdentity() && !controller.IsColorRoundActive);
    }
    private static void TickRound()
    {
        Require(Now - roundStarted < 180, "Integrated round exceeded 180 wall-clock seconds: " + roundIndex);
        if (integratedStep == IntegratedStep.WaitBall)
        {
            if (upper.ActiveBall == null) return;
            upperToken = upper.ActiveBall; upperObjectId = upperToken.GetEntityId().ToString(); ticket = upper.CurrentTicket;
            var probe = upperToken.gameObject.AddComponent<MedalIntegratedUpperFlowBumperProbe>(); probe.Round = roundIndex; probe.Token = upperToken;
            Check("newSteelBallResetsWinAndAllFourGuards_" + roundIndex, upperToken.station == upper && upperToken.ticket == ticket && Steel(upper.lotteryBallPrefab) &&
                upper.BumperWin == 0 && upper.TotalBumperHits == 0 && controller.LiveUpperWin == 0 && upper.UsedOutBlockCount == 0 && !GuardStates().Any(used => used) &&
                !upper.ColorGatesOpen && !controller.AreColorGatesUnlocked && upperStartEvents == mainUpperStartBaseline + 1);
            NegativeUpperChecks();
            if (roundIndex == 0) { blueBeforeForcedHits = controller.jackpotPools[1]; ParkUpperBall(); BeginPulses(50, IntegratedStep.PulseTo100); }
            else integratedStep = IntegratedStep.Natural;
        }
        switch (integratedStep)
        {
            case IntegratedStep.PulseTo100:
                if (!TickPulses()) return; holdUntilFixed = Time.fixedTime + .35f; idleAngles = Angles(); integratedStep = IntegratedStep.Hold100; break;
            case IntegratedStep.Hold100:
                if (Time.fixedTime < holdUntilFixed) return;
                int wallet = game.medals; bool closedRejected = upper.colorRoutePockets.All(p => !upper.TryResolveColor(p, upperToken));
                Check("fiftyActualHitsHave100WinWithAllPortsClosed", actualBumperContacts == 50 && upper.BumperWin == 100 && controller.LiveUpperWin == 100 &&
                    closedRejected && game.medals == wallet && SameUpperIdentity() && !upper.ColorGatesOpen && !controller.AreColorGatesUnlocked && GatesAtTarget(false) &&
                    AnglesUnchanged(idleAngles) && controller.stations.All(s => !s.IsDrawing && !s.IsRotating && Mathf.Abs(s.currentDividerSpeed) < .001f));
                Check("blueJPAddsOnePerActualBumperHit", controller.jackpotPools[1] == blueBeforeForcedHits + 50);
                BeginPulses(51, IntegratedStep.PulseTo102); break;
            case IntegratedStep.PulseTo102:
                if (!TickPulses()) return; holdUntilFixed = Time.fixedTime + .65f; roundAngles = Angles(); integratedStep = IntegratedStep.HoldOpen; break;
            case IntegratedStep.HoldOpen:
                if (Time.fixedTime < holdUntilFixed) return;
                var after = Angles();
                Check("fiftyFirstActualHitUnlocksThreePortsAndRotatesAllClunes", upper.BumperWin == 102 && controller.LiveUpperWin == 102 && SameUpperIdentity() &&
                    upper.ColorGatesOpen && controller.AreColorGatesUnlocked && GatesAtTarget(true) && !controller.IsColorRoundActive && legacyEvents == 0 &&
                    game.medals == roundWallet && upperEvents == upperBaseline && Enumerable.Range(0, after.Length).All(i => Quaternion.Angle(roundAngles[i], after[i]) > 2) &&
                    controller.stations.All(s => !s.IsDrawing && s.IsRotating && Math.Abs(s.currentDividerSpeed) >= 17.999f && Math.Abs(s.currentDividerSpeed) <= 55.001f));
                TestBusySettings(); guardIndex = 0; BeginGuard(IntegratedStep.Visit); break;
            case IntegratedStep.Guard:
                TickGuard(); break;
            case IntegratedStep.Visit:
                TickVisit(); break;
            case IntegratedStep.PulseTo104:
                if (!TickPulses()) return;
                Check("resumedOriginalBallCanEarnAnotherTwoWithoutEarlyUpperPayment", upper.BumperWin == 104 && actualBumperContacts == 52 && SameUpperIdentity() &&
                    game.medals == roundWallet + earnedColorTotal && upperEvents == upperBaseline && upper.ColorGatesOpen);
                guardIndex = 1; BeginGuard(IntegratedStep.FinalOut); break;
            case IntegratedStep.FinalOut:
                if (!upperRouted) { RouteActualFrontOut(); upperRouted = true; return; }
                if (upperEvents <= upperBaseline) return;
                Check("actualFrontOutPaysAccumulated104OnceAndConsumesOriginalBall", upperEvents == upperBaseline + 1 && lastUpperWin == 104 && finalUpperWin == 104 &&
                    finalUpperHits == 52 && !upperTimedOut && upperExitedBowl && upperEndedWithOriginalToken && game.medals == roundWallet + earnedColorTotal + 104 &&
                    !controller.IsUpperDrawing && !controller.IsColorRoundActive && !controller.AreColorGatesUnlocked);
                int paidWallet = game.medals; controller.FinishLottery(upper, ticket, false, 104); controller.NotifyUpperColorRoute(upper, ticket, MedalJackpotKind.Ruby);
                Check("finalUpperDuplicateAndStaleRoutesCannotPayAgain", game.medals == paidWallet && upperEvents == upperBaseline + 1 && !controller.IsUpperDrawing && !controller.IsColorRoundActive);
                CompleteRound(); break;
            case IntegratedStep.Natural:
                SampleNaturalUpper();
                if (upperEvents <= upperBaseline) return;
                Check("naturalUpperEarnedWinPaidOnce_" + roundIndex, upperEvents == upperBaseline + 1 && lastUpperWin == finalUpperHits * 2 && finalUpperWin == lastUpperWin &&
                    actualBumperContacts >= finalUpperHits && upperEndedWithOriginalToken && game.medals == roundWallet + earnedColorTotal + lastUpperWin && upperExitedBowl && !upperTimedOut);
                naturalUpperCount++; naturalUpperPhysicalContactTotal += actualBumperContacts; CompleteRound(); break;
        }
    }
    private static void BeginGuard(IntegratedStep following)
    { afterGuard = following; guardStage = 0; integratedStep = IntegratedStep.Guard; }
    private static void IncomingGuard()
    {
        var guard = upper.outBlockBodies[guardIndex]; var collider = guard.GetComponent<Collider>();
        Vector3 normal = guard.transform.forward.normalized; float radius = upperToken.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = collider.ClosestPoint(collider.bounds.center + normal * (collider.bounds.extents.magnitude + 1));
        Place(upperToken.GetComponent<Rigidbody>(), face + normal * (radius + .025f)); upperToken.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
    }
    private static void TickGuard()
    {
        Require(SameUpperIdentity() && !upper.IsUpperSuspended, "Individual guard test lost the original live upper ball.");
        var block = upper.outBlocks[guardIndex];
        if (guardStage == 0)
        {
            Require(!upper.IsOutBlockUsed(guardIndex), "Guard has already been used before its forced real collision.");
            guardContactBaseline = physicalGuardContacts.TryGetValue(block, out int count) ? count : 0; IncomingGuard(); guardStage = 1; return;
        }
        if (guardStage == 1)
        {
            if (!upper.IsOutBlockUsed(guardIndex)) { Require(Time.fixedTime - placedFixedTime < .5f, "Incoming original ball missed white guard."); return; }
            guardUsedFixed = Time.fixedTime;
            Check("actualGuardFaceConsumesOnlyItself_" + guardIndex, Time.fixedTime > placedFixedTime && upper.UsedOutBlockCount == guardIndex + 1 &&
                GuardStates().SequenceEqual(Enumerable.Range(0, 4).Select(i => i <= guardIndex)) && SameUpperIdentity() && !upperToken.IsConsumed &&
                game.medals == roundWallet + earnedColorTotal && upperEvents == upperBaseline);
            ParkUpperBall(); guardStage = 2; return;
        }
        if (guardStage == 2)
        { if (Time.fixedTime <= placedFixedTime) return; IncomingGuard(); guardStage = 3; return; }
        if (guardStage == 3)
        {
            int contacts = physicalGuardContacts.TryGetValue(block, out int count) ? count : 0;
            if (contacts < guardContactBaseline + 2) { Require(Time.fixedTime - placedFixedTime < .12f, "Second real guard contact was not observed before retraction."); return; }
            Check("sameBallSecondGuardContactDoesNotRestoreOrSpendAnotherGuard_" + guardIndex, upper.IsOutBlockUsed(guardIndex) && upper.UsedOutBlockCount == guardIndex + 1 &&
                SameUpperIdentity() && game.medals == roundWallet + earnedColorTotal && upperEvents == upperBaseline);
            ParkUpperBall(); guardStage = 4; return;
        }
        if (guardStage == 4)
        {
            if (Time.fixedTime - guardUsedFixed < .85f) return;
            if (guardIndex == 3 && upper.regenerateOutBlocks)
            {
                if (upper.OutBlockRegenerationCount == 0 || !upper.OutBlockReady) return;
                Check("allFourGuardsRegenerateAfterLastGuard", GuardTargetsMatch() && upper.UsedOutBlockCount == 0 &&
                    !GuardStates().Any(used => used) && SameUpperIdentity());
            }
            else Check("onlyUsedGuardPlatesWithdrawIndependently_" + guardIndex, GuardTargetsMatch() && upper.UsedOutBlockCount == guardIndex + 1 && SameUpperIdentity());
            successfulGuardCount++; guardContacts.Add(new { index = guardIndex, tokenId = upperObjectId, ticket, used = GuardStates(),
                actualCollisionEnterCount = physicalGuardContacts[block] - guardContactBaseline, position = Pos(upper.outBlockBodies[guardIndex].position), win = upper.BumperWin });
            if (guardIndex == 0) { integratedStep = afterGuard; visitStage = 0; return; }
            guardIndex++;
            if (guardIndex < 4) { guardStage = 0; return; }
            upperRouted = false; integratedStep = afterGuard;
        }
    }
    private static void TickVisit()
    {
        Require(Now - visitStarted < 20 || visitStage == 0, "A color visit exceeded 20 seconds.");
        MedalJackpotKind kind = (MedalJackpotKind)(visitIndex / 2); bool jackpot = visitIndex % 2 == 1;
        if (visitStage == 0)
        {
            Require(SameUpperIdentity() && !upper.IsUpperSuspended && upper.ColorGatesOpen, "Color visit did not start from the original live upper ball.");
            RestorePocketStates(); visitingStation = controller.stations[(int)kind]; visitingToken = null; visitColorTicket = 0;
            visitUpperHits = upper.TotalBumperHits; visitUpperWin = upper.BumperWin; visitWallet = game.medals; visitGuardSnapshot = GuardStates();
            visitUpperEvents = upperEvents; visitColorEvents = colorEvents; visitStarted = Now;
            yellowBeforePort = controller.jackpotPools[2];
            var port = upper.colorRoutePockets.Single(p => p.kind == kind); var collider = port.GetComponent<Collider>();
            Vector3 normal = upper.transform.TransformPoint(upper.guideLocalCenter) - collider.bounds.center; normal.y = 0; normal.Normalize();
            float radius = upperToken.GetComponent<Collider>().bounds.extents.x;
            Vector3 face = collider.ClosestPoint(collider.bounds.center + normal * (collider.bounds.extents.magnitude + 1));
            Place(upperToken.GetComponent<Rigidbody>(), face + normal * (radius + .025f)); upperToken.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
            visitStage = 1; return;
        }
        if (visitStage == 1)
        {
            if (!controller.ActiveKind.HasValue || visitingStation.ActiveBall == null) return;
            visitingToken = visitingStation.ActiveBall; visitColorTicket = visitingStation.CurrentTicket;
            Check("yellowJPAddsTenPerActualUpperPocketEntry_visit" + visitIndex, controller.jackpotPools[2] == yellowBeforePort + 10);
            Check("actualIntegratedPortSuspendsSameUpperBall_visit" + visitIndex, Time.fixedTime > placedFixedTime && controller.ActiveKind == kind &&
                upper.LastColorPocket != null && upper.LastColorPocket.kind == kind && SameUpperIdentity() && upper.IsUpperSuspended &&
                upper.BumperWin == visitUpperWin && upper.TotalBumperHits == visitUpperHits && !upperToken.IsConsumed && game.medals == visitWallet &&
                upperEvents == visitUpperEvents && GuardStates().SequenceEqual(visitGuardSnapshot) && upper.ColorGatesOpen && controller.AreColorGatesUnlocked &&
                visitingToken.station == visitingStation && visitingToken.ticket == visitColorTicket && visitingToken != upperToken &&
                controller.stations.Count(s => s.IsDrawing) == 1 && controller.stations.All(s => s.IsRotating));
            int wallet = game.medals, starts = controller.TotalLotteries;
            controller.FinishLottery(controller.stations[((int)kind + 1) % 3], visitColorTicket, true, 9999);
            controller.FinishLottery(visitingStation, visitColorTicket + 777, true, 9999);
            controller.FinishLottery(visitingStation, visitColorTicket, true, 9999);
            controller.NotifyUpperColorRoute(upper, ticket, kind); controller.NotifyUpperColorRoute(upper, ticket + 888, kind);
            Check("activeLowerRejectsPrematureWrongAndDuplicateRoutes_visit" + visitIndex, game.medals == wallet && colorEvents == visitColorEvents &&
                controller.TotalLotteries == starts && visitingStation.IsDrawing && ReferenceEquals(visitingToken, visitingStation.ActiveBall) && upper.IsUpperSuspended);
            chosenColorPocket = visitingStation.GetComponentsInChildren<MedalLotteryPocket>().First(p => p.jackpot == jackpot);
            expectedPool = controller.jackpotPools[(int)kind]; RestrictPockets(visitingStation, chosenColorPocket);
            suspendedElapsedAt = upper.UpperElapsedSeconds; lowerPocketAt = Time.time + .35f;
            bool staleResume = !upper.ResumeUpperDraw(controller, ticket + 444);
            bool pausedContact = !upper.TryHitBumper(upper.bumpers[0], upperToken) && !upper.TryBlockOutflow(upper.outBlocks[1], upperToken) &&
                !upper.TryResolveColor(upper.colorRoutePockets[0], upperToken);
            Check("pausedBallRejectsStaleResumeAndContacts_visit" + visitIndex, staleResume && pausedContact && SameUpperIdentity() && upper.IsUpperSuspended &&
                upper.BumperWin == visitUpperWin && GuardStates().SequenceEqual(visitGuardSnapshot));
            visitStage = 3; return;
        }
        if (visitStage == 3)
        {
            if (Time.time < lowerPocketAt) return;
            Check("lowerVisitPausesOnlyUpperClock_visit" + visitIndex, upper.IsUpperSuspended && SameUpperIdentity() &&
                Math.Abs(upper.UpperElapsedSeconds - suspendedElapsedAt) < .005f && game.medals == visitWallet &&
                upperToken.GetComponent<Rigidbody>().isKinematic && !upperToken.GetComponent<Rigidbody>().detectCollisions &&
                upperToken.GetComponentsInChildren<Collider>().All(c => !c.enabled) && upperToken.GetComponentsInChildren<Renderer>().All(r => !r.enabled));
            Place(visitingToken.GetComponent<Rigidbody>(), chosenColorPocket.GetComponent<Collider>().bounds.center); visitStage = 2; return;
        }
        if (visitStage == 2)
        {
            if (colorEvents <= visitColorEvents || Now - visitFinishedAt < .15 || upper.IsUpperSuspended) return;
            int expected = jackpot ? expectedPool : chosenColorPocket.smallReward;
            Check("lowerResultCreditsOnlyItsPrizeAndReturnsOriginalUpperBall_visit" + visitIndex, colorEvents == visitColorEvents + 1 && finishedKind == kind &&
                finishedJackpot == jackpot && lastColorPayout == expected && game.medals == visitWallet + expected && upperEvents == visitUpperEvents &&
                visitingStation.LastPocket == chosenColorPocket && !visitingStation.LastTimedOut && SameUpperIdentity() && upper.IsDrawing && !upper.IsUpperSuspended &&
                upperToken.GetComponent<Rigidbody>().useGravity && !upperToken.GetComponent<Rigidbody>().isKinematic &&
                upper.BumperWin == visitUpperWin && upper.TotalBumperHits == visitUpperHits && controller.LiveUpperWin == visitUpperWin &&
                GuardStates().SequenceEqual(visitGuardSnapshot) && upper.ColorGatesOpen && controller.AreColorGatesUnlocked && !controller.IsColorRoundActive &&
                !controller.ActiveKind.HasValue && controller.stations.All(s => s.IsRotating && !s.IsDrawing));
            earnedColorTotal += expected; ParkUpperBall();
            int wallet = game.medals, events = colorEvents; controller.FinishLottery(visitingStation, visitColorTicket, jackpot, expected);
            controller.NotifyUpperColorRoute(upper, ticket + 555, kind); controller.FinishColorSelection(upper, ticket, kind, false);
            Check("staleLowerResultCannotPayOrReplaceReturnedBall_visit" + visitIndex, game.medals == wallet && colorEvents == events && SameUpperIdentity() && !controller.IsColorRoundActive);
            if (jackpot) Check("actualJackpotResetsPool_" + kind, controller.jackpotPools[(int)kind] == settings.jackpotResetValues[(int)kind]);
            CheckPoolLabels("afterVisit" + visitIndex);
            visits.Add(new { index = visitIndex, kind = kind.ToString(), jackpot, payout = expected, lowerTicket = visitColorTicket, upperTicket = ticket,
                upperTokenId = upperObjectId, upperWin = upper.BumperWin, upperHits = upper.TotalBumperHits, guardsUsed = GuardStates(),
                upperElapsedSeconds = upper.UpperElapsedSeconds, upperPaidEvents = upperEvents - upperBaseline, elapsedWallSeconds = Now - visitStarted });
            RestorePocketStates(); visitIndex++; visitStage = 0;
            if (visitIndex == 6) BeginPulses(52, IntegratedStep.PulseTo104);
        }
    }
    private static void RouteActualFrontOut()
    {
        var collider = upper.upperOutflow.GetComponent<Collider>();
        Vector3 normal = upper.transform.TransformPoint(upper.guideLocalCenter) - collider.bounds.center; normal.y = 0; normal.Normalize();
        float radius = upperToken.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = collider.ClosestPoint(collider.bounds.center + normal * (collider.bounds.extents.magnitude + 1));
        // This fixture starts beyond the restored plates, at the real OUT entrance.
        Place(upperToken.GetComponent<Rigidbody>(), face + normal * .005f); upperToken.GetComponent<Rigidbody>().linearVelocity = -normal * 4f;
    }
    private static void SampleNaturalUpper()
    {
        if (Time.time < nextNaturalSample || upperToken == null || upperToken.IsConsumed) return;
        nextNaturalSample = Time.time + 1f; var body = upperToken.GetComponent<Rigidbody>();
        naturalUpperSamples.Add(new { round = roundIndex, seed = naturalSeeds[roundIndex - 1], ticket, tokenId = upperObjectId,
            localPosition = Pos(upper.transform.InverseTransformPoint(body.position)), velocity = Pos(body.linearVelocity), hits = upper.TotalBumperHits,
            win = upper.BumperWin, suspended = upper.IsUpperSuspended, upperElapsed = upper.UpperElapsedSeconds, activeColor = controller.ActiveKind?.ToString() });
    }
    internal static void ObserveUpperBumperContact(int round, LotteryBallToken token, Collision collision)
    {
        if (phase != Phase.Rounds || round != roundIndex || token == null || !ReferenceEquals(token, upperToken)) return;
        var bumper = collision.collider.GetComponent<MedalLotteryBumper>();
        if (bumper != null && bumper.station == upper && upper.bumpers.Contains(bumper))
        {
            actualBumperContacts++; bumperContactSamples.Add(new { round, ticket, bumper = bumper.name, contact = actualBumperContacts,
                fixedTime = Time.fixedTime, localBallPosition = Pos(upper.transform.InverseTransformPoint(token.GetComponent<Rigidbody>().position)) });
        }
        var block = collision.collider.GetComponent<MedalOutBlock>();
        if (block != null && block.station == upper && upper.outBlocks.Contains(block))
            physicalGuardContacts[block] = physicalGuardContacts.TryGetValue(block, out int count) ? count + 1 : 1;
    }
    private static void TestBusySettings()
    {
        string before = JsonUtility.ToJson(settings.CaptureValues()); int[] pools = (int[])controller.jackpotPools.Clone();
        settingsUI.Open(); settingsUI.targetPayoutInput.text = "80"; settingsUI.ApplyFromInputs();
        Check("settingsCannotMutateActiveIntegratedDraw", JsonUtility.ToJson(settings.CaptureValues()) == before && pools.SequenceEqual(controller.jackpotPools) && settingsUI.IsOpen);
        settingsUI.Close();
    }
    private static void CompleteRound()
    {
        RestorePocketStates();
        Check("integratedFlowNeverCreatesLegacySelector_" + roundIndex, legacyEvents == 0 && !upper.IsSelectingColor && !controller.IsSelectingColor);
        rounds.Add(new { index = roundIndex, ticket, upperTokenId = upperObjectId, natural = roundIndex > 0,
            seed = roundIndex > 0 ? (int?)naturalSeeds[roundIndex - 1] : null, finalWin = finalUpperWin, hits = finalUpperHits,
            physicalBumperContactEvents = actualBumperContacts, upperExitedBowl, upperTimedOut, upperEndedWithOriginalToken,
            finalPosition = finalUpperBallLocalPosition.HasValue ? Pos(finalUpperBallLocalPosition.Value) : null,
            upperPayout = lastUpperWin, colorPayout = earnedColorTotal, upperFinishEvents = upperEvents - upperBaseline,
            usedGuards = GuardStates(), elapsedWallSeconds = Now - roundStarted });
        stopAngles = Angles(); nextAt = Time.time + .6f; Enter(Phase.Stop);
    }

    private static void RestrictPockets(MedalBallLotteryStation station, MedalLotteryPocket chosen)
    {
        foreach (var pocket in station.GetComponentsInChildren<MedalLotteryPocket>())
        {
            var collider = pocket.GetComponent<Collider>(); if (!routedPocketStates.ContainsKey(collider)) routedPocketStates[collider] = collider.enabled;
            collider.enabled = pocket == chosen;
        }
    }
    private static void RestorePocketStates()
    { foreach (var entry in routedPocketStates) if (entry.Key != null) entry.Key.enabled = entry.Value; routedPocketStates.Clear(); }
    private static string FileHash(string path)
    {
        if (!File.Exists(path)) return null;
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
    private static void Capture(string name, string selection)
    {
        if (!Application.isBatchMode) return;
        capturing = true;
        try
        {
            var records = new List<object>(); var missing = new List<string>();
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)) continue;
                foreach (char character in text.text.Where(c => (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff')).Distinct())
                    if (text.font == null || !text.font.HasCharacter(character, true, true)) missing.Add(text.name + ": U+" + ((int)character).ToString("X4"));
                text.ForceMeshUpdate(true, true);
                records.Add(new { name = text.name, text = text.text, font = text.font == null ? null : text.font.name,
                    characters = text.textInfo.characterCount, visibleCharacters = Enumerable.Range(0, text.textInfo.characterCount).Count(i => text.textInfo.characterInfo[i].isVisible),
                    meshVertices = text.mesh == null ? 0 : text.mesh.vertexCount });
            }
            Canvas.ForceUpdateCanvases();
            results["textMeshes_" + name] = new { missingGlyphs = missing.ToArray(), texts = records.ToArray() };
            Check("captureFontCoverage_" + name, missing.Count == 0);
            MedalArcadeBatchCapture.Capture(name, selection);
        }
        catch (Exception exception) { captureIssues.Add(name + ": " + exception.Message); }
        finally { capturing = false; }
    }

    private static void Finish(bool stopPlay)
    {
        SessionState.SetBool(Key, false); RestorePocketStates();
        results["upperFixtureSlotIsolation"] = upperFixtureSlotStates.Select(pair => new { name = pair.Key == null ? null : pair.Key.name,
            originalEnabled = pair.Value, isolatedDuringFixtures = pair.Key != null && !pair.Key.enabled }).ToArray();
        foreach (var pair in upperFixtureSlotStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        upperFixtureSlotStates.Clear();
        if (initialized)
        {
            CleanupNormalBurst();
            if (upper != null) upper.drawTimeout = originalUpperTimeout;
            if (game != null) { game.OnPayoutMedalSpawned -= OnPayout; if (realCap > 0) game.maxMedalsOnBoard = realCap; game.SetSettingsOpen(false); }
            if (controller != null)
            {
                controller.OnUpperLotteryStarted -= OnUpperStarted; controller.OnUpperLotteryFinished -= OnUpperFinish; controller.OnColorSelectionStarted -= OnLegacySelectorStarted; controller.OnColorSelectionFinished -= OnLegacySelectorFinished; controller.OnLotteryFinished -= OnColorFinish;
            }
            if (settings != null) { settings.SettingsFileOverride = originalOverride; settings.persistSettings = savedPersistence; }
            Time.timeScale = originalTimeScale; UnityEngine.Random.state = originalRandomState;
            try
            {
                string finalHash = FileHash(realSettingsPath); Check("userSettingsFileRemainedUntouched", finalHash == realSettingsHash);
                results["userSettingsHashes"] = new { before = realSettingsHash, after = finalHash };
                string backupRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-expansion-20261004")) + Path.DirectorySeparatorChar;
                if (temporarySettingsPath != null && Path.GetFullPath(temporarySettingsPath).StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(temporarySettingsPath)) File.Delete(temporarySettingsPath);
                    if (File.Exists(temporarySettingsPath + ".tmp")) File.Delete(temporarySettingsPath + ".tmp");
                }
            }
            catch (Exception exception) { errors.Add("Isolated settings cleanup failed: " + exception.Message); }
        }
        Application.runInBackground = SessionState.GetBool(BackgroundKey, false);
        results["timestampUtc"] = DateTime.UtcNow.ToString("O"); results["startedUtc"] = SessionState.GetString(UtcKey, "");
        results["elapsedWallSeconds"] = Elapsed; results["playSeconds"] = Time.time; results["phase"] = phase.ToString();
        results["integratedUpperMode"] = true; results["targetRounds"] = TargetRounds;
        results["visits"] = visits.ToArray(); results["guardContacts"] = guardContacts.ToArray(); results["naturalUpperSamples"] = naturalUpperSamples.ToArray();
        results["slotPocketContacts"] = slotPocketContacts.ToArray();
        results["fixtureBoundaries"] = fixtureBoundaries.ToArray();
        results["maximumWallSeconds"] = MaximumSeconds; results["expectedNaturalUpperCount"] = 3; results["expectedMainColorVisits"] = 6; results["legacySelectorEvents"] = legacyEvents;
        results["naturalUpperCount"] = naturalUpperCount; results["rounds"] = rounds.ToArray();
        results["naturalUpperPhysicalContactTotal"] = naturalUpperPhysicalContactTotal;
        results["successfulGuardFaceCount"] = successfulGuardCount;
        results["bumperContactSamples"] = bumperContactSamples.ToArray();
        results["originalUpperDrawTimeout"] = originalUpperTimeout; results["naturalSeeds"] = naturalSeeds;
        results["normalPayoutContactSamples"] = burstCoins.Select((c, i) => new {
            index = i, spawnPosition = Pos(c.spawn), c.dynamic, c.credited,
            firstSurface = c.firstSurface, firstSurfacePosition = c.firstSurfacePosition.HasValue ? Pos(c.firstSurfacePosition.Value) : null,
            firstSurfaceDelaySeconds = c.firstSurfaceDelay,
            firstContactObject = c.firstContactObject, firstContactDelaySeconds = c.firstContactDelay,
            firstContactPosition = c.firstContactPosition.HasValue ? Pos(c.firstContactPosition.Value) : null,
            firstContactNormal = c.firstContactNormal.HasValue ? Pos(c.firstContactNormal.Value) : null, c.firstContactIsUpper,
            firstPlatePosition = c.firstPlatePosition.HasValue ? Pos(c.firstPlatePosition.Value) : null,
            c.hitPlateTop, c.firstSurfaceIsPlate, c.coinOverlaps, c.geometryOverlaps, spawnOverlaps = c.spawnOverlaps.ToArray()
        }).ToArray();
        results["payouts"] = payouts.ToArray(); results["pendingPayoutMedals"] = game == null ? 0 : game.PendingPayoutMedals;
        results["finalBootReadiness"] = Diagnostics(); results["errors"] = errors.Distinct().ToArray();
        results["editorServiceIssues"] = editorIssues.Distinct().ToArray(); results["captureIssues"] = captureIssues.Distinct().ToArray();
        results["success"] = errors.Count == 0 && phase == Phase.Complete;
        MedalPusherExpansionBuilder.WriteReport("integrated-upper-play-validation.json", results);
        Debug.Log("[MedalPusher] Integrated upper flow Play check complete: " + ((bool)results["success"] ? "PASS" : "FAIL"));
        if (stopPlay)
        {
            EditorApplication.isPaused = false;
            if (Application.isBatchMode) EditorApplication.Exit((bool)results["success"] ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}

/// <summary>Transient collision observer; never saved to a scene or attached to runtime prefabs.</summary>
public sealed class MedalIntegratedUpperFlowPayoutProbe : MonoBehaviour
{
    [NonSerialized] public int RecordIndex;
    private void OnCollisionEnter(Collision collision) => MedalIntegratedUpperFlowPlayCheck.ObserveBurstCollision(RecordIndex, collision);
    private void OnCollisionStay(Collision collision) => MedalIntegratedUpperFlowPlayCheck.ObserveBurstCollision(RecordIndex, collision);
}

/// <summary>Records actual OnCollisionEnter evidence while the harness moves the upper ball between bumpers.</summary>
public sealed class MedalIntegratedUpperFlowBumperProbe : MonoBehaviour
{
    [NonSerialized] public int Round;
    [NonSerialized] public LotteryBallToken Token;
    private void OnCollisionEnter(Collision collision) => MedalIntegratedUpperFlowPlayCheck.ObserveUpperBumperContact(Round, Token, collision);
}

/// <summary>Records actual trigger entry of the paid coin into its physical slot pocket.</summary>
public sealed class MedalIntegratedSlotPocketProbe : MonoBehaviour
{
    [NonSerialized] public MedalSlotPocket ExpectedPocket;
    private bool observed;
    private void OnTriggerEnter(Collider other)
    {
        if (observed || other.GetComponent<MedalSlotPocket>() != ExpectedPocket) return;
        observed = true; MedalIntegratedUpperFlowPlayCheck.ObserveSlotPocketContact(ExpectedPocket, other);
    }
}
