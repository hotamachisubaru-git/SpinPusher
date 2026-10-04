using System;
using System.Collections.Generic;
using UnityEngine;

public enum MedalJackpotKind { Ruby, Sapphire, Amber }

/// <summary>Turns paid medals into slots, physical board balls, and serialized lottery draws.</summary>
public class MedalSlotJackpotController : MonoBehaviour
{
    public MedalPusherGame game;
    public MedalBallLotteryStation[] stations;
    public MedalBallLotteryStation upperStation;
    public GameObject[] ballPrefabs;
    public int medalsPerSpin = 3;
    public float slotSpinDuration = 1.4f;
    public int[] jackpotPools = { 150, 250, 500 };
    public float targetPayoutPercent = 90f;
    public float slotBallChancePercent = 49f;
    public float slotMedalChancePercent = 21f;
    public int rescueSpins = 4;

    public const int MaxBoardBalls = 3;
    public const int MaxBallDraws = 12;
    public string SlotDisplay { get; private set; } = "待機 ｜ 待機 ｜ 待機";
    public string StatusText
    {
        get
        {
            if (IsUpperDrawing) return "上段ボール抽選中 ｜ " + statusMessage;
            if (IsSelectingColor) return "ポケット開放：赤／青／黄の入賞待ち ｜ " + statusMessage;
            if (ActiveKind.HasValue) return KindLabel(ActiveKind.Value) + "の抽選中 ｜ " + statusMessage;
            if (Time.time < lotteryResultUntil && !string.IsNullOrEmpty(lotteryResultMessage))
                return lotteryResultMessage == statusMessage ? statusMessage : lotteryResultMessage + " ｜ " + statusMessage;
            return statusMessage;
        }
        private set { statusMessage = value; }
    }
    public int SpinCredits { get; private set; }
    public bool IsSlotSpinning { get; private set; }
    public int PendingBallDraws => ballDraws.Count + (IsUpperDrawing || IsColorRoundActive ? 1 : 0);
    public MedalJackpotKind? ActiveKind { get; private set; }
    public bool IsUpperDrawing { get; private set; }
    public bool IsColorRoundActive { get; private set; }
    public bool IsSelectingColor { get; private set; }
    public int LastUpperWin { get; private set; }
    public int TotalUpperDraws { get; private set; }
    public int TotalColorRounds { get; private set; }
    public int MedalsTowardSpin { get; private set; }
    public int TotalSpins { get; private set; }
    public int TotalLotteries { get; private set; }
    public int TotalJackpots { get; private set; }
    public int LastPayout { get; private set; }
    public MedalJackpotKind? LastKind { get; private set; }
    public bool LastDrawTimedOut { get; private set; }
    public Action OnStateChanged;
    public Action<MedalJackpotKind> OnLotteryStarted;
    public Action<MedalJackpotKind, int, bool> OnLotteryFinished;
    public Action OnUpperLotteryStarted;
    public Action<int> OnUpperLotteryFinished;
    public Action OnColorSelectionStarted;
    public Action<MedalJackpotKind?, bool> OnColorSelectionFinished;

    private readonly Queue<MedalJackpotKind> ballDraws = new Queue<MedalJackpotKind>();
    private readonly int[] resetPools = { 150, 250, 500 };
    private MedalBallLotteryStation activeStation;
    private int activeTicket;
    private int ticketSequence;
    private int spinsWithoutBall;
    private int deferredMedals;
    private int deferredTopMedals;
    private int colorRoundTicket;
    private MedalJackpotKind? selectedColor;
    private bool selectionPending;
    private bool outBlockNoticeSent;
    private float spinEndsAt;
    private float nextReelUpdate;
    private float nextSpinAt;
    private float nextLotteryAt;
    private float lotteryDeadline;
    private float lotteryResultUntil;
    private string lotteryResultMessage;
    private string statusMessage = "メダル3枚投入でスロット抽選";
    private bool HasActiveDraw => IsUpperDrawing || IsSelectingColor || ActiveKind.HasValue;

