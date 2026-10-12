using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Scores a dart hit by its distance from the centre of the board, multiplied by
/// how far away the player threw from, and plays the hit feedback
/// (particles, ring flash, sound, floating score text).
/// The board faces along its local +Z axis.
/// </summary>
public class DartBoard : MonoBehaviour
{
    [Serializable]
    public struct Ring
    {
        public string name;
        [Tooltip("Outer radius as a fraction of the board radius.")]
        public float outerRadius;
        public int points;
        public Color color;
    }

    [Serializable]
    public struct DistanceZone
    {
        [Tooltip("Minimum throw distance (metres) for this multiplier.")]
        public float minDistance;
        public float multiplier;
    }

    // Shared with the editor scene builder so the visuals always match the scoring.
    public static readonly Ring[] DefaultRings =
    {
        new Ring { name = "BULLSEYE",   outerRadius = 0.06f, points = 50, color = new Color(0.85f, 0.10f, 0.10f) },
        new Ring { name = "Outer Bull", outerRadius = 0.14f, points = 25, color = new Color(0.10f, 0.55f, 0.20f) },
        new Ring { name = "Ring 20",    outerRadius = 0.26f, points = 20, color = new Color(0.08f, 0.08f, 0.08f) },
        new Ring { name = "Ring 15",    outerRadius = 0.38f, points = 15, color = new Color(0.95f, 0.90f, 0.75f) },
        new Ring { name = "Ring 12",    outerRadius = 0.50f, points = 12, color = new Color(0.85f, 0.10f, 0.10f) },
        new Ring { name = "Ring 10",    outerRadius = 0.62f, points = 10, color = new Color(0.08f, 0.08f, 0.08f) },
        new Ring { name = "Ring 7",     outerRadius = 0.74f, points = 7,  color = new Color(0.95f, 0.90f, 0.75f) },
        new Ring { name = "Ring 5",     outerRadius = 0.87f, points = 5,  color = new Color(0.10f, 0.55f, 0.20f) },
        new Ring { name = "Ring 2",     outerRadius = 1.00f, points = 2,  color = new Color(0.08f, 0.08f, 0.08f) },
    };

    public static readonly DistanceZone[] DefaultZones =
    {
        new DistanceZone { minDistance = 0f,   multiplier = 1f },
        new DistanceZone { minDistance = 2.0f, multiplier = 1.5f },
        new DistanceZone { minDistance = 3.0f, multiplier = 2f },
    };

    [Tooltip("Radius of the scoring area in metres.")]
    public float radius = 0.25f;

    public Ring[] rings = (Ring[])DefaultRings.Clone();
    public DistanceZone[] zones = (DistanceZone[])DefaultZones.Clone();

    [Header("Feedback")]
    [Tooltip("One renderer per ring, in the same order as Rings. Flashed on hit.")]
    public Renderer[] ringRenderers;
    public ParticleSystem hitParticles;
    public AudioSource audioSource;
    public Font popupFont;

    AudioClip m_HitClip;
    AudioClip m_BullClip;

    void Awake()
    {
        m_HitClip = CreateTone("Hit", 660f, 0.18f);
        m_BullClip = CreateTone("Bull", 990f, 0.45f);
    }

    /// <summary>Called by a Dart when it sticks into the board.</summary>
    public void RegisterHit(Vector3 worldPoint, Vector3 throwOrigin)
    {
        var center = transform.position;
        var normal = transform.forward;
        var distanceFromCenter = Vector3.ProjectOnPlane(worldPoint - center, normal).magnitude;
        var normalized = distanceFromCenter / radius;

        var ringIndex = -1;
        for (var i = 0; i < rings.Length; i++)
        {
            if (normalized <= rings[i].outerRadius)
            {
                ringIndex = i;
                break;
            }
        }

        // Throw distance measured on the floor plane, from where the dart was released.
        var throwDistance = Vector3.ProjectOnPlane(center - throwOrigin, Vector3.up).magnitude;
        var multiplier = GetMultiplier(throwDistance);

        var basePoints = ringIndex >= 0 ? rings[ringIndex].points : 0;
        var total = Mathf.RoundToInt(basePoints * multiplier);
        var label = ringIndex >= 0 ? rings[ringIndex].name : "Off the board";

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterHit(label, basePoints, multiplier, throwDistance, total);

        PlayFeedback(worldPoint, ringIndex, total, label);
    }

    public float GetMultiplier(float throwDistance)
    {
        var multiplier = 1f;
        foreach (var zone in zones)
        {
            if (throwDistance >= zone.minDistance)
                multiplier = zone.multiplier;
        }
        return multiplier;
    }

    void PlayFeedback(Vector3 point, int ringIndex, int total, string label)
    {
        var color = ringIndex >= 0 ? rings[ringIndex].color : Color.gray;
        var isBull = ringIndex == 0;

        if (hitParticles != null)
        {
            hitParticles.transform.position = point + transform.forward * 0.02f;
            var emit = new ParticleSystem.EmitParams
            {
                startColor = isBull ? Color.yellow : Color.Lerp(color, Color.white, 0.4f),
                applyShapeToPosition = true,
            };
            hitParticles.Emit(emit, isBull ? 120 : 40);
        }

        if (ringIndex >= 0 && ringRenderers != null && ringIndex < ringRenderers.Length && ringRenderers[ringIndex] != null)
            StartCoroutine(FlashRing(ringRenderers[ringIndex], rings[ringIndex].color));

        if (audioSource != null)
            audioSource.PlayOneShot(isBull ? m_BullClip : m_HitClip);

        var text = total > 0 ? $"+{total}\n<size=60%>{label}</size>" : "MISS";
        var popupColor = isBull ? Color.yellow : (total > 0 ? Color.white : new Color(1f, 0.4f, 0.4f));
        ScorePopup.Spawn(point + transform.forward * 0.15f, text, popupColor, popupFont);
    }

    IEnumerator FlashRing(Renderer ring, Color baseColor)
    {
        var material = ring.material;
        const float duration = 0.6f;
        for (var t = 0f; t < duration; t += Time.deltaTime)
        {
            // Two quick pulses towards white with an emissive glow.
            var k = Mathf.Abs(Mathf.Sin(t / duration * Mathf.PI * 2f));
            material.color = Color.Lerp(baseColor, Color.white, k);
            material.SetColor("_EmissionColor", Color.Lerp(Color.black, Color.white * 1.5f, k));
            yield return null;
        }
        material.color = baseColor;
        material.SetColor("_EmissionColor", Color.black);
    }

    static AudioClip CreateTone(string name, float frequency, float length)
    {
        const int sampleRate = 44100;
        var samples = new float[Mathf.CeilToInt(sampleRate * length)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (float)i / sampleRate;
            var envelope = Mathf.Exp(-t * 12f);
            // A short "thunk" followed by a bell-like ping.
            var thunk = Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 60f);
            var ping = Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * frequency * 1.5f * t);
            samples[i] = 0.5f * thunk + 0.25f * ping * envelope;
        }
        var clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
