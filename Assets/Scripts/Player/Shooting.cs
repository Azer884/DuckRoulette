using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class Shooting : NetworkBehaviour
{
    public event Action OnGunShot;
    public GameObject bulletPrefab;
    private GameObject bullet;
    public float bulletSpeed = 10f;
    public Transform spawnPt;
    public Transform cam;
    public InputActionAsset inputActions;
    public Animator[] animators;
    public Animator bulletAnimator;
    public NetworkVariable<bool> hasShot = new(false);
    private bool canTrigger, canShoot, isTriggered;
    [SerializeField] private Transform targetAim;
    [SerializeField] private Hands fPHands;
    public GameObject gun;
    public NetworkVariable<bool> haveGun = new(false,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    [SerializeField] private Slap slapScript;

    void Awake()
    {
        inputActions = RebindSaveLoad.Instance.actions;
        
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner) enabled = false;
    }

    private void OnEnable()
    {
        EnableHasShotServerRpc(false);
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
            if (GameManager.Instance.bulletPosition.Value == GameManager.Instance.randomBulletPosition.Value)
            {
                foreach (Animator animator in animators)
                {
                    animator.Play("Shooting");
                }
                RebindSaveLoad.Instance.RumbleGamepad(0.8f, 1f, 0f, 0.4f);
                OnGunShot?.Invoke();
                
                // Notify the server to shoot and update hasShot on all clients
                ShootServerRpc(spawnPt.position, Quaternion.identity, targetAim.position);
                
            }
            EnableHasShotServerRpc(true);

            isTriggered = false;
            foreach (Animator animator in animators)
            {
                animator.SetBool("Triggered", isTriggered);
            }
        }
    }

    [ServerRpc]
    public void ShootServerRpc(Vector3 spawnPoint, Quaternion rot, Vector3 targetAim)
    {
        // Instantiate and spawn the bullet on the server
        GameManager.Instance.isReloaded.Value = false;
        bullet = Instantiate(bulletPrefab, spawnPoint, rot);
        bullet.GetComponent<NetworkObject>().Spawn();

        if (bullet.TryGetComponent(out Rigidbody bulletRigidbody))
        {
            Vector3 direction = (targetAim - spawnPoint).normalized;
            bulletRigidbody.rotation = Quaternion.LookRotation(direction);
            bulletRigidbody.linearVelocity = direction * bulletSpeed;
        }
    }

    [ServerRpc]
    private void OnHasShotChangedServerRpc(bool oldValue, bool newValue)
    {
        // Call the GameManager function and pass the necessary parameters
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnClientShotChanged(OwnerClientId, newValue);
        }
    }

    [ServerRpc]
    private void EnableHasShotServerRpc(bool newValue)
    {
        hasShot.Value = newValue;
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
    
}
