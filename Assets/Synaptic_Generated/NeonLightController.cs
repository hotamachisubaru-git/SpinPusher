using UnityEngine;

/// <summary>
/// ネオンライトの点滅・フェードアニメーション
/// </summary>
public class NeonLightController : MonoBehaviour
{
    [Header("Animation Settings")]
    public float flickerSpeed = 2f;
    public float flickerIntensity = 0.3f;
    public float pulseSpeed = 1f;
    public float pulseIntensity = 0.5f;
    public bool enableFlicker = true;
    public bool enablePulse = true;

    private Material mat;
    private Color originalColor;
    private float time = 0f;

    void Awake()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            mat = renderer.material;
            originalColor = mat.color;
        }
    }

    void Update()
    {
        if (mat == null) return;

        time += Time.deltaTime;
        Color newColor = originalColor;

        // Flicker effect
        if (enableFlicker)
        {
            float flicker = Mathf.PerlinNoise(time * flickerSpeed, 0) * flickerIntensity;
            newColor *= (1f - flicker);
        }

        // Pulse effect
        if (enablePulse)
        {
            float pulse = Mathf.Sin(time * pulseSpeed) * pulseIntensity * 0.5f + 0.5f;
            newColor *= (0.7f + pulse * 0.3f);
        }

        mat.color = newColor;
    }
}
