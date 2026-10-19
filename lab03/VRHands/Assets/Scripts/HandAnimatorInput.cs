using UnityEngine;
using UnityEngine.InputSystem;

public class HandAnimatorInput : MonoBehaviour
{
    static readonly int k_Grip = Animator.StringToHash("Grip");
    static readonly int k_Trigger = Animator.StringToHash("Trigger");

    [SerializeField] InputActionProperty m_GripAction;
    [SerializeField] InputActionProperty m_TriggerAction;
    [SerializeField] Animator m_Animator;
    [SerializeField] float m_Responsiveness = 18f;

    public float Grip { get; private set; }
    public float Trigger { get; private set; }

    public void Configure(InputActionReference grip, InputActionReference trigger, Animator animator)
    {
        m_GripAction = new InputActionProperty(grip);
        m_TriggerAction = new InputActionProperty(trigger);
        m_Animator = animator;
    }

    void Update()
    {
        var blend = 1f - Mathf.Exp(-m_Responsiveness * Time.deltaTime);
        Grip = Mathf.Lerp(Grip, Read(m_GripAction), blend);
        Trigger = Mathf.Lerp(Trigger, Read(m_TriggerAction), blend);
        m_Animator.SetFloat(k_Grip, Grip);
        m_Animator.SetFloat(k_Trigger, Trigger);
    }

    static float Read(InputActionProperty property)
    {
        var action = property.action;
        return action != null && action.enabled ? Mathf.Clamp01(action.ReadValue<float>()) : 0f;
    }
}
