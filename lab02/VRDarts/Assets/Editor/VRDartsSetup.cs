using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

/// <summary>
/// One-click project setup: configures OpenXR in XR Plug-in Management and builds the darts scene.
/// Menu: VR Darts > Configure OpenXR / Build Darts Scene. Batch mode: -executeMethod VRDartsSetup.SetupAll
/// </summary>
public static class VRDartsSetup
{
    const string k_ScenePath = "Assets/Scenes/DartsScene.unity";
    const string k_MaterialsFolder = "Assets/Materials";
    const string k_SamplesRoot = "Assets/Samples/XR Interaction Toolkit/3.3.2";
    const string k_XROriginPrefab = k_SamplesRoot + "/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string k_SimulatorPrefab = k_SamplesRoot + "/XR Device Simulator/XR Device Simulator.prefab";

    static readonly Vector3 k_BoardCenter = new Vector3(0f, 1.6f, 2.6f);
    const float k_BoardRadius = 0.25f;

    public static void SetupAll()
    {
        ConfigureOpenXR();
        BuildScene();
    }

    // ---------------------------------------------------------------- XR settings

    [MenuItem("VR Darts/Configure OpenXR")]
    public static void ConfigureOpenXR()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        // OpenXR package settings are created lazily by the package's editor UI, so create them explicitly.
        var packageSettingsType = typeof(FeatureHelpers).Assembly.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings");
        packageSettingsType?.GetMethod("GetOrCreateInstance")?.Invoke(null, null);

        foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            if (!perTarget.HasSettingsForBuildTarget(group))
                perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(general.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.AssignedSettings);

