using UnityEngine;

/// <summary>The physical front exit after the one-use white guard has withdrawn.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class MedalLotteryOutflow : MonoBehaviour
{
    public MedalBallLotteryStation station;

    private void OnTriggerEnter(Collider other) => CheckBall(other);
    private void OnTriggerStay(Collider other) => CheckBall(other);

    private void CheckBall(Collider other)
    {
        if (station == null || other == null || other.attachedRigidbody == null) return;
        var token = other.attachedRigidbody.GetComponent<LotteryBallToken>();
        if (token != null) station.TryResolveOutflow(this, token);
    }
}
