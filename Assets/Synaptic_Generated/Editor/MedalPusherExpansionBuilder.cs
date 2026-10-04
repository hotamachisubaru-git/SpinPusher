using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
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
        Debug.Log("[MedalPusher] Slots, inlets and three physical jackpot stations saved.");
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
        var manager = game.GetComponent<PrizeDropManager>();
        manager.maxPrizesOnBoard = 12;
        manager.minPrizesOnBoard = 6;

        var inlets = field.Find("MedalInlets");
        if (inlets != null) UnityEngine.Object.DestroyImmediate(inlets.gameObject);
        inlets = Child("MedalInlets", field);
        var gold = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/MedalGold.mat");
        var dark = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Board.mat");
        var light = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NeonCyan.mat");
        var lights = new Renderer[3];
        game.medalInlets = new Transform[3];
        string[] names = { "LEFT", "CENTER", "RIGHT" };
        for (int i = 0; i < 3; i++)
        {
            var inlet = Child("Inlet_" + names[i], inlets);
            inlet.localPosition = new Vector3((i - 1) * 3.2f, 0, .6f);
            Shape("GoldHopper", inlet, new Vector3(0, 2.52f, 0), new Vector3(.85f, .45f, .45f), gold);
            Shape("BlackSlot", inlet, new Vector3(0, 2.51f, -.24f), new Vector3(.63f, .12f, .025f), dark);
            Shape("Support_Left", inlet, new Vector3(-.35f, 1.75f, .15f), new Vector3(.09f, 1.1f, .09f), gold);
            Shape("Support_Right", inlet, new Vector3(.35f, 1.75f, .15f), new Vector3(.09f, 1.1f, .09f), gold);
            lights[i] = Shape("SelectionLight", inlet, new Vector3(0, 2.8f, 0), new Vector3(.7f, .07f, .4f), light).GetComponent<Renderer>();
            game.medalInlets[i] = Child("MedalOutlet", inlet);
            game.medalInlets[i].localPosition = new Vector3(0, 2.25f, -.18f);
        }
        game.selectedInlet = 1;
        game.medalSpawnPoint = game.medalInlets[1];
        LocalizeCabinet(field);
        // Preserve regular prizes and medals; replace only this expansion's initial balls.
        foreach (var item in game.itemsRoot.GetComponentsInChildren<MedalItem>())
            if (item.isBall) UnityEngine.Object.DestroyImmediate(item.gameObject);
        for (int i = 0; i < 3; i++)
        {
            var ball = (GameObject)PrefabUtility.InstantiatePrefab(controller.ballPrefabs[i], scene);
            ball.name = "BoardBall_" + ((MedalJackpotKind)i);
            ball.transform.SetParent(game.itemsRoot, false);
            ball.transform.localPosition = new Vector3((i - 1) * 2.4f, .7f, -3.25f);
        }
        BuildUI(game, controller, lights);
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

    private static void BuildUI(MedalPusherGame game, MedalSlotJackpotController controller, Renderer[] lights)
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
        ui.game = game; ui.controller = controller; ui.inletLights = lights;
        ui.cameraView = UnityEngine.Object.FindFirstObjectByType<MedalPusherCameraView>();
        ui.towerJackpotValues = new TMP_Text[4];
        ui.towerJackpotKinds = new[] { -1, 0, 2, 1 };
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
                text.text = "<color=#FF5E6E>赤 150枚</color>\n<color=#54BBFF>青 250枚</color>\n<color=#FFD448>黄 500枚</color>";
                var caption = face.Find("ScreenCaption"); if (caption != null) caption.GetComponent<TMP_Text>().text = "";
                var divider = face.Find("ColorDivider"); if (divider != null) divider.localPosition = new Vector3(0, 6.99f, -3.074f);
            }
            else text.text = controller.jackpotPools[ui.towerJackpotKinds[i]] + "枚";
        }
        Panel("SlotPanel", root, new Vector2(.025f, .64f), new Vector2(.29f, .845f));
        Text("SlotTitle", root, "宝探しスロット", new Vector2(.04f, .80f), new Vector2(.275f, .834f), 22, new Color(1, .8f, .25f));
        ui.reelsText = Text("SlotReels", root, "7  |  7  |  7", new Vector2(.04f, .713f), new Vector2(.275f, .795f), 34, Color.white);
        ui.reelsText.alignment = TextAlignmentOptions.Center;
        ui.spinMeterText = Text("SpinMeter", root, "", new Vector2(.04f, .655f), new Vector2(.28f, .71f), 18, Color.white);
        Panel("UpperWinPanel", root, new Vector2(.025f, .53f), new Vector2(.29f, .62f));
        ui.upperWinText = Text("UpperWinText", root, "上段 0 WIN ｜ 100 WIN超で3色抽選", new Vector2(.04f, .541f), new Vector2(.278f, .609f), 18, Color.white);
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
        Panel("DrawStatus", root, new Vector2(.305f, .785f), new Vector2(.975f, .845f));
        ui.statusText = Text("DrawStatusText", root, "", new Vector2(.315f, .793f), new Vector2(.962f, .837f), 22, Color.white);
        ui.statusText.alignment = TextAlignmentOptions.Center;
        ui.statusText.enableAutoSizing = true;
        ui.statusText.fontSizeMin = 12;
        ui.statusText.fontSizeMax = 22;
        ui.statusText.textWrappingMode = TextWrappingModes.NoWrap;
        ui.inletButtons = new Image[3];
        UnityEngine.Events.UnityAction[] actions = { game.SelectLeftInlet, game.SelectCenterInlet, game.SelectRightInlet };
        string[] labels = { "1：左", "2：中央", "3：右" };
        for (int i = 0; i < 3; i++)
        {
            var panel = Panel("InletButton_" + i, root, new Vector2(.365f + i * .135f, .135f), new Vector2(.49f + i * .135f, .192f));
            var image = panel.GetComponent<Image>(); image.raycastTarget = true; ui.inletButtons[i] = image;
            var button = panel.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            UnityEventTools.AddPersistentListener(button.onClick, actions[i]);
            var text = Text("Label", panel, labels[i], Vector2.zero, Vector2.one, 21, Color.white);
            text.alignment = TextAlignmentOptions.Center;
        }
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
        if (controls != null) controls.GetComponent<TMP_Text>().text = "スペース／クリック：投入　1～3：投入口　V：全景";
    }

    private static void ConfigureBaseHUD(MedalPusherUI ui)
    {
        foreach (string name in new[] { "ScoreLabel", "ScoreValue", "ControlsPanel" })
        {
            var old = ui.transform.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        ui.scoreLabel = null; ui.scoreText = null;
        PoseUI(ui.transform, "Header", new Vector2(.025f, .885f), new Vector2(.975f, .98f));
        PoseUI(ui.transform, "Title", new Vector2(.04f, .902f), new Vector2(.36f, .967f), "宝探しメダルゲーム");
        PoseUI(ui.transform, "Controls", new Vector2(.385f, .9f), new Vector2(.96f, .968f));
        var panel = ui.transform.Find("MedalsPanel");
        if (panel == null) panel = Panel("MedalsPanel", ui.transform, new Vector2(.025f, .025f), new Vector2(.245f, .14f));
        panel.SetAsFirstSibling();
        PoseUI(ui.transform, "MedalLabel", new Vector2(.04f, .095f), new Vector2(.23f, .135f), "メダル残高");
        PoseUI(ui.transform, "MedalValue", new Vector2(.04f, .032f), new Vector2(.23f, .097f));
        ui.medalsLabel = ui.transform.Find("MedalLabel").GetComponent<TextMeshProUGUI>();
        PoseUI(ui.transform, "ViewButton", new Vector2(.365f, .035f), new Vector2(.495f, .105f));
        PoseUI(ui.transform, "ViewButton/ViewLabel", Vector2.zero, Vector2.one, "全景");
        PoseUI(ui.transform, "DropMedalButton", new Vector2(.515f, .035f), new Vector2(.745f, .105f));
        PoseUI(ui.transform, "DropMedalButton/DropLabel", Vector2.zero, Vector2.one, "メダル投入");
        PoseUI(ui.transform, "ComboPanel", new Vector2(.34f, .665f), new Vector2(.66f, .725f));
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
                case "CabinetTitle": text.text = "宝探し\nメダルゲーム"; break;
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
        if (game.medalInlets == null || game.medalInlets.Length != 3 || game.medalInlets.Any(t => t == null)) throw new InvalidOperationException("Three inlets required.");
        if (controller.ballPrefabs == null || controller.ballPrefabs.Length != 3 || controller.ballPrefabs.Any(p => p == null || !p.GetComponent<MedalItem>().isBall)) throw new InvalidOperationException("Three board balls required.");
        if (controller.stations == null || controller.stations.Length != 3 || controller.stations.Select(s => s.kind).Distinct().Count() != 3) throw new InvalidOperationException("Three different jackpot stations required.");
        if (controller.upperStation == null || !controller.upperStation.isUpperStation || controller.upperStation.GetComponentsInChildren<MedalLotteryPocket>().Any(p => p.jackpot)) throw new InvalidOperationException("Upper WIN lottery required.");
        var upper = controller.upperStation;
        if (upper.colorRoutePockets == null || upper.colorRoutePockets.Length != 3 || upper.colorGateBodies == null || upper.colorGateBodies.Length != 3 || upper.colorRoutePockets.Any(p => p == null) || upper.colorGateBodies.Any(g => g == null)) throw new InvalidOperationException("Three sealed color pockets required.");
        Vector3 red = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Ruby).transform.position);
        Vector3 blue = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Sapphire).transform.position);
        Vector3 yellow = game.transform.InverseTransformPoint(upper.colorRoutePockets.Single(p => p.kind == MedalJackpotKind.Amber).transform.position);
        if (red.x >= blue.x || yellow.z <= red.z || yellow.z <= blue.z) throw new InvalidOperationException("Expected red left, blue right, yellow rear from the pusher.");
        if (upper.outBlock == null || upper.outflow == null || upper.outBlockBody == null || upper.outBlockRaiseHeight >= 0) throw new InvalidOperationException("One-use retracting white OUT block required.");
        if (upper.outBlocks == null || upper.outBlocks.Length != 8 || upper.outBlockBodies == null || upper.outBlockBodies.Length != 8 || upper.outflows == null || upper.outflows.Length != 8 || upper.outBlocks.Any(g => g == null) || upper.outflows.Any(g => g == null)) throw new InvalidOperationException("Eight linked perimeter/front guards and real exterior exits required.");
        if (upper.GetComponentsInChildren<TMP_Text>(true).Any(t => t.name == "GateColorMark" || t.name == "ColorRouteLabel")) throw new InvalidOperationException("Color pockets should be identified by their colors.");
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
        var report = new { success = true, inlets = game.medalInlets.Length, boardBallTypes = controller.ballPrefabs.Length,
            upper = new { winPockets = upper.GetComponentsInChildren<MedalLotteryPocket>().Length, colorPockets = upper.colorRoutePockets.Length, whiteOutBlock = true, linkedGuards = upper.outBlocks.Length, exteriorExits = upper.outflows.Length, withdrawDirection = "down" },
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
