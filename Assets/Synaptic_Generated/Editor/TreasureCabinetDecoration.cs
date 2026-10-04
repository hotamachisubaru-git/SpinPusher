using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Original gold-and-ivory arcade decoration inspired by the supplied machine
/// reference. These display pieces have no gameplay components or colliders.
/// </summary>
public static class TreasureCabinetDecoration
{
    private const string MaterialRoot = "Assets/Synaptic_Generated/MedalPusher/Materials";
    private static Material gold, paleGold, ivory, graphite, screen, red, blue, yellow, lightStrip;
    private static TMP_FontAsset font;

    /// <summary>Replaces only TreasureCabinetDecor below the supplied playfield.</summary>
    public static void Build(Transform parent)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before rebuilding the decoration.");
        Directory.CreateDirectory(MaterialRoot);
        AssetDatabase.Refresh();
        gold = Material("DekoGold", new Color(1f, .62f, .12f), .72f, .48f);
        paleGold = Material("DekoPaleGold", new Color(1f, .86f, .44f), .60f, .56f);
        ivory = Material("DekoIvory", new Color(.94f, .96f, 1f), .18f, .50f);
        graphite = Material("DekoGraphite", new Color(.025f, .035f, .05f), .25f, .40f);
        screen = Material("DekoScreen", new Color(.005f, .012f, .026f), .10f, .62f);
        red = Material("DekoRuby", new Color(1f, .085f, .11f), .35f, .68f, .35f);
        blue = Material("DekoSapphire", new Color(.045f, .48f, 1f), .35f, .68f, .35f);
        yellow = Material("DekoAmber", new Color(1f, .80f, .05f), .35f, .68f, .35f);
        lightStrip = Material("DekoWarmLight", new Color(1f, .90f, .62f), .05f, .35f, 1.5f);
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/SourceFiles/Fonts/Inter-Variable SDF.asset")
            ?? AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset")
            ?? TMP_Settings.defaultFontAsset;

