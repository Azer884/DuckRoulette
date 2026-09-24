using System.Collections;
using System.Collections.Generic;
using DuckRoulette.MapGen;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Keeps the loading screen's canvas up after the game scene activates, until the game is actually
// playable for this client: the procedural map is built (MapSeedSync) and this player's object
// has spawned onto it. LoadingScreenController adds this to its canvas right before it lets the
// game scene through; see LoadingScreenController.HandOffOverlay.
//
// Purely local and presentational - the network side of the load is already over by the time
// this runs, so it never holds anything up. It gives up after a timeout so a player who never
// spawns (a late joiner, a spectator) is not stuck behind it.
public class LoadingOverlayHold : MonoBehaviour
{
    private const string LoadingSceneName = "LoadingScreen";
    private const float MapBuiltProgress = 0.93f;
    private const float BarSpeed = 1.2f;
    private const float Timeout = 30f;
    private const float FadeDuration = 0.4f;

    private const string BuildingMapStatus = "Building the map...";
    private const string SpawningStatus = "Waddling into position...";
    private const string ReadyStatus = "Let's go!";

    private Slider bar;
    private TextMeshProUGUI percentText;
    private bool percentOnly;
    private TextMeshProUGUI statusText;
    private List<Slider> remoteBars;
    private float displayed;
    private float target;
    private CanvasGroup group;

    public void Begin(Slider progressBar, TextMeshProUGUI percent, bool percentOnlyText, TextMeshProUGUI status,
        List<Slider> otherBars, float startProgress)
    {
        bar = progressBar;
        percentText = percent;
        percentOnly = percentOnlyText;
        statusText = status;
        remoteBars = otherBars;
        displayed = startProgress;
        target = startProgress;

        DontDestroyOnLoad(gameObject);

        // Above every in-game HUD canvas, which would otherwise draw on top as soon as they load.
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
        }

        group = GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = gameObject.AddComponent<CanvasGroup>();
        }

        group.alpha = 1f;
        group.interactable = false;
        group.blocksRaycasts = true;

        SetStatus(BuildingMapStatus);
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + Timeout;

        // The Single-mode load swaps scenes a frame or two after activation is allowed.
        while (SceneManager.GetActiveScene().name == LoadingSceneName && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        while (!IsMapReady() && Time.realtimeSinceStartup < deadline)
        {
            if (ShouldAbort())
            {
                Destroy(gameObject);
                yield break;
            }

            yield return null;
        }

        target = MapBuiltProgress;
        SetStatus(SpawningStatus);

        while (!IsLocalPlayerSpawned() && Time.realtimeSinceStartup < deadline)
        {
            if (ShouldAbort())
            {
                Destroy(gameObject);
                yield break;
            }

            yield return null;
        }

        target = 1f;
        SetStatus(ReadyStatus);

        while (displayed < 1f)
        {
            yield return null;
        }

        yield return new WaitForSecondsRealtime(0.3f);

        group.blocksRaycasts = false;
        for (float t = 0f; t < FadeDuration; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / FadeDuration;
            yield return null;
        }

        Destroy(gameObject);
    }

    private void Update()
    {
        displayed = Mathf.MoveTowards(displayed, target, BarSpeed * Time.unscaledDeltaTime);

        if (bar != null)
        {
            bar.value = displayed;
        }

        if (percentText != null)
        {
            int percent = Mathf.RoundToInt(displayed * 100);
            percentText.text = percentOnly ? $"{percent}%" : $"{Steamworks.SteamClient.Name} - {percent}%";
        }

        // Nothing reports the other players' map build, so their bars simply finish with ours.
        if (remoteBars != null && target >= 1f)
        {
            foreach (Slider other in remoteBars)
            {
                if (other != null)
                {
                    other.value = Mathf.Max(other.value, displayed);
                }
            }
        }
    }

    private void SetStatus(string status)
    {
        if (statusText != null)
        {
            statusText.text = status;
        }
    }

    private static bool IsMapReady()
    {
        // A scene without a synced procedural map has nothing to wait for; one with it is ready
        // once this peer has built from the server's seed. MapSeedSync.Current is only set once
        // the in-scene object spawns, so look for the component too before calling it absent.
        if (MapSeedSync.Current != null)
        {
            return MapSeedSync.Current.IsBuilt;
        }

        return FindAnyObjectByType<MapSeedSync>() == null;
    }

    private static bool IsLocalPlayerSpawned()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || manager.SpawnManager == null || manager.SpawnManager.GetLocalPlayerObject() != null;
    }

    // Session ended or the host sent everyone back to the lobby while this was still up.
    private static bool ShouldAbort()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || SceneManager.GetActiveScene().name == "Lobby";
    }
}
