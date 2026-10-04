using UnityEngine;

/// <summary>A white solid plate that rebounds the same lottery ball once.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class MedalOutBlock : MonoBehaviour
{
    public MedalBallLotteryStation station;

    private void OnCollisionEnter(Collision collision)
    {
        if (station == null || collision.rigidbody == null) return;
        var token = collision.rigidbody.GetComponent<LotteryBallToken>();
        if (token != null) station.TryBlockOutflow(this, token);
    }
}
