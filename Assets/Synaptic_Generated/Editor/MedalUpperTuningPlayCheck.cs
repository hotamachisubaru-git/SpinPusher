using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Native upper gates, repeatable guards, silver lottery balls, and optional lottery guidance.</summary>
[InitializeOnLoad]
public static class MedalUpperTuningPlayCheck
{
    private const string Key = "MedalPusher.UpperTuningCheck";
    private const double MaximumSeconds = 150;
    private enum Phase { Boot, WaitStart, ClosedStart, ClosedFlight, BumperFlight, GateWait, GuardStart, GuardFlight,
        DuplicateAway, DuplicateFlight, AllLower, LowerDelay, RegenRise, CooldownFlight, CooldownWait,
        PartialGuardFlight, VisitStart, VisitPort, VisitPause, VisitResult, OutWait, OutHold, NewStart, NewWait, NewGuardFlight, NewGuardWithdraw, NewOut, EndHold, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalBallLotteryStation upper, lower;
    private static MedalArcadeUI ui;
    private static MedalArcadeSettings settings;
    private static MedalArcadeSettingsUI settingsUI;
    private static LotteryBallToken upperBall, lowerBall;
    private static MedalLotteryPocket resultPocket;
    private static MedalColorRoutePocket targetPort;
    private static readonly Dictionary<string, object> checks = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> boundaries = new List<object>();
    private static readonly List<object> contacts = new List<object>();
    private static readonly List<object> guardSamples = new List<object>();
    private static readonly List<object> visits = new List<object>();
    private static readonly List<object> closedSamples = new List<object>();
    private static readonly Dictionary<Behaviour, bool> enabledStates = new Dictionary<Behaviour, bool>();
    private static readonly Dictionary<Collider, bool> pocketStates = new Dictionary<Collider, bool>();
    private static readonly Dictionary<MedalOutBlock, int> guardContacts = new Dictionary<MedalOutBlock, int>();
    private static readonly Dictionary<Rigidbody, int> gateContacts = new Dictionary<Rigidbody, int>();
    private static readonly Dictionary<MedalColorRoutePocket, int> portContacts = new Dictionary<MedalColorRoutePocket, int>();
    private static readonly Dictionary<MedalLotteryPocket, int> resultContacts = new Dictionary<MedalLotteryPocket, int>();
    private static bool initialized, originalPersistence, delayWitnessed, raisingWitnessed;
    private static string originalOverride, realSettingsPath, realSettingsHash, temporaryPath, ballId, firstBallId, numericSettingsSignature;
    private static double phaseAt;
    private static float stageTime, placedFixed, lowerAt, regenerationStartedAt, regenerationEndedAt;
    private static int ticket, firstTicket, closedIndex, guardIndex, regenerationCycle, visitIndex, guardContactBefore, gateContactBefore;
    private static int portContactBefore, resultContactBefore, walletBefore, expectedPrize, colorBaseline, upperBaseline;
    private static int outContactBefore;
    private static int upperEvents, colorEvents, lastUpperPayout, lastColorPayout, outContacts, expectedWin;
    private static long queueBefore;
    private static bool lastColorJackpot;
    private static MedalJackpotKind lastColorKind;
    private static bool[] visitGuards;
    private static Quaternion[] closedAngles, openAngles;
    private static UnityEngine.Random.State originalRandom;
    private static float originalTimeScale;

