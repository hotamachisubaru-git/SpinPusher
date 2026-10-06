using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Launches one Rigidbody ball and resolves only the pocket it actually enters.
/// Launch variation and a light guide force influence motion, never the reward.
/// </summary>
[DisallowMultipleComponent]
public sealed class MedalBallLotteryStation : MonoBehaviour
{
    public MedalJackpotKind kind;
    public bool isUpperStation;
    public bool rotationEnabled;
    public bool usesBumpers;
    public MedalLotteryBumper[] bumpers;
    public MedalLotteryOutflow upperOutflow;
    [Min(.1f)] public float bumperBowlOuterRadius = 2.48f;
    public float bumperBowlFloorY = -.75f;
    public TMP_Text jackpotPocketText;
    public TMP_Text upperWinText;
    [Min(.1f)] public float upperMinimumHorizontalSpeed = 1.35f;
    [Min(0f)] public float upperSpeedMaintenanceAcceleration = 1.7f;
    [Tooltip("Speed used to distinguish stopped balls from confined collision motion in recovery diagnostics.")]
    [Min(.01f)] public float upperStallSpeed = .22f;
    [Tooltip("Seconds allowed without moving beyond the progress anchor before the same ball is relaunched.")]
    [Min(.1f)] public float upperStallDuration = 1.35f;
    [Tooltip("Horizontal world distance from the progress anchor that starts a fresh motion window.")]
    [Min(.01f)] public float upperStallDisplacement = .12f;
    public Transform colorLaunchPoint;
    public MedalColorRoutePocket[] colorRoutePockets;
    public Rigidbody[] colorGateBodies;
    public Vector3[] colorGateClosedPositions;
    public Collider[] normalWinPocketTriggers;
    public float colorGateRaiseHeight = 1.1f;
    public float colorGateMoveSpeed = 4f;
    public float colorSelectionTimeout = 10f;
    public Vector3 colorLaunchVelocity = new Vector3(0f, -.08f, -1.45f);
    public Vector3 colorLaunchVelocityJitter = new Vector3(.45f, 0f, .30f);
    public Vector3 colorLaunchPositionJitter = new Vector3(1.25f, 0f, 0f);
    public Vector3 colorGuideLocalCenter = new Vector3(0f, 0f, -5.3f);
    [Min(.1f)] public float colorBowlOuterRadius = 1.95f;
    public float colorBowlFloorY = -.75f;
    public Rigidbody outBlockBody;
    public Vector3 outBlockClosedPosition;
    public MedalOutBlock outBlock;
    public MedalLotteryOutflow outflow;
    public MedalOutBlock[] outBlocks;
    public Rigidbody[] outBlockBodies;
    public Vector3[] outBlockClosedPositions;
    public MedalLotteryOutflow[] outflows;
    public TMP_Text outBlockText;
    public float outBlockRetractionDelay = .18f;
    public float outBlockRaiseHeight = -1.65f;
    public bool regenerateOutBlocks = true;
    [Min(0f)] public float outBlockRegenerationDelay = .40f;
    [Min(0f)] public float outBlockRegenerationCooldown = .18f;
    public Transform launchPoint;
    public GameObject lotteryBallPrefab;
    public Transform rotor;
    public Rigidbody rotorBody;
    public Transform dividerRotor;
    public Rigidbody dividerBody;
    public float currentDividerSpeed { get; private set; }
    [Min(.1f)] public float drawTimeout = 10f;
    public float rotorDegreesPerSecond = 28f;
    public Vector3 localLaunchVelocity = new Vector3(-.4f, 0f, 1.2f);
    public bool launchVelocityRelativeToPoint;
    public Vector3 launchVelocityJitter = new Vector3(.2f, 0f, .3f);
    public Vector3 launchPositionJitter = Vector3.zero;
    public bool radialGuide = true;
    public Vector3 guideLocalCenter = new Vector3(0f, 0f, -.55f);
    public Vector3 guideLocalDirection = Vector3.back;
    [Min(0f)] public float guideAcceleration = .3f;

    public bool IsDrawing { get; private set; }
    public bool IsUpperSuspended { get; private set; }
    public float UpperElapsedSeconds => IsDrawing ? Mathf.Max(0f, (IsUpperSuspended ? upperSuspendedAt : Time.time) - startedAt) : lastUpperElapsed;
    public bool IsSelectingColor { get; private set; }
    public bool ColorGatesOpen { get; private set; }
    public bool OutBlockUsed { get; private set; }
    public int UsedOutBlockCount { get; private set; }
    public bool[] OutBlockUsedStates { get; private set; } = new bool[0];
    public int OutBlockRegenerationCount { get; private set; }
    public bool IsOutBlockRegenerating { get; private set; }
    public bool IsOutBlockRegenerationPending => outBlockRegenerationPending;
    public int LastOutBlockRegenerationWin { get; private set; }
    public int LastOutBlockRegenerationTicket { get; private set; }
    public string LastOutBlockRegenerationBallId { get; private set; } = "";
    public bool LastOutBlockRegenerationColorGatesOpen { get; private set; }
    public bool LastExitedBowl { get; private set; }
    public int BumperWin { get; private set; }
    public int TotalBumperHits { get; private set; }
    public MedalLotteryBumper LastBumper { get; private set; }
    public bool OutBlockReady => usesBumpers ? IsDrawing && !IsUpperSuspended && !outBlockRegenerationPending
        && !IsOutBlockRegenerating && Time.time >= outBlockRearmAt && UsedOutBlockCount < OutBlockUsedStates.Length
        : IsSelectingColor && !OutBlockUsed;
    public bool IsRotating => !IsUpperSuspended && (rotationEnabled || IsDrawing || IsSelectingColor);
    public int CurrentTicket { get; private set; }
    public bool LastTimedOut { get; private set; }
    public LotteryBallToken ActiveBall { get; private set; }
    public MedalLotteryPocket LastPocket { get; private set; }
    public MedalColorRoutePocket LastColorPocket { get; private set; }
    public int InitialWin { get; private set; }
    public bool DirectJpcGatesUnlocked { get; private set; }
    public int RecoveryCount { get; private set; }
    public float LastRecoveryTime { get; private set; }
    public string LastRecoveryReason { get; private set; } = "";
    public int LastRecoveryWin { get; private set; }
    public int LastRecoveryTicket { get; private set; }
    public string LastRecoveryBallInstanceId { get; private set; } = "";
    public int LastRecoveryUsedOutBlockCount { get; private set; }
    public bool LastRecoveryColorGatesOpen { get; private set; }
    public float LastRecoveryHorizontalSpeed { get; private set; }
    public float UpperStagnantSeconds => upperStalledAt >= 0f ? Mathf.Max(0f, Time.time - upperStalledAt) : 0f;
    public float UpperHorizontalSpeed => ballBody != null ? Horizontal(ballBody.linearVelocity).magnitude : 0f;
    public float UpperProgressExcursion => ballBody != null ? Horizontal(ballBody.position - upperMotionSamplePosition).magnitude : 0f;

