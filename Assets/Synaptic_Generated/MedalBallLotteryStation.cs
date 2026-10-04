using UnityEngine;
using TMPro;

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
    public TMP_Text jackpotPocketText;
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
    public bool IsSelectingColor { get; private set; }
    public bool ColorGatesOpen { get; private set; }
    public bool OutBlockUsed { get; private set; }
    public bool LastExitedBowl { get; private set; }
    public bool OutBlockReady => IsSelectingColor && !OutBlockUsed;
    public bool IsRotating => rotationEnabled || IsDrawing || IsSelectingColor;
    public int CurrentTicket { get; private set; }
    public bool LastTimedOut { get; private set; }
    public LotteryBallToken ActiveBall { get; private set; }
    public MedalLotteryPocket LastPocket { get; private set; }
    public MedalColorRoutePocket LastColorPocket { get; private set; }

    private MedalSlotJackpotController owner;
    private Rigidbody ballBody;
    private float startedAt;
    private MedalSlotJackpotController eligibleColorOwner;
    private int eligibleColorTicket;
    private bool colorSelectionEligible;
    private float outBlockWithdrawAt;
    private float nextDividerSpeedChange;
    private float ballLocalRadius = .26f;

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
            if (IsDrawing) Complete(false, 15, true);
            else if (IsSelectingColor) CompleteColorSelection(null, true);
        }
        if (eligibleColorOwner == requestOwner && eligibleColorTicket == ticket)
            ClearColorEligibility();
    }

    public void BeginDraw(MedalSlotJackpotController drawingOwner, int ticket)
    {
        if (drawingOwner == null || IsDrawing || IsSelectingColor) return;
        ClearColorEligibility();
        ColorGatesOpen = false;
        ResetOutBlock();
        SetNormalPocketTriggers(true);
        owner = drawingOwner;
        CurrentTicket = ticket;
        LastTimedOut = false;
        LastExitedBowl = false;
        LastPocket = null;
        LastColorPocket = null;
        IsDrawing = true;
        startedAt = Time.time;
        if (!isActiveAndEnabled || launchPoint == null || lotteryBallPrefab == null)
        {
            Complete(false, 15, true);
            return;
        }

        if (!LaunchBall(launchPoint, localLaunchVelocity, launchVelocityJitter, launchPositionJitter, launchVelocityRelativeToPoint))
            Complete(false, 15, true);
    }

    public void BeginColorSelection(MedalSlotJackpotController drawingOwner, int ticket)
    {
        if (drawingOwner == null || !isUpperStation || IsDrawing || IsSelectingColor
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
        var ball = Instantiate(lotteryBallPrefab, point.position + transform.TransformVector(jitter), Quaternion.identity, transform);
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
        return true;
    }

    private void FixedUpdate()
    {
        MoveColorGates();
        MoveOutBlock();
        MoveDividers();
        if (TryFinishOutsideBowl()) return;
        if (IsRotating && rotor != null)
        {
            Quaternion next = rotor.rotation * Quaternion.Euler(0, rotorDegreesPerSecond * Time.fixedDeltaTime, 0);
            if (rotorBody != null) rotorBody.MoveRotation(next);
            else rotor.rotation = next;
        }
        if ((!IsDrawing && !IsSelectingColor) || ballBody == null) return;
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

    private void Update()
    {
        if (IsDrawing && Time.time - startedAt >= Mathf.Max(.1f, drawTimeout))
            Complete(false, 15, true);
        else if (IsSelectingColor && Time.time - startedAt >= Mathf.Max(.1f, colorSelectionTimeout))
            CompleteColorSelection(null, true);
    }

    public bool TryResolve(MedalLotteryPocket pocket, LotteryBallToken token)
    {
        if (!IsDrawing || pocket == null || token == null || pocket.station != this
            || token.station != this || token.ticket != CurrentTicket || token != ActiveBall
            || !token.TryConsume()) return false;
        LastPocket = pocket;
        Complete(pocket.jackpot, Mathf.Max(0, pocket.smallReward), false);
        return true;
    }

    public bool TryResolveColor(MedalColorRoutePocket pocket, LotteryBallToken token)
    {
        if (!IsSelectingColor || !ColorGatesOpen || pocket == null || token == null || pocket.station != this
            || token.station != this || token.ticket != CurrentTicket || token != ActiveBall
            || (int)pocket.kind < 0 || (int)pocket.kind > 2
            || !token.TryConsume()) return false;
        LastColorPocket = pocket;
        CompleteColorSelection(pocket.kind, false);
        return true;
    }

    public bool TryBlockOutflow(MedalOutBlock block, LotteryBallToken token)
    {
        if (!IsSelectingColor || OutBlockUsed || block == null || !IsRegisteredOutBlock(block) || block.station != this
            || token == null || token.station != this || token.ticket != CurrentTicket
            || token != ActiveBall || token.IsConsumed) return false;
        OutBlockUsed = true;
        outBlockWithdrawAt = Time.time + Mathf.Max(0f, outBlockRetractionDelay);
        if (outBlockText != null) outBlockText.text = "ガード\n0";
        if (ballBody != null)
        {
            // A rebound from the contacted physical plate, keeping this same ball.
            Vector3 velocity = transform.InverseTransformDirection(ballBody.linearVelocity);
            if (block == outBlock) velocity.z = Mathf.Max(velocity.z, 3.2f);
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
        IsDrawing = false;
        LastTimedOut = timedOut;
        ClearColorEligibility();
        if (isUpperStation && !timedOut && smallReward > 100)
        {
            eligibleColorOwner = callbackOwner;
            eligibleColorTicket = completedTicket;
            colorSelectionEligible = true;
        }
        owner = null;
        CleanupBall(completedBall);
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
        OutBlockUsed = false;
        outBlockWithdrawAt = float.PositiveInfinity;
        if (outBlockText != null) outBlockText.text = "ガード\n1";
    }

    private void MoveOutBlock()
    {
        bool retract = IsSelectingColor && OutBlockUsed && Time.time >= outBlockWithdrawAt;
        if (outBlockBodies != null && outBlockClosedPositions != null)
        {
            for (int i = 0; i < Mathf.Min(outBlockBodies.Length, outBlockClosedPositions.Length); i++)
                MoveOutBlockBody(outBlockBodies[i], outBlockClosedPositions[i], retract);
        }
        else MoveOutBlockBody(outBlockBody, outBlockClosedPosition, retract);
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
        rotationEnabled = false;
        currentDividerSpeed = 0f;
        ClearColorEligibility();
        if (Application.isPlaying && IsDrawing) Complete(false, 15, true);
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
