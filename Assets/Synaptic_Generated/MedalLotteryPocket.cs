using UnityEngine;

/// <summary>A real trigger pocket. Its reward is fixed by its visible geometry.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class MedalLotteryPocket : MonoBehaviour
{
    public MedalBallLotteryStation station;
    public bool jackpot;
    [Min(0)] public int smallReward = 20;

    private void OnTriggerEnter(Collider other) => CheckBall(other);
    private void OnTriggerStay(Collider other) => CheckBall(other);

    private void CheckBall(Collider other)
    {
        if (station == null || other == null || other.attachedRigidbody == null) return;
        var token = other.attachedRigidbody.GetComponent<LotteryBallToken>();
        if (token != null) station.TryResolve(this, token);
    }
}