    void Awake()
    {
        if (jackpotPools == null || jackpotPools.Length != 3)
            jackpotPools = new[] { 150, 250, 500 };
        for (int i = 0; i < 3; i++)
        {
            jackpotPools[i] = Mathf.Max(1, jackpotPools[i]);
            resetPools[i] = jackpotPools[i];
        }
        StatusText = "メダル" + Mathf.Max(1, medalsPerSpin) + "枚投入でスロット抽選";
    }

    void Start()
    {
        if (game == null) game = GetComponent<MedalPusherGame>();
        if (game == null) game = FindAnyObjectByType<MedalPusherGame>();
        if (game != null)
        {
            game.OnMedalInserted += OnMedalInserted;
            if (deferredMedals > 0)
            {
                game.AddMedals(deferredMedals);
                game.QueuePayoutMedals(deferredMedals - deferredTopMedals, false);
                game.QueuePayoutMedals(deferredTopMedals, true);
                deferredMedals = deferredTopMedals = 0;
            }
        }
        MedalArcadeSettings settings = GetComponent<MedalArcadeSettings>();
        if (settings != null) ApplySettings(settings, false);
        SetColorRotation(false);
        NotifyState();
    }

    public void ApplySettings(MedalArcadeSettings settings, bool resetPools)
    {
        if (settings == null) return;
        targetPayoutPercent = Mathf.Clamp(settings.targetPayoutPercent, 0f, 100f);
        slotBallChancePercent = Mathf.Clamp(settings.slotBallChancePercent, 0f, 100f);
        slotMedalChancePercent = Mathf.Clamp(settings.slotMedalChancePercent, 0f, 100f);
        medalsPerSpin = Mathf.Clamp(settings.medalsPerSpin, 1, 20);
        slotSpinDuration = Mathf.Clamp(settings.slotSpinDuration, .1f, 10f);
        rescueSpins = Mathf.Clamp(settings.rescueSpins, 0, 100);
        if (jackpotPools == null || jackpotPools.Length != 3) jackpotPools = new[] { 150, 250, 500 };
        int[] defaults = { 150, 250, 500 };
        for (int i = 0; i < 3; i++)
        {
            int baseValue = settings.jackpotResetValues != null && i < settings.jackpotResetValues.Length
                ? Mathf.Clamp(settings.jackpotResetValues[i], 1, 1000000) : defaults[i];
            long progressive = resetPools ? 0 : Math.Max(0L, (long)jackpotPools[i] - this.resetPools[i]);
            this.resetPools[i] = baseValue;
            jackpotPools[i] = (int)Math.Min(int.MaxValue, baseValue + progressive);
        }
        if (resetPools) spinsWithoutBall = 0;
        MedalsTowardSpin = Mathf.Min(MedalsTowardSpin, medalsPerSpin - 1);
        NotifyState();
    }

    void OnDestroy()
    {
        if (game != null) game.OnMedalInserted -= OnMedalInserted;
        SetColorRotation(false);
    }

    private void OnMedalInserted()
    {
        for (int i = 0; i < jackpotPools.Length; i++)
            if (jackpotPools[i] < int.MaxValue) jackpotPools[i]++;
        MedalsTowardSpin++;
        if (MedalsTowardSpin >= Mathf.Max(1, medalsPerSpin))
        {
            MedalsTowardSpin = 0;
            if (SpinCredits < 256) SpinCredits++;
            else { AwardMedals(5); StatusText = "スロット待ち満杯：+5枚"; }
        }
        NotifyState();
    }

    void Update()
    {
        if (!IsSlotSpinning && SpinCredits > 0 && Time.time >= nextSpinAt) BeginSpin();
        if (IsSlotSpinning)
        {
            if (Time.time >= spinEndsAt) CompleteSpin();
            else if (Time.time >= nextReelUpdate)
            {
                nextReelUpdate = Time.time + 0.07f;
                SlotDisplay = ReelSymbol() + " ｜ " + ReelSymbol() + " ｜ " + ReelSymbol();
                NotifyState();
            }
        }
        if (HasActiveDraw &&
            (activeStation == null || !activeStation.isActiveAndEnabled || Time.time >= lotteryDeadline))
            CancelActiveDraw();
        if (!HasActiveDraw && Time.time >= nextLotteryAt)
        {
            if (IsColorRoundActive) BeginNextColorLottery();
            else if (ballDraws.Count > 0) BeginNextUpperLottery();
        }
    }

