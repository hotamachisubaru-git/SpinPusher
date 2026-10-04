using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows slots, the three progressive jackpots, and the selected inlet.</summary>
public class MedalArcadeUI : MonoBehaviour
{
    public MedalPusherGame game;
    public MedalSlotJackpotController controller;
    public TMP_Text reelsText;
    public TMP_Text spinMeterText;
    public TMP_Text statusText;
    public TMP_Text upperWinText;
    public TMP_Text[] jackpotTexts;
    public TMP_Text[] stationMeters;
    public TMP_Text[] towerJackpotValues;
    public int[] towerJackpotKinds = { -1, 0, 2, 1 };
    public Image[] inletButtons;
    public Renderer[] inletLights;
    public MedalPusherCameraView cameraView;
    public TMP_Text payoutBanner;
    public Image celebrationFlash;
    public RectTransform[] confetti;

    private float celebrationUntil;
    private float celebrationLength;
    private bool celebratingJackpot;
    private Color celebrationColor;
    private bool lotteryFocused;

    void Start()
    {
        if (controller == null) controller = FindFirstObjectByType<MedalSlotJackpotController>();
        if (game == null && controller != null) game = controller.game;
        if (cameraView == null) cameraView = FindFirstObjectByType<MedalPusherCameraView>();
        if (controller != null)
        {
            controller.OnStateChanged += Refresh;
            controller.OnLotteryStarted += OnDrawStarted;
            controller.OnLotteryFinished += OnDrawFinished;
            controller.OnUpperLotteryStarted += OnUpperDrawStarted;
            controller.OnUpperLotteryFinished += OnUpperDrawFinished;
            controller.OnColorSelectionStarted += OnColorSelectionStarted;
            controller.OnColorSelectionFinished += OnColorSelectionFinished;
        }
        if (game != null) game.OnInletChanged += OnInletChanged;
        Refresh();
        OnInletChanged(game != null ? game.selectedInlet : 1);
    }

    public void Refresh()
    {
        if (controller == null) return;
        if (reelsText != null) reelsText.text = controller.SlotDisplay;
        if (spinMeterText != null)
            spinMeterText.text = "投入 " + controller.MedalsTowardSpin + "/" + controller.medalsPerSpin + " ｜ 抽選待ち " + controller.SpinCredits;
        if (statusText != null) statusText.text = controller.StatusText;
        if (upperWinText != null)
            upperWinText.text = controller.IsSelectingColor ? "ポケット開放：赤／青／黄の入賞待ち\n白い板：残り"
                + (controller.upperStation != null && controller.upperStation.OutBlockUsed ? 0 : 1) + "回"
                : controller.IsUpperDrawing ? "上段ボール抽選中：WIN 100枚超えでポケット開放"
                : "上段WIN " + controller.LastUpperWin + "枚 ｜ 100枚超えでポケット開放";
        string[] names = { "赤", "青", "黄" };
        Color[] colors = { new Color(1, .25f, .3f), new Color(.25f, .75f, 1), new Color(1, .82f, .25f) };
        for (int i = 0; i < 3; i++)
        {
            int pool = GetPool(i);
            string text = names[i] + "JACKPOT " + pool + "枚";
            if (jackpotTexts != null && i < jackpotTexts.Length && jackpotTexts[i] != null)
            { jackpotTexts[i].text = text; jackpotTexts[i].color = colors[i]; }
            if (stationMeters != null && i < stationMeters.Length && stationMeters[i] != null)
            { stationMeters[i].text = text; stationMeters[i].color = colors[i]; }
            if (controller.stations != null && i < controller.stations.Length && controller.stations[i] != null &&
                controller.stations[i].jackpotPocketText != null)
            {
                controller.stations[i].jackpotPocketText.text = "JACKPOT\n" + pool + "枚";
                controller.stations[i].jackpotPocketText.color = colors[i];
            }
        }
        for (int i = 0; towerJackpotValues != null && i < towerJackpotValues.Length; i++)
        {
            TMP_Text display = towerJackpotValues[i];
            if (display == null) continue;
            int kind = towerJackpotKinds != null && i < towerJackpotKinds.Length ? towerJackpotKinds[i] : -1;
            display.richText = true;
            display.text = kind < 0
                ? "<color=#FF5E6E>赤 " + GetPool(0) + "枚</color>\n<color=#60C8FF>青 " + GetPool(1)
                    + "枚</color>\n<color=#FFD45A>黄 " + GetPool(2) + "枚</color>"
                : GetPool(kind) + "枚";
        }
    }

    private int GetPool(int kind) => controller != null && controller.jackpotPools != null &&
        kind >= 0 && kind < controller.jackpotPools.Length ? controller.jackpotPools[kind] : 0;

    private void OnInletChanged(int index)
    {
        for (int i = 0; inletButtons != null && i < inletButtons.Length; i++)
            if (inletButtons[i] != null) inletButtons[i].color = i == index ? new Color(.05f, .7f, .85f) : new Color(.14f, .16f, .24f);
        for (int i = 0; inletLights != null && i < inletLights.Length; i++)
        {
            if (inletLights[i] == null) continue;
            var block = new MaterialPropertyBlock();
            Color color = i == index ? new Color(.05f, .95f, 1) : new Color(.15f, .18f, .24f);
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", i == index ? color * 2 : Color.black);
            inletLights[i].SetPropertyBlock(block);
        }
    }

