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
    private static Material upperClear, upperCrystal, upperPurple, upperGlow;
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
        upperClear = MakeTransparentMaterial("DrawUpperClear", new Color(.72f, .91f, 1f, .18f), .91f);
        upperCrystal = MakeTransparentMaterial("DrawUpperCrystal", new Color(.90f, .97f, 1f, .64f), .88f);
        upperPurple = MakeMaterial("DrawUpperPurple", new Color(.51f, .16f, .78f), .12f, .18f);
        upperGlow = MakeMaterial("DrawUpperGlow", new Color(.88f, .97f, 1f), .02f, .32f);
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
        if (ballPrefab == null || green == null || surfacePhysics == null || upperClear == null) PrepareAssets();
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
        station.drawTimeout = 60f;
        station.usesBumpers = true;
        const float centerZ = -.55f;
        BuildUpperBumpers(station, centerZ);
        station.normalWinPocketTriggers = Array.Empty<Collider>();
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
        station.upperWinText = Label("DrawTitle", module, "00WIN（枚獲得）", new Vector3(0, 2.84f, 2.11f), new Vector2(5.3f, .48f), 3.8f, Color.white);
        BuildUpperColorPorts(station, centerZ);
        BuildUpperOutGuards(station, centerZ);
        AssetDatabase.SaveAssets();
        return station;
    }

    private static void BuildUpperBumpers(MedalBallLotteryStation station, float centerZ)
    {
        var parent = station.transform;
        // The reference uses a broad flat disk, with open front/rear OUT lanes.
        var floor = Cylinder("UpperBumperPlayFloor", parent, new Vector3(0f, .14f, centerZ), 2.43f, .12f, black, false);
        var floorCollider = floor.gameObject.AddComponent<MeshCollider>();
        floorCollider.sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
        floorCollider.sharedMaterial = surfacePhysics;
        BuildUpperRim(parent, centerZ);
        BuildUpperFloorSectors(parent, centerZ);
        var crystalMesh = CrystalCapMesh();
        station.bumpers = new MedalLotteryBumper[4];
        for (int i = 0; i < 4; i++)
        {
            float x = (i % 2 == 0 ? -1f : 1f) * .65f;
            float z = (i < 2 ? -1f : 1f) * .65f;
            Cylinder("BumperBlackFoot", parent, new Vector3(x, .27f, centerZ + z), .30f, .14f, black, false);
            Cylinder("BumperGoldCollar", parent, new Vector3(x, .38f, centerZ + z), .325f, .10f, gold, false);
            var sphere = Sphere("UpperBumper_" + i, parent, new Vector3(x, .52f, centerZ + z), Vector3.one * .64f, white, true);
            sphere.GetComponent<Renderer>().enabled = false;
            var bumper = sphere.gameObject.AddComponent<MedalLotteryBumper>();
            bumper.station = station;
            station.bumpers[i] = bumper;
            var cap = MeshDecoration("BumperCrystalCap_" + i, parent, new Vector3(x, .52f, centerZ + z), crystalMesh, upperCrystal);
            var core = MeshDecoration("BumperCrystalGlow_" + i, cap, Vector3.zero, crystalMesh, upperGlow);
            core.localScale = new Vector3(.72f, .80f, .72f);
        }
        var exit = Group("UpperPhysicalOutflow", parent);
        exit.localPosition = new Vector3(0f, .55f, centerZ - 2.95f);
        var exitCollider = exit.gameObject.AddComponent<BoxCollider>();
        exitCollider.size = new Vector3(2.48f, 3.40f, .38f);
        exitCollider.isTrigger = true;
        station.upperOutflow = exit.gameObject.AddComponent<MedalLotteryOutflow>();
        station.upperOutflow.station = station;
        station.bumperBowlOuterRadius = 2.48f;
        station.bumperBowlFloorY = -.75f;
    }

    private static void BuildUpperRim(Transform parent, float centerZ)
    {
        const int segments = 24;
        const float radius = 2.46f;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            // Three narrow color ports replace cardinal rim sections. The
            // broad front opening holds four independently retracting plates.
            if (i == 0 || i == 6 || i == 12 || (i >= 16 && i <= 20)) continue;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float width = radius * .275f;
            var lower = Box("UpperBlackRim", parent, new Vector3(0f, .26f, centerZ) + radial * radius,
                new Vector3(width, .17f, .12f), black, true);
            lower.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg - 90f, 0f);
            var trim = Box("UpperGoldRimTrim", parent, new Vector3(0f, .36f, centerZ) + radial * radius,
                new Vector3(width, .035f, .135f), gold, false);
            trim.localRotation = lower.localRotation;
            var guard = Box("UpperClearRimGuard", parent, new Vector3(0f, .62f, centerZ) + radial * radius,
                new Vector3(width, .50f, .075f), upperClear, true);
            guard.localRotation = lower.localRotation;
            if (i % 2 == 0)
                Sphere("UpperRimBolt", parent, new Vector3(0f, .365f, centerZ) + radial * (radius - .02f), Vector3.one * .09f, gold, false);
        }
    }

    private static void BuildUpperFloorSectors(Transform parent, float centerZ)
    {
        Vector3 position = new Vector3(0f, .207f, centerZ);
        MeshDecoration("UpperRedLeftSector", parent, position, SectorMesh("DrawUpperRedSector", .34f, 2.31f, 151f, 209f), ruby);
        MeshDecoration("UpperBlueRightSector", parent, position, SectorMesh("DrawUpperBlueSector", .34f, 2.31f, -29f, 29f), sapphire);
        MeshDecoration("UpperYellowRearSector", parent, position, SectorMesh("DrawUpperYellowRear", .34f, 2.31f, 61f, 119f), amber);
        MeshDecoration("UpperPurpleFrontSector", parent, position, SectorMesh("DrawUpperPurpleFront", .34f, 2.31f, 241f, 299f), upperPurple);
        for (int i = 0; i < 4; i++)
        {
            float start = 31f + i * 90f;
            MeshDecoration("UpperGoldFan_" + i, parent, position, SectorMesh("DrawUpperGoldFan_" + i, .34f, 2.31f, start, start + 28f), gold);
        }
        Cylinder("UpperCentralRedDisk", parent, new Vector3(0f, .219f, centerZ), .34f, .022f, ruby, false);
        Cylinder("UpperCentralGoldDot", parent, new Vector3(0f, .235f, centerZ), .065f, .013f, gold, false);
    }

    private static void BuildUpperColorPorts(MedalBallLotteryStation station, float centerZ)
    {
        station.colorRoutePockets = new MedalColorRoutePocket[3];
        station.colorGateBodies = new Rigidbody[3];
        station.colorGateClosedPositions = new Vector3[3];
        var colors = new[] { ruby, sapphire, amber };
        var directions = new[] { Vector3.left, Vector3.right, Vector3.forward };
        for (int i = 0; i < 3; i++)
        {
            var port = Group("UpperColorPort_" + (MedalJackpotKind)i, station.transform);
            port.localPosition = new Vector3(0f, 0f, centerZ) + directions[i] * 2.40f;
            port.localRotation = Quaternion.FromToRotation(Vector3.forward, directions[i]);
            // A level extension catches the sphere beyond the disk edge. Its
            // entrance is sealed by a real vertical gate until live WIN > 100.
            Box("ColorPortFloor", port, new Vector3(0f, .14f, .22f), new Vector3(.64f, .12f, .78f), colors[i], true);
            for (int side = -1; side <= 1; side += 2)
                Box("ColorPortSideRail", port, new Vector3(side * .36f, .59f, .18f), new Vector3(.08f, .78f, .80f), gold, true);
            var gate = Box("ColorGate_" + (MedalJackpotKind)i, port, new Vector3(0f, .65f, 0f),
                new Vector3(.68f, .90f, .12f), colors[i], true);
            var body = gate.gameObject.AddComponent<Rigidbody>();
            ConfigureKinematicBody(body);
            var trigger = Group("ColorRouteTrigger", port);
            trigger.localPosition = new Vector3(0f, .44f, .20f);
            var collider = trigger.gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(.56f, .90f, .30f);
            collider.isTrigger = true;
            var pocket = trigger.gameObject.AddComponent<MedalColorRoutePocket>();
            pocket.station = station;
            pocket.kind = (MedalJackpotKind)i;
            pocket.gateBody = body;
            station.colorRoutePockets[i] = pocket;
            station.colorGateBodies[i] = body;
            station.colorGateClosedPositions[i] = gate.localPosition;
            Box("ColorGateGoldTrim", gate, new Vector3(0f, .52f, 0f), new Vector3(.70f, .045f, .14f), gold, false);
        }
        station.colorGateRaiseHeight = 1.20f;
        station.colorGateMoveSpeed = 4f;
        // The upper disk is the only color selection stage; no transfer ball
        // or second front tray is generated.
        station.colorLaunchPoint = null;
        station.colorGuideLocalCenter = new Vector3(0f, 0f, centerZ);
        station.colorBowlOuterRadius = station.bumperBowlOuterRadius;
        station.colorBowlFloorY = station.bumperBowlFloorY;
    }

    private static void BuildUpperOutGuards(MedalBallLotteryStation station, float centerZ)
    {
        station.outBlocks = new MedalOutBlock[4];
        station.outBlockBodies = new Rigidbody[4];
        station.outBlockClosedPositions = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float x = -.90f + i * .60f;
            var plate = Box("WhiteOutBlock_" + i, station.transform, new Vector3(x, .55f, centerZ - 2.44f),
                new Vector3(.56f, 1.20f, .12f), white, true);
            var body = plate.gameObject.AddComponent<Rigidbody>();
            ConfigureKinematicBody(body);
            var block = plate.gameObject.AddComponent<MedalOutBlock>();
            block.station = station;
            block.guardIndex = i;
            station.outBlocks[i] = block;
            station.outBlockBodies[i] = body;
            station.outBlockClosedPositions[i] = plate.localPosition;
            Box("OutBlockRetractionHousing_" + i, station.transform, new Vector3(x, -.91f, centerZ - 2.44f),
                new Vector3(.59f, 1.65f, .24f), black, false);
        }
        // Single aliases remain available for existing tooling; all four
        // independent plates protect the same front OUT lane.
        station.outBlock = station.outBlocks[0];
        station.outBlockBody = station.outBlockBodies[0];
        station.outBlockClosedPosition = station.outBlockClosedPositions[0];
        station.outflow = station.upperOutflow;
        station.outflows = new[] { station.upperOutflow };
        station.outBlockText = null;
        station.outBlockRetractionDelay = .18f;
        station.outBlockRaiseHeight = -1.65f;
    }

    private static void ConfigureKinematicBody(Rigidbody body)
    {
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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
        station.launchPoint = Group("LaunchPoint", station.isUpperStation ? station.transform : station.rotor);
        station.launchPoint.localPosition = station.isUpperStation
            ? new Vector3(0f, 1.24f, centerZ) : new Vector3(2.06f, 1.24f, -.05f);
        if (station.isUpperStation) station.launchPositionJitter = Vector3.zero;
        station.launchVelocityRelativeToPoint = true;
        Box("RotatingBallSpout", station.launchPoint, new Vector3(0, .26f, 0), new Vector3(.62f, .13f, .55f), accent, false);
        station.localLaunchVelocity = station.usesBumpers ? new Vector3(-1.45f, 0f, .45f) : new Vector3(-.45f, 0f, 1.35f);
        station.launchVelocityJitter = station.usesBumpers ? new Vector3(.20f, .05f, .25f) : new Vector3(.15f, .05f, .50f);
        station.guideLocalCenter = new Vector3(0, 0, centerZ);
        station.guideAcceleration = station.isUpperStation && station.usesBumpers ? .85f : .30f;
        if (station.isUpperStation && station.usesBumpers)
        {
            station.upperMinimumHorizontalSpeed = 1.35f;
            station.upperSpeedMaintenanceAcceleration = 1.7f;
            station.upperStallSpeed = .22f;
            station.upperStallDuration = 1.35f;
            station.upperStallDisplacement = .12f;
        }
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
        if (!station.usesBumpers)
            Cylinder("RotorHub", rotor, Vector3.zero, .20f, .12f, accent, false);
        if (!station.usesBumpers)
        {
            for (int side = 1; side >= (doubleArm ? -1 : 1); side -= 2)
            {
                Box("RotatingPaddle", rotor, new Vector3(side * 1.38f, .12f, 0), new Vector3(1.12f, .15f, .14f), gold, true);
                Box("OpenArm", rotor, new Vector3(side * .42f, .20f, 0), new Vector3(.84f, .09f, .10f), accent, false);
            }
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

    private static Mesh SectorMesh(string name, float innerRadius, float outerRadius, float startDegrees, float endDegrees)
    {
        const int steps = 16;
        var vertices = new List<Vector3>((steps + 1) * 2);
        var triangles = new List<int>(steps * 6);
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)steps) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices.Add(direction * outerRadius);
            vertices.Add(direction * innerRadius);
            if (i == steps) continue;
            int next = (i + 1) * 2;
            triangles.Add(i * 2); triangles.Add(i * 2 + 1); triangles.Add(next);
            triangles.Add(next); triangles.Add(i * 2 + 1); triangles.Add(next + 1);
        }
        return SaveDecorativeMesh(name, vertices, triangles);
    }

    private static Mesh CrystalCapMesh()
    {
        const int sides = 12;
        var radii = new[] { .30f, .32f, .27f, .155f, 0f };
        var heights = new[] { -.10f, 0f, .16f, .28f, .32f };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int ring = 0; ring < radii.Length - 1; ring++)
        for (int side = 0; side < sides; side++)
        {
            float angle = side * Mathf.PI * 2f / sides;
            float nextAngle = (side + 1) * Mathf.PI * 2f / sides;
            Vector3 lower = new Vector3(Mathf.Cos(angle) * radii[ring], heights[ring], Mathf.Sin(angle) * radii[ring]);
            Vector3 nextLower = new Vector3(Mathf.Cos(nextAngle) * radii[ring], heights[ring], Mathf.Sin(nextAngle) * radii[ring]);
            Vector3 upper = new Vector3(Mathf.Cos(angle) * radii[ring + 1], heights[ring + 1], Mathf.Sin(angle) * radii[ring + 1]);
            Vector3 nextUpper = new Vector3(Mathf.Cos(nextAngle) * radii[ring + 1], heights[ring + 1], Mathf.Sin(nextAngle) * radii[ring + 1]);
            if (radii[ring + 1] > 0f) AddFacet(vertices, triangles, lower, upper, nextUpper);
            AddFacet(vertices, triangles, lower, nextUpper, nextLower);
        }
        return SaveDecorativeMesh("DrawUpperCrystalCap", vertices, triangles);
    }

    private static void AddFacet(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
    }

    private static Mesh SaveDecorativeMesh(string name, List<Vector3> vertices, List<int> triangles)
    {
        string path = Root + "/Meshes/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool create = mesh == null;
        if (create) mesh = new Mesh { name = name };
        else mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static Transform MeshDecoration(string name, Transform parent, Vector3 position, Mesh mesh, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
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

    private static TMP_Text Label(string name, Transform parent, string content, Vector3 position, Vector2 size, float fontSize, Color color, Quaternion? rotation = null)
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
        return text;
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

    private static Material MakeTransparentMaterial(string name, Color color, float smoothness)
    {
        var material = MakeMaterial(name, color, 0f);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_AlphaToMask", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_ReceiveShadows", 0f);
        material.SetFloat("_Smoothness", smoothness);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.renderQueue = (int)RenderQueue.Transparent;
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
