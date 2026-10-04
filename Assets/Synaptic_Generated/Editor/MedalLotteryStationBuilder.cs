using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds matching colored roulettes and the upper green-ball WIN lottery.</summary>
public static class MedalLotteryStationBuilder
{
    private const string Root = "Assets/Synaptic_Generated/MedalPusher/Lottery";
    public const string GreenMaterialPath = Root + "/Materials/DrawGreen.mat";
    public static Material GreenBallMaterial => green;
    private static Material gold, white, black, ruby, sapphire, amber, green;
    private static PhysicsMaterial surfacePhysics;
    private static TMP_FontAsset font;
    private static GameObject ballPrefab;

    /// <summary>Returns Ruby, Sapphire, Amber, in MedalJackpotKind enum order.</summary>
    public static MedalBallLotteryStation[] BuildStations(Transform field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before rebuilding the lottery stations.");
        PrepareAssets();

        var stations = new[] {
            CreateStation(field, "TreasureCabinetDecor/Station_West", MedalJackpotKind.Ruby),
            CreateStation(field, "TreasureCabinetDecor/Station_East", MedalJackpotKind.Sapphire),
            CreateStation(field, "TreasureCabinetDecor/Station_Rear", MedalJackpotKind.Amber)
        };
        BuildRoulette(stations[0], ruby, "赤のボール抽選", "DrawRubyFunnel");
        BuildRoulette(stations[1], sapphire, "青のボール抽選", "DrawSapphireFunnel");
        BuildRoulette(stations[2], amber, "黄のボール抽選", "DrawAmberFunnel");
        AssetDatabase.SaveAssets();
        return stations;
    }

    private static void PrepareAssets()
    {
        foreach (string folder in new[] { "/Materials", "/Meshes", "/Prefabs" }) Directory.CreateDirectory(Root + folder);
        AssetDatabase.Refresh();
        gold = MakeMaterial("DrawGold", new Color(1f, .72f, .20f), .65f);
        white = MakeMaterial("DrawIvory", new Color(.93f, .96f, 1f), .15f);
        black = MakeMaterial("DrawGraphite", new Color(.025f, .035f, .055f), .25f);
        ruby = MakeMaterial("DrawRuby", new Color(1f, .06f, .10f), .25f, .5f);
        sapphire = MakeMaterial("DrawSapphire", new Color(.025f, .50f, 1f), .25f, .5f);
        amber = MakeMaterial("DrawAmber", new Color(1f, .78f, .045f), .25f, .5f);
        green = MakeMaterial("DrawGreen", new Color(.035f, .94f, .30f), .18f, .45f);
        surfacePhysics = MakePhysics("DrawSurface", .035f, .02f, .10f);
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Synaptic_Generated/MedalPusher/Fonts/MedalJapanese SDF.asset")
            ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/SourceFiles/Fonts/Inter-Variable SDF.asset")
            ?? TMP_Settings.defaultFontAsset;
        ballPrefab = MakeBallPrefab();
    }

