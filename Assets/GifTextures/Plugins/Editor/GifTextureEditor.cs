using Gif2Textures;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
[CustomEditor(typeof(GifTexture))]
public class GifTextureEditor : Editor
{
    private GifTexture _gifTexture;
    private static bool _eventsFoldout;
    private static bool _textureDetailFoldout;
    private int _loadedTextureCount = 0;
    private Vector2 _scrollPos;

    public void OnEnable()
    {
        _gifTexture = target as GifTexture;
        if (!Application.isPlaying && (_gifTexture.gifFrames == null || _gifTexture.gifFrames.GetCurrentLoadingState() == GifFrames.LoadingState.IDLE))
        {
            _loadedTextureCount = 0;
            var e = _gifTexture.Start();
            while (e.MoveNext()) { }
        }
    }

    public override void OnInspectorGUI()
    {
        bool hasTargetComponent = _gifTexture.GetComponent<Renderer>() || _gifTexture.GetComponent<RawImage>() || _gifTexture.GetComponent<Image>();

        if (!hasTargetComponent)
        {
            EditorGUILayout.HelpBox("No Target Component!", MessageType.Warning);
        }

        EditorGUI.BeginDisabledGroup(!hasTargetComponent);

        GUIContent content;

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        content = new GUIContent("Gif File", "Path or Url of the Gif file");
        _gifTexture.m_GifFileNameOrUrl = EditorGUILayout.TextField(content, _gifTexture.m_GifFileNameOrUrl);
        bool gifFileChanged = EditorGUI.EndChangeCheck();

        if (_gifTexture.GifFullPath != null && _gifTexture.GifFullPath.StartsWith(Application.dataPath))
        {
            EditorGUILayout.LabelField("Gif Path", _gifTexture.GifFullPath.Substring(Application.dataPath.Length + 1));
        }
        else
        {
            EditorGUILayout.LabelField("Gif Path", _gifTexture.GifFullPath);
        }

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        content = new GUIContent("Shrink Frame To Fit Texture", "Shrink frame base on the Max Texture Size and Max Atlas Texture Count");
        _gifTexture.m_ShrinkFrameToFitTexture = EditorGUILayout.Toggle(content, _gifTexture.m_ShrinkFrameToFitTexture);

        content = new GUIContent("Enable Atlas", "Put multiple frames on 1 texture for better VRAM usage and FPS");
        _gifTexture.m_EnableAtlas = EditorGUILayout.Toggle(content, _gifTexture.m_EnableAtlas);

        EditorGUI.BeginDisabledGroup(!_gifTexture.m_ShrinkFrameToFitTexture && !_gifTexture.m_EnableAtlas);
        int[] sizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
        content = new GUIContent("Max Texture Size", "The maximum texture size can be used on your target platform");
        _gifTexture.m_MaxTextureSize = EditorGUILayout.IntPopup(content, _gifTexture.m_MaxTextureSize, sizes.Select(x => new GUIContent(x.ToString())).ToArray(), sizes);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!_gifTexture.m_EnableAtlas);
        content = new GUIContent("Exclude First Frame From Atlas", "The first frame will be loaded faster, so we can use the first frame as loading screen.");
        _gifTexture.m_ExcludeFirstFrameFromAtlas = EditorGUILayout.Toggle(content, _gifTexture.m_ExcludeFirstFrameFromAtlas);
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(!_gifTexture.m_EnableAtlas || !_gifTexture.m_ShrinkFrameToFitTexture);
        content = new GUIContent("Max Atlas Texture Count", "Maximum atlas texture count. If the Load First Frame ASAP is enabled, the total texture count will be Max Atlas Texture Count + 1.");
        _gifTexture.m_MaxTextureCount = EditorGUILayout.IntField(content, _gifTexture.m_MaxTextureCount);
        EditorGUI.EndDisabledGroup();

        content = new GUIContent("Enable Mipmap", "Set it to true if you will use the textures on 3D object. Mipmap will use one-third more memory but it gives better performance.");
        _gifTexture.m_TextureMipmap = EditorGUILayout.Toggle(content, _gifTexture.m_TextureMipmap);

        bool memorySettingChanged = EditorGUI.EndChangeCheck();

