using UnityEngine;
using UnityEngine.UI;


public class HealthBar : MonoBehaviour
{
    [SerializeField] private Health health;
    [SerializeField] private Image fill;
    [SerializeField] private Gradient colorByHealth;

    private Camera mainCamera;

    private void Awake() => mainCamera = Camera.main;

    private void OnEnable()
    {
        if (health != null) health.Changed += UpdateBar;
    }

    private void OnDisable()
    {
        if (health != null) health.Changed -= UpdateBar;
    }

    private void Start()
    {
        if (health != null) UpdateBar(health.Normalized);
    }

    private void LateUpdate()
    {
        if (mainCamera == null) return;
        transform.rotation = Quaternion.LookRotation(
            transform.position - mainCamera.transform.position, mainCamera.transform.up);
    }

    private void UpdateBar(float normalized)
    {
        fill.fillAmount = normalized;
        fill.color = colorByHealth.Evaluate(normalized);
    }
}