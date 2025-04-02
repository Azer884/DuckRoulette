using Gif2Textures;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;

public class GifTexture : MonoBehaviour
{
    /// <summary>
    ///     The path of the gif. Could be a file name or a url. Please check function Load()'s command for more detail.
    /// </summary>
    public string m_GifFileNameOrUrl;

#if UNITY_EDITOR
    private string m_GifFullPath;
    public string GifFullPath { get { return m_GifFullPath; } }
#endif

    /// <summary>
    ///     Set m_EnableAsyncLoading to true to load gif asynchronously. But for some platforms like WebGL which set it to false to avoid crash.
    /// </summary>
    public bool m_EnableAsyncLoading = true;

    /// <summary>
    ///     The max size of the atlas texture. Better be power of 2. Please be aware that most of platforms (but not all platforms) support 4096x4096 textue.
    /// </summary>
    public bool m_EnableAtlas = true;
    public int m_MaxTextureSize = 2048;
    public bool m_ShrinkFrameToFitTexture = false;
    public int m_MaxTextureCount = 1;
    public bool m_ExcludeFirstFrameFromAtlas = true;
    public bool m_TextureMipmap = false;

    /// <summary>
    ///     Set m_EnableTextureCaching to true to cache generated texture locally.
    /// </summary>
    public bool m_EnableTextureCaching = false;

    /// <summary>
    ///     To know if this is a web gif.
    /// </summary>
    private bool m_IsRemoteGif = false;
    public bool IsRemoteGif { get { return m_IsRemoteGif; } }

    /// <summary>
    ///     The play speed. 1.0 is the original speed. 2.0 means 2 times faster. 0.5 means half the speed.
    /// </summary>
    [Range(0.1f, 10.0f)]
    public float m_Speed = 1.0f;

    public int MaterialCount
    {
        get
        {
            Renderer componentRenderer = GetComponent<Renderer>();
            return componentRenderer != null ? componentRenderer.sharedMaterials.Length : 0;
        }
    }

    public int m_MaterialIndex = 0;

    public Texture2D m_LoadingTexture;

    [System.Serializable]
    public class UnityEventFloat : UnityEvent<float> { }

    public UnityEvent onLoad;
    public UnityEvent onError;
    public UnityEventFloat onProgress;

    float m_Timer = 0.0f;
    int m_CurrentFrameIndex = -1;
    float m_CurrentDelay = 0;
    GifFrames m_GifFrames = null;

    string m_ProcessedGifFileNameOrUrl;

    GifFrames.LoadingState m_LoadingState = GifFrames.LoadingState.IDLE;
    float m_LoadingProgress = -1;

    Dictionary<int, Sprite> m_Sprites = new Dictionary<int, Sprite>();

    public GifFrames gifFrames { get { return m_GifFrames; } }

    public IEnumerator Start()
    {
        return InitializeGifFrames(m_GifFileNameOrUrl);
    }

    public IEnumerator InitializeGifFrames(string gifFileNameOrUrl)
    {
        ResetFrame();

        if (gifFileNameOrUrl == null || gifFileNameOrUrl.Length == 0)
        {
#if UNITY_EDITOR
            m_GifFullPath = "Please input a valid Gif file name or url.";
#endif
            yield break;
        }

        Stream glfStream = null;
        m_ProcessedGifFileNameOrUrl = gifFileNameOrUrl;
#if UNITY_EDITOR
        m_GifFullPath = "composing...";
#endif

        Uri gifUri;
        if (Uri.TryCreate(gifFileNameOrUrl, UriKind.Absolute, out gifUri) &&
            (gifUri.Scheme == "http" || gifUri.Scheme == "https"))
        {
            m_IsRemoteGif = true;

            UnityWebRequest webRequest = UnityWebRequest.Get(gifUri.AbsoluteUri);
            webRequest.SendWebRequest();
            while (!webRequest.isDone && string.IsNullOrEmpty(webRequest.error))
            {
                yield return null;
            }
            if (!webRequest.isNetworkError)
            {
                glfStream = new MemoryStream(webRequest.downloadHandler.data);
#if UNITY_EDITOR
                m_GifFullPath = gifUri.AbsoluteUri;
#endif
            }
        }
        else
        {
            m_IsRemoteGif = false;

            string[] prefixes = new string[]
            {
                "",
                Application.dataPath,
                Application.streamingAssetsPath,
                Application.persistentDataPath
                // You can add your custom paths here.
            };

            foreach (var prefix in prefixes)
            {
                string fullPath = Path.Combine(prefix, gifFileNameOrUrl);
                if (Uri.TryCreate(fullPath, UriKind.Absolute, out gifUri))
                {
                    if (gifUri.IsFile && File.Exists(gifUri.LocalPath))
                    {
                        glfStream = File.OpenRead(gifUri.LocalPath);
#if UNITY_EDITOR
                        m_GifFullPath = gifUri.LocalPath;
#endif
                    }
#if (UNITY_ANDROID || UNITY_WEBGL)
                    else if (prefix == Application.streamingAssetsPath)
                    {
                        UnityWebRequest webRequest = UnityWebRequest.Get(gifUri.AbsoluteUri);
                        webRequest.SendWebRequest();
                        while (!webRequest.isDone && string.IsNullOrEmpty(webRequest.error))
                        {
                            yield return null;
                        }
                        if (!webRequest.isNetworkError && webRequest.responseCode != 404)
                        {
                            glfStream = new MemoryStream(webRequest.downloadHandler.data);
#if UNITY_EDITOR
                            m_GifFullPath = gifUri.AbsoluteUri;
#endif
                        }
                    }
#endif
                }
            }

            if (glfStream == null)
            {
                // try to load it as a resources. The file need to be in project's Resource folder and renamed to xxx.gif.bytes.
                TextAsset ta = Resources.Load(gifFileNameOrUrl) as TextAsset;
                if (ta != null)
                {
                    glfStream = new MemoryStream(ta.bytes);
#if UNITY_EDITOR
                    m_GifFullPath = Path.Combine("Resources", gifFileNameOrUrl);
#endif
                }
            }
        }

        if (glfStream != null)
        {
            // opened the gif as a stream. pass it to GifFrames to load.
            CreateGifFrames(glfStream);
        }
        else
        {
#if UNITY_EDITOR
            m_GifFullPath = "Invalid Gif file name";
#endif
            // couldn't find the gif.
            Debug.LogWarning("Can't open Gif file \"" + gifFileNameOrUrl + "\" for object: " + gameObject.name);
        }
    }

