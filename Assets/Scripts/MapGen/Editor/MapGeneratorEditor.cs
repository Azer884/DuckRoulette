using UnityEditor;
using UnityEngine;

namespace DuckRoulette.MapGen
{
    /// <summary>
    /// Inspector for MapGenerator: generate, reroll and clear without entering play mode, plus a
    /// region map preview so the layout can be judged before the meshes are built.
    /// </summary>
    [CustomEditor(typeof(MapGenerator))]
    public class MapGeneratorEditor : Editor
    {
        Texture2D preview;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (MapGenerator)target;

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(28f)))
                {
                    Generate(generator);
                }

                if (GUILayout.Button("Reroll seed", GUILayout.Height(28f)))
                {
                    generator.RerollSeed();
                    Generate(generator);
                }

                if (GUILayout.Button("Clear", GUILayout.Height(28f)))
                {
                    Transform root = generator.transform.Find("Generated");
                    if (root != null)
                    {
                        Undo.DestroyObjectImmediate(root.gameObject);
                    }
                }
            }

            if (generator.RegionMap == null)
            {
                EditorGUILayout.HelpBox("Generate the map to see the region layout.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Refresh region preview"))
            {
                preview = BuildPreview(generator);
            }

            if (preview == null)
            {
                preview = BuildPreview(generator);
            }

            Rect rect = GUILayoutUtility.GetAspectRect(1f);
            EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
            DrawLegend();
        }

        static void Generate(MapGenerator generator)
        {
            Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, "Generate map");
            generator.Generate();
            EditorUtility.SetDirty(generator);

            if (!Application.isPlaying)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            }
        }

        static Texture2D BuildPreview(MapGenerator generator)
        {
            int width = generator.RegionMap.GetLength(0);
            int height = generator.RegionMap.GetLength(1);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color[width * height];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Color colour = RegionMarker.ColourFor(generator.RegionMap[x, y]);

                    // Shade the region colour by height, so the plateau and the river valley are
                    // both visible in the same preview.
                    float shade = Mathf.InverseLerp(-1f, 3f, generator.HeightMap[x, y]);
                    pixels[y * width + x] = Color.Lerp(colour * 0.45f, colour, shade);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static void DrawLegend()
        {
            EditorGUILayout.LabelField("Regions", EditorStyles.boldLabel);
            foreach (RegionType region in System.Enum.GetValues(typeof(RegionType)))
            {
                if (region == RegionType.None || region == RegionType.Mountains)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect swatch = GUILayoutUtility.GetRect(14f, 14f, GUILayout.Width(14f));
                    EditorGUI.DrawRect(swatch, RegionMarker.ColourFor(region));
                    EditorGUILayout.LabelField(region.ToString());
                }
            }
        }
    }
}
