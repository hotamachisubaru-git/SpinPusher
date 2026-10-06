using System;
using UnityEditor;
using UnityEngine;

/// <summary>Static curved rails steer green balls across the front playfield.</summary>
public static class MedalCurvedBallGuideBuilder
{
    private const string Materials = MedalPusherSceneBuilder.AssetRoot + "/Materials/";
    private const int Segments = 24;
    private const float PipeDiameter = .20f;
    private const float PipeHeight = .60f;
    private const float OutsideX = 4.86f;
    private const float InwardCurve = 1.66f;
    private const float WallInnerX = 5.0f;
    private const float CentreZ = -2.35f;
    private const float HalfLengthZ = 1.05f;

    public static void Build(MedalPusherGame game)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before building curved ball guides.");
        if (game == null || game.gameObject.scene.path != MedalPusherSceneBuilder.ScenePath)
            throw new InvalidOperationException("Curved ball guides can only be built in the medal pusher game scene.");
        Transform field = game.transform.Find("GeneratedPlayfield");
        if (field == null) throw new InvalidOperationException("Generated playfield missing.");

        Material steel = AssetDatabase.LoadAssetAtPath<Material>(Materials + "PusherSteel.mat");
        Material gold = AssetDatabase.LoadAssetAtPath<Material>(Materials + "MedalGold.mat");
        PhysicsMaterial physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Materials + "BoardPhysics.physicMaterial");
        if (steel == null || gold == null)
            throw new InvalidOperationException("Build the pusher's metal materials before adding ball guides.");

        Transform previous = field.Find("CurvedBallGuides");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        Transform root = Child("CurvedBallGuides", field);
        BuildSide(root, -1, steel, gold, physics);
        BuildSide(root, 1, steel, gold, physics);
    }

    private static void BuildSide(Transform root, int side, Material steel, Material gold, PhysicsMaterial physics)
    {
        Transform guide = Child(side < 0 ? "LeftCurvedBallGuide" : "RightCurvedBallGuide", root);
        for (int i = 0; i < Segments; i++)
        {
            Vector3 first = GuidePoint(side, i);
            Vector3 second = GuidePoint(side, i + 1);
            Vector3 direction = second - first;
            Transform segment = Child("GuidePipeSegment_" + i.ToString("00"), guide);
            segment.localPosition = (first + second) * .5f;
            segment.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);

            // Each capsule extends by one pipe radius beyond both line ends.
            // Neighbours overlap, so the smooth visible curve has no ball-sized gaps.
            CapsuleCollider collision = segment.gameObject.AddComponent<CapsuleCollider>();
            collision.direction = 1;
            collision.radius = PipeDiameter * .5f;
            collision.height = direction.magnitude + PipeDiameter;
            collision.sharedMaterial = physics;
            Visual("MetalPipe", PrimitiveType.Cylinder, segment, Vector3.zero,
                new Vector3(PipeDiameter, direction.magnitude * .5f, PipeDiameter), steel);
        }

        for (int i = 0; i <= Segments; i++)
            Visual("SmoothPipeJoint_" + i.ToString("00"), PrimitiveType.Sphere, guide,
                GuidePoint(side, i), Vector3.one * PipeDiameter, steel);

        // Mirror the two end brackets onto the inside faces of the side walls.
        // No feet occupy the adjustable side holes or the medal passage below.
        int[] mounts = { 0, Segments };
        for (int index = 0; index < mounts.Length; index++)
        {
            Vector3 point = GuidePoint(side, mounts[index]);
            Transform mount = Child(index == 0 ? "WallMount_Front" : "WallMount_Rear", guide);
            mount.localPosition = point;
            float bridgeLength = WallInnerX - OutsideX;
            SolidBox("WallBracket", mount, new Vector3(side * bridgeLength * .5f, 0f, 0f),
                new Vector3(bridgeLength, .12f, .14f), steel, physics);
            SolidBox("WallPlate", mount, new Vector3(side * (bridgeLength - .015f), 0f, 0f),
                new Vector3(.03f, .34f, .24f), gold, physics);
            SolidBox("PipeClamp", mount, Vector3.zero, new Vector3(.25f, .10f, .25f), gold, physics);
            for (int bolt = -1; bolt <= 1; bolt += 2)
            {
                Transform screw = Visual("WallBolt_" + bolt, PrimitiveType.Cylinder, mount,
                    new Vector3(side * (bridgeLength - .045f), bolt * .10f, 0f),
                    new Vector3(.055f, .016f, .055f), steel);
                screw.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
        }

        // Compact metallic end caps make the two exposed ends clear in game view.
        Visual("FrontEndCap", PrimitiveType.Sphere, guide, GuidePoint(side, 0), Vector3.one * .22f, gold);
        Visual("RearEndCap", PrimitiveType.Sphere, guide, GuidePoint(side, Segments), Vector3.one * .22f, gold);
    }

    private static Vector3 GuidePoint(int side, int index)
    {
        float angle = -Mathf.PI * .5f + Mathf.PI * index / Segments;
        // Top view: left bows inward like ')', right bows inward like '('.
        return new Vector3(side * (OutsideX - InwardCurve * Mathf.Cos(angle)),
            PipeHeight, CentreZ + HalfLengthZ * Mathf.Sin(angle));
    }

    private static void SolidBox(string name, Transform parent, Vector3 position,
        Vector3 size, Material material, PhysicsMaterial physics)
    {
        Transform shape = Child(name, parent);
        shape.localPosition = position;
        Visual("Metal", PrimitiveType.Cube, shape, Vector3.zero, size, material);
        BoxCollider collision = shape.gameObject.AddComponent<BoxCollider>();
        collision.size = size;
        collision.sharedMaterial = physics;
    }

    private static Transform Child(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static Transform Visual(string name, PrimitiveType type, Transform parent,
        Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go.transform;
    }
}
