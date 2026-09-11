using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// One-shot maintenance pass over import settings only - no source asset is modified, renamed or
// deleted, so every change here is reversible by changing the setting back and reimporting.
//
// Why it exists: every AudioClip in the project shipped as Decompress On Load at Vorbis quality
// 1.00, which stores audio near-uncompressed in the player build AND expands it to raw PCM in
// RAM (Rain.mp3 alone: 467s stereo = ~165MB resident). Textures were shipping uncrunched, with
// 65 of them set to Uncompressed outright.
public static class ImportSettingsOptimizer
{
    private const string MarkerPath = "Temp/import_optimizer_done.txt";

    // Third-party/sample content this project's conventions say not to touch.
    private static readonly string[] ExcludedRoots =
    {
        "Assets/Plugins/Steamworks/",
        "Assets/Samples/",
        "Assets/JMO Assets/",
        "Assets/IniFileParser/",
        "Assets/GifTextures/",
        "Assets/Quantum Mana Studio/",
        "Assets/TextMesh Pro/",
    };

    // Anything longer than this is background music or a long ambience bed: streamed off disk so
    // it never occupies decompressed memory. Shorter clips stay resident but compressed.
    private const float StreamingLengthSeconds = 15f;

    [MenuItem("Tools/Optimize Import Settings (Audio + Textures)")]
    public static void Run()
    {
        StringBuilder log = new();
        int audioChanged = 0;
        int textureChanged = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            audioChanged = OptimizeAudio(log);
            textureChanged = OptimizeTextures(log);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();

        log.Insert(0, $"audio changed: {audioChanged}\ntexture changed: {textureChanged}\n");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(MarkerPath, log.ToString());
        Debug.Log($"ImportSettingsOptimizer: {audioChanged} audio, {textureChanged} texture importers updated.");
    }

    private static int OptimizeAudio(StringBuilder log)
    {
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsExcluded(path))
            {
                continue;
            }

            if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
            {
                continue;
            }

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                continue;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            AudioClipLoadType wantedLoadType = clip.length > StreamingLengthSeconds
                ? AudioClipLoadType.Streaming
                : AudioClipLoadType.CompressedInMemory;
            // Long streamed beds tolerate more compression than a short, transient-heavy one-shot.
            float wantedQuality = clip.length > StreamingLengthSeconds ? 0.5f : 0.7f;

            bool needsChange = settings.loadType != wantedLoadType ||
                               settings.compressionFormat != AudioCompressionFormat.Vorbis ||
                               !Mathf.Approximately(settings.quality, wantedQuality);
            if (!needsChange)
            {
                continue;
            }

            log.AppendLine($"audio {path}: {settings.loadType} q{settings.quality:F2} -> {wantedLoadType} q{wantedQuality:F2}");

            settings.loadType = wantedLoadType;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = wantedQuality;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            changed++;
        }

        return changed;
    }

    private static int OptimizeTextures(StringBuilder log)
    {
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsExcluded(path))
            {
                continue;
            }

            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                continue;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                continue;
            }

            bool dirty = false;

            if (importer.textureCompression == TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Compressed;
                log.AppendLine($"texture {path}: Uncompressed -> Compressed");
                dirty = true;
            }

            // Crunch shrinks what ships on disk (the download) without changing runtime GPU
            // memory. Deliberately skipped for normal maps, where crunch artifacts show up as
            // visible lighting banding, and for small textures where the win isn't worth it.
            int largestSide = Mathf.Max(texture.width, texture.height);
            if (!importer.crunchedCompression &&
                largestSide >= 512 &&
                importer.textureType != TextureImporterType.NormalMap)
            {
                importer.crunchedCompression = true;
                importer.compressionQuality = 50;
                log.AppendLine($"texture {path}: crunch enabled ({texture.width}x{texture.height})");
                dirty = true;
            }

            if (!dirty)
            {
                continue;
            }

            importer.SaveAndReimport();
            changed++;
        }

        return changed;
    }

    private static bool IsExcluded(string path)
    {
        if (!path.StartsWith("Assets/"))
        {
            return true;
        }

        foreach (string root in ExcludedRoots)
        {
            if (path.StartsWith(root))
            {
                return true;
            }
        }

        return false;
    }
}
