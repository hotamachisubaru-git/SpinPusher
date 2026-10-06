using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds the Japanese settings button and modal after the other arcade UI.</summary>
public static class MedalArcadeSettingsBuilder
{
    private static TMP_FontAsset font;

    public static void Build(MedalPusherUI baseUI, MedalPusherGame game, MedalSlotJackpotController controller)
    {
        if (baseUI == null || game == null || controller == null) throw new ArgumentNullException("Settings UI requires the game, controller and base UI.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before rebuilding settings.");
        font = MedalJapaneseFontBuilder.GetFont();
        var settings = game.GetComponent<MedalArcadeSettings>() ?? game.gameObject.AddComponent<MedalArcadeSettings>();
        settings.game = game; settings.controller = controller;
        var old = baseUI.transform.Find("ArcadeSettings");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var root = Rect("ArcadeSettings", baseUI.transform, Vector2.zero, Vector2.one);
        var ui = root.gameObject.AddComponent<MedalArcadeSettingsUI>(); ui.settings = settings;
        ui.arcadeUI = baseUI.GetComponentInChildren<MedalArcadeUI>(true);
        if (ui.arcadeUI != null) ui.arcadeUI.settings = settings;
        var open = Button("SettingsButton", root, new Vector2(.25f, .035f), new Vector2(.345f, .105f), "設定", new Color(.13f, .3f, .43f));
        UnityEventTools.AddPersistentListener(open.onClick, ui.Open);
        var modal = Panel("SettingsModal", root, Vector2.zero, Vector2.one, new Color(0, 0, .02f, .8f));
        ui.modal = modal.gameObject;
        var content = Panel("SettingsContent", modal, new Vector2(.19f, .09f), new Vector2(.81f, .92f), new Color(.035f, .055f, .095f, 1));
        Label("Title", content, new Vector2(.04f, .922f), new Vector2(.96f, .984f), "メダルゲーム設定", 30, new Color(1, .85f, .4f));
        ui.jackpotInputs = new TMP_InputField[3];
        string[] names = { "赤JP 初期枚数", "青JP 初期枚数", "黄JP 初期枚数" };
        Color[] colors = { new Color(1, .35f, .4f), new Color(.35f, .8f, 1), new Color(1, .85f, .3f) };
        for (int i = 0; i < 3; i++)
        {
            float x = .04f + i * .31f;
            ui.jackpotInputs[i] = Field("Jackpot_" + i, content, new Vector2(x, .773f), new Vector2(x + .29f, .897f), names[i], colors[i]);
        }
        ui.targetPayoutInput = Field("TargetPayout", content, new Vector2(.04f, .624f), new Vector2(.48f, .75f), "目標払出率（P/O %）");
        ui.medalsPerSpinInput = Field("MedalsPerSpin", content, new Vector2(.52f, .624f), new Vector2(.96f, .75f), "入賞1枚で1回転（固定）");
        ui.slotBallChanceInput = Field("SlotBallChance", content, new Vector2(.04f, .475f), new Vector2(.48f, .601f), "ボール当選率の基準（%）");
        ui.slotMedalChanceInput = Field("SlotMedalChance", content, new Vector2(.52f, .475f), new Vector2(.96f, .601f), "メダル当選率の基準（%）");
        ui.slotSpinDurationInput = Field("SpinDuration", content, new Vector2(.04f, .326f), new Vector2(.32f, .452f), "スロット時間（秒）");
        ui.rescueSpinsInput = Field("RescueSpins", content, new Vector2(.36f, .326f), new Vector2(.64f, .452f), "ボール救済基準（0で無効）");
        ui.sideHoleWidthInput = Field("SideHoleWidth", content, new Vector2(.68f, .326f), new Vector2(.96f, .452f), "横穴幅（0で閉じる）");
        ui.showLotteryStatusToggle = LotteryStatusToggle(content, settings.showLotteryStatus);
        UnityEventTools.AddPersistentListener(ui.showLotteryStatusToggle.onValueChanged, ui.SetLotteryStatusVisibility);
        ui.measuredPayoutText = Label("MeasuredPayout", content, new Vector2(.04f, .209f), new Vector2(.96f, .263f), "実績払出率（P/O） 未計測", 18, new Color(.6f, .9f, 1));
        var explanation = Label("Explanation", content, new Vector2(.04f, .142f), new Vector2(.96f, .205f),
            "目標払出率に応じてスロット当選率とボール救済の間隔を調整します。ボールの物理抽選は変わらず、短時間の払出率は目標と一致しない場合があります。目標0%ではスロット当選を停止します。適用時はJACKPOTの現在枚数を初期枚数へ更新します。", 17, new Color(.78f, .82f, .9f));
        explanation.textWrappingMode = TextWrappingModes.Normal;
        ui.feedbackText = Label("Feedback", content, new Vector2(.04f, .082f), new Vector2(.96f, .14f), "", 17, Color.white);
        ui.feedbackText.textWrappingMode = TextWrappingModes.Normal;
        var apply = Button("ApplySettings", content, new Vector2(.04f, .016f), new Vector2(.61f, .079f), "適用して保存", new Color(.08f, .45f, .36f));
        var close = Button("CloseSettings", content, new Vector2(.67f, .016f), new Vector2(.96f, .079f), "閉じる", new Color(.22f, .27f, .36f));
        UnityEventTools.AddPersistentListener(apply.onClick, ui.ApplyFromInputs);
        UnityEventTools.AddPersistentListener(close.onClick, ui.Close);
        // Ensure existing dynamic Japanese fonts contain the new visible labels before Play.
        string characters = string.Concat(root.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text));
        font.TryAddCharacters(characters);
        foreach (var atlas in font.atlasTextures)
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
        EditorUtility.SetDirty(font);
        modal.gameObject.SetActive(false);
        root.SetAsLastSibling();
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
    {
        var rect = Rect(name, parent, min, max); var image = rect.gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = true; return rect;
    }
    private static TextMeshProUGUI Label(string name, Transform parent, Vector2 min, Vector2 max, string value, float size, Color color)
    {
        var rect = Rect(name, parent, min, max); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = 12; text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }
    private static Button Button(string name, Transform parent, Vector2 min, Vector2 max, string value, Color color)
    {
        var rect = Panel(name, parent, min, max, color); var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        var text = Label("Label", rect, new Vector2(.02f, .02f), new Vector2(.98f, .98f), value, 23, Color.white);
        text.alignment = TextAlignmentOptions.Center; return button;
    }
    private static Toggle LotteryStatusToggle(Transform parent, bool isOn)
    {
        var row = Panel("ShowLotteryStatus", parent, new Vector2(.04f, .268f), new Vector2(.96f, .313f), Color.clear);
        var toggle = row.gameObject.AddComponent<Toggle>();
        var box = Panel("Checkbox", row, new Vector2(0, .5f), new Vector2(0, .5f), new Color(.14f, .2f, .3f));
        box.sizeDelta = new Vector2(30, 30); box.anchoredPosition = new Vector2(15, 0);
        var mark = Panel("Checkmark", box, new Vector2(.19f, .19f), new Vector2(.81f, .81f), new Color(.4f, 1, .7f));
        mark.GetComponent<Image>().raycastTarget = false;
        toggle.targetGraphic = box.GetComponent<Image>(); toggle.graphic = mark.GetComponent<Image>();
        toggle.SetIsOnWithoutNotify(isOn);
        Label("Label", row, new Vector2(.055f, 0), Vector2.one, "抽選案内を表示", 22, Color.white);
        return toggle;
    }
    private static TMP_InputField Field(string name, Transform parent, Vector2 min, Vector2 max, string title, Color? labelColor = null)
    {
        var group = Rect(name, parent, min, max);
        Label("FieldLabel", group, new Vector2(0, .57f), Vector2.one, title, 19, labelColor ?? new Color(.9f, .92f, 1));
        var rect = Panel("Input", group, Vector2.zero, new Vector2(1, .56f), new Color(.1f, .14f, .22f));
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        var viewport = Rect("TextArea", rect, new Vector2(.04f, .04f), new Vector2(.96f, .96f));
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = Label("Text", viewport, Vector2.zero, Vector2.one, "", 24, Color.white);
        var placeholder = Label("Placeholder", viewport, Vector2.zero, Vector2.one, "数値", 20, new Color(.5f, .58f, .7f));
        input.textViewport = viewport; input.textComponent = text; input.placeholder = placeholder;
        input.targetGraphic = rect.GetComponent<Image>(); input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = TMP_InputField.ContentType.Standard;
        input.characterLimit = 16; input.richText = false; input.customCaretColor = true;
        input.caretColor = Color.white; input.selectionColor = new Color(.15f, .65f, .9f, .5f);
        return input;
    }
}
