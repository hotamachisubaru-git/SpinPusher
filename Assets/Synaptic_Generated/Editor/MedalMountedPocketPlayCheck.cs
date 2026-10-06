using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Native medal entries, paid click landings, solid curved ball rails, and moving assembly clearance.</summary>
[InitializeOnLoad]
public static class MedalMountedPocketPlayCheck
{
    private const string Key = "MedalPusher.MountedPocketsCheck";
    private const string StartedKey = Key + ".Started";
    private const string BackgroundKey = Key + ".Background";
    private const double MaximumSeconds = 80;
    private enum Phase { Boot, PocketLowerStart, PocketLowerHold, PocketStart, PocketFlight, PocketSpin, PocketHold, ClickStart, ClickFall, BallStart, BallFlight, BallHold, Cycle, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalInputHandler input;
    private static MedalArcadeSettings settings;
    private static MedalSlotPocket[] pockets;
    private static Transform guideRoot;
    private static Transform[] guides;
    private static CapsuleCollider[][] pipeColliders;
    private static Collider[] guideSolids, movingSolids;
    private static Vector3[] initialGuidePositions, pocketPlatePositions;
    private static Quaternion[] initialGuideRotations, pocketPlateRotations;
    private static readonly Dictionary<Behaviour, bool> enabledStates = new Dictionary<Behaviour, bool>();
    private static readonly Dictionary<string, object> checks = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> boundaries = new List<object>();
    private static readonly List<object> pocketSamples = new List<object>();
    private static readonly List<object> lowerPocketSamples = new List<object>();
    private static readonly List<object> spinSamples = new List<object>();
    private static readonly List<object> reelSamples = new List<object>();
    private static readonly List<object> clickSamples = new List<object>();
    private static readonly List<object> ballSamples = new List<object>();
    private static readonly List<object> nativeContacts = new List<object>();
    private static readonly List<object> overlapSamples = new List<object>();
    private static readonly List<object> cycleSamples = new List<object>();
    private static readonly Dictionary<int, int> pocketEntryCounts = new Dictionary<int, int>();
    private static readonly Dictionary<int, Vector3> pocketEntryPositions = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, int> lowerPocketEntryCounts = new Dictionary<int, int>();
    private static GameObject fixtureObject, paidCoin;
    private static MedalMountedContactProbe fixtureProbe;
    private static CapsuleCollider targetPipe;
    private static Vector3 targetPoint, spawnPoint, pipeFace, innerNormal;
    private static Vector2 targetScreen;
    private static string fixtureId, settingsPath, settingsHash, originalOverride;
    private static bool initialized, originalPersistence, gravityFallSeen;
    private static bool spinStartedObserved, spinCompletedObserved, pocketFallSeen;
    private static bool lowerOverlapSeen;
    private static double phaseStarted;
    private static float stageFixed, firstGuideFixed, worldBallRadius, minSignedDistance, maximumPenetration;
    private static float initialCoinY, originalPusherSpeed, cycleStartedFixed, cyclePeriod, nextCycleSample;
    private static float minimumPlateZ, maximumPlateZ, minimumHardwareGap, maximumAssemblyPenetration;
    private static int pocketIndex, clickIndex, ballIndex, paidSpawns, paidEvents, creditBefore, initialWallet, walletBefore, nativePocketBefore;
    private static int initialCredits, overlapPairCount, cyclePhysicsSamples;
    private static int initialSpins, spinBefore, reelChangeCount;
    private static int lowerContactsBefore;
    private static int[] lastObservedReels;
    private static float spinStartedTime, spinCompletedTime, pocketSpawnY;
    private static long initialPaid, initialReturned;
    private static UnityEngine.Random.State originalRandom;
    private static float originalTimeScale;

