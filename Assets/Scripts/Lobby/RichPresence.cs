using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Steam rich presence for every state the player can be in: main menu, lobby (with member count,
// private or not), loading, playing (map + ducks left), spectating, match over, tutorial.
//
// Steam only shows "steam_display" when it names a token from the app's rich presence
// localization file, so every status below is a "#Token" and the variable parts are separate
// keys (%map%, %members%...). The tokens are defined in SteamRichPresence/rich_presence_english.vdf
// at the repo root, which has to be uploaded once on the Steamworks partner site
// (App Admin > Community > Rich Presence) before any of this shows up on a friends list.
//
// steam_player_group makes Steam group everyone in the same lobby together on friends lists
// ("Playing with X"), and "connect" gives friends a "Join Game" button while the lobby can be
// joined.
//
// Self-contained: created on boot, survives scene loads, and re-evaluates every couple of seconds
// plus immediately on scene changes, so nothing else has to remember to push an update.
public class RichPresence : MonoBehaviour
{
    public const string ConnectPrefix = "+connect_lobby ";

    private const float RefreshInterval = 2f;

    // Scene name -> name shown to friends. Add an entry when a new map scene is added.
    private static readonly Dictionary<string, string> MapDisplayNames = new()
    {
        { "GameScene", "Duck Island" },
        { "TestingEnviroment", "Testing Grounds" },
    };

    private static RichPresence instance;

    private readonly Dictionary<string, string> lastSent = new();
    private float timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new("RichPresence");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RichPresence>();
    }

    /// <summary>Re-evaluate right now instead of waiting for the next tick (lobby joined/left,
    /// member count changed...).</summary>
    public static void Refresh()
    {
        if (instance != null)
        {
            instance.timer = RefreshInterval;
        }
    }

    public static string GetMapDisplayName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return "a map";
        }

        return MapDisplayNames.TryGetValue(sceneName, out string display) ? display : sceneName;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMembersChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMembersChanged;
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMembersChanged;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMembersChanged;
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh();

    private void OnLobbyMembersChanged(Lobby lobby, Friend member) => Refresh();

    private void OnLobbyEntered(Lobby lobby) => Refresh();

    private void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer < RefreshInterval)
        {
            return;
        }

        timer = 0f;

        if (!SteamClient.IsValid)
        {
            return;
        }

        Apply();
    }

    private void Apply()
    {
        string scene = SceneManager.GetActiveScene().name;
        Lobby? lobby = LobbySaver.instance != null ? LobbySaver.instance.currentLobby : null;
        bool inLobby = lobby.HasValue && lobby.Value.Id.IsValid;
        bool privateLobby = inLobby && lobby.Value.GetData("type") == "private";

        string display;
        string map = GetMapDisplayName(scene);
        bool joinable = false;

        if (scene == "Tutorial")
        {
            display = "#Status_Tutorial";
        }
        else if (scene == "Skins")
        {
            display = "#Status_Customizing";
        }
        else if (scene == "LoadingScreen")
        {
            map = GameNetworkManager.Instance != null
                ? GetMapDisplayName(GameNetworkManager.Instance.PendingGameSceneName)
                : "a map";
            display = "#Status_Loading";
        }
        else if (GameManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (GameManager.Instance.IsGameEnded)
            {
                display = "#Status_MatchOver";
            }
            else
            {
                display = IsLocalPlayerDead() ? "#Status_Spectating" : "#Status_Playing";
                Set("alive", GameManager.Instance.AlivePlayersCount().ToString());
            }
        }
        else if (inLobby)
        {
            display = privateLobby ? "#Status_LobbyPrivate" : "#Status_Lobby";
            Set("members", lobby.Value.MemberCount.ToString());
            Set("max", lobby.Value.MaxMembers.ToString());
            joinable = !privateLobby && lobby.Value.MemberCount < lobby.Value.MaxMembers;
        }
        else
        {
            display = "#Status_MainMenu";
        }

        Set("map", map);
        Set("steam_display", display);

        // Plain-text fallback, shown where localized presence isn't (older clients, "view game info").
        Set("status", PlainText(display, map, lobby));

        if (inLobby)
        {
            Set("steam_player_group", lobby.Value.Id.Value.ToString());
            Set("steam_player_group_size", lobby.Value.MemberCount.ToString());
        }
        else
        {
            Set("steam_player_group", null);
            Set("steam_player_group_size", null);
        }

        Set("connect", joinable ? ConnectPrefix + lobby.Value.Id.Value : null);
    }

    private static bool IsLocalPlayerDead()
    {
        NetworkObject local = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
        return local != null && local.TryGetComponent(out Death death) && death.isDead.Value;
    }

    private static string PlainText(string token, string map, Lobby? lobby)
    {
        string members = lobby.HasValue ? $"{lobby.Value.MemberCount}/{lobby.Value.MaxMembers}" : "";
        return token switch
        {
            "#Status_Tutorial" => "In the tutorial",
            "#Status_Customizing" => "Customizing their duck",
            "#Status_Loading" => $"Loading into {map}",
            "#Status_MatchOver" => $"Match over on {map}",
            "#Status_Spectating" => $"Spectating on {map}",
            "#Status_Playing" => $"Playing on {map}",
            "#Status_LobbyPrivate" => $"In a private lobby ({members})",
            "#Status_Lobby" => $"In a lobby ({members})",
            _ => "In the main menu",
        };
    }

    // Only talks to Steam when a value actually changes.
    private void Set(string key, string value)
    {
        // An empty value removes the key.
        value ??= "";
        lastSent.TryGetValue(key, out string previous);
        if (previous == value)
        {
            return;
        }

        lastSent[key] = value;
        SteamFriends.SetRichPresence(key, value);
    }

    private void OnApplicationQuit()
    {
        if (SteamClient.IsValid)
        {
            SteamFriends.ClearRichPresence();
        }
    }
}
