using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Steamworks;
using System.Threading.Tasks;
using UnityEngine.UI;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager instance;

    [SerializeField] private GameObject multiMenu, multiLobby;
    public GameObject joinMenu;
    public TextMeshProUGUI lobbyId;

    [SerializeField] private GameObject chatPanel, textObject;
    [SerializeField] private TMP_InputField inputField;

    [SerializeField] private GameObject playerFieldBox, playerCardPrefab;
    [SerializeField] private GameObject readyButton, notReadyButton, startButton, mapButton;
    public Toggle publicToggle, privateToggle, friendToggle;
    public GameObject lobbiesBox, lobbiesObj;

    public Dictionary<ulong, GameObject> playerInfo = new();
    public Transform playerObjContainer;

    [SerializeField]
    private int maxMessages = 20;

    private List<Message> messageList = new();

    public bool connected;
    public bool inGame;
    public bool isHost;
    public ulong myClientId;
    public Animator friendList;
    public GameObject offlinePlayerBox, offlinePlayer;

    public ulong? privateChatTargetId;
    public string privateChatTargetName;
    private string originalPlaceholderText;
    private bool placeholderCached;
    private ulong? lastWhispererId;
    private string lastWhispererName;

    private void Awake()
    {
        if (instance != null)
        {
            Destroy(this);
        }
        else
        {
            instance = this;
        }
    }

    private void Update()
    {
        if(inputField.text != "")
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                SendToChat();
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                inputField.ActivateInputField();
                inputField.text = " ";
            }
        }
    }

    public void SendToChat()
    {
        if (inputField == null)
        {
            Debug.LogWarning("Input field is null!");
            return;
        }

        if (string.IsNullOrWhiteSpace(inputField.text))
        {
            inputField.text = "";
            inputField.DeactivateInputField();
            return;
        }

        if (inputField.text.StartsWith("/msg ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = inputField.text.Substring("/msg ".Length).Trim();
            int spaceIndex = rest.IndexOf(' ');
            string username = spaceIndex < 0 ? rest : rest.Substring(0, spaceIndex);
            string message = spaceIndex < 0 ? "" : rest.Substring(spaceIndex + 1).Trim();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(message))
            {
                SendMessageToChat("Usage: /msg username message", 0, true);
                inputField.text = "";
                return;
            }

            bool found = false;
            foreach (KeyValuePair<ulong, GameObject> _player in playerInfo)
            {
                if (_player.Key == myClientId)
                {
                    continue;
                }
                if (_player.Value != null && _player.Value.TryGetComponent(out PlayerInfo playerInfoComponent) && string.Equals(playerInfoComponent.steamName, username, StringComparison.OrdinalIgnoreCase))
                {
                    NetworkTransmission.instance.SendPrivateChatServerRpc(message, _player.Key);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                SendMessageToChat("Player not found: " + username, 0, true);
            }
            inputField.text = "";
            return;
        }
        else if (inputField.text.StartsWith("/r ", StringComparison.OrdinalIgnoreCase))
        {
            string message = inputField.text.Substring("/r ".Length).Trim();

            if (string.IsNullOrWhiteSpace(message))
            {
                SendMessageToChat("Usage: /r message", 0, true);
                inputField.text = "";
                return;
            }

            if (!lastWhispererId.HasValue)
            {
                SendMessageToChat("No one has messaged you yet", 0, true);
                inputField.text = "";
                return;
            }

            NetworkTransmission.instance.SendPrivateChatServerRpc(message, lastWhispererId.Value);
            inputField.text = "";
            return;
        }
        else if (inputField.text.StartsWith("/kick ", StringComparison.OrdinalIgnoreCase))
        {
            string username = inputField.text.Substring("/kick ".Length).Trim();
            if (!isHost)
            {
                SendMessageToChat("Only the host can kick players", 0, true);
                inputField.text = "";
                return;
            }

            bool found = false;
            foreach (KeyValuePair<ulong, GameObject> _player in playerInfo)
            {
                if (_player.Key == myClientId)
                {
                    continue;
                }
                if (_player.Value != null && _player.Value.TryGetComponent(out PlayerInfo playerInfoComponent) && string.Equals(playerInfoComponent.steamName, username, StringComparison.OrdinalIgnoreCase))
                {
                    NetworkTransmission.instance.RequestKickServerRpc(_player.Key);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                SendMessageToChat("Player not found: " + username, 0, true);
            }
            inputField.text = "";
            return;
        }

        if (NetworkTransmission.instance != null)
        {
            if (privateChatTargetId.HasValue)
            {
                NetworkTransmission.instance.SendPrivateChatServerRpc(inputField.text, privateChatTargetId.Value);
                ClosePrivateChat();
            }
            else
            {
                NetworkTransmission.instance.IWishToSendAChatServerRPC(inputField.text, myClientId, false);
            }
        }
        else
        {
            Debug.LogWarning("NetworkTransmission instance is null!");
        }

        inputField.text = "";
    }

    public class Message
    {
        public string text;
        public TMP_Text textObject;
    }

    public void SendMessageToChat(string _text, ulong _fromwho, bool _server, bool _private = false)
    {
        // Safety checks
        if (messageList == null)
        {
            messageList = new();
        }

        if(messageList.Count >= maxMessages)
        {
            if (messageList[0]?.textObject != null)
            {
                Destroy(messageList[0].textObject.gameObject);
            }
            messageList.Remove(messageList[0]);
        }
        
        Message newMessage = new();
        string _name = "Server";

        if (!_server)
        {
            if (playerInfo != null && playerInfo.ContainsKey(_fromwho))
            {
                var playerCard = playerInfo[_fromwho];
                if (playerCard != null && playerCard.TryGetComponent(out PlayerInfo playerInfoComponent))
                {
                    _name = playerInfoComponent.steamName;
                }
            }
        }

        if (_private)
        {
            newMessage.text = "[Whisper] " + _name + ": " + _text;
        }
        else
        {
            newMessage.text = _name + ": " + _text;
        }

        if (textObject != null && chatPanel != null)
        {
            GameObject newText = Instantiate(textObject, chatPanel.transform);
            newMessage.textObject = newText.GetComponent<TMP_Text>();
            if (newMessage.textObject != null)
            {
                newMessage.textObject.text = newMessage.text;
                if (_server)
                {
                    newMessage.textObject.color = Color.red;
                }
                else if (_private)
                {
                    newMessage.textObject.color = new Color(0.7f, 0.3f, 1f);
                }
            }
        }

        messageList.Add(newMessage);
    }

    public void ClearChat()
    {
        messageList.Clear();
        GameObject[] chat = GameObject.FindGameObjectsWithTag("ChatMessage");
        foreach(GameObject chit in chat)
        {
            Destroy(chit);
        }
        Debug.Log("clearing chat");
    }
    public void ClearPlayerInfo()
    {
        playerInfo.Clear();
        GameObject[] playerCards = GameObject.FindGameObjectsWithTag("PlayerCard");
        foreach (GameObject card in playerCards)
        {
            Destroy(card);
        }
    }

    public void CopyId()
    {
        TextEditor textEditor = new()
        {
            text = lobbyId.text
        };
        textEditor.SelectAll();
        textEditor.Copy();
    }

    public void HostCreated()
    {
        multiMenu.SetActive(false);
        joinMenu.SetActive(false);
        multiLobby.SetActive(true);
        isHost = true;
        connected = true;

        foreach (Transform child in offlinePlayerBox.transform)
        {
            if(child.childCount > 0) Destroy(child.GetChild(0).gameObject);
        }

        friendList.gameObject.SetActive(true);
        friendList.Play("FriendListOtherWay");
    }

    public void ConnectedAsClient()
    {
        multiMenu.SetActive(false);
        joinMenu.SetActive(false);
        friendList.gameObject.SetActive(true);
        friendList.Play("FriendListOtherWay");
        multiLobby.SetActive(true);
        isHost = false;
        connected = true;

        foreach (Transform child in offlinePlayerBox.transform)
        {
            if(child.childCount > 0) Destroy(child.GetChild(0).gameObject);
        }

    }

    public void Disconnected()
    {
        playerInfo.Clear();
        GameObject[] playercards = GameObject.FindGameObjectsWithTag("PlayerCard");
        foreach(GameObject card in playercards)
        {
            Destroy(card);
        }

        multiMenu.SetActive(true);
        multiLobby.SetActive(false);
        readyButton.SetActive(true);
        notReadyButton.SetActive(false);
        isHost = false;
        connected = false;
        
        foreach (Transform child in offlinePlayerBox.transform)
        {
            if(child.childCount > 0) Destroy(child.GetChild(0).gameObject);
        }

        Instantiate(offlinePlayer, offlinePlayerBox.transform.GetChild(0));
        
        friendList.gameObject.SetActive(true);
        friendList.Play("FriendList");
    }

    public async Task AddPlayerToDictionaryAsync(ulong _cliendId, string _steamName, ulong _steamId)
    {
        if (!playerInfo.ContainsKey(_cliendId))
        {
            PlayerInfo _pi = Instantiate(playerCardPrefab, playerFieldBox.transform).GetComponent<PlayerInfo>();
            _pi.steamId = _steamId;
            _pi.steamName = _steamName;
            var image = await SteamFriends.GetLargeAvatarAsync(_steamId);
            _pi.profilePic.texture = SteamFriendsManager.GetTextureFromImage(image.Value);
            playerInfo.Add(_cliendId, _pi.gameObject);
        }
    }

    public void UpdateClients()
    {
        foreach(KeyValuePair<ulong,GameObject> _player in playerInfo)
        {
            ulong _steamId = _player.Value.GetComponent<PlayerInfo>().steamId;
            string _steamName = _player.Value.GetComponent<PlayerInfo>().steamName;
            ulong _clientId = _player.Key;

            NetworkTransmission.instance.UpdateClientsPlayerInfoClientRPC(_steamId, _steamName, _clientId);

        }
        CheckIfPlayersAreReady();
    }

    public void RemovePlayerFromDictionary(ulong _steamId)
    {
        GameObject _value = null;
        ulong _key = 0;
        bool found = false;
        foreach(KeyValuePair<ulong,GameObject> _player in playerInfo)
        {
            if(_player.Value.GetComponent<PlayerInfo>().steamId == _steamId)
            {
                _value = _player.Value;
                _key = _player.Key;
                found = true;
            }
        }
        if(found)
        {
            playerInfo.Remove(_key);
        }
        if(_value!= null)
        {
            Destroy(_value);
        }
    }

    public void ReadyButton(bool _ready)
    {
        NetworkTransmission.instance.IsTheClientReadyServerRPC(_ready, Coin.Instance.amount >= 5 && playerInfo.Count > 1, myClientId);
    }

    public bool CheckIfPlayersAreReady()
    {
        bool _ready = false;

        foreach(KeyValuePair<ulong,GameObject> _player in playerInfo)
        {
            if (!(_player.Value.GetComponent<PlayerInfo>().isReady && _player.Value.GetComponent<PlayerInfo>().haveEoughCoins))
            {
                startButton.SetActive(false);
                mapButton.SetActive(false);
                if (_player.Value.GetComponent<PlayerInfo>().isReady && !_player.Value.GetComponent<PlayerInfo>().haveEoughCoins)
                {
                    if (LobbySaver.instance.currentLobby?.MemberCount > 1)
                    {
                        NetworkTransmission.instance.IWishToSendAChatServerRPC(_player.Value.GetComponent<PlayerInfo>().steamName + " Don't have enough money", 0, true);
                    }
                    else
                    {
                        NetworkTransmission.instance.IWishToSendAChatServerRPC("Not enough players to start the game", 0, true);
                    }
                }
                return false;
            }
            else
            {
                startButton.SetActive(true);
                mapButton.SetActive(true);
                _ready = true;
            }
        }

        return _ready;
    }

    public void OpenPrivateChat(ulong targetClientId, string targetName)
    {
        privateChatTargetId = targetClientId;
        privateChatTargetName = targetName;

        if (inputField != null && inputField.placeholder != null)
        {
            if (!placeholderCached)
            {
                if (inputField.placeholder is TMP_Text originalPh)
                {
                    originalPlaceholderText = originalPh.text;
                }
                placeholderCached = true;
            }

            if (inputField.placeholder is TMP_Text ph)
            {
                ph.text = $"Message {targetName}...";
            }
        }

        SendMessageToChat($"-- Now whispering to {targetName}. Press Esc to return to public chat. --", 0, true);

        if (inputField != null)
        {
            inputField.ActivateInputField();
        }
    }

    public void ClosePrivateChat()
    {
        if (!privateChatTargetId.HasValue)
        {
            return;
        }

        if (inputField != null && inputField.placeholder is TMP_Text ph && placeholderCached)
        {
            ph.text = originalPlaceholderText;
        }

        privateChatTargetId = null;
        privateChatTargetName = null;

        SendMessageToChat("-- Back to public chat --", 0, true);
    }

    public void ReceivePrivateMessage(string _text, ulong _fromWho, ulong _toWho)
    {
        if (_fromWho != myClientId)
        {
            lastWhispererId = _fromWho;
            if (playerInfo.TryGetValue(_fromWho, out GameObject card) && card.TryGetComponent(out PlayerInfo playerInfoComponent))
            {
                lastWhispererName = playerInfoComponent.steamName;
            }
        }

        SendMessageToChat(_text, _fromWho, false, true);
    }

    public void Quit()
    {
        Application.Quit();
    }
}
