using UnityEngine;

/// <summary>Identifies board items without requiring custom tags.</summary>
[RequireComponent(typeof(Rigidbody))]
public class MedalItem : MonoBehaviour
{
    public bool isPrize;
    public bool isBall;
    public MedalJackpotKind ballKind;
    public int pointValue = 100;
    public string displayName;
    public bool collected;
    public bool payoutAlreadyCredited;
    private MedalPusherGame game;
    private float nextImpactTime;

    void Start()
    {
        if (game == null) game = GetComponentInParent<MedalPusherGame>();
        if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
        if (game != null) game.RegisterItem(this);
    }

    public void BindGame(MedalPusherGame owner)
    {
        game = owner;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collected || Time.time < nextImpactTime || collision.relativeVelocity.sqrMagnitude < 0.16f) return;
        if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
        nextImpactTime = Time.time + 0.18f;
        if (game != null) game.PlayMedalImpact(Mathf.Clamp(collision.relativeVelocity.magnitude / 6f, 0.08f, 0.7f));
    }

    void OnDestroy()
    {
        if (game != null) game.UnregisterItem(this);
    }
}
