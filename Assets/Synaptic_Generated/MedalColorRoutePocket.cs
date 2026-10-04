using UnityEngine;

/// <summary>Routes the actual green selector ball to its visible colored lottery.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class MedalColorRoutePocket : MonoBehaviour
{
    public MedalBallLotteryStation station;
    public MedalJackpotKind kind;
    public Rigidbody gateBody;

    private void OnTriggerEnter(Collider other) => CheckBall(other);
    private void OnTriggerStay(Collider other) => CheckBall(other);

    private void CheckBall(Collider other)
    {
        if (station == null || other == null || other.attachedRigidbody == null) return;
        var token = other.attachedRigidbody.GetComponent<LotteryBallToken>();
        if (token != null) station.TryResolveColor(this, token);
    }
}
