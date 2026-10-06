using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Bounded runtime check of the production screen-ray click path and a ball-only prize board.</summary>
[InitializeOnLoad]
public static class MedalClickBallOnlyPlayCheck
{
    private const string Key = "MedalPusher.ClickBallOnlyCheck";
    private const string StartedKey = Key + ".Started";
    private const string UtcKey = Key + ".Utc";
    private const string BackgroundKey = Key + ".Background";
    private enum Phase { Boot, Negative, Sample, Falling, Hold, Complete }
    private static Phase phase;
    private static MedalPusherGame game;
    private static MedalInputHandler input;
    private static MedalSlotJackpotController controller;
    private static MedalArcadeSettings settings;
    private static PrizeDropManager manager;
    private static Camera camera;
    private static readonly Dictionary<string, object> checks = new Dictionary<string, object>();
    private static readonly List<string> errors = new List<string>();
    private static readonly List<object> samples = new List<object>();
    private static readonly List<object> rejectedCandidates = new List<object>();
    private static int sampleIndex, spawnEvents, insertedEvents, holdStage, holdSpawnBefore, holdWalletBefore, holdInsertedBefore;
    private static long holdPaidBefore;
    private static double phaseStarted;
    private static float fallStarted, holdNextAt, initialCoinY;
    private static GameObject currentCoin;
    private static MedalClickBallOnlyCollisionProbe probe;
    private static Vector3 target, spawnPosition, holdFirstTarget, holdSecondTarget;
    private static Vector2 screen;
    private static bool initialized, savedPersistence, fallingObserved;
    private static float savedTimeScale;
    private static string settingsPath, settingsHash, savedOverride, temporarySettingsPath;
    private static UnityEngine.Random.State savedRandom;

