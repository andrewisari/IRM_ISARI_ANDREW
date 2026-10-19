using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class VRHandsSetup
{
    const string k_SamplesRoot = "Assets/Samples/XR Interaction Toolkit/3.3.2";
    const string k_InputActions = k_SamplesRoot + "/Starter Assets/XRI Default Input Actions.inputactions";
    const string k_ControllerModel = k_SamplesRoot + "/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";
    const string k_ModelPath = "Assets/Models/Hand.fbx";
    const string k_TexturePath = "Assets/Models/Textures/Hand_Albedo.png";
    const string k_MaterialPath = "Assets/Models/Hand_Skin.mat";
    const string k_AnimatorPath = "Assets/Animations/Hand.controller";
    const string k_RightPrefab = "Assets/Prefabs/Right Hand.prefab";
    const string k_LeftPrefab = "Assets/Prefabs/Left Hand.prefab";
    const string k_DartsScene = "Assets/Scenes/DartsScene.unity";
    const string k_HandsScene = "Assets/Scenes/HandsScene.unity";

    static readonly string[] k_Clips = { "Idle", "Grip", "Trigger", "Fist" };

    // Pose of the right hand model inside the controller: fingers forward and a little
    // down, back of the hand facing out (+X), controller origin just under the palm.
    static readonly Vector3 k_FingerDirection = new Vector3(0f, -0.3f, 1f);
    static readonly Vector3 k_BackDirection = new Vector3(1f, 0f, 0f);
    static readonly Vector3 k_GripPointInModel = new Vector3(0f, -0.03f, 0.055f);

    public static void SetupAllAndPreview()
    {
        SetupAll();
        RenderAlignmentPreview();
    }

    [MenuItem("VR Hands/Setup Everything")]
    public static void SetupAll()
    {
        ConfigureModelImport();
        var material = CreateMaterial();
        var controller = CreateAnimator();
        CreateHandPrefabs(material, controller);
        BuildScene();
    }

    [MenuItem("VR Hands/1. Configure Hand Import")]
    public static void ConfigureModelImport()
    {
        AssetDatabase.ImportAsset(k_ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(k_ModelPath);
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;

        var clips = new List<ModelImporterClipAnimation>();
        foreach (var take in importer.importedTakeInfos)
        {
            var name = take.name.Substring(take.name.LastIndexOf('|') + 1);
            clips.Add(new ModelImporterClipAnimation
            {
                name = name,
                takeName = take.name,
                firstFrame = take.startTime * take.sampleRate,
                lastFrame = take.stopTime * take.sampleRate,
                loopTime = false,
            });
        }
        importer.clipAnimations = clips.ToArray();
        importer.SaveAndReimport();
        Debug.Log("[VRHands] Hand.fbx clips: " + string.Join(", ", clips.Select(c => c.name)));
    }

    static Material CreateMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, k_MaterialPath);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
        material.color = new Color(0.84f, 0.76f, 0.72f);
        material.SetFloat("_Glossiness", 0.25f);
        material.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static AnimationClip Clip(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(k_ModelPath).OfType<AnimationClip>()
            .First(c => c.name == name);
    }

    [MenuItem("VR Hands/2. Create Hand Animator")]
    public static AnimatorController CreateAnimator()
    {
        Directory.CreateDirectory("Assets/Animations");
        AssetDatabase.DeleteAsset(k_AnimatorPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(k_AnimatorPath);
        controller.AddParameter("Grip", AnimatorControllerParameterType.Float);
        controller.AddParameter("Trigger", AnimatorControllerParameterType.Float);

        controller.CreateBlendTreeInController("Hand Pose", out var tree, 0);
        tree.blendType = BlendTreeType.FreeformCartesian2D;
        tree.blendParameter = "Grip";
        tree.blendParameterY = "Trigger";
        tree.AddChild(Clip("Idle"), new Vector2(0f, 0f));
        tree.AddChild(Clip("Grip"), new Vector2(1f, 0f));
        tree.AddChild(Clip("Trigger"), new Vector2(0f, 1f));
        tree.AddChild(Clip("Fist"), new Vector2(1f, 1f));
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static InputActionReference ActionReference(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(k_InputActions).OfType<InputActionReference>()
            .First(r => r.name == path);
    }

    static void CreateHandPrefabs(Material material, AnimatorController controller)
    {
        Directory.CreateDirectory("Assets/Prefabs");
        CreateHandPrefab(k_RightPrefab, "Right", false, material, controller);
        CreateHandPrefab(k_LeftPrefab, "Left", true, material, controller);
    }

    static void CreateHandPrefab(string path, string side, bool mirror, Material material, AnimatorController controller)
    {
        var root = new GameObject(side + " Hand");
        // the assignment asks for the same model on both controllers, mirrored with scale x = -1
        root.transform.localScale = mirror ? new Vector3(-1f, 1f, 1f) : Vector3.one;

        var offset = new GameObject("Offset").transform;
        offset.SetParent(root.transform, false);
        var rotation = Quaternion.LookRotation(k_FingerDirection.normalized, k_BackDirection);
        offset.localRotation = rotation;
        offset.localPosition = -(rotation * k_GripPointInModel);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(k_ModelPath));
        model.name = "Hand Model";
        model.transform.SetParent(offset, false);
        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            renderer.sharedMaterial = material;
            renderer.updateWhenOffscreen = true;
        }

        var animator = model.GetComponent<Animator>();
        if (animator == null)
            animator = model.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        var input = root.AddComponent<HandAnimatorInput>();
        input.Configure(ActionReference($"XRI {side} Interaction/Select Value"),
            ActionReference($"XRI {side} Interaction/Activate Value"), animator);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    [MenuItem("VR Hands/3. Build Hands Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.OpenScene(k_DartsScene, OpenSceneMode.Single);
        HandAnimatorInput left = null, right = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name != "Left Controller" && t.name != "Right Controller")
                continue;
            var isLeft = t.name.StartsWith("Left");
            var visual = t.Find(isLeft ? "Left Controller Visual" : "Right Controller Visual");
            if (visual != null)
                visual.gameObject.SetActive(false);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(isLeft ? k_LeftPrefab : k_RightPrefab);
            var hand = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t);
            hand.transform.localPosition = Vector3.zero;
            hand.transform.localRotation = Quaternion.identity;
            if (isLeft)
                left = hand.GetComponent<HandAnimatorInput>();
            else
                right = hand.GetComponent<HandAnimatorInput>();
        }

        BuildInputPanel(left, right);

        EditorSceneManager.SaveScene(scene, k_HandsScene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(k_HandsScene, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[VRHands] Scene built at " + k_HandsScene);
    }

    static void BuildInputPanel(HandAnimatorInput left, HandAnimatorInput right)
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var canvasGo = new GameObject("Hand Input Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520, 300);
        canvasGo.transform.SetPositionAndRotation(new Vector3(0.45f, 1.2f, 0.75f), Quaternion.Euler(-20f, 0f, 0f));
        canvasGo.transform.localScale = Vector3.one * 0.001f;

        var background = Panel(canvasGo.transform, "Background", new Color(0.05f, 0.07f, 0.12f, 0.88f));
        Stretch(background.rectTransform);
        var title = Label(canvasGo.transform, font, "HAND INPUT", 34, new Color(1f, 0.8f, 0.2f), TextAnchor.MiddleCenter);
        Place(title.rectTransform, 0, -12, 500, 48);

        var labels = new[] { "L  Grip", "L  Trigger", "R  Grip", "R  Trigger" };
        var bars = new Image[4];
        var values = new Text[4];
        for (var i = 0; i < 4; i++)
        {
            var y = -72 - i * 54;
            Place(Label(canvasGo.transform, font, labels[i], 26, Color.white, TextAnchor.MiddleLeft).rectTransform, -150, y, 170, 26);
            var track = Panel(canvasGo.transform, "Track " + i, new Color(1f, 1f, 1f, 0.12f));
            Place(track.rectTransform, 60, y, 250, 26);
            bars[i] = Panel(track.transform, "Fill", Color.white);
            Stretch(bars[i].rectTransform);
            bars[i].sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            bars[i].type = Image.Type.Filled;
            bars[i].fillMethod = Image.FillMethod.Horizontal;
            bars[i].fillAmount = 0f;
            values[i] = Label(canvasGo.transform, font, "0%", 24, new Color(0.8f, 0.9f, 1f), TextAnchor.MiddleRight);
            Place(values[i].rectTransform, 222, y, 70, 26);
        }
        canvasGo.AddComponent<HandInputPanel>().Configure(left, right, bars, values);
    }

    static Image Panel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    static Text Label(Transform parent, Font font, string text, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(text, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = anchor;
        label.fontStyle = FontStyle.Bold;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
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

    // Renders both hands next to a controller model to check the alignment (batch mode helper).
    public static void RenderAlignmentPreview()
    {
        var outDir = Path.GetFullPath("../previews");
        Directory.CreateDirectory(outDir);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var right = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(k_RightPrefab));
        right.transform.position = new Vector3(0.12f, 0f, 0f);
        var left = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(k_LeftPrefab));
        left.transform.position = new Vector3(-0.12f, 0f, 0f);
        foreach (var p in new[] { right.transform.position, left.transform.position })
        {
            var c = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(k_ControllerModel));
            c.transform.position = p;
        }
        var cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.8f, 0.82f, 0.85f);
        cam.nearClipPlane = 0.01f;
        cam.fieldOfView = 40f;
        var views = new Dictionary<string, Vector3>
        {
            { "first_person", new Vector3(0f, 0.25f, -0.45f) }, { "top", new Vector3(0f, 0.6f, 0.001f) },
            { "side", new Vector3(0.6f, 0.05f, 0f) }, { "front", new Vector3(0f, 0.08f, 0.6f) },
        };
        var rt = new RenderTexture(900, 700, 24);
        cam.targetTexture = rt;
        foreach (var view in views)
        {
            cam.transform.position = view.Value;
            cam.transform.LookAt(Vector3.zero);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            File.WriteAllBytes(Path.Combine(outDir, "unity_" + view.Key + ".png"), tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
    }
}