    private static string ReelSymbol()
    {
        int symbol = UnityEngine.Random.Range(0, 5);
        return symbol == 0 ? "ボール" : symbol == 1 ? "メダル" : symbol == 2 ? "7" : symbol == 3 ? "3" : "1";
    }

    private void BeginSpin()
    {
        SpinCredits--;
        IsSlotSpinning = true;
        spinEndsAt = Time.time + Mathf.Max(0.1f, slotSpinDuration);
        nextReelUpdate = 0f;
        StatusText = "スロット抽選中";
        NotifyState();
    }

    private void CompleteSpin()
    {
        IsSlotSpinning = false;
        nextSpinAt = Time.time + 0.65f;
        TotalSpins++;
        float factor = AdaptiveSlotFactor();
        float ballChance = Mathf.Clamp(slotBallChancePercent, 0f, 100f) * factor;
        float medalChance = Mathf.Clamp(slotMedalChancePercent, 0f, 100f) * factor;
        float totalChance = ballChance + medalChance;
        if (totalChance > 95f) { ballChance *= 95f / totalChance; medalChance *= 95f / totalChance; }
        float outcome = UnityEngine.Random.Range(0f, 100f);
        bool ball = outcome < ballChance;
        bool seven = ball && UnityEngine.Random.value < 9f / 49f;
        bool rescued = false;
        int effectiveRescue = factor > 0f && rescueSpins > 0 ? Mathf.CeilToInt(rescueSpins / factor) : 0;
        if (!ball)
        {
            if (spinsWithoutBall < int.MaxValue) spinsWithoutBall++;
            if (targetPayoutPercent > 0f && slotBallChancePercent > 0f && effectiveRescue > 0 && spinsWithoutBall >= effectiveRescue)
            { ball = true; seven = false; rescued = true; }
        }
        if (ball)
        {
            spinsWithoutBall = 0;
            MedalJackpotKind kind = (MedalJackpotKind)UnityEngine.Random.Range(0, 3);
            SlotDisplay = seven ? "7 ｜ 7 ｜ 7" : "ボール ｜ ボール ｜ ボール";
            if (seven) AwardMedals(10);
            SpawnOrQueueBall(kind, rescued ? "ボール救済当たり" : seven ? "777当たり +10枚" : "ボール当たり");
            if (game != null) game.PlayEventSound(game.bonusSound);
        }
        else if (outcome < ballChance + medalChance)
        {
            int reward = UnityEngine.Random.value < 0.5f ? 5 : 10;
            SlotDisplay = "メダル ｜ メダル ｜ メダル";
            AwardMedals(reward);
            StatusText = "スロット当たり：+" + reward + "枚";
            if (game != null) game.PlayEventSound(game.bonusSound);
        }
        else
        {
            SlotDisplay = "1 ｜ 3 ｜ 7";
            StatusText = "スロットはずれ：次の投入で挑戦";
        }
        NotifyState();
    }

    private float AdaptiveSlotFactor()
    {
        if (targetPayoutPercent <= 0f) return 0f;
        if (game == null || game.TotalPaidMedals < 20) return 1f;
        double actualRatio = (double)game.TotalReturnedMedals / game.TotalPaidMedals;
        return Mathf.Clamp((float)(targetPayoutPercent / 100f / Math.Max(.05, actualRatio)), .005f, 1.5f);
    }

