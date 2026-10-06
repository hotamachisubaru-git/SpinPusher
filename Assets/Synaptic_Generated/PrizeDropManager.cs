using System.Collections.Generic;
using UnityEngine;

/// <summary>Collects medals and lottery balls; optional legacy prize spawning stays disabled.</summary>
[RequireComponent(typeof(MedalPusherGame))]
public class PrizeDropManager : MonoBehaviour
{
    [Header("Drop Zone Settings")]
    public Vector3 dropZoneCenter = new Vector3(0f, 0f, -4.7f);
    public Vector2 dropZoneSize = new Vector2(8f, 2f);
    public float dropYPosition = -2f;
    [Header("Prize Settings")]
    public bool spawnGenericPrizes;
    public int minPrizesOnBoard = 8;
    public int maxPrizesOnBoard = 15;
    public float prizeSpawnInterval = 2f;
    public float spawnTimer;
    [Header("Prize Types")]
    public PrizeType[] prizeTypes;
    [Header("Medal Settings")]
    public int medalsPerPrize = 5;
    public int bonusMedalChance = 20;
    public int bonusMedalCount = 10;
    [Header("Scoring")]
    public int scorePerPrize = 100;
    public int scorePerMedal = 10;
    private readonly List<PrizeType> validTypes = new List<PrizeType>();
    private MedalPusherGame game;

    void Awake() { game = GetComponent<MedalPusherGame>(); }

    void Start()
    {
        if (!spawnGenericPrizes) return;
        RefreshPrizeTypes();
        int minimum = Mathf.Clamp(minPrizesOnBoard, 0, PrizeLimit());
        int initialCount = game != null ? game.CountBoardItems(true) : 0;
        for (int i = initialCount; i < minimum; i++) SpawnPrize();
        spawnTimer = Mathf.Max(0.2f, prizeSpawnInterval);
    }

    void Update()
    {
        if (game == null || !spawnGenericPrizes) return;
        spawnTimer -= Time.deltaTime;
        if (spawnTimer > 0f) return;
        if (game.CountBoardItems(true) < Mathf.Clamp(minPrizesOnBoard, 0, PrizeLimit())) SpawnPrize();
        spawnTimer = Mathf.Max(0.2f, prizeSpawnInterval);
    }

    private int PrizeLimit()
    {
        return Mathf.Max(0, Mathf.Min(maxPrizesOnBoard, game != null ? game.maxPrizesOnBoard : maxPrizesOnBoard));
    }

    private void RefreshPrizeTypes()
    {
        validTypes.Clear();
        if (prizeTypes != null)
            foreach (PrizeType type in prizeTypes)
                if (type.prefab != null && type.prefab.GetComponent<Rigidbody>() != null) validTypes.Add(type);
        if (validTypes.Count == 0 && game != null && game.prizePrefabs != null)
            foreach (GameObject prefab in game.prizePrefabs)
                if (prefab != null && prefab.GetComponent<Rigidbody>() != null)
                    validTypes.Add(new PrizeType { name = prefab.name, prefab = prefab, weight = 1, pointValue = scorePerPrize });
    }

    public void SpawnPrize()
    {
        if (game == null || !spawnGenericPrizes || game.CountBoardItems(true) >= PrizeLimit()) return;
        if (validTypes.Count == 0) RefreshPrizeTypes();
        if (validTypes.Count == 0) return;
        float total = 0f;
        foreach (PrizeType candidate in validTypes) total += Mathf.Max(1, candidate.weight);
        float choice = Random.value * total;
        PrizeType type = validTypes[validTypes.Count - 1];
        foreach (PrizeType candidate in validTypes)
        {
            choice -= Mathf.Max(1, candidate.weight);
            if (choice <= 0f) { type = candidate; break; }
        }
        Vector3 local = new Vector3(Random.Range(-3f, 3f), 2.4f, Random.Range(-1f, 2f));
        GameObject prize = Instantiate(type.prefab, game.transform.TransformPoint(local), Random.rotation,
            game.itemsRoot != null ? game.itemsRoot : game.transform);
        MedalItem item = prize.GetComponent<MedalItem>();
        if (item == null) item = prize.AddComponent<MedalItem>();
        item.isPrize = true;
        item.collected = false;
        item.pointValue = type.pointValue > 0 ? type.pointValue : Mathf.Max(1, scorePerPrize);
        item.displayName = string.IsNullOrWhiteSpace(type.name) ? type.prefab.name : type.name;
        Rigidbody body = prize.GetComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        game.RegisterItem(item);
    }

    // Collection-trigger entry is authoritative: no extra height threshold.
    public void CheckPrizeDrop(GameObject prize, Vector3 position)
    {
        if (game == null || prize == null) return;
        MedalItem item = prize.GetComponent<MedalItem>();
        if (item == null || !item.isPrize || item.collected) return;
        if (item.isBall)
        {
            var jackpots = GetComponent<MedalSlotJackpotController>();
            if (jackpots != null) jackpots.CollectBall(item);
            return;
        }
        item.collected = true;
        game.DropPrize(prize);
        game.AddScore(item.pointValue > 0 ? item.pointValue : scorePerPrize);
        game.AddMedals(Mathf.Max(0, medalsPerPrize));
        game.QueuePayoutMedals(Mathf.Max(0, medalsPerPrize), false);
        if (Random.Range(0, 100) < Mathf.Clamp(bonusMedalChance, 0, 100))
            game.DropBonusMedals(bonusMedalCount);
    }

    public void CheckMedalDrop(GameObject medal, Vector3 position)
    {
        if (game == null || medal == null) return;
        MedalItem item = medal.GetComponent<MedalItem>();
        if (item == null || item.isPrize || item.collected) return;
        item.collected = true;
        game.AddScore(Mathf.Max(0, scorePerMedal));
        if (!item.payoutAlreadyCredited) game.AddMedals(1);
        game.PlayMedalImpact(0.5f);
        Destroy(medal);
    }

    [System.Serializable]
    public struct PrizeType
    {
        public string name;
        public GameObject prefab;
        public int weight;
        public int pointValue;
    }
}
