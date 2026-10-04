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

/// <summary>Bounded Play-only check of settings, actual payout coins, side holes, and the two-stage lottery.</summary>
[InitializeOnLoad]
public static class MedalUpperFlowPlayCheck
{
    private const string Key = "MedalPusher.UpperFlowCheck";
    private const string StartedKey = Key + ".Started";
    private const string BackgroundKey = Key + ".Background";
    private const string UtcKey = Key + ".Utc";
    private const string StabilityKey = Key + ".Stability";
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
    private static readonly List<object> naturalSelectorSamples = new List<object>();
    private static readonly List<object> dividerSpeedSamples = new List<object>();
    private static readonly HashSet<float>[] observedDividerSpeeds = { new HashSet<float>(), new HashSet<float>(), new HashSet<float>() };
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
    private static int realCap, walletBefore, upperEvents, selectionEvents, colorEvents;
    private static long returnedBefore, paidBefore, queuedBefore;
    private static int roundIndex, ticket, upperBaseline, selectionBaseline, colorBaseline, lotteriesBefore, colorRoundsBefore;
    private static int lastUpperWin, lastColorPayout, roundWallet, selectorWallet, colorWallet, expectedPool, jackpotsBefore;
    private static int naturalUpperCount, naturalSelectorCount, spinsBefore;
    private static int successfulGuardCount;
    private static MedalJackpotKind? selectedKind, finishedKind;
    private static bool selectionTimedOut, finishedJackpot, upperChecked, upperRouted, selectorChecked, selectorRouted;
    private static bool lowerChecked, lowerRouted, rotationObserved, gatesLiftObserved, captureDone;
    private static double roundStarted, colorFinishedAt;
    private static bool upperTimedOut;
    private static float selectorStartedFixed;
    private static float originalSelectorTimeout;
    private static LotteryBallToken guardedToken;
    private static int guardStep;
    private static bool guardCollisionObserved, selectionGuardUsed;
    private static bool selectionExitedBowl;
    private static Vector3? unguardedExitPosition;
    private static float unguardedBallRadius;
    private static float guardConsumedFixed;
    private static float nextSelectorSampleAt;

    static MedalUpperFlowPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Upper Flow Play Check")]
    public static void Run() => Start(false);

    [MenuItem("Tools/Medal Pusher/Run Selector Stability Play Check")]
    public static void RunSelectorStability() => Start(true);

