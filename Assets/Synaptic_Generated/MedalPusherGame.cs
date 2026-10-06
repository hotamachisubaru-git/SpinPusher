using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns board physics, input, rewards and sound playback.</summary>
[RequireComponent(typeof(AudioSource))]
public class MedalPusherGame : MonoBehaviour
{
    [Header("Game Settings")]
    public int startingMedals = 100;
    public int medalsPerThrow = 1;
    public float throwInterval = 0.15f;
    public float pusherSpeed = 1.5f;
    public float pusherRange = 0.65f;
    public float dropHeight = -8f;
    public float medalSpawnHeight = 2f;
    public float medalSpawnDelay = 0.2f;
    [Header("Board References")]
    public Rigidbody pusherBody;
    public Transform medalSpawnPoint;
    public Transform itemsRoot;
    public Transform[] medalInlets;
    public int selectedInlet = 1;
    public float inletScatter = 0.11f;
    [Header("Payout And Side Holes")]
    public Transform[] sidePayoutPoints;
    public Transform jackpotPayoutPoint;
    public Transform sideHoleMainDeck;
    public Transform[] sideHoleDeckStrips;
    public Transform[] sideHoleTriggers;
    public Transform[] sideHoleVisuals;
    public float sideHoleWidth = .55f;
    [Header("Prefabs")]
    public GameObject medalPrefab;
    public GameObject[] prizePrefabs;
    public GameObject[] bonusMedalPrefabs;
    [Header("Scoring")]
    public int score;
    public int medals;
    public int comboCount;
    public float comboTimer;
    public float comboWindow = 3f;
    [Header("Prize Drop Settings")]
    public float prizeDropThreshold = 0.3f;
    public int maxPrizesOnBoard = 20;
    public int maxMedalsOnBoard = 1024;
    [Header("Audio")]
    public AudioClip medalDropSound;
    public AudioClip medalImpactSound;
    public AudioClip prizeDropSound;
    public AudioClip bonusSound;
    public AudioClip comboSound;
    public AudioClip jackpotSound;
    [Header("Visual Effects")]
    public GameObject prizeDropEffect;
    public GameObject bonusEffect;
    public GameObject comboEffect;
    public GameObject jackpotEffect;
    public System.Action<int> OnScoreChanged;
    public System.Action<int> OnMedalsChanged;
    public System.Action<int> OnComboChanged;
    public System.Action<GameObject> OnPrizeDropped;
    public System.Action OnJackpot;
    public System.Action OnMedalInserted;
    public System.Action<int> OnInletChanged;
    public System.Action<GameObject> OnPaidMedalSpawned;
    public System.Action<GameObject, bool> OnPayoutMedalSpawned;
    public long TotalPaidMedals { get; private set; }
    public long TotalReturnedMedals { get; private set; }
    public long PendingPayoutMedals { get; private set; }
    public bool SettingsOpen { get; private set; }
    public Vector3 CurrentMedalDropLocalPosition { get; private set; }
    public GameObject LastInsertedMedal { get; private set; }

    private readonly List<MedalItem> boardItems = new List<MedalItem>();
    private readonly HashSet<GameObject> notifiedPrizes = new HashSet<GameObject>();
    private AudioSource audioSource;
    private PrizeDropManager prizeManager;
    private Transform pusherParent;
    private Vector3 pusherInitialPosition;
    private Vector3 pusherLocalDirection;
    private float pusherTime;
    private float throwTimer;
    private float cleanupTimer;
    private float nextImpactTime;
    private bool isThrowing;
    private int pendingBonusMedals;
    private float bonusSpawnTimer;
    private readonly Queue<PayoutBatch> payoutQueue = new Queue<PayoutBatch>();
    private readonly Queue<PayoutBatch> jackpotPayoutQueue = new Queue<PayoutBatch>();
    private PayoutBatch lastPayoutBatch;
    private PayoutBatch lastJackpotBatch;
    private int sidePayoutSequence;
    private readonly List<Collider> medalDropSurfaces = new List<Collider>();
    private readonly List<MedalSlotPocket> medalClickOpenings = new List<MedalSlotPocket>();
    private sealed class PayoutBatch
    {
        public long remaining;
        public bool fromTop, alreadyCredited;
    }

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        prizeManager = GetComponent<PrizeDropManager>();
        medals = Mathf.Max(0, startingMedals);
        if (itemsRoot == null) itemsRoot = transform;
        if (pusherBody == null)
        {
            Transform plate = transform.Find("PusherPlate");
            if (plate != null) pusherBody = plate.GetComponent<Rigidbody>();
        }
        if (pusherBody != null)
        {
            pusherParent = pusherBody.transform.parent;
            pusherInitialPosition = pusherBody.transform.localPosition;
            pusherLocalDirection = pusherBody.transform.localRotation * Vector3.forward;
            pusherBody.isKinematic = true;
            pusherBody.useGravity = false;
            pusherBody.interpolation = RigidbodyInterpolation.Interpolate;
            pusherBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        foreach (MedalItem item in FindObjectsByType<MedalItem>(FindObjectsSortMode.None)) RegisterItem(item);
        CacheMedalDropSurfaces();
        CurrentMedalDropLocalPosition = Vector3.zero;
    }