    private void SpawnOrQueueBall(MedalJackpotKind kind, string message)
    {
        int index = (int)kind;
        GameObject prefab = ballPrefabs != null && index < ballPrefabs.Length ? ballPrefabs[index] : null;
        bool boardFull = game != null && (CountBoardBalls() >= MaxBoardBalls ||
            game.CountBoardItems(true) >= Mathf.Max(0, game.maxPrizesOnBoard));
        if (game == null || boardFull || prefab == null || prefab.GetComponent<Rigidbody>() == null)
        {
            bool queued = PendingBallDraws < MaxBallDraws;
            QueueBallDraw(kind);
            if (queued)
                StatusText = message + (boardFull ? " ｜ 盤面満杯：" : " ｜ 抽選券に交換：") + "上段抽選待ち";
            return;
        }
        Vector3 local = new Vector3(UnityEngine.Random.Range(-2.5f, 2.5f), 1.4f,
            UnityEngine.Random.Range(-1.3f, 0.3f));
        GameObject ball = Instantiate(prefab, game.transform.TransformPoint(local), UnityEngine.Random.rotation,
            game.itemsRoot != null ? game.itemsRoot : game.transform);
        ball.SetActive(true);
        MedalItem item = ball.GetComponent<MedalItem>();
        if (item == null) item = ball.AddComponent<MedalItem>();
        item.isPrize = true;
        item.isBall = true;
        item.ballKind = kind;
        item.collected = false;
        item.displayName = "緑ボール";
        Rigidbody body = ball.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        game.RegisterItem(item);
        StatusText = message + "：緑ボールを押して回収";
    }

    private int CountBoardBalls()
    {
        if (game == null) return 0;
        Transform root = game.itemsRoot != null ? game.itemsRoot : game.transform;
        int count = 0;
        foreach (MedalItem item in root.GetComponentsInChildren<MedalItem>())
            if (item.isBall && !item.collected) count++;
        return count;
    }

    public void CollectBall(MedalItem item)
    {
        if (item == null || !item.isBall || item.collected) return;
        item.collected = true;
        MedalJackpotKind kind = item.ballKind;
        if (game != null) game.UnregisterItem(item);
        Destroy(item.gameObject);
        QueueBallDraw(kind);
    }

    public void QueueBallDraw(MedalJackpotKind kind)
    {
        if (PendingBallDraws >= MaxBallDraws)
        {
            AwardMedals(10);
            StatusText = "上段抽選待ち満杯：+10枚";
        }
        else
        {
            ballDraws.Enqueue(MedalJackpotKind.Ruby);
            StatusText = "緑ボール獲得：上段抽選待ち";
        }
        NotifyState();
    }

    private void BeginNextUpperLottery()
    {
        MedalBallLotteryStation station = upperStation;
        if (station != null && station.isActiveAndEnabled && station.IsDrawing)
        {
            nextLotteryAt = Time.time + .25f;
            StatusText = "上段抽選待ち：抽選券は保持";
            NotifyState();
            return;
        }
        ballDraws.Dequeue();
        if (station == null || !station.isActiveAndEnabled)
        {
            AwardMedals(15);
            ShowLotteryResult("上段抽選休止：+15枚補償");
            NotifyState();
            return;
        }
        activeStation = station;
        activeTicket = ++ticketSequence;
        IsUpperDrawing = true;
        lotteryDeadline = Time.time + 20f;
        TotalUpperDraws++;
        StatusText = "緑ボールの行方を見よう";
        OnUpperLotteryStarted?.Invoke();
        NotifyState();
        station.BeginDraw(this, activeTicket);
    }

    // A single upper WIN unlocks physical pocket selection, never automatic color draws.
    private void BeginColorRound(int ticket)
    {
        IsColorRoundActive = true;
        TotalColorRounds++;
        colorRoundTicket = ticket;
        selectedColor = null;
        selectionPending = true;
        SetColorRotation(true);
    }

    private void BeginNextColorLottery()
    {
        if (selectionPending) { BeginColorSelection(); return; }
        if (!selectedColor.HasValue) { EndColorRound(); return; }
        MedalJackpotKind kind = selectedColor.Value;
        int index = (int)kind;
        MedalBallLotteryStation station = stations != null && index < stations.Length ? stations[index] : null;
        if (station != null && station.isActiveAndEnabled && station.IsDrawing)
        {
            nextLotteryAt = Time.time + .25f;
            StatusText = KindLabel(kind) + "の抽選待ち：抽選券は保持";
            NotifyState();
            return;
        }
        if (station == null || !station.isActiveAndEnabled)
        {
            ActiveKind = kind;
            CompleteColorLottery(false, 15, true);
            return;
        }
        activeStation = station;
        activeTicket = colorRoundTicket;
        ActiveKind = kind;
        lotteryDeadline = Time.time + 20f;
        TotalLotteries++;
        StatusText = "ボールの行方を見よう";
        OnLotteryStarted?.Invoke(kind);
        NotifyState();
        station.BeginDraw(this, activeTicket);
    }

