using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    public float Current { get; private set; }
    public float Normalized => Current / maxHealth;
    public bool IsDead => Current <= 0f;

    public event Action<float> Changed;
    public event Action Damaged;
    public event Action Died;
    public event Action Revived;

    private void Awake() => Current = maxHealth;

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;

        Current = Mathf.Max(0f, Current - amount);
        Changed?.Invoke(Normalized);
        Damaged?.Invoke();

        if (IsDead) Died?.Invoke();
    }

    public void ReviveIfDead()
    {
        if (!IsDead) return;

        Current = maxHealth;
        Changed?.Invoke(Normalized);
        Revived?.Invoke();
    }
}