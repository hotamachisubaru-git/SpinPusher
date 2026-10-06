using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MedalPusherExpansionBuilder
{
    private const string Root = "Assets/Synaptic_Generated/MedalPusher";

    [MenuItem("Tools/Medal Pusher/Build Slots And Three Jackpots")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != MedalPusherSceneBuilder.ScenePath) throw new InvalidOperationException("Open the medal pusher game scene.");
        var game = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>();
        if (game == null) throw new InvalidOperationException("Build the original medal pusher first.");
        ApplyToScene(scene, game);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("[MedalPusher] Slots, ball-only board and three physical jackpot stations saved.");
    }

    [MenuItem("Tools/Medal Pusher/Apply Click Placement And Ball Only")]
    public static void ApplyClickPlacementAndBallOnly()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != MedalPusherSceneBuilder.ScenePath) throw new InvalidOperationException("Open the medal pusher game scene.");
        var game = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>();
        if (game == null) throw new InvalidOperationException("Medal pusher game missing.");
        ApplyClickPlacementAndBallOnlyToScene(scene, game);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Validate();
        Debug.Log("[MedalPusher] Click-placement HUD and ball-only board saved without rebuilding lottery geometry.");
    }

    [MenuItem("Tools/Medal Pusher/Apply Slot And Upper Recovery")]
    public static void ApplySlotAndUpperRecovery() => ApplyClickPlacementAndBallOnly();

    public static void ApplyClickPlacementAndBallOnlyToScene(Scene scene, MedalPusherGame game)
    {
        var controller = game != null ? game.GetComponent<MedalSlotJackpotController>() : null;
        if (controller == null || game.gameObject.scene != scene) throw new InvalidOperationException("Controller/game scene connection missing.");
        ApplyBallOnlyContent(game);
        BuildUI(game, controller);
        MedalArcadeSettingsBuilder.Build(UnityEngine.Object.FindFirstObjectByType<MedalPusherUI>(), game, controller);
        if (controller.upperStation != null)
        {
            controller.upperStation.guideAcceleration = .85f;
            controller.upperStation.upperMinimumHorizontalSpeed = 1.35f;
            controller.upperStation.upperSpeedMaintenanceAcceleration = 1.7f;
            controller.upperStation.upperStallSpeed = .22f;
            controller.upperStation.upperStallDuration = 1.35f;
            controller.upperStation.upperStallDisplacement = .12f;
            var title = controller.upperStation.transform.Find("DrawTitle");
            if (title != null) controller.upperStation.upperWinText = title.GetComponent<TMP_Text>();
            if (controller.upperStation.upperWinText != null) controller.upperStation.upperWinText.text = "00WIN（枚獲得）";
        }
        var view = UnityEngine.Object.FindFirstObjectByType<MedalPusherCameraView>();
        if (view != null) { view.overview = false; view.ReleaseStation(); view.ApplyView(); }
    }

    private static void ApplyBallOnlyContent(MedalPusherGame game)
    {
        game.prizePrefabs = Array.Empty<GameObject>();
        var manager = game.GetComponent<PrizeDropManager>() ?? game.gameObject.AddComponent<PrizeDropManager>();
        manager.spawnGenericPrizes = false;
        manager.minPrizesOnBoard = 0;
        manager.maxPrizesOnBoard = 0;
        manager.prizeTypes = Array.Empty<PrizeDropManager.PrizeType>();
        if (game.itemsRoot != null)
            foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>(true))
                if (item.isPrize && !item.isBall) UnityEngine.Object.DestroyImmediate(item.gameObject);
        EditorUtility.SetDirty(game);
        EditorUtility.SetDirty(manager);
    }

    public static void ApplyToScene(Scene scene, MedalPusherGame game)
    {
        MedalJapaneseFontBuilder.GetFont();
        var field = game.transform.Find("GeneratedPlayfield");
        if (field == null) throw new InvalidOperationException("Generated playfield missing.");
        if (field.Find("TreasureCabinetDecor") == null) TreasureCabinetDecoration.Build(field);
        var controller = game.GetComponent<MedalSlotJackpotController>() ?? game.gameObject.AddComponent<MedalSlotJackpotController>();
        controller.game = game;
        game.maxMedalsOnBoard = 1024;
        controller.medalsPerSpin = 3;
        controller.slotSpinDuration = 1.4f;
        controller.jackpotPools = new[] { 150, 250, 500 };
        controller.stations = MedalLotteryStationBuilder.BuildStations(field);
        controller.upperStation = MedalLotteryStationBuilder.BuildUpperStation(field);
        controller.ballPrefabs = CreateBoardBalls();
        MedalPayoutBuilder.Build(field, game);
        var settings = game.GetComponent<MedalArcadeSettings>() ?? game.gameObject.AddComponent<MedalArcadeSettings>();
        game.maxPrizesOnBoard = 12;
        ApplyBallOnlyContent(game);

        MedalSlotPocketLayout.Build(game, controller);
        LocalizeCabinet(field);
        // Keep medals; only lottery balls are populated as board prizes.
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
            if (item.isBall) UnityEngine.Object.DestroyImmediate(item.gameObject);
        for (int i = 0; i < 3; i++)
        {
            var ball = (GameObject)PrefabUtility.InstantiatePrefab(controller.ballPrefabs[i], scene);
            ball.name = "BoardBall_" + ((MedalJackpotKind)i);
            ball.transform.SetParent(game.itemsRoot, false);
            ball.transform.localPosition = new Vector3((i - 1) * 2.4f, .7f, -3.25f);
        }
        BuildUI(game, controller);
        settings.ApplyToGame(true);
        MedalArcadeSettingsBuilder.Build(UnityEngine.Object.FindFirstObjectByType<MedalPusherUI>(), game, controller);
        EditorUtility.SetDirty(game);
        EditorUtility.SetDirty(controller);
    }

    private static GameObject[] CreateBoardBalls()
    {
        Directory.CreateDirectory(Root + "/Prefabs");
        AssetDatabase.Refresh();
        string[] names = { "Ruby", "Sapphire", "Amber" };
        var result = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = names[i] + "LotteryBall";
            go.tag = "Prize";
            go.transform.localScale = Vector3.one * .62f;
            go.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MedalLotteryStationBuilder.GreenMaterialPath);
            go.GetComponent<Collider>().sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Root + "/Materials/MedalPhysics.physicMaterial");
            var body = go.AddComponent<Rigidbody>();
            body.mass = .08f;
            body.angularDamping = .7f;
            body.linearDamping = .1f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var item = go.AddComponent<MedalItem>();
            item.isPrize = true; item.isBall = true; item.ballKind = (MedalJackpotKind)i; item.pointValue = 0; item.displayName = "緑の抽選ボール";
            result[i] = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + names[i] + "LotteryBall.prefab");
            UnityEngine.Object.DestroyImmediate(go);
        }
        return result;
    }

    private static void BuildUI(MedalPusherGame game, MedalSlotJackpotController controller)
    {
        var baseUI = UnityEngine.Object.FindFirstObjectByType<MedalPusherUI>();
        if (baseUI == null) throw new InvalidOperationException("Game UI missing.");
        ConfigureBaseHUD(baseUI);
        var old = baseUI.transform.Find("ArcadeExpansion");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var rootGo = new GameObject("ArcadeExpansion", typeof(RectTransform));
        rootGo.transform.SetParent(baseUI.transform, false);
        var root = rootGo.transform;
        var rect = rootGo.GetComponent<RectTransform>();
        SetRect(rect, Vector2.zero, Vector2.one);
        var ui = root.gameObject.AddComponent<MedalArcadeUI>();
        ui.game = game; ui.controller = controller;
        ui.inletButtons = Array.Empty<Image>();
        ui.inletLights = Array.Empty<Renderer>();
        ui.cameraView = UnityEngine.Object.FindFirstObjectByType<MedalPusherCameraView>();
        ui.worldReelsText = game.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "CabinetTitle");
        if (ui.worldReelsText != null)
        {
            ui.worldReelsText.text = "− | − | −";
            ui.worldReelsText.color = Color.white;
            ui.worldReelsText.font = MedalJapaneseFontBuilder.GetFont();
            ui.worldReelsText.fontStyle = FontStyles.Bold;
            ui.worldReelsText.enableAutoSizing = true;
            ui.worldReelsText.fontSizeMin = 2.5f; ui.worldReelsText.fontSizeMax = 5.5f;
            ui.worldReelsText.textWrappingMode = TextWrappingModes.NoWrap;
        }
        ui.towerJackpotValues = new TMP_Text[4];
        ui.towerJackpotKinds = new[] { -1, 0, 2, 1 };
        int[] displayedPools = controller.jackpotPools != null && controller.jackpotPools.Length == 3
            ? controller.jackpotPools : new[] { 150, 250, 500 };
        var tower = game.transform.Find("GeneratedPlayfield/TreasureCabinetDecor/FourSidedTreasureTower");
        for (int i = 0; i < 4 && tower != null; i++)
        {
            var face = tower.Find("DisplayFace_" + i);
            var number = face == null ? null : face.Find("JackpotTitle");
            if (number == null) continue;
            var text = number.GetComponent<TMP_Text>();
            ui.towerJackpotValues[i] = text;
            text.enableAutoSizing = true; text.fontSizeMin = 2.5f; text.fontSizeMax = i == 0 ? 4.2f : 6.1f;
            if (i == 0)
            {
                text.rectTransform.localPosition = new Vector3(0, 6.15f, -3.09f);
                text.rectTransform.sizeDelta = new Vector2(5.2f, 1.65f);
                text.text = "<color=#FF5E6E>赤 " + displayedPools[0] + "枚</color>\n<color=#54BBFF>青 "
                    + displayedPools[1] + "枚</color>\n<color=#FFD448>黄 " + displayedPools[2] + "枚</color>";
                var caption = face.Find("ScreenCaption"); if (caption != null) caption.GetComponent<TMP_Text>().text = "";
                var divider = face.Find("ColorDivider"); if (divider != null) divider.localPosition = new Vector3(0, 6.99f, -3.074f);
            }
            else text.text = displayedPools[ui.towerJackpotKinds[i]] + "枚";
        }
        ui.reelsText = Text("SlotReels", root, "−  |  −  |  −", new Vector2(.04f, .916f), new Vector2(.36f, .978f), 44, Color.white);
        ui.reelsText.alignment = TextAlignmentOptions.Center;
        ui.reelsText.fontStyle = FontStyles.Bold;
        ui.reelsText.enableAutoSizing = true; ui.reelsText.fontSizeMin = 24; ui.reelsText.fontSizeMax = 44;
        ui.reelsText.textWrappingMode = TextWrappingModes.NoWrap;
        ui.spinMeterText = Text("SpinMeter", root, "", new Vector2(.04f, .887f), new Vector2(.36f, .918f), 18, Color.white);
        ui.spinMeterText.alignment = TextAlignmentOptions.Center;
        Panel("UpperWinPanel", root, new Vector2(.025f, .75f), new Vector2(.29f, .845f));
        ui.upperWinText = Text("UpperWinText", root, "00WIN（枚獲得）\n100WIN超で3色開放", new Vector2(.04f, .758f), new Vector2(.278f, .837f), 20, Color.white);
        ui.payoutRemainingText = Text("PayoutRemaining", root, "PAYOUT  00枚", new Vector2(.04f, .155f), new Vector2(.24f, .192f), 24, new Color(1, .85f, .3f));
        ui.payoutRemainingText.enableAutoSizing = true; ui.payoutRemainingText.fontSizeMin = 16; ui.payoutRemainingText.fontSizeMax = 24;
        Panel("JPPanel", root, new Vector2(.77f, .025f), new Vector2(.975f, .225f));
        ui.jackpotTexts = new TMP_Text[3];
        for (int i = 0; i < 3; i++)
        {
            ui.jackpotTexts[i] = Text("JP_" + (MedalJackpotKind)i, root, "", new Vector2(.783f, .163f - i * .06f), new Vector2(.963f, .213f - i * .06f), 25, Color.white);
            ui.jackpotTexts[i].enableAutoSizing = true;
            ui.jackpotTexts[i].fontSizeMin = 14;
            ui.jackpotTexts[i].fontSizeMax = 25;
            ui.jackpotTexts[i].textWrappingMode = TextWrappingModes.NoWrap;
        }
        ui.lotteryStatusPanel = Panel("DrawStatus", root, new Vector2(.305f, .785f), new Vector2(.975f, .845f)).gameObject;
        ui.statusText = Text("DrawStatusText", root, "", new Vector2(.315f, .793f), new Vector2(.962f, .837f), 22, Color.white);
        ui.statusText.alignment = TextAlignmentOptions.Center;
        ui.statusText.enableAutoSizing = true;
        ui.statusText.fontSizeMin = 12;
        ui.statusText.fontSizeMax = 22;
        ui.statusText.textWrappingMode = TextWrappingModes.NoWrap;
        var visibilitySettings = game.GetComponent<MedalArcadeSettings>();
        bool showStatus = visibilitySettings != null && visibilitySettings.showLotteryStatus;
        ui.lotteryStatusPanel.SetActive(showStatus);
        ui.statusText.gameObject.SetActive(showStatus);
        ui.stationMeters = new TMP_Text[3];
        for (int i = 0; i < controller.stations.Length; i++)
        {
            var station = controller.stations[i];
            var label = station.transform.parent.Find("StationDisplaySubtitle");
            if (label != null) ui.stationMeters[i] = label.GetComponent<TMP_Text>();
        }
        var flash = Panel("CelebrationFlash", root, Vector2.zero, Vector2.one);
        ui.celebrationFlash = flash.GetComponent<Image>();
        ui.celebrationFlash.color = Color.clear;
        ui.payoutBanner = Text("PayoutBanner", root, "", new Vector2(.23f, .35f), new Vector2(.77f, .58f), 54, Color.white);
        ui.payoutBanner.alignment = TextAlignmentOptions.Center;
        ui.payoutBanner.fontStyle = FontStyles.Bold;
        ui.payoutBanner.gameObject.SetActive(false);
        ui.confetti = new RectTransform[28];
        for (int i = 0; i < ui.confetti.Length; i++)
        {
            var piece = Panel("Confetti_" + i, root, Vector2.zero, Vector2.zero).GetComponent<RectTransform>();
            piece.sizeDelta = new Vector2(10 + i % 3 * 3, 19 + i % 4 * 3);
            piece.gameObject.SetActive(false);
            ui.confetti[i] = piece;
        }
        var controls = baseUI.transform.Find("Controls");
        if (controls != null) controls.GetComponent<TMP_Text>().text = "盤面をクリック・長押しで投入　V：全景";
    }

    private static void ConfigureBaseHUD(MedalPusherUI ui)
    {
        foreach (string name in new[] { "ScoreLabel", "ScoreValue", "ControlsPanel", "Title", "ComboPanel" })
        {
            var old = ui.transform.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        ui.scoreLabel = null; ui.scoreText = null;
        ui.comboPanel = null; ui.comboText = null; ui.comboLabel = null;
        PoseUI(ui.transform, "Header", new Vector2(.025f, .885f), new Vector2(.975f, .98f));
        PoseUI(ui.transform, "Controls", new Vector2(.385f, .9f), new Vector2(.96f, .968f));
        var panel = ui.transform.Find("MedalsPanel");
        if (panel == null) panel = Panel("MedalsPanel", ui.transform, new Vector2(.025f, .025f), new Vector2(.245f, .205f));
        PoseUI(ui.transform, "MedalsPanel", new Vector2(.025f, .025f), new Vector2(.245f, .205f));
        panel.SetAsFirstSibling();
        PoseUI(ui.transform, "MedalLabel", new Vector2(.04f, .095f), new Vector2(.23f, .135f), "メダル残高");
        PoseUI(ui.transform, "MedalValue", new Vector2(.04f, .032f), new Vector2(.23f, .097f));
        ui.medalsLabel = ui.transform.Find("MedalLabel").GetComponent<TextMeshProUGUI>();
        PoseUI(ui.transform, "ViewButton", new Vector2(.365f, .035f), new Vector2(.495f, .105f));
        PoseUI(ui.transform, "ViewButton/ViewLabel", Vector2.zero, Vector2.one, "全景");
        PoseUI(ui.transform, "DropMedalButton", new Vector2(.515f, .035f), new Vector2(.745f, .105f));
        PoseUI(ui.transform, "DropMedalButton/DropLabel", Vector2.zero, Vector2.one, "メダル投入");
        PoseUI(ui.transform, "JackpotPanel/JackpotText", Vector2.zero, Vector2.one, "JACKPOT");
        foreach (var text in ui.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = MedalJapaneseFontBuilder.GetFont();
            if (text.name == "Controls") text.fontSize = 20;
        }
    }
    private static void PoseUI(Transform root, string path, Vector2 min, Vector2 max, string value = null)
    {
        var target = root.Find(path);
        if (target == null) return;
        SetRect(target.GetComponent<RectTransform>(), min, max);
        if (value != null) target.GetComponent<TMP_Text>().text = value;
    }
    private static void LocalizeCabinet(Transform field)
    {
        foreach (var text in field.GetComponentsInChildren<TMP_Text>(true))
        {
            text.font = MedalJapaneseFontBuilder.GetFont();
            switch (text.name)
            {
                case "CabinetTitle": text.text = "− | − | −"; break;
                case "TreasureTitle": text.text = "宝探し"; break;
                case "JackpotTitle": text.text = "150枚"; break;
                case "ScreenCaption": text.text = "赤 ／ 青 ／ 黄"; break;
                case "StationDisplayTitle":
                    text.text = text.transform.parent.name == "Station_West" ? "赤の抽選機" : text.transform.parent.name == "Station_East" ? "青の抽選機" : "黄の抽選機";
                    break;
                case "StationDisplaySubtitle": text.text = "ボール抽選"; break;
                default:
                    text.text = text.text.Replace("TREASURE", "宝探し").Replace("大当たり", "JACKPOT")
                        .Replace("RUBY", "赤").Replace("SAPPHIRE", "青").Replace("AMBER", "黄");
                    break;
            }
        }
    }

    [MenuItem("Tools/Medal Pusher/Validate Slots And Jackpots")]
    public static void Validate()
    {
        var game = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>();
        var controller = UnityEngine.Object.FindFirstObjectByType<MedalSlotJackpotController>();
        if (game == null || controller == null || controller.game != game) throw new InvalidOperationException("Controller/game connection missing.");
        if (game.prizePrefabs == null || game.prizePrefabs.Length != 0 || game.itemsRoot == null || game.itemsRoot.GetComponentsInChildren<MedalItem>(true).Any(i => i.isPrize && !i.isBall)) throw new InvalidOperationException("Board prizes must consist only of lottery balls.");
        var manager = game.GetComponent<PrizeDropManager>();
        if (manager == null || manager.spawnGenericPrizes || manager.minPrizesOnBoard != 0 || manager.maxPrizesOnBoard != 0 || manager.prizeTypes == null || manager.prizeTypes.Length != 0) throw new InvalidOperationException("Generic prize spawning must stay disabled.");
        var arcadeUI = UnityEngine.Object.FindFirstObjectByType<MedalArcadeUI>();
        if (arcadeUI == null || arcadeUI.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("InletButton_")) || (arcadeUI.inletButtons != null && arcadeUI.inletButtons.Length != 0)) throw new InvalidOperationException("Left/center/right selection controls must be removed.");
        if (game.medalInlets == null || game.medalInlets.Length != 3 || game.medalInlets.Any(t => t == null)) throw new InvalidOperationException("Three inlets required.");
        if (controller.ballPrefabs == null || controller.ballPrefabs.Length != 3 || controller.ballPrefabs.Any(p => p == null || !p.GetComponent<MedalItem>().isBall)) throw new InvalidOperationException("Three board balls required.");
        if (controller.stations == null || controller.stations.Length != 3 || controller.stations.Select(s => s.kind).Distinct().Count() != 3) throw new InvalidOperationException("Three different jackpot stations required.");
        if (controller.upperStation == null || !controller.upperStation.isUpperStation || !controller.upperStation.usesBumpers) throw new InvalidOperationException("Upper physical bumper lottery required.");
        var upper = controller.upperStation;
        var steelBall = AssetDatabase.LoadAssetAtPath<Material>(MedalLotteryStationBuilder.SteelBallMaterialPath);
        var greenBall = AssetDatabase.LoadAssetAtPath<Material>(MedalLotteryStationBuilder.GreenMaterialPath);
        if (steelBall == null || greenBall == null || upper.lotteryBallPrefab.GetComponent<Renderer>().sharedMaterial != steelBall ||
            controller.stations.Any(s => s.lotteryBallPrefab.GetComponent<Renderer>().sharedMaterial != steelBall) ||
            controller.ballPrefabs.Any(p => p.GetComponent<Renderer>().sharedMaterial != greenBall))
            throw new InvalidOperationException("Lottery spheres must be silver steel; pusher board spheres must remain green.");
        if (upper.bumpers == null || upper.bumpers.Length != 4 || upper.bumpers.Any(b => b == null || b.station != upper || b.GetComponent<Collider>() == null || b.GetComponent<Collider>().isTrigger)) throw new InvalidOperationException("Four solid round bumpers required.");
        if (upper.upperOutflow == null || upper.upperOutflow.station != upper || upper.GetComponentsInChildren<MedalLotteryPocket>().Length != 0) throw new InvalidOperationException("Upper draw must finish through actual outflow rather than WIN pockets.");
        var upperFloor = upper.transform.Find("UpperBumperPlayFloor");
        var clearRimGuards = upper.GetComponentsInChildren<Transform>(true).Where(t => t.name == "UpperClearRimGuard").ToArray();
        if (upperFloor == null || upperFloor.GetComponent<MeshCollider>() == null || upper.transform.Find("SlopedPhysicalBowl") != null || clearRimGuards.Length != 16 || upper.transform.Find("ColorSelectionStage") != null) throw new InvalidOperationException("Integrated flat upper disk, clear perimeter and no separate selector required.");
        if (upper.colorRoutePockets == null || upper.colorRoutePockets.Length != 3 || upper.colorGateBodies == null || upper.colorGateBodies.Length != 3 || upper.colorRoutePockets.Any(p => p == null) || upper.colorGateBodies.Any(g => g == null)) throw new InvalidOperationException("Three sealed color pockets required.");
        Vector3 red = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Ruby).transform.position);
        Vector3 blue = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Sapphire).transform.position);
        Vector3 yellow = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Amber).transform.position);
        if (red.x >= blue.x || yellow.z <= red.z || yellow.z <= blue.z) throw new InvalidOperationException("Expected red left, blue right, yellow rear from the pusher.");
        if (upper.outBlockRaiseHeight >= 0 || upper.outBlocks == null || upper.outBlocks.Length != 4 || upper.outBlockBodies == null || upper.outBlockBodies.Length != 4 || upper.outBlockClosedPositions == null || upper.outBlockClosedPositions.Length != 4 || upper.outBlocks.Any(g => g == null) || upper.outBlockBodies.Any(g => g == null)) throw new InvalidOperationException("Four independently retracting front OUT blocks required.");
        if (upper.outflows == null || upper.outflows.Length != 1 || upper.outflows[0] == null || upper.upperOutflow != upper.outflows[0]) throw new InvalidOperationException("One real front OUT exit required.");
        if (upper.GetComponentsInChildren<TMP_Text>(true).Any(t => t.name == "GateColorMark" || t.name == "ColorRouteLabel" || t.name == "OutBlockMark")) throw new InvalidOperationException("Color pockets and guards should be identified by their appearance.");
        if (game.sidePayoutPoints == null || game.sidePayoutPoints.Length != 2 || game.jackpotPayoutPoint == null) throw new InvalidOperationException("Payout outlets missing.");
        if (game.sidePayoutPoints.Any(p => game.transform.InverseTransformPoint(p.position).z < 1f || game.transform.InverseTransformPoint(p.position).y < 1.7f)) throw new InvalidOperationException("Side hoppers must pay onto the upper pusher plate.");
        if (game.GetComponent<MedalArcadeSettings>() == null || UnityEngine.Object.FindFirstObjectByType<MedalArcadeSettingsUI>() == null) throw new InvalidOperationException("Settings missing.");
        foreach (var station in controller.stations)
        {
            if (station.lotteryBallPrefab == null || station.launchPoint == null || station.GetComponentsInChildren<MedalLotteryPocket>().Length < 3) throw new InvalidOperationException("Station physics/pockets missing: " + station.kind);
            if (station.dividerRotor == null || station.dividerBody == null || !station.dividerBody.isKinematic) throw new InvalidOperationException("Rotating central partitions missing: " + station.kind);
        }
        for (int i = 0; i < 3; i++)
            if (controller.stations[i].kind != (MedalJackpotKind)i) throw new InvalidOperationException("Station kind/order mismatch.");
        if (UnityEngine.Object.FindFirstObjectByType<MedalArcadeUI>() == null) throw new InvalidOperationException("Arcade UI missing.");
        var report = new { success = true, inlets = game.medalInlets.Length, boardBallTypes = controller.ballPrefabs.Length, genericPrizePrefabs = game.prizePrefabs.Length, genericBoardPrizes = 0, genericPrizeSpawning = manager.spawnGenericPrizes, inletSelectionButtons = 0,
            lotteryBallAppearance = "silver-steel", boardBallAppearance = "green",
            upper = new { bumpers = upper.bumpers.Length, winPerHit = 2, colorDrawOnActualPocketEntry = true, resumeSameUpperBallAfterColorDraw = true, openColorGatesAboveWin = 100, flatReferenceDisk = true, clearRimGuards = clearRimGuards.Length, rearPocketIsYellow = true, upperExitOpenings = 1, separateFrontSelector = false, winPockets = upper.GetComponentsInChildren<MedalLotteryPocket>().Length, colorPockets = upper.colorRoutePockets.Length, independentOutBlocks = upper.outBlocks.Length, usesPerOutBlockPerCycle = 1, regenerateAfterAllBlocksUsed = upper.regenerateOutBlocks, exteriorExits = upper.outflows.Length, withdrawDirection = "down" },
            sidePayoutPositions = game.sidePayoutPoints.Select(p => { Vector3 v = game.transform.InverseTransformPoint(p.position); return new { x = v.x, y = v.y, z = v.z }; }).ToArray(),
            stations = controller.stations.Select(s => new { kind = s.kind.ToString(), pockets = s.GetComponentsInChildren<MedalLotteryPocket>().Length, liveJackpotAmount = s.jackpotPocketText != null, randomCentralPartitions = s.dividerBody != null }).ToArray() };
        WriteReport("expansion-scene-validation.json", report);
    }

    public static void WriteReport(string name, object value)
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-expansion-20261004");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented));
    }
    private static Transform Child(string name, Transform parent)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
    private static Transform Shape(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = scale; go.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
    }
    private static Transform Panel(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); SetRect(go.GetComponent<RectTransform>(), min, max);
        go.GetComponent<Image>().color = new Color(.025f, .03f, .075f, .94f); go.GetComponent<Image>().raycastTarget = false; return go.transform;
    }
    private static TextMeshProUGUI Text(string name, Transform parent, string text, Vector2 min, Vector2 max, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false); SetRect(go.GetComponent<RectTransform>(), min, max);
        var tmp = go.GetComponent<TextMeshProUGUI>(); tmp.font = MedalJapaneseFontBuilder.GetFont();
        tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.alignment = TextAlignmentOptions.MidlineLeft; tmp.raycastTarget = false; return tmp;
    }
    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
}
