using UnityEngine;

/// <summary>Side drains remove board items without awarding a front-collection reward.</summary>
[RequireComponent(typeof(Collider))]
public sealed class MedalSideHole : MonoBehaviour
{
    public MedalPusherGame game;
    void OnTriggerEnter(Collider other)
    {
        if (game == null || other == null) return;
        game.LoseToSideHole(other.GetComponentInParent<MedalItem>());
    }
}
