using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }
    [SerializeField] private float roundDuration = 30f;
    private readonly NetworkVariable<float> _remainingTime = new();
    private readonly NetworkVariable<int> _currentRoundId = new();
    private bool _isRoundActive;
    private bool _isTimerRunning;
    private bool _timeoutShotRequested;
    private Shooting _currentShooting;
    public float RemainingTime => _remainingTime.Value;
    public int CurrentRoundId => _currentRoundId.Value;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            _remainingTime.Value = roundDuration;
            _currentRoundId.Value = 0;
        }
    }
    private void Update()
    {
        if (!IsServer || !_isRoundActive || !_isTimerRunning)
        {
            return;
        }
        _remainingTime.Value = Mathf.Max(0f, _remainingTime.Value - Time.deltaTime);
        if (_remainingTime.Value > 0f)
        {
            return;
        }
        _remainingTime.Value = 0f;
        StopTimer();
        if (_timeoutShotRequested)
        {
            return;
        }
        _timeoutShotRequested = true;
        ForceTimeoutShot();
    }
    public void StartRound()
    {
        if (!IsServer || GameManager.Instance == null)
        {
            return;
        }
        if (GameManager.Instance.playerWithGun.Value == ulong.MaxValue)
        {
            return;
        }
        UnsubscribeFromCurrentShot();
        _currentRoundId.Value++;
        _remainingTime.Value = roundDuration;
        _isRoundActive = true;
        _timeoutShotRequested = false;
        SubscribeToCurrentShot();
        StartTimer();
    }
    public void EndRound()
    {
        if (!IsServer)
        {
            return;
        }
        StopTimer();
        _isRoundActive = false;
        _timeoutShotRequested = false;
        UnsubscribeFromCurrentShot();
    }
    public void StartTimer()
    {
        if (!IsServer)
        {
            return;
        }
        _isTimerRunning = true;
    }
    public void StopTimer()
    {
        if (!IsServer)
        {
            return;
        }
        _isTimerRunning = false;
    }
    private void SubscribeToCurrentShot()
    {
        if (NetworkManager.Singleton == null || GameManager.Instance == null)
        {
            return;
        }
        ulong gunHolderClientId = GameManager.Instance.playerWithGun.Value;
        if (gunHolderClientId == ulong.MaxValue || !NetworkManager.Singleton.ConnectedClients.TryGetValue(gunHolderClientId, out var client) || client.PlayerObject == null)
        {
            return;
        }
        _currentShooting = client.PlayerObject.GetComponent<Shooting>();
        if (_currentShooting != null)
        {
            _currentShooting.hasShot.OnValueChanged += OnCurrentShotChanged;
        }
    }
    private void UnsubscribeFromCurrentShot()
    {
        if (_currentShooting != null)
        {
            _currentShooting.hasShot.OnValueChanged -= OnCurrentShotChanged;
            _currentShooting = null;
        }
    }
    private void OnCurrentShotChanged(bool oldValue, bool newValue)
    {
        if (!IsServer || !_isRoundActive || !newValue)
        {
            return;
        }
        EndRound();
    }
    private void ForceTimeoutShot()
    {
        if (!IsServer || GameManager.Instance == null || NetworkManager.Singleton == null)
        {
            return;
        }
        ulong gunHolderClientId = GameManager.Instance.playerWithGun.Value;
        if (gunHolderClientId == ulong.MaxValue || !NetworkManager.Singleton.ConnectedClients.ContainsKey(gunHolderClientId))
        {
            return;
        }
        var clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new List<ulong> { gunHolderClientId }
            }
        };
        ForceTimeoutShotClientRpc(_currentRoundId.Value, clientRpcParams);
    }
    [ClientRpc]
    private void ForceTimeoutShotClientRpc(int roundId, ClientRpcParams clientRpcParams = default)
    {
        _ = clientRpcParams;
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null || GameManager.Instance == null)
        {
            return;
        }
        var localPlayerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayerObject == null || !localPlayerObject.TryGetComponent<Shooting>(out var shooting))
        {
            return;
        }
        if (NetworkManager.Singleton.LocalClientId != GameManager.Instance.playerWithGun.Value)
        {
            return;
        }
        shooting.ForceShoot(roundId);
    }
}