        if (gifFileChanged || memorySettingChanged)
        {
            _loadedTextureCount = 0;
            var e = _gifTexture.Start();
            while (e.MoveNext()) { }
        }

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

        content = new GUIContent("Enable Async Loading", "Loads gif asynchronously, so user interaction or UI won't be blocked (not supported on platforms that don't support multi-threading. e.g. WebGL and Windows Store platform).");
        _gifTexture.m_EnableAsyncLoading = EditorGUILayout.Toggle(content, _gifTexture.m_EnableAsyncLoading);

        content = new GUIContent("Playback Speed", "0.1: one tenth of the original speed\n1.0: original speed\n10.0: 10 times of the original speed");
        _gifTexture.m_Speed = EditorGUILayout.Slider(content, _gifTexture.m_Speed, 0.1f, 10.0f);

        EditorGUI.BeginDisabledGroup(!_gifTexture.IsRemoteGif);
        content = new GUIContent("Enable Texture Caching", "Caching generated textures will make loading faster next time. Only works with remote gif.");
        _gifTexture.m_EnableTextureCaching = EditorGUILayout.Toggle(content, _gifTexture.m_EnableTextureCaching);
        EditorGUI.EndDisabledGroup();

        int materialCount = _gifTexture.MaterialCount;
        EditorGUI.BeginDisabledGroup(materialCount <= 1);
        content = new GUIContent("Material Index", "Choose which material to update when there are multiple materials in the renderer.");
        _gifTexture.m_MaterialIndex = EditorGUILayout.IntSlider(content, _gifTexture.m_MaterialIndex, 0, materialCount - 1);
        EditorGUI.EndDisabledGroup();

        bool miscSettingChanged = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();

        _gifTexture.m_LoadingTexture = EditorGUILayout.ObjectField("Loading Texture", _gifTexture.m_LoadingTexture, typeof(Texture2D), false) as Texture2D;

        EditorGUILayout.Space();

        _eventsFoldout = EditorGUILayout.Foldout(_eventsFoldout, "Events:");
        if (_eventsFoldout)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onLoad"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onError"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("onProgress"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.Space();
        }