    void CreateGifFrames(Stream glfStream)
    {
        m_GifFrames = new GifFrames();
        GifFrames.Config config = new GifFrames.Config();
        config.EnableAtlas = m_EnableAtlas;
        config.ShrinkFrameToFitTexture = m_ShrinkFrameToFitTexture;
        config.MaxTextureCount = m_MaxTextureCount;
        config.MaxTextureSize = m_MaxTextureSize;
        config.ExcludeFirstFrameFromAtlas = m_ExcludeFirstFrameFromAtlas;
        config.CacheTextureToPath = (m_IsRemoteGif && m_EnableTextureCaching) ? Application.temporaryCachePath + "/GIFCache" : "";

        if (m_EnableAsyncLoading)
        {
            // Please be aware that LoadAsync will fall back to Load on Windows Store platform since it doesn't support Threading.
            m_GifFrames.LoadAsync(glfStream, config);
        }
        else
        {
            m_GifFrames.Load(glfStream, config);
        }
    }

    void Update()
    {
        if (m_GifFileNameOrUrl != m_ProcessedGifFileNameOrUrl)
        {
            StartCoroutine(InitializeGifFrames(m_GifFileNameOrUrl));
        }

        if (m_GifFrames != null)
        {
            GifFrames.LoadingState loadingState = m_GifFrames.GetCurrentLoadingState();
            switch (loadingState)
            {
                case GifFrames.LoadingState.IN_PROGRESS:
                    float loadingProgress = m_GifFrames.GetLoadingProgress();
                    if (m_LoadingProgress != loadingProgress)
                    {
                        onProgress.Invoke(loadingProgress);
                        m_LoadingProgress = loadingProgress;
                    }
                    if (m_CurrentFrameIndex == -1 && m_GifFrames.GetLoadedFrameCount() > 0)
                    {
                        m_CurrentFrameIndex = 0;
                        ApplyFrame(m_CurrentFrameIndex);
                    }
                    break;
                case GifFrames.LoadingState.SUCCEED:
                    if (m_LoadingState != GifFrames.LoadingState.SUCCEED)
                    {
                        onLoad.Invoke();
                    }
                    m_Timer += Time.deltaTime * m_Speed;
                    if (m_Timer >= m_CurrentDelay)
                    {
                        m_Timer -= m_CurrentDelay;
                        m_CurrentFrameIndex = (m_CurrentFrameIndex + 1) % m_GifFrames.GetFrameCount();
                        ApplyFrame(m_CurrentFrameIndex);
                    }
                    break;
                case GifFrames.LoadingState.FAILED:
                    m_GifFrames = null;
                    if (m_LoadingState != GifFrames.LoadingState.FAILED)
                    {
                        onError.Invoke();
                    }
                    break;
            }
            m_LoadingState = loadingState;
        }
    }

