using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Play-only regression for the old 160-medal stop, Japanese HUD, and matching roulettes.</summary>
[InitializeOnLoad]
public static class MedalPusherRevisionCheck
{
    private const string SessionKey = "MedalPusher.RevisionCheck";
    private const string StartedKey = SessionKey + ".Started";
    private const string UtcKey = SessionKey + ".Utc";
    private const string BackgroundKey = SessionKey + ".OriginalBackground";
    private const int ThrowTarget = 300;
    private const double MaximumSeconds = 95;
    private enum Phase { Boot, Throws, Reservation, Settle, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalSlotJackpotController controller;
    private static MedalPusherUI baseUI;
    private static MedalArcadeUI arcadeUI;
    private static readonly Dictionary<string, object> results = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<string> editorIssues = new List<string>();
    private static readonly List<string> captureIssues = new List<string>();
    private static readonly List<object> samples = new List<object>();
    private static int attempts, paidEvents, rejected, debitFailures, maxCount, startingCount, realCap;
    private static int creditsBefore, towardBefore;
    private static float nextThrowAt, settleUntil, pusherMin, pusherMax;
    private static bool initialized, capturing, originalBackground;
    private static float originalTimeScale;
    private static double nextSampleAt;

    static MedalPusherRevisionCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Revision Play Check")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before the revision check.");
        MedalPusherExpansionBuilder.Validate();
        Reset();
        SessionState.SetBool("MedalPusher.PlayCheck", false);
        SessionState.SetBool("MedalArcade.PlayCheck", false);
        SessionState.SetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
        SessionState.SetString(UtcKey, DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(BackgroundKey, Application.runInBackground);
        Application.runInBackground = true;
        SessionState.SetBool(SessionKey, true);
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
    }

    /// <summary>Use an isolated project with -batchmode -executeMethod and without -quit.</summary>
    public static void BatchRun()
    {
        try
        {
            Require(Application.isBatchMode, "BatchRun requires an isolated batch-mode Unity project.");
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSceneManager.OpenScene(MedalPusherSceneBuilder.ScenePath);
            MedalPusherExpansionBuilder.Build();
            Run();
        }
        catch (Exception exception)
        {
            MedalPusherExpansionBuilder.WriteReport("revision-play-validation.json", new {
                timestampUtc = DateTime.UtcNow.ToString("O"), success = false, errors = new[] { exception.ToString() }
            });
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Tools/Medal Pusher/Inspect Revision Play Check")]
    public static void Inspect()
    {
        var current = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>();
        MedalPusherExpansionBuilder.WriteReport("revision-play-state.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(SessionKey, false),
            playing = EditorApplication.isPlaying, paused = EditorApplication.isPaused,
            phase = phase.ToString(), elapsedWallSeconds = Elapsed, attempts, paidEvents, rejected, maxCount,
            boardMedals = current == null ? 0 : current.CountBoardItems(false),
            wallet = current == null ? 0 : current.medals,
            cap = current == null ? 0 : current.maxMedalsOnBoard, bootReadiness = BootDiagnostics()
        });
    }

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartedKey, (float)Now);
    private static void Reset()
    {
        game = null; controller = null; baseUI = null; arcadeUI = null;
        phase = Phase.Boot; initialized = capturing = false;
        attempts = paidEvents = rejected = debitFailures = maxCount = startingCount = 0;
        results.Clear(); errors.Clear(); editorIssues.Clear(); captureIssues.Clear(); samples.Clear();
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(SessionKey, false) ||
            (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
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
            errors.Add("Play mode exited before the revision check completed."); Finish(false);
        }
    }

