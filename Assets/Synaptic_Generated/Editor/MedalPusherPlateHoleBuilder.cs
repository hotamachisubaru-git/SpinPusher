using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Replaces the moving plate's solid cube with three real through-holes.</summary>
public static class MedalPusherPlateHoleBuilder
{
    private const string MeshFolder = MedalPusherSceneBuilder.AssetRoot + "/Meshes";
    public const string MeshAssetPath = MeshFolder + "/PusherPlateWithThreeSlots.asset";
    public const float DefaultFrontInset = .55f;
    public const float DefaultHoleRadius = .40f;
    private const int CircleSegments = 48;
    private static readonly float[] HoleCentresX = { -3.2f, 0f, 3.2f };

    public static MeshCollider Build(MedalPusherGame game, float frontInset = DefaultFrontInset,
        float holeRadius = DefaultHoleRadius)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before cutting the pusher plate.");
        if (game == null || game.gameObject.scene.path != MedalPusherSceneBuilder.ScenePath || game.pusherBody == null)
            throw new InvalidOperationException("The playable medal scene and moving pusher plate are required.");
        Rigidbody body = game.pusherBody;
        if (!body.isKinematic)
            throw new InvalidOperationException("The perforated pusher plate must remain kinematic.");
        MeshFilter filter = body.GetComponent<MeshFilter>();
        Collider[] originalColliders = body.GetComponents<Collider>();
        if (filter == null || filter.sharedMesh == null || originalColliders.Length != 1
            || !(originalColliders[0] is BoxCollider || originalColliders[0] is MeshCollider)
            || originalColliders[0].isTrigger)
            throw new InvalidOperationException("The plate must have one solid box or mesh collider and a visible mesh.");
        if (!Finite(frontInset) || !Finite(holeRadius) || holeRadius <= .01f)
            throw new ArgumentOutOfRangeException(nameof(holeRadius), "A finite positive hole radius is required.");

        Bounds bounds = filter.sharedMesh.bounds;
        if (bounds.size.x <= .001f || bounds.size.y <= .001f || bounds.size.z <= .001f)
            throw new InvalidOperationException("The pusher plate mesh must have a non-zero volume.");
        if (originalColliders[0] is BoxCollider originalBox
            && ((originalBox.center - bounds.center).sqrMagnitude > .000001f
                || (originalBox.size - bounds.size).sqrMagnitude > .000001f))
            throw new InvalidOperationException("The original plate mesh and box bounds must agree.");

