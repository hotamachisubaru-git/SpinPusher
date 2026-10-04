using UnityEngine;

/// <summary>Routes collection trigger contacts to the shared reward manager.</summary>
[RequireComponent(typeof(Collider))]
public class MedalDropDetector : MonoBehaviour
{
    public float dropThreshold = -2f;
    public MedalPusherGame game;
    private PrizeDropManager prizeManager;

    void Start()
    {
        if (game == null) game = FindFirstObjectByType<MedalPusherGame>();
        if (game != null) prizeManager = game.GetComponent<PrizeDropManager>();
        Collider collector = GetComponent<Collider>();
        if (collector != null) collector.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (prizeManager == null || other == null) return;
        MedalItem item = other.GetComponentInParent<MedalItem>();
        if (item == null || item.collected) return;
        if (item.isPrize) prizeManager.CheckPrizeDrop(item.gameObject, item.transform.position);
        else prizeManager.CheckMedalDrop(item.gameObject, item.transform.position);
    }
}