    private static FieldInfo Field(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException(target.GetType().Name, name);
        return field;
    }
    private static int PendingBonus => (int)Field(game, "pendingBonusMedals").GetValue(game);
    private static float ThrowTimer => (float)Field(game, "throwTimer").GetValue(game);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, bool passed)
    { results[name] = passed; if (!passed) errors.Add("Failed: " + name); }
    private static string[] Targets(Delegate action) => action == null ? Array.Empty<string>()
        : action.GetInvocationList().Select(d => (d.Target == null ? "static" : d.Target.GetType().Name) + "." + d.Method.Name).ToArray();
    private static bool HasTarget(Delegate action, object target) => action != null && target != null &&
        action.GetInvocationList().Any(d => ReferenceEquals(d.Target, target));

    private static bool Ready()
    {
        game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>();
        controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>();
        baseUI = UnityEngine.Object.FindAnyObjectByType<MedalPusherUI>();
        arcadeUI = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
        return game != null && controller != null && baseUI != null && arcadeUI != null &&
            HasTarget(game.OnMedalInserted, controller) && HasTarget(game.OnMedalsChanged, baseUI) &&
            HasTarget(controller.OnStateChanged, arcadeUI);
    }

    private static object BootDiagnostics() => new {
        frameCount = Time.frameCount, gameTime = Time.time,
        enterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled,
        enterPlayModeOptions = EditorSettings.enterPlayModeOptions.ToString(),
        game = game == null ? null : new { game.enabled, active = game.gameObject.activeInHierarchy },
        controller = controller == null ? null : new { controller.enabled, active = controller.gameObject.activeInHierarchy },
        paidTargets = Targets(game == null ? null : game.OnMedalInserted),
        walletTargets = Targets(game == null ? null : game.OnMedalsChanged),
        stateTargets = Targets(controller == null ? null : controller.OnStateChanged)
    };

    private static void Tick()
    {
        if (!SessionState.GetBool(SessionKey, false)) return;
        Application.runInBackground = true;
        if (Elapsed > MaximumSeconds) { errors.Add("Revision check exceeded 95 wall-clock seconds."); Finish(true); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        try
        {
            if (phase == Phase.Boot)
            {
                Require(Elapsed < 25, "Runtime Play did not initialize within 25 wall-clock seconds.");
                if (Time.time < .5f) return;
                if (!Ready())
                {
                    results["bootReadiness"] = BootDiagnostics();
                    Require(Elapsed < 25, "Runtime Start subscriptions were not ready within 25 seconds."); return;
                }
                Initialize();
            }
            ObservePhysics();
            if (phase == Phase.Throws && Time.time >= nextThrowAt && ThrowTimer <= 0f)
            {
                game.SelectInlet(attempts % 3);
                int beforeEvents = paidEvents, wallet = game.medals;
                game.ThrowSingleMedal(); attempts++;
                int inserted = paidEvents - beforeEvents;
                if (inserted != 1) rejected++;
                if (game.medals != wallet - inserted) debitFailures++;
                nextThrowAt = Time.time + .05f;
                if (attempts == ThrowTarget) CompleteThrows();
            }
            else if (phase == Phase.Reservation && ThrowTimer <= 0f) CheckReservation();
            else if (phase == Phase.Settle && Time.time >= settleUntil)
            {
                Check("dynamicBoardStayedBelowCap", maxCount <= realCap && game.CountBoardItems(false) <= realCap);
                Check("pusherContinuedMoving", pusherMax - pusherMin > .5f && game.enabled && game.pusherBody != null);
                CheckUI(); Capture("revision-hud-japanese.png", null); Capture("revision-cabinet-japanese.png", "Cabinet");
                phase = Phase.Complete; Finish(true);
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }

    private static void Initialize()
    {
        results["bootReadiness"] = BootDiagnostics();
        originalBackground = SessionState.GetBool(BackgroundKey, false); originalTimeScale = Time.timeScale;
        Application.runInBackground = true; Time.timeScale = 1;
        initialized = true;
        game.StopThrowing();
        foreach (var input in UnityEngine.Object.FindObjectsByType<MedalInputHandler>(FindObjectsSortMode.None)) input.enabled = false;
        controller.enabled = false; // Keep its real paid-medal subscription; prevent incidental spins/draws.
        var manager = game.GetComponent<PrizeDropManager>();
        Require(manager != null, "Prize manager missing.");
        manager.enabled = false; manager.bonusMedalChance = 0;
        game.medalsPerThrow = 1; game.throwInterval = .03f;
        game.AddMedals(1000); // The reported bug occurs with a positive wallet, not an empty one.
        realCap = game.maxMedalsOnBoard; startingCount = game.CountBoardItems(false);
        creditsBefore = controller.SpinCredits; towardBefore = controller.MedalsTowardSpin;
        game.OnMedalInserted += CountPaid;
        pusherMin = float.PositiveInfinity; pusherMax = float.NegativeInfinity;
        Check("configuredCapExceedsFormer160", realCap > 160 && realCap <= 1024);
        Check("actualBoardPhysicsRemainsEnabled", game.enabled && game.pusherBody != null && game.itemsRoot != null);
        results["previousLiveFailure"] = new { wallet = 326, boardMedals = 160, previousCap = 160,
            source = "Root's live scene inspection before this revision" };
        results["initial"] = new { wallet = game.medals, boardMedals = startingCount, cap = realCap,
            physicsFrozenByHarness = false, boardItemsRemovedByHarness = 0, bonusReservations = PendingBonus };
        CheckClones();
        nextThrowAt = Time.time; nextSampleAt = Now; phase = Phase.Throws;
    }

    private static void CountPaid() { paidEvents++; }

    private static void ObservePhysics()
    {
        int count = game.CountBoardItems(false); maxCount = Mathf.Max(maxCount, count);
        if (game.pusherBody != null)
        {
            float z = game.transform.InverseTransformPoint(game.pusherBody.position).z;
            pusherMin = Mathf.Min(pusherMin, z); pusherMax = Mathf.Max(pusherMax, z);
        }
        if (Now < nextSampleAt) return;
        nextSampleAt = Now + .5;
        int dynamicCoins = 0;
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
        {
            if (item.collected || item.isPrize) continue;
            var body = item.GetComponent<Rigidbody>();
            Require(body != null && !body.isKinematic, "An ordinary board medal lost its dynamic Rigidbody.");
            Require(Finite(body.position) && Finite(body.linearVelocity) && Finite(body.angularVelocity), "Non-finite medal physics.");
            dynamicCoins++;
        }
        samples.Add(new { playSeconds = Time.time, attempts, paidEvents, boardMedals = count, dynamicCoins,
            wallet = game.medals, pendingBonus = PendingBonus });
        Require(game.medals > 0, "Wallet became empty during the positive-wallet regression.");
        Require(count <= realCap, "Ordinary medals exceeded the configured safety cap.");
    }
    private static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsInfinity(v.x) ||
        float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z));

    private static void CompleteThrows()
    {
        maxCount = Mathf.Max(maxCount, game.CountBoardItems(false));
        Check("threeHundredPaidThrowsSucceeded", attempts == ThrowTarget && paidEvents == ThrowTarget && rejected == 0);
        Check("paidThrowsDebitExactlyOne", debitFailures == 0);
        Check("realBoardExceededFormer160", maxCount > 160);
        int paidUnits = creditsBefore * controller.medalsPerSpin + towardBefore + paidEvents;
        Check("paidThrowsStillEarnSlotCredits", controller.SpinCredits == paidUnits / controller.medalsPerSpin &&
            controller.MedalsTowardSpin == paidUnits % controller.medalsPerSpin);
        results["throws"] = new { attempts, paidEvents, rejected, debitFailures, startingCount, maxCount,
            finalCount = game.CountBoardItems(false), wallet = game.medals,
            slotCredits = controller.SpinCredits, towardSpin = controller.MedalsTowardSpin };
        phase = Phase.Reservation;
    }

    private static void CheckReservation()
    {
        int board = game.CountBoardItems(false), beforeEvents = paidEvents, wallet = game.medals;
        int pendingBefore = PendingBonus;
        Require(pendingBefore == 0, "Unexpected bonus reservations before the isolated reservation check.");
        try
        {
            // Reproduce the old bug without deleting a single collectible or changing its physics.
            // Both bonus requests reserve the remaining two places; paid input must still take one.
            game.maxMedalsOnBoard = board + 2;
            game.DropBonusMedals(2);
            int reserved = PendingBonus;
            game.ThrowSingleMedal();
            Check("pendingBonusDoesNotBlockPaidMedal", reserved == 2 && paidEvents == beforeEvents + 1 && game.medals == wallet - 1);
            results["reservation"] = new { boardBefore = board, temporaryCap = board + 2, reserved,
                paidAfterReservation = paidEvents - beforeEvents, walletBefore = wallet, walletAfter = game.medals,
                boardAfter = game.CountBoardItems(false), pendingAfter = PendingBonus };
        }
        finally { game.maxMedalsOnBoard = realCap; }
        settleUntil = Time.time + 5f; phase = Phase.Settle;
    }

    private static void CheckClones()
    {
        Require(controller.stations != null && controller.stations.Length == 3 && controller.stations.All(s => s != null), "Three lottery stations required.");
        var red = controller.stations[0];
        var stationResults = new List<object>();
        for (int i = 0; i < 3; i++)
        {
            var current = controller.stations[i];
            var pockets = current.GetComponentsInChildren<MedalLotteryPocket>();
            bool connected = current.kind == (MedalJackpotKind)i && pockets.All(p => p.station == current);
            bool triggerPockets = pockets.Length == 8 && pockets.Count(p => p.jackpot) == 1 &&
                pockets.All(p => p.GetComponent<Collider>() != null && p.GetComponent<Collider>().isTrigger);
            bool same = SameGeometry(red, current);
            Check("eightConnectedPockets_" + current.kind, connected && triggerPockets);
            Check("matchesRedPhysicalGeometry_" + current.kind, same);
            stationResults.Add(new { kind = current.kind.ToString(), pockets = pockets.Length,
                jackpotPockets = pockets.Count(p => p.jackpot), colliders = current.GetComponentsInChildren<Collider>().Length,
                transforms = current.GetComponentsInChildren<Transform>().Length, sameGeometryAsRed = same });
        }
        results["lotteryClones"] = stationResults.ToArray();
    }

    private static bool SameGeometry(MedalBallLotteryStation a, MedalBallLotteryStation b)
    {
        var ta = a.GetComponentsInChildren<Transform>(); var tb = b.GetComponentsInChildren<Transform>();
        if (ta.Length != tb.Length) return false;
        for (int i = 1; i < ta.Length; i++)
        {
            if (ta[i].name != tb[i].name || (ta[i].localPosition - tb[i].localPosition).sqrMagnitude > .00001f ||
                (ta[i].localScale - tb[i].localScale).sqrMagnitude > .00001f) return false;
            if (ta[i] != a.rotor && Quaternion.Angle(ta[i].localRotation, tb[i].localRotation) > .01f) return false;
            var ca = ta[i].GetComponents<Collider>(); var cb = tb[i].GetComponents<Collider>();
            if (ca.Length != cb.Length) return false;
            for (int c = 0; c < ca.Length; c++) if (!SameCollider(ca[c], cb[c])) return false;
            var pa = ta[i].GetComponent<MedalLotteryPocket>(); var pb = tb[i].GetComponent<MedalLotteryPocket>();
            if ((pa == null) != (pb == null) || (pa != null && (pa.jackpot != pb.jackpot || pa.smallReward != pb.smallReward))) return false;
        }
        return a.radialGuide == b.radialGuide && a.launchVelocityRelativeToPoint == b.launchVelocityRelativeToPoint &&
            a.localLaunchVelocity == b.localLaunchVelocity && a.launchVelocityJitter == b.launchVelocityJitter &&
            a.launchPositionJitter == b.launchPositionJitter && a.guideLocalCenter == b.guideLocalCenter &&
            a.guideAcceleration == b.guideAcceleration && a.rotorDegreesPerSecond == b.rotorDegreesPerSecond;
    }

    private static bool SameCollider(Collider a, Collider b)
    {
        if (a.GetType() != b.GetType() || a.isTrigger != b.isTrigger || a.enabled != b.enabled) return false;
        if (a is BoxCollider box) { var other = (BoxCollider)b; return box.center == other.center && box.size == other.size; }
        if (a is SphereCollider sphere) { var other = (SphereCollider)b; return sphere.center == other.center && sphere.radius == other.radius; }
        if (a is CapsuleCollider capsule) { var other = (CapsuleCollider)b; return capsule.center == other.center && capsule.radius == other.radius && capsule.height == other.height && capsule.direction == other.direction; }
        if (a is MeshCollider mesh)
        {
            var other = (MeshCollider)b;
            return mesh.convex == other.convex && mesh.sharedMesh != null && other.sharedMesh != null &&
                mesh.sharedMesh.vertices.SequenceEqual(other.sharedMesh.vertices) && mesh.sharedMesh.triangles.SequenceEqual(other.sharedMesh.triangles);
        }
        return false;
    }

    private static void CheckUI()
    {
        Canvas.ForceUpdateCanvases();
        var scoreObjects = baseUI.GetComponentsInChildren<TMP_Text>(true).Where(t => t.name.IndexOf("Score", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        Check("scoreDisplayRemoved", scoreObjects.All(t => !t.gameObject.activeInHierarchy) &&
            (baseUI.scoreText == null || !baseUI.scoreText.gameObject.activeInHierarchy) &&
            (baseUI.scoreLabel == null || !baseUI.scoreLabel.gameObject.activeInHierarchy));
        Require(baseUI.medalsText != null && arcadeUI.jackpotTexts != null && arcadeUI.jackpotTexts.Length == 3, "Balance/JP text missing.");
        var root = baseUI.GetComponent<RectTransform>();
        var balance = NormalizedRect(baseUI.medalsText.rectTransform, root);
        var jp = arcadeUI.transform.Find("JPPanel") as RectTransform;
        Require(jp != null, "JP panel missing.");
        var jpBounds = NormalizedRect(jp, root);
        Check("balanceAtBottomLeft", balance.xMin < .15f && balance.xMax < .4f && balance.yMin < .25f && balance.yMax < .4f);
        Check("jackpotsAtBottomRight", jpBounds.xMin > .65f && jpBounds.xMax > .9f && jpBounds.yMin < .25f && jpBounds.yMax < .4f);
        Check("balanceShowsCurrentWallet", baseUI.medalsText.text == game.medals.ToString("D4"));
        string[] colors = { "赤", "青", "黄" };
        Check("allThreeJapaneseJackpotValuesShown", Enumerable.Range(0, 3).All(i =>
            arcadeUI.jackpotTexts[i] != null && arcadeUI.jackpotTexts[i].text == colors[i] + "JP " + controller.jackpotPools[i] + "枚"));
        var glyphFailures = new List<string>(); var english = new List<string>(); var textRecords = new List<object>();
        int japaneseCharacters = 0;
        foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
        {
            if (!text.gameObject.activeInHierarchy || !text.enabled || string.IsNullOrWhiteSpace(text.text)) continue;
            string plain = Regex.Replace(text.text, "<[^>]+>", "");
            foreach (Match token in Regex.Matches(plain, "[A-Za-z]{2,}"))
                if (token.Value != "JP") english.Add(text.name + ": " + token.Value);
            text.ForceMeshUpdate();
            foreach (char character in plain.Where(Japanese).Distinct())
                if (text.font == null || !text.font.HasCharacter(character, true, true)) glyphFailures.Add(text.name + ": U+" + ((int)character).ToString("X4"));
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                var character = text.textInfo.characterInfo[i];
                if (!Japanese(character.character)) continue;
                japaneseCharacters++;
                if (!character.isVisible || character.textElement == null || character.textElement.unicode != character.character)
                    glyphFailures.Add(text.name + ": unrendered/replaced U+" + ((int)character.character).ToString("X4"));
            }
            textRecords.Add(new { name = text.name, text = plain, font = text.font == null ? null : text.font.name,
                renderedCharacters = text.textInfo.characterCount });
        }
        Check("visibleTextLocalized", english.Count == 0);
        Check("japaneseGlyphsPresentInRenderedMeshes", japaneseCharacters > 20 && glyphFailures.Count == 0);
        results["hud"] = new { balance = RectData(balance), jackpotPanel = RectData(jpBounds), japaneseCharacters,
            englishWords = english.Distinct().ToArray(), glyphFailures = glyphFailures.Distinct().ToArray(), texts = textRecords.ToArray() };
    }
    private static bool Japanese(char c) => (c >= '\u3040' && c <= '\u30ff') || (c >= '\u3400' && c <= '\u9fff');
    private static Rect NormalizedRect(RectTransform item, RectTransform root)
    {
        Require(root != null && root.rect.width > 0 && root.rect.height > 0, "HUD root has no canvas dimensions.");
        var corners = new Vector3[4]; item.GetWorldCorners(corners);
        var points = corners.Select(root.InverseTransformPoint).ToArray();
        return Rect.MinMaxRect((points.Min(p => p.x) - root.rect.xMin) / root.rect.width,
            (points.Min(p => p.y) - root.rect.yMin) / root.rect.height,
            (points.Max(p => p.x) - root.rect.xMin) / root.rect.width,
            (points.Max(p => p.y) - root.rect.yMin) / root.rect.height);
    }
    private static object RectData(Rect r) => new { xMin = r.xMin, yMin = r.yMin, xMax = r.xMax, yMax = r.yMax };

    private static void Capture(string filename, string selection)
    {
        if (!Application.isBatchMode) return;
        capturing = true;
        try
        {
            MedalArcadeBatchCapture.Capture(filename, selection);
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-expansion-20261004", filename);
            results[filename] = File.Exists(path) && File.GetLastWriteTimeUtc(path) >= DateTime.Parse(SessionState.GetString(UtcKey, "")).ToUniversalTime();
            if (!(bool)results[filename]) captureIssues.Add("Capture did not create a fresh PNG: " + filename);
        }
        catch (Exception exception) { captureIssues.Add(filename + ": " + exception.Message); }
        finally { capturing = false; }
    }

    private static void Finish(bool stopPlay)
    {
        SessionState.SetBool(SessionKey, false);
        if (game != null) { game.OnMedalInserted -= CountPaid; if (initialized) game.maxMedalsOnBoard = realCap; }
        Application.runInBackground = SessionState.GetBool(BackgroundKey, originalBackground);
        if (initialized) Time.timeScale = originalTimeScale;
        results["timestampUtc"] = DateTime.UtcNow.ToString("O");
        results["startedUtc"] = SessionState.GetString(UtcKey, "");
        results["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        results["elapsedWallSeconds"] = Elapsed; results["playSeconds"] = Time.time;
        results["phase"] = phase.ToString(); results["totalPaidEvents"] = paidEvents; results["maxBoardMedals"] = maxCount;
        results["configuredCap"] = realCap; results["pusherLocalZ"] = new { min = pusherMin, max = pusherMax };
        results["samples"] = samples.ToArray(); results["finalBootReadiness"] = BootDiagnostics();
        results["success"] = errors.Count == 0 && phase == Phase.Complete;
        results["errors"] = errors.Distinct().ToArray(); results["editorServiceIssues"] = editorIssues.Distinct().ToArray();
        results["captureIssues"] = captureIssues.Distinct().ToArray();
        MedalPusherExpansionBuilder.WriteReport("revision-play-validation.json", results);
        Debug.Log("[MedalPusher] Revision Play check complete: " + ((bool)results["success"] ? "PASS" : "FAIL"));
        if (stopPlay)
        {
            EditorApplication.isPaused = false;
            if (Application.isBatchMode) EditorApplication.Exit((bool)results["success"] ? 0 : 1);
            else EditorApplication.isPlaying = false;
        }
    }
}