    private void OnDrawStarted(MedalJackpotKind kind)
    {
        celebrationUntil = 0;
        lotteryFocused = true;
        if (controller == null || controller.stations == null) return;
        foreach (var station in controller.stations)
            if (station != null && station.kind == kind && cameraView != null) { cameraView.FocusStation(station); break; }
        Refresh();
    }
    private void OnDrawFinished(MedalJackpotKind kind, int payout, bool jackpot)
    {
        if (controller == null || !controller.IsColorRoundActive) ReleaseLotteryCamera();
        celebratingJackpot = jackpot;
        celebrationLength = jackpot ? 4f : 1.6f;
        celebrationUntil = Time.unscaledTime + celebrationLength;
        celebrationColor = kind == MedalJackpotKind.Ruby ? new Color(1, .3f, .4f)
            : kind == MedalJackpotKind.Sapphire ? new Color(.25f, .8f, 1) : new Color(1, .85f, .25f);
        if (payoutBanner != null)
        {
            string message = jackpot ? "JACKPOT!" : controller != null && controller.LastDrawTimedOut ? "補償ボーナス" : "当たり！";
            string colorName = kind == MedalJackpotKind.Ruby ? "赤" : kind == MedalJackpotKind.Sapphire ? "青" : "黄";
            payoutBanner.text = colorName + message + "\n+" + payout + "枚";
            payoutBanner.gameObject.SetActive(true);
        }
        Refresh();
    }

    private void OnUpperDrawStarted()
    {
        celebrationUntil = 0;
        lotteryFocused = true;
        if (controller != null && controller.upperStation != null && cameraView != null)
            cameraView.FocusStation(controller.upperStation);
        Refresh();
    }

    private void OnColorSelectionStarted()
    {
        celebrationUntil = 0;
        lotteryFocused = true;
        if (controller != null && controller.upperStation != null && cameraView != null)
            cameraView.FocusStation(controller.upperStation);
        Refresh();
    }

    private void OnUpperDrawFinished(int payout)
    {
        if (controller == null || !controller.IsColorRoundActive) ReleaseLotteryCamera();
        celebratingJackpot = false;
        celebrationLength = 1.6f;
        celebrationUntil = Time.unscaledTime + celebrationLength;
        celebrationColor = new Color(.3f, 1f, .5f);
        if (payoutBanner != null)
        {
            payoutBanner.text = (controller != null && controller.LastDrawTimedOut ? "上段抽選 補償ボーナス" : "上段WIN")
                + "\n+" + payout + "枚";
            payoutBanner.gameObject.SetActive(true);
        }
        Refresh();
    }

    private void OnColorSelectionFinished(MedalJackpotKind? kind, bool timedOut)
    {
        if (timedOut || !kind.HasValue)
        {
            ReleaseLotteryCamera();
            celebratingJackpot = false;
            celebrationLength = 1.6f;
            celebrationUntil = Time.unscaledTime + celebrationLength;
            celebrationColor = new Color(.3f, 1f, .5f);
            if (payoutBanner != null)
            {
                payoutBanner.text = timedOut ? "色選択 補償ボーナス\n+" + (controller != null ? controller.LastPayout : 15) + "枚"
                    : "アウト\n今回の抽選は終了";
                payoutBanner.gameObject.SetActive(true);
            }
        }
        Refresh();
    }

    private void ReleaseLotteryCamera()
    {
        lotteryFocused = false;
        if (cameraView != null) cameraView.ReleaseStation();
    }

    void Update()
    {
        if (lotteryFocused && controller != null && !controller.IsUpperDrawing &&
            !controller.IsColorRoundActive && !controller.ActiveKind.HasValue) ReleaseLotteryCamera();
        float remaining = celebrationUntil - Time.unscaledTime;
        bool visible = remaining > 0;
        float elapsed = celebrationLength - remaining;
        float alpha = visible ? Mathf.Clamp01(remaining / .5f) : 0;
        if (payoutBanner != null)
        {
            payoutBanner.gameObject.SetActive(visible);
            payoutBanner.color = new Color(celebrationColor.r, celebrationColor.g, celebrationColor.b, alpha);
            payoutBanner.transform.localScale = Vector3.one * (1 + (visible ? .035f * Mathf.Sin(elapsed * 12) : 0));
        }
        if (celebrationFlash != null)
            celebrationFlash.color = new Color(celebrationColor.r, celebrationColor.g, celebrationColor.b,
                visible && celebratingJackpot ? .16f * alpha * Mathf.Exp(-elapsed * 2) : 0);
        for (int i = 0; confetti != null && i < confetti.Length; i++)
        {
            var piece = confetti[i];
            if (piece == null) continue;
            piece.gameObject.SetActive(visible && celebratingJackpot);
            if (!visible || !celebratingJackpot) continue;
            float phase = (elapsed * .27f + i * .137f) % 1f;
            float x = .04f + (i * .173f % .92f) + .04f * Mathf.Sin(elapsed * 3 + i);
            piece.anchorMin = piece.anchorMax = new Vector2(x, 1.08f - phase * 1.2f);
            piece.anchoredPosition = Vector2.zero;
            piece.localRotation = Quaternion.Euler(0, 0, elapsed * (100 + i * 11));
            var image = piece.GetComponent<Image>();
            if (image != null) image.color = new Color(i % 2 == 0 ? 1 : celebrationColor.r,
                i % 2 == 0 ? .85f : celebrationColor.g, i % 2 == 0 ? .3f : celebrationColor.b, alpha);
        }
    }
    void OnDestroy()
    {
        if (controller != null)
        {
            controller.OnStateChanged -= Refresh;
            controller.OnLotteryStarted -= OnDrawStarted;
            controller.OnLotteryFinished -= OnDrawFinished;
            controller.OnUpperLotteryStarted -= OnUpperDrawStarted;
            controller.OnUpperLotteryFinished -= OnUpperDrawFinished;
            controller.OnColorSelectionStarted -= OnColorSelectionStarted;
            controller.OnColorSelectionFinished -= OnColorSelectionFinished;
        }
        if (game != null) game.OnInletChanged -= OnInletChanged;
    }
}
