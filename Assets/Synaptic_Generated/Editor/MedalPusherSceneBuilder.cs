using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Rebuilds only the generated parts of the existing medal game scene.</summary>
public static class MedalPusherSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Scene_20261004_125922.unity";
    public const string AssetRoot = "Assets/Synaptic_Generated/MedalPusher";
    private const string CabinetScene = "Assets/Scenes/Scene_20261004_125057.unity";
    private static Material board, steel, gold, cyan, pink, shell;

    [MenuItem("Tools/Medal Pusher/Toggle Cabinet View")]
    public static void ToggleCabinetView()
    {
        var view = UnityEngine.Object.FindFirstObjectByType<MedalPusherCameraView>();
        if (view != null) view.ToggleView();
    }

    [MenuItem("Tools/Medal Pusher/Complete Remaining Tasks")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before building the scene.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open the existing medal game scene first: " + ScenePath);
        Directory.CreateDirectory(AssetRoot + "/Materials");
        Directory.CreateDirectory(AssetRoot + "/Prefabs");
        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        RegisterTag("Medal");
        RegisterTag("Prize");

        board = MakeMaterial("Board", new Color(.075f, .12f, .19f), .45f, .38f);
        steel = MakeMaterial("PusherSteel", new Color(.36f, .45f, .55f), .8f, .6f);
        gold = MakeMaterial("MedalGold", new Color(1f, .65f, .12f), .8f, .55f);
        cyan = MakeMaterial("NeonCyan", new Color(.08f, .85f, 1f), .25f, .5f, true);
        pink = MakeMaterial("NeonPink", new Color(1f, .08f, .5f), .25f, .5f, true);
        shell = MakeMaterial("Cabinet", new Color(.83f, .86f, .88f), .45f, .6f);
        var physics = MakePhysicsMaterial("MedalPhysics", .3f, .23f, .015f);
        var surfacePhysics = MakePhysicsMaterial("BoardPhysics", .32f, .25f, 0f);

        var gameObject = FindRoot(scene, "MedalPusher_Game") ?? new GameObject("MedalPusher_Game");
        Undo.RegisterFullObjectHierarchyUndo(gameObject, "Complete medal pusher");
        var generated = gameObject.transform.Find("GeneratedPlayfield");
        if (generated != null) UnityEngine.Object.DestroyImmediate(generated.gameObject);
        var oldPlate = gameObject.transform.Find("PusherPlate");
        if (oldPlate != null) UnityEngine.Object.DestroyImmediate(oldPlate.gameObject);
        var field = Child("GeneratedPlayfield", gameObject.transform);
        var deck = Cube("PusherBoard", field, new Vector3(0, -.15f, -.4f), new Vector3(10, .3f, 8.2f), board);
        deck.GetComponent<Collider>().sharedMaterial = surfacePhysics;
        Cube("Wall_Left", field, new Vector3(-5.08f, .62f, -.4f), new Vector3(.16f, 1.55f, 8.2f), steel);
        Cube("Wall_Right", field, new Vector3(5.08f, .62f, -.4f), new Vector3(.16f, 1.55f, 8.2f), steel);
        Cube("Wall_Back", field, new Vector3(0, .65f, 3.85f), new Vector3(10.32f, 1.6f, .3f), steel);
        Decoration("DeckNeon_Left", field, new Vector3(-4.95f, .025f, -.4f), new Vector3(.055f, .055f, 8.1f), cyan);
        Decoration("DeckNeon_Right", field, new Vector3(4.95f, .025f, -.4f), new Vector3(.055f, .055f, 8.1f), pink);
        Decoration("CollectionEdge", field, new Vector3(0, -.02f, -4.5f), new Vector3(10, .045f, .065f), cyan);

        var plate = Cube("PusherPlate", gameObject.transform, new Vector3(0, .27f, 1.8f), new Vector3(9.6f, .5f, 2f), steel);
        plate.GetComponent<Collider>().sharedMaterial = surfacePhysics;
        var pusherBody = plate.AddComponent<Rigidbody>();
        pusherBody.isKinematic = true;
        pusherBody.useGravity = false;
        pusherBody.interpolation = RigidbodyInterpolation.Interpolate;
        pusherBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        PrefabUtility.SaveAsPrefabAsset(plate, AssetRoot + "/Prefabs/PusherPlate.prefab");

        var coin = CreateMedal(physics);
        var game = GetOrAdd<MedalPusherGame>(gameObject);
        var manager = GetOrAdd<PrizeDropManager>(gameObject);
        var input = GetOrAdd<MedalInputHandler>(gameObject);
        var audio = GetOrAdd<AudioSource>(gameObject);
        audio.playOnAwake = false;
        audio.spatialBlend = 0f;
        audio.volume = .6f;
        game.startingMedals = 100;
        game.medalsPerThrow = 1;
        game.throwInterval = .25f;
        game.pusherSpeed = 1.25f;
        game.pusherRange = .85f;
        game.maxMedalsOnBoard = 1024;
        game.maxPrizesOnBoard = 8;
        game.medalSpawnHeight = 2.4f;
        game.medalPrefab = coin;
        game.prizePrefabs = Array.Empty<GameObject>();
        game.bonusMedalPrefabs = new[] { coin };
        game.pusherBody = pusherBody;
        game.medalSpawnPoint = Child("MedalSpawnPoint", field);
        game.medalSpawnPoint.localPosition = new Vector3(0, 2.4f, .6f);
        game.itemsRoot = Child("Items", field);
        game.medalDropSound = LoadAudio("MedalDrop");
        game.medalImpactSound = LoadAudio("MedalImpact");
        game.prizeDropSound = LoadAudio("PrizeWin");
        game.bonusSound = LoadAudio("Bonus");
        game.comboSound = LoadAudio("Combo");
        game.jackpotSound = LoadAudio("Jackpot");
        game.prizeDropEffect = CreateEffect();
        game.comboEffect = game.prizeDropEffect;
        game.jackpotEffect = game.prizeDropEffect;
        input.game = game;
        manager.spawnGenericPrizes = false;
        manager.minPrizesOnBoard = 0;
        manager.maxPrizesOnBoard = 0;
        manager.prizeSpawnInterval = 5f;
        manager.dropZoneCenter = new Vector3(0, -.9f, -5.15f);
        manager.dropZoneSize = new Vector2(10.2f, 1.7f);
        manager.dropYPosition = -.2f;
        manager.prizeTypes = Array.Empty<PrizeDropManager.PrizeType>();
        for (int row = 0; row < 7; row++)
            for (int col = 0; col < 12; col++)
            {
                var medal = (GameObject)PrefabUtility.InstantiatePrefab(coin, scene);
                medal.name = "Medal_" + row + "_" + col;
                medal.transform.SetParent(game.itemsRoot);
                medal.transform.position = new Vector3((col - 5.5f) * .72f, .075f, -4.06f + row * .66f);
            }
        var dropZone = FindRoot(scene, "MedalDropZone") ?? new GameObject("MedalDropZone");
        dropZone.transform.position = manager.dropZoneCenter;
        dropZone.transform.rotation = Quaternion.identity;
        dropZone.transform.localScale = Vector3.one;
        var renderer = dropZone.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = false;
        var trigger = GetOrAdd<BoxCollider>(dropZone);
        trigger.size = new Vector3(10.2f, 1.35f, 1.7f);
        trigger.isTrigger = true;
        GetOrAdd<MedalDropDetector>(dropZone).game = game;

        ImportCabinet(scene, field);
        TreasureCabinetDecoration.Build(field);
        ConfigureCameraAndLighting(scene, field);
        BuildUI(scene, game);
        MedalPusherExpansionBuilder.ApplyToScene(scene, game);
        var legacyAudio = FindRoot(scene, "MedalPusherAudioMixer_AudioController");
        if (legacyAudio != null)
        {
            var source = legacyAudio.GetComponent<AudioSource>();
            if (source != null) source.playOnAwake = false;
        }
        // The playable scene is first; retain the tutorial's existing build entry.
        var buildScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = gameObject;
        Validate();
        Debug.Log("[MedalPusher] Remaining tasks built and saved in " + ScenePath);
    }

    [MenuItem("Tools/Medal Pusher/Validate Scene")]
    public static void Validate()
    {
        var errors = new List<string>();
        var game = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>();
        if (game == null) errors.Add("Game controller missing");
        else
        {
            if (game.medalPrefab == null || game.medalPrefab.GetComponent<MedalItem>() == null || game.medalPrefab.GetComponent<Rigidbody>() == null)
                errors.Add("Medal prefab missing physics/item component");
            if (game.medalPrefab != null && (game.medalPrefab.GetComponent<Collider>() == null || game.medalPrefab.GetComponent<Collider>().sharedMaterial == null))
                errors.Add("Medal physics material missing");
            if (game.pusherBody == null || !game.pusherBody.isKinematic) errors.Add("Kinematic pusher missing");
            if (game.prizePrefabs == null || game.prizePrefabs.Length != 0) errors.Add("Generic prize prefabs must stay empty");
            if (game.itemsRoot != null && game.itemsRoot.GetComponentsInChildren<MedalItem>(true).Any(i => i.isPrize && !i.isBall))
                errors.Add("Only lottery balls may remain as board prizes");
            var manager = game.GetComponent<PrizeDropManager>();
            if (manager == null || manager.spawnGenericPrizes || manager.minPrizesOnBoard != 0 || manager.maxPrizesOnBoard != 0 || manager.prizeTypes == null || manager.prizeTypes.Length != 0)
                errors.Add("Generic prize replenishment must stay disabled");
            if (new[] { game.medalDropSound, game.medalImpactSound, game.prizeDropSound, game.bonusSound, game.comboSound, game.jackpotSound }.Any(c => c == null))
                errors.Add("Six SE clips must be assigned");
            if (game.itemsRoot == null || game.medalSpawnPoint == null) errors.Add("Spawn/items references missing");
        }
        var detector = UnityEngine.Object.FindFirstObjectByType<MedalDropDetector>();
        if (detector == null || detector.GetComponent<Collider>() == null || !detector.GetComponent<Collider>().isTrigger) errors.Add("Collection trigger missing");
        if (UnityEngine.Object.FindFirstObjectByType<MedalPusherUI>() == null || UnityEngine.Object.FindFirstObjectByType<InputSystemUIInputModule>() == null)
            errors.Add("UI or Input System module missing");
        if (game != null && new[] { "Wall_Left", "Wall_Right", "Wall_Back" }.Any(n => game.transform.Find("GeneratedPlayfield/" + n) == null))
            errors.Add("Playfield walls missing");
        var missing = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        if (missing > 0) errors.Add("Missing scripts: " + missing);
        var report = new { scene = SceneManager.GetActiveScene().path, success = errors.Count == 0, errors, prizePrefabs = game == null ? 0 : game.prizePrefabs.Length, items = game == null || game.itemsRoot == null ? 0 : game.itemsRoot.childCount };
        WriteReport("scene-validation.json", JsonUtilitySafe(report));
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
        Debug.Log("[MedalPusher] Scene validation passed.");
    }

    public static void WriteReport(string filename, string json)
    {
        var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".codex-backups/medal-resume-20261004");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, filename), json);
    }

    private static string JsonUtilitySafe(object value) => Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented);
    private static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
    private static T GetOrAdd<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    private static Transform Child(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }
    private static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }
    private static void Decoration(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = Cube(name, parent, position, scale, material);
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    private static Material MakeMaterial(string name, Color color, float metallic, float smoothness, bool emission = false)
    {
        string path = AssetRoot + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 2f); }
        EditorUtility.SetDirty(material);
        return material;
    }
    private static PhysicsMaterial MakePhysicsMaterial(string name, float staticFriction, float dynamicFriction, float bounce)
    {
        var path = AssetRoot + "/Materials/" + name + ".physicMaterial";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (mat == null) { mat = new PhysicsMaterial(name); AssetDatabase.CreateAsset(mat, path); }
        mat.staticFriction = staticFriction;
        mat.dynamicFriction = dynamicFriction;
        mat.bounciness = bounce;
        mat.frictionCombine = PhysicsMaterialCombine.Average;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(mat);
        return mat;
    }
    private static Mesh CoinMesh()
    {
        string path = AssetRoot + "/Meshes/MedalDisk.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        const int segments = 32;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        vertices.Add(new Vector3(0, -.05f, 0));
        vertices.Add(new Vector3(0, .05f, 0));
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            vertices.Add(new Vector3(Mathf.Cos(angle) * .31f, -.05f, Mathf.Sin(angle) * .31f));
            vertices.Add(new Vector3(Mathf.Cos(angle) * .31f, .05f, Mathf.Sin(angle) * .31f));
        }
        for (int i = 0; i < segments; i++)
        {
            int b = 2 + i * 2, t = b + 1, nb = 2 + ((i + 1) % segments) * 2, nt = nb + 1;
            triangles.AddRange(new[] { 0, b, nb, 1, nt, t, b, t, nt, b, nt, nb });
        }
        var mesh = new Mesh { name = "MedalDisk" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
    private static GameObject CreateMedal(PhysicsMaterial physics)
    {
        var go = new GameObject("Medal");
        go.tag = "Medal";
        go.AddComponent<MeshFilter>().sharedMesh = CoinMesh();
        go.AddComponent<MeshRenderer>().sharedMaterial = gold;
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = CoinMesh();
        collider.convex = true;
        collider.sharedMaterial = physics;
        var medalBody = go.AddComponent<Rigidbody>();
        ConfigureBody(medalBody, .04f);
        // Speculative CCD supports the convex disk's mesh collider.
        medalBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var item = go.AddComponent<MedalItem>();
        item.isPrize = false;
        item.displayName = "Medal";
        item.pointValue = 10;
        var stamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stamp.name = "MedalStamp";
        stamp.transform.SetParent(go.transform, false);
        stamp.transform.localPosition = new Vector3(0, .052f, 0);
        stamp.transform.localScale = new Vector3(.4f, .002f, .4f);
        stamp.GetComponent<Renderer>().sharedMaterial = steel;
        UnityEngine.Object.DestroyImmediate(stamp.GetComponent<Collider>());
        var result = PrefabUtility.SaveAsPrefabAsset(go, AssetRoot + "/Prefabs/Medal.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        return result;
    }
    private static void ConfigureBody(Rigidbody body, float mass)
    {
        body.mass = mass;
        body.useGravity = true;
        body.isKinematic = false;
        body.linearDamping = .15f;
        body.angularDamping = .7f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.solverIterations = 12;
        body.solverVelocityIterations = 4;
    }
    private static AudioClip LoadAudio(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetRoot + "/Audio/" + name + ".wav");
        if (clip == null) throw new InvalidOperationException("Missing SE: " + name);
        return clip;
    }
    private static GameObject CreateEffect()
    {
        var go = new GameObject("PrizeConfetti");
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.duration = .9f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = .8f;
        main.startSpeed = 2.5f;
        main.startSize = .13f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .65f, .1f), new Color(.1f, .9f, 1));
        main.gravityModifier = .45f;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = particles.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0, 32) });
        var shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = .1f;
        var materialPath = AssetRoot + "/Materials/Confetti.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        var result = PrefabUtility.SaveAsPrefabAsset(go, AssetRoot + "/Prefabs/PrizeConfetti.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        return result;
    }
    private static void RegisterTag(string name)
    {
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var array = tags.FindProperty("tags");
        for (int i = 0; i < array.arraySize; i++) if (array.GetArrayElementAtIndex(i).stringValue == name) return;
        array.InsertArrayElementAtIndex(array.arraySize);
        array.GetArrayElementAtIndex(array.arraySize - 1).stringValue = name;
        tags.ApplyModifiedProperties();
    }
    private static void ImportCabinet(Scene target, Transform field)
    {
        var cabinet = Child("Cabinet", field);
        var source = EditorSceneManager.OpenScene(CabinetScene, OpenSceneMode.Additive);
        try
        {
            foreach (var original in source.GetRootGameObjects())
            {
                if (!(original.name.StartsWith("Cabinet_") || original.name.StartsWith("NeonLight_") || new[] { "CoinSlot", "PlayButton", "PrizeChute", "ScoreDisplay" }.Contains(original.name))) continue;
                var go = UnityEngine.Object.Instantiate(original);
                go.name = original.name;
                SceneManager.MoveGameObjectToScene(go, target);
                go.transform.SetParent(cabinet, true);
                foreach (var collider in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
                if (go.GetComponent<Renderer>() != null) go.GetComponent<Renderer>().sharedMaterial = go.name.StartsWith("Neon") ? (go.name.Contains("Right") ? pink : gold) : shell;
                switch (go.name)
                {
                    case "Cabinet_TopPanel": Pose(go, new Vector3(0, 3.55f, 3.6f), new Vector3(11.6f, .35f, 1.3f)); go.GetComponent<Renderer>().sharedMaterial = gold; break;
                    case "Cabinet_SideLeft": Pose(go, new Vector3(-5.6f, -.45f, -.4f), new Vector3(.45f, 2.9f, 9.5f)); break;
                    case "Cabinet_SideRight": Pose(go, new Vector3(5.6f, -.45f, -.4f), new Vector3(.45f, 2.9f, 9.5f)); break;
                    case "Cabinet_BackPanel": Pose(go, new Vector3(0, 1.25f, 4.25f), new Vector3(11.6f, 4.3f, .4f)); break;
                    case "Cabinet_FrontPanel": Pose(go, new Vector3(0, -1.25f, -5.25f), new Vector3(11.6f, 1.3f, .4f)); break;
                    case "Cabinet_BottomPanel": Pose(go, new Vector3(0, -1.95f, -.5f), new Vector3(11.6f, .35f, 10.5f)); break;
                    case "PrizeChute": Pose(go, new Vector3(0, -1.35f, -5.55f), new Vector3(4.5f, .18f, 1.1f)); break;
                    case "PlayButton": Pose(go, new Vector3(-3.6f, -.51f, -5.3f), new Vector3(1.2f, .15f, .7f)); go.GetComponent<Renderer>().sharedMaterial = pink; break;
                    case "CoinSlot": Pose(go, new Vector3(3.6f, -.51f, -5.3f), new Vector3(1.2f, .15f, .7f)); go.GetComponent<Renderer>().sharedMaterial = steel; break;
                    case "ScoreDisplay": Pose(go, new Vector3(0, 2.7f, 3.98f), new Vector3(6, 1, .12f)); break;
                    case "NeonLight_Front": Pose(go, new Vector3(0, -.52f, -5.5f), new Vector3(11, .055f, .06f)); break;
                    case "NeonLight_Back": Pose(go, new Vector3(0, 3.32f, 4f), new Vector3(11, .06f, .06f)); break;
                    case "NeonLight_Left": Pose(go, new Vector3(-5.36f, 1.4f, 4f), new Vector3(.06f, 3.8f, .06f)); break;
                    case "NeonLight_Right": Pose(go, new Vector3(5.36f, 1.4f, 4f), new Vector3(.06f, 3.8f, .06f)); break;
                    case "NeonLight_Bottom": Pose(go, new Vector3(0, -1.72f, -5.5f), new Vector3(11, .055f, .06f)); break;
                }
            }
        }
        finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(target); }
        var marquee = new GameObject("CabinetTitle", typeof(TextMeshPro));
        marquee.transform.SetParent(cabinet, false);
        marquee.transform.localPosition = new Vector3(0, 2.7f, 3.83f);
        marquee.transform.localRotation = Quaternion.identity;
        var text = marquee.GetComponent<TextMeshPro>();
        text.font = Font(); text.text = "− | − | −"; text.fontSize = 4.3f;
        text.alignment = TextAlignmentOptions.Center; text.color = new Color(1, .82f, .25f);
        text.rectTransform.sizeDelta = new Vector2(5.8f, 1.1f);
        var display = cabinet.Find("ScoreDisplay");
        if (display != null) display.GetComponent<Renderer>().sharedMaterial = board;
        foreach (float x in new[] { -5.35f, 5.35f })
        {
            Decoration("WhitePillar_" + x, cabinet, new Vector3(x, 1.7f, 3.4f), new Vector3(.32f, 3.8f, .4f), shell);
            Decoration("GoldPillarTrim_" + x, cabinet, new Vector3(x, 1.7f, 3.13f), new Vector3(.12f, 3.8f, .08f), gold);
            Decoration("SideGoldRail_" + x, cabinet, new Vector3(x, .9f, -.4f), new Vector3(.2f, .18f, 8.6f), gold);
        }
        Decoration("ArcadeFloor", field, new Vector3(0, -2.25f, -.5f), new Vector3(40, .2f, 40), board);
    }
    private static void Pose(GameObject go, Vector3 position, Vector3 scale)
    { go.transform.localPosition = position; go.transform.localRotation = Quaternion.identity; go.transform.localScale = scale; }
    private static void ConfigureCameraAndLighting(Scene scene, Transform field)
    {
        var cameraGo = FindRoot(scene, "Main Camera") ?? new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        var camera = GetOrAdd<Camera>(cameraGo);
        cameraGo.tag = "MainCamera";
        cameraGo.transform.position = new Vector3(0f, 10.3f, -17.2f);
        cameraGo.transform.LookAt(new Vector3(0, 1.7f, 1.4f));
        camera.fieldOfView = 48;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 100;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.014f, .016f, .033f);
        GetOrAdd<AudioListener>(cameraGo);
        var view = GetOrAdd<MedalPusherCameraView>(cameraGo);
        view.targetCamera = camera;
        view.overview = false;
        view.ApplyView();
        var lightGo = FindRoot(scene, "Directional Light") ?? new GameObject("Directional Light", typeof(Light));
        var light = GetOrAdd<Light>(lightGo);
        light.type = LightType.Directional;
        light.intensity = 1.4f;
        light.color = new Color(.8f, .87f, 1);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(55, -25, 0);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.25f, .28f, .4f);
        var point = new GameObject("PlayfieldLight", typeof(Light));
        point.transform.SetParent(field, false); point.transform.localPosition = new Vector3(0, 3, -.5f);
        var pl = point.GetComponent<Light>(); pl.type = LightType.Point; pl.range = 13; pl.intensity = 5; pl.color = new Color(1, .78f, .45f);
    }
    private static TMP_FontAsset Font() => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/SourceFiles/Fonts/Inter-Variable SDF.asset") ?? TMP_Settings.defaultFontAsset;
    private static void BuildUI(Scene scene, MedalPusherGame game)
    {
        var old = FindRoot(scene, "MedalPusher_UI");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        var go = new GameObject("MedalPusher_UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var ui = go.AddComponent<MedalPusherUI>();
        Panel("Header", go.transform, new Vector2(.025f, .865f), new Vector2(.975f, .975f), new Color(.025f, .03f, .075f, .94f));
        Text("Title", go.transform, "スロット", new Vector2(.045f, .885f), new Vector2(.49f, .96f), 34, new Color(1, .8f, .25f));
        Text("ScoreLabel", go.transform, "SCORE", new Vector2(.53f, .928f), new Vector2(.69f, .96f), 20, Color.gray);
        ui.scoreText = Text("ScoreValue", go.transform, "000000", new Vector2(.53f, .882f), new Vector2(.72f, .93f), 38, Color.yellow);
        Text("MedalLabel", go.transform, "MEDALS", new Vector2(.78f, .928f), new Vector2(.94f, .96f), 20, Color.gray);
        ui.medalsText = Text("MedalValue", go.transform, "0100", new Vector2(.78f, .882f), new Vector2(.94f, .93f), 38, Color.cyan);
        Panel("ControlsPanel", go.transform, new Vector2(.025f, .03f), new Vector2(.58f, .11f), new Color(.025f, .03f, .075f, .9f));
        Text("Controls", go.transform, "HOLD SPACE / LEFT CLICK  |  V: CABINET VIEW", new Vector2(.042f, .04f), new Vector2(.565f, .1f), 22, Color.white);
        var viewPanel = Panel("ViewButton", go.transform, new Vector2(.605f, .035f), new Vector2(.74f, .115f), new Color(.32f, .23f, .08f, .98f));
        var viewButton = viewPanel.gameObject.AddComponent<Button>();
        viewButton.targetGraphic = viewPanel.GetComponent<Image>();
        viewPanel.GetComponent<Image>().raycastTarget = true;
        UnityEventTools.AddPersistentListener(viewButton.onClick, UnityEngine.Object.FindFirstObjectByType<MedalPusherCameraView>().ToggleView);
        Text("ViewLabel", viewPanel, "VIEW", Vector2.zero, Vector2.one, 26, Color.white).alignment = TextAlignmentOptions.Center;
        var buttonPanel = Panel("DropMedalButton", go.transform, new Vector2(.76f, .035f), new Vector2(.975f, .115f), new Color(.08f, .55f, .65f, .98f));
        var button = buttonPanel.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonPanel.GetComponent<Image>();
        UnityEventTools.AddPersistentListener(button.onClick, game.ThrowSingleMedal);
        Text("DropLabel", buttonPanel, "DROP MEDAL", Vector2.zero, Vector2.one, 30, Color.white).alignment = TextAlignmentOptions.Center;
        var notification = Panel("PrizeNotification", go.transform, new Vector2(.3f, .73f), new Vector2(.7f, .81f), new Color(.04f, .04f, .09f, .94f));
        ui.prizeCanvas = notification.gameObject.AddComponent<Canvas>();
        ui.prizeDisplay = Text("PrizeText", notification, "", Vector2.zero, Vector2.one, 30, Color.white);
        ui.prizeDisplay.alignment = TextAlignmentOptions.Center;
        var jackpot = Panel("JackpotPanel", go.transform, new Vector2(.25f, .45f), new Vector2(.75f, .58f), new Color(.18f, .025f, .19f, .92f));
        ui.jackpotPanel = jackpot.gameObject;
        ui.jackpotText = Text("JackpotText", jackpot, "JACKPOT", Vector2.zero, Vector2.one, 45, Color.yellow);
        ui.jackpotText.alignment = TextAlignmentOptions.Center;
        jackpot.gameObject.SetActive(false);
        var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null) eventSystem = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        var legacy = eventSystem.GetComponent<StandaloneInputModule>();
        if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
        var module = GetOrAdd<InputSystemUIInputModule>(eventSystem.gameObject);
        module.AssignDefaultActions();
    }
    private static Transform Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false); SetRect(go.GetComponent<RectTransform>(), min, max);
        go.GetComponent<Image>().color = color;
        go.GetComponent<Image>().raycastTarget = name == "DropMedalButton";
        return go.transform;
    }
    private static TextMeshProUGUI Text(string name, Transform parent, string content, Vector2 min, Vector2 max, float size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false); SetRect(go.GetComponent<RectTransform>(), min, max);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = Font(); text.text = content; text.fontSize = size;
        text.color = color; text.alignment = TextAlignmentOptions.MidlineLeft; text.raycastTarget = false;
        return text;
    }
    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
}