    void ApplyFrame(int frameIndex)
    {
        Texture2D tex;
        int offsetX;
        int offsetY;
        int width;
        int height;
        if (!m_GifFrames.GetFrameTexture(frameIndex, out tex, out offsetX, out offsetY, out width, out height))
        {
            return;
        }
        m_GifFrames.GetFrameDelay(frameIndex, out m_CurrentDelay);

        RawImage componentRawImage = GetComponent<RawImage>();
        if (componentRawImage != null)
        {
            componentRawImage.rectTransform.sizeDelta =
                calculateSizeDelta(componentRawImage.rectTransform.sizeDelta, width, height);

            componentRawImage.texture = tex;
            componentRawImage.uvRect = new Rect((float)offsetX / tex.width, (float)offsetY / tex.height, (float)width / tex.width, (float)height / tex.height);
        }

        Image componentImage = GetComponent<Image>();
        if (componentImage != null)
        {
            componentImage.rectTransform.sizeDelta =
                calculateSizeDelta(componentImage.rectTransform.sizeDelta, width, height);

            if (!m_Sprites.ContainsKey(frameIndex))
            {
                m_Sprites[frameIndex] = Sprite.Create(tex, new Rect(offsetX, offsetY, width, height), new Vector2(0, 0));
            }

            componentImage.sprite = m_Sprites[frameIndex];
        }

        Renderer componentRenderer = GetComponent<Renderer>();
        if (componentRenderer != null)
        {
            SpriteRenderer spriteRenderer = componentRenderer as SpriteRenderer;
            if (spriteRenderer)
            {
                if (!m_Sprites.ContainsKey(frameIndex))
                {
                    Vector2 spriteSize = Vector2.one;
                    Vector2 spritePivot = new Vector2(0.5f, 0.5f);
                    Sprite oldSprite = spriteRenderer.sprite;
                    if (oldSprite != null)
                    {
                        Vector2 oldRectSize = oldSprite.rect.size;
                        spriteSize = oldRectSize / oldSprite.pixelsPerUnit;
                        spritePivot = new Vector2(oldSprite.pivot.x / oldRectSize.x, oldSprite.pivot.y / oldRectSize.y);
                    }

                    float spritePixelsPerUnit = Mathf.Max(width / spriteSize.x, height / spriteSize.y);
                    m_Sprites[frameIndex] = Sprite.Create(tex, new Rect(offsetX, offsetY, width, height), spritePivot, spritePixelsPerUnit);
                }

                spriteRenderer.sprite = m_Sprites[frameIndex];
            }
            else
            {
                if (componentRenderer.material != null)
                {
                    Material material = componentRenderer.material;
                    if (m_MaterialIndex > 0 && componentRenderer.materials != null && componentRenderer.materials.Length > m_MaterialIndex)
                    {
                        material = componentRenderer.materials[m_MaterialIndex];
                    }
                    material.mainTexture = tex;
                    material.mainTextureOffset = new Vector2((float)offsetX / tex.width, (float)offsetY / tex.height);
                    material.mainTextureScale = new Vector2((float)width / tex.width, (float)height / tex.height);
                }
            }
        }
    }

    public void ResetFrame()
    {
        m_ProcessedGifFileNameOrUrl = null;
        m_LoadingState = GifFrames.LoadingState.IDLE;
        m_LoadingProgress = -1;

        if (m_GifFrames != null)
        {
            m_GifFrames.Dispose();
            m_GifFrames = null;
        }

        RawImage componentRawImage = GetComponent<RawImage>();
        if (componentRawImage != null)
        {
            componentRawImage.texture = m_LoadingTexture;
        }

        m_Sprites.Clear();
        Image componentImage = GetComponent<Image>();
        if (componentImage != null)
        {
            if (m_LoadingTexture)
            {
                componentImage.sprite = Sprite.Create(m_LoadingTexture, new Rect(0, 0, m_LoadingTexture.width, m_LoadingTexture.height), new Vector2(0, 0)); ;
            }
            else
            {
                componentImage.sprite = null;
            }
        }

        Renderer componentRenderer = GetComponent<Renderer>();
        if (componentRenderer != null && componentRenderer.sharedMaterial != null)
        {
            componentRenderer.sharedMaterial.mainTexture = m_LoadingTexture;
        }
    }

    Vector2 calculateSizeDelta(Vector2 oldSize, int texWidth, int texHeight)
    {
        Vector2 newSize = oldSize;
        if (oldSize.x * texHeight > oldSize.y * texWidth)
        {
            newSize.Set(oldSize.y * texWidth / texHeight, oldSize.y);
        }
        else if (oldSize.x * texHeight < oldSize.y * texWidth)
        {
            newSize.Set(oldSize.x, oldSize.x * texHeight / texWidth);
        }

        return newSize;
    }

    void OnDestroy()
    {
        if (m_GifFrames != null)
        {
            m_GifFrames.Dispose();
            m_GifFrames = null;
        }
    }
}