    private MedalSlotJackpotController owner;
    private Rigidbody ballBody;
    private float startedAt;
    private float upperSuspendedAt;
    private float lastUpperElapsed;
    private Collider[] suspendedColliders;
    private bool[] suspendedColliderStates;
    private Renderer[] suspendedRenderers;
    private bool[] suspendedRendererStates;
    private float[] outBlockWithdrawTimes = new float[0];
    private bool outBlockRegenerationPending;
    private float outBlockRegenerationAt = float.PositiveInfinity;
    private float outBlockRearmAt;
    private MedalSlotJackpotController eligibleColorOwner;
    private int eligibleColorTicket;
    private bool colorSelectionEligible;
    private float outBlockWithdrawAt;
    private float nextDividerSpeedChange;
    private float ballLocalRadius = .26f;
    private float upperMotionSampleAt;
    private Vector3 upperMotionSamplePosition;
    private float upperStalledAt = -1f;
    private readonly Dictionary<MedalLotteryBumper, float> nextBumperHitAt = new Dictionary<MedalLotteryBumper, float>();

    public void SetRotationEnabled(bool enabled)
    {
        if (rotationEnabled == enabled) return;
        rotationEnabled = enabled;
        if (enabled && dividerRotor != null) ChooseDividerSpeed();
        else if (!IsRotating) currentDividerSpeed = 0f;
    }

    private void ChooseDividerSpeed()
    {
        // Only the physical partition motion varies; the entered pocket decides the result.
        currentDividerSpeed = Random.Range(18f, 55f) * (Random.value < .5f ? -1f : 1f);
        nextDividerSpeedChange = Time.time + Random.Range(2f, 5f);
    }

    private void MoveDividers()
    {
        if (!IsRotating || dividerRotor == null)
        {
            currentDividerSpeed = 0f;
            return;
        }
        if (currentDividerSpeed == 0f || Time.time >= nextDividerSpeedChange) ChooseDividerSpeed();
        Quaternion next = dividerRotor.rotation * Quaternion.Euler(0f, currentDividerSpeed * Time.fixedDeltaTime, 0f);
        if (dividerBody != null) dividerBody.MoveRotation(next);
        else dividerRotor.rotation = next;
    }

    public void CancelDraw(MedalSlotJackpotController requestOwner, int ticket)
    {
        if (owner == requestOwner && CurrentTicket == ticket)
        {
            if (IsDrawing) Complete(false, usesBumpers ? BumperWin : 15, true);
            else if (IsSelectingColor) CompleteColorSelection(null, true);
        }
        if (eligibleColorOwner == requestOwner && eligibleColorTicket == ticket)
            ClearColorEligibility();
    }

    public void BeginDraw(MedalSlotJackpotController drawingOwner, int ticket, int initialWin = 0, bool unlockedAtStart = false)
    {
        if (drawingOwner == null || IsDrawing || IsSelectingColor) return;
        ClearColorEligibility();
        DirectJpcGatesUnlocked = isUpperStation && usesBumpers && unlockedAtStart;
        ColorGatesOpen = DirectJpcGatesUnlocked;
        ResetOutBlock();
        SetNormalPocketTriggers(true);
        owner = drawingOwner;
        CurrentTicket = ticket;
        LastTimedOut = false;
        LastExitedBowl = false;
        InitialWin = isUpperStation && usesBumpers ? Mathf.Max(0, initialWin) : 0;
        BumperWin = InitialWin;
        ColorGatesOpen = DirectJpcGatesUnlocked || BumperWin > 100;
        TotalBumperHits = 0;
        LastBumper = null;
        nextBumperHitAt.Clear();
        RecoveryCount = 0;
        LastRecoveryTime = 0f;
        LastRecoveryReason = "";
        LastRecoveryWin = 0;
        LastRecoveryTicket = 0;
        LastRecoveryBallInstanceId = "";
        LastRecoveryUsedOutBlockCount = 0;
        LastRecoveryColorGatesOpen = false;
        LastRecoveryHorizontalSpeed = 0f;
        upperStalledAt = -1f;
        RefreshUpperWinText();
        LastPocket = null;
        LastColorPocket = null;
        IsUpperSuspended = false;
        lastUpperElapsed = 0f;
        IsDrawing = true;
        startedAt = Time.time;
        if (!isActiveAndEnabled || launchPoint == null || lotteryBallPrefab == null)
        {
            Complete(false, usesBumpers ? BumperWin : 15, true);
            return;
        }

        if (!LaunchBall(launchPoint, localLaunchVelocity, launchVelocityJitter, launchPositionJitter, launchVelocityRelativeToPoint))
            Complete(false, usesBumpers ? BumperWin : 15, true);
    }

