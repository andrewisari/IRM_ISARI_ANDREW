using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

// Records the demo video frames: drives simulated XR devices through a scripted
// sequence (the same input actions a real headset uses) and saves every frame.
// Run with DEMO_OUT=<folder> and -runTests -testPlatform PlayMode.
public class DemoRecorder
{
    const int k_Fps = 30;

    string m_Out;
    int m_Frame;
    readonly List<string> m_Captions = new List<string>();
    RenderTexture m_Target;
    Texture2D m_Readback;
    Camera m_Head;
    Transform m_Offset;
    XRSimulatedHMD m_Hmd;
    XRSimulatedController m_LeftDevice, m_RightDevice;

    Vector3 m_HeadPos;
    Quaternion m_HeadRot;
    Pose m_Left, m_Right;
    float m_LeftGrip, m_LeftTrigger, m_RightGrip, m_RightTrigger;

    static readonly Pose k_LeftRest = new Pose(new Vector3(-0.17f, 1.3f, 0.36f), Quaternion.Euler(-12f, 18f, 55f));
    static readonly Pose k_RightRest = new Pose(new Vector3(0.17f, 1.3f, 0.36f), Quaternion.Euler(-12f, -18f, -55f));

    [UnityTest, Timeout(3600000)]
    public IEnumerator RecordDemo()
    {
        m_Out = Environment.GetEnvironmentVariable("DEMO_OUT");
        if (string.IsNullOrEmpty(m_Out))
            Assert.Ignore("DEMO_OUT is not set");
        Directory.CreateDirectory(m_Out);
        LogAssert.ignoreFailingMessages = true;

        SceneManager.LoadScene("HandsScene");
        yield return null;
        yield return null;
        var xr = XRGeneralSettings.Instance;
        if (xr != null && xr.Manager != null && xr.Manager.activeLoader != null)
        {
            xr.Manager.StopSubsystems();
            xr.Manager.DeinitializeLoader();
        }
        foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour != null && behaviour.GetType().Name.Contains("Simulator"))
                Object.Destroy(behaviour.gameObject);
        }
        yield return null;

        m_Hmd = InputSystem.AddDevice<XRSimulatedHMD>("Demo HMD");
        m_LeftDevice = InputSystem.AddDevice<XRSimulatedController>("Demo Left");
        InputSystem.SetDeviceUsage(m_LeftDevice, CommonUsages.LeftHand);
        m_RightDevice = InputSystem.AddDevice<XRSimulatedController>("Demo Right");
        InputSystem.SetDeviceUsage(m_RightDevice, CommonUsages.RightHand);

        var origin = Object.FindFirstObjectByType<XROrigin>();
        m_Offset = origin.CameraFloorOffsetObject.transform;
        m_Head = origin.Camera;
        m_Head.fieldOfView = 72f;
        m_Head.nearClipPlane = 0.02f;

        m_Target = new RenderTexture(1280, 720, 24) { antiAliasing = 8 };
        m_Readback = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        Time.captureFramerate = k_Fps;

        m_HeadPos = new Vector3(0f, 1.62f, 0f);
        m_HeadRot = Quaternion.Euler(26f, 0f, 0f);
        m_Left = k_LeftRest;
        m_Right = k_RightRest;
        for (var i = 0; i < 8; i++)
        {
            Push();
            yield return null;
        }

        yield return Intro();
        yield return RightHandPoses();
        yield return LeftHandPoses();
        yield return AnalogBoth();
        yield return GrabAndThrow();
        yield return Outro();

        File.WriteAllLines(Path.Combine(m_Out, "captions.txt"), m_Captions);
        Time.captureFramerate = 0;
    }

    // ------------------------------------------------------------------ segments
    IEnumerator Intro()
    {
        Caption("First-person view: Blender hands attached to both XR controllers");
        yield return Run(2.0f, u =>
        {
            var bob = Mathf.Sin(u * Mathf.PI * 2f) * 0.01f;
            m_Left = Offset(k_LeftRest, new Vector3(0f, bob, 0f));
            m_Right = Offset(k_RightRest, new Vector3(0f, -bob, 0f));
        });
    }

    IEnumerator RightHandPoses()
    {
        Caption("Right hand - Grip button: middle, ring and little finger");
        yield return Press(v => m_RightGrip = v);
        Caption("Right hand - Trigger: index finger");
        yield return Press(v => m_RightTrigger = v);
        Caption("Right hand - Grip + Trigger: fist");
        yield return Press(v => { m_RightGrip = v; m_RightTrigger = v; });
    }

    IEnumerator LeftHandPoses()
    {
        Caption("Left hand (same model, scale x = -1) - Grip");
        yield return Press(v => m_LeftGrip = v);
        Caption("Left hand - Trigger");
        yield return Press(v => m_LeftTrigger = v);
        Caption("Left hand - Grip + Trigger");
        yield return Press(v => { m_LeftGrip = v; m_LeftTrigger = v; });
    }

    IEnumerator AnalogBoth()
    {
        Caption("Analog values blend the poses (2D blend tree: Grip x Trigger)");
        yield return Run(3.5f, u =>
        {
            var a = u * Mathf.PI * 2f;
            m_RightGrip = 0.5f - 0.5f * Mathf.Cos(a);
            m_LeftTrigger = 0.5f - 0.5f * Mathf.Cos(a);
            m_RightTrigger = 0.5f - 0.5f * Mathf.Cos(a * 2f);
            m_LeftGrip = 0.5f - 0.5f * Mathf.Cos(a * 2f);
            var turn = Mathf.Sin(u * Mathf.PI) * 70f;
            m_Left = new Pose(k_LeftRest.position, k_LeftRest.rotation * Quaternion.Euler(0f, 0f, -turn));
            m_Right = new Pose(k_RightRest.position, k_RightRest.rotation * Quaternion.Euler(0f, 0f, turn));
        });
        yield return Run(0.5f, u =>
        {
            m_RightGrip = m_LeftGrip = m_RightTrigger = m_LeftTrigger = 0f;
        });
    }

    IEnumerator GrabAndThrow()
    {
        Caption("Grabbing a dart from Lab 02 - the hand closes on the grip");
        var startHead = m_HeadPos;
        var startHeadRot = m_HeadRot;
        var tableHead = new Vector3(0.3f, 1.6f, -0.05f);
        var tableHeadRot = Quaternion.Euler(42f, 12f, 0f);
        var hover = new Pose(new Vector3(0.45f, 0.95f, 0.31f), Quaternion.Euler(20f, 0f, 90f));
        var leftLow = new Pose(new Vector3(-0.05f, 1.15f, 0.25f), Quaternion.Euler(-10f, 20f, 60f));
        var startLeft = m_Left;
        var startRight = m_Right;
        yield return Run(2.0f, u =>
        {
            var s = Smooth(u);
            m_HeadPos = Vector3.Lerp(startHead, tableHead, s);
            m_HeadRot = Quaternion.Slerp(startHeadRot, tableHeadRot, s);
            m_Left = Lerp(startLeft, leftLow, s);
            m_Right = Lerp(startRight, hover, s);
        });
        yield return Run(0.8f, u => m_RightGrip = Smooth(Mathf.Clamp01(u * 2f)));

        var aim = new Pose(new Vector3(0.3f, 1.45f, 0.1f), Quaternion.Euler(-5f, -5f, 0f));
        var boardHeadRot = Quaternion.Euler(4f, -5f, 0f);
        var lifted = m_Right;
        yield return Run(1.6f, u =>
        {
            var s = Smooth(u);
            m_Right = Lerp(lifted, aim, s);
            m_HeadRot = Quaternion.Slerp(tableHeadRot, boardHeadRot, s);
        });

        Caption("Throw: releasing the grip opens the hand");
        var windup = new Pose(new Vector3(0.33f, 1.4f, -0.15f), Quaternion.Euler(-20f, -5f, 0f));
        yield return Run(0.6f, u => m_Right = Lerp(aim, windup, Smooth(u)));
        var release = new Pose(new Vector3(0.26f, 1.75f, 0.62f), Quaternion.Euler(-5f, -5f, 0f));
        yield return Run(0.2f, u => m_Right = Lerp(windup, release, u));
        m_RightGrip = 0f;
        var follow = new Pose(new Vector3(0.24f, 1.7f, 0.75f), Quaternion.Euler(20f, -5f, 0f));
        yield return Run(0.4f, u => { m_Right = Lerp(release, follow, Smooth(u)); LogDarts(); });
        LogDarts();
        yield return Run(2.2f, u => m_Right = Lerp(follow, k_RightRest, Smooth(Mathf.Clamp01(u * 1.5f))));
    }

    IEnumerator Outro()
    {
        Caption("Lab 03 - VR hands modelled, unwrapped, textured and animated in Blender");
        var startLeft = m_Left;
        var startRight = m_Right;
        var startHead = m_HeadPos;
        var startHeadRot = m_HeadRot;
        yield return Run(3f, u =>
        {
            var s = Smooth(Mathf.Clamp01(u * 2f));
            m_HeadPos = Vector3.Lerp(startHead, new Vector3(0f, 1.62f, 0f), s);
            m_HeadRot = Quaternion.Slerp(startHeadRot, Quaternion.Euler(26f, 0f, 0f), s);
            m_Left = Lerp(startLeft, k_LeftRest, s);
            m_Right = Lerp(startRight, k_RightRest, s);
            var wave = Mathf.Sin(u * Mathf.PI * 3f);
            m_LeftGrip = m_RightGrip = Mathf.Max(0f, wave);
            m_LeftTrigger = m_RightTrigger = Mathf.Max(0f, wave);
        });
    }

    // ------------------------------------------------------------------ helpers
    IEnumerator Press(Action<float> set)
    {
        yield return Run(0.55f, u => set(Smooth(u)));
        yield return Run(0.6f, u => set(1f));
        yield return Run(0.45f, u => set(1f - Smooth(u)));
        yield return Run(0.15f, u => set(0f));
    }

    IEnumerator Run(float seconds, Action<float> update)
    {
        var frames = Mathf.Max(1, Mathf.RoundToInt(seconds * k_Fps));
        for (var i = 1; i <= frames; i++)
        {
            update(i / (float)frames);
            Push();
            yield return null;
            Capture();
        }
    }

    void Caption(string text)
    {
        m_Captions.Add(m_Frame + "\t" + text);
    }

    void Push()
    {
        var headLocal = m_Offset.InverseTransformPoint(m_HeadPos);
        var headRotLocal = Quaternion.Inverse(m_Offset.rotation) * m_HeadRot;
        var hmd = new XRSimulatedHMDState
        {
            centerEyePosition = headLocal, centerEyeRotation = headRotLocal,
            devicePosition = headLocal, deviceRotation = headRotLocal,
            leftEyePosition = headLocal, leftEyeRotation = headRotLocal,
            rightEyePosition = headLocal, rightEyeRotation = headRotLocal,
            isTracked = true, trackingState = 3,
        };
        InputSystem.QueueStateEvent(m_Hmd, hmd);
        InputSystem.QueueStateEvent(m_LeftDevice, Controller(m_Left, m_LeftGrip, m_LeftTrigger));
        InputSystem.QueueStateEvent(m_RightDevice, Controller(m_Right, m_RightGrip, m_RightTrigger));
    }

    XRSimulatedControllerState Controller(Pose pose, float grip, float trigger)
    {
        var state = new XRSimulatedControllerState
        {
            devicePosition = m_Offset.InverseTransformPoint(pose.position),
            deviceRotation = Quaternion.Inverse(m_Offset.rotation) * pose.rotation,
            grip = grip, trigger = trigger, isTracked = true, trackingState = 3,
        };
        return state.WithButton(ControllerButton.GripButton, grip > 0.55f)
            .WithButton(ControllerButton.TriggerButton, trigger > 0.55f);
    }

    void Capture()
    {
        var previous = m_Head.targetTexture;
        m_Head.targetTexture = m_Target;
        m_Head.Render();
        m_Head.targetTexture = previous;
        RenderTexture.active = m_Target;
        m_Readback.ReadPixels(new Rect(0, 0, m_Target.width, m_Target.height), 0, 0);
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(m_Out, $"frame_{m_Frame:D5}.jpg"), m_Readback.EncodeToJPG(93));
        m_Frame++;
    }

    static void LogDarts()
    {
        foreach (var body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body.name.StartsWith("Dart") && body.linearVelocity.sqrMagnitude > 0.25f)
                Debug.Log($"[Demo] {body.name} pos={body.position} vel={body.linearVelocity}");
        }
    }

    static float Smooth(float u) => u * u * (3f - 2f * u);

    static float Pulse(float phase, float offset)
    {
        var x = Mathf.Repeat(phase - offset, 1f);
        return Mathf.Clamp01(Mathf.Sin(x * Mathf.PI) * 1.6f);
    }

    static Pose Lerp(Pose a, Pose b, float t) =>
        new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));

    static Pose Offset(Pose p, Vector3 delta) => new Pose(p.position + delta, p.rotation);
}
