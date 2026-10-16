using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Makes sure every URP renderer in the project carries TaskOutlineFeature (the task highlight
// outline), with its two shaders assigned. Runs once after each script reload and only touches a
// renderer that is missing the feature, so it is safe to leave in place - and a renderer added
// later picks the outline up automatically.
[InitializeOnLoad]
internal static class TaskOutlineFeatureInstaller
{
    private const string MaskShader = "Hidden/DuckRoulette/TaskOutlineMask";
    private const string CompositeShader = "Hidden/DuckRoulette/TaskOutlineComposite";

    static TaskOutlineFeatureInstaller()
    {
        EditorApplication.delayCall += Install;
    }

    [MenuItem("Tools/DuckRoulette/Install Task Outline Feature")]
    private static void Install()
    {
        Shader mask = Shader.Find(MaskShader);
        Shader composite = Shader.Find(CompositeShader);
        if (mask == null || composite == null)
        {
            return;
        }

        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UniversalRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null || HasFeature(data))
            {
                continue;
            }

            TaskOutlineFeature feature = ScriptableObject.CreateInstance<TaskOutlineFeature>();
            feature.name = "TaskOutline";

            SerializedObject featureObject = new(feature);
            featureObject.FindProperty("maskShader").objectReferenceValue = mask;
            featureObject.FindProperty("compositeShader").objectReferenceValue = composite;
            featureObject.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            // Both lists, as URP's own "Add Renderer Feature" button does: the map is how the
            // renderer finds its sub-asset features again.
            SerializedObject dataObject = new(data);
            SerializedProperty features = dataObject.FindProperty("m_RendererFeatures");
            SerializedProperty map = dataObject.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            dataObject.ApplyModifiedPropertiesWithoutUndo();

            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"TaskOutlineFeatureInstaller: added the task outline to {path}.");
        }
    }

    private static bool HasFeature(ScriptableRendererData data)
    {
        foreach (ScriptableRendererFeature feature in data.rendererFeatures)
        {
            if (feature is TaskOutlineFeature)
            {
                return true;
            }
        }

        return false;
    }
}