    static MedalClickBallOnlyPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayState;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Tools/Medal Pusher/Run Click And Ball Only Play Check")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play mode before the click check.");
        MedalPusherExpansionBuilder.Validate();
        checks.Clear(); errors.Clear(); samples.Clear(); rejectedCandidates.Clear();
        phase = Phase.Boot; initialized = false; game = null; input = null; controller = null; settings = null;
        currentCoin = null; spawnEvents = insertedEvents = sampleIndex = holdStage = 0;
        foreach (string other in new[] { "MedalPusher.PlayCheck", "MedalArcade.PlayCheck", "MedalPusher.RevisionCheck", "MedalPusher.UpperFlowCheck", "MedalPusher.IntegratedUpperFlowCheck" }) SessionState.SetBool(other, false);
        SessionState.SetFloat(StartedKey, (float)Now); SessionState.SetString(UtcKey, DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(BackgroundKey, Application.runInBackground); Application.runInBackground = true;
        SessionState.SetBool(Key, true); EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
    }

    [MenuItem("Tools/Medal Pusher/Inspect Click And Ball Only Play Check")]
    public static void Inspect()
    {
        MedalPusherExpansionBuilder.WriteReport("click-ball-only-play-state.json", new {
            timestampUtc = DateTime.UtcNow.ToString("O"), requested = SessionState.GetBool(Key, false),
            playing = EditorApplication.isPlaying, phase = phase.ToString(), elapsedWallSeconds = Elapsed,
            sampleIndex, spawnEvents, insertedEvents, holdStage, completedSamples = samples.Count, errors = errors.ToArray()
        });
    }

    private static double Now => EditorApplication.timeSinceStartup;
    private static double Elapsed => Now - SessionState.GetFloat(StartedKey, (float)Now);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Check(string name, bool condition) { checks[name] = condition; if (!condition) errors.Add("Failed: " + name); }
    private static object Pos(Vector3 value) => new { x = value.x, y = value.y, z = value.z };
    private static object Pixel(Vector2 value) => new { x = value.x, y = value.y };
    private static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    private static void ResetCooldown()
    {
        var field = typeof(MedalPusherGame).GetField("throwTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, "Paid input cooldown field missing."); field.SetValue(game, 0f);
    }
    private static void Enter(Phase next) { phase = next; phaseStarted = Now; }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (!SessionState.GetBool(Key, false) || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        if ((stack ?? "").Contains("Unity.AI.ModelSelector")) return;
        errors.Add(message);
    }
    private static void OnPlayState(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key, false)) { errors.Add("Play exited before completion."); Finish(false); }
    }
    private static void OnInserted() => insertedEvents++;
    private static void OnSpawn(GameObject coin) { spawnEvents++; currentCoin = coin; }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        Application.runInBackground = true;
        if (Elapsed > 120) { errors.Add("Click check exceeded 120 wall-clock seconds."); Finish(true); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        try
        {
            switch (phase)
            {
                case Phase.Boot:
                    Require(Elapsed < 25, "Runtime was not ready in 25 seconds.");
                    game = UnityEngine.Object.FindAnyObjectByType<MedalPusherGame>(); input = UnityEngine.Object.FindAnyObjectByType<MedalInputHandler>();
                    controller = UnityEngine.Object.FindAnyObjectByType<MedalSlotJackpotController>(); settings = UnityEngine.Object.FindAnyObjectByType<MedalArcadeSettings>();
                    if (Time.time < .5f || game == null || input == null || controller == null || settings == null || Camera.main == null) return;
                    Initialize(); Enter(Phase.Negative); break;
                case Phase.Negative:
                    TestRejections(); Enter(Phase.Sample); break;
                case Phase.Sample:
                    StartSample(); Enter(Phase.Falling); break;
                case Phase.Falling:
                    TickFalling(); break;
                case Phase.Hold:
                    TickHold(); break;
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(true); }
    }

    private static bool Green(GameObject prefab)
    {
        var renderer = prefab == null ? null : prefab.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterial == null) return false;
        var material = renderer.sharedMaterial; Color color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.color;
        return color.g > color.r * 1.4f && color.g > color.b * 1.1f;
    }
    private static void Initialize()
    {
        initialized = true; savedTimeScale = Time.timeScale; savedRandom = UnityEngine.Random.state; Time.timeScale = 1;
        settingsPath = settings.SavePath; settingsHash = Hash(settingsPath); savedPersistence = settings.persistSettings; savedOverride = settings.SettingsFileOverride;
        temporarySettingsPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-expansion-20261004", "settings-click-" + Guid.NewGuid().ToString("N") + ".json");
        settings.SettingsFileOverride = temporarySettingsPath; settings.persistSettings = false;
        Check("settingsUseIsolatedGuidOverride", settings.SavePath != settingsPath && settings.SavePath == temporarySettingsPath);
        camera = Camera.main; var view = UnityEngine.Object.FindAnyObjectByType<MedalPusherCameraView>();
        if (view != null) { view.overview = false; view.ReleaseStation(); }
        input.enabled = false; controller.enabled = false; game.StopThrowing(); game.SetSettingsOpen(false);
        manager = game.GetComponent<PrizeDropManager>(); Require(manager != null, "Prize manager missing."); manager.enabled = false;
        game.OnMedalInserted += OnInserted; game.OnPaidMedalSpawned += OnSpawn;
        var items = game.itemsRoot.GetComponentsInChildren<MedalItem>(true);
        Check("boardHasNoBoxTrophyOrNonBallPrize", items.All(i => !i.isPrize || i.isBall));
        Check("legacyPrizePrefabListIsEmpty", game.prizePrefabs == null || game.prizePrefabs.Length == 0);
        Check("legacyPrizeSpawnerHasNoTypesOrMinimum", !manager.spawnGenericPrizes && (manager.prizeTypes == null || manager.prizeTypes.Length == 0) && manager.minPrizesOnBoard == 0);
        Check("greenBoardBallPrefabsRemain", controller.ballPrefabs != null && controller.ballPrefabs.Length == 3 && controller.ballPrefabs.All(p => p != null && p.GetComponent<MedalItem>().isBall && Green(p)));
        Check("boardStartsWithThreeGreenBalls", items.Count(i => i.isBall && !i.collected) == 3 && items.Where(i => i.isBall).All(i => Green(i.gameObject)));
        Check("threePhysicalSlotPocketsRemain", game.medalInlets != null && game.medalInlets.Length == 3 && game.medalInlets.All(t => t != null) &&
            game.GetComponentsInChildren<MedalSlotPocket>(true).Length == 3 && game.GetComponentsInChildren<MedalSlotPocket>(true).All(p => p.controller == controller && p.GetComponent<Collider>() != null && p.GetComponent<Collider>().isTrigger));
        var arcade = UnityEngine.Object.FindAnyObjectByType<MedalArcadeUI>();
        Check("threeInletButtonAndLampBindingsAreRemoved", arcade != null && (arcade.inletButtons == null || arcade.inletButtons.Length == 0) && (arcade.inletLights == null || arcade.inletLights.Length == 0));
        Check("leftCenterRightSelectorObjectsAreRemoved", !UnityEngine.Object.FindObjectsByType<Transform>().Any(t => t.name.StartsWith("InletButton_", StringComparison.Ordinal)));
        Check("leftCenterRightSelectorTextIsAbsent", !UnityEngine.Object.FindObjectsByType<TMP_Text>().Any(t => t.isActiveAndEnabled && (t.text.Contains("1：左") || t.text.Contains("2：中央") || t.text.Contains("3：右"))));
        checks["initialBoard"] = new { medals = items.Count(i => !i.isPrize), balls = items.Count(i => i.isBall), otherPrizes = items.Count(i => i.isPrize && !i.isBall), cameraPosition = Pos(camera.transform.position), cameraPixelWidth = camera.pixelWidth, cameraPixelHeight = camera.pixelHeight };
        // Remove only transient Play objects to expose the real deck/plate for contact evidence.
        foreach (var item in items) { item.collected = true; game.UnregisterItem(item); UnityEngine.Object.Destroy(item.gameObject); }
        int before = game.CountBoardItems(true); manager.SpawnPrize();
        Check("callingLegacyPrizeSpawnerCannotRecreateBoxes", game.CountBoardItems(true) == before && !game.itemsRoot.GetComponentsInChildren<MedalItem>().Any(i => i.isPrize && !i.isBall));
    }

    private static Vector2 FindTarget(int index)
    {
        float x = new[] { -2f, 0f, 2f }[index % 3];
        bool upper = index >= 3;
        Vector3 plate = game.transform.InverseTransformPoint(game.pusherBody.position);
        float y = upper ? game.pusherBody.GetComponent<Collider>().bounds.max.y - game.transform.position.y : .001f;
        foreach (float dz in new[] { 0f, .2f, -.2f, .4f, -.4f })
        {
            // Sample behind the slot rims so their valid medal contact does not
            // replace this check's intended deck/plate collision evidence.
            float z = upper ? plate.z + .6f + dz : -2f + dz;
            if (upper && (z < 1.5f || Math.Abs(z - plate.z) > .65f)) continue;
            Vector3 local = new Vector3(x, y, z);
            Vector3 projected = camera.WorldToScreenPoint(game.transform.TransformPoint(local));
            Vector2 pixel = new Vector2(projected.x, projected.y);
            if (projected.z > 0 && projected.x >= 0 && projected.x < camera.pixelWidth && projected.y >= 0 && projected.y < camera.pixelHeight && input.TryAimAtScreenPosition(pixel))
            {
                target = game.CurrentMedalDropLocalPosition;
                Check("cameraRayMapsTargetXZ_sample" + index, Math.Abs(target.x - local.x) < .015f && Math.Abs(target.z - local.z) < .015f);
                return pixel;
            }
            rejectedCandidates.Add(new { index, requestedLocal = Pos(local), pixel = Pixel(pixel) });
        }
        throw new InvalidOperationException("No visible unblocked " + (upper ? "upper plate" : "front deck") + " target at X = " + x + ".");
    }
    private static void Rejected(string name, Vector2 pixel)
    {
        Vector3 before = game.CurrentMedalDropLocalPosition; int wallet = game.medals, inserts = insertedEvents, spawns = spawnEvents; long paid = game.TotalPaidMedals;
        ResetCooldown(); bool accepted = input.TryInsertAtScreenPosition(pixel);
        Check(name, !accepted && game.medals == wallet && game.TotalPaidMedals == paid && insertedEvents == inserts && spawnEvents == spawns && game.CurrentMedalDropLocalPosition == before);
    }
    private static void TestRejections()
    {
        screen = FindTarget(1);
        Vector3 clicked = game.CurrentMedalDropLocalPosition;
        game.SelectLeftInlet(); game.SelectCenterInlet(); game.SelectRightInlet();
        Check("legacyInletSelectionCannotChangeClickedDropPosition", game.CurrentMedalDropLocalPosition == clicked);
        var button = UnityEngine.Object.FindObjectsByType<RectTransform>().FirstOrDefault(t => t.name == "SettingsButton");
        Require(button != null, "Settings UI button missing."); Canvas.ForceUpdateCanvases();
        Rejected("settingsButtonClickDoesNotInsertOrChangeAim", RectTransformUtility.WorldToScreenPoint(null, button.TransformPoint(button.rect.center)));
        Rejected("negativeScreenCoordinateDoesNotInsert", new Vector2(-40f, -40f));
        Rejected("outsideScreenCoordinateDoesNotInsert", new Vector2(camera.pixelWidth + 40f, camera.pixelHeight + 40f));
        Vector3 outside = camera.WorldToScreenPoint(game.transform.TransformPoint(new Vector3(12f, .1f, -2f)));
        Rejected("rayOutsideBoardDoesNotInsert", new Vector2(outside.x, outside.y));
        game.SetSettingsOpen(true); Rejected("modalBlocksValidBoardClick", screen); game.SetSettingsOpen(false);
    }
    private static void StartSample()
    {
        int wallet = game.medals, inserts = insertedEvents, spawns = spawnEvents; long paid = game.TotalPaidMedals;
        screen = FindTarget(sampleIndex); Vector3 aim = game.CurrentMedalDropLocalPosition;
        Check("aimDoesNotSpendOrEmitEvents_sample" + sampleIndex, game.medals == wallet && game.TotalPaidMedals == paid && insertedEvents == inserts && spawnEvents == spawns);
        ResetCooldown(); currentCoin = null; bool accepted = input.TryInsertAtScreenPosition(screen);
        Check("clickSpendsOneAndEmitsEachEventOnce_sample" + sampleIndex, accepted && game.medals == wallet - 1 && game.TotalPaidMedals == paid + 1 && insertedEvents == inserts + 1 && spawnEvents == spawns + 1);
        Require(currentCoin != null, "Paid click did not expose its actual coin.");
        Check("lastInsertedMedalMatchesSpawnEvent_sample" + sampleIndex, currentCoin == game.LastInsertedMedal);
        spawnPosition = game.transform.InverseTransformPoint(currentCoin.transform.position); target = aim;
        var body = currentCoin.GetComponent<Rigidbody>(); var item = currentCoin.GetComponent<MedalItem>();
        Check("paidCoinIsDynamicAndUncredited_sample" + sampleIndex, body != null && !body.isKinematic && body.useGravity && item != null && !item.isPrize && !item.isBall && !item.payoutAlreadyCredited);
        Check("spawnUsesExactClickedXZWithoutScatter_sample" + sampleIndex, Math.Abs(spawnPosition.x - aim.x) < .001f && Math.Abs(spawnPosition.z - aim.z) < .001f && spawnPosition.y > aim.y + .3f);
        initialCoinY = currentCoin.transform.position.y; fallStarted = Time.fixedTime; fallingObserved = false;
        probe = currentCoin.AddComponent<MedalClickBallOnlyCollisionProbe>();
        Check("sameFrameSecondClickIsThrottled_sample" + sampleIndex, !input.TryInsertAtScreenPosition(screen) && game.medals == wallet - 1 && spawnEvents == spawns + 1 && insertedEvents == inserts + 1);
    }
    private static void TickFalling()
    {
        if (currentCoin != null && currentCoin.transform.position.y < initialCoinY - .08f) fallingObserved = true;
        if (probe != null && probe.Contacted)
        {
            Check("coinActuallyFallsUnderPhysics_sample" + sampleIndex, fallingObserved && Time.fixedTime > fallStarted);
            var field = game.transform.Find("GeneratedPlayfield/PusherBoard");
            Check("coinActuallyContactsBoardOrMovingPlate_sample" + sampleIndex, probe.ContactCollider != null &&
                (probe.ContactCollider == game.pusherBody.GetComponent<Collider>() ||
                 (game.sideHoleMainDeck != null && probe.ContactCollider == game.sideHoleMainDeck.GetComponent<Collider>()) ||
                 (field != null && probe.ContactCollider == field.GetComponent<Collider>()) ||
                 (game.sideHoleDeckStrips != null && game.sideHoleDeckStrips.Any(t => t != null && probe.ContactCollider == t.GetComponent<Collider>()))));
            samples.Add(new { index = sampleIndex, targetLocal = Pos(target), screenPosition = Pixel(screen), spawnLocal = Pos(spawnPosition), dynamicFall = fallingObserved, collider = probe.ContactName, contactPosition = Pos(probe.ContactPosition), contactDelaySeconds = Time.fixedTime - fallStarted });
            game.UnregisterItem(currentCoin.GetComponent<MedalItem>()); UnityEngine.Object.Destroy(currentCoin); currentCoin = null;
            sampleIndex++;
            if (sampleIndex < 6) Enter(Phase.Sample);
            else { holdStage = 0; Enter(Phase.Hold); }
            return;
        }
        Require(Now - phaseStarted < 5, "A clicked coin did not reach the real deck/plate within five seconds.");
    }
    private static void TickHold()
    {
        if (holdStage == 0)
        {
            FindTarget(0); holdFirstTarget = game.CurrentMedalDropLocalPosition; holdSpawnBefore = spawnEvents; holdInsertedBefore = insertedEvents; holdPaidBefore = game.TotalPaidMedals; holdWalletBefore = game.medals;
            ResetCooldown(); game.StartThrowing(); holdStage = 1; return;
        }
        if (holdStage == 1)
        {
            if (spawnEvents == holdSpawnBefore) return;
            game.StopThrowing();
            Vector3 spawned = game.transform.InverseTransformPoint(currentCoin.transform.position);
            Check("heldInputFirstCoinUsesFirstAimOnce", spawnEvents == holdSpawnBefore + 1 && insertedEvents == holdInsertedBefore + 1 && game.TotalPaidMedals == holdPaidBefore + 1 && game.medals == holdWalletBefore - 1 && Math.Abs(spawned.x - holdFirstTarget.x) < .01f && Math.Abs(spawned.z - holdFirstTarget.z) < .01f);
            FindTarget(2); holdSecondTarget = game.CurrentMedalDropLocalPosition; game.StartThrowing(); holdNextAt = Time.time + game.throwInterval + .15f; holdStage = 2; return;
        }
        if (holdStage == 2)
        {
            if (spawnEvents == holdSpawnBefore + 1) { Require(Time.time < holdNextAt + 2, "Held input did not emit its second coin."); return; }
            game.StopThrowing(); Vector3 spawned = game.transform.InverseTransformPoint(currentCoin.transform.position);
            Check("heldInputNextCoinFollowsUpdatedAimExactlyOnce", spawnEvents == holdSpawnBefore + 2 && insertedEvents == holdInsertedBefore + 2 && game.TotalPaidMedals == holdPaidBefore + 2 && game.medals == holdWalletBefore - 2 && Math.Abs(spawned.x - holdSecondTarget.x) < .01f && Math.Abs(spawned.z - holdSecondTarget.z) < .01f);
            checks["heldInputPositions"] = new { first = Pos(holdFirstTarget), second = Pos(holdSecondTarget), finalSpawn = Pos(spawned) };
            holdNextAt = Time.time + .5f; holdStage = 3; return;
        }
        if (Time.time < holdNextAt) return;
        Check("stoppingHeldInputPreventsFurtherPaidCoins", spawnEvents == holdSpawnBefore + 2 && insertedEvents == holdInsertedBefore + 2 && game.TotalPaidMedals == holdPaidBefore + 2);
        Check("sixClicksCoverLeftCenterRightDeckAndMovingPlate", samples.Count == 6);
        Check("noNonBallPrizeAppearedDuringPlay", !game.itemsRoot.GetComponentsInChildren<MedalItem>().Any(i => i.isPrize && !i.isBall));
        phase = Phase.Complete; Finish(true);
    }

    private static void Finish(bool stopPlay)
    {
        SessionState.SetBool(Key, false);
        if (initialized)
        {
            if (game != null) { game.StopThrowing(); game.SetSettingsOpen(false); game.OnMedalInserted -= OnInserted; game.OnPaidMedalSpawned -= OnSpawn; }
            if (settings != null) { settings.persistSettings = savedPersistence; settings.SettingsFileOverride = savedOverride; }
            Check("realSettingsFileRemainedUntouched", settingsHash == Hash(settingsPath));
            checks["settingsFileHashes"] = new { before = settingsHash, after = Hash(settingsPath) };
            checks["isolatedSettingsPath"] = temporarySettingsPath;
            Time.timeScale = savedTimeScale; UnityEngine.Random.state = savedRandom;
        }
        Application.runInBackground = SessionState.GetBool(BackgroundKey, false);
        checks["timestampUtc"] = DateTime.UtcNow.ToString("O"); checks["startedUtc"] = SessionState.GetString(UtcKey, "");
        checks["elapsedWallSeconds"] = Elapsed; checks["phase"] = phase.ToString(); checks["samples"] = samples.ToArray();
        checks["rejectedCandidatePositions"] = rejectedCandidates.ToArray(); checks["paidSpawnEvents"] = spawnEvents; checks["insertedEvents"] = insertedEvents;
        checks["errors"] = errors.Distinct().ToArray(); checks["success"] = errors.Count == 0 && phase == Phase.Complete;
        MedalPusherExpansionBuilder.WriteReport("click-ball-only-play-validation.json", checks);
        Debug.Log("[MedalPusher] Click and ball-only Play check: " + ((bool)checks["success"] ? "PASS" : "FAIL"));
        if (stopPlay) { EditorApplication.isPaused = false; EditorApplication.isPlaying = false; }
    }
}

/// <summary>Transient actual collision evidence, used only during a Play check.</summary>
public sealed class MedalClickBallOnlyCollisionProbe : MonoBehaviour
{
    [NonSerialized] public bool Contacted;
    [NonSerialized] public string ContactName;
    [NonSerialized] public Collider ContactCollider;
    [NonSerialized] public Vector3 ContactPosition;
    private void OnCollisionEnter(Collision collision)
    {
        if (Contacted || collision.contactCount == 0) return;
        Contacted = true; ContactCollider = collision.collider; ContactName = collision.collider.name; ContactPosition = collision.GetContact(0).point;
    }
}
