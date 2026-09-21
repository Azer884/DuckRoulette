using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// One place to instantiate generated content. In the editor it keeps the prefab link, so a
    /// generated map can be inspected, tweaked by hand and saved into the scene like normal level
    /// geometry; at runtime it is a plain Instantiate.
    /// </summary>
    public static class MapSpawner
    {
        public static GameObject Spawn(GameObject prefab, Transform parent)
        {
    #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
                if (instance != null)
                {
                    return instance;
                }
            }
    #endif
            return Object.Instantiate(prefab, parent);
        }

        /// <summary>Removes every child of a transform, in editor or at runtime.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
    #if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    Object.DestroyImmediate(child);
                    continue;
                }
    #endif
                Object.Destroy(child);
            }
        }
    }
}