    public void BeginColorSelection(MedalSlotJackpotController drawingOwner, int ticket)
    {
        if (drawingOwner == null || !isUpperStation || usesBumpers || IsDrawing || IsSelectingColor
            || !colorSelectionEligible || eligibleColorOwner != drawingOwner || eligibleColorTicket != ticket) return;
        ClearColorEligibility();
        owner = drawingOwner;
        CurrentTicket = ticket;
        LastTimedOut = false;
        LastColorPocket = null;
        IsSelectingColor = true;
        LastExitedBowl = false;
        ResetOutBlock();
        ColorGatesOpen = true;
        SetNormalPocketTriggers(false);
        startedAt = Time.time;
        if (!isActiveAndEnabled || colorLaunchPoint == null || lotteryBallPrefab == null
            || !LaunchBall(colorLaunchPoint, colorLaunchVelocity, colorLaunchVelocityJitter, colorLaunchPositionJitter, false))
            CompleteColorSelection(null, true);
    }

    private bool LaunchBall(Transform point, Vector3 launchVelocity, Vector3 velocityJitter, Vector3 positionJitter, bool relativeToPoint)
    {
        var jitter = new Vector3(Random.Range(-positionJitter.x, positionJitter.x),
            Random.Range(-positionJitter.y, positionJitter.y), Random.Range(-positionJitter.z, positionJitter.z));
        // Upper draws always start at the fixed board center, including after rotor movement.
        Vector3 position = isUpperStation && !IsSelectingColor
            ? transform.TransformPoint(new Vector3(0f, 1.24f, guideLocalCenter.z))
            : point.position + transform.TransformVector(jitter);
        var ball = Instantiate(lotteryBallPrefab, position, Quaternion.identity, transform);
        ball.name = (isUpperStation ? "Upper" : kind.ToString()) + (IsSelectingColor ? "_ColorBall_" : "_LotteryBall_") + CurrentTicket;
        ActiveBall = ball.GetComponent<LotteryBallToken>();
        ballBody = ball.GetComponent<Rigidbody>();
        if (ActiveBall == null || ballBody == null)
        {
            Destroy(ball);
            return false;
        }
        ActiveBall.Initialize(this, CurrentTicket);
        var sphere = ball.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            Vector3 sphereScale = sphere.transform.lossyScale;
            Vector3 stationScale = transform.lossyScale;
            float worldRadius = sphere.radius * Mathf.Max(Mathf.Abs(sphereScale.x), Mathf.Abs(sphereScale.y), Mathf.Abs(sphereScale.z));
            float horizontalScale = Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(stationScale.x), Mathf.Abs(stationScale.z)));
            ballLocalRadius = worldRadius / horizontalScale;
        }
        ballBody.isKinematic = false;
        ballBody.useGravity = true;
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ballBody.interpolation = RigidbodyInterpolation.Interpolate;
        var velocity = launchVelocity + new Vector3(Random.Range(-velocityJitter.x, velocityJitter.x),
            Random.Range(-velocityJitter.y, velocityJitter.y), Random.Range(-velocityJitter.z, velocityJitter.z));
        ballBody.linearVelocity = relativeToPoint ? point.TransformDirection(velocity) : transform.TransformDirection(velocity);
        ballBody.angularVelocity = transform.TransformDirection(new Vector3(1.5f, .5f, -2f));
        ballBody.WakeUp();
        ResetUpperMotionTracking();
        return true;
    }

    private void FixedUpdate()
    {
        // Actual OUT must finish the ticket before any guard cycle can rearm it.
        if (TryFinishBumperOutside()) return;
        if (TryFinishOutsideBowl()) return;
        MoveColorGates();
        MoveOutBlock();
        MoveDividers();
        if (IsRotating && rotor != null)
        {
            Quaternion next = rotor.rotation * Quaternion.Euler(0, rotorDegreesPerSecond * Time.fixedDeltaTime, 0);
            if (rotorBody != null) rotorBody.MoveRotation(next);
            else rotor.rotation = next;
        }
        if ((!IsDrawing && !IsSelectingColor) || IsUpperSuspended || ballBody == null) return;
        if (isUpperStation && usesBumpers && TryRecoverStalledUpperBall()) return;
        if (ballBody.isKinematic) return;
        Vector3 guide;
        if (IsSelectingColor)
        {
            Vector3 radial = transform.InverseTransformPoint(ballBody.position) - colorGuideLocalCenter;
            radial.y = 0;
            // The outer bowl drains inward; around the convex dome the ball
            // drains outward. Neither vector refers to a color or a reward.
            guide = radial.sqrMagnitude < .81f ? radial : -radial;
            guide.y = 0;
            if (guide.sqrMagnitude < .0004f) return;
            guide.Normalize();
        }
        else if (usesBumpers)
        {
            Vector3 radial = transform.InverseTransformPoint(ballBody.position) - guideLocalCenter;
            radial.y = 0f;
            if (radial.sqrMagnitude < .0004f) radial = Vector3.right * .02f;
            // Circulate across the physical bumper deck instead of pressing a
            // ball into the central dome. No bumper or reward is targeted.
            Vector3 tangent = new Vector3(-radial.z, 0f, radial.x).normalized;
            guide = (tangent + radial.normalized * (radial.sqrMagnitude < .81f ? .30f : -.25f)).normalized;
        }
        else if (radialGuide)
        {
            Vector3 localPosition = transform.InverseTransformPoint(ballBody.position);
            guide = guideLocalCenter - localPosition;
            guide.y = 0;
            if (guide.sqrMagnitude < .25f) return;
            guide.Normalize();
        }
        else guide = guideLocalDirection.normalized;
        // This force only keeps a ball moving toward the physical play surface.
        // It does not point at, select, or test any reward pocket.
        ballBody.AddForce(transform.TransformDirection(guide) * guideAcceleration, ForceMode.Acceleration);
        if (isUpperStation && usesBumpers && IsDrawing)
        {
            float speed = Horizontal(ballBody.linearVelocity).magnitude;
            float shortfall = Mathf.Clamp01(1f - speed / Mathf.Max(.1f, upperMinimumHorizontalSpeed));
            if (shortfall > 0f)
                ballBody.AddForce(transform.TransformDirection(guide) * upperSpeedMaintenanceAcceleration * shortfall, ForceMode.Acceleration);
        }
    }

    private static Vector3 Horizontal(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private void RefreshUpperWinText()
    {
        if (isUpperStation && usesBumpers && upperWinText != null)
            upperWinText.text = $"{BumperWin:00}WIN（枚獲得）";
    }

    private void ResetUpperMotionTracking()
    {
        upperMotionSampleAt = Time.time;
        upperMotionSamplePosition = ballBody != null ? ballBody.position : transform.position;
        upperStalledAt = -1f;
    }

    private bool TryRecoverStalledUpperBall()
    {
        if (!IsDrawing || IsUpperSuspended || owner == null || ballBody == null || ActiveBall == null
            || ActiveBall.station != this || ActiveBall.ticket != CurrentTicket || ActiveBall.IsConsumed) return false;
        Vector3 localPosition = transform.InverseTransformPoint(ballBody.position);
        Vector3 radial = Horizontal(localPosition - guideLocalCenter);
        float insideRadius = Mathf.Max(.1f, bumperBowlOuterRadius + ballLocalRadius + .04f - .015f);
        // OUT is checked before recovery. Include supported outer rim/port/guard
        // contacts, while keeping a small margin inside the whole-ball OUT edge.
        // A falling sphere or one outside the deck's height band is never relaunched.
        if (localPosition.y < .30f || localPosition.y > 1.65f || radial.sqrMagnitude > insideRadius * insideRadius)
        {
            ResetUpperMotionTracking();
            return false;
        }
        // Hold the anchor for the whole window. Checking every physics step sees
        // real excursions even when a normal orbit returns to its start later.
        // Collision jitter can have high velocity without escaping this area.
        float moved = Horizontal(ballBody.position - upperMotionSamplePosition).magnitude;
        if (moved >= Mathf.Max(.01f, upperStallDisplacement))
        {
            ResetUpperMotionTracking();
            return false;
        }
        if (upperStalledAt < 0f) upperStalledAt = upperMotionSampleAt;
        if (Time.time - upperStalledAt < Mathf.Max(.1f, upperStallDuration)) return false;

        LastRecoveryHorizontalSpeed = Horizontal(ballBody.linearVelocity).magnitude;
        LastRecoveryReason = LastRecoveryHorizontalSpeed < Mathf.Max(.01f, upperStallSpeed)
            ? "stalled-on-upper-deck" : "confined-motion-on-upper-deck";
        LastRecoveryTime = Time.time;
        LastRecoveryWin = BumperWin;
        LastRecoveryTicket = CurrentTicket;
        LastRecoveryBallInstanceId = ActiveBall.GetEntityId().ToString();
        LastRecoveryUsedOutBlockCount = UsedOutBlockCount;
        LastRecoveryColorGatesOpen = ColorGatesOpen;
        RecoveryCount++;
        RelaunchUpperBallAtCenter();
        return true;
    }

    private void RelaunchUpperBallAtCenter()
    {
        // Keep the original Rigidbody, token, ticket, WIN, gates and used plates.
        ballBody.position = transform.TransformPoint(new Vector3(0f, 1.24f, guideLocalCenter.z));
        ballBody.rotation = Quaternion.identity;
        ballBody.isKinematic = false;
        ballBody.useGravity = true;
        ballBody.detectCollisions = true;
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var velocity = localLaunchVelocity + new Vector3(Random.Range(-launchVelocityJitter.x, launchVelocityJitter.x),
            Random.Range(-launchVelocityJitter.y, launchVelocityJitter.y), Random.Range(-launchVelocityJitter.z, launchVelocityJitter.z));
        // A varying heading prevents repeated recovery into the same obstruction.
        velocity = Quaternion.Euler(0f, Random.Range(-180f, 180f), 0f) * velocity;
        ballBody.linearVelocity = transform.TransformDirection(velocity);
        ballBody.angularVelocity = transform.TransformDirection(new Vector3(1.5f, .5f, -2f));
        ballBody.WakeUp();
        ResetUpperMotionTracking();
    }

    private bool TryFinishOutsideBowl()
    {
        if (!IsSelectingColor || ballBody == null || ActiveBall == null || ActiveBall.station != this
            || ActiveBall.ticket != CurrentTicket || ActiveBall.IsConsumed) return false;
        Vector3 position = transform.InverseTransformPoint(ballBody.position);
        Vector3 offset = position - colorGuideLocalCenter;
        offset.y = 0f;
        float outsideRadius = colorBowlOuterRadius + ballLocalRadius + .04f;
        // This is the physical outer boundary, including jumps over a closed
        // guard. The whole sphere must have left the bowl or fallen below it.
        if (offset.sqrMagnitude <= outsideRadius * outsideRadius && position.y + ballLocalRadius >= colorBowlFloorY)
            return false;
        if (!ActiveBall.TryConsume()) return false;
        LastExitedBowl = true;
        CompleteColorSelection(null, false);
        return true;
    }

    private bool TryFinishBumperOutside()
    {
        if (!IsDrawing || IsUpperSuspended || !usesBumpers || ballBody == null || ActiveBall == null || ActiveBall.station != this
            || ActiveBall.ticket != CurrentTicket || ActiveBall.IsConsumed) return false;
        Vector3 position = transform.InverseTransformPoint(ballBody.position);
        Vector3 offset = position - guideLocalCenter;
        offset.y = 0f;
        float outsideRadius = bumperBowlOuterRadius + ballLocalRadius + .04f;
        if (offset.sqrMagnitude <= outsideRadius * outsideRadius && position.y + ballLocalRadius >= bumperBowlFloorY)
            return false;
        if (!ActiveBall.TryConsume()) return false;
        LastExitedBowl = true;
        Complete(false, BumperWin, false);
        return true;
    }

    public bool TryHitBumper(MedalLotteryBumper bumper, LotteryBallToken token)
        => TryHitBumper(bumper, token, Vector3.zero);

    public bool TryHitBumper(MedalLotteryBumper bumper, LotteryBallToken token, Vector3 contactNormal)
    {
        if (!IsDrawing || IsUpperSuspended || !usesBumpers || bumper == null || bumper.station != this || token == null
            || token.station != this || token.ticket != CurrentTicket || token != ActiveBall || token.IsConsumed)
            return false;
        bool registered = false;
        if (bumpers != null)
            foreach (var candidate in bumpers)
                if (candidate == bumper) { registered = true; break; }
        if (!registered || (nextBumperHitAt.TryGetValue(bumper, out float nextHit) && Time.time < nextHit)) return false;
        nextBumperHitAt[bumper] = Time.time + .08f;
        TotalBumperHits++;
        BumperWin += 2;
        RefreshUpperWinText();
        LastBumper = bumper;
        // The same upper ball can enter a real color port once its live WIN exceeds 100.
        if (BumperWin > 100) ColorGatesOpen = true;
        if (ballBody != null)
        {
            Vector3 away = ballBody.position - bumper.transform.position;
            away.y = 0f;
            contactNormal.y = 0f;
            if (contactNormal.sqrMagnitude < .0001f) contactNormal = away;
            if (Vector3.Dot(contactNormal, away) < 0f) contactNormal = -contactNormal;
            if (contactNormal.sqrMagnitude > .0001f)
            {
                contactNormal.Normalize();
                Vector3 velocity = ballBody.linearVelocity;
                float incidentSpeed = Vector3.Dot(velocity, contactNormal);
                velocity += contactNormal * (Mathf.Max(3.8f, Mathf.Abs(incidentSpeed)) - incidentSpeed);
                velocity.y = Mathf.Max(velocity.y, .25f);
                ballBody.linearVelocity = velocity;
                ballBody.WakeUp();
            }
        }
        if (owner != null) owner.NotifyUpperBumperHit(this, CurrentTicket, BumperWin);
        return true;
    }

    private void Update()
    {
        if (IsDrawing && !IsUpperSuspended && isUpperStation && usesBumpers
            && (ActiveBall == null || ballBody == null))
            Complete(false, BumperWin, true);
        else if (IsDrawing && !IsUpperSuspended && !(isUpperStation && usesBumpers)
            && UpperElapsedSeconds >= Mathf.Max(.1f, drawTimeout))
            Complete(false, usesBumpers ? BumperWin : 15, true);
        else if (IsSelectingColor && Time.time - startedAt >= Mathf.Max(.1f, colorSelectionTimeout))
            CompleteColorSelection(null, true);
    }

    public bool TryResolve(MedalLotteryPocket pocket, LotteryBallToken token)
    {
        if (!IsDrawing || IsUpperSuspended || pocket == null || token == null || pocket.station != this
            || token.station != this || token.ticket != CurrentTicket || token != ActiveBall
            || !token.TryConsume()) return false;
        LastPocket = pocket;
        Complete(usesBumpers ? false : pocket.jackpot, usesBumpers ? BumperWin : Mathf.Max(0, pocket.smallReward), false);
        return true;
    }

    public bool TryResolveColor(MedalColorRoutePocket pocket, LotteryBallToken token)
    {
        if (!ColorGatesOpen || pocket == null || token == null || pocket.station != this
            || token.station != this || token.ticket != CurrentTicket || token != ActiveBall
            || token.IsConsumed || (int)pocket.kind < 0 || (int)pocket.kind > 2) return false;
        bool registered = false;
        if (colorRoutePockets != null)
            foreach (var candidate in colorRoutePockets)
                if (candidate == pocket) { registered = true; break; }
        if (!registered) return false;
        if (usesBumpers && isUpperStation)
        {
            if (!IsDrawing || IsUpperSuspended || (BumperWin <= 100 && !DirectJpcGatesUnlocked) || owner == null || ballBody == null) return false;
            LastColorPocket = pocket;
            var callbackOwner = owner;
            int routedTicket = CurrentTicket;
            SuspendUpperBall();
            if (!callbackOwner.NotifyUpperColorRoute(this, routedTicket, pocket.kind))
            {
                if (IsDrawing && IsUpperSuspended && owner == callbackOwner && CurrentTicket == routedTicket
                    && !ResumeUpperDraw(callbackOwner, routedTicket)) CancelDraw(callbackOwner, routedTicket);
                return false;
            }
            return true;
        }
        if (!IsSelectingColor || !token.TryConsume()) return false;
        LastColorPocket = pocket;
        CompleteColorSelection(pocket.kind, false);
        return true;
    }

    private void SuspendUpperBall()
    {
        IsUpperSuspended = true;
        upperStalledAt = -1f;
        upperSuspendedAt = Time.time;
        ballBody.linearVelocity = Vector3.zero;
        ballBody.angularVelocity = Vector3.zero;
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        ballBody.isKinematic = true;
        ballBody.detectCollisions = false;
        suspendedColliders = ActiveBall.GetComponentsInChildren<Collider>();
        suspendedColliderStates = new bool[suspendedColliders.Length];
        for (int i = 0; i < suspendedColliders.Length; i++)
        {
            suspendedColliderStates[i] = suspendedColliders[i].enabled;
            suspendedColliders[i].enabled = false;
        }
        suspendedRenderers = ActiveBall.GetComponentsInChildren<Renderer>();
        suspendedRendererStates = new bool[suspendedRenderers.Length];
        for (int i = 0; i < suspendedRenderers.Length; i++)
        {
            suspendedRendererStates[i] = suspendedRenderers[i].enabled;
            suspendedRenderers[i].enabled = false;
        }
    }

    public bool ResumeUpperDraw(MedalSlotJackpotController requestOwner, int ticket)
    {
        if (!isActiveAndEnabled || !usesBumpers || !IsDrawing || !IsUpperSuspended || owner != requestOwner
            || CurrentTicket != ticket || ActiveBall == null || ActiveBall.station != this
            || ActiveBall.ticket != ticket || ActiveBall.IsConsumed || ballBody == null || launchPoint == null) return false;
        // Restore the original token at the inside launch spout. No new draw,
        // score reset, guard reset or reward choice occurs on the return.
        float pausedDuration = Mathf.Max(0f, Time.time - upperSuspendedAt);
        startedAt += pausedDuration;
        ShiftOutBlockTimersForPause(pausedDuration);
        ballBody.position = isUpperStation
            ? transform.TransformPoint(new Vector3(0f, 1.24f, guideLocalCenter.z)) : launchPoint.position;
        ballBody.rotation = Quaternion.identity;
        ballBody.isKinematic = false;
        ballBody.useGravity = true;
        ballBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (suspendedColliders != null)
            for (int i = 0; i < suspendedColliders.Length; i++)
                if (suspendedColliders[i] != null) suspendedColliders[i].enabled = suspendedColliderStates[i];
        if (suspendedRenderers != null)
            for (int i = 0; i < suspendedRenderers.Length; i++)
                if (suspendedRenderers[i] != null) suspendedRenderers[i].enabled = suspendedRendererStates[i];
        ballBody.detectCollisions = true;
        var velocity = localLaunchVelocity + new Vector3(Random.Range(-launchVelocityJitter.x, launchVelocityJitter.x),
            Random.Range(-launchVelocityJitter.y, launchVelocityJitter.y), Random.Range(-launchVelocityJitter.z, launchVelocityJitter.z));
        ballBody.linearVelocity = launchVelocityRelativeToPoint ? launchPoint.TransformDirection(velocity) : transform.TransformDirection(velocity);
        ballBody.angularVelocity = transform.TransformDirection(new Vector3(1.5f, .5f, -2f));
        IsUpperSuspended = false;
        ballBody.WakeUp();
        ResetUpperMotionTracking();
        return true;
    }

    public bool IsOutBlockUsed(int index)
        => index >= 0 && index < OutBlockUsedStates.Length && OutBlockUsedStates[index];

    public bool TryBlockOutflow(MedalOutBlock block, LotteryBallToken token)
    {
        if (block == null || !IsRegisteredOutBlock(block) || block.station != this
            || token == null || token.station != this || token.ticket != CurrentTicket
            || token != ActiveBall || token.IsConsumed) return false;
        bool independent = usesBumpers && isUpperStation;
        int blockIndex = FindOutBlockIndex(block);
        if (independent)
        {
            if (!IsDrawing || IsUpperSuspended || outBlockRegenerationPending || IsOutBlockRegenerating
                || Time.time < outBlockRearmAt || blockIndex < 0 || blockIndex >= OutBlockUsedStates.Length
                || IsOutBlockUsed(blockIndex)) return false;
            OutBlockUsedStates[blockIndex] = true;
            outBlockWithdrawTimes[blockIndex] = Time.time + Mathf.Max(0f, outBlockRetractionDelay);
            UsedOutBlockCount++;
            if (regenerateOutBlocks && UsedOutBlockCount == OutBlockUsedStates.Length && CanRegenerateOutBlocks())
            {
                outBlockRegenerationPending = true;
                outBlockRegenerationAt = float.PositiveInfinity;
            }
        }
        else if (!IsSelectingColor || OutBlockUsed) return false;
        OutBlockUsed = true;
        outBlockWithdrawAt = Time.time + Mathf.Max(0f, outBlockRetractionDelay);
        if (outBlockText != null) outBlockText.text = "ガード\n0";
        if (ballBody != null)
        {
            // A rebound from the contacted physical plate, keeping this same ball.
            Vector3 velocity = transform.InverseTransformDirection(ballBody.linearVelocity);
            if (independent || block == outBlock) velocity.z = Mathf.Max(velocity.z, 3.2f);
            else
            {
                Vector3 inward = colorGuideLocalCenter - transform.InverseTransformPoint(block.transform.position);
                inward.y = 0f;
                inward.Normalize();
                float incidentSpeed = Vector3.Dot(velocity, inward);
                velocity += inward * (Mathf.Max(3.2f, Mathf.Abs(incidentSpeed)) - incidentSpeed);
            }
            velocity.y = Mathf.Max(velocity.y, .70f);
            ballBody.linearVelocity = transform.TransformDirection(velocity);
            ballBody.WakeUp();
        }
        if (owner != null) owner.NotifyOutBlockConsumed(this, CurrentTicket);
        return true;
    }

    public bool TryResolveOutflow(MedalLotteryOutflow exit, LotteryBallToken token)
    {
        if (IsDrawing && !IsUpperSuspended && usesBumpers && exit != null && exit == upperOutflow && exit.station == this
            && token != null && token.station == this && token.ticket == CurrentTicket && token == ActiveBall
            && token.TryConsume())
        {
            LastExitedBowl = true;
            Complete(false, BumperWin, false);
            return true;
        }
        if (!IsSelectingColor || !OutBlockUsed || exit == null || !IsRegisteredOutflow(exit) || exit.station != this
            || token == null || token.station != this || token.ticket != CurrentTicket
            || token != ActiveBall || !token.TryConsume()) return false;
        LastExitedBowl = true;
        CompleteColorSelection(null, false);
        return true;
    }

    private bool IsRegisteredOutBlock(MedalOutBlock block)
    {
        if (block == outBlock) return true;
        if (outBlocks != null)
            foreach (var candidate in outBlocks)
                if (candidate == block) return true;
        return false;
    }

    private int FindOutBlockIndex(MedalOutBlock block)
    {
        if (outBlocks == null) return -1;
        for (int i = 0; i < outBlocks.Length; i++)
            if (outBlocks[i] == block) return i;
        return -1;
    }

    private bool IsRegisteredOutflow(MedalLotteryOutflow exit)
    {
        if (exit == outflow) return true;
        if (outflows != null)
            foreach (var candidate in outflows)
                if (candidate == exit) return true;
        return false;
    }

    private void Complete(bool jackpot, int smallReward, bool timedOut)
    {
        if (!IsDrawing) return;
        var callbackOwner = owner;
        int completedTicket = CurrentTicket;
        var completedBall = ActiveBall;
        lastUpperElapsed = UpperElapsedSeconds;
        StopOutBlockRegeneration();
        IsDrawing = false;
        IsUpperSuspended = false;
        LastTimedOut = timedOut;
        ClearColorEligibility();
        if (isUpperStation && !usesBumpers && !timedOut && smallReward > 100)
        {
            eligibleColorOwner = callbackOwner;
            eligibleColorTicket = completedTicket;
            colorSelectionEligible = true;
        }
        else ColorGatesOpen = false;
        owner = null;
        CleanupBall(completedBall, usesBumpers && LastExitedBowl && !timedOut ? .65f : 0f);
        // Clear state first: the owner may synchronously begin another draw.
        if (callbackOwner != null)
            callbackOwner.FinishLottery(this, completedTicket, jackpot, smallReward);
    }

    private void CompleteColorSelection(MedalJackpotKind? selectedKind, bool timedOut)
    {
        if (!IsSelectingColor) return;
        var callbackOwner = owner;
        int completedTicket = CurrentTicket;
        var completedBall = ActiveBall;
        IsSelectingColor = false;
        ColorGatesOpen = false;
        LastTimedOut = timedOut;
        owner = null;
        ClearColorEligibility();
        SetNormalPocketTriggers(true);
        CleanupBall(completedBall, selectedKind == null && !timedOut ? .65f : 0f);
        if (callbackOwner != null)
            callbackOwner.FinishColorSelection(this, completedTicket, selectedKind, timedOut);
    }

    private void CleanupBall(LotteryBallToken completedBall, float displayDelay = 0f)
    {
        ActiveBall = null;
        ballBody = null;
        suspendedColliders = null;
        suspendedColliderStates = null;
        suspendedRenderers = null;
        suspendedRendererStates = null;
        if (completedBall != null)
        {
            completedBall.TryConsume();
            foreach (var collider in completedBall.GetComponentsInChildren<Collider>()) collider.enabled = false;
            Destroy(completedBall.gameObject, displayDelay);
        }
    }

    private void ClearColorEligibility()
    {
        colorSelectionEligible = false;
        eligibleColorOwner = null;
        eligibleColorTicket = 0;
    }

    private void SetNormalPocketTriggers(bool enabled)
    {
        if (normalWinPocketTriggers == null) return;
        foreach (var collider in normalWinPocketTriggers)
            if (collider != null) collider.enabled = enabled;
    }

    private void MoveColorGates()
    {
        if (colorGateBodies == null || colorGateClosedPositions == null) return;
        for (int i = 0; i < Mathf.Min(colorGateBodies.Length, colorGateClosedPositions.Length); i++)
        {
            var gate = colorGateBodies[i];
            if (gate == null || gate.transform.parent == null) continue;
            Vector3 localTarget = colorGateClosedPositions[i] + Vector3.up * (ColorGatesOpen ? colorGateRaiseHeight : 0f);
            Vector3 worldTarget = gate.transform.parent.TransformPoint(localTarget);
            gate.MovePosition(Vector3.MoveTowards(gate.position, worldTarget, Mathf.Max(.01f, colorGateMoveSpeed) * Time.fixedDeltaTime));
        }
    }

    private void ResetOutBlock()
    {
        StopOutBlockRegeneration();
        OutBlockRegenerationCount = 0;
        LastOutBlockRegenerationWin = 0;
        LastOutBlockRegenerationTicket = 0;
        LastOutBlockRegenerationBallId = "";
        LastOutBlockRegenerationColorGatesOpen = false;
        OutBlockUsed = false;
        UsedOutBlockCount = 0;
        int count = outBlocks != null ? outBlocks.Length : 0;
        OutBlockUsedStates = new bool[count];
        outBlockWithdrawTimes = new float[count];
        for (int i = 0; i < count; i++) outBlockWithdrawTimes[i] = float.PositiveInfinity;
        outBlockWithdrawAt = float.PositiveInfinity;
        if (outBlockText != null) outBlockText.text = "ガード\n1";
    }

    private void MoveOutBlock()
    {
        if (usesBumpers && isUpperStation)
        {
            if (IsUpperSuspended) return;
            AdvanceOutBlockRegeneration();
        }
        bool retract = IsSelectingColor && OutBlockUsed && Time.time >= outBlockWithdrawAt;
        if (outBlockBodies != null && outBlockClosedPositions != null)
        {
            for (int i = 0; i < Mathf.Min(outBlockBodies.Length, outBlockClosedPositions.Length); i++)
            {
                bool lowerIndividual = usesBumpers && isUpperStation
                    ? !IsOutBlockRegenerating && IsOutBlockUsed(i) && i < outBlockWithdrawTimes.Length
                        && Time.time >= outBlockWithdrawTimes[i]
                    : retract;
                MoveOutBlockBody(outBlockBodies[i], outBlockClosedPositions[i], lowerIndividual);
            }
        }
        else MoveOutBlockBody(outBlockBody, outBlockClosedPosition, retract);
    }

    private bool CanRegenerateOutBlocks()
    {
        int count = OutBlockUsedStates.Length;
        if (!usesBumpers || !isUpperStation || count == 0 || outBlockBodies == null || outBlockClosedPositions == null
            || outBlockBodies.Length < count || outBlockClosedPositions.Length < count) return false;
        for (int i = 0; i < count; i++)
            if (outBlockBodies[i] == null || outBlockBodies[i].transform.parent == null) return false;
        return true;
    }

    private bool OutBlocksAtTarget(bool withdrawn)
    {
        if (!CanRegenerateOutBlocks()) return false;
        for (int i = 0; i < OutBlockUsedStates.Length; i++)
        {
            var body = outBlockBodies[i];
            Vector3 local = outBlockClosedPositions[i] + Vector3.up * (withdrawn ? outBlockRaiseHeight : 0f);
            Vector3 target = body.transform.parent.TransformPoint(local);
            if ((body.position - target).sqrMagnitude > .0004f) return false;
        }
        return true;
    }

    private void AdvanceOutBlockRegeneration()
    {
        if (!outBlockRegenerationPending && !IsOutBlockRegenerating) return;
        if (!regenerateOutBlocks || !IsDrawing || owner == null || ActiveBall == null || ballBody == null
            || ActiveBall.station != this || ActiveBall.ticket != CurrentTicket || ActiveBall.IsConsumed
            || !CanRegenerateOutBlocks())
        {
            StopOutBlockRegeneration();
            return;
        }
        if (IsUpperSuspended) return;
        if (IsOutBlockRegenerating)
        {
            if (!OutBlocksAtTarget(false)) return;
            // All physical plates have returned before their flags are armed again.
            LastOutBlockRegenerationWin = BumperWin;
            LastOutBlockRegenerationTicket = CurrentTicket;
            LastOutBlockRegenerationBallId = ActiveBall.GetEntityId().ToString();
            LastOutBlockRegenerationColorGatesOpen = ColorGatesOpen;
            OutBlockRegenerationCount++;
            OutBlockUsed = false;
            UsedOutBlockCount = 0;
            for (int i = 0; i < OutBlockUsedStates.Length; i++)
            {
                OutBlockUsedStates[i] = false;
                outBlockWithdrawTimes[i] = float.PositiveInfinity;
            }
            outBlockWithdrawAt = float.PositiveInfinity;
            outBlockRegenerationPending = false;
            IsOutBlockRegenerating = false;
            outBlockRegenerationAt = float.PositiveInfinity;
            // Restoring a plate can itself produce a new contact with the old ball.
            outBlockRearmAt = Time.time + Mathf.Max(0f, outBlockRegenerationCooldown);
            return;
        }
        if (!OutBlocksAtTarget(true)) return;
        if (float.IsPositiveInfinity(outBlockRegenerationAt))
            outBlockRegenerationAt = Time.time + Mathf.Max(0f, outBlockRegenerationDelay);
        if (Time.time >= outBlockRegenerationAt) IsOutBlockRegenerating = true;
    }

    private void ShiftOutBlockTimersForPause(float duration)
    {
        for (int i = 0; i < outBlockWithdrawTimes.Length; i++)
            if (!float.IsInfinity(outBlockWithdrawTimes[i])) outBlockWithdrawTimes[i] += duration;
        if (!float.IsInfinity(outBlockWithdrawAt)) outBlockWithdrawAt += duration;
        if (!float.IsInfinity(outBlockRegenerationAt)) outBlockRegenerationAt += duration;
        if (outBlockRearmAt > 0f) outBlockRearmAt += duration;
    }

    private void StopOutBlockRegeneration()
    {
        outBlockRegenerationPending = false;
        IsOutBlockRegenerating = false;
        outBlockRegenerationAt = float.PositiveInfinity;
        outBlockRearmAt = 0f;
    }

    private void MoveOutBlockBody(Rigidbody body, Vector3 closedPosition, bool retract)
    {
        if (body == null || body.transform.parent == null) return;
        Vector3 localTarget = closedPosition + Vector3.up * (retract ? outBlockRaiseHeight : 0f);
        Vector3 target = body.transform.parent.TransformPoint(localTarget);
        body.MovePosition(Vector3.MoveTowards(body.position, target, Mathf.Max(.01f, colorGateMoveSpeed) * Time.fixedDeltaTime));
    }

    private void OnDisable()
    {
        StopOutBlockRegeneration();
        rotationEnabled = false;
        currentDividerSpeed = 0f;
        ClearColorEligibility();
        if (Application.isPlaying && IsDrawing) Complete(false, usesBumpers ? BumperWin : 15, true);
        else if (Application.isPlaying && IsSelectingColor) CompleteColorSelection(null, true);
        ColorGatesOpen = false;
        SetNormalPocketTriggers(true);
        if (colorGateBodies != null && colorGateClosedPositions != null)
        {
            for (int i = 0; i < Mathf.Min(colorGateBodies.Length, colorGateClosedPositions.Length); i++)
            {
                var gate = colorGateBodies[i];
                if (gate != null && gate.transform.parent != null)
                    gate.position = gate.transform.parent.TransformPoint(colorGateClosedPositions[i]);
            }
        }
        if (outBlockBodies != null && outBlockClosedPositions != null)
        {
            for (int i = 0; i < Mathf.Min(outBlockBodies.Length, outBlockClosedPositions.Length); i++)
            {
                var body = outBlockBodies[i];
                if (body != null && body.transform.parent != null)
                    body.position = body.transform.parent.TransformPoint(outBlockClosedPositions[i]);
            }
        }
        else if (outBlockBody != null && outBlockBody.transform.parent != null)
            outBlockBody.position = outBlockBody.transform.parent.TransformPoint(outBlockClosedPosition);
    }
}
