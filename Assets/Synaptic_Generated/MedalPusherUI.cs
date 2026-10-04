using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>Displays game state and optional prize, combo and jackpot notifications.</summary>
public class MedalPusherUI : MonoBehaviour
{
    public static MedalPusherUI Instance { get; private set; }
    [Header("Score Display")]
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI scoreLabel;
    [Header("Medals Display")]
    public TextMeshProUGUI medalsText;
    public TextMeshProUGUI medalsLabel;
    [Header("Prize Display")]
    public TextMeshProUGUI prizeDisplay;
    public Canvas prizeCanvas;
    public float prizeDisplayDuration = 2f;
    [Header("Combo Display")]
    public GameObject comboPanel;
    public TextMeshProUGUI comboText;
    public TextMeshProUGUI comboLabel;
    public float comboDisplayDuration = 1f;
    [Header("Jackpot Display")]
    public GameObject jackpotPanel;
    public TextMeshProUGUI jackpotText;
    public float jackpotDuration = 3f;
    [Header("Visual Settings")]
    public Color scoreColor = new Color(1f, 1f, 0f, 1f);
    public Color medalsColor = new Color(0f, 1f, 1f, 1f);
    public Color comboColor = new Color(1f, 0.5f, 0f, 1f);
    public Color jackpotColor = new Color(1f, 0f, 1f, 1f);
    private MedalPusherGame game;
    private float prizeTimer;
    private float comboTimer;
    private Coroutine jackpotRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        HideScore();
    }

    void Start()
    {
        game = FindFirstObjectByType<MedalPusherGame>();
        if (game != null)
        {
            game.OnMedalsChanged += UpdateMedals;
            game.OnComboChanged += UpdateCombo;
            game.OnPrizeDropped += OnPrizeDropped;
            game.OnJackpot += OnJackpot;
        }
        HideScore();
        if (medalsLabel != null) medalsLabel.text = "メダル";
        if (comboLabel != null) comboLabel.text = "連続獲得";
        UpdateMedals(game != null ? game.medals : 0);
        UpdateCombo(game != null ? game.comboCount : 0);
        if (prizeDisplay != null) prizeDisplay.text = "";
        if (prizeCanvas != null) prizeCanvas.enabled = false;
        if (jackpotPanel != null) jackpotPanel.SetActive(false);
        StartCoroutine(RefreshTextMeshesAfterStartup());
    }

    private IEnumerator RefreshTextMeshesAfterStartup()
    {
        // Rebuild static captions after every component has applied its startup
        // text and the dynamically populated Japanese font has initialized.
        yield return null;
        foreach (var text in GetComponentsInChildren<TMP_Text>())
            if (text.isActiveAndEnabled) text.ForceMeshUpdate(false, true);
        Canvas.ForceUpdateCanvases();
    }

    public void UpdateScore(int value)
    {
        HideScore();
    }

    private void HideScore()
    {
        if (scoreText != null) { scoreText.text = ""; scoreText.gameObject.SetActive(false); }
        if (scoreLabel != null) { scoreLabel.text = ""; scoreLabel.gameObject.SetActive(false); }
    }

    public void UpdateMedals(int value)
    {
        if (medalsText == null) return;
        medalsText.text = value.ToString("D4");
        medalsText.color = medalsColor;
    }

    public void UpdateMedalsUI(int value) { UpdateMedals(value); }

    public void UpdateCombo(int value)
    {
        if (comboText != null)
        {
            comboText.text = value > 0 ? value + "連続獲得！" : "";
            comboText.color = comboColor;
        }
        if (comboPanel != null) comboPanel.SetActive(value > 0);
        comboTimer = value > 0 ? Mathf.Max(0.1f, comboDisplayDuration) : 0f;
    }

    public void UpdateComboUI(int value) { UpdateCombo(value); }

    void OnPrizeDropped(GameObject prize)
    {
        if (prize == null) return;
        MedalItem item = prize.GetComponent<MedalItem>();
        string label = item != null && !string.IsNullOrWhiteSpace(item.displayName)
            ? item.displayName : prize.name.Replace("(Clone)", "");
        label = JapanesePrizeName(label);
        if (prizeDisplay != null) { prizeDisplay.text = label + "を獲得！"; prizeDisplay.color = Color.white; }
        if (prizeCanvas != null) prizeCanvas.enabled = true;
        prizeTimer = Mathf.Max(0.1f, prizeDisplayDuration);
    }

    void OnJackpot()
    {
        if (jackpotPanel != null) jackpotPanel.SetActive(true);
        if (jackpotText != null) { jackpotText.text = "JACKPOT！\nボーナスメダル"; jackpotText.color = jackpotColor; }
        if (jackpotRoutine != null) StopCoroutine(jackpotRoutine);
        jackpotRoutine = StartCoroutine(HideJackpot());
    }

    void Update()
    {
        if (prizeTimer > 0f)
        {
            prizeTimer -= Time.deltaTime;
            if (prizeTimer <= 0f)
            {
                if (prizeDisplay != null) prizeDisplay.text = "";
                if (prizeCanvas != null) prizeCanvas.enabled = false;
            }
        }
        if (comboTimer > 0f)
        {
            comboTimer -= Time.deltaTime;
            if (comboTimer <= 0f && comboPanel != null) comboPanel.SetActive(false);
        }
    }

    private IEnumerator HideJackpot()
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, jackpotDuration));
        if (jackpotPanel != null) jackpotPanel.SetActive(false);
        jackpotRoutine = null;
    }

    private static string JapanesePrizeName(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "景品";
        switch (label)
        {
            case "Ruby Capsule": case "RubyCapsule": return "赤カプセル";
            case "Sapphire Box": case "SapphireBox": return "青ボックス";
            case "Golden Trophy": case "GoldenTrophy": return "金のトロフィー";
            case "Ruby Ball": case "RubyLotteryBall": return "赤ボール";
            case "Sapphire Ball": case "SapphireLotteryBall": return "青ボール";
            case "Amber Ball": case "AmberLotteryBall": return "黄ボール";
        }
        foreach (char character in label)
            if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z')) return "景品";
        return label;
    }

    void OnDestroy()
    {
        if (game != null)
        {
            game.OnMedalsChanged -= UpdateMedals;
            game.OnComboChanged -= UpdateCombo;
            game.OnPrizeDropped -= OnPrizeDropped;
            game.OnJackpot -= OnJackpot;
        }
        if (Instance == this) Instance = null;
    }
}