    private void BeginColorSelection()
    {
        MedalBallLotteryStation station = upperStation;
        if (station != null && station.isActiveAndEnabled && station.IsDrawing)
        {
            nextLotteryAt = Time.time + .25f;
            StatusText = "色選択待ち：抽選券は保持";
            NotifyState();
            return;
        }
        if (station == null || !station.isActiveAndEnabled)
        {
            CompleteColorSelection(null, true);
            return;
        }
        selectionPending = false;
        IsSelectingColor = true;
        outBlockNoticeSent = false;
        activeStation = station;
        activeTicket = colorRoundTicket;
        lotteryDeadline = Time.time + 20f;
        StatusText = "緑ボールの入賞ポケットを見よう";
        OnColorSelectionStarted?.Invoke();
        NotifyState();
        station.BeginColorSelection(this, activeTicket);
    }

    public void NotifyOutBlockConsumed(MedalBallLotteryStation station, int ticket)
    {
        if (!IsSelectingColor || !IsColorRoundActive || station == null || station != upperStation ||
            station != activeStation || ticket != activeTicket || !station.OutBlockUsed || outBlockNoticeSent) return;
        outBlockNoticeSent = true;
        StatusText = "白い板が流出を防いだ：ガード残り0回";
        NotifyState();
    }

    public void FinishColorSelection(MedalBallLotteryStation station, int ticket, MedalJackpotKind? kind, bool timedOut)
    {
        if (!IsSelectingColor || !IsColorRoundActive || station == null ||
            station != activeStation || station != upperStation || ticket != activeTicket) return;
        CompleteColorSelection(kind, timedOut);
    }

    private void CompleteColorSelection(MedalJackpotKind? kind, bool timedOut)
    {
        if (!IsColorRoundActive) return;
        bool valid = !timedOut && kind.HasValue && (int)kind.Value >= 0 && (int)kind.Value < 3;
        bool naturalOut = !timedOut && !kind.HasValue;
        IsSelectingColor = false;
        activeStation = null;
        activeTicket = 0;
        selectionPending = false;
        if (naturalOut)
        {
            LastDrawTimedOut = false;
            LastPayout = 0;
            EndColorRound();
            ShowLotteryResult("アウト：ボールが流出。今回のJACKPOT抽選は終了");
        }
        else if (!valid)
        {
            LastDrawTimedOut = true;
            LastPayout = 15;
            AwardMedals(15);
            EndColorRound();
            ShowLotteryResult("色選択中断：+15枚補償");
        }
        else
        {
            LastDrawTimedOut = false;
            selectedColor = kind.Value;
            ShowLotteryResult(KindLabel(kind.Value) + "に入賞：" + KindLabel(kind.Value) + "JACKPOT抽選へ");
            nextLotteryAt = Time.time + .45f;
        }
        OnColorSelectionFinished?.Invoke(valid ? kind : null, !valid && !naturalOut);
        NotifyState();
    }

    public void FinishLottery(MedalBallLotteryStation station, int ticket, bool jackpot, int smallReward)
    {
        if (!HasActiveDraw || IsSelectingColor || station == null || station != activeStation || ticket != activeTicket) return;
        if (IsUpperDrawing) CompleteUpperLottery(smallReward, station.LastTimedOut);
        else CompleteColorLottery(jackpot, smallReward, station.LastTimedOut);
    }

