using UnityEngine;
using Vuforia;


public class CharacterProximity : MonoBehaviour
{
    private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");

    [Header("References")]
    [SerializeField] private Transform opponent;
    [SerializeField] private ObserverBehaviour ownTarget;
    [SerializeField] private ObserverBehaviour opponentTarget;

    [Header("Distances (meters)")]
    [SerializeField, Min(0f)] private float attackDistance = 0.25f;
    [SerializeField, Min(0f)] private float releaseDistance = 0.30f;

    [Header("Behaviour")]
    [SerializeField] private bool faceOpponent = true;
    [SerializeField, Min(0f)] private float turnSpeed = 5f;

    private Animator animator;
    private bool isAttacking;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null)
            Debug.LogError($"{name}: no Animator found in children.", this);
    }

    private void Update()
    {
        if (animator == null || opponent == null) return;

        bool bothVisible = IsVisible(ownTarget) && IsVisible(opponentTarget);
        float distance = Vector3.Distance(transform.position, opponent.position);
        Debug.Log($"{name} distance: {distance:F3}");

        bool shouldAttack = bothVisible &&
                            (isAttacking ? distance < releaseDistance
                                         : distance < attackDistance);

        if (shouldAttack != isAttacking)
        {
            isAttacking = shouldAttack;
            animator.SetBool(IsAttackingHash, isAttacking);
        }

        if (faceOpponent && isAttacking) RotateTowardsOpponent();
    }

    private void RotateTowardsOpponent()
    {
        Vector3 up = transform.parent != null ? transform.parent.up : Vector3.up;
        Vector3 direction = Vector3.ProjectOnPlane(opponent.position - transform.position, up);
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
    }

    private static bool IsVisible(ObserverBehaviour target)
    {
        return target != null && target.TargetStatus.Status == Status.TRACKED;
    }
}