        _textureDetailFoldout = EditorGUILayout.Foldout(_textureDetailFoldout, "Texture Detail:");
        if (_textureDetailFoldout)
        {
            //EditorGUI.indentLevel++;

            int frameCount = -1;
            int frameWidth = 0;
            int frameHeight = 0;
            int frameShrunkenWidth = 0;
            int frameShrunkenHeight = 0;
            int textureCount = -1;
            long totalMemorySize = 0;
            long[] memorySize = null;
            int maxWidth = 0;
            int maxHeight = 0;
            Texture2D[] loadedTextures = null;
            Vector2Int[] textureSizes = null;

            if (_gifTexture.gifFrames != null)
            {
                Texture2D frameTexture;
                int offsetX, offsetY;
                frameCount = _gifTexture.gifFrames.GetFrameCount();
                frameWidth = _gifTexture.gifFrames.GetFrameWidth();
                frameHeight = _gifTexture.gifFrames.GetFrameHeight();
                _gifTexture.gifFrames.GetFrameTexture(0, out frameTexture, out offsetX, out offsetY, out frameShrunkenWidth, out frameShrunkenHeight);
                textureCount = _gifTexture.gifFrames.GetTextureCount();
                loadedTextures = _gifTexture.gifFrames.GetLoadedTextures();
                textureSizes = _gifTexture.gifFrames.GetTextureSizes();
                if (loadedTextures != null && textureSizes != null && textureCount > 0)
                {
                    memorySize = new long[textureCount];
                    for (int i = 0; i < textureCount; i++)
                    {
                        Texture texture = i < loadedTextures.Length ? loadedTextures[i] : null;
                        memorySize[i] = texture != null ? Profiler.GetRuntimeMemorySizeLong(texture) : 0;
                        if (textureCount == loadedTextures.Length)
                        {
                            totalMemorySize += memorySize[i];
                        }

                        var textureWidth = i < textureSizes.Length ? textureSizes[i].x : 0;
                        var textureHeight = i < textureSizes.Length ? textureSizes[i].y : 0;
                        maxWidth = Mathf.Max(maxWidth, textureWidth);
                        maxHeight = Mathf.Max(maxHeight, textureHeight);
                    }
                }
            }

            EditorGUILayout.LabelField("Frame Count", frameCount > 0 ? frameCount.ToString() : "loading...");
            EditorGUILayout.LabelField("Frame Size", frameWidth > 0 ? string.Format("{0} x {1}", frameWidth, frameHeight) : "loading...");
            EditorGUI.BeginDisabledGroup(!_gifTexture.m_ShrinkFrameToFitTexture);
            EditorGUILayout.LabelField("Frame Shrunken Size", frameShrunkenWidth > 0 ? string.Format("{0} x {1}", frameShrunkenWidth, frameShrunkenHeight) : "loading...");
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.LabelField("Texture Count", textureCount > 0 ? textureCount.ToString() : "loading...");
            EditorGUILayout.LabelField("Approx. Total Memory", totalMemorySize > 0 ? GetMemorySizeString(totalMemorySize).ToString() : "loading...");

            const float TEXTURE_PREVIEW_HEIGHT = 140;
            const float TEXTURE_PREVIEW_WIDTH_MIN = 100;
            const float TEXTURE_PREVIEW_GAP = 10;
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(TEXTURE_PREVIEW_HEIGHT + 50));
            if (loadedTextures != null && textureSizes != null && maxHeight > 0)
            {
                var texWidth = Mathf.Max(TEXTURE_PREVIEW_WIDTH_MIN, TEXTURE_PREVIEW_HEIGHT * maxWidth / maxHeight);
                var texScale = TEXTURE_PREVIEW_HEIGHT / maxHeight;

                var wholeRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(textureCount * (texWidth + TEXTURE_PREVIEW_GAP)), GUILayout.Height(TEXTURE_PREVIEW_HEIGHT));
                for (int i = 0; i < textureCount; i++)
                {
                    var drawWidth = textureSizes[i].x * texScale;
                    var drawHeight = textureSizes[i].y * texScale;
                    var rectX = wholeRect.x + i * (texWidth + TEXTURE_PREVIEW_GAP);
                    var texRect = new Rect(rectX + (texWidth - drawWidth) / 2, wholeRect.y + (TEXTURE_PREVIEW_HEIGHT - drawHeight) / 2, drawWidth, drawHeight);
                    if (i < loadedTextures.Length)
                    {
                        EditorGUI.DrawTextureTransparent(texRect, loadedTextures[i]);
                    }
                    else
                    {
                        EditorGUI.DrawRect(texRect, Color.black);
                    }
                    EditorGUI.HelpBox(new Rect(rectX, wholeRect.y + TEXTURE_PREVIEW_HEIGHT, texWidth, 30),
                        string.Format("SIZE:{0}x{1}\nMEM:{2}", textureSizes[i].x, textureSizes[i].y,
                        memorySize[i] > 0 ? GetMemorySizeString(memorySize[i]) : "loading"),
                        MessageType.None);
                }
            }
            EditorGUILayout.EndScrollView();

            //EditorGUI.indentLevel--;
        }

        EditorGUI.EndDisabledGroup();

        if ((gifFileChanged || memorySettingChanged || miscSettingChanged) && !Application.isPlaying)
        {
            EditorUtility.SetDirty(_gifTexture);
			EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
		}
    }

    private string GetMemorySizeString(long memorySize)
    {
        if (memorySize >= 1024 * 1024 * 1024)
        {
            return string.Format("{0}GB", (memorySize + 1024 * 1024 * 512) / 1024 / 1024 / 1024);
        }
        else if (memorySize >= 1024 * 1024)
        {
            return string.Format("{0}MB", (memorySize + 1024 * 512) / 1024 / 1024);
        }

        return string.Format("{0}KB", (memorySize + 512) / 1024);
    }

    public override bool RequiresConstantRepaint()
    {
        if (!Application.isPlaying && _gifTexture != null && _gifTexture.gifFrames != null)
        {
            var loadingState = _gifTexture.gifFrames.GetCurrentLoadingState();
            if ((loadingState == GifFrames.LoadingState.IN_PROGRESS || loadingState == GifFrames.LoadingState.SUCCEED) &&
                _gifTexture.gifFrames.GetLoadedTextures().Length != _loadedTextureCount)
            {
                _loadedTextureCount = _gifTexture.gifFrames.GetLoadedTextures().Length;
                return true;
            }
        }
        return false;
    }
}