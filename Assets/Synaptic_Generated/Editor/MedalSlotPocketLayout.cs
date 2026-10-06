using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MedalSlotPocketLayout
{
    public const float PocketTiltDegrees = -30f;
    public const float PocketHeightAbovePlate = .30f;
    public const float PocketFrontInset = .55f;
    public const float PocketHoleRadius = .40f;
    private const string MeshRoot = "Assets/Synaptic_Generated/MedalPusher/Meshes/";

    public static Renderer[] Build(MedalPusherGame game, MedalSlotJackpotController controller)
    {
        if (game == null || controller == null || game.pusherBody == null)
            throw new InvalidOperationException("Game, slot controller and moving pusher plate are required.");
        MedalPusherPlateHoleBuilder.Build(game, PocketFrontInset, PocketHoleRadius);
        var field = game.transform.Find("GeneratedPlayfield");
        var old = field.Find("MedalInlets");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var oldMounted = game.pusherBody.transform.Find("MedalInlets");
        if (oldMounted != null) UnityEngine.Object.DestroyImmediate(oldMounted.gameObject);
        var root = new GameObject("MedalInlets").transform;
        root.SetParent(field, false);
        var plateCollider = game.pusherBody.GetComponent<Collider>();
        if (plateCollider == null) throw new InvalidOperationException("A pusher plate collider is required.");
        var bounds = plateCollider.bounds;
        Vector3 frontTop = game.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.min.z));
        root.localPosition = new Vector3(frontTop.x, frontTop.y + PocketHeightAbovePlate, frontTop.z + PocketFrontInset);
        // Keep metre-sized mouths while following the plate, whose primitive transform is scaled.
        root.SetParent(game.pusherBody.transform, true);
        var gold = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synaptic_Generated/MedalPusher/Materials/MedalGold.mat");
        var steel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Synaptic_Generated/MedalPusher/Materials/PusherSteel.mat");
        var physics = plateCollider.sharedMaterial;
        var rimMesh = StoreMesh("MountedSlotGoldRim", BuildRimMesh());
        var collarMesh = StoreMesh("MountedSlotSteelCollar", BuildCollarMesh());
        var cheekMesh = StoreMesh("MountedSlotSupportCheek", BuildSupportCheekMesh());
        var lights = new Renderer[3];
        game.medalInlets = new Transform[3];
        string[] names = { "LEFT", "CENTER", "RIGHT" };
        for (int i = 0; i < 3; i++)
        {
            var pocket = new GameObject("Inlet_" + names[i]).transform;
            pocket.SetParent(root, false);
            pocket.localPosition = new Vector3((i - 1) * 3.2f, 0f, 0f);
            pocket.localRotation = Quaternion.Euler(PocketTiltDegrees, 0f, 0f);
            lights[i] = MeshPart("SelectionLight", pocket, rimMesh, gold);
            MeshPart("SlantedMountingCollar", pocket, collarMesh, steel);
            // A hollow solid collar supports the rim; its centre remains open for medals.
            for (int segment = 0; segment < 32; segment++)
            {
                float angle = (segment + .5f) * Mathf.PI * 2f / 32;
                var solid = new GameObject("MountCollarCollider").transform;
                solid.SetParent(pocket, false);
                solid.localPosition = new Vector3(Mathf.Cos(angle) * .5325f, -.035f, Mathf.Sin(angle) * .5325f);
                solid.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                var collider = solid.gameObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(.215f, .06f, .12f);
                collider.sharedMaterial = physics;
            }
            var tongue = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tongue.name = "PlateMountingTongue";
            tongue.transform.SetParent(root, false);
            tongue.transform.localPosition = new Vector3((i - 1) * 3.2f, -PocketHeightAbovePlate - .035f, .59f);
            tongue.transform.localScale = new Vector3(1.28f, .10f, .24f);
            tongue.GetComponent<Renderer>().sharedMaterial = steel;
            tongue.GetComponent<Collider>().sharedMaterial = physics;
            for (int side = -1; side <= 1; side += 2)
            {
                var cheek = MeshPart("SlantedSupportCheek", root, cheekMesh, steel).gameObject;
                cheek.transform.localPosition = new Vector3((i - 1) * 3.2f + side * .565f, 0f, 0f);
                var support = cheek.AddComponent<MeshCollider>();
                support.sharedMesh = cheekMesh; support.convex = true; support.sharedMaterial = physics;
                var bolt = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bolt.name = "MountingBolt";
                bolt.transform.SetParent(root, false);
                bolt.transform.localPosition = new Vector3((i - 1) * 3.2f + side * .42f, -PocketHeightAbovePlate + .029f, .59f);
                bolt.transform.localScale = new Vector3(.075f, .012f, .075f);
                bolt.GetComponent<Renderer>().sharedMaterial = gold;
                UnityEngine.Object.DestroyImmediate(bolt.GetComponent<Collider>());
            }
            var trigger = pocket.gameObject.AddComponent<SphereCollider>();
            trigger.radius = .24f;
            // Detect below the plate opening, after a medal has physically passed through it.
            // The mouth is tilted; keep the detector vertically aligned with the actual hole.
            trigger.center = pocket.InverseTransformPoint(pocket.position - game.transform.up * (PocketHeightAbovePlate + .32f));
            trigger.isTrigger = true;
            var entry = pocket.gameObject.AddComponent<MedalSlotPocket>();
            entry.controller = controller;
            entry.requireEntryFromAbove = true;
            entry.minimumEntryHeightOffset = .12f;
            entry.clickOpeningRadius = PocketHoleRadius;
            var outlet = new GameObject("MedalOutlet").transform;
            outlet.SetParent(pocket, false);
            outlet.localPosition = new Vector3(0, 1.35f, 0);
            game.medalInlets[i] = outlet;
        }
        game.selectedInlet = Mathf.Clamp(game.selectedInlet, 0, 2);
        game.medalSpawnPoint = game.medalInlets[game.selectedInlet];
        EditorUtility.SetDirty(game);
        MedalCurvedBallGuideBuilder.Build(game);
        return lights;
    }

    private static Renderer MeshPart(string name, Transform parent, Mesh mesh, Material material)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        part.transform.SetParent(parent, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        return renderer;
    }

    private static Mesh StoreMesh(string name, Mesh mesh)
    {
        Directory.CreateDirectory(MeshRoot);
        string path = MeshRoot + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        mesh.name = name;
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh); EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Mesh BuildRimMesh()
    {
        const int segments = 48, sides = 10;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int segment = 0; segment <= segments; segment++)
            for (int side = 0; side <= sides; side++)
            {
                float angle = segment * Mathf.PI * 2f / segments, tube = side * Mathf.PI * 2f / sides;
                float radius = .49f + Mathf.Cos(tube) * .055f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(tube) * .055f, Mathf.Sin(angle) * radius));
                if (segment == segments || side == sides) continue;
                int a = segment * (sides + 1) + side, b = a + sides + 1;
                triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
            }
        return FinishMesh(vertices, triangles);
    }

    private static Mesh BuildCollarMesh()
    {
        const int segments = 48;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int segment = 0; segment <= segments; segment++)
        {
            float angle = segment * Mathf.PI * 2f / segments, x = Mathf.Cos(angle), z = Mathf.Sin(angle);
            vertices.Add(new Vector3(x * .425f, -.005f, z * .425f));
            vertices.Add(new Vector3(x * .64f, -.005f, z * .64f));
            vertices.Add(new Vector3(x * .425f, -.065f, z * .425f));
            vertices.Add(new Vector3(x * .64f, -.065f, z * .64f));
            if (segment == segments) continue;
            int a = segment * 4, b = a + 4;
            triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1,
                a + 2, a + 3, b + 2, a + 3, b + 3, b + 2,
                a + 1, b + 1, a + 3, a + 3, b + 1, b + 3,
                a, a + 2, b, a + 2, b + 2, b });
        }
        return FinishMesh(vertices, triangles);
    }

    private static Mesh FinishMesh(List<Vector3> vertices, List<int> triangles)
    {
        var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    private static Mesh BuildSupportCheekMesh()
    {
        // Visible inclined cheeks join the ring's sides to the plate body.
        // The underside overlaps the plate top, keeping the mounting visibly continuous.
        float bottom = -PocketHeightAbovePlate - .10f;
        var vertices = new List<Vector3> {
            new Vector3(-.06f, bottom, -.05f), new Vector3(.06f, bottom, -.05f),
            new Vector3(-.06f, bottom, .55f), new Vector3(.06f, bottom, .55f),
            new Vector3(-.06f, -.045f, -.05f), new Vector3(.06f, -.045f, -.05f),
            new Vector3(-.06f, .22f, .55f), new Vector3(.06f, .22f, .55f)
        };
        var triangles = new List<int> { 0, 4, 1, 1, 4, 5, 2, 3, 6, 3, 7, 6,
            0, 1, 2, 1, 3, 2, 4, 6, 5, 5, 6, 7, 0, 2, 4, 2, 6, 4, 1, 5, 3, 3, 5, 7 };
        return FinishMesh(vertices, triangles);
    }

    [MenuItem("Tools/Medal Pusher/Apply Circular Slot Pockets")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
        var game = UnityEngine.Object.FindFirstObjectByType<MedalPusherGame>();
        if (game == null) throw new InvalidOperationException("Medal game missing.");
        if (game.gameObject.scene.path != MedalPusherSceneBuilder.ScenePath)
            throw new InvalidOperationException("Open the playable medal pusher scene first.");
        var controller = game.GetComponent<MedalSlotJackpotController>();
        Undo.RegisterFullObjectHierarchyUndo(game.gameObject, "Mount slot pockets and curved ball guides");
        Build(game, controller);
        var scene = game.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[MedalPusher] Upper-surface slot pockets with real plate openings and curved ball guides saved.");
    }

    [MenuItem("Tools/Medal Pusher/Apply Mounted Pockets And Curved Guides")]
    public static void ApplyMountedPocketsAndGuides() => Apply();

    [MenuItem("Tools/Medal Pusher/Apply Upper Surface Slot Pockets")]
    public static void ApplyUpperSurfaceSlotPockets() => Apply();
}
