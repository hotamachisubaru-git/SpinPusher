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
    public const int BallSlotSymbol = 0;
    public const float HighProbabilityMultiplier = 1.5f;
    public string SlotDisplay { get; private set; } = "待機 ｜ 待機 ｜ 待機";
    public IReadOnlyList<int> ReelSymbols => reelSymbols;
    public IReadOnlyList<int> LastSlotSymbols => lastSlotSymbols;
    public bool IsHighProbability { get; private set; }
    public bool LastSlotWasWin { get; private set; }
    public bool LastSlotWasDirectJpc { get; private set; }
    public int LastSlotPayout { get; private set; }
    public int LastSlotBasePayout { get; private set; }
    public int LastSlotBonusPayout { get; private set; }
    public string LastSlotResult { get; private set; } = "スロット待機";
    public int PendingBallRefunds => ballRefunds.Count;
    public long PendingDirectJpcDraws
    {
        get
        {
            SynchronizeUpperDrawStarts();
            long count = deferredDirectJpcDraws;
            foreach (UpperDrawStart start in ballDrawStarts) if (start.directJpc) count++;
            return count;
        }
    }
    public int TotalDirectJpcWins { get; private set; }
    public int TotalBallRefunds { get; private set; }
    public float EffectiveSlotBallChancePercent { get { GetEffectiveSlotChances(out float ball, out _); return ball; } }
    public float EffectiveSlotMedalChancePercent { get { GetEffectiveSlotChances(out _, out float medal); return medal; } }
    public float EffectiveSlotWinChancePercent { get { GetEffectiveSlotChances(out float ball, out float medal); return ball + medal; } }
    public string StatusText
    {
        get
        {
            if (IsUpperDrawing) return "上段WIN " + LiveUpperWin + "枚 ｜ " + statusMessage;
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
    public int PendingBallDraws => ballDraws.Count + (IsUpperDrawing ? 1 : 0);
    public MedalJackpotKind? ActiveKind { get; private set; }
    public bool IsUpperDrawing { get; private set; }
    public bool IsUpperRoundActive => IsUpperDrawing;
    public bool AreColorGatesUnlocked => IsUpperDrawing && (directJpcGatesUnlocked || LiveUpperWin > 100);
    public bool IsDirectJpcUpperRound => IsUpperDrawing && directJpcGatesUnlocked;
    public bool IsColorRoundActive { get; private set; }
    public bool IsSelectingColor { get; private set; }
    public int LiveUpperWin { get; private set; }
    public int LastUpperWin { get; private set; }
    public int CarriedUpperWin { get; private set; }
    public int UpperWinCarriedAtStart { get; private set; }
    public int InitialUpperWin { get; private set; }
    public int UpperWinEarnedThisRound => Mathf.Max(0, LiveUpperWin - UpperWinCarriedAtStart);
    public bool LastUpperWasBumperDraw { get; private set; }
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
    private readonly Queue<UpperDrawStart> ballDrawStarts = new Queue<UpperDrawStart>();
    private readonly Queue<MedalJackpotKind> ballRefunds = new Queue<MedalJackpotKind>();
    private readonly int[] reelSymbols = { -1, -1, -1 };
    private readonly int[] lastSlotSymbols = { -1, -1, -1 };
    private readonly int[] resetPools = { 150, 250, 500 };
    private struct UpperDrawStart
    {
        public int initialWin;
        public bool directJpc;
    }
    private MedalBallLotteryStation activeStation;
    private int activeTicket;
    private MedalBallLotteryStation upperRoundStation;
    private int upperTicket;
    private int ticketSequence;
    private int spinsWithoutBall;
    private int paidMedalsTowardRubyJackpot;
    private int deferredMedals;
    private int deferredTopMedals;
    private bool colorVisitPending;
    private bool isEndingUpperRound;
    private bool activeUpperUsesBumpers;
    private bool directJpcGatesUnlocked;
    private long highProbabilityChain;
    private long upperRoundHighChain = -1;
    private long deferredDirectJpcDraws;
    private float spinEndsAt;
    private float nextReelUpdate;
    private float nextSpinAt;
    private float nextBallRefundAt;
    private float nextLotteryAt;
    private float lotteryDeadline;
    private float lotteryResultUntil;
    private string lotteryResultMessage;
    private string statusMessage = "丸い投入口に入るとスロット抽選";

    void Awake()
    {
        if (jackpotPools == null || jackpotPools.Length != 3)
            jackpotPools = new[] { 150, 250, 500 };
        for (int i = 0; i < 3; i++)
        {
            jackpotPools[i] = Mathf.Max(1, jackpotPools[i]);
            resetPools[i] = jackpotPools[i];
        }
        StatusText = "丸い投入口に入るとスロット抽選";
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
        if (resetPools) { spinsWithoutBall = 0; paidMedalsTowardRubyJackpot = 0; SetHighProbability(false); CarriedUpperWin = 0; }
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
        // Integer hundredths avoid floating-point drift at the 100-medal boundary.
        paidMedalsTowardRubyJackpot++;
        if (paidMedalsTowardRubyJackpot >= 100)
        {
            paidMedalsTowardRubyJackpot = 0;
            AddJackpotProgress(MedalJackpotKind.Ruby, 1);
        }
        NotifyState();
    }

    public void NotifySlotPocketEntry()
    {
        if (SpinCredits < 256) SpinCredits++;
        else { AwardMedals(5); StatusText = "スロット待ち満杯：+5枚"; }
        if (!IsSlotSpinning && Time.time >= nextSpinAt) BeginSpin();
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
                SetReelSymbols(ReelSymbol(), ReelSymbol(), ReelSymbol());
                NotifyState();
            }
        }
        // A paused or slow upper ball is relaunched by the station with its ticket and WIN intact.
        // Only actual OUT, destruction, or disabling ends an upper round.
        if (IsUpperDrawing && (upperRoundStation == null || !upperRoundStation.isActiveAndEnabled)) CancelUpperRound();
        if (IsColorRoundActive)
        {
            if (Time.time >= lotteryDeadline) CancelColorVisit();
            else if (colorVisitPending)
            {
                if (Time.time >= nextLotteryAt) BeginNextColorLottery();
            }
            else if (activeStation == null || !activeStation.isActiveAndEnabled) CancelColorVisit();
        }
        if (ballRefunds.Count > 0 && Time.time >= nextBallRefundAt)
        {
            nextBallRefundAt = Time.time + .25f;
            if (TrySpawnRefundBall(ballRefunds.Peek()))
            {
                ballRefunds.Dequeue();
                TotalBallRefunds++;
                StatusText = "ボール払い戻し：緑ボールを押して回収";
                NotifyState();
            }
        }
        while (deferredDirectJpcDraws > 0 && PendingBallDraws < MaxBallDraws)
        {
            deferredDirectJpcDraws--;
            EnqueueUpperDraw(MedalJackpotKind.Ruby, 100, true);
        }
        if (!IsUpperDrawing && ballDraws.Count > 0 && Time.time >= nextLotteryAt) BeginNextUpperLottery();
    }

    private static int ReelSymbol() => UnityEngine.Random.Range(0, 10);

    private void SetReelSymbols(int first, int second, int third)
    {
        reelSymbols[0] = first;
        reelSymbols[1] = second;
        reelSymbols[2] = third;
        SlotDisplay = SymbolLabel(first) + " ｜ " + SymbolLabel(second) + " ｜ " + SymbolLabel(third);
    }

    private static string SymbolLabel(int symbol) => symbol == BallSlotSymbol ? "ボール" : symbol < 0 ? "待機" : symbol.ToString();

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
        bool wasHighProbability = IsHighProbability;
        IsSlotSpinning = false;
        nextSpinAt = Time.time + 0.65f;
        TotalSpins++;
        float factor = AdaptiveSlotFactor();
        GetEffectiveSlotChances(out float ballChance, out float medalChance);
        float outcome = UnityEngine.Random.Range(0f, 100f);
        bool ball = outcome < ballChance;
        bool seven = ball && UnityEngine.Random.value < 9f / 49f;
        bool rescued = false;
        float rescueFactor = factor * (IsHighProbability ? HighProbabilityMultiplier : 1f);
        int effectiveRescue = rescueFactor > 0f && rescueSpins > 0 ? Mathf.CeilToInt(rescueSpins / rescueFactor) : 0;
        LastSlotWasWin = false;
        LastSlotWasDirectJpc = false;
        LastSlotPayout = 0;
        LastSlotBasePayout = 0;
        LastSlotBonusPayout = 0;
        if (!ball)
        {
            if (spinsWithoutBall < int.MaxValue) spinsWithoutBall++;
            if (targetPayoutPercent > 0f && slotBallChancePercent > 0f && effectiveRescue > 0 && spinsWithoutBall >= effectiveRescue)
            { ball = true; seven = false; rescued = true; }
        }
        if (ball)
        {
            spinsWithoutBall = 0;
            LastSlotWasWin = true;
            LastSlotWasDirectJpc = seven;
            SetReelSymbols(seven ? 7 : BallSlotSymbol, seven ? 7 : BallSlotSymbol, seven ? 7 : BallSlotSymbol);
            if (seven)
            {
                SetHighProbability(true);
                AwardNumericSlot(7, wasHighProbability);
                TotalDirectJpcWins++;
                QueueDirectJpcDraw();
                StatusText = "777：+" + LastSlotPayout + "枚" + (LastSlotBonusPayout > 0 ? "（確変ボーナス+5枚）" : "")
                    + " ｜ ダイレクトJPC・100WIN以上で開始";
            }
            else
            {
                MedalJackpotKind kind = (MedalJackpotKind)UnityEngine.Random.Range(0, 3);
                SpawnOrQueueBall(kind, rescued ? "ボール救済当たり" : "ボール当たり");
            }
            if (game != null) game.PlayEventSound(game.bonusSound);
        }
        else if (outcome < ballChance + medalChance)
        {
            // Seven is reserved for the direct JPC result; all other digits share this prize branch.
            int digit = UnityEngine.Random.Range(1, 9);
            if (digit >= 7) digit++;
            SetReelSymbols(digit, digit, digit);
            SetHighProbability((digit & 1) == 1);
            LastSlotWasWin = true;
            AwardNumericSlot(digit, wasHighProbability);
            StatusText = digit + "揃い：+" + LastSlotPayout + "枚" + (LastSlotBonusPayout > 0 ? "（確変ボーナス+5枚）" : "")
                + " ｜ " + (IsHighProbability ? "確変中" : "確変終了・通常確率へ");
            if (game != null) game.PlayEventSound(game.bonusSound);
        }
        else
        {
            int first = ReelSymbol();
            int second = (first + UnityEngine.Random.Range(1, 10)) % 10;
            SetReelSymbols(first, second, ReelSymbol());
            StatusText = "スロットはずれ：次の入賞で挑戦";
        }
        Array.Copy(reelSymbols, lastSlotSymbols, reelSymbols.Length);
        LastSlotResult = statusMessage;
        NotifyState();
    }

    private void AwardNumericSlot(int digit, bool wasHighProbability)
    {
        LastSlotBasePayout = digit * 10;
        LastSlotBonusPayout = wasHighProbability && (digit & 1) == 1 ? 5 : 0;
        LastSlotPayout = LastSlotBasePayout + LastSlotBonusPayout;
        AwardMedals(LastSlotPayout);
    }

    private void SetHighProbability(bool enabled)
    {
        if (IsHighProbability == enabled) return;
        IsHighProbability = enabled;
        CarriedUpperWin = 0;
        highProbabilityChain++;
    }

    private void GetEffectiveSlotChances(out float ballChance, out float medalChance)
    {
        float factor = AdaptiveSlotFactor() * (IsHighProbability ? HighProbabilityMultiplier : 1f);
        ballChance = Mathf.Clamp(slotBallChancePercent, 0f, 100f) * factor;
        medalChance = Mathf.Clamp(slotMedalChancePercent, 0f, 100f) * factor;
        float totalChance = ballChance + medalChance;
        float totalCap = IsHighProbability ? 99f : 95f;
        if (totalChance > totalCap)
        {
            ballChance *= totalCap / totalChance;
            medalChance *= totalCap / totalChance;
        }
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
        if (ballRefunds.Count == 0 && TrySpawnRefundBall(kind))
        {
            TotalBallRefunds++;
            StatusText = message + "：緑ボールを押して回収";
        }
        else
        {
            ballRefunds.Enqueue(kind);
            StatusText = message + " ｜ 緑ボール払い戻し待ち（盤面の空きを待機）";
        }
    }

    private bool TrySpawnRefundBall(MedalJackpotKind kind)
    {
        int index = (int)kind;
        GameObject prefab = ballPrefabs != null && index >= 0 && index < ballPrefabs.Length ? ballPrefabs[index] : null;
        bool boardFull = game != null && (CountBoardBalls() >= MaxBoardBalls ||
            game.CountBoardItems(true) >= Mathf.Max(0, game.maxPrizesOnBoard));
        if (game == null || boardFull || prefab == null || prefab.GetComponent<Rigidbody>() == null)
            return false;
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
        return true;
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
            EnqueueUpperDraw(MedalJackpotKind.Ruby, 0, false);
            StatusText = "緑ボール獲得：上段抽選待ち";
        }
        NotifyState();
    }

    private void QueueDirectJpcDraw()
    {
        if (PendingBallDraws < MaxBallDraws) EnqueueUpperDraw(MedalJackpotKind.Ruby, 100, true);
        else deferredDirectJpcDraws++;
    }

    private void EnqueueUpperDraw(MedalJackpotKind kind, int initialWin, bool directJpc)
    {
        SynchronizeUpperDrawStarts();
        ballDraws.Enqueue(kind);
        ballDrawStarts.Enqueue(new UpperDrawStart { initialWin = initialWin, directJpc = directJpc });
    }

    private void SynchronizeUpperDrawStarts()
    {
        // Existing editor diagnostics clear the original queue by reflection. Reconcile that clear
        // so a later ordinary ball can never inherit a removed 777 ticket's initial WIN.
        while (ballDrawStarts.Count > ballDraws.Count) ballDrawStarts.Dequeue();
        while (ballDrawStarts.Count < ballDraws.Count) ballDrawStarts.Enqueue(default(UpperDrawStart));
    }

    private void BeginNextUpperLottery()
    {
        MedalBallLotteryStation station = upperStation;
        if (station == null || !station.isActiveAndEnabled || station.IsDrawing || station.IsSelectingColor)
        {
            nextLotteryAt = Time.time + .25f;
            StatusText = "上段抽選待ち：抽選券は保持";
            NotifyState();
            return;
        }
        SynchronizeUpperDrawStarts();
        ballDraws.Dequeue();
        UpperDrawStart start = ballDrawStarts.Dequeue();
        upperRoundStation = station;
        upperTicket = ++ticketSequence;
        activeUpperUsesBumpers = station.usesBumpers;
        // Resolve the carry when the next ball starts, after the previous OUT.
        // Balls collected during a busy draw inherit its final WIN, not an early snapshot.
        UpperWinCarriedAtStart = IsHighProbability ? CarriedUpperWin : 0;
        InitialUpperWin = Mathf.Max(Mathf.Max(0, start.initialWin), UpperWinCarriedAtStart);
        LiveUpperWin = InitialUpperWin;
        upperRoundHighChain = IsHighProbability ? highProbabilityChain : -1;
        directJpcGatesUnlocked = start.directJpc;
        IsUpperDrawing = true;
        TotalUpperDraws++;
        SetColorRotation(AreColorGatesUnlocked);
        StatusText = start.directJpc ? "ダイレクトJPC：" + InitialUpperWin + "WINから3色開放"
            : UpperWinCarriedAtStart > 0 ? "確変：" + UpperWinCarriedAtStart + "WIN引き継ぎ。増加分を払い出し"
                : "丸いバンパーに接触で+2WIN。100WIN超で3色開放・色抽選後も上段続行";
        OnUpperLotteryStarted?.Invoke();
        NotifyState();
        station.BeginDraw(this, upperTicket, LiveUpperWin, directJpcGatesUnlocked);
    }

    public void NotifyUpperBumperHit(MedalBallLotteryStation station, int ticket, int totalWin)
    {
        if (!IsUpperDrawing || !activeUpperUsesBumpers || station == null || station != upperStation ||
            station != upperRoundStation || ticket != upperTicket || !station.IsDrawing || station.IsUpperSuspended || !station.usesBumpers ||
            totalWin != station.BumperWin || (long)totalWin != (long)LiveUpperWin + 2L) return;
        LiveUpperWin = totalWin;
        AddJackpotProgress(MedalJackpotKind.Sapphire, 1);
        StatusText = "接触+2WIN" + (AreColorGatesUnlocked ? "：3色開放。色抽選後も同じ上段ボールで続行" : "：100WIN超えを目指そう");
        if (AreColorGatesUnlocked) SetColorRotation(true);
        NotifyState();
    }

    public bool NotifyUpperColorRoute(MedalBallLotteryStation station, int ticket, MedalJackpotKind kind)
    {
        if (!IsUpperDrawing || IsColorRoundActive || station == null || station != upperRoundStation ||
            station != upperStation || ticket != upperTicket || !station.IsDrawing || !station.IsUpperSuspended ||
            !AreColorGatesUnlocked || station.BumperWin != LiveUpperWin || (int)kind < 0 || (int)kind >= 3) return false;
        IsColorRoundActive = true;
        AddJackpotProgress(MedalJackpotKind.Amber, 10);
        TotalColorRounds++;
        ActiveKind = kind;
        colorVisitPending = true;
        lotteryDeadline = Time.time + 20f;
        nextLotteryAt = Time.time;
        BeginNextColorLottery();
        return true;
    }

    private void BeginNextColorLottery()
    {
        if (!IsUpperDrawing || !IsColorRoundActive || !colorVisitPending || !ActiveKind.HasValue) return;
        MedalJackpotKind kind = ActiveKind.Value;
        int index = (int)kind;
        MedalBallLotteryStation station = stations != null && index < stations.Length ? stations[index] : null;
        if (station != null && station.isActiveAndEnabled && (station.IsDrawing || station.IsSelectingColor))
        {
            nextLotteryAt = Time.time + .25f;
            StatusText = KindLabel(kind) + "の抽選待ち：上段ボールは保持";
            NotifyState();
            return;
        }
        if (station == null || !station.isActiveAndEnabled || station.kind != kind || station.isUpperStation)
        {
            CompleteColorLottery(false, 0, true);
            return;
        }
        colorVisitPending = false;
        activeStation = station;
        activeTicket = ++ticketSequence;
        lotteryDeadline = Time.time + Mathf.Max(20f, station.drawTimeout + 2f);
        TotalLotteries++;
        StatusText = KindLabel(kind) + "の抽選中。結果後に同じ上段ボールへ戻る";
        OnLotteryStarted?.Invoke(kind);
        NotifyState();
        station.BeginDraw(this, activeTicket);
    }

    public void NotifyOutBlockConsumed(MedalBallLotteryStation station, int ticket)
    {
        if (!IsUpperDrawing || station == null || station != upperStation || station != upperRoundStation ||
            ticket != upperTicket || !station.IsDrawing || station.IsUpperSuspended || !station.OutBlockUsed) return;
        StatusText = "白い板がボールの流出を防いだ";
        NotifyState();
    }

    public void FinishColorSelection(MedalBallLotteryStation station, int ticket, MedalJackpotKind? kind, bool timedOut)
    {
        // Compatibility only: separate selector draws no longer exist.
    }

    public void FinishLottery(MedalBallLotteryStation station, int ticket, bool jackpot, int smallReward)
    {
        if (station == null || station.IsDrawing) return;
        if (IsUpperDrawing && station == upperRoundStation && station == upperStation && ticket == upperTicket)
            CompleteUpperLottery(activeUpperUsesBumpers ? station.BumperWin : smallReward, station.LastTimedOut);
        else if (IsColorRoundActive && ActiveKind.HasValue && station == activeStation &&
            ticket == activeTicket && station.kind == ActiveKind.Value)
            CompleteColorLottery(jackpot, smallReward, station.LastTimedOut);
    }

    private void CompleteUpperLottery(int reward, bool timedOut)
    {
        if (!IsUpperDrawing) return;
        bool bumperDraw = activeUpperUsesBumpers;
        int finalWin = Mathf.Max(0, reward);
        int payout = bumperDraw ? Mathf.Max(0, finalWin - UpperWinCarriedAtStart) : finalWin;
        if (bumperDraw && IsHighProbability && upperRoundHighChain == highProbabilityChain)
            CarriedUpperWin = finalWin;
        upperRoundHighChain = -1;
        IsUpperDrawing = false;
        upperRoundStation = null;
        upperTicket = 0;
        activeUpperUsesBumpers = false;
        directJpcGatesUnlocked = false;
        bool previousEnding = isEndingUpperRound;
        isEndingUpperRound = true;
        if (IsColorRoundActive) CancelColorVisit();
        isEndingUpperRound = previousEnding;
        LastUpperWasBumperDraw = bumperDraw;
        LiveUpperWin = bumperDraw ? finalWin : 0;
        LastUpperWin = timedOut && !bumperDraw ? 0 : finalWin;
        LastPayout = payout;
        LastKind = null;
        LastDrawTimedOut = timedOut;
        AwardMedals(payout);
        SetColorRotation(false);
        ShowLotteryResult((timedOut ? "上段抽選終了：" : "アウト：") + finalWin + "WIN ｜ +" + payout + "枚払い出し"
            + (IsHighProbability && CarriedUpperWin > 0 ? "・WIN引き継ぎ" : ""));
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
        int payout = timedOut ? 0 : jackpot ? Mathf.Max(1, jackpotPools[index]) : Mathf.Max(0, smallReward);
        ActiveKind = null;
        activeStation = null;
        activeTicket = 0;
        colorVisitPending = false;
        IsColorRoundActive = false;
        LastKind = kind;
        LastPayout = payout;
        LastDrawTimedOut = timedOut;
        if (jackpot)
        {
            TotalJackpots++;
            jackpotPools[index] = resetPools[index];
            if (kind == MedalJackpotKind.Ruby) paidMedalsTowardRubyJackpot = 0;
        }
        AwardMedals(payout, jackpot);
        bool resumeFailed = false;
        if (!isEndingUpperRound && IsUpperDrawing)
        {
            if (upperRoundStation != null && upperRoundStation.isActiveAndEnabled && upperRoundStation.IsDrawing &&
                upperRoundStation.IsUpperSuspended) upperRoundStation.ResumeUpperDraw(this, upperTicket);
            resumeFailed = upperRoundStation == null || !upperRoundStation.isActiveAndEnabled ||
                !upperRoundStation.IsDrawing || upperRoundStation.IsUpperSuspended;
        }
        ShowLotteryResult(KindLabel(kind) + (jackpot ? "JACKPOT：+" + payout + "枚" : timedOut ? "抽選中断" : "抽選結果：+" + payout + "枚")
            + (IsUpperDrawing ? " ｜ 同じ上段ボールで続行" : ""));
        if (game != null && payout > 0) game.PlayEventSound(jackpot ? game.jackpotSound : game.bonusSound);
        OnLotteryFinished?.Invoke(kind, payout, jackpot);
        NotifyState();
        if (resumeFailed) CancelUpperRound();
    }

    private void ShowLotteryResult(string message)
    {
        StatusText = message;
        lotteryResultMessage = message;
        nextLotteryAt = Time.time + 1.5f;
        lotteryResultUntil = nextLotteryAt;
    }

    private void SetColorRotation(bool enabled)
    {
        if (stations == null) return;
        foreach (MedalBallLotteryStation station in stations)
            if (station != null) station.SetRotationEnabled(enabled);
    }

    private void AddJackpotProgress(MedalJackpotKind kind, int amount)
    {
        int index = (int)kind;
        jackpotPools[index] = (int)Math.Min(int.MaxValue, (long)jackpotPools[index] + amount);
    }

    private void CancelColorVisit()
    {
        if (!IsColorRoundActive || !ActiveKind.HasValue) return;
        int ticket = activeTicket;
        if (activeStation != null) activeStation.CancelDraw(this, ticket);
        if (IsColorRoundActive && activeTicket == ticket) CompleteColorLottery(false, 0, true);
    }

    private void CancelUpperRound()
    {
        if (!IsUpperDrawing) return;
        bool previousEnding = isEndingUpperRound;
        isEndingUpperRound = true;
        if (IsColorRoundActive) CancelColorVisit();
        int ticket = upperTicket;
        if (upperRoundStation != null) upperRoundStation.CancelDraw(this, ticket);
        if (IsUpperDrawing && upperTicket == ticket) CompleteUpperLottery(LiveUpperWin, true);
        isEndingUpperRound = previousEnding;
    }

    void OnDisable()
    {
        if (!Application.isPlaying) { SetColorRotation(false); return; }
        CancelUpperRound();
        if (IsColorRoundActive) CancelColorVisit();
        IsSelectingColor = false;
        SetColorRotation(false);
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
