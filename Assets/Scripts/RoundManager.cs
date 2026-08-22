using Unity.Netcode;
using UnityEngine;
public class RoundManager : NetworkBehaviour
{
    public static RoundManager Instance { get; private set; }
    [SerializeField] private float roundDuration = 30f;
    private readonly NetworkVariable<float> _remainingTime = new();
    // Networked mirror of _isRoundActive so clients can drive a shot-clock UI off it - the
    // plain bool below is server-only bookkeeping and was never visible to clients before.
    private readonly NetworkVariable<bool> _isRoundActiveNetworked = new(false);
    private bool _isRoundActive;
    private bool _isTimerRunning;
    private bool _timeoutHandled;
    private Shooting _currentShooting;
    public float RemainingTime => _remainingTime.Value;
    public bool IsRoundActive => _isRoundActiveNetworked.Value;
    public float RoundDuration => roundDuration;
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
        if (_timeoutHandled)
        {
            return;
        }
        _timeoutHandled = true;
        PassGunOnTimeout();
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
        _remainingTime.Value = roundDuration;
        _isRoundActive = true;
        _isRoundActiveNetworked.Value = true;
        _timeoutHandled = false;
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
        _isRoundActiveNetworked.Value = false;
        _timeoutHandled = false;
        UnsubscribeFromCurrentShot();
    }
    // Time's up: the gun holder didn't shoot, so nobody shoots - just hand the gun to another
    // player. Handled directly on the server instead of round-tripping a forced-shot ClientRpc
    // through the (possibly gone) gun holder's client, which was the previous approach.
    private void PassGunOnTimeout()
    {
        if (!IsServer || GameManager.Instance == null)
        {
            return;
        }
        EndRound();
        GameManager.Instance.PassGunOnTimeout();
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
}