    static MedalMountedPocketPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlay;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Mounted Pockets And Guides Play Check")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play before the mounted pocket/guide check.");
        Reset();
        foreach (string other in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck", "MedalPusher.UpperFlowCheck", "MedalPusher.IntegratedUpperFlowCheck", "MedalPusher.ClickBallOnlyCheck", "MedalPusher.SlotMotionCheck", "MedalPusher.HighWinCheck" })
            SessionState.SetBool(other, false);
        SessionState.SetFloat(StartedKey, (float)Now); SessionState.SetString(Key + ".Utc", DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(BackgroundKey, Application.runInBackground); Application.runInBackground = true;
        SessionState.SetBool(Key, true); EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Medal Pusher/Inspect Mounted Pockets And Guides Play Check")]
    public static void Inspect() => Write("mounted-pockets-play-state.json", new {
        timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false), playing = EditorApplication.isPlaying,
        phase = phase.ToString(), elapsedWallSeconds = Elapsed, pocketIndex, clickIndex, ballIndex, errors,
        nativePocketCounts = pocketEntryCounts, nativeLowerPocketCounts = lowerPocketEntryCounts, spinStartedObserved, spinCompletedObserved, reelChangeCount,
        paidSpawns, paidEvents, cyclePhysicsSamples, maximumAssemblyPenetration,
        fixture = fixtureObject == null ? null : new { name = fixtureObject.name, position = Pos(fixtureObject.transform.position),
            guideContacts = fixtureProbe == null ? 0 : fixtureProbe.GuideContacts },
        controller = controller == null ? null : new { controller.SpinCredits, controller.IsSlotSpinning, controller.TotalSpins,
            controller.PendingBallDraws, controller.PendingBallRefunds },
        plate = game == null || game.pusherBody == null ? null : Pos(game.pusherBody.position)
    });

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartedKey, (float)Now);
    private static object Pos(Vector3 vector) => new { x = vector.x, y = vector.y, z = vector.z };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, bool condition) { checks[name] = condition; if (!condition) errors.Add("Failed: " + name); }
    private static FieldInfo Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(target.GetType().Name, name);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Disable(Behaviour component)
    {
        if (component == null) return; if (!enabledStates.ContainsKey(component)) enabledStates[component] = component.enabled; component.enabled = false;
    }
    private static void Reset()
    {
        phase = Phase.Boot; initialized = false; game = null; controller = null; input = null; settings = null; fixtureObject = paidCoin = null; fixtureProbe = null;
        pocketIndex = clickIndex = ballIndex = paidSpawns = paidEvents = cyclePhysicsSamples = overlapPairCount = 0;
        guideRoot = null; settingsPath = settingsHash = originalOverride = fixtureId = null;
        enabledStates.Clear(); checks.Clear(); errors.Clear(); boundaries.Clear(); pocketSamples.Clear(); lowerPocketSamples.Clear(); spinSamples.Clear(); reelSamples.Clear(); clickSamples.Clear(); ballSamples.Clear();
        nativeContacts.Clear(); overlapSamples.Clear(); cycleSamples.Clear(); pocketEntryCounts.Clear(); pocketEntryPositions.Clear(); lowerPocketEntryCounts.Clear();
    }
    private static void OnPlay(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) IsolateSettings();
        if (state == PlayModeStateChange.ExitingPlayMode) { errors.Add("Play exited before completion."); Finish(false); }
    }
    private static void IsolateSettings()
    {
        if (settingsPath != null) return;
        settings = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettings>(); if (settings == null) return;
        originalOverride = settings.SettingsFileOverride; originalPersistence = settings.persistSettings;
        settingsPath = Path.Combine(Application.persistentDataPath, "medal-arcade-settings.json"); settingsHash = Hash(settingsPath);
        settings.SettingsFileOverride = Path.Combine(Path.GetTempPath(), "medal-mounted-pockets-" + Guid.NewGuid().ToString("N") + ".json"); settings.persistSettings = false;
    }
    private static bool Ready()
    {
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>(); controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        input = UnityEngine.Object.FindAnyObjectByType<MedalInputHandler>();
        return game != null && controller != null && input != null && Camera.main != null && game.pusherBody != null &&
            game.OnMedalInserted != null && game.OnMedalInserted.GetInvocationList().Any(d => ReferenceEquals(d.Target, controller)) && Time.frameCount >= 2;
    }
    private static void Setup()
    {
        IsolateSettings(); Require(settings != null, "Settings fixture missing.");
        originalRandom = UnityEngine.Random.state; originalTimeScale = Time.timeScale; Time.timeScale = 1;
        originalPusherSpeed = game.pusherSpeed; game.pusherSpeed = 1.25f;
        Disable(input); foreach (var manager in UnityEngine.Object.FindObjectsByType<PrizeDropManager>(FindObjectsSortMode.None)) Disable(manager);
        game.StopThrowing(); game.SetSettingsOpen(false); ClearBoard("initial-scene-items"); ClearQueues();
        controller.targetPayoutPercent = controller.slotBallChancePercent = controller.slotMedalChancePercent = 0; controller.rescueSpins = 0;
        controller.enabled = true; Set(controller, "nextSpinAt", Time.time);
        Set(controller, "<SpinCredits>k__BackingField", 0); Set(controller, "<IsSlotSpinning>k__BackingField", false);
        Set(controller, "nextLotteryAt", float.PositiveInfinity);
        initialWallet = game.medals; initialPaid = game.TotalPaidMedals; initialReturned = game.TotalReturnedMedals; initialCredits = controller.SpinCredits;
        initialSpins = controller.TotalSpins;
        game.OnPaidMedalSpawned += PaidSpawned; game.OnMedalInserted += PaidInserted;
        controller.OnStateChanged += ObserveSlotState;
        var view = UnityEngine.Object.FindAnyObjectByType<MedalPusherCameraView>(); if (view != null) { view.overview = false; view.ReleaseStation(); }
        pockets = game.GetComponentsInChildren<MedalSlotPocket>(true).OrderBy(p => game.transform.InverseTransformPoint(p.transform.position).x).ToArray();
        Require(pockets.Length == 3, "Expected three mounted slot pockets.");
        foreach (var pocket in pockets)
        {
            Require(pocket.isActiveAndEnabled && pocket.controller == controller, "Mounted pocket is not connected to its active controller.");
            var probe = pocket.gameObject.AddComponent<MedalMountedPocketProbe>(); probe.Index = Array.IndexOf(pockets, pocket);
        }
        guideRoot = game.transform.Find("GeneratedPlayfield/CurvedBallGuides"); Require(guideRoot != null, "Curved ball guides missing.");
        guides = new[] { guideRoot.Find("LeftCurvedBallGuide"), guideRoot.Find("RightCurvedBallGuide") }; Require(guides.All(g => g != null), "Two mirrored guides missing.");
        pipeColliders = guides.Select(g => g.GetComponentsInChildren<CapsuleCollider>().OrderBy(c => c.name).ToArray()).ToArray();
        guideSolids = guideRoot.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
        movingSolids = game.pusherBody.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
        initialGuidePositions = guideSolids.Select(c => c.transform.position).ToArray(); initialGuideRotations = guideSolids.Select(c => c.transform.rotation).ToArray();
        pocketPlatePositions = pockets.Select(p => game.pusherBody.transform.InverseTransformPoint(p.transform.position)).ToArray();
        pocketPlateRotations = pockets.Select(p => Quaternion.Inverse(game.pusherBody.rotation) * p.transform.rotation).ToArray();
        Check("threePocketsAreCompoundChildrenOfMovingPlate", pockets.All(p => p.transform.IsChildOf(game.pusherBody.transform) &&
            p.GetComponent<SphereCollider>() != null && p.GetComponent<SphereCollider>().attachedRigidbody == game.pusherBody));
        Check("pocketWorldScalesRemainOneUnderScaledPlate", pockets.All(p => Vector3.Distance(p.transform.lossyScale, Vector3.one) < .002f));
        var plateCollider = game.pusherBody.GetComponent<Collider>();
        Require(plateCollider is MeshCollider && ((MeshCollider)plateCollider).sharedMesh != null, "Expected the actual perforated plate mesh collider.");
        var localPlateBounds = ((MeshCollider)plateCollider).sharedMesh.bounds;
        Vector3 frontTop = plateCollider.transform.TransformPoint(new Vector3(localPlateBounds.center.x, localPlateBounds.max.y, localPlateBounds.min.z));
        Vector3 expectedPlane = frontTop + game.transform.up * MedalSlotPocketLayout.PocketHeightAbovePlate + game.transform.forward * MedalSlotPocketLayout.PocketFrontInset;
        var mouthGeometry = pockets.Select(p => new {
            name = p.name, upDot = Vector3.Dot(p.transform.up, game.transform.up), forwardDot = Vector3.Dot(p.transform.up, game.transform.forward),
            expectedPlane = Pos(expectedPlane), actualPlane = Pos(p.transform.position),
            heightError = Vector3.Dot(p.transform.position - expectedPlane, game.transform.up),
            insetError = Vector3.Dot(p.transform.position - expectedPlane, game.transform.forward)
        }).ToArray();
        Check("threeTiltedOpeningsSitAboveAndInsidePusherUpperSurface", pockets.All(p =>
            Mathf.Abs(Vector3.Dot(p.transform.up, game.transform.up) - Mathf.Cos(MedalSlotPocketLayout.PocketTiltDegrees * Mathf.Deg2Rad)) < .005f &&
            Vector3.Dot(p.transform.up, game.transform.forward) < -.45f &&
            Mathf.Abs(Vector3.Dot(p.transform.position - expectedPlane, game.transform.up)) < .005f &&
            Mathf.Abs(Vector3.Dot(p.transform.position - expectedPlane, game.transform.forward)) < .005f));
        Check("nativeDetectorsAreVerticallyAlignedBelowRealPlateHoles", pockets.All(p => {
            Vector3 detector = p.transform.TransformPoint(p.GetComponent<SphereCollider>().center);
            return Mathf.Abs(detector.x - p.transform.position.x) < .005f && Mathf.Abs(detector.z - p.transform.position.z) < .005f &&
                Mathf.Abs(detector.y - (frontTop.y - .32f)) < .005f && p.requireEntryFromAbove;
        }));
        boundaries.Add(new { phase = "plate-local-geometry", plateLocalBounds = new { center = Pos(localPlateBounds.center), size = Pos(localPlateBounds.size) },
            plateTransformPosition = Pos(plateCollider.transform.position), physicsBodyPosition = Pos(game.pusherBody.position), colliderWorldBoundsCentre = Pos(plateCollider.bounds.center), mouthGeometry });
        Check("isolatedSlotStartsInNormalProbability", !controller.IsHighProbability);
        Check("bothGuidesHaveTwentyFourContinuousSolidCapsules", pipeColliders.All(side => side.Length == 24 && side.All(c => c.enabled && !c.isTrigger && c.attachedRigidbody == null)));
        Check("guidesUseWallMountsAndNoFloorPosts", guides.All(g => g.Find("WallMount_Front") != null && g.Find("WallMount_Rear") != null) &&
            !guideRoot.GetComponentsInChildren<Transform>().Any(t => t.name.StartsWith("GuideSupport", StringComparison.Ordinal) || t.name == "DeckFoot" || t.name == "SupportPost"));
        bool mirror = pipeColliders.All(c => c.Length == 24);
        if (mirror)
            for (int i = 0; i < 24; i++)
            {
                Vector3 left = game.transform.InverseTransformPoint(pipeColliders[0][i].bounds.center), right = game.transform.InverseTransformPoint(pipeColliders[1][i].bounds.center);
                mirror &= Mathf.Abs(left.x + right.x) < .002f && Mathf.Abs(left.y - right.y) < .002f && Mathf.Abs(left.z - right.z) < .002f;
            }
        Check("leftAndRightCurvedPipesAreMirrored", mirror);
        boundaries.Add(new { phase = "isolated-fixture", wallet = initialWallet, paid = initialPaid, returned = initialReturned, credits = initialCredits,
            boardItems = game.CountBoardItems(false) + game.CountBoardItems(true), payout = game.PendingPayoutMedals, settingsSavePath = settings.SavePath,
            movingSolidCount = movingSolids.Length, guideSolidCount = guideSolids.Length, pusherSpeedFixture = game.pusherSpeed });
        initialized = true; Enter(Phase.PocketLowerStart);
    }
    private static void Enter(Phase next) { phase = next; phaseStarted = Now; stageFixed = Time.fixedTime; }
    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        try
        {
            Require(Elapsed < MaximumSeconds, "Mounted pocket/guide test exceeded 80 seconds.");
            if (!EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling) return;
            if (!initialized) { if (!Ready()) { Require(Elapsed < 20, "Runtime Start readiness timeout."); return; } Setup(); return; }
            Require(Now - phaseStarted < 15, "Fixture phase timed out: " + phase);
            switch (phase)
            {
                case Phase.PocketLowerStart: StartLowerPocket(); Enter(Phase.PocketLowerHold); break;
                case Phase.PocketLowerHold:
                    Require(fixtureObject != null, "Lower-deck negative medal was incorrectly consumed.");
                    var lowerBody = fixtureObject.GetComponent<Rigidbody>();
                    var lowerCollider = fixtureObject.GetComponent<Collider>();
                    var lowerDetector = pockets[pocketIndex].GetComponent<SphereCollider>();
                    lowerOverlapSeen |= Physics.ComputePenetration(lowerCollider, lowerBody.position, lowerBody.rotation,
                        lowerDetector, lowerDetector.transform.position, lowerDetector.transform.rotation, out Vector3 lowerDirection, out float lowerDistance);
                    if (Time.fixedTime - stageFixed < .25f) return;
                    int lowerNative = lowerPocketEntryCounts.TryGetValue(pocketIndex, out int observedLower) ? observedLower - lowerContactsBefore : 0;
                    Check("lowerDeckMedalActuallyTouchesOrOverlapsUpperDetector_" + pocketIndex, lowerNative > 0 || lowerOverlapSeen);
                    Check("lowerDeckMedalCannotBeSweptIntoUpperSlot_" + pocketIndex,
                        !fixtureObject.GetComponent<MedalItem>().collected && game.CountBoardItems(false) == 1 &&
                        controller.TotalSpins == spinBefore && controller.SpinCredits == creditBefore && !controller.IsSlotSpinning &&
                        game.medals == initialWallet && game.TotalPaidMedals == initialPaid && game.TotalReturnedMedals == initialReturned && game.PendingPayoutMedals == 0);
                    lowerPocketSamples.Add(new { index = pocketIndex, fixtureId, nativeTriggerContacts = lowerNative, lowerOverlapSeen,
                        spawn = Pos(spawnPoint), finalBodyPosition = Pos(lowerBody.position), detectorCenter = Pos(lowerDetector.transform.TransformPoint(lowerDetector.center)),
                        controller.TotalSpins, controller.SpinCredits, stillRegistered = game.CountBoardItems(false) == 1,
                        itemCollected = fixtureObject.GetComponent<MedalItem>().collected });
                    RemoveFixture(fixtureObject); fixtureObject = null; Enter(Phase.PocketStart); break;
                case Phase.PocketStart: StartPocket(); Enter(Phase.PocketFlight); break;
                case Phase.PocketFlight:
                    if (fixtureObject != null && fixtureObject.GetComponent<Rigidbody>().position.y < pocketSpawnY - .08f) pocketFallSeen = true;
                    if (!pocketEntryCounts.TryGetValue(pocketIndex, out int entries) || entries == nativePocketBefore) return;
                    Check("nativeUpperPocketConsumesExactlyOneMedalAndCreatesOneSpin_" + pocketIndex,
                        controller.SpinCredits - creditBefore + controller.TotalSpins - spinBefore + (controller.IsSlotSpinning ? 1 : 0) == 1 &&
                        entries == nativePocketBefore + 1 &&
                        game.medals == initialWallet && game.TotalPaidMedals == initialPaid && game.TotalReturnedMedals == initialReturned &&
                        game.PendingPayoutMedals == 0 && game.CountBoardItems(false) == 0);
                    Check("realUpperPocketMedalFallsFromAboveThroughOpening_" + pocketIndex, pocketFallSeen);
                    Check("nativeCoinCrossesPlateTopBeforeItsUpperPocketEntry_" + pocketIndex,
                        pocketEntryPositions.TryGetValue(pocketIndex, out Vector3 entryPosition) &&
                        entryPosition.y < game.pusherBody.GetComponent<Collider>().bounds.max.y - .01f);
                    pocketSamples.Add(new { index = pocketIndex, fixtureId, creditsBefore = creditBefore, creditsAfter = controller.SpinCredits,
                        nativeEntries = pocketEntryCounts.TryGetValue(pocketIndex, out int native) ? native : 0,
                        pocketPosition = Pos(pockets[pocketIndex].transform.position), spawnPosition = Pos(spawnPoint), normal = Pos(pockets[pocketIndex].transform.up),
                        entryBodyPosition = pocketEntryPositions.TryGetValue(pocketIndex, out Vector3 nativePosition) ? Pos(nativePosition) : null,
                        triggerCenter = Pos(pockets[pocketIndex].transform.TransformPoint(pockets[pocketIndex].GetComponent<SphereCollider>().center)),
                        delayFixedSeconds = Time.fixedTime - stageFixed });
                    fixtureObject = null; Enter(Phase.PocketSpin); break;
                case Phase.PocketSpin:
                    if (controller.TotalSpins == spinBefore) return;
                    Check("nativeUpperPocketStartsAndCompletesOneRealSpin_" + pocketIndex,
                        spinStartedObserved && spinCompletedObserved && controller.TotalSpins == spinBefore + 1 &&
                        !controller.IsSlotSpinning && controller.SpinCredits == creditBefore && reelChangeCount >= 2);
                    Check("zeroPayoutNativeSpinCannotAwardOrChangeProbability_" + pocketIndex,
                        !controller.LastSlotWasWin && !controller.LastSlotWasDirectJpc && controller.LastSlotPayout == 0 &&
                        !controller.IsHighProbability && game.medals == initialWallet && game.PendingPayoutMedals == 0 &&
                        controller.PendingBallDraws == 0 && controller.PendingBallRefunds == 0);
                    spinSamples.Add(new { index = pocketIndex, fixtureId, spinStartedObserved, spinCompletedObserved, reelChangeCount,
                        spinsBefore = spinBefore, spinsAfter = controller.TotalSpins, configuredDuration = controller.slotSpinDuration,
                        spinStartedTime, spinCompletedTime, actualDuration = spinCompletedTime - spinStartedTime,
                        finalSymbols = controller.LastSlotSymbols.ToArray(), controller.LastSlotResult, controller.LastSlotPayout,
                        controller.SpinCredits, controller.IsHighProbability });
                    Enter(Phase.PocketHold); break;
                case Phase.PocketHold:
                    if (Time.fixedTime - stageFixed < .15f) return;
                    Check("nativePocketCannotSpinConsumedCoinAgain_" + pocketIndex, controller.SpinCredits == creditBefore &&
                        controller.TotalSpins == spinBefore + 1 && pocketEntryCounts[pocketIndex] == nativePocketBefore + 1 &&
                        !controller.IsSlotSpinning && game.PendingPayoutMedals == 0);
                    pocketIndex++; Enter(pocketIndex < 3 ? Phase.PocketLowerStart : Phase.ClickStart); break;
                case Phase.ClickStart: StartClick(); Enter(Phase.ClickFall); break;
                case Phase.ClickFall:
                    Require(paidCoin != null && fixtureProbe != null, "Paid click fixture lost its actual medal before a surface contact.");
                    if (paidCoin.GetComponent<Rigidbody>().position.y < initialCoinY - .08f) gravityFallSeen = true;
                    if (!fixtureProbe.SurfaceContact) return;
                    Check("paidClickedMedalLandsAtItsActualAimedXZ_" + clickIndex, gravityFallSeen && IsPlaySurface(fixtureProbe.FirstSurface) &&
                        Mathf.Abs(fixtureProbe.FirstBodyPosition.x - game.transform.TransformPoint(targetPoint).x) < .045f &&
                        Mathf.Abs(fixtureProbe.FirstBodyPosition.z - game.transform.TransformPoint(targetPoint).z) < .045f);
                    clickSamples.Add(new { index = clickIndex, target = Pos(targetPoint), screen = new { x = targetScreen.x, y = targetScreen.y },
                        spawn = Pos(spawnPoint), contactBodyPosition = Pos(game.transform.InverseTransformPoint(fixtureProbe.FirstBodyPosition)),
                        surface = fixtureProbe.FirstSurface == null ? null : fixtureProbe.FirstSurface.name, gravityFallSeen,
                        delayFixedSeconds = Time.fixedTime - stageFixed });
                    RemoveFixture(paidCoin); paidCoin = null; fixtureObject = null; clickIndex++;
                    Enter(clickIndex < 2 ? Phase.ClickStart : Phase.BallStart); break;
                case Phase.BallStart: StartBall(); Enter(Phase.BallFlight); break;
                case Phase.BallFlight:
                    Require(fixtureObject != null && fixtureProbe != null, "Green ball vanished before its native guide contact.");
                    if (fixtureProbe.GuideContacts == 0) return;
                    Check("greenBallMakesRealSolidPipeContact_" + ballIndex, fixtureProbe.FirstGuide is CapsuleCollider &&
                        fixtureProbe.FirstGuide.transform.IsChildOf(guides[ballIndex / 2]) && !fixtureProbe.FirstGuide.isTrigger &&
                        Time.fixedTime > stageFixed && !fixtureObject.GetComponent<Rigidbody>().isKinematic);
                    firstGuideFixed = Time.fixedTime; minSignedDistance = float.PositiveInfinity; maximumPenetration = 0;
                    Enter(Phase.BallHold); break;
                case Phase.BallHold:
                    Require(fixtureObject != null && fixtureProbe != null, "Native guide-contact ball disappeared during the no-through check.");
                    SampleBall(); if (Time.fixedTime - firstGuideFixed < .40f) return;
                    Check("solidCurvedGuidePreventsSpherePassingThrough_" + ballIndex, minSignedDistance >= worldBallRadius - .075f &&
                        maximumPenetration < .075f && fixtureProbe.GuideContacts > 0 && game.medals == initialWallet - 2 &&
                        game.TotalReturnedMedals == initialReturned && controller.PendingBallDraws == 0);
                    ballSamples.Add(new { index = ballIndex, side = ballIndex < 2 ? "left" : "right", fixtureId,
                        expectedPipe = targetPipe.name, nativePipe = fixtureProbe.FirstGuide.name, initialPosition = Pos(spawnPoint),
                        innerNormal = Pos(innerNormal), pipeFace = Pos(pipeFace), sphereRadius = worldBallRadius,
                        minSignedDistance, maximumPenetration, nativeGuideContacts = fixtureProbe.GuideContacts,
                        contactNormal = Pos(fixtureProbe.FirstGuideNormal), finalPosition = Pos(game.transform.InverseTransformPoint(fixtureObject.GetComponent<Rigidbody>().position)) });
                    RemoveFixture(fixtureObject); fixtureObject = null; ballIndex++;
                    if (ballIndex < 4) Enter(Phase.BallStart); else StartCycle();
                    break;
                case Phase.Cycle:
                    SampleCycle(); if (Time.fixedTime - cycleStartedFixed < cyclePeriod + .10f) return;
                    Check("movingPlateCompletesRealFullStroke", maximumPlateZ - minimumPlateZ >= game.pusherRange * 1.95f && cyclePhysicsSamples >= 50);
                    Check("mountedPocketsKeepTheirPlateRelativeTransforms", PocketTransformsStable());
                    Check("bothCurvedGuidesRemainFixedDuringFullPusherCycle", GuideTransformsStable());
                    Check("fullMovingPlateAndMountedMouthAssemblyNeverPenetratesGuides", overlapPairCount == 0 && maximumAssemblyPenetration < .001f);
                    Check("newFixtureOnlySpendsTwoPaidClickMedalsAndNeverAwards", game.medals == initialWallet - 2 && game.TotalPaidMedals == initialPaid + 2 &&
                        game.TotalReturnedMedals == initialReturned && game.PendingPayoutMedals == 0 && controller.SpinCredits == initialCredits &&
                        controller.TotalSpins == initialSpins + 3 && !controller.IsSlotSpinning && !controller.IsHighProbability && paidSpawns == 2 && paidEvents == 2);
                    phase = Phase.Complete; Finish(true); break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }
    private static void StartLowerPocket()
    {
        Require(!controller.IsSlotSpinning && controller.SpinCredits == initialCredits, "Previous native spin was not completed before its negative case.");
        CheckPocketAiming();
        Vector3 detector = pockets[pocketIndex].transform.TransformPoint(pockets[pocketIndex].GetComponent<SphereCollider>().center);
        Vector3 local = game.transform.InverseTransformPoint(detector); local.y = .05f; spawnPoint = game.transform.TransformPoint(local);
        fixtureObject = UnityEngine.Object.Instantiate(game.medalPrefab, spawnPoint, Quaternion.FromToRotation(Vector3.up, game.transform.up), game.itemsRoot);
        fixtureObject.name = "MountedPocketLowerDeckMedal_" + pocketIndex; SetupItem(fixtureObject, false);
        fixtureId = fixtureObject.GetComponent<MedalItem>().GetEntityId().ToString();
        Rigidbody body = fixtureObject.GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true;
        body.linearVelocity = game.pusherBody.linearVelocity; body.angularVelocity = Vector3.zero;
        spinBefore = controller.TotalSpins; creditBefore = controller.SpinCredits; lowerOverlapSeen = false;
        lowerContactsBefore = lowerPocketEntryCounts.TryGetValue(pocketIndex, out int count) ? count : 0;
        Physics.SyncTransforms();
        boundaries.Add(new { phase = "lower-deck-negative-medal", index = pocketIndex, fixtureId, spawn = Pos(spawnPoint),
            actualDetector = Pos(detector), mintedFixtureMedal = true, expectedAcceptance = false, paid = game.TotalPaidMedals, wallet = game.medals });
    }
    private static void CheckPocketAiming()
    {
        var pocket = pockets[pocketIndex]; Camera camera = Camera.main; int spins = controller.TotalSpins, credits = controller.SpinCredits;
        Vector3 centre = pocket.transform.position; Vector3 screen = camera.WorldToScreenPoint(centre);
        bool aimed = screen.z > 0 && camera.pixelRect.Contains(new Vector2(screen.x, screen.y)) && input.TryAimAtScreenPosition(new Vector2(screen.x, screen.y));
        Vector3 target = game.transform.TransformPoint(game.CurrentMedalDropLocalPosition);
        Check("productionRayCanAimAtActualUpperOpening_" + pocketIndex,
            aimed && Mathf.Abs(target.x - centre.x) < .01f && Mathf.Abs(target.z - centre.z) < .01f);
        Vector3 collar = pocket.transform.TransformPoint(new Vector3(MedalSlotPocketLayout.PocketHoleRadius + .13f, -.02f, 0));
        Vector3 collarScreen = camera.WorldToScreenPoint(collar);
        bool collarAimed = input.TryAimAtScreenPosition(new Vector2(collarScreen.x, collarScreen.y));
        Check("productionRayRejectsSolidUpperCollar_" + pocketIndex, collarScreen.z > 0 && !collarAimed);
        Check("aimingAtUnenteredOpeningCannotSpendOrSpin_" + pocketIndex,
            controller.TotalSpins == spins && controller.SpinCredits == credits && !controller.IsSlotSpinning &&
            game.medals == initialWallet && game.TotalPaidMedals == initialPaid && paidSpawns == 0 && game.CountBoardItems(false) == 0);
        boundaries.Add(new { phase = "native-opening-screen-rays", index = pocketIndex, worldCentre = Pos(centre), screen = Pos(screen),
            aimed, actualTarget = Pos(target), solidCollar = Pos(collar), collarScreen = Pos(collarScreen), collarAimed,
            controller.TotalSpins, controller.SpinCredits, paid = game.TotalPaidMedals });
    }
    private static void StartPocket()
    {
        var pocket = pockets[pocketIndex]; var trigger = pocket.GetComponent<SphereCollider>(); Vector3 normal = game.transform.up.normalized;
        float radius = trigger.radius * Mathf.Max(Mathf.Abs(trigger.transform.lossyScale.x), Mathf.Abs(trigger.transform.lossyScale.y), Mathf.Abs(trigger.transform.lossyScale.z));
        Vector3 center = trigger.transform.TransformPoint(trigger.center);
        Vector3 planeCenter = pocket.transform.position;
        Ray holeRay = new Ray(planeCenter + normal * .35f, -normal);
        Check("upperPocketCentreHasARealOpenPathThroughAllPlateSolids_" + pocketIndex,
            movingSolids.All(c => !c.Raycast(holeRay, out RaycastHit ignored, 1.4f)));
        Vector3 controlLocal = game.transform.InverseTransformPoint(planeCenter); controlLocal.x = pocketIndex == 2 ? 1.5f : -1.5f;
        Ray solidRay = new Ray(game.transform.TransformPoint(controlLocal) + normal * .35f, -normal);
        Check("plateStillBlocksCoinBetweenUpperHoles_" + pocketIndex,
            game.pusherBody.GetComponent<Collider>().Raycast(solidRay, out RaycastHit controlHit, 1.1f));
        spawnPoint = new Vector3(center.x, pocket.transform.position.y, center.z) + normal * .45f;
        fixtureObject = UnityEngine.Object.Instantiate(game.medalPrefab, spawnPoint, Quaternion.FromToRotation(Vector3.up, normal), game.itemsRoot);
        fixtureObject.name = "MountedPocketNativeMedal_" + pocketIndex;
        SetupItem(fixtureObject, false); fixtureId = fixtureObject.GetComponent<MedalItem>().GetEntityId().ToString();
        fixtureProbe = fixtureObject.AddComponent<MedalMountedContactProbe>();
        Rigidbody body = fixtureObject.GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true;
        body.linearVelocity = game.pusherBody.linearVelocity - normal * .5f; body.angularVelocity = Vector3.zero;
        creditBefore = controller.SpinCredits; nativePocketBefore = pocketEntryCounts.TryGetValue(pocketIndex, out int entries) ? entries : 0;
        spinBefore = controller.TotalSpins; spinStartedObserved = spinCompletedObserved = pocketFallSeen = false;
        reelChangeCount = 0; lastObservedReels = controller.ReelSymbols.ToArray(); spinStartedTime = spinCompletedTime = -1;
        pocketSpawnY = body.position.y;
        Physics.SyncTransforms();
        boundaries.Add(new { phase = "native-pocket-start", index = pocketIndex, fixtureId, mintedFixtureMedal = true, paid = game.TotalPaidMedals,
            wallet = game.medals, triggerCenter = Pos(center), triggerRadius = radius, spawn = Pos(spawnPoint), normal = Pos(normal), startVelocity = Pos(body.linearVelocity) });
    }
    private static void StartClick()
    {
        FindClickTarget(clickIndex); walletBefore = game.medals; int spawnsBefore = paidSpawns, eventsBefore = paidEvents;
        long paidBefore = game.TotalPaidMedals; Set(game, "throwTimer", 0f); paidCoin = null;
        bool inserted = input.TryInsertAtScreenPosition(targetScreen);
        Require(paidCoin != null, "Production screen click did not expose its spawned medal.");
        fixtureObject = paidCoin; fixtureProbe = paidCoin.AddComponent<MedalMountedContactProbe>();
        spawnPoint = game.transform.InverseTransformPoint(paidCoin.GetComponent<Rigidbody>().position);
        var body = paidCoin.GetComponent<Rigidbody>(); var item = paidCoin.GetComponent<MedalItem>();
        Check("productionClickPaysOneAndKeepsExactClickedSpawnXZ_" + clickIndex, inserted && game.medals == walletBefore - 1 &&
            game.TotalPaidMedals == paidBefore + 1 && paidSpawns == spawnsBefore + 1 && paidEvents == eventsBefore + 1 &&
            Mathf.Abs(spawnPoint.x - targetPoint.x) < .001f && Mathf.Abs(spawnPoint.z - targetPoint.z) < .001f &&
            !body.isKinematic && body.useGravity && !item.isPrize && !item.isBall && !item.payoutAlreadyCredited);
        initialCoinY = body.position.y; gravityFallSeen = false;
    }
    private static void FindClickTarget(int sample)
    {
        Camera camera = Camera.main; Require(camera != null, "Main camera missing.");
        var plate = game.pusherBody.GetComponent<Collider>();
        foreach (float x in new[] { -1.5f, 1.5f, 0f })
            foreach (float offset in new[] { 0f, .2f, -.2f })
            {
                Vector3 local = new Vector3(x, sample == 0 ? .001f : game.transform.InverseTransformPoint(new Vector3(plate.bounds.center.x, plate.bounds.max.y, plate.bounds.center.z)).y,
                    sample == 0 ? -2.1f + offset : game.transform.InverseTransformPoint(game.pusherBody.position).z + offset);
                Vector3 screen = camera.WorldToScreenPoint(game.transform.TransformPoint(local)); Vector2 pixel = new Vector2(screen.x, screen.y);
                if (screen.z > 0 && camera.pixelRect.Contains(pixel) && input.TryAimAtScreenPosition(pixel))
                {
                    targetPoint = game.CurrentMedalDropLocalPosition; targetScreen = pixel;
                    bool valid = sample == 0 ? targetPoint.y < .10f : targetPoint.y > .40f;
                    if (valid) return;
                }
            }
        throw new InvalidOperationException("No valid native screen-ray click target for sample " + sample);
    }
    private static void StartBall()
    {
        int side = ballIndex / 2; int segment = ballIndex % 2 == 0 ? 11 : 12; targetPipe = pipeColliders[side][segment];
        Vector3 tangent = targetPipe.transform.up; tangent.y = 0; tangent.Normalize();
        innerNormal = Vector3.Cross(Vector3.up, tangent).normalized;
        Vector3 desired = game.transform.right * (side == 0 ? 1 : -1); if (Vector3.Dot(innerNormal, desired) < 0) innerNormal = -innerNormal;
        Vector3 center = targetPipe.transform.TransformPoint(targetPipe.center);
        pipeFace = targetPipe.ClosestPoint(center + innerNormal * 2f);
        GameObject prefab = controller.ballPrefabs != null && controller.ballPrefabs.Length > 0 ? controller.ballPrefabs[0] : null;
        Require(prefab != null && prefab.GetComponent<SphereCollider>() != null, "Green board-ball sphere prefab missing.");
        fixtureObject = UnityEngine.Object.Instantiate(prefab, center, Quaternion.identity, game.itemsRoot); fixtureObject.name = "CurvedGuideNativeGreenBall_" + ballIndex;
        SetupItem(fixtureObject, true); fixtureId = fixtureObject.GetComponent<MedalItem>().GetEntityId().ToString();
        var sphere = fixtureObject.GetComponent<SphereCollider>(); worldBallRadius = sphere.radius * Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x), Mathf.Abs(sphere.transform.lossyScale.y), Mathf.Abs(sphere.transform.lossyScale.z));
        spawnPoint = pipeFace + innerNormal * (worldBallRadius + .13f);
        Rigidbody body = fixtureObject.GetComponent<Rigidbody>(); body.position = spawnPoint; body.isKinematic = false; body.useGravity = true;
        body.linearVelocity = -innerNormal * (ballIndex % 2 == 0 ? 3.5f : 5.5f); body.angularVelocity = Vector3.zero;
        fixtureProbe = fixtureObject.AddComponent<MedalMountedContactProbe>(); Physics.SyncTransforms();
        Check("greenSphereCanOnlyBeBlockedByRealSolidGuide_" + ballIndex, Green(fixtureObject) && !sphere.isTrigger &&
            worldBallRadius * 2 > .50f && !targetPipe.isTrigger && targetPipe.attachedRigidbody == null);
        boundaries.Add(new { phase = "native-guide-ball-start", index = ballIndex, fixtureId, targetPipe = targetPipe.name, side,
            radius = worldBallRadius, mintedFixtureBall = true, spawn = Pos(spawnPoint), velocity = Pos(body.linearVelocity), paid = game.TotalPaidMedals, wallet = game.medals });
    }
    private static void SampleBall()
    {
        Rigidbody body = fixtureObject.GetComponent<Rigidbody>(); Collider collider = fixtureObject.GetComponent<Collider>();
        minSignedDistance = Mathf.Min(minSignedDistance, Vector3.Dot(body.position - pipeFace, innerNormal));
        foreach (var guide in pipeColliders[ballIndex / 2])
            if (Physics.ComputePenetration(collider, body.position, body.rotation, guide, guide.transform.position, guide.transform.rotation,
                out Vector3 direction, out float distance)) maximumPenetration = Mathf.Max(maximumPenetration, distance);
    }
    private static void StartCycle()
    {
        ClearBoard("before-full-stroke"); cycleStartedFixed = Time.fixedTime; cyclePeriod = 2 * Mathf.PI / game.pusherSpeed;
        minimumPlateZ = float.PositiveInfinity; maximumPlateZ = float.NegativeInfinity; minimumHardwareGap = float.PositiveInfinity; maximumAssemblyPenetration = 0;
        nextCycleSample = Time.fixedTime; Enter(Phase.Cycle);
    }
    private static void SampleCycle()
    {
        if (Time.fixedTime < nextCycleSample) return; nextCycleSample = Time.fixedTime + .019f; cyclePhysicsSamples++;
        float z = game.transform.InverseTransformPoint(game.pusherBody.position).z; minimumPlateZ = Mathf.Min(minimumPlateZ, z); maximumPlateZ = Mathf.Max(maximumPlateZ, z);
        float front = game.pusherBody.GetComponent<Collider>().bounds.min.z;
        float guideRear = guideSolids.Max(c => c.bounds.max.z); minimumHardwareGap = Mathf.Min(minimumHardwareGap, front - guideRear);
        foreach (Collider moving in movingSolids)
            foreach (Collider guide in guideSolids)
                if (moving.bounds.Intersects(guide.bounds) && Physics.ComputePenetration(moving, moving.transform.position, moving.transform.rotation,
                    guide, guide.transform.position, guide.transform.rotation, out Vector3 direction, out float distance) && distance > .001f)
                {
                    overlapPairCount++; maximumAssemblyPenetration = Mathf.Max(maximumAssemblyPenetration, distance);
                    if (overlapSamples.Count < 12) overlapSamples.Add(new { moving = moving.name, guide = guide.name, depth = distance, direction = Pos(direction), plateZ = z, fixedTime = Time.fixedTime });
                }
        if (cyclePhysicsSamples % 12 == 0) cycleSamples.Add(new { fixedTime = Time.fixedTime, platePosition = Pos(game.pusherBody.position),
            pocketPositions = pockets.Select(p => Pos(p.transform.position)).ToArray(), hardwareGap = front - guideRear,
            pocketTransformsStable = PocketTransformsStable(), guideTransformsStable = GuideTransformsStable(), overlapPairCount });
    }
    private static bool PocketTransformsStable() => Enumerable.Range(0, pockets.Length).All(i =>
        Vector3.Distance(game.pusherBody.transform.InverseTransformPoint(pockets[i].transform.position), pocketPlatePositions[i]) < .002f &&
        Quaternion.Angle(Quaternion.Inverse(game.pusherBody.rotation) * pockets[i].transform.rotation, pocketPlateRotations[i]) < .05f);
    private static bool GuideTransformsStable() => Enumerable.Range(0, guideSolids.Length).All(i =>
        Vector3.Distance(guideSolids[i].transform.position, initialGuidePositions[i]) < .001f && Quaternion.Angle(guideSolids[i].transform.rotation, initialGuideRotations[i]) < .01f);
    private static void SetupItem(GameObject obj, bool ball)
    {
        var item = obj.GetComponent<MedalItem>(); Require(item != null && obj.GetComponent<Rigidbody>() != null, "Fixture prefab physics/item missing.");
        item.collected = false; item.isBall = ball; item.isPrize = ball; item.payoutAlreadyCredited = false; game.RegisterItem(item); obj.SetActive(true);
    }
    private static void RemoveFixture(GameObject obj)
    {
        if (obj == null) return; var item = obj.GetComponent<MedalItem>(); if (item != null) { item.collected = true; game.UnregisterItem(item); }
        obj.SetActive(false); UnityEngine.Object.Destroy(obj);
    }
    private static void ClearBoard(string label)
    {
        var items = game.itemsRoot.GetComponentsInChildren<MedalItem>(); int count = items.Length;
        foreach (var item in items) RemoveFixture(item.gameObject);
        boundaries.Add(new { phase = label, removedTransientItems = count, remainingBoardItems = game.CountBoardItems(false) + game.CountBoardItems(true) });
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
    private static bool Green(GameObject obj) => obj.GetComponentsInChildren<Renderer>().Any(r => r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") &&
        r.sharedMaterial.GetColor("_BaseColor").g > .6f && r.sharedMaterial.GetColor("_BaseColor").g > r.sharedMaterial.GetColor("_BaseColor").r * 1.4f);
    internal static bool IsGuide(Collider collider) => collider != null && guideRoot != null && collider.transform.IsChildOf(guideRoot);
    internal static bool IsPlaySurface(Collider collider)
    {
        if (collider == null || game == null) return false;
        if (collider == game.pusherBody.GetComponent<Collider>()) return true;
        if (game.sideHoleMainDeck != null && collider == game.sideHoleMainDeck.GetComponent<Collider>()) return true;
        if (game.sideHoleDeckStrips != null && game.sideHoleDeckStrips.Any(t => t != null && collider == t.GetComponent<Collider>())) return true;
        var deck = game.transform.Find("GeneratedPlayfield/PusherBoard"); return deck != null && collider == deck.GetComponent<Collider>();
    }
    internal static void ObservePocket(int index, Collider other)
    {
        if (!SessionState.GetBool(Key, false) || other == null) return;
        var item = other.GetComponentInParent<MedalItem>();
        if (item == null) return;
        if (item.name.StartsWith("MountedPocketLowerDeckMedal_", StringComparison.Ordinal))
        {
            lowerPocketEntryCounts[index] = lowerPocketEntryCounts.TryGetValue(index, out int lowerCount) ? lowerCount + 1 : 1;
            nativeContacts.Add(new { type = "lower-deck-slot-trigger", index, item = item.name, entity = item.GetEntityId().ToString(), fixedTime = Time.fixedTime,
                collected = item.collected, bodyPosition = Pos(item.GetComponent<Rigidbody>().position), controller.TotalSpins, controller.SpinCredits });
            return;
        }
        if (!item.name.StartsWith("MountedPocketNativeMedal_", StringComparison.Ordinal)) return;
        pocketEntryCounts[index] = pocketEntryCounts.TryGetValue(index, out int count) ? count + 1 : 1;
        Rigidbody body = item.GetComponent<Rigidbody>();
        pocketEntryPositions[index] = body == null ? item.transform.position : body.position;
        pocketFallSeen |= pocketEntryPositions[index].y < pocketSpawnY - .08f;
        nativeContacts.Add(new { type = "slot-trigger", index, item = item.name, entity = item.GetEntityId().ToString(), fixedTime = Time.fixedTime,
            bodyDynamic = other.attachedRigidbody != null && !other.attachedRigidbody.isKinematic, position = Pos(pocketEntryPositions[index]) });
    }
    internal static void ObserveCollision(Collision collision, Rigidbody body)
    {
        if (!SessionState.GetBool(Key, false) || collision.contactCount == 0) return;
        if (IsGuide(collision.collider)) nativeContacts.Add(new { type = "solid-guide", name = collision.collider.name,
            colliderType = collision.collider.GetType().Name, fixture = body == null ? null : body.name, fixedTime = Time.fixedTime,
            point = Pos(collision.GetContact(0).point), normal = Pos(collision.GetContact(0).normal) });
    }
    private static void PaidSpawned(GameObject coin) { paidCoin = coin; paidSpawns++; }
    private static void PaidInserted() => paidEvents++;
    private static void ObserveSlotState()
    {
        if (!SessionState.GetBool(Key, false) || controller == null ||
            (phase != Phase.PocketFlight && phase != Phase.PocketSpin && phase != Phase.PocketHold)) return;
        if (controller.IsSlotSpinning)
        {
            if (!spinStartedObserved) { spinStartedObserved = true; spinStartedTime = Time.time; }
            int[] symbols = controller.ReelSymbols.ToArray();
            if (!symbols.SequenceEqual(lastObservedReels))
            {
                reelChangeCount++; lastObservedReels = symbols;
                reelSamples.Add(new { index = pocketIndex, fixtureId, fixedTime = Time.fixedTime, time = Time.time,
                    controller.IsSlotSpinning, controller.TotalSpins, symbols, controller.SlotDisplay });
            }
        }
        else if (controller.TotalSpins == spinBefore + 1 && !spinCompletedObserved)
        { spinCompletedObserved = true; spinCompletedTime = Time.time; }
    }
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
        SessionState.SetBool(Key, false);
        if (game != null) { game.OnPaidMedalSpawned -= PaidSpawned; game.OnMedalInserted -= PaidInserted; game.StopThrowing(); game.pusherSpeed = originalPusherSpeed; }
        if (controller != null) controller.OnStateChanged -= ObserveSlotState;
        foreach (var pair in enabledStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
        if (settings != null) { settings.SettingsFileOverride = originalOverride; settings.persistSettings = originalPersistence; }
        string finalHash = settingsPath == null ? "not-initialized" : Hash(settingsPath);
        Check("realUserSettingsFileRemainedUntouched", settingsHash != null && settingsHash == finalHash);
        if (initialized) { UnityEngine.Random.state = originalRandom; Time.timeScale = originalTimeScale; }
        Application.runInBackground = SessionState.GetBool(BackgroundKey, false);
        Write("mounted-pockets-play-validation.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), startedUtc = SessionState.GetString(Key + ".Utc", ""), success = errors.Count == 0 && phase == Phase.Complete,
            completed = phase == Phase.Complete, phase = phase.ToString(), elapsedWallSeconds = Elapsed, maximumWallSeconds = MaximumSeconds,
            cases = checks.Count, checks, errors, boundaries, pocketSamples, lowerPocketSamples, spinSamples, reelSamples, clickSamples, ballSamples, nativeContacts, cycleSamples, overlapSamples,
            fullStroke = new { cyclePhysicsSamples, minimumPlateZ, maximumPlateZ, minimumHardwareGap, maximumAssemblyPenetration, overlapPairCount,
                movingSolidCount = movingSolids == null ? 0 : movingSolids.Length, guideSolidCount = guideSolids == null ? 0 : guideSolids.Length },
            wallet = new { before = initialWallet, after = game == null ? 0 : game.medals, expectedPaidClickCount = 2, paidSpawns, paidEvents },
            settingsHashes = new { before = settingsHash, after = finalHash },
            fixtureNotes = new[] { "Initial Play items are removed with explicit fixture boundaries; no source scene or prefab is changed.",
                "Slot medal and green ball fixtures instantiate production prefabs; no positive entry or award result is called directly.",
                "Slot outcome probabilities and rescue are zero; each native upper entry runs the real BeginSpin/Update/CompleteSpin path once.",
                "Lower-deck native medal fixtures overlap the moving detectors and remain uncollected without spin credits.",
                "Paid medals use the production screen-ray TryInsertAtScreenPosition path and native landing contacts.",
                "Full-stroke checks include the plate and all solid child collars, tongues and mounting wedges." }
        });
        if (stop && EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }
    private static void Write(string name, object report)
    {
        string root = Path.GetDirectoryName(Application.dataPath), pointer = Path.Combine(root, ".codex-backups", "current-medal-upper-pockets.txt");
        string directory = File.Exists(pointer) ? File.ReadAllText(pointer).Trim() : Path.Combine(root, ".codex-backups", "medal-mounted-pockets");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
    }
}

public sealed class MedalMountedPocketProbe : MonoBehaviour
{
    public int Index;
    private void OnTriggerEnter(Collider other) => MedalMountedPocketPlayCheck.ObservePocket(Index, other);
}

public sealed class MedalMountedContactProbe : MonoBehaviour
{
    public bool SurfaceContact;
    public Collider FirstSurface;
    public Vector3 FirstBodyPosition;
    public int GuideContacts;
    public Collider FirstGuide;
    public Vector3 FirstGuideNormal;
    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody body = GetComponent<Rigidbody>();
        if (!SurfaceContact && MedalMountedPocketPlayCheck.IsPlaySurface(collision.collider))
        { SurfaceContact = true; FirstSurface = collision.collider; FirstBodyPosition = body.position; }
        if (MedalMountedPocketPlayCheck.IsGuide(collision.collider))
        {
            GuideContacts++;
            if (FirstGuide == null) { FirstGuide = collision.collider; if (collision.contactCount > 0) FirstGuideNormal = collision.GetContact(0).normal; }
        }
        MedalMountedPocketPlayCheck.ObserveCollision(collision, body);
    }
}
