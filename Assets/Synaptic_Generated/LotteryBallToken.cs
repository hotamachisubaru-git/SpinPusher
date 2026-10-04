using UnityEngine;

/// <summary>Identifies one physical lottery ball; it is never a playfield medal.</summary>
[DisallowMultipleComponent]
public sealed class LotteryBallToken : MonoBehaviour
{
    public MedalBallLotteryStation station;
    public int ticket;
    public bool IsConsumed { get; private set; }

    public void Initialize(MedalBallLotteryStation drawingStation, int drawTicket)
    {
        station = drawingStation;
        ticket = drawTicket;
        IsConsumed = false;
    }

    public bool TryConsume()
    {
        if (IsConsumed) return false;
        IsConsumed = true;
        return true;
    }
}
