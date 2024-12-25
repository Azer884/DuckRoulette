using UnityEngine;
using Unity.Netcode;

public class DeathTrigger : NetworkBehaviour
{

    public void OnTriggerEnter(Collider other) 
    {
        Debug.Log("hit smt");
        if (!IsServer) return; 

        Debug.Log(other.name);
        if (other.TryGetComponent(out BulletBehavior bullet) && bullet.OwnerClientId != GetComponent<NetworkObject>().OwnerClientId)
        {
            Debug.Log("hit a player");
            // Trigger death ragdoll
            GetComponent<Ragdoll>().TriggerRagdoll(true);

            ulong shooterId = bullet.OwnerClientId;
            ulong victimId = GetComponent<NetworkObject>().OwnerClientId;

            // Fetch player names from the Username component
            string shooterName = GameManager.Instance.GetPlayerNickname(shooterId);
            string victimName = GameManager.Instance.GetPlayerNickname(victimId);

            Debug.Log($"{shooterName} killed {victimName}");
            GameManager.Instance.playersKills[(int)shooterId]++;

            // Notify GameManager about the death
            GameManager.Instance.UpdatePlayerState(victimId, isDead: true);
            Debug.Log($"Collision detected with {other.name}. Bullet Owner: {bullet.OwnerClientId}, Victim Owner: {GetComponent<NetworkObject>().OwnerClientId}");

            // Award coins to the shooter
            //UpdateCoinValueServerRpc(bullet.bulletId);
            bullet.DestroyNow();
        }
    }
}