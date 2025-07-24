using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Shooting : NetworkBehaviour
{
    public event Action OnGunShot;
    public GameObject bulletPrefab;
    private GameObject bullet;
    public Transform spawnPt;
    public Transform cam;
    private InputActionAsset inputActions;
    public Animator[] animators;
    public Animator bulletAnimator;
    public NetworkVariable<bool> hasShot = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public bool canTrigger, canShoot, isTriggered;
    [SerializeField] private Transform targetAim;
    [SerializeField] private Hands fPHands;
    public GameObject gun;
    public NetworkVariable<bool> haveGun = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    [SerializeField] private Slap slapScript;
    public int shotCounter = 0, emptyShots;
    [SerializeField] private GameObject vfxPrefab;

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) enabled = false;

        base.OnNetworkSpawn();
    }

    private void Awake() 
    {
        inputActions = GetComponent<InputSystem>().inputActions;
    }

    private void OnEnable()
    {
        hasShot.Value = false;

        hasShot.OnValueChanged += OnHasShotChangedServerRpc;

        HandsState(true);
        haveGun.Value = true;
    }
    private void OnDisable()
    {
        hasShot.OnValueChanged -= OnHasShotChangedServerRpc;
        
        HandsState(false);
        haveGun.Value = false;
    }

    [Obsolete]
    void Update()
    {
        Reload();
        Trigger();
        Shoot();
    }

    private void Reload()
    {
        if (inputActions.FindAction("Reload").triggered && !GameManager.Instance.isReloaded.Value && GameManager.Instance.canShoot.Value)
        {
            foreach (Animator animator in animators)
            {
                animator.Play("Reload");
            }
            bulletAnimator.Play("Reload");
            
            ReloadServerRpc();
        }
        if (animators[0].GetCurrentAnimatorStateInfo(0).IsName("Reload"))
        {
            canTrigger = false;
        }
        else
        {
            canTrigger = true;
        }
    }
    private void Trigger()
    {
        if (inputActions.FindAction("Trigger").triggered && !isTriggered && GameManager.Instance.isReloaded.Value && canTrigger && GameManager.Instance.canShoot.Value)
        {
            isTriggered = true;
            foreach (Animator animator in animators)
            {
                animator.SetBool("Triggered", isTriggered);
            }
        }
        if (animators[0].GetCurrentAnimatorStateInfo(0).IsName("Trigger"))
        {
            canShoot = false;
        }
        else
        {
            canShoot = true;
        }
    }

    private void Shoot()
    {
        if (inputActions.FindAction("Shoot").triggered && canShoot && isTriggered)
        {
            //if (!GameManager.Instance.powerGunIsActive.Value)
            {
                if (GameManager.Instance.bulletPosition.Value == GameManager.Instance.randomBulletPosition.Value)
                {
                    foreach (Animator animator in animators)
                    {
                        animator.Play("Shooting");
                    }
                    OnGunShot?.Invoke();
                    
                    // Notify the server to shoot and update hasShot on all clients
                    ShootServerRpc(spawnPt.position, Quaternion.identity, targetAim.position);
                    
                    shotCounter++;
                }
                hasShot.Value = true;
            }
            // else
            // {
            //     foreach (Animator animator in animators)
            //     {
            //         animator.Play("Shooting");
            //     }
            //     OnGunShot?.Invoke();
                
            //     // Notify the server to shoot and update hasShot on all clients
            //     ShootServerRpc(spawnPt.position, Quaternion.identity, targetAim.position, false);
                
            //     shotCounter++;
            // }

            for (int i = 0; i < animators.Length - 1; i++)
            {
                animators[i].Play("Shooting");
            }
            StartCoroutine(Triggering());
            
            emptyShots++;
        }
    }
    private System.Collections.IEnumerator Triggering()
    {
        // Wait until the "Shooting" animation has finished playing
        while (animators[2].GetCurrentAnimatorStateInfo(0).IsName("Shooting"))
        {
            yield return null;
        }
        isTriggered = false;
        foreach (Animator animator in animators)
        {
            animator.SetBool("Triggered", isTriggered);
        }
    }

    [ServerRpc]
    public void ShootServerRpc(Vector3 spawnPoint, Quaternion rot, Vector3 targetAim, bool haveToReload = true, ServerRpcParams serverRpcParams = default)
    {
        GameManager.Instance.isReloaded.Value = !haveToReload;

        bullet = Instantiate(bulletPrefab, spawnPoint, rot);
        var bulletNetworkObject = bullet.GetComponent<NetworkObject>();
        bulletNetworkObject.SpawnWithOwnership(serverRpcParams.Receive.SenderClientId);

        Vector3 direction = (targetAim - spawnPoint).normalized;

        if (bullet.TryGetComponent(out BulletBehavior bulletBehavior))
        {
            bulletBehavior.initialVelocity.Value = direction;
        }

        GameObject vfx = Instantiate(vfxPrefab, spawnPoint, rot);
        NetworkObject networkVfx = vfx.GetComponent<NetworkObject>();
        networkVfx.Spawn(); // Or SpawnWithOwnership if needed
        StartCoroutine(DestroyVfxAfterDelay(networkVfx, 1f));
    }

    [ServerRpc(RequireOwnership = false)]
    private void OnHasShotChangedServerRpc(bool oldValue, bool newValue)
    {
        // Call the GameManager function and pass the necessary parameters
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnClientShotChangedServerRpc(OwnerClientId, newValue);
        }
    }

    [ServerRpc]
    private void ReloadServerRpc()
    {
        if (GameManager.Instance)
        {
            GameManager.Instance.Reload();
        }
    }

    private void HandsState(bool state)
    {
        foreach (Animator anim in animators)
        {
            anim.SetBool("HaveAGun", state);
        }
        fPHands.SwitchParent(state);
        slapScript.enabled = !state;
    }

    private System.Collections.IEnumerator DestroyVfxAfterDelay(NetworkObject netObj, float delay)
    {
        yield return new WaitForSeconds(delay);
        netObj.Despawn();
    }
}
