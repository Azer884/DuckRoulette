using UnityEngine;
using Unity.Netcode;

public class DeathTrigger : MonoBehaviour
{
    private bool isDead = false;
    private ulong victimId;

    public void OnTriggerEnter(Collider other) 
    {
        victimId = GetComponentInParent<NetworkObject>().OwnerClientId;

        if (other.transform.parent.TryGetComponent(out BulletBehavior bullet) && bullet.OwnerClientId != victimId && !isDead)
        {
            isDead = false;
            GetComponentInParent<Ragdoll>().TriggerRagdoll(true);

            ulong shooterId = bullet.OwnerClientId;

            // Fetch player names from the Username component
            string shooterName = GameManager.Instance.GetPlayerNickname(shooterId);
            string victimName = GameManager.Instance.GetPlayerNickname(victimId);

            Debug.Log($"{shooterName} killed {victimName}");
            GameManager.Instance.UpdateKillsServerRpc(shooterId, 1);

            // Notify GameManager about the death
            GameManager.Instance.UpdatePlayerState(victimId, isDead: true);
            Debug.Log($"Collision detected with {other.name}. Bullet Owner: {bullet.OwnerClientId}, Victim Owner: {victimId}");

            // Award coins to the shooter
            //UpdateCoinValueServerRpc(bullet.bulletId);
        }
        bullet.DestroyServerRpc(0);
    }
}