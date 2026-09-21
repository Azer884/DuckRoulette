using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Weather;

/// <summary>
/// One-shot wiring for the weather feature: it builds the sky rig, drops the weather rig into the
/// game scene, puts sway on the trees, cloth on the tents, the snuffer on the campfire, and
/// creates the alert HUD.
///
/// It is re-runnable. Most steps check for what they would create first (the weather rig, the
/// campfire snuffer, the tent cloth), but the sky rig, the wind VFX and the alert HUD are torn
/// down and rebuilt every run, because their shape has changed between versions. So tune those
/// three in the scene, not by re-running this - a re-run discards their inspector values.
///
/// Run it from Tools > Weather > Set Up Game Scene, or drive the open editor from a terminal:
///   unity command open_scene --path Assets/Scenes/GameScene.unity
///   unity command menu --path "Tools/Weather/Set Up Game Scene"
/// </summary>
public static class WeatherSceneSetup
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";
    private const string WeatherRigPrefabPath = "Assets/Prefabs/WheaterHandler.prefab";
    private const string TreePrefabPath = "Assets/Prefabs/Tree.prefab";
    private const string TentPrefabPath = "Assets/Prefabs/Tent.prefab";
    private const string WindLineMaterialPath = "Assets/Materials/WindLines.mat";
    private const string NightPanoramaPath =
        "Assets/Quantum Mana Studio/QMS - Cartoon Skybox Pack FREE/Textures/Sky0003_Night.png";
    private const string WeatherSkyMaterialPath = "Assets/Materials/Weather/WeatherSky.mat";
    private const string FontAssetPath = "Assets/Font/Comic Sans MS SDF.asset";
    private const string WeatherMaterialFolder = "Assets/Materials/Weather";
    private const string AlertHudPrefabPath = "Assets/Prefabs/Ui/WeatherAlertHUD.prefab";

    // The HUD accent the task list already uses, so the weather banner reads as part of the same UI.
    private static readonly Color AccentOrange = new(1f, 0.5568628f, 0.023529412f, 1f);
    // Alarm red for the weather alert - louder than the HUD accent on purpose, it is a warning.
    private static readonly Color AlarmRed = new(1f, 0.17f, 0.15f, 1f);
    private static readonly Color OffWhite = new(0.94509804f, 0.9529412f, 0.9137255f, 1f);

    [MenuItem("Tools/Weather/Set Up Game Scene")]
    public static void SetUpGameScene()
    {
        SetUpTreePrefab();
        SetUpTentPrefab();
        EnsureAlertHudPrefab();

        Scene scene = SceneManager.GetActiveScene();
        bool reopened = false;
        if (scene.path != GameScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("WeatherSceneSetup: cancelled, the open scene was not saved.");
                return;
            }

            scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            reopened = true;
        }

        EnsureWeatherRig(scene);
        RemoveSupersededObjects(scene);
        EnsureSkyRig(scene);
        EnsureWindVfx(scene);
        EnsureCampfireSnuffer(scene);
        EnsureAlertHudInstance(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"WeatherSceneSetup: done{(reopened ? " (GameScene was opened for this)" : string.Empty)}.");
    }

    // Play-mode triggers. The in-game roll only becomes likely several minutes into a match, which
    // makes the whole feature awkward to look at; these fire it immediately on the host.
    [MenuItem("Tools/Weather/Force Rain (Play Mode)")]
    public static void ForceRain() => WeatherSystem.DebugForce(WeatherPhase.Rain);

    [MenuItem("Tools/Weather/Force Storm (Play Mode)")]
    public static void ForceStorm() => WeatherSystem.DebugForce(WeatherPhase.Storm);

    [MenuItem("Tools/Weather/Force Clear (Play Mode)")]
    public static void ForceClear() => WeatherSystem.DebugForce(WeatherPhase.Clear);

    #region Prefabs

    [MenuItem("Tools/Weather/Set Up Tree Prefab")]
    public static void SetUpTreePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreePrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"WeatherSceneSetup: no tree prefab at {TreePrefabPath}.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(TreePrefabPath);
        try
        {
            if (root.GetComponent<TreeWindSway>() == null)
            {
                root.AddComponent<TreeWindSway>();
            }

            // Batching Static bakes the transform into the combined mesh, so a static tree would
            // never visibly move however hard the wind blew. Everything else it was marked for
            // (occlusion, lightmapping, navigation) stays.
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(root);
            GameObjectUtility.SetStaticEditorFlags(root, flags & ~StaticEditorFlags.BatchingStatic);

            PrefabUtility.SaveAsPrefabAsset(root, TreePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log("WeatherSceneSetup: tree prefab now sways with the wind.");
    }

    [MenuItem("Tools/Weather/Set Up Tent Cloth")]
    public static void SetUpTentPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TentPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"WeatherSceneSetup: no tent prefab at {TentPrefabPath}.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(TentPrefabPath);
        try
        {
            var existingCloth = root.GetComponent<Cloth>();
            if (existingCloth != null)
            {
                // Already cloth: re-paint which vertices are pinned, so changes to the pinning
                // rule apply to tents set up by an earlier run.
                var skinnedMesh = root.GetComponent<SkinnedMeshRenderer>();
                ConfigureTentCloth(existingCloth, skinnedMesh != null ? skinnedMesh.sharedMesh : null);
                PrefabUtility.SaveAsPrefabAsset(root, TentPrefabPath);
                Debug.Log("WeatherSceneSetup: tent cloth re-pinned - edges solid, panels floppy.");
                return;
            }

            var filter = root.GetComponent<MeshFilter>();
            var renderer = root.GetComponent<MeshRenderer>();
            if (filter == null || filter.sharedMesh == null || renderer == null)
            {
                Debug.LogWarning("WeatherSceneSetup: the tent root has no mesh to simulate - " +
                    "move the canvas mesh onto the root, or run this on the object that has it.");
                return;
            }

            Mesh mesh = filter.sharedMesh;
            Material[] materials = renderer.sharedMaterials;

            // Cloth drives a SkinnedMeshRenderer, so the static pair has to go first.
            Object.DestroyImmediate(renderer, true);
            Object.DestroyImmediate(filter, true);

            var skinned = root.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = mesh;
            skinned.sharedMaterials = materials;
            skinned.updateWhenOffscreen = false;
            skinned.localBounds = mesh.bounds;

            var cloth = root.AddComponent<Cloth>();
            ConfigureTentCloth(cloth, mesh);

            if (root.GetComponent<ClothWindReceiver>() == null)
            {
                root.AddComponent<ClothWindReceiver>();
            }

            PrefabUtility.SaveAsPrefabAsset(root, TentPrefabPath);
            Debug.Log("WeatherSceneSetup: tent canvas is now real-time cloth driven by the wind.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureTentCloth(Cloth cloth, Mesh mesh)
    {
        cloth.useGravity = true;
        // Looser than before: low bending stiffness is what lets a panel billow instead of
        // flexing as one stiff sheet.
        cloth.damping = 0.25f;
        cloth.stretchingStiffness = 0.9f;
        cloth.bendingStiffness = 0.25f;
        cloth.useTethers = true;
        cloth.friction = 0.4f;
        // Canvas pegged to poles does not need 120Hz, and cloth solver frequency is the single
        // biggest knob on its cost.
        cloth.clothSolverFrequency = 90f;
        cloth.worldVelocityScale = 0.35f;
        cloth.worldAccelerationScale = 0.6f;

        Vector3[] vertices = cloth.vertices;
        ClothSkinningCoefficient[] coefficients = cloth.coefficients;
        if (vertices == null || coefficients == null || vertices.Length != coefficients.Length)
        {
            return;
        }

        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 vertex in vertices)
        {
            bounds.Encapsulate(vertex);
        }

        Vector3 size = bounds.size;
        const float edgeBand = 0.05f;

        for (int i = 0; i < coefficients.Length; i++)
        {
            // Normalised 0..1 position inside the canvas's own bounding box.
            Vector3 v = vertices[i] - bounds.min;
            float nx = size.x > 0.0001f ? v.x / size.x : 0.5f;
            float ny = size.y > 0.0001f ? v.y / size.y : 0.5f;
            float nz = size.z > 0.0001f ? v.z / size.z : 0.5f;

            // How far this vertex is from the nearest edge of the canvas, on any axis. The ridge
            // (top), the pegged hem (bottom) and both ends all count as edges: those are the lines
            // a real tent is held along, so they are pinned solid.
            float edgeDistance = Mathf.Min(
                Mathf.Min(nx, 1f - nx),
                Mathf.Min(Mathf.Min(ny, 1f - ny), Mathf.Min(nz, 1f - nz)));

            if (edgeDistance <= edgeBand)
            {
                coefficients[i].maxDistance = 0f;
            }
            else
            {
                // Everything inside the frame is free, and increasingly so towards the middle of
                // each panel, so the whole canvas billows rather than only a strip of it.
                float inner = Mathf.InverseLerp(edgeBand, 0.5f, edgeDistance);
                coefficients[i].maxDistance = Mathf.Lerp(0.08f, 0.45f, Mathf.SmoothStep(0f, 1f, inner));
            }

            coefficients[i].collisionSphereDistance = 0f;
        }

        cloth.coefficients = coefficients;
    }

    // The sky rig, the wind VFX and the alert HUD all changed shape (quads to spheres, a canned
    // prefab to an authored particle system, two panels to one morphing widget), so a project
    // that ran the old setup has to drop those before this one can rebuild them.
    private static void RemoveSupersededObjects(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Sky Rig" || root.name == "Wind VFX" || root.name == "WeatherAlertHUD")
            {
                // The sun is the scene's own directional light parented under the rig - put it
                // back at the root before the rig goes, or the scene loses its key light.
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.type == LightType.Directional && light.name != "Moon Light")
                    {
                        light.transform.SetParent(null, true);
                        SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                    }
                }

                Object.DestroyImmediate(root);
            }
        }
    }

    private static void EnsureAlertHudPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(AlertHudPrefabPath);
        if (existing != null)
        {
            AssetDatabase.DeleteAsset(AlertHudPrefabPath);
        }

        GameObject hud = BuildAlertHud();
        PrefabUtility.SaveAsPrefabAsset(hud, AlertHudPrefabPath);
        Object.DestroyImmediate(hud);
        Debug.Log($"WeatherSceneSetup: created {AlertHudPrefabPath}.");
    }

    // Built in code rather than by hand so it picks up the same font and canvas settings the other
    // HUDs use instead of drifting from them. One panel, not two: WeatherAlertHUD morphs it from
    // the centre-screen alarm into the docked timer, so every part has to live in the same rect.
    private static GameObject BuildAlertHud()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

        var root = new GameObject("WeatherAlertHUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Just above the other HUDs so the alarm is never drawn under them while it shouts.
        canvas.sortingOrder = 56;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        // Centre-anchored with a centred pivot: WeatherAlertHUD lerps anchoredPosition from the
        // middle of the canvas to its top-right corner, which only works in centre-origin space.
        GameObject panelObject = CreatePanel(root.transform, "Alert", new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(900f, 170f));
        var panel = panelObject.GetComponent<RectTransform>();

        var panelGroup = panelObject.AddComponent<CanvasGroup>();
        panelGroup.blocksRaycasts = false;
        panelGroup.interactable = false;

        // No backdrop plate. The text carries a dark outline instead so it still reads over a
        // bright sky or snow without boxing the alert in.
        TextMeshProUGUI title = CreateLabel(panelObject.transform, "Title", font, 80f, AlarmRed,
            new Vector2(0f, 28f), new Vector2(880f, 90f));
        title.fontStyle = FontStyles.Bold;
        AddOutline(title, 0.22f);

        TextMeshProUGUI subtitle = CreateLabel(panelObject.transform, "Subtitle", font, 28f, OffWhite,
            new Vector2(0f, -38f), new Vector2(880f, 50f));
        AddOutline(subtitle, 0.18f);

        // The drain bar fills the panel, because once docked the panel IS the bar - the headline
        // has faded out by then and nothing else is left in it. Invisible until that point.
        var barObject = new GameObject("Bar", typeof(RectTransform));
        barObject.transform.SetParent(panelObject.transform, false);
        var barRect = barObject.GetComponent<RectTransform>();
        barRect.anchorMin = Vector2.zero;
        barRect.anchorMax = Vector2.one;
        barRect.pivot = new Vector2(0.5f, 0.5f);
        barRect.anchoredPosition = Vector2.zero;
        barRect.sizeDelta = Vector2.zero;
        var barGroup = barObject.AddComponent<CanvasGroup>();
        barGroup.alpha = 0f;
        barGroup.blocksRaycasts = false;
        barGroup.interactable = false;

        // A faint trough so the drained part still shows how much has gone - it is the bar's own
        // track, not a panel background.
        var trackObject = new GameObject("Track", typeof(RectTransform));
        trackObject.transform.SetParent(barObject.transform, false);
        StretchToParent(trackObject.GetComponent<RectTransform>());
        var track = trackObject.AddComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.35f);
        track.raycastTarget = false;

        var fillObject = new GameObject("Fill", typeof(RectTransform));
        fillObject.transform.SetParent(barObject.transform, false);
        StretchToParent(fillObject.GetComponent<RectTransform>());
        var barFill = fillObject.AddComponent<Image>();
        barFill.raycastTarget = false;
        // Filled needs a sprite to cut; the built-in UI sprite is a plain white quad.
        barFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        barFill.fillAmount = 1f;
        barFill.color = AlarmRed;

        var hud = root.AddComponent<WeatherAlertHUD>();
        var serialized = new SerializedObject(hud);
        serialized.FindProperty("panel").objectReferenceValue = panel;
        serialized.FindProperty("panelGroup").objectReferenceValue = panelGroup;
        serialized.FindProperty("title").objectReferenceValue = title;
        serialized.FindProperty("subtitle").objectReferenceValue = subtitle;
        serialized.FindProperty("barGroup").objectReferenceValue = barGroup;
        serialized.FindProperty("barFill").objectReferenceValue = barFill;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        panelObject.SetActive(false);

        return root;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    // Outline on a per-label material instance, so the shared font asset is never modified.
    private static void AddOutline(TextMeshProUGUI label, float width)
    {
        if (label.fontSharedMaterial == null)
        {
            return;
        }

        var material = new Material(label.fontSharedMaterial) { name = label.name + " Outline" };
        material.EnableKeyword("OUTLINE_ON");
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0f, 0f, 0f, 0.85f));

        EnsureWeatherFolder();
        string path = $"{WeatherMaterialFolder}/{material.name}.mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(material, path);
        label.fontSharedMaterial = material;
    }

    private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 position, Vector2 size)
    {
        var panel = new GameObject(name, typeof(RectTransform));
        panel.transform.SetParent(parent, false);

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return panel;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string name, TMP_FontAsset font, float size,
        Color color, Vector2 position, Vector2 rectSize)
    {
        var labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.transform.SetParent(parent, false);

        var rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = rectSize;

        var label = labelObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
        {
            label.font = font;
        }

        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    #endregion

    #region Scene

    private static void EnsureWeatherRig(Scene scene)
    {
        if (FindInScene<WeatherSystem>(scene) != null)
        {
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeatherRigPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"WeatherSceneSetup: no weather rig prefab at {WeatherRigPrefabPath}.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "Weather";
        instance.transform.position = Vector3.zero;

        // Size the hail volume to whatever the level actually covers, rather than leaving the
        // prefab's 100x100 box sitting wherever the test scene had it.
        var spawnArea = instance.GetComponentInChildren<BoxCollider>();
        if (spawnArea != null && TryGetLevelBounds(scene, out Bounds bounds))
        {
            spawnArea.transform.position = new Vector3(bounds.center.x, bounds.max.y + 12f, bounds.center.z);
            spawnArea.size = new Vector3(Mathf.Max(20f, bounds.size.x), 6f, Mathf.Max(20f, bounds.size.z));
        }

        Debug.Log("WeatherSceneSetup: weather rig added to the scene.");
    }

    private static void EnsureSkyRig(Scene scene)
    {
        if (FindInScene<DayNightCycle>(scene) != null)
        {
            return;
        }

        Light sun = FindDirectionalLight(scene);
        if (sun == null)
        {
            Debug.LogWarning("WeatherSceneSetup: no directional light in the scene, so there is " +
                "nothing to use as the sun.");
            return;
        }

        var rig = new GameObject("Sky Rig");
        SceneManager.MoveGameObjectToScene(rig, scene);
        rig.transform.position = Vector3.zero;

        var sunPivot = new GameObject("Sun Pivot");
        sunPivot.transform.SetParent(rig.transform, false);

        // The scene's existing directional light becomes the sun, so its shadow settings, colour
        // temperature and rendering layers carry over instead of being reinvented.
        sun.transform.SetParent(sunPivot.transform, false);
        sun.transform.localPosition = Vector3.zero;
        sun.transform.localRotation = Quaternion.identity;
        sun.shadows = sun.shadows == LightShadows.None ? LightShadows.Soft : sun.shadows;

        Renderer sunSphere = CreateBody(sunPivot.transform, "Sun", new Color(1f, 0.93f, 0.72f), 46f, 380f);
        Renderer sunGlow = CreateGlow(sunSphere.transform, "Sun Glow", new Color(1f, 0.82f, 0.5f), 2.8f, 2.2f);

        var moonPivot = new GameObject("Moon Pivot");
        moonPivot.transform.SetParent(sunPivot.transform, false);
        moonPivot.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);

        var moonObject = new GameObject("Moon Light", typeof(Light));
        moonObject.transform.SetParent(moonPivot.transform, false);
        var moonLight = moonObject.GetComponent<Light>();
        moonLight.type = LightType.Directional;
        moonLight.intensity = 0f;
        moonLight.color = new Color(0.55f, 0.65f, 1f);
        moonLight.shadows = LightShadows.None;

        Renderer moonSphere = CreateBody(moonPivot.transform, "Moon", new Color(0.85f, 0.88f, 1f), 26f, 380f);
        Renderer moonGlow = CreateGlow(moonSphere.transform, "Moon Glow", new Color(0.62f, 0.72f, 1f), 2.3f, 3f);

        var cycle = rig.AddComponent<DayNightCycle>();
        var serialized = new SerializedObject(cycle);
        serialized.FindProperty("sunPivot").objectReferenceValue = sunPivot.transform;
        serialized.FindProperty("sunLight").objectReferenceValue = sun;
        serialized.FindProperty("moonLight").objectReferenceValue = moonLight;
        serialized.FindProperty("sunSphere").objectReferenceValue = sunSphere;
        serialized.FindProperty("sunGlow").objectReferenceValue = sunGlow;
        serialized.FindProperty("moonSphere").objectReferenceValue = moonSphere;
        serialized.FindProperty("moonGlow").objectReferenceValue = moonGlow;
        serialized.FindProperty("skyboxMaterial").objectReferenceValue = GetOrCreateWeatherSkybox();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("WeatherSceneSetup: sky rig built around the existing directional light.");
    }

    // A sphere, not a billboarded quad: it reads as a body in the sky from any angle, needs no
    // per-frame camera facing, and cannot edge-on vanish as the pivot swings it around.
    private static Renderer CreateBody(Transform parent, string name, Color color, float size, float distance)
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = name;
        body.transform.SetParent(parent, false);
        // Pushed out along the light's own -Z, which is the direction it shines from.
        body.transform.localPosition = new Vector3(0f, 0f, -distance);
        body.transform.localScale = Vector3.one * size;

        Object.DestroyImmediate(body.GetComponent<Collider>());

        var renderer = body.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.sharedMaterial = GetOrCreateUnlitMaterial(name, color);
        return renderer;
    }

    // The corona: a larger sphere around the body using Weather/BodyGlow, which fades to nothing at
    // its silhouette and adds its light on top of the sky. Parented to the body so it follows it
    // without needing its own transform maths.
    private static Renderer CreateGlow(Transform body, string name, Color color, float relativeSize, float falloff)
    {
        GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = name;
        glow.transform.SetParent(body, false);
        glow.transform.localPosition = Vector3.zero;
        glow.transform.localScale = Vector3.one * relativeSize;

        Object.DestroyImmediate(glow.GetComponent<Collider>());

        var renderer = glow.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        EnsureWeatherFolder();
        string path = $"{WeatherMaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Weather/BodyGlow");
            if (shader == null)
            {
                Debug.LogWarning("WeatherSceneSetup: Weather/BodyGlow shader missing, " + name + " skipped.");
                Object.DestroyImmediate(glow);
                return null;
            }

            material = new Material(shader) { name = name };
            material.SetColor("_GlowColor", color);
            material.SetFloat("_Falloff", falloff);
            AssetDatabase.CreateAsset(material, path);
        }

        renderer.sharedMaterial = material;
        return renderer;
    }

    private static Material GetOrCreateUnlitMaterial(string name, Color color)
    {
        EnsureWeatherFolder();

        string path = $"{WeatherMaterialFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Builds (once) a Skybox/WeatherPanoramic material seeded from whatever panorama the scene is
    // already using, so the sky can cross-fade day to night and clear to storm instead of the
    // material being swapped. If the shader or a night panorama is missing it falls back to the
    // scene's current skybox, which DayNightCycle then grades with tint and exposure only.
    private static Material GetOrCreateWeatherSkybox()
    {
        Material current = RenderSettings.skybox;

        Shader blendShader = Shader.Find("Skybox/WeatherPanoramic");
        if (blendShader == null)
        {
            Debug.LogWarning("WeatherSceneSetup: Skybox/WeatherPanoramic not found, keeping the " +
                "scene's own skybox. Tint and exposure will still be driven.");
            return current;
        }

        var existing = AssetDatabase.LoadAssetAtPath<Material>(WeatherSkyMaterialPath);
        if (existing != null)
        {
            return existing;
        }

        EnsureWeatherFolder();

        var material = new Material(blendShader) { name = "WeatherSky" };

        Texture day = current != null && current.HasProperty("_MainTex") ? current.GetTexture("_MainTex") : null;
        if (day == null && current != null && current.HasProperty("_DayTex"))
        {
            day = current.GetTexture("_DayTex");
        }

        var night = AssetDatabase.LoadAssetAtPath<Texture>(NightPanoramaPath);

        material.SetTexture("_DayTex", day);
        // No dedicated storm panorama ships with the project, so the storm slot reuses the day
        // sky and gets its darkening from _StormTint instead of a second texture.
        material.SetTexture("_StormTex", day);
        material.SetTexture("_NightTex", night != null ? night : day);

        if (night == null)
        {
            Debug.LogWarning($"WeatherSceneSetup: no night panorama at {NightPanoramaPath}; the " +
                "night sky will be the day one dimmed. Assign _NightTex on " + WeatherSkyMaterialPath + ".");
        }

        AssetDatabase.CreateAsset(material, WeatherSkyMaterialPath);
        RenderSettings.skybox = material;
        return material;
    }

    private static void EnsureWeatherFolder()
    {
        if (!Directory.Exists(WeatherMaterialFolder))
        {
            Directory.CreateDirectory(WeatherMaterialFolder);
            AssetDatabase.Refresh();
        }
    }

    private static void EnsureWindVfx(Scene scene)
    {
        if (FindInScene<WindVfx>(scene) != null)
        {
            return;
        }

        // Authored here rather than instanced from a canned VFX prefab: WindVfx steers every live
        // particle towards the procedural wind each frame, which needs a system it owns (world
        // simulation space, stretched billboards, its own emission slab) rather than one tuned
        // for a fixed direction.
        var root = new GameObject("Wind VFX", typeof(ParticleSystem));
        SceneManager.MoveGameObjectToScene(root, scene);

        var vfx = root.AddComponent<WindVfx>();
        var system = root.GetComponent<ParticleSystem>();

        var serialized = new SerializedObject(vfx);
        serialized.FindProperty("windLines").objectReferenceValue = system;
        serialized.FindProperty("lineMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>(WindLineMaterialPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // Run the same module setup the component would apply at runtime, so the system looks
        // right in the editor too instead of only once play mode starts.
        vfx.Configure();

        Debug.Log("WeatherSceneSetup: wind line VFX authored.");
    }

    private static void EnsureCampfireSnuffer(Scene scene)
    {
        TaskObjective[] objectives = FindAllInScene<TaskObjective>(scene);
        foreach (TaskObjective objective in objectives)
        {
            if (objective.Task == null || objective.Task.name.IndexOf("Campfire", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            GameObject campfire = objective.gameObject;
            if (campfire.GetComponent<CampfireWeatherSnuffer>() != null)
            {
                return;
            }

            var snuffer = campfire.AddComponent<CampfireWeatherSnuffer>();

            var visuals = new List<GameObject>();
            foreach (ParticleSystem system in campfire.GetComponentsInChildren<ParticleSystem>(true))
            {
                visuals.Add(system.gameObject);
            }

            Light fireLight = campfire.GetComponentInChildren<Light>(true);
            AudioSource fireAudio = campfire.GetComponentInChildren<AudioSource>(true);

            var serialized = new SerializedObject(snuffer);
            serialized.FindProperty("campfireObjective").objectReferenceValue = objective;
            serialized.FindProperty("fireLight").objectReferenceValue = fireLight;
            serialized.FindProperty("fireAudio").objectReferenceValue = fireAudio;

            SerializedProperty visualsProperty = serialized.FindProperty("fireVisuals");
            visualsProperty.arraySize = visuals.Count;
            for (int i = 0; i < visuals.Count; i++)
            {
                visualsProperty.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("WeatherSceneSetup: campfire will now go out in rain or a gale, and its task " +
                "is withdrawn with it.");
            return;
        }

        Debug.LogWarning("WeatherSceneSetup: no campfire TaskObjective found in the scene, so the " +
            "snuffer was not wired.");
    }

    private static void EnsureAlertHudInstance(Scene scene)
    {
        if (FindInScene<WeatherAlertHUD>(scene) != null)
        {
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AlertHudPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"WeatherSceneSetup: {AlertHudPrefabPath} is missing.");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "WeatherAlertHUD";
        Debug.Log("WeatherSceneSetup: weather alert HUD added to the scene.");
    }

    #endregion

    #region Helpers

    private static bool TryGetLevelBounds(Scene scene, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
        }

        return any;
    }

    private static Light FindDirectionalLight(Scene scene)
    {
        foreach (Light light in FindAllInScene<Light>(scene))
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }

        return null;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        T[] found = FindAllInScene<T>(scene);
        return found.Length > 0 ? found[0] : null;
    }

    private static T[] FindAllInScene<T>(Scene scene) where T : Component
    {
        var results = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            results.AddRange(root.GetComponentsInChildren<T>(true));
        }

        return results.ToArray();
    }

    #endregion
}
