using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class MedalJapaneseFontBuilder
{
    public const string AssetPath = "Assets/Synaptic_Generated/MedalPusher/Fonts/MedalJapanese SDF.asset";
    public static TMP_FontAsset GetFont()
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (asset != null) return asset;
        AssetDatabase.ImportAsset("Assets/Synaptic_Generated/MedalPusher/Fonts/MedalJapanese.ttf", ImportAssetOptions.ForceSynchronousImport);
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Synaptic_Generated/MedalPusher/Fonts/MedalJapanese.ttf");
        if (font == null) throw new InvalidOperationException("Japanese font source missing.");
        asset = TMP_FontAsset.CreateFontAsset(font, 72, 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
        asset.name = "MedalJapanese SDF";
        AssetDatabase.CreateAsset(asset, AssetPath);
        asset.material.name = "MedalJapanese Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        foreach (var atlas in asset.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, asset);
        string code = string.Concat(Directory.GetFiles("Assets/Synaptic_Generated", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        string characters = new string(code.Where(c => !char.IsControl(c) && !char.IsSurrogate(c)).Distinct().ToArray());
        if (!asset.TryAddCharacters(characters, out string missing))
            throw new InvalidOperationException("Japanese font glyphs missing: " + missing);
        foreach (var atlas in asset.atlasTextures)
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, asset);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        return asset;
    }
}
