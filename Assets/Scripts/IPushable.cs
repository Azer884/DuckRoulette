using UnityEngine;

/// <summary>
/// A rigidbody that handles player pushes itself instead of taking PlayerPushObject's generic
/// impulse. Called on the server only, after PlayerPushObject has validated the contact.
/// </summary>
public interface IPushable
{
    /// <param name="pusherId">Client that pushed.</param>
    /// <param name="pusherPosition">Server's view of the pusher's position.</param>
    /// <param name="hitPoint">Contact point on this object.</param>
    void OnPushed(ulong pusherId, Vector3 pusherPosition, Vector3 hitPoint);
}
