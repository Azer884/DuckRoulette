using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Measures real round-trip time over Netcode itself.
//
// The ping HUD used to ask the transport (NetworkTransport.GetCurrentRtt), but FacepunchTransport
// returns a hard-coded 0 and the bundled Facepunch.Steamworks build exposes no connection-status
// API to read Steam's own ping from - so every player saw "0 ms", always.
//
// Instead, each peer sends a small timestamped named message once a second and the other side
// echoes it straight back; the time until the echo arrives is the round trip.
//   - A client pings the host: that is the client's ping.
//   - The host pings every client: the host has no ping of its own (it IS the server), so it
//     shows the average of its clients' pings, which is what it actually experiences.
//
// Self-installing (RuntimeInitializeOnLoadMethod), so no scene or prefab has to carry it, and it
// re-registers its handlers for every new session.
public class NetworkPing : MonoBehaviour
{
    private const string RequestMessage = "DuckRoulette.PingRequest";
    private const string ReplyMessage = "DuckRoulette.PingReply";
    private const float Interval = 1f;
    // Exponential smoothing so one late packet doesn't make the number jump around.
    private const float Smoothing = 0.3f;

    private static NetworkPing instance;

    private CustomMessagingManager registeredWith;
    private float timer;
    private float clientPing = -1f;
    private readonly Dictionary<ulong, float> clientPings = new();

    /// <summary>Smoothed round-trip time in milliseconds, or -1 before the first measurement.
    /// On the host this is the average over connected clients (0 when alone).</summary>
    public static float CurrentPingMs
    {
        get
        {
            if (instance == null)
            {
                return -1f;
            }

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening)
            {
                return -1f;
            }

            if (!nm.IsServer)
            {
                return instance.clientPing;
            }

            if (instance.clientPings.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            foreach (float ping in instance.clientPings.Values)
            {
                sum += ping;
            }

            return sum / instance.clientPings.Count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (instance != null)
        {
            return;
        }

        GameObject go = new("NetworkPing") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        instance = go.AddComponent<NetworkPing>();
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null)
        {
            ResetSession();
            return;
        }

        // A new session brings a new CustomMessagingManager; handlers do not carry over.
        if (registeredWith != nm.CustomMessagingManager)
        {
            ResetSession();
            registeredWith = nm.CustomMessagingManager;
            registeredWith.RegisterNamedMessageHandler(RequestMessage, OnRequest);
            registeredWith.RegisterNamedMessageHandler(ReplyMessage, OnReply);
        }

        timer += Time.unscaledDeltaTime;
        if (timer < Interval)
        {
            return;
        }

        timer = 0f;
        SendPings(nm);
    }

    private void SendPings(NetworkManager nm)
    {
        double now = Time.realtimeSinceStartupAsDouble;

        if (!nm.IsServer)
        {
            if (nm.IsConnectedClient)
            {
                Send(NetworkManager.ServerClientId, RequestMessage, now);
            }
            return;
        }

        // Forget clients that have left, then ping everyone still here except the host itself.
        List<ulong> stale = null;
        foreach (ulong id in clientPings.Keys)
        {
            if (!nm.ConnectedClients.ContainsKey(id))
            {
                (stale ??= new List<ulong>()).Add(id);
            }
        }

        if (stale != null)
        {
            foreach (ulong id in stale)
            {
                clientPings.Remove(id);
            }
        }

        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (id != NetworkManager.ServerClientId)
            {
                Send(id, RequestMessage, now);
            }
        }
    }

    private void Send(ulong target, string message, double timestamp)
    {
        using FastBufferWriter writer = new(sizeof(double), Allocator.Temp);
        writer.WriteValueSafe(timestamp);
        registeredWith.SendNamedMessage(message, target, writer, NetworkDelivery.Unreliable);
    }

    // Echo the sender's own timestamp back untouched: the round trip is then measured entirely on
    // the sender's clock, so the two machines' clocks never have to agree.
    private void OnRequest(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out double timestamp);
        if (registeredWith != null)
        {
            Send(senderId, ReplyMessage, timestamp);
        }
    }

    private void OnReply(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out double timestamp);
        float rtt = (float)((Time.realtimeSinceStartupAsDouble - timestamp) * 1000.0);
        if (rtt < 0f || rtt > 10000f)
        {
            return;
        }

        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer)
        {
            clientPings[senderId] = clientPings.TryGetValue(senderId, out float previous)
                ? Mathf.Lerp(previous, rtt, Smoothing)
                : rtt;
        }
        else
        {
            clientPing = clientPing < 0f ? rtt : Mathf.Lerp(clientPing, rtt, Smoothing);
        }
    }

    private void ResetSession()
    {
        if (registeredWith != null)
        {
            try
            {
                registeredWith.UnregisterNamedMessageHandler(RequestMessage);
                registeredWith.UnregisterNamedMessageHandler(ReplyMessage);
            }
            catch
            {
                // The manager was already torn down with its session - nothing left to unregister.
            }
        }

        registeredWith = null;
        timer = 0f;
        clientPing = -1f;
        clientPings.Clear();
    }
}
