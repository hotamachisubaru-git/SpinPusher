using System;
using System.IO;
using UnityEngine;

/// <summary>Owns the validated arcade configuration and its versioned local save.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MedalPusherGame))]
public sealed class MedalArcadeSettings : MonoBehaviour
{
    public MedalPusherGame game;
    public MedalSlotJackpotController controller;
    public bool persistSettings = true;
    [NonSerialized] public string SettingsFileOverride;
    public float targetPayoutPercent = 90f;
    public float slotBallChancePercent = 49f;
    public float slotMedalChancePercent = 21f;
    public int medalsPerSpin = 3;
    public float slotSpinDuration = 1.4f;
    public int rescueSpins = 4;
    public int[] jackpotResetValues = { 150, 250, 500 };
    public float sideHoleWidth = .55f;
    public bool showLotteryStatus = false;

    public string LastError { get; private set; } = "";
    public bool LoadedFromDisk { get; private set; }
    public string SavePath => string.IsNullOrWhiteSpace(SettingsFileOverride)
        ? Path.Combine(Application.persistentDataPath, "medal-arcade-settings.json") : SettingsFileOverride;
    public bool IsBusy
    {
        get
        {
            ResolveReferences();
            return controller != null && (controller.IsUpperDrawing || controller.IsColorRoundActive || controller.IsSlotSpinning);
        }
    }

    [Serializable]
    public sealed class Values
    {
        public int version = 1;
        public float targetPayoutPercent = 90f;
        public float slotBallChancePercent = 49f;
        public float slotMedalChancePercent = 21f;
        public int medalsPerSpin = 3;
        public float slotSpinDuration = 1.4f;
        public int rescueSpins = 4;
        public int[] jackpotResetValues = { 150, 250, 500 };
        public float sideHoleWidth = .55f;
        public bool showLotteryStatus = false;
    }

    private void Start()
    {
        ResolveReferences();
        Load();
        // Start runs after the controller has initialized its progressive pool baselines.
        // Retain any paid-medal increments which happened before this component's Start.
        ApplyToGame(false);
    }

    private void ResolveReferences()
    {
        if (game == null) game = GetComponent<MedalPusherGame>();
        if (controller == null) controller = GetComponent<MedalSlotJackpotController>();
    }

    public Values CaptureValues() => new Values {
        targetPayoutPercent = targetPayoutPercent, slotBallChancePercent = slotBallChancePercent,
        slotMedalChancePercent = slotMedalChancePercent, medalsPerSpin = medalsPerSpin,
        slotSpinDuration = slotSpinDuration, rescueSpins = rescueSpins,
        jackpotResetValues = jackpotResetValues == null ? null : (int[])jackpotResetValues.Clone(),
        sideHoleWidth = sideHoleWidth, showLotteryStatus = showLotteryStatus
    };

    /// <summary>Checks all fields before changing any component value.</summary>
    public bool TrySetValues(Values values, out string error)
    {
        if (!Validate(values, out error)) return false;
        if (IsBusy) { error = "抽選中は変更できません。抽選終了後に適用してください。"; return false; }
        CopyFrom(values); LastError = ""; return true;
    }

    public static bool Validate(Values values, out string error)
    {
        if (values == null || values.version != 1) { error = "対応していない設定データです。"; return false; }
        if (!Range(values.targetPayoutPercent, 0, 100)) { error = "目標払出率は0～100%で入力してください。"; return false; }
        if (!Range(values.slotBallChancePercent, 0, 100) || !Range(values.slotMedalChancePercent, 0, 100))
        { error = "スロット当選率は0～100%で入力してください。"; return false; }
        if (values.slotBallChancePercent + values.slotMedalChancePercent > 100f)
        { error = "ボールとメダルの当選率の合計は100%以下にしてください。"; return false; }
        if (values.medalsPerSpin < 1 || values.medalsPerSpin > 20)
        { error = "スロット1回の投入枚数は1～20枚で入力してください。"; return false; }
        if (!Range(values.slotSpinDuration, .1f, 10f)) { error = "スロット時間は0.1～10秒で入力してください。"; return false; }
        if (values.rescueSpins < 0 || values.rescueSpins > 100)
        { error = "ボール救済は0～100回で入力してください。0で無効になります。"; return false; }
        if (values.jackpotResetValues == null || values.jackpotResetValues.Length != 3 ||
            Array.Exists(values.jackpotResetValues, value => value < 1 || value > 1000000))
        { error = "JACKPOTの初期枚数は各1～1,000,000枚で入力してください。"; return false; }
        if (!Range(values.sideHoleWidth, 0, 1.5f)) { error = "横穴の幅は0～1.5で入力してください。0で穴を閉じます。"; return false; }
        error = ""; return true;
    }