        // Measures are gameplay metres, even though the cube's Transform is non-uniformly scaled.
        float xUnits = game.transform.InverseTransformVector(body.transform.TransformVector(Vector3.right)).magnitude;
        float zUnits = game.transform.InverseTransformVector(body.transform.TransformVector(Vector3.forward)).magnitude;
        if (xUnits <= .0001f || zUnits <= .0001f)
            throw new InvalidOperationException("The pusher plate's horizontal scale must be non-zero.");
        float holeZ = bounds.min.z + frontInset / zUnits;
        float[] holeX = new float[HoleCentresX.Length];
        for (int i = 0; i < holeX.Length; i++) holeX[i] = bounds.center.x + HoleCentresX[i] / xUnits;

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();
        for (int i = 0; i < holeX.Length; i++)
        {
            float left = i == 0 ? bounds.min.x : (holeX[i - 1] + holeX[i]) * .5f;
            float right = i == holeX.Length - 1 ? bounds.max.x : (holeX[i] + holeX[i + 1]) * .5f;
            if ((holeX[i] - left) * xUnits <= holeRadius + .01f
                || (right - holeX[i]) * xUnits <= holeRadius + .01f
                || frontInset <= holeRadius + .01f
                || (bounds.max.z - holeZ) * zUnits <= holeRadius + .01f)
                throw new ArgumentOutOfRangeException(nameof(frontInset), "All three holes must fit inside the plate.");
            AddCell(vertices, triangles, uvs, bounds, left, right, holeX[i], holeZ, holeRadius, xUnits, zUnits);
        }
        AddOuterWalls(vertices, triangles, uvs, bounds);
        var mesh = new Mesh { name = "PusherPlateWithThreeSlots" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Mesh persistentMesh = StoreMesh(mesh);

        PhysicsMaterial physics = originalColliders[0].sharedMaterial;
        float contactOffset = originalColliders[0].contactOffset;
        MeshCollider collider = originalColliders[0] as MeshCollider;
        if (collider == null)
        {
            UnityEngine.Object.DestroyImmediate(originalColliders[0]);
            collider = body.gameObject.AddComponent<MeshCollider>();
        }
        // Convex cooking would fill the holes. The existing kinematic body supports this concave plate.
        collider.convex = false;
        collider.isTrigger = false;
        collider.sharedMesh = null;
        collider.sharedMaterial = physics;
        collider.contactOffset = contactOffset;
        filter.sharedMesh = persistentMesh;
        collider.sharedMesh = persistentMesh;
        EditorUtility.SetDirty(filter);
        EditorUtility.SetDirty(collider);
        return collider;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void AddCell(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
        Bounds bounds, float left, float right, float centreX, float centreZ,
        float radius, float xUnits, float zUnits)
    {
        var angles = new List<float>(CircleSegments + 4);
        for (int i = 0; i < CircleSegments; i++) angles.Add(i * Mathf.PI * 2f / CircleSegments);
        // Rays through every rectangle corner prevent clipped outer corners and gaps between cells.
        foreach (float x in new[] { left, right })
            foreach (float z in new[] { bounds.min.z, bounds.max.z })
                angles.Add(Mathf.Repeat(Mathf.Atan2((z - centreZ) * zUnits, (x - centreX) * xUnits), Mathf.PI * 2f));
        angles.Sort();
        for (int i = angles.Count - 1; i > 0; i--)
            if (Mathf.Abs(angles[i] - angles[i - 1]) < .00001f) angles.RemoveAt(i);

        for (int i = 0; i < angles.Count; i++)
        {
            float first = angles[i], second = angles[(i + 1) % angles.Count];
            Vector3 innerA = CirclePoint(centreX, centreZ, radius, first, bounds.max.y, xUnits, zUnits);
            Vector3 innerB = CirclePoint(centreX, centreZ, radius, second, bounds.max.y, xUnits, zUnits);
            Vector3 outerA = RectanglePoint(bounds, left, right, centreX, centreZ, first, xUnits, zUnits);
            Vector3 outerB = RectanglePoint(bounds, left, right, centreX, centreZ, second, xUnits, zUnits);
            AddQuad(vertices, triangles, uvs, innerA, innerB, outerB, outerA);
            AddQuad(vertices, triangles, uvs, AtY(innerA, bounds.min.y), AtY(outerA, bounds.min.y),
                AtY(outerB, bounds.min.y), AtY(innerB, bounds.min.y));
            AddQuad(vertices, triangles, uvs, innerA, AtY(innerA, bounds.min.y),
                AtY(innerB, bounds.min.y), innerB);
        }
    }

    private static Vector3 CirclePoint(float x, float z, float radius, float angle,
        float y, float xUnits, float zUnits)
        => new Vector3(x + Mathf.Cos(angle) * radius / xUnits, y, z + Mathf.Sin(angle) * radius / zUnits);

    private static Vector3 RectanglePoint(Bounds bounds, float left, float right, float centreX, float centreZ,
        float angle, float xUnits, float zUnits)
    {
        float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
        float horizontal = Mathf.Abs(x) < .000001f ? float.PositiveInfinity
            : (x > 0f ? right - centreX : left - centreX) * xUnits / x;
        float depth = Mathf.Abs(z) < .000001f ? float.PositiveInfinity
            : (z > 0f ? bounds.max.z - centreZ : bounds.min.z - centreZ) * zUnits / z;
        float length = Mathf.Min(horizontal, depth);
        float resultX = Mathf.Clamp(centreX + x * length / xUnits, left, right);
        float resultZ = Mathf.Clamp(centreZ + z * length / zUnits, bounds.min.z, bounds.max.z);
        if (Mathf.Abs(resultX - left) < .00001f) resultX = left;
        if (Mathf.Abs(resultX - right) < .00001f) resultX = right;
        if (Mathf.Abs(resultZ - bounds.min.z) < .00001f) resultZ = bounds.min.z;
        if (Mathf.Abs(resultZ - bounds.max.z) < .00001f) resultZ = bounds.max.z;
        return new Vector3(resultX, bounds.max.y, resultZ);
    }

    private static Vector3 AtY(Vector3 point, float y) => new Vector3(point.x, y, point.z);

    private static void AddOuterWalls(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Bounds bounds)
    {
        Vector3 min = bounds.min, max = bounds.max;
        AddQuad(vertices, triangles, uvs, new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z));
        AddQuad(vertices, triangles, uvs, new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
            new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z));
        AddQuad(vertices, triangles, uvs, new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z));
        AddQuad(vertices, triangles, uvs, new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
            new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z));
    }

    private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;
        vertices.AddRange(new[] { a, b, c, d });
        triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        uvs.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
    }

    private static Mesh StoreMesh(Mesh mesh)
    {
        string parent = "Assets";
        foreach (string part in MeshFolder.Substring("Assets/".Length).Split('/'))
        {
            string folder = parent + "/" + part;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, part);
            parent = folder;
        }
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
