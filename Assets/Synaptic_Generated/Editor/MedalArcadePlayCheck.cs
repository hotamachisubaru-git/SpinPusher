using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Bounded Play-only checks of paid inlets, slots and real lottery triggers.</summary>
[InitializeOnLoad]
public static class MedalArcadePlayCheck
{
    private const string SessionKey = "MedalArcade.PlayCheck";
    private const string StartedKey = SessionKey + ".Started";
    private const string UtcKey = SessionKey + ".Utc";
    private const string BackgroundKey = SessionKey + ".OriginalBackground";
    private const double MaximumSeconds = 145;
    private enum Phase { Boot, Spins, Collector, Natural, Jackpot, Timeout, Recovery }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalBallLotteryStation station;
    private static MedalItem collectorBall;
    private static readonly Dictionary<string, object> results = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<string> editorIssues = new List<string>();
    private static readonly List<string> captureIssues = new List<string>();
    private static readonly List<Tuple<double, string, string>> captures = new List<Tuple<double, string, string>>();
    private static readonly List<object> naturalDraws = new List<object>();
    private static readonly Dictionary<Collider, bool> pocketStates = new Dictionary<Collider, bool>();
    private static int[] resetPools;
    private static int spinsBefore, naturalIndex, jackpotIndex, ticket, walletBefore, finishBaseline;
    private static int finishedEvents, lastPayout, lastWallet, expectedPayout, jackpotsBefore;
    private static int collectorWallet, collectorScore;
    private static MedalJackpotKind lastKind;
    private static bool lastJackpot, sawSlotBall, drawStarted, routed, negativeChecked;
    private static bool initialized, originalRunInBackground;
    private static float originalTimeScale, originalSpinDuration, originalDrawTimeout, placedFixedTime;
    private static double phaseStarted, naturalStarted;
    private static double finishedAt;
    private static bool capturing, readyToFinish;
    private static double finishAt;

