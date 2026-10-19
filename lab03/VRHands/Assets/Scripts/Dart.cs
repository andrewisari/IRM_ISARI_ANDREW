using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
public class Dart : MonoBehaviour
{
    public float tipOffset = 0.1f;
    public float penetration = 0.02f;
    public float minStickSpeed = 1.0f;
    public float stuckReturnDelay = 3f;
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
        m_ReleasePosition = transform.position;
        m_InFlight = true;
        m_Landed = false;
    }

    void FixedUpdate()
    {
        if (!m_InFlight || m_Body.isKinematic)
            return;

        m_LastVelocity = m_Body.linearVelocity;

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
