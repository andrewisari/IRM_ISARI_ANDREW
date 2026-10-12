# Lab 02 – VR Darts (OpenXR + XR Interaction Toolkit)

Unity project: [`VRDarts`](VRDarts) · Unity **6000.3.25f1** · scene: `Assets/Scenes/DartsScene.unity`

## 1. Setup (OpenXR + XR Interaction Toolkit)

| Package | Version |
|---|---|
| OpenXR Plugin (`com.unity.xr.openxr`) | 1.16.1 |
| XR Interaction Toolkit (`com.unity.xr.interaction.toolkit`) | 3.3.2 |
| XR Plug-in Management (`com.unity.xr.management`) | 4.6.1 |
| Input System | 1.20.0 (Active Input Handling = Both) |
| Shader Graph (needed by XRI Starter Assets) | 17.3.0 |

* **XR Plug-in Management → OpenXR** is enabled for **PC (Standalone)** and **Android**, with *Initialize XR on Startup* turned on.
* Interaction profiles: Oculus Touch, Meta Quest Touch Plus, Valve Index, HTC Vive, Khronos Simple (PC). On Android the Meta Quest Support feature is also enabled.
* Imported XRI samples: **Starter Assets** (for the `XR Origin (XR Rig)` prefab, input actions and controller models) and **XR Device Simulator**.

All of this is scripted in [`Assets/Editor/VRDartsSetup.cs`](VRDarts/Assets/Editor/VRDartsSetup.cs). The **VR Darts** menu has *Configure OpenXR* and *Build Darts Scene*, so the setup can be repeated from a clean project.

## 2. XR Device Simulator (testing without a headset)

The scene contains the `XR Device Simulator` prefab. Press Play, then:

| Action | Keys |
|---|---|
| Look around (headset) | hold **Right Mouse** + move mouse |
| Move the headset | **W A S D**, **Q / E** down / up |
| Control the right / left controller | hold **Space** / **Left Shift** (or toggle with **Y** / **T**) and move the mouse |
| **Grab (Select)** | **G** (grip) |
| Trigger / UI click | **Left Mouse** |
| Throw | hold **G**, flick the mouse towards the board, release **G** |
| Reset simulated devices | **V** |

Far grab works too: point a controller ray at a dart on the table and press **G** to pull it into your hand.

## 3. The game: darts

* Five darts lie on the table. Each one has an `XRGrabInteractable` (Interactable) with *Throw On Detach*, and the rig's Near-Far Interactors (Interactors) pick them up through the **Select / Grab** action.
* **Throw event** ([`Dart.cs`](VRDarts/Assets/Scripts/Dart.cs)): on `selectExited` the dart remembers where it was released and starts flying tip-first along its velocity. When it hits the board it sticks in (it becomes kinematic and is parented to the board) and reports the hit. If it lands anywhere else, the throw counts as a miss. Darts go back to the table after 2–3 s.
* **Score** ([`DartBoard.cs`](VRDarts/Assets/Scripts/DartBoard.cs)): the points depend on **how far the hit is from the centre of the board**:

  | Distance from centre (× board radius 25 cm) | ≤ 0.06 (bull) | ≤ 0.14 | ≤ 0.26 | ≤ 0.38 | ≤ 0.50 | ≤ 0.62 | ≤ 0.74 | ≤ 0.87 | ≤ 1.00 |
  |---|---|---|---|---|---|---|---|---|---|
  | Points | 50 | 25 | 20 | 15 | 12 | 10 | 7 | 5 | 2 |

  The **player's position** gives a multiplier: the throw distance is measured on the floor from the release point to the board. There's ×1 up close, **×1.5 behind the yellow line (2 m)** and **×2 behind the red line (3 m)**. A hit off the scoring area scores 0.
* **UI** ([`ScoreManager.cs`](VRDarts/Assets/Scripts/ScoreManager.cs)): a world-space scoreboard shows the total score, the last throw (ring, points × multiplier, distance), darts thrown, hits and best throw. It has a **RESET** button you can click with the XR ray (`TrackedDeviceGraphicRaycaster` + `XRUIInputModule`).

## 4. Bonus effects

* The ring that was hit flashes to white with an emissive glow.
* A particle burst appears at the impact point in the ring's colour, with a large yellow burst for a bullseye.
* A floating "+points / ring name" text rises, faces the player and fades out ([`ScorePopup.cs`](VRDarts/Assets/Scripts/ScorePopup.cs)).
* A procedurally generated "thunk + ping" sound plays as 3D audio, higher-pitched for a bullseye.
* Darts align with their flight path, stick into the board and return to the table on their own.
