using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>Visible payout spouts and genuine adjustable side openings.</summary>
public static class MedalPayoutBuilder
{
    private const string Root = "Assets/Synaptic_Generated/MedalPusher/Materials/";
    public static void Build(Transform field, MedalPusherGame game)
    {
        Transform previous = field.Find("PayoutAndSideHoles");
        if (previous != null) Object.DestroyImmediate(previous.gameObject);
        Transform root = Child("PayoutAndSideHoles", field);
        Material gold = AssetDatabase.LoadAssetAtPath<Material>(Root + "MedalGold.mat");
        Material shell = AssetDatabase.LoadAssetAtPath<Material>(Root + "Cabinet.mat");
        Material dark = AssetDatabase.LoadAssetAtPath<Material>(Root + "Board.mat");
        var oldDeck = field.Find("PusherBoard");
        Material deckMaterial = oldDeck.GetComponent<Renderer>().sharedMaterial;
        var deckPhysics = oldDeck.GetComponent<Collider>().sharedMaterial;
        foreach (var collider in oldDeck.GetComponents<Collider>()) collider.enabled = false;
        oldDeck.GetComponent<Renderer>().enabled = false;
        game.sideHoleMainDeck = Shape("AdjustableMainDeck", root, new Vector3(0, -.15f, -.4f), new Vector3(8.9f, .3f, 8.2f), deckMaterial, true);
        game.sideHoleMainDeck.GetComponent<Collider>().sharedMaterial = deckPhysics;
        game.sideHoleDeckStrips = new Transform[4];
        game.sideHoleTriggers = new Transform[2];
        game.sideHoleVisuals = new Transform[2];
        game.sidePayoutPoints = new Transform[2];
        for (int side = 0; side < 2; side++)
        {
            float sign = side == 0 ? -1 : 1;
            for (int piece = 0; piece < 2; piece++)
            {
                var strip = Shape("SideDeck_" + side + "_" + piece, root, Vector3.zero, Vector3.one, deckMaterial, true);
                strip.GetComponent<Collider>().sharedMaterial = deckPhysics;
                game.sideHoleDeckStrips[side * 2 + piece] = strip;
            }
            var hole = Child("SideHole_" + side, root);
            var trigger = hole.gameObject.AddComponent<BoxCollider>(); trigger.isTrigger = true;
            hole.gameObject.AddComponent<MedalSideHole>().game = game;
            game.sideHoleTriggers[side] = hole;
            game.sideHoleVisuals[side] = Shape("SideDrainFloor_" + side, root, Vector3.zero, Vector3.one, dark, false);
            Shape("SideDrainOuterRail_" + side, root, new Vector3(sign * 4.985f, .02f, -1.3f), new Vector3(.03f, .035f, 1.8f), gold, false);
            var spout = Child(side == 0 ? "LeftPayoutSpout" : "RightPayoutSpout", root);
            spout.localPosition = new Vector3(sign * 4.28f, 3.05f, 1.8f);
            Shape("GoldNozzle", spout, new Vector3(sign * .08f, .12f, 0), new Vector3(.63f, .32f, .76f), gold, false);
            Shape("DarkOutlet", spout, new Vector3(-sign * .245f, .045f, 0), new Vector3(.025f, .14f, .55f), dark, false);
            Shape("WhiteSupport", root, new Vector3(sign * 4.7f, 1.5f, 1.8f), new Vector3(.2f, 3.0f, .24f), shell, false);
            var point = Child("PayoutPoint", spout); point.localPosition = new Vector3(-sign * .35f, 0, 0);
            game.sidePayoutPoints[side] = point;
        }
        var upper = Child("UpperJackpotPayout", root); upper.localPosition = new Vector3(0, 4.6f, .25f);
        Shape("UpperGoldHopper", upper, new Vector3(0, .28f, .08f), new Vector3(4.6f, .55f, .60f), gold, false);
        Shape("UpperDarkMouth", upper, new Vector3(0, -.015f, -.2f), new Vector3(3.95f, .07f, .25f), dark, false);
        game.jackpotPayoutPoint = Child("JackpotPayoutPoint", upper);
        game.jackpotPayoutPoint.localPosition = new Vector3(0, -.15f, -.19f);
        for (int side = -1; side <= 1; side += 2)
            Shape("UpperPayoutSupport", root, new Vector3(side * 3.9f, 3.35f, .6f), new Vector3(.14f, 2.85f, .16f), gold, false);
        Label("UpperPayoutLabel", upper, "JACKPOT 払い出し", new Vector3(0, .55f, -.25f), new Vector2(4.2f, .4f), 3.2f);
        game.ConfigureSideHoles(game.sideHoleWidth);
    }
    private static Transform Child(string name, Transform parent)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }
    private static Transform Shape(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool physics)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
        if (!physics) Object.DestroyImmediate(go.GetComponent<Collider>()); return go.transform;
    }
    private static void Label(string name, Transform parent, string value, Vector3 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(TextMeshPro)); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        var text = go.GetComponent<TextMeshPro>(); text.font = MedalJapaneseFontBuilder.GetFont(); text.text = value;
        text.fontSize = fontSize; text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.rectTransform.sizeDelta = size;
    }
}
