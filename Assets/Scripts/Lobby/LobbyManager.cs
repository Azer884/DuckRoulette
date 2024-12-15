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

    [SerializeField] private GameObject multiMenu, multiLobby, joinMenu;
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
        if (string.IsNullOrWhiteSpace(inputField.text))
        {
            inputField.text = "";
            inputField.DeactivateInputField();
            return;
        }
        NetworkTransmission.instance.IWishToSendAChatServerRPC(inputField.text, myClientId, false);
        inputField.text = "";
    }

    public class Message
    {
        public string text;
        public TMP_Text textObject;
    }

    public void SendMessageToChat(string _text, ulong _fromwho, bool _server)
    {
        if(messageList.Count >= maxMessages)
        {
            Destroy(messageList[0].textObject.gameObject);
            messageList.Remove(messageList[0]);
        }
        Message newMessage = new();
        string _name = "Server";

        if (!_server)
        {
            if (playerInfo.ContainsKey(_fromwho))
            {
                _name = playerInfo[_fromwho].GetComponent<PlayerInfo>().steamName;
            }
        }

        newMessage.text = _name + ": " + _text;

        GameObject newText = Instantiate(textObject, chatPanel.transform);
        newMessage.textObject = newText.GetComponent<TMP_Text>();
        newMessage.textObject.text = newMessage.text;
        if (_server)
        {
            newMessage.textObject.color = Color.red;
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
    }

    public void RemovePlayerFromDictionary(ulong _steamId)
    {
        GameObject _value = null;
        ulong _key = 100;
        foreach(KeyValuePair<ulong,GameObject> _player in playerInfo)
        {
            if(_player.Value.GetComponent<PlayerInfo>().steamId == _steamId)
            {
                _value = _player.Value;
                _key = _player.Key;
            }
        }
        if(_key != 100)
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

    public void Quit()
    {
        Application.Quit();
    }
}