    private static bool Range(float value, float minimum, float maximum) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    private static float Clamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);

    private void ClampValues()
    {
        targetPayoutPercent = Clamp(targetPayoutPercent, 0, 100, 90);
        slotBallChancePercent = Clamp(slotBallChancePercent, 0, 100, 49);
        slotMedalChancePercent = Clamp(slotMedalChancePercent, 0, 100, 21);
        float total = slotBallChancePercent + slotMedalChancePercent;
        if (total > 100) { slotBallChancePercent *= 100 / total; slotMedalChancePercent *= 100 / total; }
        medalsPerSpin = Mathf.Clamp(medalsPerSpin, 1, 20);
        slotSpinDuration = Clamp(slotSpinDuration, .1f, 10, 1.4f);
        rescueSpins = Mathf.Clamp(rescueSpins, 0, 100);
        if (jackpotResetValues == null || jackpotResetValues.Length != 3) jackpotResetValues = new[] { 150, 250, 500 };
        for (int i = 0; i < 3; i++) jackpotResetValues[i] = Mathf.Clamp(jackpotResetValues[i], 1, 1000000);
        sideHoleWidth = Clamp(sideHoleWidth, 0, 1.5f, .55f);
    }

    public void ApplyToGame(bool resetPools)
    {
        ResolveReferences();
        if (game == null || controller == null) throw new InvalidOperationException("設定対象のゲームが見つかりません。");
        if (IsBusy) throw new InvalidOperationException("抽選中は設定を変更できません。");
        ClampValues();
        controller.ApplySettings(this, resetPools);
        game.ConfigureSideHoles(sideHoleWidth);
    }

    private void CopyFrom(Values values)
    {
        targetPayoutPercent = values.targetPayoutPercent;
        slotBallChancePercent = values.slotBallChancePercent; slotMedalChancePercent = values.slotMedalChancePercent;
        medalsPerSpin = values.medalsPerSpin; slotSpinDuration = values.slotSpinDuration; rescueSpins = values.rescueSpins;
        jackpotResetValues = (int[])values.jackpotResetValues.Clone(); sideHoleWidth = values.sideHoleWidth;
        showLotteryStatus = values.showLotteryStatus;
    }

    public bool Load()
    {
        LastError = ""; LoadedFromDisk = false;
        if (!persistSettings) return false;
        try
        {
            if (!File.Exists(SavePath)) return false;
            var values = JsonUtility.FromJson<Values>(File.ReadAllText(SavePath));
            if (!Validate(values, out string error)) { LastError = "保存済み設定を読み込めませんでした。" + error; return false; }
            if (IsBusy) { LastError = "抽選中のため保存済み設定は読み込んでいません。"; return false; }
            CopyFrom(values); LoadedFromDisk = true; return true;
        }
        catch (Exception) { LastError = "保存済み設定を読み込めませんでした。現在の設定で開始します。"; return false; }
    }

    public bool Save()
    {
        LastError = "";
        if (!persistSettings) return true;
        if (!Validate(CaptureValues(), out string error)) { LastError = error; return false; }
        string temporary = SavePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SavePath)));
            File.WriteAllText(temporary, JsonUtility.ToJson(CaptureValues(), true));
            if (File.Exists(SavePath)) File.Replace(temporary, SavePath, null);
            else File.Move(temporary, SavePath);
            return true;
        }
        catch (Exception)
        {
            LastError = "設定は適用しましたが、ファイルへ保存できませんでした。";
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
