using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Validates the whole settings form before applying or saving any value.</summary>
public sealed class MedalArcadeSettingsUI : MonoBehaviour
{
    public MedalArcadeSettings settings;
    public GameObject modal;
    public TMP_InputField[] jackpotInputs;
    public TMP_InputField targetPayoutInput;
    public TMP_InputField slotBallChanceInput;
    public TMP_InputField slotMedalChanceInput;
    public TMP_InputField medalsPerSpinInput;
    public TMP_InputField slotSpinDurationInput;
    public TMP_InputField rescueSpinsInput;
    public TMP_InputField sideHoleWidthInput;
    public TMP_Text feedbackText;
    public TMP_Text measuredPayoutText;
    public Toggle showLotteryStatusToggle;
    public MedalArcadeUI arcadeUI;
    public bool IsOpen => modal != null && modal.activeSelf;

    private void Start()
    {
        if (settings == null) settings = FindAnyObjectByType<MedalArcadeSettings>();
        if (modal != null) modal.SetActive(false);
    }

    public void Open()
    {
        if (settings == null || modal == null) return;
        Populate();
        SetFeedback(settings.LastError, !string.IsNullOrEmpty(settings.LastError));
        modal.SetActive(true);
        modal.transform.SetAsLastSibling();
        if (settings.game != null) settings.game.SetSettingsOpen(true);
        UpdateMeasuredPayout();
    }

    public void Close()
    {
        if (modal != null) modal.SetActive(false);
        if (settings != null && settings.game != null) settings.game.SetSettingsOpen(false);
    }

    private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static void SetInput(TMP_InputField input, string value) { if (input != null) input.SetTextWithoutNotify(value); }
    private void Populate()
    {
        var values = settings.CaptureValues();
        for (int i = 0; jackpotInputs != null && i < jackpotInputs.Length; i++)
            SetInput(jackpotInputs[i], values.jackpotResetValues != null && i < values.jackpotResetValues.Length ? values.jackpotResetValues[i].ToString(CultureInfo.InvariantCulture) : "");
        SetInput(targetPayoutInput, Number(values.targetPayoutPercent));
        SetInput(slotBallChanceInput, Number(values.slotBallChancePercent));
        SetInput(slotMedalChanceInput, Number(values.slotMedalChancePercent));
        SetInput(medalsPerSpinInput, "1");
        if (medalsPerSpinInput != null) medalsPerSpinInput.interactable = false;
        SetInput(slotSpinDurationInput, Number(values.slotSpinDuration));
        SetInput(rescueSpinsInput, values.rescueSpins.ToString(CultureInfo.InvariantCulture));
        SetInput(sideHoleWidthInput, Number(values.sideHoleWidth));
        if (showLotteryStatusToggle != null) showLotteryStatusToggle.SetIsOnWithoutNotify(values.showLotteryStatus);
    }

    private static bool Integer(TMP_InputField input, out int value)
    { value = 0; return input != null && int.TryParse(input.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value); }
    private static bool Decimal(TMP_InputField input, out float value)
    {
        value = 0;
        return input != null && float.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public void ApplyFromInputs()
    {
        if (settings == null) { SetFeedback("設定対象のゲームが見つかりません。", true); return; }
        if (settings.IsBusy)
        { SetFeedback("抽選中は変更できません。抽選終了後に適用してください。", true); return; }
        var values = new MedalArcadeSettings.Values();
        values.showLotteryStatus = showLotteryStatusToggle != null ? showLotteryStatusToggle.isOn : settings.showLotteryStatus;
        if (jackpotInputs == null || jackpotInputs.Length != 3 ||
            !Integer(jackpotInputs[0], out values.jackpotResetValues[0]) ||
            !Integer(jackpotInputs[1], out values.jackpotResetValues[1]) ||
            !Integer(jackpotInputs[2], out values.jackpotResetValues[2]) ||
            !Decimal(targetPayoutInput, out values.targetPayoutPercent) ||
            !Decimal(slotBallChanceInput, out values.slotBallChancePercent) ||
            !Decimal(slotMedalChanceInput, out values.slotMedalChancePercent) ||
            !Integer(medalsPerSpinInput, out values.medalsPerSpin) ||
            !Decimal(slotSpinDurationInput, out values.slotSpinDuration) ||
            !Integer(rescueSpinsInput, out values.rescueSpins) ||
            !Decimal(sideHoleWidthInput, out values.sideHoleWidth))
        { SetFeedback("すべての欄に数値を入力してください。小数点は「.」を使用します。", true); return; }
        if (!MedalArcadeSettings.Validate(values, out string error)) { SetFeedback(error, true); return; }
        if (!settings.TrySetValues(values, out error)) { SetFeedback(error, true); return; }
        try
        {
            settings.ApplyToGame(true);
            RefreshLotteryStatus();
            if (settings.Save()) SetFeedback(settings.persistSettings ? "設定を適用して保存しました。JACKPOTの現在枚数も更新しました。" : "設定を適用しました。JACKPOTの現在枚数も更新しました。", false);
            else SetFeedback(settings.LastError, true);
            Populate(); UpdateMeasuredPayout();
        }
        catch (Exception) { SetFeedback("設定を適用できませんでした。抽選終了後に再度お試しください。", true); }
    }

    /// <summary>Changes presentation only; numeric settings remain locked during active lotteries.</summary>
    public void SetLotteryStatusVisibility(bool visible)
    {
        if (settings == null) settings = FindAnyObjectByType<MedalArcadeSettings>();
        if (settings == null) { SetFeedback("設定対象のゲームが見つかりません。", true); return; }
        settings.showLotteryStatus = visible;
        RefreshLotteryStatus();
        if (settings.Save()) SetFeedback(visible ? "抽選案内を表示しました。" : "抽選案内を非表示にしました。", false);
        else SetFeedback(settings.LastError, true);
    }

    private void RefreshLotteryStatus()
    {
        if (arcadeUI == null) arcadeUI = FindAnyObjectByType<MedalArcadeUI>();
        if (arcadeUI != null) { arcadeUI.settings = settings; arcadeUI.Refresh(); }
    }

    private void SetFeedback(string message, bool error)
    {
        if (feedbackText == null) return;
        feedbackText.text = message ?? "";
        feedbackText.color = error ? new Color(1, .45f, .4f) : new Color(.4f, 1, .7f);
    }

    private void UpdateMeasuredPayout()
    {
        if (measuredPayoutText == null || settings == null || settings.game == null) return;
        var game = settings.game;
        string percentage = game.TotalPaidMedals > 0 ?
            ((double)game.TotalReturnedMedals * 100 / game.TotalPaidMedals).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "未計測";
        measuredPayoutText.text = "投入 " + game.TotalPaidMedals + "枚 ／ 払出 " + game.TotalReturnedMedals + "枚\n実績払出率（P/O） " + percentage;
    }
    private void Update() { if (IsOpen) UpdateMeasuredPayout(); }
    private void OnDisable() { Close(); }
    private void OnDestroy() { if (settings != null && settings.game != null) settings.game.SetSettingsOpen(false); }
}