            FeatureHelpers.RefreshFeatures(group);
            var openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openXR == null)
                continue;

            EnableFeature<OculusTouchControllerProfile>(openXR);
            EnableFeature<MetaQuestTouchPlusControllerProfile>(openXR);
            if (group == BuildTargetGroup.Standalone)
            {
                EnableFeature<KHRSimpleControllerProfile>(openXR);
                EnableFeature<ValveIndexControllerProfile>(openXR);
                EnableFeature<HTCViveControllerProfile>(openXR);
            }
            else
            {
                EnableFeature<MetaQuestFeature>(openXR);
            }
            EditorUtility.SetDirty(openXR);
        }

        EditorUtility.SetDirty(perTarget);
        AssetDatabase.SaveAssets();
        Debug.Log("[VRDarts] OpenXR configured (Standalone + Android).");
    }

    static void EnableFeature<T>(OpenXRSettings settings) where T : OpenXRFeature
    {
        var feature = settings.GetFeature<T>();
        if (feature == null)
            return;
        feature.enabled = true;
        EditorUtility.SetDirty(feature);
    }

    // ---------------------------------------------------------------- Scene

    [MenuItem("VR Darts/Build Darts Scene")]
    public static void BuildScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory(k_MaterialsFolder);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Lighting
        RenderSettings.ambientMode = AmbientMode.Skybox;
        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.0f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        var spot = new GameObject("Board Spotlight").AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 6f;
        spot.spotAngle = 45f;
        spot.intensity = 1.6f;
        spot.color = new Color(1f, 0.95f, 0.85f);
        spot.shadows = LightShadows.Soft;
        spot.transform.position = new Vector3(0f, 2.9f, 1.4f);
        spot.transform.LookAt(k_BoardCenter);

        // XR core: interaction manager, rig, simulator, XR-aware event system
        new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

        var rig = InstantiatePrefab(k_XROriginPrefab);
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var xrCamera = rig.GetComponentInChildren<Camera>();

        InstantiatePrefab(k_SimulatorPrefab);

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        eventSystem.transform.SetSiblingIndex(0);

        BuildEnvironment(font);
        var board = BuildBoard(font);
        BuildTableAndDarts();
        BuildScoreboard(font, xrCamera, out var scoreManager);

        EditorSceneManager.SaveScene(scene, k_ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_ScenePath, true) };
        AssetDatabase.SaveAssets();
        Selection.activeObject = board;
        Debug.Log("[VRDarts] Scene built at " + k_ScenePath);
    }

    static GameObject InstantiatePrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new FileNotFoundException("Missing prefab (import the XR Interaction Toolkit samples): " + path);
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    static void BuildEnvironment(Font font)
    {
        var root = new GameObject("Environment").transform;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(root);
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        floor.GetComponent<Renderer>().sharedMaterial = Mat("Floor", new Color(0.36f, 0.25f, 0.18f), 0.3f);

        var wall = CreateBox("Back Wall", root, new Vector3(0f, 2f, 2.85f), new Vector3(8f, 4f, 0.2f), Mat("Wall", new Color(0.22f, 0.28f, 0.35f)));
        wall.isStatic = true;
        CreateBox("Left Wall", root, new Vector3(-3.5f, 2f, 0f), new Vector3(0.2f, 4f, 6f), Mat("Wall"));
        CreateBox("Right Wall", root, new Vector3(3.5f, 2f, 0f), new Vector3(0.2f, 4f, 6f), Mat("Wall"));

        // Throw distance zones: the further back the player throws from, the higher the multiplier.
        foreach (var zone in DartBoard.DefaultZones)
        {
            if (zone.minDistance <= 0f)
                continue;
            var z = k_BoardCenter.z - zone.minDistance;
            var color = zone.multiplier >= 2f ? new Color(0.95f, 0.25f, 0.2f) : new Color(1f, 0.75f, 0.1f);
            var line = CreateBox($"Throw Line {zone.minDistance:0.#}m", root, new Vector3(0f, 0.003f, z), new Vector3(3f, 0.006f, 0.05f),
                Mat($"Line x{zone.multiplier:0.#}", color, 0.2f, color * 0.6f));
            Object.DestroyImmediate(line.GetComponent<Collider>());

            var label = CreateWorldCanvas($"Zone Label x{zone.multiplier:0.#}", root, new Vector3(-1.1f, 0.005f, z - 0.2f),
                Quaternion.Euler(90f, 0f, 0f), new Vector2(400, 120), 0.002f);
            AddText(label.transform, "Label", font, $"x{zone.multiplier:0.#} behind this line", 44, color, TextAnchor.MiddleCenter, FontStyle.Bold);
        }
    }

    static GameObject BuildBoard(Font font)
    {
        var boardGo = new GameObject("DartBoard");
        boardGo.transform.SetPositionAndRotation(k_BoardCenter, Quaternion.Euler(0f, 180f, 0f)); // +Z faces the player
        var board = boardGo.AddComponent<DartBoard>();
        board.radius = k_BoardRadius;
        board.popupFont = font;

        // Cabinet / backing. Its front face is flush with the rings, darts stick into it.
        var cabinet = CreateBox("Cabinet", boardGo.transform, new Vector3(0f, 0f, -0.03f), new Vector3(0.8f, 0.8f, 0.06f),
            Mat("Cabinet", new Color(0.25f, 0.13f, 0.06f), 0.4f));
        Object.DestroyImmediate(cabinet.GetComponent<Collider>());
        var faceCollider = boardGo.AddComponent<BoxCollider>();
        faceCollider.center = new Vector3(0f, 0f, -0.02f);
        faceCollider.size = new Vector3(0.8f, 0.8f, 0.07f); // front face at z = 0.015, level with the rings

        var surround = CreateDisc("Number Ring", boardGo.transform, k_BoardRadius * 1.22f, 0.025f, Mat("Board Surround", new Color(0.05f, 0.05f, 0.05f)));
        surround.transform.localPosition = new Vector3(0f, 0f, -0.01f);

        // Concentric scoring rings, outermost first, each slightly in front of the previous one.
        var rings = DartBoard.DefaultRings;
        board.rings = (DartBoard.Ring[])rings.Clone();
        board.ringRenderers = new Renderer[rings.Length];
        for (var i = rings.Length - 1; i >= 0; i--)
        {
            var depth = 0.007f + (rings.Length - 1 - i) * 0.0012f;
            var mat = Mat("Ring " + i, rings[i].color, 0.25f, Color.black);
            var disc = CreateDisc(rings[i].name, boardGo.transform, k_BoardRadius * rings[i].outerRadius, depth * 2f, mat);
            board.ringRenderers[i] = disc.GetComponent<Renderer>();
        }

        // Point values printed around the board.
        var legend = CreateWorldCanvas("Board Legend", boardGo.transform, new Vector3(0f, k_BoardRadius * 1.22f + 0.1f, 0.02f),
            Quaternion.Euler(0f, 180f, 0f), new Vector2(800, 80), 0.002f);
        AddText(legend.transform, "Text", font, "Bull 50 · 25 · 20 · 15 · 12 · 10 · 7 · 5 · 2", 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

        // Hit particles, re-positioned and emitted by the board on each hit.
        var particlesGo = new GameObject("Hit Particles");
        particlesGo.transform.SetParent(boardGo.transform, false);
        var ps = particlesGo.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.025f);
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1000;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.01f;
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;
        particlesGo.GetComponent<ParticleSystemRenderer>().sharedMaterial =
            AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
        board.hitParticles = ps;

        var audio = boardGo.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = 1f;
        audio.minDistance = 2f;
        board.audioSource = audio;

        return boardGo;
    }

    static void BuildTableAndDarts()
    {
        var table = new GameObject("Dart Table").transform;
        table.position = new Vector3(0.45f, 0f, 0.35f);
        var wood = Mat("Table Wood", new Color(0.45f, 0.3f, 0.18f), 0.35f);
        CreateBox("Top", table, new Vector3(0f, 0.85f, 0f), new Vector3(0.6f, 0.04f, 0.4f), wood);
        foreach (var x in new[] { -0.26f, 0.26f })
        foreach (var z in new[] { -0.16f, 0.16f })
            CreateBox("Leg", table, new Vector3(x, 0.415f, z), new Vector3(0.04f, 0.83f, 0.04f), wood);

        var flightColors = new[]
        {
            new Color(0.9f, 0.15f, 0.15f), new Color(0.15f, 0.45f, 0.95f), new Color(0.15f, 0.8f, 0.3f),
            new Color(0.95f, 0.8f, 0.1f), new Color(0.75f, 0.25f, 0.9f),
        };
        var darts = new GameObject("Darts").transform;
        for (var i = 0; i < flightColors.Length; i++)
        {
            var position = table.position + new Vector3(-0.22f + i * 0.11f, 0.89f, 0f);
            CreateDart($"Dart {i + 1}", darts, position, flightColors[i]);
        }
    }

    static void CreateDart(string name, Transform parent, Vector3 position, Color flightColor)
    {
        var dart = new GameObject(name);
        dart.transform.SetParent(parent);
        dart.transform.SetPositionAndRotation(position, Quaternion.identity); // tip points along +Z
        dart.layer = 0;

        var metal = Mat("Dart Metal", new Color(0.8f, 0.8f, 0.85f), 0.85f, null, 0.9f);
        var barrel = Mat("Dart Barrel", new Color(0.15f, 0.15f, 0.17f), 0.7f, null, 0.8f);
        var flight = Mat("Flight " + ColorUtility.ToHtmlStringRGB(flightColor), flightColor, 0.3f);

        // Visual parts (no colliders); a single capsule + tail box on the root handle physics.
        CreateVisual(PrimitiveType.Cylinder, "Tip", dart.transform, new Vector3(0f, 0f, 0.07f), new Vector3(0.004f, 0.03f, 0.004f), Quaternion.Euler(90f, 0f, 0f), metal);
        CreateVisual(PrimitiveType.Capsule, "Barrel", dart.transform, new Vector3(0f, 0f, 0.015f), new Vector3(0.016f, 0.04f, 0.016f), Quaternion.Euler(90f, 0f, 0f), barrel);
        CreateVisual(PrimitiveType.Cylinder, "Shaft", dart.transform, new Vector3(0f, 0f, -0.04f), new Vector3(0.006f, 0.03f, 0.006f), Quaternion.Euler(90f, 0f, 0f), flight);
        CreateVisual(PrimitiveType.Cube, "Flight A", dart.transform, new Vector3(0f, 0f, -0.075f), new Vector3(0.04f, 0.002f, 0.035f), Quaternion.identity, flight);
        CreateVisual(PrimitiveType.Cube, "Flight B", dart.transform, new Vector3(0f, 0f, -0.075f), new Vector3(0.002f, 0.04f, 0.035f), Quaternion.identity, flight);

        var capsule = dart.AddComponent<CapsuleCollider>();
        capsule.direction = 2;
        capsule.radius = 0.01f;
        capsule.height = 0.2f;
        var tail = dart.AddComponent<BoxCollider>(); // stops the dart rolling off the table
        tail.center = new Vector3(0f, 0f, -0.075f);
        tail.size = new Vector3(0.04f, 0.04f, 0.035f);

        var body = dart.AddComponent<Rigidbody>();
        body.mass = 0.1f;
        body.linearDamping = 0.05f;
        body.angularDamping = 2f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var attach = new GameObject("Attach").transform;
        attach.SetParent(dart.transform, false);
        attach.localPosition = new Vector3(0f, 0f, 0.0f);

        var grab = dart.AddComponent<XRGrabInteractable>();
        grab.attachTransform = attach;
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        grab.throwOnDetach = true;
        grab.throwVelocityScale = 1.5f;
        grab.throwAngularVelocityScale = 0.5f;
        grab.useDynamicAttach = false;

        var dartScript = dart.AddComponent<Dart>();
        dartScript.tipOffset = 0.1f;
    }

    static void BuildScoreboard(Font font, Camera eventCamera, out ScoreManager scoreManager)
    {
        var canvasGo = CreateWorldCanvas("Scoreboard", null, new Vector3(-1.05f, 1.7f, 2.4f), Quaternion.Euler(0f, -24f, 0f),
            new Vector2(600, 440), 0.002f);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.worldCamera = eventCamera;
        canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();

        var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(canvasGo.transform, false);
        Stretch(background.GetComponent<RectTransform>());
        var bgImage = background.GetComponent<Image>();
        bgImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
        bgImage.type = Image.Type.Sliced;
        bgImage.color = new Color(0.05f, 0.07f, 0.12f, 0.9f);

        var title = AddText(canvasGo.transform, "Title", font, "VR DARTS", 46, new Color(1f, 0.8f, 0.2f), TextAnchor.UpperCenter, FontStyle.Bold);
        Place(title.rectTransform, 0f, -18f, 560, 60);
        var total = AddText(canvasGo.transform, "Total", font, "SCORE: 0", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Place(total.rectTransform, 0f, -90f, 560, 80);
        var last = AddText(canvasGo.transform, "Last Throw", font, "Grab a dart and throw it at the board!", 28, new Color(0.7f, 0.95f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal);
        Place(last.rectTransform, 0f, -170f, 560, 70);
        var stats = AddText(canvasGo.transform, "Stats", font, "Darts thrown: 0   Hits: 0   Best throw: 0", 24, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Normal);
        Place(stats.rectTransform, 0f, -230f, 560, 40);
        var rules = AddText(canvasGo.transform, "Rules", font,
            "Closer to the centre = more points.\nThrow from behind the floor lines for x1.5 / x2.", 20,
            new Color(0.6f, 0.65f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Italic);
        Place(rules.rectTransform, 0f, -285f, 560, 60);

        var buttonGo = new GameObject("Reset Button", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(canvasGo.transform, false);
        Place(buttonGo.GetComponent<RectTransform>(), 0f, -360f, 220, 60);
        var buttonImage = buttonGo.GetComponent<Image>();
        buttonImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        buttonImage.type = Image.Type.Sliced;
        buttonImage.color = new Color(0.85f, 0.2f, 0.2f);
        var buttonText = AddText(buttonGo.transform, "Text", font, "RESET", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        Stretch(buttonText.rectTransform);

        scoreManager = new GameObject("Score Manager").AddComponent<ScoreManager>();
        scoreManager.totalText = total;
        scoreManager.lastThrowText = last;
        scoreManager.statsText = stats;
        UnityEventTools.AddPersistentListener(buttonGo.GetComponent<Button>().onClick, scoreManager.ResetScore);
    }

    // ---------------------------------------------------------------- Helpers

    static Material Mat(string name, Color? color = null, float smoothness = 0.2f, Color? emission = null, float metallic = 0f)
    {
        var path = $"{k_MaterialsFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }
        if (color.HasValue)
            mat.color = color.Value;
        mat.SetFloat("_Glossiness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static GameObject CreateDisc(string name, Transform parent, float radius, float thickness, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis along the board normal
        go.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static void CreateVisual(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
    }

    static GameObject CreateWorldCanvas(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size, float scale)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(parent, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = Vector3.one * scale;
        go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        return go;
    }

    static Text AddText(Transform parent, string name, Font font, string content, int size, Color color, TextAnchor anchor, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        var text = go.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.fontStyle = style;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