    void Start()
    {
        OnScoreChanged?.Invoke(score);
        OnMedalsChanged?.Invoke(medals);
    }

    void FixedUpdate()
    {
        if (pusherBody == null) return;
        pusherTime += Time.fixedDeltaTime * Mathf.Max(0f, pusherSpeed);
        Vector3 target = pusherInitialPosition + pusherLocalDirection *
            (Mathf.Sin(pusherTime) * Mathf.Max(0f, pusherRange));
        pusherBody.MovePosition(pusherParent != null ? pusherParent.TransformPoint(target) : target);
    }

    void Update()
    {
        if (comboCount > 0)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f) { comboCount = 0; OnComboChanged?.Invoke(0); }
        }
        throwTimer = Mathf.Max(0f, throwTimer - Time.deltaTime);
        if (isThrowing && throwTimer <= 0f)
        {
            ThrowSingleMedal();
            if (medals <= 0) StopThrowing();
        }
        bonusSpawnTimer -= Time.deltaTime;
        if ((payoutQueue.Count > 0 || jackpotPayoutQueue.Count > 0) && bonusSpawnTimer <= 0f && CountBoardItems(false) < maxMedalsOnBoard && HasBody(medalPrefab))
        {
            Queue<PayoutBatch> activeQueue = jackpotPayoutQueue.Count > 0 ? jackpotPayoutQueue : payoutQueue;
            PayoutBatch batch = activeQueue.Peek();
            if (!batch.fromTop && !SidePayoutOutletClear()) bonusSpawnTimer = .05f;
            else
            {
                SpawnPayoutMedal(batch.fromTop, batch.alreadyCredited);
                batch.remaining--;
                PendingPayoutMedals--;
                if (!batch.alreadyCredited) pendingBonusMedals = Mathf.Max(0, pendingBonusMedals - 1);
                bonusSpawnTimer = batch.fromTop ? .025f : .075f;
                if (batch.remaining <= 0) activeQueue.Dequeue();
                if (payoutQueue.Count == 0) lastPayoutBatch = null;
                if (jackpotPayoutQueue.Count == 0) lastJackpotBatch = null;
            }
        }
        cleanupTimer -= Time.deltaTime;
        if (cleanupTimer > 0f) return;
        cleanupTimer = 0.5f;
        for (int i = boardItems.Count - 1; i >= 0; i--)
        {
            MedalItem item = boardItems[i];
            if (item == null || item.collected) { boardItems.RemoveAt(i); continue; }
            Vector3 position = transform.InverseTransformPoint(item.transform.position);
            if (position.y < Mathf.Min(-8f, dropHeight) || Mathf.Abs(position.x) > 15f || Mathf.Abs(position.z) > 20f)
            {
                item.collected = true;
                boardItems.RemoveAt(i);
                Destroy(item.gameObject);
            }
        }
    }

    public void RegisterItem(MedalItem item)
    {
        if (item == null) return;
        item.BindGame(this);
        if (!item.collected && !boardItems.Contains(item)) boardItems.Add(item);
    }

    public void UnregisterItem(MedalItem item)
    {
        boardItems.Remove(item);
        if (item != null) notifiedPrizes.Remove(item.gameObject);
    }

    public int CountBoardItems(bool prizes)
    {
        int count = 0;
        foreach (MedalItem item in boardItems)
            if (item != null && !item.collected && item.isPrize == prizes) count++;
        return count;
    }

    public void ThrowSingleMedal()
    {
        if (SettingsOpen || throwTimer > 0f || medals <= 0 || !HasBody(medalPrefab)) return;
        // Paid input uses the actual board capacity. Queued bonus medals wait
        // for free space instead of reserving every slot and blocking the player.
        int available = Mathf.Max(0, maxMedalsOnBoard - CountBoardItems(false));
        int count = Mathf.Min(Mathf.Max(1, medalsPerThrow), Mathf.Min(medals, available));
        if (count == 0) return;
        medals -= count;
        TotalPaidMedals += count;
        for (int i = 0; i < count; i++) { SpawnMedal(medalPrefab); OnMedalInserted?.Invoke(); }
        OnMedalsChanged?.Invoke(medals);
        PlaySound(medalDropSound);
        throwTimer = Mathf.Max(0.03f, throwInterval);
    }

    private static bool HasBody(GameObject prefab)
    {
        return prefab != null && prefab.GetComponent<Rigidbody>() != null;
    }

    private void SpawnMedal(GameObject prefab)
    {
        Vector3 local = CurrentMedalDropLocalPosition;
        local.y = Mathf.Max(medalSpawnHeight, local.y + .8f);
        // Keep the clicked X/Z exact. A tall pile only raises the release point
        // so that new medals start outside the existing physical pieces.
        Vector3 world = transform.TransformPoint(local);
        foreach (MedalItem boardItem in boardItems)
        {
            if (boardItem == null || boardItem.collected) continue;
            Collider occupied = boardItem.GetComponent<Collider>();
            if (occupied == null || !occupied.enabled) continue;
            Bounds bounds = occupied.bounds;
            if (world.x + .4f < bounds.min.x || world.x - .4f > bounds.max.x
                || world.z + .4f < bounds.min.z || world.z - .4f > bounds.max.z) continue;
            float top = transform.InverseTransformPoint(new Vector3(world.x, bounds.max.y, world.z)).y;
            local.y = Mathf.Max(local.y, top + .15f);
        }
        GameObject medal = Instantiate(prefab, transform.TransformPoint(local), transform.rotation, itemsRoot);
        medal.name = "InsertedMedal";
        MedalItem item = medal.GetComponent<MedalItem>();
        if (item == null) item = medal.AddComponent<MedalItem>();
        item.isPrize = false;
        item.collected = false;
        item.payoutAlreadyCredited = false;
        Rigidbody body = medal.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = transform.TransformDirection(new Vector3(0f, -.5f, 0f));
        RegisterItem(item);
        LastInsertedMedal = medal;
        OnPaidMedalSpawned?.Invoke(medal);
    }

    private void CacheMedalDropSurfaces()
    {
        medalDropSurfaces.Clear();
        medalClickOpenings.Clear();
        AddMedalDropSurface(pusherBody != null ? pusherBody.transform : transform.Find("PusherPlate"));
        if (pusherBody != null)
            medalClickOpenings.AddRange(pusherBody.GetComponentsInChildren<MedalSlotPocket>(true));
        AddMedalDropSurface(sideHoleMainDeck);
        if (sideHoleDeckStrips != null)
            foreach (Transform strip in sideHoleDeckStrips) AddMedalDropSurface(strip);
        Transform field = transform.Find("GeneratedPlayfield");
        if (field != null) AddMedalDropSurface(field.Find("PusherBoard"));
    }

    private void AddMedalDropSurface(Transform surface)
    {
        if (surface == null) return;
        foreach (Collider collider in surface.GetComponents<Collider>())
            if (!medalDropSurfaces.Contains(collider)) medalDropSurfaces.Add(collider);
    }

    public bool TrySetMedalDropTarget(Camera camera, Vector2 screenPosition)
    {
        if (SettingsOpen || camera == null || !camera.pixelRect.Contains(screenPosition)) return false;
        if (medalDropSurfaces.Count == 0) CacheMedalDropSurfaces();
        Ray ray = camera.ScreenPointToRay(screenPosition);
        Vector3 closestPoint = Vector3.zero;
        Vector3 closestNormal = Vector3.zero;
        float closestDistance = float.PositiveInfinity;
        Collider closestCollider = null;
        bool found = false;
        foreach (Collider surface in medalDropSurfaces)
        {
            if (surface == null || !surface.enabled || !surface.gameObject.activeInHierarchy || surface.isTrigger) continue;
            if (!surface.Raycast(ray, out RaycastHit hit, camera.farClipPlane)) continue;
            if (hit.distance < closestDistance)
            {
                closestPoint = hit.point; closestNormal = hit.normal;
                closestDistance = hit.distance; closestCollider = hit.collider; found = true;
            }
        }
        // An opening has no top collider. Its mouth plane still describes the
        // clicked release position, without filling the real physical hole.
        foreach (MedalSlotPocket pocket in medalClickOpenings)
        {
            if (pocket == null || !pocket.isActiveAndEnabled) continue;
            float radius = pocket.clickOpeningRadius;
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) continue;
            Vector3 normal = pocket.transform.up;
            if (Vector3.Dot(ray.direction, normal) >= 0f) continue;
            Plane mouth = new Plane(normal, pocket.transform.position);
            if (!mouth.Raycast(ray, out float distance) || distance > camera.farClipPlane || distance >= closestDistance) continue;
            Vector3 point = ray.GetPoint(distance);
            Vector3 local = pocket.transform.InverseTransformPoint(point);
            if (local.x * local.x + local.z * local.z >= radius * radius) continue;
            closestPoint = point; closestNormal = normal;
            closestDistance = distance; closestCollider = null; found = true;
        }
        // Only playing faces and the front side of the tilted mouths count.
        if (!found || Vector3.Dot(closestNormal, transform.up) < .85f) return false;
        foreach (RaycastHit obstacle in Physics.RaycastAll(ray, closestDistance + .001f,
            Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (obstacle.collider == closestCollider || medalDropSurfaces.Contains(obstacle.collider)) continue;
            if (obstacle.collider.GetComponentInParent<MedalItem>() != null) continue;
            return false;
        }
        CurrentMedalDropLocalPosition = transform.InverseTransformPoint(closestPoint);
        return true;
    }

    public void SelectInlet(int index)
    {
        // Kept for old scene/test callbacks. Paid drops use the last clicked
        // board position; the former three inlet transforms no longer aim them.
        selectedInlet = Mathf.Clamp(index, 0, 2);
        OnInletChanged?.Invoke(selectedInlet);
    }

    public void SelectLeftInlet() { SelectInlet(0); }
    public void SelectCenterInlet() { SelectInlet(1); }
    public void SelectRightInlet() { SelectInlet(2); }

    public void StartThrowing() { if (!SettingsOpen && medals > 0) isThrowing = true; }
    public void StopThrowing() { isThrowing = false; }
    public void SetSettingsOpen(bool open) { SettingsOpen = open; if (open) StopThrowing(); }
    void OnDisable() { StopThrowing(); }

    public void AddScore(int points)
    {
        if (points <= 0) return;
        comboCount++;
        comboTimer = Mathf.Max(0.1f, comboWindow);
        score += points * (1 + comboCount / 10);
        OnScoreChanged?.Invoke(score);
        OnComboChanged?.Invoke(comboCount);
        if (comboCount % 10 == 0)
        {
            PlaySound(comboSound);
            SpawnEffect(comboEffect, transform.position + Vector3.up * 2f);
        }
        if (comboCount % 30 == 0 && GetComponent<MedalSlotJackpotController>() == null)
        {
            PlaySound(jackpotSound);
            SpawnEffect(jackpotEffect, transform.position + Vector3.up * 2f);
            OnJackpot?.Invoke();
            DropBonusMedals(prizeManager != null ? prizeManager.bonusMedalCount : 10);
        }
    }

    public void AddMedals(int count)
    {
        if (count <= 0) return;
        int added = (int)System.Math.Min(count, (long)int.MaxValue - medals);
        medals += added;
        TotalReturnedMedals += added;
        OnMedalsChanged?.Invoke(medals);
    }

    public void DropPrize(GameObject prize)
    {
        if (prize == null || !notifiedPrizes.Add(prize)) return;
        MedalItem item = prize.GetComponent<MedalItem>();
        if (item != null) item.collected = true;
        OnPrizeDropped?.Invoke(prize);
        PlaySound(prizeDropSound);
        SpawnEffect(prizeDropEffect, prize.transform.position);
        Destroy(prize);
    }

    public void DropBonusMedals(int count)
    {
        QueuePayout(count, false, false);
        if (count > 0)
        {
            PlaySound(bonusSound);
            SpawnEffect(bonusEffect, transform.position + Vector3.up * 2f);
        }
    }

    // Rewards are credited once when won. Their visible medals cannot credit
    // the wallet again when they subsequently cross the front collector.
    public void QueuePayoutMedals(int count, bool fromTop)
    {
        QueuePayout(count, fromTop, true);
    }

    private void QueuePayout(int count, bool fromTop, bool alreadyCredited)
    {
        if (count <= 0 || !HasBody(medalPrefab)) return;
        PayoutBatch batch = fromTop ? lastJackpotBatch : lastPayoutBatch;
        if (batch == null || batch.alreadyCredited != alreadyCredited)
        {
            batch = new PayoutBatch { fromTop = fromTop, alreadyCredited = alreadyCredited };
            if (fromTop) { lastJackpotBatch = batch; jackpotPayoutQueue.Enqueue(batch); }
            else { lastPayoutBatch = batch; payoutQueue.Enqueue(batch); }
        }
        batch.remaining += count;
        PendingPayoutMedals += count;
        if (!alreadyCredited) pendingBonusMedals = (int)System.Math.Min(int.MaxValue, (long)pendingBonusMedals + count);
    }

    private void SpawnPayoutMedal(bool fromTop, bool alreadyCredited)
    {
        int side = sidePayoutSequence++ % 2;
        Transform point = fromTop ? jackpotPayoutPoint :
            (sidePayoutPoints != null && side < sidePayoutPoints.Length ? sidePayoutPoints[side] : null);
        Vector3 local = point != null ? transform.InverseTransformPoint(point.position) :
            fromTop ? new Vector3(0, 5.2f, -1.5f) : new Vector3(side == 0 ? -4.2f : 4.2f, 3.05f, 1.8f);
        local.x += fromTop ? Random.Range(-1.7f, 1.7f) : Random.Range(-.08f, .08f);
        local.z += Random.Range(-.10f, .10f);
        GameObject medal = Instantiate(medalPrefab, transform.TransformPoint(local), transform.rotation, itemsRoot);
        medal.name = fromTop ? "JACKPOT_PayoutMedal" : side == 0 ? "Left_PayoutMedal" : "Right_PayoutMedal";
        var item = medal.GetComponent<MedalItem>() ?? medal.AddComponent<MedalItem>();
        item.isPrize = false; item.isBall = false; item.collected = false; item.payoutAlreadyCredited = alreadyCredited;
        var body = medal.GetComponent<Rigidbody>();
        body.isKinematic = false; body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = transform.TransformDirection(fromTop
            ? new Vector3(Random.Range(-1.8f, 1.8f), -1.2f, Random.Range(-.8f, .2f))
            : SidePayoutVelocity(side, local));
        RegisterItem(item);
        OnPayoutMedalSpawned?.Invoke(medal, fromTop);
    }

    private Vector3 SidePayoutVelocity(int side, Vector3 spawnLocal)
    {
        float targetZ = 1.8f + Random.Range(-.55f, .55f);
        float flight = .45f;
        if (pusherBody != null)
        {
            var plate = pusherBody.GetComponent<Collider>();
            float topY = plate != null
                ? transform.InverseTransformPoint(new Vector3(plate.bounds.center.x, plate.bounds.max.y, plate.bounds.center.z)).y
                : .52f;
            float gravity = Mathf.Max(.1f, -transform.InverseTransformDirection(Physics.gravity).y);
            float drop = Mathf.Max(.05f, spawnLocal.y - topY - .05f);
            flight = Mathf.Max(.1f, (-.65f + Mathf.Sqrt(.65f * .65f + 2f * gravity * drop)) / gravity);
            Vector3 futurePlate = pusherInitialPosition + pusherLocalDirection *
                (Mathf.Sin(pusherTime + flight * Mathf.Max(0f, pusherSpeed)) * Mathf.Max(0f, pusherRange));
            Vector3 futureWorld = pusherParent != null ? pusherParent.TransformPoint(futurePlate) : futurePlate;
            targetZ = transform.InverseTransformPoint(futureWorld).z + Random.Range(-.55f, .55f);
        }
        // Give each medal its trajectory at the outlet. Gravity then drops it
        // across the moving upper plate instead of piling up at the front edge.
        float targetX = side == 0 ? Random.Range(-3.5f, .7f) : Random.Range(-.7f, 3.5f);
        return new Vector3((targetX - spawnLocal.x) / flight, -.65f, (targetZ - spawnLocal.z) / flight);
    }

    private bool SidePayoutOutletClear()
    {
        int side = sidePayoutSequence % 2;
        Vector3 position = sidePayoutPoints != null && side < sidePayoutPoints.Length && sidePayoutPoints[side] != null
            ? sidePayoutPoints[side].position : transform.TransformPoint(new Vector3(side == 0 ? -4.2f : 4.2f, 3.05f, 1.8f));
        // This box covers the actual medal plus the outlet's spawn scatter.
        // Leave the entire payout queued until an occupied outlet clears.
        return !Physics.CheckBox(position, new Vector3(.41f, .055f, .42f), transform.rotation,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
    }

    public void ConfigureSideHoles(float width)
    {
        sideHoleWidth = float.IsNaN(width) || float.IsInfinity(width) ? .55f : Mathf.Clamp(width, 0f, 1.5f);
        float w = sideHoleWidth;
        if (sideHoleMainDeck != null) sideHoleMainDeck.localScale = new Vector3(10f - w * 2f, .3f, 8.2f);
        for (int i = 0; sideHoleDeckStrips != null && i < sideHoleDeckStrips.Length; i++)
        {
            Transform strip = sideHoleDeckStrips[i]; if (strip == null) continue;
            int side = i < 2 ? -1 : 1;
            bool front = i % 2 == 0;
            strip.gameObject.SetActive(w > .001f);
            strip.localPosition = new Vector3(side * (5f - w * .5f), -.15f, front ? -3.35f : 1.65f);
            strip.localScale = new Vector3(Mathf.Max(.001f, w), .3f, front ? 2.3f : 4.1f);
        }
        for (int i = 0; sideHoleTriggers != null && i < sideHoleTriggers.Length; i++)
        {
            Transform hole = sideHoleTriggers[i]; if (hole == null) continue;
            hole.gameObject.SetActive(w > .001f);
            hole.localPosition = new Vector3((i == 0 ? -1 : 1) * (5f - w * .5f), -.7f, -1.3f);
            hole.localScale = new Vector3(Mathf.Max(.001f, w), .8f, 1.8f);
        }
        for (int i = 0; sideHoleVisuals != null && i < sideHoleVisuals.Length; i++)
        {
            Transform visual = sideHoleVisuals[i]; if (visual == null) continue;
            visual.gameObject.SetActive(w > .001f);
            visual.localPosition = new Vector3((i == 0 ? -1 : 1) * (5f - w * .5f), -.85f, -1.3f);
            visual.localScale = new Vector3(Mathf.Max(.001f, w), .05f, 1.8f);
        }
    }

    public void LoseToSideHole(MedalItem item)
    {
        if (item == null || item.collected) return;
        item.collected = true;
        UnregisterItem(item);
        Destroy(item.gameObject);
    }

    public void PlayMedalImpact(float volume)
    {
        if (Time.time < nextImpactTime || medalImpactSound == null || audioSource == null) return;
        nextImpactTime = Time.time + 0.06f;
        audioSource.PlayOneShot(medalImpactSound, Mathf.Clamp01(volume));
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }

    public void PlayEventSound(AudioClip clip) { PlaySound(clip); }

    private static void SpawnEffect(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;
        GameObject effect = Instantiate(prefab, position, Quaternion.identity);
        Destroy(effect, 3f);
    }
}