    private static void Start(bool selectorStability)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play mode before running the upper-flow check.");
        MedalPusherExpansionBuilder.Validate(); Reset();
        SessionState.SetBool(StabilityKey, selectorStability);
        foreach (string other in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck" }) SessionState.SetBool(other, false);
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
            MedalPusherExpansionBuilder.WriteReport("upper-play-validation.json", new { timestampUtc = DateTime.UtcNow.ToString("O"), success = false, errors = new[] { exception.ToString() } });
            Debug.LogException(exception); if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Medal Pusher/Inspect Upper Flow Play Check")]
    public static void Inspect()
    {
        var current = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        MedalPusherExpansionBuilder.WriteReport("upper-play-state.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false),
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused, elapsedWallSeconds = Elapsed,
            phase = phase.ToString(), roundIndex, upperEvents, selectionEvents, colorEvents,
            selectorStability = StabilityMode, targetRounds = TargetRounds, maximumWallSeconds = MaximumSeconds,
            bootReadiness = Diagnostics(), controller = current == null ? null : new {
                current.IsUpperDrawing, current.IsSelectingColor, current.IsColorRoundActive, activeKind = current.ActiveKind?.ToString(),
                current.PendingBallDraws, current.LastUpperWin,
                upperBall = current.upperStation == null || current.upperStation.ActiveBall == null ? null : Pos(current.upperStation.ActiveBall.transform.position)
            }
        });
    }

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartedKey, (float)Now);
    private static bool StabilityMode => SessionState.GetBool(StabilityKey, false);
    private static int TargetRounds => StabilityMode ? 52 : 14;
    private static int ExpectedNaturalSelectors => StabilityMode ? 33 : 3;
    private static int MaximumSeconds => StabilityMode ? 500 : 160;
    private static object Pos(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, bool condition) { results[name] = condition; if (!condition) errors.Add("Failed: " + name); }
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
        initialized = capturing = false; upperEvents = selectionEvents = colorEvents = roundIndex = naturalUpperCount = naturalSelectorCount = 0;
        successfulGuardCount = 0; dividerSpeedSamples.Clear(); foreach (var speeds in observedDividerSpeeds) speeds.Clear();
        temporarySettingsPath = realSettingsPath = realSettingsHash = null;
        results.Clear(); errors.Clear(); editorIssues.Clear(); captureIssues.Clear(); payouts.Clear(); sidePositions.Clear(); topPositions.Clear();
        payoutItems.Clear(); rounds.Clear(); naturalSelectorSamples.Clear(); holeItems.Clear(); routedPocketStates.Clear();
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
            if (phase == Phase.NormalPayoutBurst) Require(Now - phaseStarted < 55, "Normal 500-medal payout exceeded 55 wall-clock seconds.");
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
                    for (int i = 0; i < 6; i++) PaidThrow();
                    controller.enabled = true; Enter(Phase.Slots); break;
                case Phase.Slots:
                    if (controller.TotalSpins < spinsBefore + 3 || controller.IsSlotSpinning) return;
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
                        Check("threeNaturalUpperDrawsFinishWithoutTimeout", naturalUpperCount == 3);
                        Check(StabilityMode ? "thirtyThreeNaturalSelectorsResolveWithoutTimeout" : "threeNaturalSelectorsReachRealColorPocket", naturalSelectorCount == ExpectedNaturalSelectors);
                        Check("allGuardFacesResolveSameBallWithoutCompensation", successfulGuardCount == (StabilityMode ? 8 : 1));
                        Check("partitionSpeedsVaryAcrossDraws", observedDividerSpeeds.All(s => s.Count >= 2));
                        phase = Phase.Complete; Finish(true);
                    }
                    break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }

    private static void Initialize()
    {
        initialized = true; originalTimeScale = Time.timeScale; Time.timeScale = 1;
        results["bootReadiness"] = Diagnostics();
        upper = controller.upperStation;
        originalSelectorTimeout = upper == null ? 10f : upper.colorSelectionTimeout;
        Require(upper != null && upper.isUpperStation && controller.stations != null && controller.stations.Length == 3, "Upper/colored stations missing.");
        Require(!controller.IsUpperDrawing && !controller.IsColorRoundActive && !controller.IsSlotSpinning, "An incidental draw is already active.");
        game.StopThrowing(); controller.enabled = false; ClearCredits(); ClearDrawQueue();
        foreach (var input in UnityEngine.Object.FindObjectsByType<MedalInputHandler>(FindObjectsSortMode.None)) input.enabled = false;
        var manager = game.GetComponent<PrizeDropManager>(); Require(manager != null, "Prize manager missing."); manager.enabled = false; manager.bonusMedalChance = 0;
        FreezeBoard();
        controller.OnUpperLotteryFinished += OnUpperFinish; controller.OnColorSelectionFinished += OnSelectionFinish; controller.OnLotteryFinished += OnColorFinish;
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
    private static void PaidThrow()
    {
        Set(game, "throwTimer", 0f);
        var before = new HashSet<MedalItem>(game.itemsRoot.GetComponentsInChildren<MedalItem>());
        game.ThrowSingleMedal();
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>()) if (!before.Contains(item)) Freeze(item.GetComponent<Rigidbody>());
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
        var probe = coin.AddComponent<MedalUpperFlowPayoutProbe>(); probe.RecordIndex = index;
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
        var pockets = upper.GetComponentsInChildren<MedalLotteryPocket>();
        Check("upperHasEightNormalWinPockets", pockets.Length == 8 && pockets.All(p => !p.jackpot && p.station == upper) &&
            pockets.Select(p => p.smallReward).OrderBy(v => v).SequenceEqual(new[] { 20, 40, 60, 80, 100, 120, 150, 200 }));
        Check("threeSolidSealedColorGatesAtIdle", upper.colorRoutePockets != null && upper.colorRoutePockets.Length == 3 &&
            upper.colorRoutePockets.Select(p => p.kind).Distinct().Count() == 3 && upper.colorGateBodies != null && upper.colorGateBodies.Length == 3 &&
            upper.colorGateBodies.All(b => b != null && b.isKinematic && b.GetComponent<Collider>() != null && b.GetComponent<Collider>().enabled && !b.GetComponent<Collider>().isTrigger) && !upper.ColorGatesOpen && GatesAtTarget(false));
        Check("coloredSelectorPocketLettersRemoved", !upper.GetComponentsInChildren<Transform>(true).Any(t => t.name == "GateColorMark" || t.name == "ColorRouteLabel"));
        bool eightGuards = upper.outBlocks != null && upper.outBlockBodies != null && upper.outBlockClosedPositions != null && upper.outflows != null &&
            upper.outBlocks.Length == 8 && upper.outBlockBodies.Length == 8 && upper.outBlockClosedPositions.Length == 8 && upper.outflows.Length == 8 &&
            Enumerable.Range(0, 8).All(i => upper.outBlocks[i] != null && upper.outBlockBodies[i] != null && upper.outBlockBodies[i].isKinematic &&
                upper.outBlockBodies[i].GetComponent<Collider>() != null && !upper.outBlockBodies[i].GetComponent<Collider>().isTrigger &&
                upper.outflows[i] != null && upper.outflows[i].GetComponent<Collider>() != null && upper.outflows[i].GetComponent<Collider>().isTrigger);
        Check("eightSolidOutGuardsAndEightExteriorTriggers", eightGuards && upper.outBlockBody == upper.outBlockBodies[0] && upper.outflow == upper.outflows[0]);
        Vector3 red = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Ruby).transform.position);
        Vector3 blue = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Sapphire).transform.position);
        Vector3 yellow = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Amber).transform.position);
        Check("colorRoutesUseRedLeftBlueRightYellowRear", red.x < 0 && blue.x > 0 && yellow.z > red.z + .2f && yellow.z > blue.z + .2f);
        results["selectorOrientation"] = new { red = Pos(red), blue = Pos(blue), yellow = Pos(yellow) };
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
        Check("allDrawAndBoardBallsUseGreenMaterial", Green(upper.lotteryBallPrefab) && controller.stations.All(s => Green(s.lotteryBallPrefab)) &&
            controller.ballPrefabs != null && controller.ballPrefabs.Length == 3 && controller.ballPrefabs.All(Green));
        Check("coloredPhysicalJackpotLabelsUseJackpot", controller.stations.All(s => s.jackpotPocketText != null && s.jackpotPocketText.text.Contains("JACKPOT") && !s.jackpotPocketText.text.Contains("大当たり")));
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

    // Stability mode adds natural selectors at 14..43, seven guard faces at 44..50, and an unguarded airborne exit at 51.
    private static int? ForcedUpperWin => roundIndex == 0 ? 100 : roundIndex == 1 || roundIndex == 2 ? 60 : roundIndex >= 6 ? 120 : (int?)null;
    private static bool NaturalSelectorRound => (roundIndex >= 6 && roundIndex <= 8) || (StabilityMode && roundIndex >= 14 && roundIndex <= 43);
    private static bool ForceColor => roundIndex >= 9 && roundIndex <= 11;
    private static bool GuardRound => roundIndex == 12 || (StabilityMode && roundIndex >= 44 && roundIndex <= 50);
    private static int GuardFaceIndex => roundIndex >= 44 ? roundIndex - 43 : 0;
    private static Rigidbody GuardBody => upper.outBlockBodies[GuardFaceIndex];
    private static MedalLotteryOutflow GuardOutflow => upper.outflows[GuardFaceIndex];
    private static string GuardCheckName(string name) => GuardFaceIndex == 0 ? name : name + "_face" + GuardFaceIndex;
    private static bool TimeoutRound => roundIndex == 13;
    private static bool UnguardedOutsideRound => StabilityMode && roundIndex == 51;
    private static void StartRound()
    {
        Require(!controller.IsUpperDrawing && !controller.IsSelectingColor && !controller.IsColorRoundActive && !controller.ActiveKind.HasValue, "Previous lottery did not become idle.");
        RestorePocketStates(); ClearDrawQueue(); ClearCredits(); upper.colorSelectionTimeout = originalSelectorTimeout;
        upperBaseline = upperEvents; selectionBaseline = selectionEvents; colorBaseline = colorEvents;
        lotteriesBefore = controller.TotalLotteries; colorRoundsBefore = controller.TotalColorRounds;
        upperChecked = upperRouted = selectorChecked = selectorRouted = lowerChecked = lowerRouted = rotationObserved = gatesLiftObserved = captureDone = false;
        selectedKind = finishedKind = null; ticket = 0; roundWallet = game.medals; roundStarted = Now;
        guardStep = 0; guardedToken = null; guardCollisionObserved = selectionGuardUsed = false;
        selectionExitedBowl = false; unguardedExitPosition = null;
        unguardedBallRadius = .26f;
        nextSelectorSampleAt = 0;
        roundAngles = Angles(); selectorStartedFixed = -1;
        controller.QueueBallDraw(MedalJackpotKind.Ruby); Set(controller, "nextLotteryAt", 0f); controller.enabled = true; Enter(Phase.Rounds);
    }
    private static void OnUpperFinish(int payout) { upperEvents++; lastUpperWin = payout; upperTimedOut = upper.LastTimedOut; }
    private static void OnSelectionFinish(MedalJackpotKind? kind, bool timedOut)
    {
        selectionEvents++; selectedKind = kind; selectionTimedOut = timedOut; selectionGuardUsed = upper.OutBlockUsed;
        selectionExitedBowl = upper.LastExitedBowl;
        if (UnguardedOutsideRound && guardedToken != null)
            unguardedExitPosition = upper.transform.InverseTransformPoint(guardedToken.GetComponent<Rigidbody>().position);
    }
    private static void OnColorFinish(MedalJackpotKind kind, int payout, bool jackpot)
    { colorEvents++; finishedKind = kind; lastColorPayout = payout; finishedJackpot = jackpot; colorFinishedAt = Now; }

    private static void TickRound()
    {
        Require(Now - roundStarted < 38, "Lottery round exceeded 38 wall-clock seconds: " + roundIndex);
        SampleNaturalSelector();
        if (controller.IsUpperDrawing && upper.ActiveBall != null)
        {
            if (!upperChecked)
            {
                upperChecked = true; ticket = upper.CurrentTicket;
                Check("sealedRoutesCannotConsumeNormalUpperBall_" + roundIndex,
                    !upper.ColorGatesOpen && !upper.TryResolveColor(upper.colorRoutePockets[0], upper.ActiveBall) && upper.ActiveBall != null && !upper.ActiveBall.IsConsumed);
                int wallet = game.medals; controller.FinishLottery(controller.stations[0], ticket, false, 200); controller.FinishLottery(upper, ticket + 1, false, 200);
                upper.BeginColorSelection(controller, ticket);
                Check("wrongUpperCallbackAndPrematureSelectionRejected_" + roundIndex,
                    game.medals == wallet && controller.IsUpperDrawing && !upper.IsSelectingColor && !upper.ColorGatesOpen);
            }
            if (ForcedUpperWin.HasValue && !upperRouted)
            {
                var pocket = upper.GetComponentsInChildren<MedalLotteryPocket>().Single(p => p.smallReward == ForcedUpperWin.Value);
                RestrictPockets(upper, pocket); Place(upper.ActiveBall.GetComponent<Rigidbody>(), pocket.GetComponent<Collider>().bounds.center); upperRouted = true;
            }
            return;
        }
        if (upperEvents <= upperBaseline) return;
        if (upperChecked && !results.ContainsKey("upperRewardOnce_" + roundIndex))
        {
            RestorePocketStates();
            Check("upperRewardOnce_" + roundIndex, upperEvents == upperBaseline + 1 && !upper.LastTimedOut && upper.LastPocket != null &&
                !upper.LastPocket.jackpot && controller.LastUpperWin == lastUpperWin && game.medals == roundWallet + lastUpperWin &&
                (!ForcedUpperWin.HasValue || (lastUpperWin == ForcedUpperWin.Value && upperRouted && Time.fixedTime > placedFixedTime)));
            int wallet = game.medals; controller.FinishLottery(upper, ticket, false, lastUpperWin);
            Check("duplicateUpperRewardIgnored_" + roundIndex, game.medals == wallet && upperEvents == upperBaseline + 1);
            if (roundIndex >= 3 && roundIndex <= 5 && !upperTimedOut && upper.LastPocket != null) naturalUpperCount++;
            selectorWallet = game.medals;
        }
        if (lastUpperWin <= 100)
        {
            Check("singleWinAtOrBelow100NeverUnlocksColors_" + roundIndex, !controller.IsColorRoundActive && !controller.IsSelectingColor && !upper.ColorGatesOpen && controller.TotalColorRounds == colorRoundsBefore && controller.TotalLotteries == lotteriesBefore);
            Check("singleWinAtOrBelow100KeepsPartitionsStopped_" + roundIndex, AnglesUnchanged(roundAngles) && controller.stations.All(s => Mathf.Abs(s.currentDividerSpeed) < .001f && !s.IsRotating));
            if (roundIndex == 2) Check("twoSixtyWinsAreNotAccumulatedIntoUnlock", lastUpperWin == 60 && rounds.Count >= 2 && !controller.IsColorRoundActive);
            CompleteRound(); return;
        }
        CheckOnce("singleWinAbove100StartsColorRound_" + roundIndex, controller.TotalColorRounds == colorRoundsBefore + 1);
        if (controller.IsSelectingColor && upper.ActiveBall != null)
        {
            if (!selectorChecked)
            {
                selectorChecked = true; selectorStartedFixed = Time.fixedTime;
                Check("selectorUsesSameTicketAndDisablesNormalWins_" + roundIndex, upper.CurrentTicket == ticket && upper.IsSelectingColor && upper.ColorGatesOpen &&
                    upper.normalWinPocketTriggers.All(c => !c.enabled) && controller.stations.All(s => s.IsRotating && !s.IsDrawing));
                var token = upper.ActiveBall; var pocket = upper.colorRoutePockets[0]; int originalTicket = token.ticket;
                token.ticket++; bool wrongTicket = !upper.TryResolveColor(pocket, token); token.ticket = originalTicket;
                var originalStation = token.station; token.station = controller.stations[0]; bool wrongStation = !upper.TryResolveColor(pocket, token); token.station = originalStation;
                int wallet = game.medals;
                controller.FinishLottery(upper, ticket, false, 999); controller.FinishColorSelection(controller.stations[0], ticket, MedalJackpotKind.Ruby, false);
                controller.FinishColorSelection(upper, ticket + 1, MedalJackpotKind.Ruby, false);
                Check("selectorWrongTokenAndCallbacksRejected_" + roundIndex, wrongTicket && wrongStation && !token.IsConsumed && upper.IsSelectingColor && controller.IsSelectingColor && game.medals == wallet);
                Check("freshSelectorStartsWithOneOutGuard_" + roundIndex, upper.OutBlockReady && !upper.OutBlockUsed && !upper.TryResolveOutflow(upper.outflow, token) && !token.IsConsumed);
                if (TimeoutRound)
                {
                    Check("nextSelectorRestoresConsumedOutGuard", upper.OutBlockReady && !upper.OutBlockUsed);
                    upper.colorSelectionTimeout = .2f;
                    Place(token.GetComponent<Rigidbody>(), upper.colorLaunchPoint.position + Vector3.up * 4f);
                }
                TestBusySettings();
            }
            if (Time.fixedTime - selectorStartedFixed >= .35f)
            {
                var angles = Angles();
                rotationObserved |= Enumerable.Range(0, roundAngles.Length).All(i => Quaternion.Angle(roundAngles[i], angles[i]) > 2) &&
                    controller.stations.All(s => Mathf.Abs(s.currentDividerSpeed) >= 17.999f && Mathf.Abs(s.currentDividerSpeed) <= 55.001f);
                ObserveDividerSpeeds();
                gatesLiftObserved |= GatesAtTarget(true);
                if (!captureDone) { Capture("upper-gates-open-" + roundIndex + ".png", null); captureDone = true; }
                if (GuardRound) TickOutGuard();
                else if (UnguardedOutsideRound && !selectorRouted)
                {
                    guardedToken = upper.ActiveBall;
                    unguardedBallRadius = guardedToken.GetComponent<Collider>().bounds.extents.x / Mathf.Max(.001f, Mathf.Abs(upper.transform.lossyScale.x));
                    Place(guardedToken.GetComponent<Rigidbody>(), upper.transform.TransformPoint(new Vector3(1.2f, 2.85f, -5.3f)));
                    guardedToken.GetComponent<Rigidbody>().linearVelocity = upper.transform.TransformDirection(new Vector3(5.5f, 0, 0));
                    selectorRouted = true;
                }
                else if (ForceColor && !selectorRouted)
                {
                    var pocket = upper.colorRoutePockets.Single(p => p.kind == (MedalJackpotKind)(roundIndex - 9));
                    Place(upper.ActiveBall.GetComponent<Rigidbody>(), pocket.GetComponent<Collider>().bounds.center); selectorRouted = true;
                }
            }
            return;
        }
        if (selectionEvents <= selectionBaseline) return;
        if (!results.ContainsKey("realSelectorResolvesColorOrNaturalOut_" + roundIndex))
        {
            bool hitColor = !selectionTimedOut && selectedKind.HasValue && upper.LastColorPocket != null && upper.LastColorPocket.kind == selectedKind;
            bool naturalOut = !selectionTimedOut && !selectedKind.HasValue && selectionExitedBowl && controller.LastPayout == 0 && !controller.LastDrawTimedOut;
            bool timedOut = selectionTimedOut && !selectedKind.HasValue && controller.LastPayout == 15 && controller.LastDrawTimedOut;
            Check("realSelectorResolvesColorOrNaturalOut_" + roundIndex, (TimeoutRound ? timedOut : hitColor || naturalOut) &&
                selectionEvents == selectionBaseline + 1 && upper.CurrentTicket == ticket && game.medals == selectorWallet + (TimeoutRound ? 15 : 0) &&
                (!ForceColor || (hitColor && selectedKind == (MedalJackpotKind)(roundIndex - 9))));
            if (!TimeoutRound) Check("allThreeRotorsAndGatesOpenedBeforeRouting_" + roundIndex, rotationObserved && gatesLiftObserved);
            int wallet = game.medals; controller.FinishColorSelection(upper, ticket, selectedKind, false); upper.BeginColorSelection(controller, ticket);
            Check("duplicateSelectorCannotLaunchOrPayAgain_" + roundIndex, game.medals == wallet && selectionEvents == selectionBaseline + 1 && !upper.IsSelectingColor);
            if (NaturalSelectorRound && (hitColor || naturalOut)) naturalSelectorCount++;
            if (GuardRound)
            {
                bool sameBallOut = naturalOut && guardCollisionObserved && guardStep == 5 && Time.fixedTime > placedFixedTime &&
                    controller.TotalLotteries == lotteriesBefore && game.medals == selectorWallet && !controller.IsColorRoundActive;
                Check(GuardCheckName("actualExteriorOutEndsWithoutCompensation"), sameBallOut);
                if (sameBallOut) successfulGuardCount++;
            }
            if (UnguardedOutsideRound)
            {
                bool actuallyOutside = unguardedExitPosition.HasValue && new Vector2(unguardedExitPosition.Value.x, unguardedExitPosition.Value.z + 5.3f).magnitude > 1.95f + unguardedBallRadius + .035f;
                Check("unguardedAirborneExitEndsWithoutCompensation", naturalOut && !selectionGuardUsed && selectorRouted && actuallyOutside &&
                    Time.fixedTime > placedFixedTime && controller.TotalLotteries == lotteriesBefore && game.medals == selectorWallet && !controller.IsColorRoundActive);
            }
            if (!selectedKind.HasValue)
            {
                if (TimeoutRound) Check("selectorWatchdogPaysFifteenOnlyOnce", timedOut && game.medals == selectorWallet + 15 && controller.TotalLotteries == lotteriesBefore && !controller.IsColorRoundActive);
                CompleteRound(); return;
            }
        }
        if (controller.ActiveKind.HasValue)
        {
            var station = controller.stations[(int)controller.ActiveKind.Value];
            if (station.ActiveBall == null) return;
            if (!lowerChecked)
            {
                lowerChecked = true; colorWallet = game.medals; expectedPool = controller.jackpotPools[(int)station.kind]; jackpotsBefore = controller.TotalJackpots;
                Check("onlyPhysicallySelectedStationDraws_" + roundIndex, station.kind == selectedKind && station.CurrentTicket == ticket &&
                    controller.TotalLotteries == lotteriesBefore + 1 && controller.stations.Count(s => s.IsDrawing) == 1);
                int wallet = game.medals;
                var wrong = controller.stations.First(s => s != station); controller.FinishLottery(wrong, ticket, true, 999); controller.FinishLottery(station, ticket + 1, true, 999);
                Check("wrongColoredRewardCallbacksRejected_" + roundIndex, game.medals == wallet && controller.ActiveKind == station.kind && station.IsDrawing);
                CheckPoolLabels("beforeColorResult_" + roundIndex);
            }
            if (ForceColor && !lowerRouted)
            {
                var jackpot = station.GetComponentsInChildren<MedalLotteryPocket>().Single(p => p.jackpot);
                RestrictPockets(station, jackpot); Place(station.ActiveBall.GetComponent<Rigidbody>(), jackpot.GetComponent<Collider>().bounds.center); lowerRouted = true;
            }
            return;
        }
        if (colorEvents <= colorBaseline || Now - colorFinishedAt < .15) return;
        var completed = controller.stations[(int)finishedKind.Value];
        Check("coloredPocketRewardsExactlyOnce_" + roundIndex, colorEvents == colorBaseline + 1 && finishedKind == selectedKind && !completed.LastTimedOut &&
            completed.LastPocket != null && game.medals == colorWallet + lastColorPayout && controller.TotalLotteries == lotteriesBefore + 1);
        int afterWallet = game.medals; controller.FinishLottery(completed, ticket, finishedJackpot, lastColorPayout);
        Check("duplicateColoredRewardIgnored_" + roundIndex, game.medals == afterWallet && colorEvents == colorBaseline + 1);
        if (ForceColor)
        {
            Check("actualJackpotTriggerResetsPool_" + completed.kind, lowerRouted && Time.fixedTime > placedFixedTime && completed.LastPocket.jackpot && finishedJackpot &&
                lastColorPayout == expectedPool && controller.TotalJackpots == jackpotsBefore + 1 && controller.jackpotPools[(int)completed.kind] == settings.jackpotResetValues[(int)completed.kind]);
            CheckPoolLabels("afterJackpotReset_" + completed.kind);
            Check("jackpotBannerAmount_" + completed.kind, arcade.payoutBanner != null && arcade.payoutBanner.gameObject.activeInHierarchy &&
                arcade.payoutBanner.text.Contains("JACKPOT") && arcade.payoutBanner.text.Contains("+" + lastColorPayout + "枚"));
            Capture("upper-" + completed.kind.ToString().ToLowerInvariant() + "-jackpot.png", completed.kind.ToString());
        }
        CompleteRound();
    }

    private static void SampleNaturalSelector()
    {
        if (!NaturalSelectorRound || !upper.IsSelectingColor || upper.ActiveBall == null || Time.time < nextSelectorSampleAt) return;
        nextSelectorSampleAt = Time.time + 1f;
        var body = upper.ActiveBall.GetComponent<Rigidbody>();
        Vector3 position = body == null ? upper.ActiveBall.transform.position : body.position;
        var ballCollider = upper.ActiveBall.GetComponent<Collider>();
        float radius = ballCollider == null ? .25f : ballCollider.bounds.extents.x;
        var nearby = Physics.OverlapSphere(position, radius + .03f).Where(c => c != null && c != ballCollider &&
            (body == null || c.attachedRigidbody != body) && !c.transform.IsChildOf(upper.ActiveBall.transform)).Select(c => new {
                name = c.name, parent = c.transform.parent == null ? null : c.transform.parent.name,
                trigger = c.isTrigger, localCenter = Pos(upper.transform.InverseTransformPoint(c.bounds.center))
            }).ToArray();
        naturalSelectorSamples.Add(new {
            round = roundIndex, ticket = upper.CurrentTicket, phase = phase.ToString(), stage = "ColorSelection",
            playSeconds = Time.time, elapsedWallSeconds = Now - roundStarted,
            localPosition = Pos(upper.transform.InverseTransformPoint(position)),
            worldPosition = Pos(position), nearbyColliders = nearby,
            localVelocity = body == null ? null : Pos(upper.transform.InverseTransformVector(body.linearVelocity)),
            worldVelocity = body == null ? null : Pos(body.linearVelocity),
            angularVelocity = body == null ? null : Pos(body.angularVelocity),
            speed = body == null ? 0 : body.linearVelocity.magnitude,
            sleeping = body != null && body.IsSleeping(), kinematic = body != null && body.isKinematic,
            upper.OutBlockUsed, upper.OutBlockReady, upper.ColorGatesOpen,
            upper.LastExitedBowl,
            tokenConsumed = upper.ActiveBall.IsConsumed
        });
    }

    private static void ObserveDividerSpeeds()
    {
        for (int i = 0; i < 3; i++)
        {
            float speed = controller.stations[i].currentDividerSpeed;
            if (observedDividerSpeeds[i].Add(speed)) dividerSpeedSamples.Add(new {
                round = roundIndex, station = controller.stations[i].kind.ToString(), ticket,
                speedDegreesPerSecond = speed, selectorSeconds = Time.fixedTime - selectorStartedFixed,
                playSeconds = Time.time
            });
        }
    }

    private static void IncomingWhitePlate(LotteryBallToken token)
    {
        var body = token.GetComponent<Rigidbody>(); var plate = GuardBody.GetComponent<Collider>();
        Require(plate != null && plate.enabled && !plate.isTrigger, "The white out block needs a real solid collider.");
        var normal = GuardBody.transform.forward.normalized;
        float ballRadius = token.GetComponent<Collider>().bounds.extents.x;
        Vector3 face = plate.ClosestPoint(plate.bounds.center + normal * (plate.bounds.extents.magnitude + 1f));
        var position = face + normal * (ballRadius + .025f) + Vector3.up * .1f;
        Place(body, position); body.linearVelocity = -normal * 3.5f;
    }

    private static void TickOutGuard()
    {
        if (guardStep == 0)
        {
            Require(GuardBody != null && upper.outBlocks[GuardFaceIndex] != null && GuardOutflow != null, "White blocker or actual exterior OUT trigger missing.");
            guardedToken = upper.ActiveBall;
            Require(upper.OutBlockReady && !upper.OutBlockUsed && guardedToken != null, "The eligible selector did not start with a fresh white blocker.");
            IncomingWhitePlate(guardedToken); guardStep = 1; return;
        }
        if (guardStep == 1)
        {
            if (!upper.OutBlockUsed) return;
            guardCollisionObserved = true; guardConsumedFixed = Time.fixedTime;
            Check(GuardCheckName("actualWhiteCollisionConsumesOnlyOneGuard"), Time.fixedTime > placedFixedTime && !upper.OutBlockReady &&
                ReferenceEquals(guardedToken, upper.ActiveBall) && !guardedToken.IsConsumed && upper.IsSelectingColor && game.medals == selectorWallet);
            int originalTicket = guardedToken.ticket; guardedToken.ticket++;
            bool wrongTicket = !upper.TryResolveOutflow(GuardOutflow, guardedToken); guardedToken.ticket = originalTicket;
            var originalStation = guardedToken.station; guardedToken.station = controller.stations[0];
            bool wrongStation = !upper.TryResolveOutflow(GuardOutflow, guardedToken); guardedToken.station = originalStation;
            Check(GuardCheckName("outflowRejectsWrongTokenWithoutConsumingIt"), wrongTicket && wrongStation && !guardedToken.IsConsumed && upper.IsSelectingColor);
            Capture("upper-white-block-used-" + GuardFaceIndex + ".png", null);
            var normal = GuardBody.transform.forward.normalized;
            Place(guardedToken.GetComponent<Rigidbody>(), GuardBody.position + normal * 1.3f + Vector3.up * .1f);
            guardStep = 2; return;
        }
        if (guardStep == 2)
        {
            if (Time.fixedTime <= placedFixedTime) return;
            IncomingWhitePlate(guardedToken); guardStep = 3; return;
        }
        if (guardStep == 3)
        {
            if (Time.fixedTime - placedFixedTime < .04f) return;
            Check(GuardCheckName("secondWhiteContactCannotRestoreGuardOrReplaceBall"), upper.OutBlockUsed && !upper.OutBlockReady &&
                ReferenceEquals(guardedToken, upper.ActiveBall) && !guardedToken.IsConsumed && upper.IsSelectingColor && game.medals == selectorWallet);
            // Keep this same dynamic test ball away from the colored bins while the plate withdraws.
            Place(guardedToken.GetComponent<Rigidbody>(), upper.colorLaunchPoint.position + Vector3.up * 3f);
            guardStep = 4; return;
        }
        if (guardStep == 4)
        {
            if (Time.fixedTime - guardConsumedFixed < .75f) return;
            Vector3 closedWorld = GuardBody.transform.parent.TransformPoint(upper.outBlockClosedPositions[GuardFaceIndex]);
            Check(GuardCheckName("whitePlateRetractsDownIntoMachine"), GuardBody.position.y < closedWorld.y - .8f);
            Check(GuardCheckName("oneGuardContactRetractsAllEightWhiteBlocks"), Enumerable.Range(0, 8).All(i =>
                Vector3.Distance(upper.outBlockBodies[i].position, upper.outBlockBodies[i].transform.parent.TransformPoint(
                    upper.outBlockClosedPositions[i] + Vector3.up * upper.outBlockRaiseHeight)) < .035f));
            var exterior = GuardOutflow.GetComponent<Collider>(); Require(exterior != null && exterior.isTrigger, "Exterior OUT collider is not a trigger.");
            Vector3 normal = GuardBody.transform.forward.normalized;
            Check(GuardCheckName("outTriggerLiesBeyondFrontWhitePlate"), Vector3.Dot(exterior.bounds.center - closedWorld, normal) < -.2f);
            Place(guardedToken.GetComponent<Rigidbody>(), exterior.bounds.center); guardStep = 5;
        }
    }

    private static void CheckOnce(string name, bool passed) { if (!results.ContainsKey(name)) Check(name, passed); }
    private static void TestBusySettings()
    {
        string before = JsonUtility.ToJson(settings.CaptureValues()); int[] pools = (int[])controller.jackpotPools.Clone();
        settingsUI.Open(); settingsUI.targetPayoutInput.text = "80"; settingsUI.ApplyFromInputs();
        CheckOnce("settingsCannotMutateActiveDraw", JsonUtility.ToJson(settings.CaptureValues()) == before && pools.SequenceEqual(controller.jackpotPools) && settingsUI.IsOpen);
        settingsUI.Close();
    }
    private static void CompleteRound()
    {
        RestorePocketStates(); upper.colorSelectionTimeout = originalSelectorTimeout;
        rounds.Add(new { index = roundIndex, ticket, upperWin = lastUpperWin,
            upperPocket = upper.LastPocket == null ? null : upper.LastPocket.transform.parent.name,
            naturalUpper = roundIndex >= 3 && roundIndex <= 5, naturalSelector = NaturalSelectorRound,
            selectorPocket = upper.LastColorPocket == null ? null : upper.LastColorPocket.kind.ToString(),
            selectedColor = selectedKind?.ToString(), finalColor = finishedKind?.ToString(), payout = colorEvents > colorBaseline ? lastColorPayout : lastUpperWin,
            jackpot = colorEvents > colorBaseline && finishedJackpot, upperTimedOut, selectorTimedOut = selectionEvents > selectionBaseline && selectionTimedOut,
            guardUsed = selectionGuardUsed, forcedGuardCollision = guardCollisionObserved, guardStep,
            exitedBowl = selectionExitedBowl, unguardedExitPosition = unguardedExitPosition.HasValue ? Pos(unguardedExitPosition.Value) : null,
            forcedGuardFace = GuardRound ? GuardFaceIndex : (int?)null,
            naturalOut = selectionEvents > selectionBaseline && !selectedKind.HasValue && !selectionTimedOut,
            lowerDraws = controller.TotalLotteries - lotteriesBefore, elapsedWallSeconds = Now - roundStarted });
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
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
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
        if (initialized)
        {
            CleanupNormalBurst();
            if (upper != null) upper.colorSelectionTimeout = originalSelectorTimeout;
            if (game != null) { game.OnPayoutMedalSpawned -= OnPayout; if (realCap > 0) game.maxMedalsOnBoard = realCap; game.SetSettingsOpen(false); }
            if (controller != null)
            {
                controller.OnUpperLotteryFinished -= OnUpperFinish; controller.OnColorSelectionFinished -= OnSelectionFinish; controller.OnLotteryFinished -= OnColorFinish;
            }
            if (settings != null) { settings.SettingsFileOverride = originalOverride; settings.persistSettings = savedPersistence; }
            Time.timeScale = originalTimeScale;
            try
            {
                Check("userSettingsFileRemainedUntouched", FileHash(realSettingsPath) == realSettingsHash);
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
        results["selectorStability"] = StabilityMode; results["targetRounds"] = TargetRounds;
        results["expectedNaturalSelectorCount"] = ExpectedNaturalSelectors; results["maximumWallSeconds"] = MaximumSeconds;
        results["naturalUpperCount"] = naturalUpperCount; results["naturalSelectorCount"] = naturalSelectorCount; results["rounds"] = rounds.ToArray();
        results["naturalSelectorSamples"] = naturalSelectorSamples.ToArray();
        results["successfulGuardFaceCount"] = successfulGuardCount;
        results["dividerSpeedSamples"] = dividerSpeedSamples.ToArray();
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
        MedalPusherExpansionBuilder.WriteReport("upper-play-validation.json", results);
        Debug.Log("[MedalPusher] Upper flow Play check complete: " + ((bool)results["success"] ? "PASS" : "FAIL"));
        if (stopPlay)
        {
            EditorApplication.isPaused = false;
            if (Application.isBatchMode) EditorApplication.Exit((bool)results["success"] ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}

/// <summary>Transient collision observer; never saved to a scene or attached to runtime prefabs.</summary>
public sealed class MedalUpperFlowPayoutProbe : MonoBehaviour
{
    [NonSerialized] public int RecordIndex;
    private void OnCollisionEnter(Collision collision) => MedalUpperFlowPlayCheck.ObserveBurstCollision(RecordIndex, collision);
    private void OnCollisionStay(Collision collision) => MedalUpperFlowPlayCheck.ObserveBurstCollision(RecordIndex, collision);
}
