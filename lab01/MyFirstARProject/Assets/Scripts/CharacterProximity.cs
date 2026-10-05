using UnityEngine;
using Vuforia;

[RequireComponent(typeof(Health))]
public class CharacterProximity : MonoBehaviour
{
    private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
    private static readonly int DieHash = Animator.StringToHash("Die");

    [Header("References")]
    [SerializeField] private Health opponent;
    [SerializeField] private ObserverBehaviour ownTarget;
    [SerializeField] private ObserverBehaviour opponentTarget;

    [Header("Distances (meters)")]
    [SerializeField, Min(0f)] private float attackDistance = 0.25f;
    [SerializeField, Min(0f)] private float releaseDistance = 0.30f;

    [Header("Combat")]
    [SerializeField, Min(0f)] private float damagePerHit = 20f;
    [Tooltip("Seconds between hits. Match it to the Attack clip length.")]
    [SerializeField, Min(0.05f)] private float hitInterval = 1f;

    [Header("Behaviour")]
    [SerializeField] private bool faceOpponent = true;
    [SerializeField, Min(0f)] private float turnSpeed = 5f;

    private Animator animator;
    private Health health;
    private bool isAttacking;
    private float hitTimer;

    private void Awake()
    {
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
        if (animator == null)
            Debug.LogError($"{name}: no Animator found in children.", this);
    }

    private void OnEnable()
    {
        health.Died += HandleDied;
        health.Revived += HandleRevived;
    }

    private void OnDisable()
    {
        health.Died -= HandleDied;
        health.Revived -= HandleRevived;
    }

    private void Update()
    {
        if (animator == null || opponent == null) return;

        bool canFight = !health.IsDead && !opponent.IsDead &&
                        IsVisible(ownTarget) && IsVisible(opponentTarget);

        float distance = Vector3.Distance(transform.position, opponent.transform.position);

        bool shouldAttack = canFight &&
                            (isAttacking ? distance < releaseDistance
                                         : distance < attackDistance);
        SetAttacking(shouldAttack);

        if (!isAttacking) return;

        if (faceOpponent) RotateTowardsOpponent();

        hitTimer += Time.deltaTime;
        if (hitTimer >= hitInterval)
        {
            hitTimer -= hitInterval;
            opponent.TakeDamage(damagePerHit);
        }
    }

    private void SetAttacking(bool value)
    {
        if (value == isAttacking) return;

        isAttacking = value;
        hitTimer = 0f;
        animator.SetBool(IsAttackingHash, isAttacking);
    }

    private void HandleDied()
    {
        SetAttacking(false);
        animator.SetTrigger(DieHash);
    }

    private void HandleRevived()
    {
        animator.Rebind();
        animator.Update(0f);
        isAttacking = false;
        hitTimer = 0f;
    }

    private void RotateTowardsOpponent()
    {
        Vector3 up = transform.parent != null ? transform.parent.up : Vector3.up;
        Vector3 direction = Vector3.ProjectOnPlane(opponent.transform.position - transform.position, up);
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    private static bool IsVisible(ObserverBehaviour target)
    {
        return target != null && target.TargetStatus.Status == Status.TRACKED;
    }
}