    static MedalArcadePlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Arcade Play Check")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before running the arcade check.");
        MedalPusherExpansionBuilder.Validate();
        ResetFields();
        // Prevent the older automatic check from sharing this test's Play session.
        SessionState.SetBool("MedalPusher.PlayCheck", false);
        SessionState.SetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
        SessionState.SetString(UtcKey, DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(BackgroundKey, Application.runInBackground);
        Application.runInBackground = true;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
    }

    /// <summary>Entry point for an isolated Unity -batchmode -executeMethod run without -quit.</summary>
    public static void BatchRun()
    {
        try
        {
            Require(Application.isBatchMode, "BatchRun requires an isolated batch-mode Unity project.");
            // Unity 6.7 can still honor the option flags after the legacy enable toggle.
            // Force both values in the isolated project so Start/static state is fresh.
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSceneManager.OpenScene(MedalPusherSceneBuilder.ScenePath);
            MedalPusherExpansionBuilder.Build();
            Run();
        }
        catch (Exception exception)
        {
            MedalPusherExpansionBuilder.WriteReport("expansion-play-validation.json", new {
                timestampUtc = DateTime.UtcNow.ToString("O"), success = false, errors = new[] { exception.ToString() }
            });
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Medal Pusher/Inspect Arcade Play Check")]
    public static void Inspect()
    {
        var current = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        MedalPusherExpansionBuilder.WriteReport("expansion-play-state.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), playing = EditorApplication.isPlaying,
            paused = EditorApplication.isPaused, compiling = EditorApplication.isCompiling,
            requested = SessionState.GetBool(SessionKey, false), phase = phase.ToString(),
            elapsedWallSeconds = Elapsed(), gameTime = Time.time, naturalIndex, jackpotIndex,
            activeKind = current == null ? null : current.ActiveKind?.ToString(),
            pendingDraws = current == null ? 0 : current.PendingBallDraws,
            bootReadiness = BootDiagnostics(),
            stations = current == null || current.stations == null ? null : current.stations.Select(s => s == null ? null : new {
                kind = s.kind.ToString(), drawing = s.IsDrawing, ticket = s.CurrentTicket,
                timedOut = s.LastTimedOut, pocket = s.LastPocket == null ? null : s.LastPocket.transform.parent.name,
                ballPosition = s.ActiveBall == null ? null : Position(s.ActiveBall.transform.position),
                ballLocalPosition = s.ActiveBall == null ? null : Position(s.transform.InverseTransformPoint(s.ActiveBall.transform.position))
            }).ToArray()
        });
    }

    private static object Position(Vector3 position) => new { x = position.x, y = position.y, z = position.z };
    private static double Elapsed() => EditorApplication.timeSinceStartup - SessionState.GetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
    private static double Now => EditorApplication.timeSinceStartup;

    private static void ResetFields()
    {
        phase = Phase.Boot; initialized = false; game = null; controller = null; station = null;
        collectorBall = null; naturalIndex = jackpotIndex = finishedEvents = 0;
        results.Clear(); errors.Clear(); editorIssues.Clear(); captureIssues.Clear(); captures.Clear(); naturalDraws.Clear(); pocketStates.Clear();
        drawStarted = routed = negativeChecked = sawSlotBall = false;
        capturing = readyToFinish = false;
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (capturing) captureIssues.Add(message);
        else if ((stack ?? "").Contains("Unity.AI.ModelSelector") || message.Contains("Unity.AI.ModelSelector") ||
            ((stack ?? "").Contains("UnityEditor.Search.SearchDatabase") &&
             (stack ?? "").Contains("GetDefaultSearchDatabase") && (stack ?? "").Contains("IndexationOnStartup")))
            editorIssues.Add(message);
        else errors.Add(message);
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(SessionKey, false))
        {
            errors.Add("Play mode exited before the arcade check completed.");
            Finish(false);
        }
    }

    private static void Check(string name, bool passed)
    {
        results[name] = passed;
        if (!passed) errors.Add("Failed: " + name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static FieldInfo Field(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        return field;
    }
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void ClearQueue() => ((Queue<MedalJackpotKind>)Field(controller, "ballDraws").GetValue(controller)).Clear();

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        Application.runInBackground = true;
        if (Elapsed() > MaximumSeconds)
        {
            errors.Add("Arcade check exceeded its 145 second wall-clock limit."); Finish(true); return;
        }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        try
        {
            ProcessCaptures();
            if (readyToFinish) { if (Now >= finishAt) Finish(true); return; }
            if (phase == Phase.Boot)
            {
                Require(Elapsed() < 25, "Runtime Play did not initialize within 25 wall-clock seconds.");
                if (Time.time < .5f) return;
                // Editor ticks can arrive between Awake and the first player-loop Start.
                // Disabling the controller at that point would suppress its paid-medal binding.
                if (!RuntimeSubscriptionsReady())
                {
                    results["bootReadiness"] = BootDiagnostics();
                    Require(Elapsed() < 25, "Runtime Start subscriptions were not ready within 25 seconds.");
                    return;
                }
                Initialize();
            }
            else if (phase == Phase.Spins) TickSpins();
            else if (phase == Phase.Collector) TickCollector();
            else TickDraw();
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString()); Finish(true);
        }
    }

    private static bool RuntimeSubscriptionsReady()
    {
        var candidateGame = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>();
        var candidateController = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        if (candidateGame == null || candidateController == null || !candidateController.isActiveAndEnabled) return false;
        bool paidBound = candidateGame.OnMedalInserted != null && candidateGame.OnMedalInserted.GetInvocationList()
            .Any(callback => ReferenceEquals(callback.Target, candidateController));
        bool arcadeBound = candidateController.OnStateChanged != null && candidateController.OnStateChanged.GetInvocationList()
            .Any(callback => callback.Target is MedalArcadeUI);
        bool balanceBound = candidateGame.OnMedalsChanged != null && candidateGame.OnMedalsChanged.GetInvocationList()
            .Any(callback => callback.Target is MedalPusherUI);
        return paidBound && arcadeBound && balanceBound;
    }

    private static string[] Targets(Delegate callbacks) => callbacks == null ? Array.Empty<string>() : callbacks.GetInvocationList()
        .Select(callback => (callback.Target == null ? "static" : callback.Target.GetType().FullName) + "." + callback.Method.Name).ToArray();

    private static object BootDiagnostics()
    {
        var candidateGame = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>();
        var candidateController = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        var arcade = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
        var balance = UnityEngine.Object.FindAnyObjectByType<MedalPusherUI>();
        return new {
            frameCount = Time.frameCount, gameTime = Time.time,
            enterPlayModeOptions = EditorSettings.enterPlayModeOptions.ToString(),
            enterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
            game = candidateGame == null ? null : new {
                enabled = candidateGame.enabled, active = candidateGame.gameObject.activeInHierarchy,
                name = candidateGame.name, medals = candidateGame.medals,
                onMedalInsertedTargets = Targets(candidateGame.OnMedalInserted),
                onMedalsChangedTargets = Targets(candidateGame.OnMedalsChanged)
            },
            controller = candidateController == null ? null : new {
                enabled = candidateController.enabled, active = candidateController.gameObject.activeInHierarchy,
                gameAssigned = candidateController.game != null, onStateChangedTargets = Targets(candidateController.OnStateChanged)
            },
            arcadeUI = arcade == null ? null : new { enabled = arcade.enabled, active = arcade.gameObject.activeInHierarchy },
            balanceUI = balance == null ? null : new { enabled = balance.enabled, active = balance.gameObject.activeInHierarchy }
        };
    }

    private static void Initialize()
    {
        results["bootReadiness"] = BootDiagnostics();
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>();
        controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        Require(game != null && controller != null && controller.game == game, "Arcade/game connection missing.");
        Require(controller.stations != null && controller.stations.Length == 3 &&
            controller.stations.Where((s, i) => s == null || s.kind != (MedalJackpotKind)i).Count() == 0,
            "Three stations must be in Ruby/Sapphire/Amber order.");
        Require(game.medalInlets != null && game.medalInlets.Length == 3 && game.medalInlets.All(t => t != null), "Three inlets missing.");
        Require(!controller.ActiveKind.HasValue, "An incidental lottery was already running at test initialization.");
        originalRunInBackground = SessionState.GetBool(BackgroundKey, false);
        originalTimeScale = Time.timeScale;
        originalSpinDuration = controller.slotSpinDuration;
        initialized = true;
        Application.runInBackground = true; Time.timeScale = 1f;
        game.StopThrowing();
        var input = game.GetComponent<MedalInputHandler>(); if (input != null) input.enabled = false;
        var manager = game.GetComponent<PrizeDropManager>(); if (manager != null) manager.enabled = false;
        // Disable only game Update/FixedUpdate; its public paid/reward methods still run.
        // This prevents physical bonus/normal medal awards from masking exact wallet checks.
        game.enabled = false; controller.enabled = false;
        ClearBoardBalls(); FreezeBoardItems(); ClearQueue();
        resetPools = (int[])controller.jackpotPools.Clone();
        controller.OnLotteryStarted += OnDrawStarted;
        controller.OnLotteryFinished += OnDrawFinished;
        int initialWallet = game.medals, initialCredits = controller.SpinCredits;
        int insertedEvents = 0;
        Action inserted = () => insertedEvents++;
        game.OnMedalInserted += inserted;
        try
        {
            for (int inlet = 0; inlet < 3; inlet++)
            {
                game.SelectInlet(inlet);
                Check("inletSelected_" + inlet, game.selectedInlet == inlet && game.medalSpawnPoint == game.medalInlets[inlet]);
                for (int throwIndex = 0; throwIndex < 4; throwIndex++)
                {
                    var previous = new HashSet<MedalItem>(game.itemsRoot.GetComponentsInChildren<MedalItem>(true));
                    int wallet = game.medals, count = game.CountBoardItems(false);
                    Set(game, "throwTimer", 0f); game.ThrowSingleMedal();
                    var spawned = game.itemsRoot.GetComponentsInChildren<MedalItem>(true).Where(item => !previous.Contains(item)).ToArray();
                    Check("paidThrow_" + inlet + "_" + throwIndex,
                        game.medals == wallet - 1 && game.CountBoardItems(false) == count + 1 && spawned.Length == 1);
                    if (spawned.Length == 1)
                    {
                        Vector3 local = game.transform.InverseTransformPoint(spawned[0].transform.position);
                        Vector3 outlet = game.transform.InverseTransformPoint(game.medalInlets[inlet].position);
                        Check("inletSpawn_" + inlet + "_" + throwIndex,
                            !spawned[0].isBall && !spawned[0].isPrize &&
                            Mathf.Abs(local.x - outlet.x) <= game.inletScatter + .02f &&
                            Mathf.Abs(local.y - outlet.y) < .01f && Mathf.Abs(local.z - outlet.z) <= .2f);
                    }
                    FreezeBoardItems();
                }
            }
        }
        finally { game.OnMedalInserted -= inserted; }
        Check("twelvePaidMedalsConsumeTwelve", game.medals == initialWallet - 12 && insertedEvents == 12);
        Check("twelvePaidMedalsProduceFourCredits", controller.SpinCredits == initialCredits + 4 && controller.MedalsTowardSpin == 0);
        Check("allJackpotPoolsAdvanceByTwelve", controller.jackpotPools.Where((pool, i) => pool != resetPools[i] + 12).Count() == 0);
        Require(initialCredits == 0 && controller.medalsPerSpin == 3 && game.medalsPerThrow == 1, "The check requires fresh three-medal slots and single-medal throws.");
        spinsBefore = controller.TotalSpins;
        controller.slotSpinDuration = .1f;
        Set(controller, "nextLotteryAt", float.PositiveInfinity);
        Set(controller, "nextSpinAt", 0f);
        controller.enabled = true; phase = Phase.Spins; phaseStarted = Now;
        TryCapture("arcade-cabinet.png", "Cabinet");
    }

    private static void FreezeBoardItems()
    {
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>(true))
        {
            foreach (var collider in item.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var body = item.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
            { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.isKinematic = true; }
        }
    }

    private static void ClearBoardBalls()
    {
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>(true))
            if (item.isBall)
            {
                item.collected = true; game.UnregisterItem(item);
                foreach (var collider in item.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                UnityEngine.Object.Destroy(item.gameObject);
            }
    }

    private static void TickSpins()
    {
        sawSlotBall |= controller.PendingBallDraws > 0 || game.itemsRoot.GetComponentsInChildren<MedalItem>().Any(item => item.isBall && !item.collected);
        FreezeBoardItems();
        if (controller.TotalSpins < spinsBefore + 4 || controller.IsSlotSpinning) { Require(Now - phaseStarted < 8, "Four slot spins did not complete within eight seconds."); return; }
        Check("fourRealSlotSpinsComplete", controller.TotalSpins == spinsBefore + 4 && controller.SpinCredits == 0);
        Check("fourSpinsGuaranteeBallOrDraw", sawSlotBall);
        controller.enabled = false; controller.slotSpinDuration = originalSpinDuration;
        ClearBoardBalls(); ClearQueue(); Set(controller, "nextLotteryAt", 0f);
        var detector = UnityEngine.Object.FindAnyObjectByType<MedalDropDetector>();
        Require(detector != null && detector.GetComponent<Collider>().isTrigger, "Real collection trigger missing.");
        var collector = detector.GetComponent<Collider>();
        Vector3 point = collector.bounds.center; point.y = collector.bounds.max.y + 1f;
        var ball = UnityEngine.Object.Instantiate(controller.ballPrefabs[0], point, Quaternion.identity, game.itemsRoot);
        ball.SetActive(true); collectorBall = ball.GetComponent<MedalItem>();
        Require(collectorBall != null && collectorBall.isBall, "Collection test ball prefab missing its identity.");
        collectorBall.collected = false; game.RegisterItem(collectorBall);
        var body = ball.GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true; body.linearVelocity = Vector3.down * .1f;
        foreach (var collider in ball.GetComponentsInChildren<Collider>(true)) collider.enabled = true;
        collectorWallet = game.medals; collectorScore = game.score;
        phase = Phase.Collector; phaseStarted = Now;
    }

    private static void TickCollector()
    {
        if (controller.PendingBallDraws == 0) { Require(Now - phaseStarted < 3, "Board ball did not enter the real collection trigger."); return; }
        Check("realCollectorQueuesOneBall", controller.PendingBallDraws == 1 && !ReferenceEquals(collectorBall, null) && collectorBall.collected);
        Check("ballCollectionDoesNotAwardPrizeOrCoin", game.medals == collectorWallet && game.score == collectorScore);
        // Repeating the same collected item must not issue a second lottery ticket.
        controller.CollectBall(collectorBall);
        Check("collectedBoardBallQueuesExactlyOnce", controller.PendingBallDraws == 1);
        naturalIndex = 0; naturalStarted = Now;
        StartDraw(Phase.Natural, MedalJackpotKind.Ruby, true);
    }

    private static void StartDraw(Phase nextPhase, MedalJackpotKind kind, bool alreadyQueued = false)
    {
        Require(!controller.ActiveKind.HasValue, "Previous lottery ticket remains active.");
        RestorePockets();
        phase = nextPhase; station = controller.stations[(int)kind];
        phaseStarted = Now; drawStarted = routed = negativeChecked = false;
        finishBaseline = finishedEvents; ticket = 0; walletBefore = game.medals;
        expectedPayout = nextPhase == Phase.Jackpot ? controller.jackpotPools[(int)kind] : 0;
        jackpotsBefore = controller.TotalJackpots;
        if (!alreadyQueued) { ClearQueue(); controller.QueueBallDraw(kind); }
        Set(controller, "nextLotteryAt", 0f); controller.enabled = true;
    }

    private static void OnDrawStarted(MedalJackpotKind kind)
    {
        if (station == null || station.kind != kind) { errors.Add("A lottery started for an unexpected station."); return; }
        drawStarted = true;
        ticket = (int)Field(controller, "activeTicket").GetValue(controller);
        if (phase == Phase.Timeout)
        {
            int before = game.medals;
            Check("activeTicketIncludedInQueueCap", controller.PendingBallDraws == 12);
            controller.QueueBallDraw(MedalJackpotKind.Amber);
            Check("activeQueueOverflowCompensatesOnce", controller.PendingBallDraws == 12 && game.medals == before + 10);
            ClearQueue(); walletBefore = game.medals;
        }
    }

    private static void OnDrawFinished(MedalJackpotKind kind, int payout, bool jackpot)
    {
        finishedEvents++; lastKind = kind; lastPayout = payout; lastJackpot = jackpot; lastWallet = game.medals;
        finishedAt = Now;
        // Capture this draw's celebration before the next OnLotteryStarted clears it.
        if (phase == Phase.Jackpot)
            captures.Add(Tuple.Create(Now + .08, "arcade-" + kind.ToString().ToLowerInvariant() + "-jackpot.png", kind.ToString()));
    }

    private static void TickDraw()
    {
        if (phase == Phase.Natural) Require(Now - naturalStarted < 120, "Nine natural draws exceeded their 120 second observation limit.");
        if (finishedEvents > finishBaseline)
        {
            // Give the actual UI Update and scheduled render a frame before changing draws.
            if (Now - finishedAt >= .15) CompleteDraw();
            return;
        }
        Require(Now - phaseStarted < (phase == Phase.Natural ? 13 : 4), "Lottery phase did not complete: " + phase + " / " + station.kind);
        if (!drawStarted || !station.IsDrawing || station.ActiveBall == null) return;
        if (phase == Phase.Natural && !negativeChecked)
        {
            var token = station.ActiveBall;
            int before = game.medals;
            var other = controller.stations[((int)station.kind + 1) % 3];
            controller.FinishLottery(other, ticket, true, 9999);
            controller.FinishLottery(station, ticket + 10000, true, 9999);
            var foreignPocket = other.GetComponentsInChildren<MedalLotteryPocket>().First();
            bool rejected = !station.TryResolve(foreignPocket, token) && !other.TryResolve(foreignPocket, token);
            Check("ticketAndStationRejected_" + naturalIndex,
                rejected && !token.IsConsumed && game.medals == before && controller.ActiveKind == station.kind && station.IsDrawing);
            Check("naturalBallHasDynamicPhysics_" + naturalIndex,
                token.gameObject.activeInHierarchy && token.GetComponent<Rigidbody>() != null &&
                !token.GetComponent<Rigidbody>().isKinematic && token.GetComponent<Rigidbody>().useGravity && token.GetComponent<MedalItem>() == null);
            negativeChecked = true;
            if (naturalIndex < 3) TryCapture("arcade-" + station.kind.ToString().ToLowerInvariant() + ".png", station.kind.ToString());
        }
        if ((phase == Phase.Jackpot || phase == Phase.Recovery) && !routed)
            RouteBallIntoRealPocket(phase == Phase.Jackpot);
    }

    private static void RouteBallIntoRealPocket(bool jackpot)
    {
        var pockets = station.GetComponentsInChildren<MedalLotteryPocket>();
        var target = pockets.FirstOrDefault(pocket => pocket.jackpot == jackpot);
        Require(target != null, "Required real reward pocket is missing.");
        foreach (var pocket in pockets)
        {
            var collider = pocket.GetComponent<Collider>();
            pocketStates[collider] = collider.enabled; collider.enabled = pocket == target;
        }
        var body = station.ActiveBall.GetComponent<Rigidbody>();
        Require(body != null && target.GetComponent<Collider>().isTrigger, "Real pocket route lacks a Rigidbody or trigger.");
        expectedPayout = jackpot ? controller.jackpotPools[(int)station.kind] : target.smallReward;
        body.position = target.GetComponent<Collider>().bounds.center;
        body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.WakeUp();
        Physics.SyncTransforms(); placedFixedTime = Time.fixedTime; routed = true;
    }

    private static void CompleteDraw()
    {
        string label = station.kind + "_" + naturalIndex;
        Check("oneFinishEvent_" + phase + "_" + label, finishedEvents == finishBaseline + 1 && lastKind == station.kind);
        Check("oneWalletAward_" + phase + "_" + label, lastWallet == walletBefore + lastPayout && game.medals == lastWallet);
        int before = game.medals;
        controller.FinishLottery(station, ticket, true, 9999);
        Check("duplicateTicketIgnored_" + phase + "_" + label, game.medals == before && finishedEvents == finishBaseline + 1 && !controller.ActiveKind.HasValue);
        if (phase == Phase.Natural)
        {
            bool resolved = drawStarted && ticket > 0 && station.LastPocket != null &&
                station.LastPocket.station == station && !station.LastTimedOut && !controller.LastDrawTimedOut;
            Check("naturalPocketResolved_" + naturalIndex + "_" + station.kind, resolved);
            naturalDraws.Add(new {
                kind = station.kind.ToString(), repetition = naturalIndex / 3 + 1, ticket,
                pocket = station.LastPocket == null ? null : station.LastPocket.transform.parent.name,
                trigger = station.LastPocket == null ? null : station.LastPocket.name,
                timedOut = station.LastTimedOut, jackpot = lastJackpot, payout = lastPayout,
                elapsedWallSeconds = Now - phaseStarted
            });
            naturalIndex++;
            if (naturalIndex < 9) StartDraw(Phase.Natural, (MedalJackpotKind)(naturalIndex % 3));
            else { Check("nineNaturalDrawsObserved", naturalDraws.Count == 9); jackpotIndex = 0; StartDraw(Phase.Jackpot, MedalJackpotKind.Ruby); }
        }
        else if (phase == Phase.Jackpot)
        {
            Check("jackpotRealTrigger_" + station.kind,
                routed && Time.fixedTime > placedFixedTime && station.LastPocket != null && station.LastPocket.jackpot &&
                lastJackpot && !station.LastTimedOut && lastPayout == expectedPayout);
            Check("jackpotPoolReset_" + station.kind,
                controller.TotalJackpots == jackpotsBefore + 1 && controller.jackpotPools[(int)station.kind] == resetPools[(int)station.kind]);
            var arcade = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
            Check("jackpotBannerMatchesResult_" + station.kind,
                arcade != null && arcade.payoutBanner != null && arcade.payoutBanner.gameObject.activeInHierarchy &&
                arcade.payoutBanner.text.Contains(station.kind == MedalJackpotKind.Ruby ? "赤" : station.kind == MedalJackpotKind.Sapphire ? "青" : "黄") &&
                arcade.payoutBanner.text.Contains("大当たり！") && arcade.payoutBanner.text.Contains("+" + lastPayout + "枚"));
            RestorePockets(); jackpotIndex++;
            if (jackpotIndex < 3) StartDraw(Phase.Jackpot, (MedalJackpotKind)jackpotIndex);
            else StartQueueAndTimeoutCheck();
        }
        else if (phase == Phase.Timeout)
        {
            Check("timeoutPaysFifteenExactlyOnce", drawStarted && station.LastTimedOut && controller.LastDrawTimedOut && !lastJackpot && lastPayout == 15);
            Check("timeoutClearsBallAndBusyState", !station.IsDrawing && station.ActiveBall == null && !controller.ActiveKind.HasValue);
            station.drawTimeout = originalDrawTimeout; RestorePockets();
            StartDraw(Phase.Recovery, MedalJackpotKind.Sapphire);
        }
        else
        {
            Check("drawRecoversAfterTimeout", drawStarted && routed && Time.fixedTime > placedFixedTime &&
                station.LastPocket != null && !station.LastPocket.jackpot && !station.LastTimedOut && !controller.LastDrawTimedOut &&
                !lastJackpot && lastPayout == expectedPayout);
            RestorePockets(); controller.enabled = false; readyToFinish = true; finishAt = Now + .15;
        }
    }

    private static void StartQueueAndTimeoutCheck()
    {
        controller.enabled = false; ClearQueue();
        int before = game.medals;
        for (int i = 0; i < 20; i++) controller.QueueBallDraw((MedalJackpotKind)(i % 3));
        Check("drawQueueIsBoundedAtTwelve", controller.PendingBallDraws == 12);
        Check("eightQueueOverflowsCreditEighty", game.medals == before + 80);
        station = controller.stations[0]; originalDrawTimeout = station.drawTimeout; station.drawTimeout = .2f;
        foreach (var pocket in station.GetComponentsInChildren<MedalLotteryPocket>())
        {
            var collider = pocket.GetComponent<Collider>(); pocketStates[collider] = collider.enabled; collider.enabled = false;
        }
        phase = Phase.Timeout; phaseStarted = Now; drawStarted = routed = negativeChecked = false;
        finishBaseline = finishedEvents; walletBefore = game.medals; ticket = 0;
        Set(controller, "nextLotteryAt", 0f); controller.enabled = true;
    }

    private static void RestorePockets()
    {
        foreach (var state in pocketStates) if (state.Key != null) state.Key.enabled = state.Value;
        pocketStates.Clear();
    }

    private static void ProcessCaptures()
    {
        for (int i = captures.Count - 1; i >= 0; i--)
            if (Now >= captures[i].Item1)
            {
                var request = captures[i]; captures.RemoveAt(i); TryCapture(request.Item2, request.Item3);
            }
    }

    private static void TryCapture(string filename, string selection)
    {
        if (!Application.isBatchMode) return;
        capturing = true;
        try { MedalArcadeBatchCapture.Capture(filename, selection); }
        catch (Exception exception) { captureIssues.Add(filename + ": " + exception.Message); }
        finally { capturing = false; }
    }

    private static void Finish(bool stopPlay)
    {
        SessionState.SetBool(SessionKey, false);
        RestorePockets();
        if (controller != null)
        {
            controller.OnLotteryStarted -= OnDrawStarted; controller.OnLotteryFinished -= OnDrawFinished;
        }
        Application.runInBackground = SessionState.GetBool(BackgroundKey, originalRunInBackground);
        if (initialized) Time.timeScale = originalTimeScale;
        results["timestampUtc"] = DateTime.UtcNow.ToString("O");
        results["startedUtc"] = SessionState.GetString(UtcKey, "");
        results["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        results["elapsedWallSeconds"] = Elapsed(); results["playSeconds"] = Time.time;
        results["completedPhase"] = phase.ToString(); results["naturalDraws"] = naturalDraws.ToArray();
        results["finalBootReadiness"] = BootDiagnostics();
        results["success"] = errors.Count == 0 && phase == Phase.Recovery;
        results["errors"] = errors.Distinct().ToArray(); results["editorServiceIssues"] = editorIssues.Distinct().ToArray();
        results["captureIssues"] = captureIssues.Distinct().ToArray();
        MedalPusherExpansionBuilder.WriteReport("expansion-play-validation.json", results);
        Debug.Log("[MedalPusher] Arcade Play check complete: " + ((bool)results["success"] ? "PASS" : "FAIL"));
        if (stopPlay)
        {
            EditorApplication.isPaused = false;
            if (Application.isBatchMode) EditorApplication.Exit((bool)results["success"] ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