        var previous = parent.Find("TreasureCabinetDecor");
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var decor = Group("TreasureCabinetDecor", parent);
        BuildTower(decor);
        BuildJackpotOrbs(decor);
        BuildStation(decor, "Station_East", new Vector3(9f, 0f, 8f), -90f, blue, "SAPPHIRE");
        BuildStation(decor, "Station_West", new Vector3(-9f, 0f, 8f), 90f, red, "RUBY");
        BuildStation(decor, "Station_Rear", new Vector3(0f, 0f, 17f), 180f, yellow, "AMBER");
        AssetDatabase.SaveAssets();
    }

    private static void BuildTower(Transform parent)
    {
        var tower = Group("FourSidedTreasureTower", parent);
        tower.localPosition = new Vector3(0f, 0f, 8f);
        Box("Foundation_White", tower, new Vector3(0, -1.82f, 0), new Vector3(7.3f, .60f, 7.3f), ivory);
        Box("Foundation_GoldRim", tower, new Vector3(0, -1.47f, 0), new Vector3(7.48f, .13f, 7.48f), gold);
        Box("Plinth", tower, new Vector3(0, .50f, 0), new Vector3(3.85f, 3.90f, 3.85f), graphite);
        Box("Plinth_WhiteCap", tower, new Vector3(0, 2.58f, 0), new Vector3(4.4f, .30f, 4.4f), ivory);
        Box("Plinth_GoldCap", tower, new Vector3(0, 2.80f, 0), new Vector3(4.65f, .12f, 4.65f), gold);

        for (int x = -1; x <= 1; x += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 post = new Vector3(x * 3.12f, 3.45f, z * 3.12f);
            Box("WhiteCornerPillar", tower, post, new Vector3(.44f, 9.7f, .44f), ivory);
            Box("Pillar_GoldFoot", tower, new Vector3(post.x, -1.0f, post.z), new Vector3(.68f, .55f, .68f), gold);
            Box("Pillar_GoldCollar", tower, new Vector3(post.x, 4.35f, post.z), new Vector3(.62f, .30f, .62f), paleGold);
            Box("Pillar_GoldCapital", tower, new Vector3(post.x, 8.23f, post.z), new Vector3(.76f, .30f, .76f), gold);
            Box("Pillar_LightFlute", tower, new Vector3(post.x + x * .231f, 4.4f, post.z), new Vector3(.025f, 6.8f, .11f), lightStrip);
            Box("Pillar_LightFlute", tower, new Vector3(post.x, 4.4f, post.z + z * .231f), new Vector3(.11f, 6.8f, .025f), lightStrip);
        }

        // Four independent black display faces leave a visible open-air lower tower.
        var accents = new[] { red, blue, yellow, blue };
        for (int side = 0; side < 4; side++)
        {
            var face = Group("DisplayFace_" + side, tower);
            face.localRotation = Quaternion.Euler(0, side * 90f, 0);
            Box("ScreenBacking", face, new Vector3(0, 6.5f, -2.89f), new Vector3(5.73f, 3.55f, .24f), graphite);
            Box("BlackScreen", face, new Vector3(0, 6.5f, -3.025f), new Vector3(5.25f, 3.05f, .05f), screen);
            Box("FrameTop", face, new Vector3(0, 8.18f, -3.08f), new Vector3(6.2f, .26f, .29f), gold);
            Box("FrameBottom", face, new Vector3(0, 4.82f, -3.08f), new Vector3(6.2f, .26f, .29f), gold);
            Box("LeftFrame", face, new Vector3(-2.85f, 6.5f, -3.08f), new Vector3(.26f, 3.2f, .29f), paleGold);
            Box("RightFrame", face, new Vector3(2.85f, 6.5f, -3.08f), new Vector3(.26f, 3.2f, .29f), paleGold);
            Box("TopLightLine", face, new Vector3(0, 7.93f, -3.195f), new Vector3(5.2f, .055f, .035f), lightStrip);
            Box("ColorDivider", face, new Vector3(0, 6.58f, -3.074f), new Vector3(4.82f, .055f, .025f), accents[side]);
            Label("TreasureTitle", face, "TREASURE", new Vector3(0, 7.32f, -3.09f), new Vector2(5f, .68f), 5.4f, new Color(1f, .85f, .35f));
            Label("JackpotTitle", face, "JACKPOT", new Vector3(0, 6.14f, -3.09f), new Vector2(4.8f, .75f), 6.1f, Color.white);
            Label("ScreenCaption", face, "RUBY   /   SAPPHIRE   /   AMBER", new Vector3(0, 5.32f, -3.09f), new Vector2(4.7f, .30f), 2.4f, new Color(.68f, .82f, 1f));
            foreach (float x in new[] { -2.44f, 2.44f })
            {
                var jewel = Box("ScreenCornerJewel", face, new Vector3(x, 7.50f, -3.13f), new Vector3(.18f, .18f, .055f), accents[side]);
                jewel.localRotation = Quaternion.Euler(0, 0, 45);
            }
            Box("GoldLowerApron", face, new Vector3(0, 4.40f, -3.00f), new Vector3(5.45f, .48f, .20f), ivory);
            Box("ApronGoldLine", face, new Vector3(0, 4.17f, -3.13f), new Vector3(5.8f, .07f, .045f), gold);
        }

        BuildCrown(tower);
    }

    private static void BuildCrown(Transform tower)
    {
        var crown = Group("AngularGoldenCrown", tower);
        Box("RoofEave", crown, new Vector3(0, 8.5f, 0), new Vector3(7.3f, .30f, 7.3f), ivory);
        Box("RoofGoldEave", crown, new Vector3(0, 8.70f, 0), new Vector3(7.62f, .12f, 7.62f), gold);
        Box("CrownCenterPlinth", crown, new Vector3(0, 9.07f, 0), new Vector3(2.25f, .55f, 2.25f), gold);
        Box("CrownTopCap", crown, new Vector3(0, 9.45f, 0), new Vector3(2.75f, .18f, 2.75f), paleGold);
        var gem = Box("AmberCrownGem", crown, new Vector3(0, 9.88f, 0), new Vector3(.78f, .78f, .78f), yellow);
        gem.localRotation = Quaternion.Euler(0, 45, 45);

        // Open gold rafters and alternating angled points read as an ornate crown
        // without enclosing the machine in one heavy opaque roof slab.
        for (int side = 0; side < 4; side++)
        {
            var edge = Group("CrownEdge_" + side, crown);
            edge.localRotation = Quaternion.Euler(0, side * 90f, 0);
            Beam("RoofRafter", edge, new Vector3(-3.50f, 8.79f, -3.50f), new Vector3(-.75f, 9.45f, -.75f), .15f, paleGold);
            Beam("RoofRafter", edge, new Vector3(3.50f, 8.79f, -3.50f), new Vector3(.75f, 9.45f, -.75f), .15f, paleGold);
            for (int i = -2; i <= 2; i++)
            {
                float x = i * 1.28f;
                float peak = i % 2 == 0 ? 9.63f : 9.25f;
                Beam("CrownPointLeft", edge, new Vector3(x - .62f, 8.77f, -3.72f), new Vector3(x, peak, -3.85f), .13f, gold);
                Beam("CrownPointRight", edge, new Vector3(x, peak, -3.85f), new Vector3(x + .62f, 8.77f, -3.72f), .13f, gold);
            }
            Box("CrownLightRail", edge, new Vector3(0, 8.77f, -3.81f), new Vector3(7.0f, .065f, .04f), lightStrip);
        }
    }

    private static void BuildJackpotOrbs(Transform parent)
    {
        var orbs = Group("ThreeJackpotOrbs", parent);
        BuildOrb(orbs, "RubyJackpot", new Vector3(-4.60f, 0f, 5.25f), red, "RUBY");
        BuildOrb(orbs, "SapphireJackpot", new Vector3(4.60f, 0f, 5.25f), blue, "SAPPHIRE");
        BuildOrb(orbs, "AmberJackpot", new Vector3(0f, 0f, 11.95f), yellow, "AMBER");
    }

    private static void BuildOrb(Transform parent, string name, Vector3 location, Material accent, string title)
    {
        var orb = Group(name, parent);
        orb.localPosition = location;
        Cylinder("GoldPedestalFoot", orb, new Vector3(0, -1.8f, 0), .72f, .35f, gold);
        Cylinder("WhitePedestal", orb, new Vector3(0, .65f, 0), .46f, 4.6f, ivory);
        Cylinder("PedestalBand", orb, new Vector3(0, 2.8f, 0), .63f, .16f, paleGold);
        Cylinder("PedestalLight", orb, new Vector3(0, 2.96f, 0), .56f, .08f, accent);
        Sphere("JackpotOrb", orb, new Vector3(0, 3.85f, 0), new Vector3(1.52f, 1.85f, 1.52f), accent);
        Cylinder("OrbTopFinial", orb, new Vector3(0, 4.88f, 0), .16f, .30f, gold);
        Sphere("OrbFinialJewel", orb, new Vector3(0, 5.08f, 0), Vector3.one * .25f, lightStrip);
        // Slender gold cage ribs retain the large colored ball silhouette.
        for (int side = 0; side < 4; side++)
        {
            var rib = Group("OrbCageRib", orb);
            rib.localRotation = Quaternion.Euler(0, side * 90f, 0);
            Beam("CageLower", rib, new Vector3(0, 2.98f, -.15f), new Vector3(0, 3.85f, -.84f), .075f, paleGold);
            Beam("CageUpper", rib, new Vector3(0, 3.85f, -.84f), new Vector3(0, 4.85f, -.15f), .075f, paleGold);
        }
        Box("OrbLabelBacking", orb, new Vector3(0, 1.75f, -.50f), new Vector3(1.85f, .52f, .12f), graphite);
        Label("OrbTitle", orb, title, new Vector3(0, 1.78f, -.57f), new Vector2(1.7f, .34f), 2.6f, new Color(1f, .90f, .50f));
    }

    private static void BuildStation(Transform parent, string name, Vector3 center, float yaw, Material accent, string title)
    {
        var station = Group(name, parent);
        station.localPosition = center;
        station.localRotation = Quaternion.Euler(0, yaw, 0);
        Box("StationLowerBody", station, new Vector3(0, -1.18f, 0), new Vector3(6.65f, 1.88f, 5.7f), ivory);
        Box("GoldSkirt", station, new Vector3(0, -2.08f, 0), new Vector3(6.85f, .16f, 5.90f), gold);
        Box("BlackMedalTray", station, new Vector3(0, -.16f, -.30f), new Vector3(5.7f, .18f, 4.45f), graphite);
        Box("TrayBackSlope", station, new Vector3(0, .17f, 1.40f), new Vector3(5.65f, .50f, 1.1f), paleGold);
        Box("TrayBackRail", station, new Vector3(0, .65f, 2.35f), new Vector3(6.2f, .25f, .24f), gold);
        Box("FrontConsole", station, new Vector3(0, -.10f, -2.93f), new Vector3(6.95f, .55f, .8f), graphite);
        Box("ConsoleGoldLip", station, new Vector3(0, .22f, -3.27f), new Vector3(7.05f, .09f, .14f), gold);
        Box("FrontWhitePanel", station, new Vector3(0, -1.15f, -2.93f), new Vector3(6.35f, 1.45f, .20f), ivory);
        Box("FrontAccentLine", station, new Vector3(0, -.48f, -3.06f), new Vector3(6.4f, .075f, .055f), accent);
        Box("CollectionHatch", station, new Vector3(0, -1.33f, -3.07f), new Vector3(2.6f, .55f, .07f), graphite);
        Box("CollectionGoldLip", station, new Vector3(0, -1.63f, -3.22f), new Vector3(2.85f, .12f, .36f), paleGold);

        for (int side = -1; side <= 1; side += 2)
        {
            Box("WhiteSideWall", station, new Vector3(side * 3.16f, .15f, -.15f), new Vector3(.27f, .85f, 5.05f), ivory);
            Box("SideGoldRim", station, new Vector3(side * 3.16f, .63f, -.15f), new Vector3(.30f, .10f, 5.05f), gold);
            Box("WhiteUpright", station, new Vector3(side * 3.16f, 2.40f, 2.29f), new Vector3(.28f, 4.25f, .32f), ivory);
            Box("UprightAccent", station, new Vector3(side * 3.16f, 2.40f, 2.095f), new Vector3(.09f, 3.90f, .05f), accent);
            Box("ControlButtonBase", station, new Vector3(side * 2.10f, .20f, -2.90f), new Vector3(.70f, .11f, .42f), paleGold);
            Cylinder("ControlButton", station, new Vector3(side * 2.1f, .31f, -2.90f), .19f, .12f, accent);
            Beam("SideApronChevron", station, new Vector3(side * 2.8f, -1.8f, -3.07f), new Vector3(side * 1.6f, -.55f, -3.07f), .09f, gold);
        }

        Box("StationBackPanel", station, new Vector3(0, 2.65f, 2.36f), new Vector3(6.05f, 3.1f, .25f), ivory);
        Box("SmallScreenFrame", station, new Vector3(0, 2.90f, 2.19f), new Vector3(4.74f, 2.24f, .14f), gold);
        Box("SmallBlackScreen", station, new Vector3(0, 2.90f, 2.10f), new Vector3(4.42f, 1.92f, .05f), screen);
        Label("StationDisplayTitle", station, title, new Vector3(0, 3.2f, 2.061f), new Vector2(4.15f, .60f), 4.8f, new Color(1f, .88f, .50f));
        Label("StationDisplaySubtitle", station, "TREASURE STATION", new Vector3(0, 2.51f, 2.061f), new Vector2(4.10f, .40f), 2.8f, Color.white);
        Box("MarqueeWhite", station, new Vector3(0, 4.43f, 2.29f), new Vector3(7.08f, .50f, 1.25f), ivory);
        Box("MarqueeGoldRim", station, new Vector3(0, 4.76f, 2.29f), new Vector3(7.22f, .15f, 1.40f), gold);
        Box("MarqueeLight", station, new Vector3(0, 4.19f, 1.64f), new Vector3(6.65f, .07f, .045f), lightStrip);
        for (int side = -1; side <= 1; side += 2)
            Beam("MarqueeWing", station, new Vector3(side * 3.2f, 4.80f, 2.22f), new Vector3(side * 3.68f, 5.25f, 2.29f), .20f, paleGold);
    }

    private static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static Transform Box(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        return Shape(PrimitiveType.Cube, name, parent, position, size, material);
    }

    private static Transform Sphere(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        return Shape(PrimitiveType.Sphere, name, parent, position, size, material);
    }

    private static Transform Cylinder(string name, Transform parent, Vector3 position, float radius, float height, Material material)
    {
        return Shape(PrimitiveType.Cylinder, name, parent, position, new Vector3(radius * 2, height * .5f, radius * 2), material);
    }

    private static Transform Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        foreach (var collider in go.GetComponents<Collider>()) UnityEngine.Object.DestroyImmediate(collider);
        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    private static void Beam(string name, Transform parent, Vector3 from, Vector3 to, float width, Material material)
    {
        var vector = to - from;
        var beam = Box(name, parent, (from + to) * .5f, new Vector3(width, vector.magnitude, width), material);
        beam.localRotation = Quaternion.FromToRotation(Vector3.up, vector);
    }

    private static void Label(string name, Transform parent, string content, Vector3 position, Vector2 size, float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(TextMeshPro));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var label = go.GetComponent<TextMeshPro>();
        label.font = font;
        label.text = content;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Truncate;
        label.rectTransform.sizeDelta = size;
        var renderer = label.GetComponent<Renderer>();
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Material Material(string name, Color color, float metallic, float smoothness, float emission = 0f)
    {
        string path = MaterialRoot + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("The URP Lit shader could not be loaded.");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (emission > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(material);
        return material;
    }
}
