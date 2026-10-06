using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shows slots, the three progressive jackpots, and ongoing ball lotteries.</summary>
public class MedalArcadeUI : MonoBehaviour
{
    public MedalPusherGame game;
    public MedalSlotJackpotController controller;
    public MedalArcadeSettings settings;
    public TMP_Text reelsText;
    public TMP_Text worldReelsText;
    public TMP_Text spinMeterText;
    public TMP_Text statusText;
    public GameObject lotteryStatusPanel;
    public TMP_Text upperWinText;
    public TMP_Text payoutRemainingText;
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
    private long shownPendingPayout = -1;
    private float nextReelRefresh;
    private string lastReelValue;
    private bool lotteryStatusPanelSearched;

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
        Refresh();
    }

    public void Refresh()
    {
        RefreshLotteryStatusVisibility();
        if (controller == null) return;
        RefreshReels();
        RefreshPayout();
        if (spinMeterText != null)
            spinMeterText.text = (controller.IsHighProbability ? "<color=#FF7589>確変中</color>" : "通常")
                + (controller.IsHighProbability && controller.CarriedUpperWin > 0 ? " ｜ 持越" + controller.CarriedUpperWin + "WIN" : "")
                + " ｜ 抽選待ち " + controller.SpinCredits;
        if (statusText != null) statusText.text = controller.StatusText;
        if (upperWinText != null)
            upperWinText.text = controller.IsUpperDrawing ? controller.LiveUpperWin.ToString("D2") + "WIN（枚獲得）\n"
                    + (controller.UpperWinCarriedAtStart > 0 ? "引継" + controller.UpperWinCarriedAtStart + "WIN ｜ 今回+" + controller.UpperWinEarnedThisRound + "枚"
                        : controller.IsColorRoundActive ? "色抽選中：結果後に同じ上段ボールで続行"
                        : controller.AreColorGatesUnlocked ? "3色開放：赤／青／黄へ入賞で色抽選"
                        : "100WIN超で3色開放・色抽選後も上段続行")
                : controller.LastUpperWin.ToString("D2") + "WIN（枚獲得）\n"
                    + (controller.IsHighProbability && controller.CarriedUpperWin > 0 ? "次球へ" + controller.CarriedUpperWin + "WIN引き継ぎ" : "100WIN超で3色開放");
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

    private void RefreshLotteryStatusVisibility()
    {
        if (settings == null)
        {
            if (game != null) settings = game.GetComponent<MedalArcadeSettings>();
            else if (controller != null && controller.game != null) settings = controller.game.GetComponent<MedalArcadeSettings>();
        }
        if (lotteryStatusPanel == null && !lotteryStatusPanelSearched)
        {
            lotteryStatusPanelSearched = true;
            Transform parent = statusText != null ? statusText.transform.parent : transform;
            Transform panel = parent == null ? null : parent.Find("DrawStatus");
            if (panel != null) lotteryStatusPanel = panel.gameObject;
        }
        bool visible = settings != null && settings.showLotteryStatus;
        if (statusText != null && statusText.gameObject.activeSelf != visible) statusText.gameObject.SetActive(visible);
        if (lotteryStatusPanel != null && lotteryStatusPanel.activeSelf != visible) lotteryStatusPanel.SetActive(visible);
    }

    private int GetPool(int kind) => controller != null && controller.jackpotPools != null &&
        kind >= 0 && kind < controller.jackpotPools.Length ? controller.jackpotPools[kind] : 0;

    public static string FormatSlotSymbol(int symbol)
    {
        if (symbol < 0) return "<color=#AEBBCC>−</color>";
        if (symbol == 0) return "<color=#57FF76>ボール</color>";
        string color = symbol == 7 ? "#FFFFFF" : symbol % 2 == 1 ? "#FF526C" : "#55C4FF";
        return "<color=" + color + ">" + symbol + "</color>";
    }

    private void RefreshReels()
    {
        if (controller == null) return;
        var symbols = controller.ReelSymbols;
        string value = symbols == null || symbols.Count != 3 ? controller.SlotDisplay
            : FormatSlotSymbol(symbols[0]) + " <color=#8391A6>|</color> "
                + FormatSlotSymbol(symbols[1]) + " <color=#8391A6>|</color> " + FormatSlotSymbol(symbols[2]);
        RenderReels(reelsText, value);
        RenderReels(worldReelsText, value);
        if (lastReelValue != value)
        {
            lastReelValue = value;
            MedalPusherUI.Instance?.RefreshVisibleTextMeshes();
        }
    }

    private static void RenderReels(TMP_Text text, string value)
    {
        if (text == null) return;
        text.richText = true;
        text.text = value;
        text.ForceMeshUpdate();
        for (int i = 0; i < text.textInfo.characterCount; i++)
        {
            var character = text.textInfo.characterInfo[i];
            if (!character.isVisible || character.character != '7') continue;
            var colors = text.textInfo.meshInfo[character.materialReferenceIndex].colors32;
            for (int v = 0; v < 4; v++)
                colors[character.vertexIndex + v] = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * .18f + v * .23f, 1f), .75f, 1f);
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private void RefreshPayout()
    {
        if (game == null || payoutRemainingText == null) return;
        long pending = game.PendingPayoutMedals;
        if (pending == shownPendingPayout) return;
        shownPendingPayout = pending;
        payoutRemainingText.text = "PAYOUT  " + pending.ToString("D2") + "枚";
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
        if (controller != null && controller.IsUpperRoundActive && controller.upperStation != null)
        {
            lotteryFocused = true;
            if (cameraView != null) cameraView.FocusStation(controller.upperStation);
        }
        else ReleaseLotteryCamera();
        celebratingJackpot = jackpot;
        celebrationLength = jackpot ? 4f : 1.6f;
        celebrationUntil = Time.unscaledTime + celebrationLength;
        celebrationColor = kind == MedalJackpotKind.Ruby ? new Color(1, .3f, .4f)
            : kind == MedalJackpotKind.Sapphire ? new Color(.25f, .8f, 1) : new Color(1, .85f, .25f);
        if (payoutBanner != null)
        {
            bool interrupted = controller != null && controller.LastDrawTimedOut;
            string message = jackpot ? "JACKPOT!" : interrupted ? "抽選中断" : "当たり！";
            string colorName = kind == MedalJackpotKind.Ruby ? "赤" : kind == MedalJackpotKind.Sapphire ? "青" : "黄";
            payoutBanner.text = colorName + message + (interrupted && payout == 0 ? "" : "\n" + (payout > 0 ? "+" : "") + payout + "枚");
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
            bool compensation = controller != null && controller.LastDrawTimedOut && !controller.LastUpperWasBumperDraw;
            payoutBanner.text = (compensation ? "上段抽選 補償ボーナス" : controller != null ? controller.LastUpperWin + "WIN" : "上段WIN")
                + "\n" + (payout > 0 ? "+" : "") + payout + "枚";
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
        RefreshPayout();
        if (Time.unscaledTime >= nextReelRefresh)
        { nextReelRefresh = Time.unscaledTime + .08f; RefreshReels(); }
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
    }
}