    /// <summary>Creates a fourth physical WIN lottery above the central crown.</summary>
    public static MedalBallLotteryStation BuildUpperStation(Transform field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before rebuilding the upper lottery.");
        if (ballPrefab == null || green == null || surfacePhysics == null) PrepareAssets();
        var decor = field.Find("TreasureCabinetDecor");
        if (decor == null || decor.Find("FourSidedTreasureTower") == null)
            throw new InvalidOperationException("Build the central treasure tower before its upper lottery.");
        var previous = decor.Find("UpperLotteryStation");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var upper = Group("UpperLotteryStation", decor);

        // The crown gem tops out below y=10.44. This raised platform begins at
        // y=10.51, with every physical pocket and ball kept above the old roof.
        Box("UpperWhitePlatform", upper, new Vector3(0, 10.60f, 8.016f), new Vector3(5.58f, .18f, 5.58f), white, false);
        Box("UpperGoldPlatformRim", upper, new Vector3(0, 10.73f, 8.016f), new Vector3(5.78f, .08f, 5.78f), gold, false);
        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Box("UpperSupportWhite", upper, new Vector3(x * 3.12f, 9.47f, 8f + z * 3.12f), new Vector3(.24f, 2.50f, .24f), white, false);
            var diagonal = Box("UpperSupportGoldBrace", upper, new Vector3(x * 2.90f, 10.25f, 8f + z * 2.90f), new Vector3(.12f, .92f, .12f), gold, false);
            diagonal.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(-x * .55f, .68f, -z * .55f));
            Box("UpperSupportCapital", upper, new Vector3(x * 2.72f, 10.65f, 8.016f + z * 2.72f), new Vector3(.42f, .18f, .42f), gold, false);
        }

        var module = Group("LotteryModule", upper);
        module.localPosition = new Vector3(0f, 10.95f, 8.5f);
        module.localScale = Vector3.one * .88f;
        var station = module.gameObject.AddComponent<MedalBallLotteryStation>();
        station.kind = MedalJackpotKind.Ruby;
        station.isUpperStation = true;
        station.rotationEnabled = false;
        station.lotteryBallPrefab = ballPrefab;
        station.drawTimeout = 10f;
        const float centerZ = -.55f;
        BuildFunnel(station, "DrawUpperFunnel", 2.48f, 1.12f, 1.53f, .30f, centerZ, green);
        BuildRadialPockets(station, 8, centerZ, Array.Empty<int>(), new[] { 20, 40, 60, 80, 100, 120, 150, 200 }, green);
        var winPockets = module.GetComponentsInChildren<MedalLotteryPocket>();
        station.normalWinPocketTriggers = new Collider[winPockets.Length];
        for (int i = 0; i < winPockets.Length; i++) station.normalWinPocketTriggers[i] = winPockets[i].GetComponent<Collider>();
        BuildSweeper(station, centerZ, 25f, green, false);
        ConfigureLaunch(station, green, centerZ);
        for (int side = -1; side <= 1; side += 2)
        {
            Box("UpperMarqueeWhitePost", module, new Vector3(side * 2.68f, 1.55f, 2.27f), new Vector3(.18f, 2.65f, .22f), white, false);
            Box("UpperMarqueeGoldPost", module, new Vector3(side * 2.68f, 1.55f, 2.13f), new Vector3(.07f, 2.50f, .035f), gold, false);
        }
        Box("UpperDrawHeaderBacking", module, new Vector3(0, 2.82f, 2.24f), new Vector3(5.58f, .57f, .22f), black, false);
        Box("UpperDrawHeaderTop", module, new Vector3(0, 3.17f, 2.24f), new Vector3(5.95f, .12f, .43f), gold, false);
        Box("UpperDrawHeaderLight", module, new Vector3(0, 2.49f, 2.08f), new Vector3(5.48f, .075f, .045f), green, false);
        Label("DrawTitle", module, "上段ボール抽選", new Vector3(0, 2.84f, 2.11f), new Vector2(5.3f, .48f), 3.8f, Color.white);
        BuildColorSelectionStage(station);
        AssetDatabase.SaveAssets();
        return station;
    }

    private static void BuildColorSelectionStage(MedalBallLotteryStation station)
    {
        const float centerZ = -5.3f;
        var parent = station.transform;
        var stage = Group("ColorSelectionStage", parent);
        BuildFunnel(station, "DrawColorSelectionFunnel", 1.95f, 1.12f, 1.40f, .30f, centerZ, green, stage, true);
        for (int side = -1; side <= 1; side += 2)
        {
            Box("ColorSelectionFrontSupport", stage, new Vector3(side * 1.55f, -.42f, centerZ), new Vector3(.15f, .72f, .15f), white, false);
            var brace = Box("ColorSelectionGoldBrace", stage, new Vector3(side * 1.55f, -.72f, -4.35f), new Vector3(.10f, 2.15f, .10f), gold, false);
            brace.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(0, .65f, -2f));
        }

        station.colorRoutePockets = new MedalColorRoutePocket[3];
        station.colorGateBodies = new Rigidbody[3];
        station.colorGateClosedPositions = new Vector3[3];
        var colors = new[] { ruby, sapphire, amber };
        var directions = new[] { Vector3.left, Vector3.right, Vector3.forward };
        for (int i = 0; i < 3; i++)
        {
            var pocketGroup = Group("ColorRoutePocket_" + (MedalJackpotKind)i, stage);
            pocketGroup.localPosition = new Vector3(directions[i].x * .84f, 0f, centerZ + directions[i].z * .84f);
            pocketGroup.localRotation = Quaternion.FromToRotation(Vector3.forward, directions[i]);
            Box("ColorRouteCup", pocketGroup, new Vector3(0, -.36f, 0), new Vector3(.88f, .12f, 1.04f), colors[i], true);
            var trigger = Group("ColorRouteTrigger", pocketGroup);
            trigger.localPosition = new Vector3(0, .04f, 0);
            var triggerCollider = trigger.gameObject.AddComponent<BoxCollider>();
            triggerCollider.size = new Vector3(.78f, .66f, .92f);
            triggerCollider.isTrigger = true;
            var pocket = trigger.gameObject.AddComponent<MedalColorRoutePocket>();
            pocket.station = station;
            pocket.kind = (MedalJackpotKind)i;
            var gate = Box("ColorGate_" + pocket.kind, pocketGroup, new Vector3(0, .46f, 0), new Vector3(.93f, .12f, 1.08f), colors[i], true);
            var gateBody = gate.gameObject.AddComponent<Rigidbody>();
            gateBody.isKinematic = true;
            gateBody.useGravity = false;
            gateBody.interpolation = RigidbodyInterpolation.Interpolate;
            gateBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            pocket.gateBody = gateBody;
            station.colorRoutePockets[i] = pocket;
            station.colorGateBodies[i] = gateBody;
            station.colorGateClosedPositions[i] = gate.localPosition;
            Box("GateGoldHandle", gate, new Vector3(0, .55f, 0), new Vector3(.24f, .30f, .18f), gold, false);
        }
        for (int i = 0; i < 4; i++)
        {
            float angle = (45f + i * 90f) * Mathf.Deg2Rad;
            // A low sloping fin drains off its top toward the real pocket
            // entrances, rather than wedging a ball above the bowl/chute rail.
            var divider = Box("ColorRouteDivider", stage, new Vector3(Mathf.Cos(angle) * 1.10f, -.06f, centerZ + Mathf.Sin(angle) * 1.10f), new Vector3(.06f, .32f, .90f), white, true);
            divider.localRotation = Quaternion.Euler(0, 90f - angle * Mathf.Rad2Deg, 0) * Quaternion.Euler(-30f, 0, 0);
        }
        // The front sector is cut from the bowl. A descending physical chute
        // leads through the one-use plate to an OUT trigger beyond the outer rim.
        // The rear edge is embedded inside the central dome. A ball therefore
        // meets a continuous descending face, rather than a lip opposing it.
        var ramp = Box("PhysicalOutflowRamp", stage, new Vector3(0, -.04f, -6.70f), new Vector3(.99f, .12f, 2.52f), white, true);
        ramp.localRotation = Quaternion.Euler(-14f, 0, 0);
        for (int side = -1; side <= 1; side += 2)
            Box("OutflowSideRail", stage, new Vector3(side * .55f, .20f, -6.90f), new Vector3(.08f, 1.08f, 2.03f), gold, true);
        var plate = Box("WhiteOutBlock", stage, new Vector3(0, .40f, -7.26f), new Vector3(.91f, 1.30f, .13f), white, true);
        var plateBody = plate.gameObject.AddComponent<Rigidbody>();
        plateBody.isKinematic = true;
        plateBody.useGravity = false;
        plateBody.interpolation = RigidbodyInterpolation.Interpolate;
        plateBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        station.outBlockBody = plateBody;
        station.outBlockClosedPosition = plate.localPosition;
        station.outBlock = plate.gameObject.AddComponent<MedalOutBlock>();
        station.outBlock.station = station;
        Label("OutBlockMark", plate, "ガード\n1", new Vector3(0, 0, -.57f), new Vector2(.70f, .68f), 1.8f, new Color(.04f, .08f, .10f));
        station.outBlockText = plate.Find("OutBlockMark").GetComponent<TMP_Text>();
        Box("OutBlockRetractionHousing", stage, new Vector3(0, -.99f, -7.26f), new Vector3(1.08f, 1.50f, .26f), black, false);
        var exit = Group("ActualOutsideOutflow", stage);
        exit.localPosition = new Vector3(0, -.18f, -7.76f);
        var exitCollider = exit.gameObject.AddComponent<BoxCollider>();
        exitCollider.size = new Vector3(.83f, 1.0f, .38f);
        exitCollider.isTrigger = true;
        station.outflow = exit.gameObject.AddComponent<MedalLotteryOutflow>();
        station.outflow.station = station;
        ConfigurePerimeterGuards(station, stage, centerZ);
        Box("ColorSelectionTitleBacking", stage, new Vector3(0, 2.15f, -3.22f), new Vector3(4.24f, .49f, .14f), black, false);
        Label("ColorSelectionTitle", stage, "色選択", new Vector3(0, 2.15f, -3.31f), new Vector2(3.9f, .42f), 3.5f, Color.white);
        station.colorLaunchPoint = Group("ColorLaunchPoint", parent);
        station.colorLaunchPoint.localPosition = new Vector3(0, 2.05f, centerZ);
        Box("GreenBallTransferPort", station.colorLaunchPoint, new Vector3(0, .24f, 0), new Vector3(.72f, .12f, .50f), green, false);
        station.colorSelectionTimeout = 10f;
        station.colorGateRaiseHeight = 1.1f;
        station.colorGateMoveSpeed = 4f;
        station.colorLaunchVelocity = new Vector3(0f, -.08f, 0f);
        station.colorLaunchVelocityJitter = new Vector3(1.15f, 0f, 1.15f);
        station.colorLaunchPositionJitter = new Vector3(.12f, 0f, .12f);
        station.colorGuideLocalCenter = new Vector3(0f, 0f, centerZ);
        station.colorBowlOuterRadius = 1.95f;
        station.colorBowlFloorY = -.75f;
        station.outBlockRetractionDelay = .18f;
        station.outBlockRaiseHeight = -1.65f;
    }

    private static void ConfigurePerimeterGuards(MedalBallLotteryStation station, Transform stage, float centerZ)
    {
        // Index zero remains the original front plate/exit for callers that use the aliases.
        var blocks = new List<MedalOutBlock> { station.outBlock };
        var bodies = new List<Rigidbody> { station.outBlockBody };
        var closedPositions = new List<Vector3> { station.outBlockClosedPosition };
        var exits = new List<MedalLotteryOutflow> { station.outflow };
        int wallCount = stage.childCount;
        for (int i = 0; i < wallCount; i++)
        {
            Transform wall = stage.GetChild(i);
            if (wall.name != "BowlOuterWall") continue;
            var renderer = wall.GetComponent<Renderer>();
            bool becomesGuard = renderer.sharedMaterial == green;
            // Narrow fixed gold panels leave a .589-wide opening when a white
            // guard retracts, enough for the actual .52-diameter lottery ball.
            wall.localScale = new Vector3(becomesGuard ? .62f : .40f, .55f, .13f);
            if (!becomesGuard) continue;
            wall.name = "WhitePerimeterGuard_" + blocks.Count;
            renderer.sharedMaterial = white;
            var body = wall.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var block = wall.gameObject.AddComponent<MedalOutBlock>();
            block.station = station;
            blocks.Add(block);
            bodies.Add(body);
            closedPositions.Add(wall.localPosition);

            Vector3 outward = wall.localPosition - new Vector3(0f, 0f, centerZ);
            outward.y = 0f;
            outward.Normalize();
            var exit = Group("ActualPerimeterOutflow_" + (blocks.Count - 1), stage);
            exit.localPosition = new Vector3(0f, .85f, centerZ) + outward * 2.48f;
            exit.localRotation = wall.localRotation;
            var collider = exit.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(.64f, 3.25f, .32f);
            collider.isTrigger = true;
            var outflow = exit.gameObject.AddComponent<MedalLotteryOutflow>();
            outflow.station = station;
            exits.Add(outflow);
        }
        station.outBlocks = blocks.ToArray();
        station.outBlockBodies = bodies.ToArray();
        station.outBlockClosedPositions = closedPositions.ToArray();
        station.outflows = exits.ToArray();
    }

    private static MedalBallLotteryStation CreateStation(Transform field, string path, MedalJackpotKind kind)
    {
        var parent = field.Find(path);
        if (parent == null) throw new InvalidOperationException("Build the treasure cabinet before its ball lottery: " + path);
        var previous = parent.Find("LotteryModule");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var module = Group("LotteryModule", parent);
        var station = module.gameObject.AddComponent<MedalBallLotteryStation>();
        station.kind = kind;
        station.rotationEnabled = false;
        station.lotteryBallPrefab = ballPrefab;
        station.drawTimeout = 10f;
        return station;
    }

    private static void BuildRoulette(MedalBallLotteryStation station, Material accent, string title, string meshName)
    {
        const float centerZ = -.55f;
        var parent = station.transform;
        Box("DrawHeaderBacking", parent, new Vector3(0, 4.05f, 2.065f), new Vector3(5.8f, .48f, .035f), black, false);
        Label("DrawTitle", parent, title, new Vector3(0, 4.05f, 2.02f), new Vector2(5.6f, .42f), 3.8f, Color.white);
        BuildFunnel(station, meshName, 2.48f, 1.12f, 1.53f, .30f, centerZ, accent);
        BuildRadialPockets(station, 8, centerZ, new[] { 0 }, new[] { 0, 20, 20, 30, 20, 40, 20, 30 }, accent);
        BuildSweeper(station, centerZ, 25f, accent, false);
        ConfigureLaunch(station, accent, centerZ);
    }

    private static void ConfigureLaunch(MedalBallLotteryStation station, Material accent, float centerZ)
    {
        station.launchPoint = Group("LaunchPoint", station.rotor);
        station.launchPoint.localPosition = new Vector3(2.06f, 1.24f, -.05f);
        station.launchVelocityRelativeToPoint = true;
        Box("RotatingBallSpout", station.launchPoint, new Vector3(0, .26f, 0), new Vector3(.62f, .13f, .55f), accent, false);
        station.localLaunchVelocity = new Vector3(-.45f, 0f, 1.35f);
        station.launchVelocityJitter = new Vector3(.15f, .05f, .50f);
        station.guideLocalCenter = new Vector3(0, 0, centerZ);
        station.guideAcceleration = .30f;
    }

    private static void BuildFunnel(MedalBallLotteryStation station, string meshName, float outerRadius, float innerRadius, float outerY, float innerY, float centerZ, Material accent, Transform parentOverride = null, bool frontOpening = false)
    {
        var parent = parentOverride ?? station.transform;
        var meshGo = new GameObject("SlopedPhysicalBowl", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        meshGo.transform.SetParent(parent, false);
        meshGo.transform.localPosition = new Vector3(0, 0, centerZ);
        var mesh = FunnelMesh(meshName, outerRadius, innerRadius, outerY, innerY, frontOpening);
        meshGo.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = meshGo.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = white;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        var collider = meshGo.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.sharedMaterial = surfacePhysics;
        const int walls = 24;
        for (int i = 0; i < walls; i++)
        {
            float angle = i * Mathf.PI * 2f / walls;
            if (frontOpening && Mathf.Sin(angle) < -.94f) continue;
            var wall = Box("BowlOuterWall", parent, new Vector3(Mathf.Cos(angle) * outerRadius, outerY + .25f, centerZ + Mathf.Sin(angle) * outerRadius),
                new Vector3(outerRadius * .275f, .55f, .13f), i % 3 == 0 ? accent : gold, true);
            wall.localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg - 90f, 0);
        }
        // The small central dome sends an arriving ball into a surrounding pocket.
        Sphere("CenterDeflector", parent, new Vector3(0, .18f, centerZ), Vector3.one * .67f, gold, true);
    }

    private static void BuildRadialPockets(MedalBallLotteryStation station, int count, float centerZ, int[] jackpots, int[] rewards, Material accent)
    {
        float spacing = Mathf.PI * 2f / count;
        Transform dividerParent = station.transform;
        float dividerCenterZ = centerZ;
        if (!station.isUpperStation)
        {
            dividerParent = Group("CentralDividerRotor", station.transform);
            dividerParent.localPosition = new Vector3(0f, 0f, centerZ);
            var body = dividerParent.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            station.dividerRotor = dividerParent;
            station.dividerBody = body;
            dividerCenterZ = 0f;
        }
        for (int i = 0; i < count; i++)
        {
            float angle = -Mathf.PI * .5f + spacing * i;
            var pocketGroup = Group("Pocket_" + i, station.transform);
            pocketGroup.localPosition = new Vector3(Mathf.Cos(angle) * .72f, 0, centerZ + Mathf.Sin(angle) * .72f);
            pocketGroup.localRotation = Quaternion.Euler(0, 90f - angle * Mathf.Rad2Deg, 0);
            bool jackpot = Array.IndexOf(jackpots, i) >= 0;
            Material color = jackpot || station.isUpperStation ? accent : gold;
            float width = count == 8 ? .47f : .63f;
            Box("PocketCup", pocketGroup, new Vector3(0, -.25f, 0), new Vector3(width, .12f, .78f), color, true);
            var trigger = Box("PhysicalPocketTrigger", pocketGroup, new Vector3(0, .15f, 0), new Vector3(width * .90f, .68f, .74f), black, false);
            trigger.GetComponent<Renderer>().enabled = false;
            var triggerCollider = trigger.gameObject.AddComponent<BoxCollider>();
            triggerCollider.isTrigger = true;
            var pocket = trigger.gameObject.AddComponent<MedalLotteryPocket>();
            pocket.station = station;
            pocket.jackpot = jackpot;
            pocket.smallReward = rewards[i];
            Box("PocketFrontColor", pocketGroup, new Vector3(0, .30f, -.40f), new Vector3(width, .12f, .11f), color, false);
            int initialPool = station.kind == MedalJackpotKind.Ruby ? 150 : station.kind == MedalJackpotKind.Sapphire ? 250 : 500;
            string pocketText = station.isUpperStation ? rewards[i] + " WIN" : jackpot ? "JACKPOT\n" + initialPool + "枚" : rewards[i].ToString();
            Label("PocketMark", pocketGroup, pocketText, new Vector3(0, .55f, -.03f), jackpot ? new Vector2(.75f, .50f) : new Vector2(width * 1.3f, .32f), jackpot ? 1.05f : station.isUpperStation ? 1.4f : 2.6f,
                jackpot ? new Color(1f, .90f, .40f) : Color.white, Quaternion.Euler(90f, 0, 0));
            if (jackpot) station.jackpotPocketText = pocketGroup.Find("PocketMark").GetComponent<TMP_Text>();
            float edge = angle + spacing * .5f;
            // The colored partitions sit below the trigger's upper surface,
            // avoiding a high flat shelf above the actual pocket entrances.
            var divider = Box("RadialPocketDivider", dividerParent, new Vector3(Mathf.Cos(edge) * .74f, .16f, dividerCenterZ + Mathf.Sin(edge) * .74f),
                new Vector3(.055f, station.isUpperStation ? .77f : .60f, 1.05f), white, true);
            divider.localRotation = Quaternion.Euler(0, 90f - edge * Mathf.Rad2Deg, 0);
        }
    }

    private static void BuildSweeper(MedalBallLotteryStation station, float centerZ, float speed, Material accent, bool doubleArm)
    {
        var rotor = Group("PhysicalRotatingGuide", station.transform);
        rotor.localPosition = new Vector3(0, .61f, centerZ);
        var body = rotor.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Cylinder("RotorHub", rotor, Vector3.zero, .20f, .12f, accent, false);
        for (int side = 1; side >= (doubleArm ? -1 : 1); side -= 2)
        {
            Box("RotatingPaddle", rotor, new Vector3(side * 1.38f, .12f, 0), new Vector3(1.12f, .15f, .14f), gold, true);
            Box("OpenArm", rotor, new Vector3(side * .42f, .20f, 0), new Vector3(.84f, .09f, .10f), accent, false);
        }
        station.rotor = rotor;
        station.rotorBody = body;
        station.rotorDegreesPerSecond = speed;
    }

    private static Mesh FunnelMesh(string name, float outerRadius, float innerRadius, float outerY, float innerY, bool frontOpening = false)
    {
        const int segments = 64;
        var vertices = new Vector3[segments * 2];
        var triangles = new List<int>(segments * 6);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vertices[i * 2] = new Vector3(Mathf.Cos(angle) * outerRadius, outerY, Mathf.Sin(angle) * outerRadius);
            vertices[i * 2 + 1] = new Vector3(Mathf.Cos(angle) * innerRadius, innerY, Mathf.Sin(angle) * innerRadius);
            if (frontOpening && Mathf.Sin(angle + Mathf.PI / segments) < -.9659f) continue;
            int next = (i + 1) % segments;
            triangles.Add(i * 2); triangles.Add(i * 2 + 1); triangles.Add(next * 2);
            triangles.Add(next * 2); triangles.Add(i * 2 + 1); triangles.Add(next * 2 + 1);
        }
        string path = Root + "/Meshes/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create) mesh = new Mesh { name = name };
        else mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static GameObject MakeBallPrefab()
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "DrawLotteryBall";
        ball.transform.localScale = Vector3.one * .52f;
        ball.GetComponent<Renderer>().sharedMaterial = green;
        ball.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        ball.GetComponent<Collider>().sharedMaterial = surfacePhysics;
        var body = ball.AddComponent<Rigidbody>();
        body.mass = .12f;
        body.linearDamping = .025f;
        body.angularDamping = .025f;
        body.maxAngularVelocity = 30f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ball.AddComponent<LotteryBallToken>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(ball, Root + "/Prefabs/DrawLotteryBall.prefab");
        UnityEngine.Object.DestroyImmediate(ball);
        return prefab;
    }

    private static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static Transform Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool physics)
        => Shape(PrimitiveType.Cube, name, parent, position, scale, material, physics);
    private static Transform Sphere(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool physics)
        => Shape(PrimitiveType.Sphere, name, parent, position, scale, material, physics);
    private static Transform Cylinder(string name, Transform parent, Vector3 position, float radius, float height, Material material, bool physics)
        => Shape(PrimitiveType.Cylinder, name, parent, position, new Vector3(radius * 2, height * .5f, radius * 2), material, physics);

    private static Transform Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool physics)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var collider = go.GetComponent<Collider>();
        if (physics) collider.sharedMaterial = surfacePhysics;
        else UnityEngine.Object.DestroyImmediate(collider);
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private static void Label(string name, Transform parent, string content, Vector3 position, Vector2 size, float fontSize, Color color, Quaternion? rotation = null)
    {
        var go = new GameObject(name, typeof(TextMeshPro));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        var text = go.GetComponent<TextMeshPro>();
        text.font = font; text.text = content; text.fontSize = fontSize; text.color = color;
        text.fontStyle = FontStyles.Bold; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = size;
        text.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Material MakeMaterial(string name, Color color, float metallic, float emission = 0f)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", .56f);
        if (emission > 0)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static PhysicsMaterial MakePhysics(string name, float staticFriction, float dynamicFriction, float bounce)
    {
        string path = Root + "/Materials/" + name + ".physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial(name);
            AssetDatabase.CreateAsset(material, path);
        }
        material.staticFriction = staticFriction; material.dynamicFriction = dynamicFriction;
        material.bounciness = bounce;
        material.frictionCombine = PhysicsMaterialCombine.Minimum;
        material.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(material);
        return material;
    }
}