    private void CompleteUpperLottery(int reward, bool timedOut)
    {
        if (!IsUpperDrawing) return;
        int roundTicket = activeTicket;
        int payout = Mathf.Max(0, reward);
        IsUpperDrawing = false;
        activeStation = null;
        activeTicket = 0;
        LastUpperWin = timedOut ? 0 : payout;
        LastPayout = payout;
        LastKind = null;
        LastDrawTimedOut = timedOut;
        AwardMedals(payout);
        // A single completed physical WIN must exceed 100. Wins are never accumulated.
        if (!timedOut && payout > 100) BeginColorRound(roundTicket);
        else SetColorRotation(false);
        ShowLotteryResult(timedOut ? "上段抽選時間切れ：+" + payout + "枚補償"
            : "上段WIN " + payout + "枚" + (IsColorRoundActive ? "：ポケット選択へ" : "：100枚を超えると3色ポケット開放"));
        if (game != null && payout > 0) game.PlayEventSound(game.bonusSound);
        OnUpperLotteryFinished?.Invoke(payout);
        NotifyState();
    }

    private void CompleteColorLottery(bool jackpot, int smallReward, bool timedOut)
    {
        if (!ActiveKind.HasValue) return;
        MedalJackpotKind kind = ActiveKind.Value;
        jackpot = jackpot && !timedOut;
        int index = (int)kind;
        int payout = jackpot ? Mathf.Max(1, jackpotPools[index]) : Mathf.Max(0, smallReward);
        ActiveKind = null;
        activeStation = null;
        activeTicket = 0;
        LastKind = kind;
        LastPayout = payout;
        LastDrawTimedOut = timedOut;
        if (jackpot) { TotalJackpots++; jackpotPools[index] = resetPools[index]; }
        AwardMedals(payout, jackpot);
        EndColorRound();
        ShowLotteryResult(KindLabel(kind) + (jackpot ? "JACKPOT：+" : timedOut ? "抽選時間切れ：+" : "抽選結果：+")
            + payout + "枚");
        if (game != null && payout > 0) game.PlayEventSound(jackpot ? game.jackpotSound : game.bonusSound);
        OnLotteryFinished?.Invoke(kind, payout, jackpot);
        NotifyState();
    }

    private void ShowLotteryResult(string message)
    {
        StatusText = message;
        lotteryResultMessage = message;
        nextLotteryAt = Time.time + 1.5f;
        lotteryResultUntil = nextLotteryAt;
    }

    private void EndColorRound()
    {
        IsColorRoundActive = false;
        IsSelectingColor = false;
        selectionPending = false;
        selectedColor = null;
        if (upperStation != null && colorRoundTicket != 0)
            upperStation.CancelDraw(this, colorRoundTicket);
        colorRoundTicket = 0;
        SetColorRotation(false);
    }

    private void SetColorRotation(bool enabled)
    {
        if (stations == null) return;
        foreach (MedalBallLotteryStation station in stations)
            if (station != null) station.SetRotationEnabled(enabled);
    }

    private void CancelActiveDraw()
    {
        int ticket = activeTicket;
        if (activeStation != null) activeStation.CancelDraw(this, ticket);
        // A deleted station or rejected callback still returns this controller's ticket once.
        if (!HasActiveDraw || activeTicket != ticket) return;
        if (IsSelectingColor) CompleteColorSelection(null, true);
        else if (IsUpperDrawing) CompleteUpperLottery(15, true);
        else CompleteColorLottery(false, 15, true);
    }

    void OnDisable()
    {
        if (!Application.isPlaying) { SetColorRotation(false); return; }
        bool pendingColorRound = IsColorRoundActive && !HasActiveDraw;
        if (HasActiveDraw) CancelActiveDraw();
        if (pendingColorRound) AwardMedals(15);
        EndColorRound();
        if (IsSlotSpinning)
        {
            IsSlotSpinning = false;
            if (SpinCredits < 256) SpinCredits++;
            else AwardMedals(5);
        }
        NotifyState();
    }

    private void AwardMedals(int count, bool fromTop = false)
    {
        if (count <= 0) return;
        if (game != null)
        {
            game.AddMedals(count);
            game.QueuePayoutMedals(count, fromTop);
        }
        else
        {
            deferredMedals += count;
            if (fromTop) deferredTopMedals += count;
        }
    }

    private static string KindLabel(MedalJackpotKind kind) =>
        kind == MedalJackpotKind.Ruby ? "赤" : kind == MedalJackpotKind.Sapphire ? "青" : "黄";
    private void NotifyState() { OnStateChanged?.Invoke(); }
}