    static MedalUpperTuningPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlay;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Upper Tuning Play Check")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before the upper tuning check.");
        Reset();
        foreach (string other in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck", "MedalPusher.UpperFlowCheck",
            "MedalPusher.IntegratedUpperFlowCheck", "MedalPusher.ClickBallOnlyCheck", "MedalPusher.SlotMotionCheck", "MedalPusher.HighWinCheck", "MedalPusher.MountedPocketsCheck" })
            SessionState.SetBool(other, false);
        SessionState.SetFloat(Key + ".Started", (float)Now); SessionState.SetString(Key + ".Utc", DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(Key + ".Background", Application.runInBackground); Application.runInBackground = true;
        SessionState.SetBool(Key, true); EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Medal Pusher/Inspect Upper Tuning Play Check")]
    public static void Inspect() => Write("upper-tuning-play-state.json", new {
        timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false), playing = EditorApplication.isPlaying,
        phase = phase.ToString(), elapsedWallSeconds = Elapsed, closedIndex, guardIndex, regenerationCycle, visitIndex, errors, contacts,
        upper = upper == null ? null : new { upper.IsDrawing, upper.IsUpperSuspended, upper.BumperWin, upper.ColorGatesOpen,
            upper.UsedOutBlockCount, upper.OutBlockRegenerationCount, upper.IsOutBlockRegenerationPending, upper.IsOutBlockRegenerating, upper.OutBlockReady,
            ball = upper.ActiveBall == null ? null : Pos(upper.ActiveBall.GetComponent<Rigidbody>().position) },
        lower = lower == null ? null : new { lower.kind, lower.IsDrawing, activeBall = lower.ActiveBall == null ? null : lower.ActiveBall.name },
        upperEvents, colorEvents, lastUpperPayout, lastColorPayout
    });

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(Key + ".Started", (float)Now);
    private static object Pos(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Check(string name, bool value) { checks[name] = value; if (!value) errors.Add("Failed: " + name); }
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static int Count<T>(Dictionary<T, int> counts, T item) => item != null && counts.TryGetValue(item, out int count) ? count : 0;
    private static void Disable(Behaviour component) { if (component == null) return; enabledStates[component] = component.enabled; component.enabled = false; }
    private static void Enter(Phase next) { phase = next; phaseAt = Now; stageTime = Time.time; }
    private static bool SameBall() => upperBall != null && ReferenceEquals(upper.ActiveBall, upperBall) && !upperBall.IsConsumed &&
        upper.CurrentTicket == ticket && upperBall.ticket == ticket && upperBall.station == upper;
    private static void Reset()
    {
        phase = Phase.Boot; initialized = false; settings = null; game = null; controller = null; upper = lower = null; ui = null; settingsUI = null;
        upperBall = lowerBall = null; upperEvents = colorEvents = outContacts = closedIndex = guardIndex = regenerationCycle = visitIndex = 0;
        originalOverride = realSettingsPath = realSettingsHash = temporaryPath = ballId = firstBallId = null;
        checks.Clear(); errors.Clear(); boundaries.Clear(); contacts.Clear(); guardSamples.Clear(); visits.Clear(); closedSamples.Clear();
        enabledStates.Clear(); pocketStates.Clear(); guardContacts.Clear(); gateContacts.Clear(); portContacts.Clear(); resultContacts.Clear();
    }
    private static void OnPlay(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) IsolateSettings();
        if (state == PlayModeStateChange.ExitingPlayMode) { errors.Add("Play exited before completion."); Finish(false); }
    }
    private static void IsolateSettings()
    {
        if (realSettingsPath != null) return;
        settings = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettings>(); if (settings == null) return;
        originalPersistence = settings.persistSettings; originalOverride = settings.SettingsFileOverride;
        realSettingsPath = Path.Combine(Application.persistentDataPath, "medal-arcade-settings.json"); realSettingsHash = Hash(realSettingsPath);
        temporaryPath = Path.Combine(Path.GetTempPath(), "medal-upper-tuning-" + Guid.NewGuid().ToString("N") + ".json");
        settings.SettingsFileOverride = temporaryPath; settings.persistSettings = false;
    }
    private static bool Ready()
    {
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>(); controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        ui = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>(); settingsUI = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettingsUI>();
        return game != null && controller != null && ui != null && settingsUI != null && controller.upperStation != null && Time.frameCount >= 2 &&
            controller.OnStateChanged != null && controller.OnStateChanged.GetInvocationList().Any(d => ReferenceEquals(d.Target, ui));
    }
    private static void Setup()
    {
        IsolateSettings(); Require(settings != null, "Settings fixture missing."); originalRandom = UnityEngine.Random.state;
        originalTimeScale = Time.timeScale; Time.timeScale = 1; upper = controller.upperStation;
        CheckMaterials();
        Disable(game); foreach (var input in UnityEngine.Object.FindObjectsByType<MedalInputHandler>(FindObjectsSortMode.None)) Disable(input);
        foreach (var pocket in UnityEngine.Object.FindObjectsByType<MedalSlotPocket>(FindObjectsSortMode.None)) Disable(pocket);
        foreach (var manager in UnityEngine.Object.FindObjectsByType<PrizeDropManager>(FindObjectsSortMode.None)) Disable(manager);
        int removed = game.itemsRoot.GetComponentsInChildren<MedalItem>().Length;
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
        { item.collected = true; game.UnregisterItem(item); item.gameObject.SetActive(false); UnityEngine.Object.Destroy(item.gameObject); }
        ClearQueues(); controller.targetPayoutPercent = controller.slotBallChancePercent = controller.slotMedalChancePercent = 0; controller.rescueSpins = 0;
        Set(controller, "<SpinCredits>k__BackingField", 0); Set(controller, "nextSpinAt", float.PositiveInfinity); Set(controller, "nextLotteryAt", 0f);
        controller.jackpotPools = new[] { 123, 234, 345 }; controller.enabled = true;
        controller.OnUpperLotteryFinished += UpperFinished; controller.OnLotteryFinished += ColorFinished;
        boundaries.Add(new { phase = "fixture-start", removedSceneItems = removed, heldGamePusherAndPayoutSpawning = true, wallet = game.medals,
            payoutQueue = game.PendingPayoutMedals, fixturePools = controller.jackpotPools.ToArray(), temporarySettingsPath = temporaryPath });
        CheckSettingsIdle();
        Set(controller, "<IsHighProbability>k__BackingField", true); Set(controller, "<CarriedUpperWin>k__BackingField", 100);
        boundaries.Add(new { phase = "explicit-initial-carry-fixture", bank = 100, high = true,
            note = "Only initial already-paid carry is seeded; all subsequent WIN, guard and route results require native contacts." });
        initialized = true; controller.QueueBallDraw(MedalJackpotKind.Ruby); Enter(Phase.WaitStart);
    }
    private static void CheckMaterials()
    {
        var sceneBalls = game.itemsRoot.GetComponentsInChildren<MedalItem>().Where(i => i.isBall).ToArray();
        Check("boardBallPrefabsRemainGreen", controller.ballPrefabs != null && controller.ballPrefabs.Length >= 3 && controller.ballPrefabs.All(Green));
        Check("existingSceneBoardBallsRemainGreen", sceneBalls.Length > 0 && sceneBalls.All(i => Green(i.gameObject)));
        foreach (var station in controller.stations.Concat(new[] { upper }))
        {
            var prefab = station.lotteryBallPrefab; var sphere = prefab == null ? null : prefab.GetComponent<SphereCollider>();
            Check("drawSphereIsSilverMetallicWithOriginalRadius_" + (station.isUpperStation ? "Upper" : station.kind.ToString()),
                Silver(prefab) && sphere != null && Mathf.Abs(sphere.radius * Mathf.Max(prefab.transform.lossyScale.x, prefab.transform.lossyScale.y, prefab.transform.lossyScale.z) - .26f) < .001f);
            var material = prefab == null ? null : prefab.GetComponent<Renderer>().sharedMaterial;
            boundaries.Add(new { phase = "actual-draw-material", station = station.isUpperStation ? "Upper" : station.kind.ToString(),
                assetPath = material == null ? null : AssetDatabase.GetAssetPath(material),
                metallic = material == null ? -1 : material.GetFloat("_Metallic"), smoothness = material == null ? -1 : material.GetFloat("_Smoothness"),
                radius = sphere == null ? 0 : sphere.radius * Mathf.Max(prefab.transform.lossyScale.x, prefab.transform.lossyScale.y, prefab.transform.lossyScale.z) });
        }
    }
    private static string NumericSignature()
    { var data = JObject.Parse(JsonUtility.ToJson(settings.CaptureValues())); data.Remove("showLotteryStatus"); return data.ToString(Newtonsoft.Json.Formatting.None); }
    private static void CheckSettingsIdle()
    {
        Require(settingsUI.showLotteryStatusToggle != null && ui.statusText != null && ui.lotteryStatusPanel != null, "Status toggle or DrawStatus references missing.");
        ui.Refresh(); Check("lotteryTextAndBackgroundHiddenByDefault", !settings.showLotteryStatus && !ui.statusText.gameObject.activeSelf && !ui.lotteryStatusPanel.activeSelf);
        Check("settingsToggleHasJapaneseLabel", settingsUI.showLotteryStatusToggle.GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t => t.text == "抽選案内を表示"));
        settings.persistSettings = true; numericSettingsSignature = NumericSignature(); settingsUI.Open();
        settingsUI.showLotteryStatusToggle.isOn = true;
        Check("realToggleCallbackShowsBothAndSavesTemporaryFile", settings.showLotteryStatus && ui.statusText.gameObject.activeSelf && ui.lotteryStatusPanel.activeSelf &&
            File.Exists(temporaryPath) && JsonUtility.FromJson<MedalArcadeSettings.Values>(File.ReadAllText(temporaryPath)).showLotteryStatus && NumericSignature() == numericSettingsSignature);
        settings.showLotteryStatus = false; ui.Refresh();
        bool loaded = settings.Load(); ui.Refresh();
        Check("savedStatusPreferenceReloadsWithoutNumericChanges", loaded && settings.showLotteryStatus && ui.statusText.gameObject.activeSelf && ui.lotteryStatusPanel.activeSelf && NumericSignature() == numericSettingsSignature);
        JObject legacy = JObject.Parse(File.ReadAllText(temporaryPath)); legacy.Remove("showLotteryStatus"); File.WriteAllText(temporaryPath, legacy.ToString());
        loaded = settings.Load(); ui.Refresh();
        Check("legacyVersionOneWithoutStatusFlagDefaultsHidden", loaded && legacy.Value<int>("version") == 1 && !settings.showLotteryStatus &&
            !ui.statusText.gameObject.activeSelf && !ui.lotteryStatusPanel.activeSelf && NumericSignature() == numericSettingsSignature);
        settingsUI.Close(); settings.persistSettings = false;
    }
    private static void CheckSettingsBusy()
    {
        Park(); int wallet = game.medals, spins = controller.TotalSpins, win = upper.BumperWin; int[] pools = controller.jackpotPools.ToArray();
        settings.persistSettings = true; settingsUI.Open(); numericSettingsSignature = NumericSignature();
        foreach (bool visible in new[] { true, false })
        {
            settingsUI.showLotteryStatusToggle.isOn = visible;
            Check("actualBusyTogglePreservesDrawAndPools_" + visible, settings.IsBusy && settings.showLotteryStatus == visible &&
                ui.statusText.gameObject.activeSelf == visible && ui.lotteryStatusPanel.activeSelf == visible && SameBall() && upper.BumperWin == win &&
                game.medals == wallet && controller.TotalSpins == spins && controller.jackpotPools.SequenceEqual(pools) && NumericSignature() == numericSettingsSignature &&
                JsonUtility.FromJson<MedalArcadeSettings.Values>(File.ReadAllText(temporaryPath)).showLotteryStatus == visible);
        }
        settingsUI.targetPayoutInput.SetTextWithoutNotify("0"); settingsUI.ApplyFromInputs();
        Check("numericApplyStillRejectedDuringActualUpperDraw", NumericSignature() == numericSettingsSignature && SameBall() && upper.BumperWin == win &&
            controller.jackpotPools.SequenceEqual(pools) && settingsUI.feedbackText.text.Contains("抽選中"));
        settingsUI.Close(); settings.persistSettings = false;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            Require(Elapsed < MaximumSeconds, "Upper tuning check exceeded 150 seconds.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling) return;
            if (!initialized) { if (!Ready()) { Require(Elapsed < 25, "Runtime Start readiness timeout."); return; } Setup(); return; }
            Require(Now - phaseAt < 15, "Native fixture phase timeout: " + phase);
            switch (phase)
            {
                case Phase.WaitStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    CaptureBall(); firstBallId = ballId; firstTicket = ticket; expectedWin = 100;
                    Check("normalNativeQueuedBallStartsPaid100AndThreeGatesClosed", SameBall() && upper.BumperWin == 100 && controller.LiveUpperWin == 100 &&
                        controller.UpperWinCarriedAtStart == 100 && controller.UpperWinEarnedThisRound == 0 && !upper.DirectJpcGatesUnlocked && !upper.ColorGatesOpen && !controller.AreColorGatesUnlocked);
                    Check("regenerationDefaultsAreEnabledPoint40AndPoint18", upper.regenerateOutBlocks && Mathf.Abs(upper.outBlockRegenerationDelay - .4f) < .001f &&
                        Mathf.Abs(upper.outBlockRegenerationCooldown - .18f) < .001f && upper.outBlocks.Length == 4 && upper.OutBlockRegenerationCount == 0);
                    CheckSettingsBusy(); closedAngles = Angles(); Enter(Phase.ClosedStart); break;
                case Phase.ClosedStart:
                    Park(); if (!GatesAtTarget(false)) return;
                    targetPort = upper.colorRoutePockets.Single(p => (int)p.kind == closedIndex); gateContactBefore = Count(gateContacts, targetPort.gateBody);
                    IncomingPort(targetPort, closedIndex % 2 == 0 ? -.16f : .16f); Enter(Phase.ClosedFlight); break;
                case Phase.ClosedFlight:
                    if (Count(gateContacts, targetPort.gateBody) <= gateContactBefore) return;
                    Check("100WinNativeGateBlocksIncomingSphere_" + targetPort.kind, SameBall() && upper.BumperWin == 100 && !upper.IsUpperSuspended &&
                        !controller.IsColorRoundActive && colorEvents == 0 && upper.LastColorPocket == null && !upper.ColorGatesOpen &&
                        controller.stations.All(s => !s.IsDrawing && !s.IsRotating && Mathf.Abs(s.currentDividerSpeed) < .001f) &&
                        targetPort.transform.InverseTransformPoint(upperBall.GetComponent<Rigidbody>().position).z < .01f);
                    closedSamples.Add(new { kind = targetPort.kind.ToString(), gateNativeContacts = Count(gateContacts, targetPort.gateBody) - gateContactBefore,
                        actualPortLocalPosition = Pos(targetPort.transform.InverseTransformPoint(upperBall.GetComponent<Rigidbody>().position)), ticket, ballId });
                    Park(); closedIndex++; if (closedIndex < 3) Enter(Phase.ClosedStart);
                    else { Check("allColorDividerRotationsStayStoppedAt100", AnglesEqual(closedAngles)); IncomingBumper(); Enter(Phase.BumperFlight); }
                    break;
                case Phase.BumperFlight:
                    if (upper.TotalBumperHits == 0) return;
                    Check("nativeBumper102OpensColorGatesAndOnlyTwoNewWin", SameBall() && upper.TotalBumperHits == 1 && upper.BumperWin == 102 &&
                        controller.LiveUpperWin == 102 && controller.UpperWinEarnedThisRound == 2 && upper.ColorGatesOpen && controller.AreColorGatesUnlocked);
                    expectedWin = 102; Park(); openAngles = Angles(); Enter(Phase.GateWait); break;
                case Phase.GateWait:
                    Park(); if (Time.time - stageTime < .65f || !GatesAtTarget(true)) return;
                    Check("allThreePhysicalGatesRaiseAndRandomDividersMove", SameBall() && GatesAtTarget(true) &&
                        controller.stations.All(s => s.IsRotating && Mathf.Abs(s.currentDividerSpeed) >= 18f) && AnglesAllMoved(openAngles));
                    guardIndex = 0; regenerationCycle = 0; Enter(Phase.GuardStart); break;
                case Phase.GuardStart: Park(); IncomingGuard(guardIndex); Enter(Phase.GuardFlight); break;
                case Phase.GuardFlight:
                    if (!upper.IsOutBlockUsed(guardIndex)) return;
                    Check("nativeGuardSingleUseCycle" + regenerationCycle + "Index" + guardIndex, Count(guardContacts, upper.outBlocks[guardIndex]) > guardContactBefore &&
                        SameBall() && upper.BumperWin == expectedWin && upper.UsedOutBlockCount == guardIndex + 1 && upper.OutBlockRegenerationCount == regenerationCycle);
                    if (guardIndex < 3) Check("partialGuardsDoNotRegenerateCycle" + regenerationCycle + "Index" + guardIndex,
                        !upper.IsOutBlockRegenerationPending && !upper.IsOutBlockRegenerating);
                    RecordGuards("native-hit"); Park();
                    if (regenerationCycle == 0 && guardIndex == 0) Enter(Phase.DuplicateAway); else NextGuard();
                    break;
                case Phase.DuplicateAway:
                    Park(); if (Time.time - stageTime < .025f) return;
                    IncomingGuard(0); Enter(Phase.DuplicateFlight); break;
                case Phase.DuplicateFlight:
                    if (Count(guardContacts, upper.outBlocks[0]) <= guardContactBefore) return;
                    Check("nativeSecondContactBeforeRetractionCannotReuseOneGuard", SameBall() && upper.UsedOutBlockCount == 1 && upper.OutBlockRegenerationCount == 0 &&
                        Time.time - stageTime < upper.outBlockRetractionDelay && !upper.IsOutBlockRegenerationPending);
                    RecordGuards("duplicate-contact-before-retraction"); Park(); NextGuard(); break;
                case Phase.AllLower:
                    Park(); if (!OutBlocksAtTarget(true)) return;
                    lowerAt = Time.time; delayWitnessed = raisingWitnessed = false; regenerationStartedAt = -1;
                    Check("allFourPhysicallyWithdrawBeforeRegenerationCycle" + regenerationCycle,
                        SameBall() && upper.UsedOutBlockCount == 4 && upper.OutBlockUsedStates.All(b => b) && upper.IsOutBlockRegenerationPending && !upper.OutBlockReady);
                    RecordGuards("all-four-down"); Enter(Phase.LowerDelay); break;
                case Phase.LowerDelay:
                    Park(); if (Time.time - lowerAt < upper.outBlockRegenerationDelay - .06f) return;
                    delayWitnessed = true;
                    Check("allFourRemainDownForConfiguredDelayCycle" + regenerationCycle, OutBlocksAtTarget(true) && upper.IsOutBlockRegenerationPending &&
                        !upper.IsOutBlockRegenerating && upper.UsedOutBlockCount == 4 && upper.OutBlockRegenerationCount == regenerationCycle);
                    Enter(Phase.RegenRise); break;
                case Phase.RegenRise:
                    Park(); if (upper.IsOutBlockRegenerating && !raisingWitnessed) { raisingWitnessed = true; regenerationStartedAt = Time.time; }
                    if (upper.OutBlockRegenerationCount == regenerationCycle) return;
                    regenerationEndedAt = Time.time;
                    Check("physicalGuardRegenerationPreservesSameDrawCycle" + regenerationCycle, delayWitnessed && raisingWitnessed &&
                        regenerationStartedAt - lowerAt >= upper.outBlockRegenerationDelay - .04f && upper.OutBlockRegenerationCount == regenerationCycle + 1 &&
                        SameBall() && upper.BumperWin == expectedWin && OutBlocksAtTarget(false) && upper.UsedOutBlockCount == 0 &&
                        !upper.OutBlockUsedStates.Any(b => b) && !upper.IsOutBlockRegenerationPending && !upper.IsOutBlockRegenerating &&
                        upper.LastOutBlockRegenerationWin == expectedWin && upper.LastOutBlockRegenerationTicket == ticket &&
                        upper.LastOutBlockRegenerationBallId == ballId && upper.LastOutBlockRegenerationColorGatesOpen && upper.ColorGatesOpen);
                    RecordGuards("all-four-restored"); regenerationCycle++;
                    if (regenerationCycle == 1) { IncomingGuard(0); Enter(Phase.CooldownFlight); } else Enter(Phase.CooldownWait);
                    break;
                case Phase.CooldownFlight:
                    if (Count(guardContacts, upper.outBlocks[0]) <= guardContactBefore) return;
                    Check("nativeContactDuringRegenerationCooldownCannotConsume", SameBall() && Time.time - regenerationEndedAt < upper.outBlockRegenerationCooldown &&
                        upper.UsedOutBlockCount == 0 && !upper.OutBlockReady && upper.OutBlockRegenerationCount == 1);
                    RecordGuards("cooldown-contact"); Park(); Enter(Phase.CooldownWait); break;
                case Phase.CooldownWait:
                    Park(); if (!upper.OutBlockReady || Time.time - regenerationEndedAt < upper.outBlockRegenerationCooldown) return;
                    Check("guardsRearmOnlyAfterCooldownCycle" + regenerationCycle, OutBlocksAtTarget(false) && upper.UsedOutBlockCount == 0 && SameBall());
                    if (regenerationCycle == 1) { guardIndex = 0; Enter(Phase.GuardStart); }
                    else { IncomingGuard(0); Enter(Phase.PartialGuardFlight); }
                    break;
                case Phase.PartialGuardFlight:
                    if (!upper.IsOutBlockUsed(0)) return;
                    Check("nativePartialGuardAfterTwoRegenerationsIsRetainedForColorVisits", SameBall() && upper.OutBlockRegenerationCount == 2 &&
                        upper.UsedOutBlockCount == 1 && !upper.IsOutBlockRegenerationPending && Count(guardContacts, upper.outBlocks[0]) > guardContactBefore);
                    Park(); Enter(Phase.VisitStart); break;
                case Phase.VisitStart: BeginVisit(); Enter(Phase.VisitPort); break;
                case Phase.VisitPort:
                    Require(SameBall(), "Open native port lost the original upper ball.");
                    if (!upper.IsUpperSuspended || lower.ActiveBall == null) return;
                    lowerBall = lower.ActiveBall; AttachProbe(lowerBall); ParkLower(); expectedPrize = visitIndex % 2 == 1 ? controller.jackpotPools[(int)lower.kind] : resultPocket.smallReward;
                    Check("nativeWidePortRoutesSameBallAtOffset_visit" + visitIndex, Count(portContacts, targetPort) > portContactBefore && upper.LastColorPocket == targetPort &&
                        controller.ActiveKind == lower.kind && upper.IsUpperSuspended && upper.BumperWin == expectedWin && upper.OutBlockUsedStates.SequenceEqual(visitGuards) && Silver(lowerBall.gameObject));
                    Enter(Phase.VisitPause); break;
                case Phase.VisitPause:
                    ParkLower(); if (Time.time - stageTime < .20f) return;
                    Check("nativeColorVisitSuspendsSameWinAndGuardState_visit" + visitIndex, SameBall() && upper.IsUpperSuspended && upper.BumperWin == expectedWin &&
                        upper.OutBlockUsedStates.SequenceEqual(visitGuards) && upper.OutBlockRegenerationCount == 2 && game.medals == walletBefore);
                    resultPocket.GetComponent<Collider>().enabled = true; resultContactBefore = Count(resultContacts, resultPocket); IncomingResult(); Enter(Phase.VisitResult); break;
                case Phase.VisitResult:
                    if (colorEvents == colorBaseline || upper.IsUpperSuspended) return;
                    Check("nativeNormalOrJpPaysAndReturnsOriginalSphere_visit" + visitIndex, Count(resultContacts, resultPocket) > resultContactBefore &&
                        colorEvents == colorBaseline + 1 && lastColorKind == lower.kind && lastColorJackpot == (visitIndex % 2 == 1) && lastColorPayout == expectedPrize &&
                        lower.LastPocket == resultPocket && !lower.LastTimedOut && game.medals == walletBefore + expectedPrize && game.PendingPayoutMedals == queueBefore + expectedPrize &&
                        SameBall() && upper.BumperWin == expectedWin && controller.LiveUpperWin == expectedWin && upper.OutBlockUsedStates.SequenceEqual(visitGuards) &&
                        upper.OutBlockRegenerationCount == 2 && upper.ColorGatesOpen && !controller.IsColorRoundActive && upperEvents == upperBaseline);
                    visits.Add(new { index = visitIndex, kind = lower.kind.ToString(), jackpot = lastColorJackpot, payout = expectedPrize,
                        offset = visitIndex % 2 == 0 ? -.16f : .16f, ticket, ballId, win = upper.BumperWin, guards = upper.OutBlockUsedStates.ToArray(), regenerationCount = upper.OutBlockRegenerationCount });
                    RestorePockets(); Park(); visitIndex++; if (visitIndex < 6) Enter(Phase.VisitStart);
                    else { walletBefore = game.medals; queueBefore = game.PendingPayoutMedals; RouteOut(); Enter(Phase.OutWait); }
                    break;
                case Phase.OutWait:
                    if (upperEvents == upperBaseline) return;
                    Check("nativeOutEndsOnceAndPaysOnlyTwoNewWin", outContacts == outContactBefore + 1 && upperEvents == upperBaseline + 1 && lastUpperPayout == 2 &&
                        controller.LastUpperWin == 102 && controller.CarriedUpperWin == 102 && game.medals == walletBefore + 2 && game.PendingPayoutMedals == queueBefore + 2 && !controller.IsUpperDrawing);
                    Enter(Phase.OutHold); break;
                case Phase.OutHold:
                    if (Time.time - stageTime < .25f) return;
                    Check("completedOutCannotRegenerateOrPayAgain", upperEvents == upperBaseline + 1 && game.medals == walletBefore + 2 &&
                        !upper.IsOutBlockRegenerationPending && !upper.IsOutBlockRegenerating && !controller.IsColorRoundActive);
                    controller.QueueBallDraw(MedalJackpotKind.Ruby); Enter(Phase.NewStart); break;
                case Phase.NewStart:
                    if (!controller.IsUpperDrawing || upper.ActiveBall == null) return;
                    CaptureBall(); expectedWin = 102; Enter(Phase.NewWait); break;
                case Phase.NewWait:
                    Park(); if (!OutBlocksAtTarget(false) || !upper.OutBlockReady) return;
                    Check("newNativeQueuedDrawResetsGuardCounterAndRearmsAllFour", ticket != firstTicket && ballId != firstBallId && SameBall() &&
                        upper.OutBlockRegenerationCount == 0 && upper.UsedOutBlockCount == 0 && !upper.OutBlockUsedStates.Any(b => b) &&
                        !upper.IsOutBlockRegenerationPending && !upper.IsOutBlockRegenerating && upper.BumperWin == 102 && upper.TotalBumperHits == 0 && Silver(upperBall.gameObject));
                    walletBefore = game.medals; queueBefore = game.PendingPayoutMedals; upperBaseline = upperEvents;
                    IncomingGuard(0); Enter(Phase.NewGuardFlight); break;
                case Phase.NewGuardFlight:
                    if (!upper.IsOutBlockUsed(0)) return;
                    Check("nextBallUsesNativeGuardBeforeItsPhysicalOut", SameBall() && upper.BumperWin == expectedWin && upper.UsedOutBlockCount == 1 &&
                        upper.OutBlockRegenerationCount == 0 && Count(guardContacts, upper.outBlocks[0]) > guardContactBefore);
                    Park(); Enter(Phase.NewGuardWithdraw); break;
                case Phase.NewGuardWithdraw:
                    Park(); if (Vector3.Distance(upper.outBlockBodies[0].position, upper.outBlockBodies[0].transform.parent.TransformPoint(
                        upper.outBlockClosedPositions[0] + Vector3.up * upper.outBlockRaiseHeight)) >= .025f) return;
                    RouteOut(); Enter(Phase.NewOut); break;
                case Phase.NewOut:
                    if (upperEvents == upperBaseline) return;
                    Check("secondNativeOutDoesNotPayAlreadyCarriedWinAgain", outContacts == outContactBefore + 1 && upperEvents == upperBaseline + 1 && lastUpperPayout == 0 &&
                        game.medals == walletBefore && game.PendingPayoutMedals == queueBefore && !controller.IsUpperDrawing);
                    Enter(Phase.EndHold); break;
                case Phase.EndHold:
                    if (Time.time - stageTime < .25f) return;
                    Check("allSixColorVisitsAndTwoNativeOutsFinished", colorEvents == 6 && upperEvents == 2 && outContacts == 2 && controller.stations.All(s => !s.IsDrawing && !s.IsRotating));
                    ui.Refresh(); Check("lotteryGuidanceRemainsHiddenAfterActualOut", !settings.showLotteryStatus && !ui.statusText.gameObject.activeSelf && !ui.lotteryStatusPanel.activeSelf);
                    phase = Phase.Complete; Finish(true); break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }
    private static void CaptureBall()
    { upperBall = upper.ActiveBall; ticket = upper.CurrentTicket; ballId = upperBall.GetEntityId().ToString(); AttachProbe(upperBall); Park(); upperBaseline = upperEvents; }
    private static void AttachProbe(LotteryBallToken token) { if (token.GetComponent<MedalUpperTuningContactProbe>() == null) token.gameObject.AddComponent<MedalUpperTuningContactProbe>(); }
    private static void Place(Rigidbody body, Vector3 position)
    { body.isKinematic = false; body.useGravity = true; body.detectCollisions = true; body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.position = position; placedFixed = Time.fixedTime; Physics.SyncTransforms(); }
    private static void Park() { if (upperBall != null && !upper.IsUpperSuspended && !upperBall.IsConsumed) Place(upperBall.GetComponent<Rigidbody>(), upper.transform.TransformPoint(upper.guideLocalCenter + Vector3.up * 10)); }
    private static void ParkLower() { if (lowerBall != null && !lowerBall.IsConsumed) Place(lowerBall.GetComponent<Rigidbody>(), lower.transform.TransformPoint(lower.guideLocalCenter + Vector3.up * 10)); }
    private static void IncomingPort(MedalColorRoutePocket port, float offset)
    {
        Place(upperBall.GetComponent<Rigidbody>(), port.transform.TransformPoint(new Vector3(offset, .52f, -.65f)));
        upperBall.GetComponent<Rigidbody>().linearVelocity = port.transform.forward * 3.5f;
        boundaries.Add(new { phase = "physical-port-approach", kind = port.kind.ToString(), win = upper.BumperWin, offset,
            localStart = Pos(new Vector3(offset, .52f, -.65f)), nativeOnly = true, ticket, ballId });
    }
    private static void IncomingBumper()
    {
        var c = upper.bumpers[0].GetComponent<Collider>(); Vector3 normal = c.bounds.center - upper.transform.TransformPoint(upper.guideLocalCenter); normal.y = 0; normal.Normalize();
        Vector3 face = c.ClosestPoint(c.bounds.center + normal * 3);
        Place(upperBall.GetComponent<Rigidbody>(), face + normal * (.26f + .025f)); upperBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
    }
    private static void IncomingGuard(int index)
    {
        var c = upper.outBlockBodies[index].GetComponent<Collider>(); Vector3 normal = c.transform.forward.normalized;
        Vector3 face = c.ClosestPoint(c.bounds.center + normal * 3);
        Place(upperBall.GetComponent<Rigidbody>(), face + normal * (.26f + .025f)); upperBall.GetComponent<Rigidbody>().linearVelocity = -normal * 3.5f;
        guardContactBefore = Count(guardContacts, upper.outBlocks[index]);
    }
    private static void NextGuard() { guardIndex++; Enter(guardIndex < 4 ? Phase.GuardStart : Phase.AllLower); }
    private static void RecordGuards(string step) => guardSamples.Add(new { step, cycle = regenerationCycle, guardIndex, fixedTime = Time.fixedTime, time = Time.time,
        ticket, ballId, win = upper.BumperWin, used = upper.OutBlockUsedStates.ToArray(), upper.UsedOutBlockCount, upper.OutBlockRegenerationCount,
        upper.IsOutBlockRegenerationPending, upper.IsOutBlockRegenerating, upper.OutBlockReady, positions = upper.outBlockBodies.Select(b => Pos(b.position)).ToArray(), lowerAt, regenerationStartedAt });
    private static bool OutBlocksAtTarget(bool withdrawn) => Enumerable.Range(0, 4).All(i => Vector3.Distance(upper.outBlockBodies[i].position,
        upper.outBlockBodies[i].transform.parent.TransformPoint(upper.outBlockClosedPositions[i] + Vector3.up * (withdrawn ? upper.outBlockRaiseHeight : 0))) < .025f);
    private static bool GatesAtTarget(bool open) => Enumerable.Range(0, 3).All(i => Vector3.Distance(upper.colorGateBodies[i].position,
        upper.colorGateBodies[i].transform.parent.TransformPoint(upper.colorGateClosedPositions[i] + Vector3.up * (open ? upper.colorGateRaiseHeight : 0))) < .025f);
    private static Quaternion[] Angles() => controller.stations.Select(s => s.dividerRotor.rotation).ToArray();
    private static bool AnglesEqual(Quaternion[] previous) => Enumerable.Range(0, 3).All(i => Quaternion.Angle(previous[i], controller.stations[i].dividerRotor.rotation) < .05f);
    private static bool AnglesAllMoved(Quaternion[] previous) => Enumerable.Range(0, 3).All(i => Quaternion.Angle(previous[i], controller.stations[i].dividerRotor.rotation) > 1f);
    private static void BeginVisit()
    {
        Park(); RestorePockets(); lower = controller.stations.Single(s => (int)s.kind == visitIndex / 2);
        resultPocket = lower.GetComponentsInChildren<MedalLotteryPocket>().First(p => p.jackpot == (visitIndex % 2 == 1));
        foreach (var p in lower.GetComponentsInChildren<MedalLotteryPocket>()) { var c = p.GetComponent<Collider>(); pocketStates[c] = c.enabled; c.enabled = false; }
        targetPort = upper.colorRoutePockets.Single(p => p.kind == lower.kind); portContactBefore = Count(portContacts, targetPort);
        lowerBall = null; walletBefore = game.medals; queueBefore = game.PendingPayoutMedals; colorBaseline = colorEvents; visitGuards = upper.OutBlockUsedStates.ToArray();
        IncomingPort(targetPort, visitIndex % 2 == 0 ? -.16f : .16f);
    }
    private static void IncomingResult()
    {
        var c = resultPocket.GetComponent<Collider>(); float radius = .26f;
        Place(lowerBall.GetComponent<Rigidbody>(), new Vector3(c.bounds.center.x, c.bounds.max.y + radius + .025f, c.bounds.center.z));
        lowerBall.GetComponent<Rigidbody>().linearVelocity = Vector3.down * 1.5f;
    }
    private static void RouteOut()
    {
        Require(upper.IsOutBlockUsed(0), "The physical OUT approach requires the previously consumed outer guard lane.");
        var c = upper.upperOutflow.GetComponent<Collider>(); Vector3 inward = upper.transform.forward.normalized;
        Vector3 query = c.bounds.center + inward * 4; query.x = upper.outBlockBodies[0].position.x;
        Vector3 face = c.ClosestPoint(query); var sphere = upperBall.GetComponent<SphereCollider>();
        float radius = sphere.radius * Mathf.Max(sphere.transform.lossyScale.x, sphere.transform.lossyScale.y, sphere.transform.lossyScale.z);
        Vector3 incoming = face + inward * (radius + .05f);
        Place(upperBall.GetComponent<Rigidbody>(), incoming); upperBall.GetComponent<Rigidbody>().linearVelocity = -inward * 3.5f;
        outContactBefore = outContacts;
        Vector3 local = upper.transform.InverseTransformPoint(incoming), radial = local - upper.guideLocalCenter; radial.y = 0;
        float outsideRadius = upper.bumperBowlOuterRadius + radius / Mathf.Min(upper.transform.lossyScale.x, upper.transform.lossyScale.z) + .04f;
        Check("incomingOutStartsInsideWholeBallTerminationRadius_ticket" + ticket, radial.magnitude < outsideRadius &&
            local.y + radius / Mathf.Min(upper.transform.lossyScale.x, upper.transform.lossyScale.z) > upper.bumperBowlFloorY);
        boundaries.Add(new { phase = "actual-out-trigger-approach", ticket, ballId, body = Pos(incoming), collider = c.name,
            triggerInnerFace = Pos(face), incomingLocalPosition = Pos(local), incomingRadius = radial.magnitude, outsideRadius,
            consumedGuardLane = 0, guardPositions = upper.outBlockBodies.Select(b => Pos(b.position)).ToArray(), win = upper.BumperWin });
    }
    private static void UpperFinished(int payout) { upperEvents++; lastUpperPayout = payout; }
    private static void ColorFinished(MedalJackpotKind kind, int payout, bool jackpot)
    { colorEvents++; lastColorKind = kind; lastColorPayout = payout; lastColorJackpot = jackpot; Park(); }
    internal static void ObserveCollision(Collision collision, LotteryBallToken token)
    {
        if (!SessionState.GetBool(Key, false)) return;
        var guard = collision.collider.GetComponent<MedalOutBlock>(); var bumper = collision.collider.GetComponent<MedalLotteryBumper>();
        var gate = collision.collider.attachedRigidbody; bool isGate = upper != null && upper.colorGateBodies.Contains(gate);
        if (guard != null) guardContacts[guard] = Count(guardContacts, guard) + 1;
        if (isGate) gateContacts[gate] = Count(gateContacts, gate) + 1;
        if (guard != null || bumper != null || isGate) contacts.Add(new { type = guard != null ? "guard" : isGate ? "closed-gate" : "bumper",
            name = collision.collider.name, entity = token == null ? null : token.GetEntityId().ToString(), fixedTime = Time.fixedTime, time = Time.time,
            point = collision.contactCount == 0 ? null : Pos(collision.GetContact(0).point) });
    }
    internal static void ObserveTrigger(Collider collider, LotteryBallToken token)
    {
        if (!SessionState.GetBool(Key, false)) return;
        var port = collider.GetComponent<MedalColorRoutePocket>(); var result = collider.GetComponent<MedalLotteryPocket>(); var exit = collider.GetComponent<MedalLotteryOutflow>();
        if (port != null) portContacts[port] = Count(portContacts, port) + 1;
        if (result != null) resultContacts[result] = Count(resultContacts, result) + 1;
        if (exit != null && exit == upper.upperOutflow) outContacts++;
        if (port != null || result != null || exit != null) contacts.Add(new { type = port != null ? "color-port" : result != null ? "lower-prize" : "out",
            name = collider.name, entity = token == null ? null : token.GetEntityId().ToString(), fixedTime = Time.fixedTime, time = Time.time,
            actualBodyPosition = token == null ? null : Pos(token.GetComponent<Rigidbody>().position) });
    }
    private static bool Green(GameObject obj) => obj != null && obj.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") &&
        r.sharedMaterial.GetColor("_BaseColor").g > .6f && r.sharedMaterial.GetColor("_BaseColor").g > r.sharedMaterial.GetColor("_BaseColor").r * 1.4f);
    private static bool Silver(GameObject obj) => obj != null && obj.GetComponentsInChildren<Renderer>().Any(r => {
        var m = r.sharedMaterial; if (m == null || !m.HasProperty("_BaseColor") || !m.HasProperty("_Metallic") || !m.HasProperty("_Smoothness")) return false;
        Color c = m.GetColor("_BaseColor"); return AssetDatabase.GetAssetPath(m) == MedalLotteryStationBuilder.SteelBallMaterialPath &&
            Mathf.Min(c.r, c.g, c.b) > .6f && Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b) < .15f &&
            m.GetFloat("_Metallic") >= .85f && Mathf.Abs(m.GetFloat("_Smoothness") - .85f) < .01f;
    });
    private static void RestorePockets() { foreach (var p in pocketStates) if (p.Key != null) p.Key.enabled = p.Value; pocketStates.Clear(); }
    private static void ClearQueues()
    {
        foreach (string name in new[] { "ballDraws", "ballDrawStarts", "ballRefunds" }) { object q = Field(controller, name).GetValue(controller); q.GetType().GetMethod("Clear").Invoke(q, null); }
        Set(controller, "deferredDirectJpcDraws", 0L);
        foreach (string name in new[] { "payoutQueue", "jackpotPayoutQueue" }) { object q = Field(game, name).GetValue(game); q.GetType().GetMethod("Clear").Invoke(q, null); }
        Set(game, "lastPayoutBatch", null); Set(game, "lastJackpotBatch", null); Set(game, "<PendingPayoutMedals>k__BackingField", 0L);
    }
    private static string Hash(string path) { if (!File.Exists(path)) return "missing"; using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (SessionState.GetBool(Key, false) && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) &&
            !(stack ?? "").Contains("Unity.AI.ModelSelector") && !(stack ?? "").Contains("UnityEditor.Search.SearchDatabase")) errors.Add(message);
    }
    private static void Finish(bool stop)
    {
        if (!SessionState.GetBool(Key, false)) return; SessionState.SetBool(Key, false); RestorePockets();
        if (settingsUI != null) settingsUI.Close();
        if (controller != null) { controller.OnUpperLotteryFinished -= UpperFinished; controller.OnLotteryFinished -= ColorFinished; }
        foreach (var pair in enabledStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (settings != null) { settings.SettingsFileOverride = originalOverride; settings.persistSettings = originalPersistence; }
        string afterHash = realSettingsPath == null ? "not-initialized" : Hash(realSettingsPath); Check("realUserSettingsFileRemainedUntouched", realSettingsHash != null && realSettingsHash == afterHash);
        if (initialized) { UnityEngine.Random.state = originalRandom; Time.timeScale = originalTimeScale; }
        Application.runInBackground = SessionState.GetBool(Key + ".Background", false);
        Write("upper-tuning-play-validation.json", new { timestampUtc = DateTime.UtcNow.ToString("O"), startedUtc = SessionState.GetString(Key + ".Utc", ""),
            success = phase == Phase.Complete && errors.Count == 0, completed = phase == Phase.Complete, phase = phase.ToString(), elapsedWallSeconds = Elapsed,
            maximumWallSeconds = MaximumSeconds, cases = checks.Count, checks, errors, boundaries, contacts, closedSamples, guardSamples, visits,
            upperEvents, colorEvents, outContacts, settingsHashes = new { before = realSettingsHash, after = afterHash }, temporarySettingsPath = temporaryPath,
            fixtureNotes = new[] { "Initial high probability and already-paid 100WIN carry are explicit setup values; no later winning result is injected.",
                "All bumper, guard, gate, route, lower prize and OUT transitions use real Rigidbody contacts; no positive resolve or finish method is called.",
                "Dynamic balls are parked above the deck only during bounded fixture waits. This is an integration test, not a natural free-play trajectory.",
                "Game payout spawning is held while exact wallet and reserved-payout increments are compared; no JP physical drain is claimed.",
                "Only a GUID temporary settings file is saved and reloaded. The real settings SHA256 is checked before and after." } });
        if (stop && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    private static void Write(string name, object report)
    {
        string root = Path.GetDirectoryName(Application.dataPath); string pointer = Path.Combine(root, ".codex-backups", "current-medal-upper-tuning.txt");
        string directory = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : Path.Combine(root, ".codex-backups", "medal-upper-tuning");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
    }
}

public sealed class MedalUpperTuningContactProbe : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision) => MedalUpperTuningPlayCheck.ObserveCollision(collision, GetComponent<LotteryBallToken>());
    private void OnTriggerEnter(Collider collider) => MedalUpperTuningPlayCheck.ObserveTrigger(collider, GetComponent<LotteryBallToken>());
}
