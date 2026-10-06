using UnityEngine;

/// <summary>Consumes each physical medal once and starts one slot spin.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class MedalSlotPocket : MonoBehaviour
{
    public MedalSlotJackpotController controller;
    public bool requireEntryFromAbove;
    public float minimumEntryHeightOffset = .12f;
    public float clickOpeningRadius = .40f;

    void OnTriggerEnter(Collider other) { TryAccept(other); }

    public bool TryAccept(Collider other)
    {
        if (!isActiveAndEnabled || controller == null || !controller.isActiveAndEnabled || other == null) return false;
        var item = other.GetComponentInParent<MedalItem>();
        if (item == null || item.collected || item.isPrize || item.isBall || controller.game == null ||
            !item.transform.IsChildOf(controller.game.itemsRoot)) return false;
        if (requireEntryFromAbove)
        {
            // A moving hole must not sweep up coins resting on the lower playfield.
            var body = other.attachedRigidbody;
            var detector = GetComponent<SphereCollider>();
            if (body == null || body.isKinematic || detector == null) return false;
            Vector3 up = controller.game.transform.up;
            Vector3 centre = detector.transform.TransformPoint(detector.center);
            if (Vector3.Dot(body.position - centre, up) < minimumEntryHeightOffset) return false;
            Vector3 plateVelocity = controller.game.pusherBody == null ? Vector3.zero : controller.game.pusherBody.linearVelocity;
            if (Vector3.Dot(body.linearVelocity - plateVelocity, up) >= 0f) return false;
        }
        item.collected = true;
        controller.game.UnregisterItem(item);
        foreach (var collider in item.GetComponentsInChildren<Collider>()) collider.enabled = false;
        controller.NotifySlotPocketEntry();
        Destroy(item.gameObject);
        return true;
    }
}
