using UnityEngine;

public class CharacterEffects : MonoBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private Transform effectAnchor;

    [Header("Particles")]
    [SerializeField] private ParticleSystem hitEffectPrefab;
    [SerializeField] private ParticleSystem deathEffectPrefab;

    private void Awake()
    {
        if (effectAnchor == null) effectAnchor = transform;
    }

    private void OnEnable()
    {
        if (health == null) return;
        health.Damaged += OnDamaged;
        health.Died += OnDied;
    }

    private void OnDisable()
    {
        if (health == null) return;
        health.Damaged -= OnDamaged;
        health.Died -= OnDied;
    }

    private void OnDamaged() => Spawn(hitEffectPrefab);

    private void OnDied() => Spawn(deathEffectPrefab);

    private void Spawn(ParticleSystem prefab)
    {
        if (prefab == null) return;
        Instantiate(prefab, effectAnchor.position, effectAnchor.rotation, effectAnchor);
    }
}