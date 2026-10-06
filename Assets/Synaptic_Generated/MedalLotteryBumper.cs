using UnityEngine;

/// <summary>A physical round bumper that awards two WIN on a new ball contact.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
public sealed class MedalLotteryBumper : MonoBehaviour
{
    public MedalBallLotteryStation station;
    private LotteryBallToken touchingBall;

    private void OnCollisionEnter(Collision collision)
    {
        if (station == null || collision.rigidbody == null) return;
        var token = collision.rigidbody.GetComponent<LotteryBallToken>();
        if (token == null || token == touchingBall) return;
        touchingBall = token;
        Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.zero;
        station.TryHitBumper(this, token, normal);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.rigidbody == null) return;
        var token = collision.rigidbody.GetComponent<LotteryBallToken>();
        if (token == touchingBall) touchingBall = null;
    }

    private void OnDisable() => touchingBall = null;
}
