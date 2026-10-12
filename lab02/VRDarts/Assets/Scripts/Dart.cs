using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// A throwable dart. It is grabbed through an XRGrabInteractable (Select / Grip),
/// flies tip-first after release, sticks into the DartBoard and reports the hit.
/// Darts that miss, or that have been scored, return to their spawn point.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
public class Dart : MonoBehaviour
{
    [Tooltip("Distance from the pivot to the tip of the dart along +Z.")]
    public float tipOffset = 0.1f;

    [Tooltip("How deep the tip sinks into the board when it sticks.")]
    public float penetration = 0.02f;

    [Tooltip("Minimum speed needed for the dart to stick into the board.")]
    public float minStickSpeed = 1.0f;

    [Tooltip("Seconds a scored dart stays in the board before returning.")]
    public float stuckReturnDelay = 3f;

    [Tooltip("Seconds a missed dart lies around before returning.")]
    public float missReturnDelay = 2f;

    Rigidbody m_Body;
    XRGrabInteractable m_Grab;
    Transform m_SpawnParent;
    Vector3 m_SpawnPosition;
    Quaternion m_SpawnRotation;
    Vector3 m_LastVelocity;
    Vector3 m_ReleasePosition;
    bool m_InFlight;
    bool m_Landed;

    void Awake()
    {
        m_Body = GetComponent<Rigidbody>();
        m_Grab = GetComponent<XRGrabInteractable>();
        m_SpawnParent = transform.parent;
        m_SpawnPosition = transform.position;
        m_SpawnRotation = transform.rotation;
    }

    void OnEnable()
    {
        m_Grab.selectEntered.AddListener(OnGrabbed);
        m_Grab.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        m_Grab.selectEntered.RemoveListener(OnGrabbed);
        m_Grab.selectExited.RemoveListener(OnReleased);
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        StopAllCoroutines();
        m_InFlight = false;
        m_Landed = false;
    }

    void OnReleased(SelectExitEventArgs args)
    {
        // The throw event: remember where the player released the dart from,
        // the board uses it for the distance multiplier.
        m_ReleasePosition = transform.position;
        m_InFlight = true;
        m_Landed = false;
    }

    void FixedUpdate()
    {
        if (!m_InFlight || m_Body.isKinematic)
            return;

        m_LastVelocity = m_Body.linearVelocity;

        // Keep the tip pointing along the flight path, like a real dart.
        if (m_LastVelocity.sqrMagnitude > 0.5f)
        {
            m_Body.MoveRotation(Quaternion.LookRotation(m_LastVelocity.normalized, Vector3.up));
            m_Body.angularVelocity = Vector3.zero;
        }

        if (transform.position.y < -10f)
            ReturnToSpawn();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!m_InFlight || m_Landed)
            return;

        var board = collision.collider.GetComponentInParent<DartBoard>();
        if (board != null && m_LastVelocity.magnitude >= minStickSpeed)
        {
            StickInto(board, collision.GetContact(0).point);
            return;
        }

        // Hit the floor, a wall, or the board too slowly: it is a miss.
        m_Landed = true;
        m_InFlight = false;
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RegisterMiss();
        StartCoroutine(ReturnAfter(missReturnDelay));
    }

    void StickInto(DartBoard board, Vector3 contactPoint)
    {
        m_Landed = true;
        m_InFlight = false;

        var direction = m_LastVelocity.sqrMagnitude > 0.01f ? m_LastVelocity.normalized : transform.forward;
        m_Body.isKinematic = true;
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.position = contactPoint - direction * (tipOffset - penetration);
        transform.SetParent(board.transform, true);

        // A dart stuck in the board can't be grabbed until it comes back to the table.
        m_Grab.enabled = false;

        board.RegisterHit(contactPoint, m_ReleasePosition);
        StartCoroutine(ReturnAfter(stuckReturnDelay));
    }

    IEnumerator ReturnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnToSpawn();
    }

    public void ReturnToSpawn()
    {
        StopAllCoroutines();
        m_InFlight = false;
        m_Landed = false;

        transform.SetParent(m_SpawnParent, true);
        m_Body.isKinematic = false;
        m_Body.linearVelocity = Vector3.zero;
        m_Body.angularVelocity = Vector3.zero;
        m_Body.position = m_SpawnPosition;
        m_Body.rotation = m_SpawnRotation;
        transform.SetPositionAndRotation(m_SpawnPosition, m_SpawnRotation);
        m_Grab.enabled = true;
    }
}
