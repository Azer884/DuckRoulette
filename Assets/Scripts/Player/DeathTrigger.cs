using UnityEngine;
using Unity.Netcode;
using System.Collections;
using Unity.Cinemachine;

public class DeathTrigger : MonoBehaviour
{
    private ulong victimId;
    private ulong spectatedPlayerId;
    private CinemachineCamera spectatorCamera;
    private Death death;

    private void Awake()
    {
        death = GetComponentInParent<Death>();
    }

    public void OnTriggerEnter(Collider other) 
    {
        victimId = GetComponentInParent<NetworkObject>().OwnerClientId;

        if (other.transform.parent.TryGetComponent(out BulletBehavior bullet) && bullet.OwnerClientId != victimId && !death.isDead.Value)
        {
            if (GetComponentInParent<TeamUp>().isTeamedUp && (int)bullet.OwnerClientId == GetComponentInParent<TeamUp>().teamMateId)
            {
                return;
            }
            death.DieServerRpc();
            GetComponentInParent<Ragdoll>().TriggerRagdoll(true);

            ulong shooterId = bullet.OwnerClientId;
            spectatedPlayerId = shooterId;

            // Fetch player names from the Username component
            string shooterName = GameManager.Instance.GetPlayerNickname(shooterId);
            string victimName = GameManager.Instance.GetPlayerNickname(victimId);

            Debug.Log($"{shooterName} killed {victimName}");
            GameManager.Instance.UpdateKillsServerRpc(shooterId, 1);

            // Notify GameManager about the death
            GameManager.Instance.UpdatePlayerStateServerRpc(victimId);
            Debug.Log($"Collision detected with {other.name}. Bullet Owner: {bullet.OwnerClientId}, Victim Owner: {victimId}");

            StartCoroutine(WaitBeforeSpctate(5f));
        }
        bullet.DestroyServerRpc(0);
    }

    private IEnumerator WaitBeforeSpctate(float delay)
    {
        yield return new WaitForSeconds(delay);

        Spectate(spectatedPlayerId);
    }

    private void Spectate(ulong playerId)
    {
        spectatorCamera = GameManager.Instance.GetPlayerSpectateCam(playerId);
        if(spectatorCamera == null)
        {
            return;
        }
        spectatorCamera.Priority = 20;
    }
    private void EndSpectate(ulong playerId)
    {
        if(spectatorCamera == null)
        {
            return;
        }
        spectatorCamera.Priority = 0;
    }

    private void Update() {
        if (death.isDead.Value &&
            Input.GetKeyDown(KeyCode.Space))
        {
            EndSpectate(spectatedPlayerId);
            spectatedPlayerId++;
            spectatedPlayerId %= (ulong)NetworkManager.Singleton.ConnectedClientsList.Count;
            Spectate(spectatedPlayerId);
        }
    }
}