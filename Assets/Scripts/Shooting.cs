using Unity.Netcode;
using UnityEngine;

public class Shooting : NetworkBehaviour
{
    public GameObject bulletPrefab;
    private GameObject bullet;
    public float bulletSpeed = 10f;
    public Transform spawnPt;
    public Transform cam;
    private PlayerInput inputActions;
    public Animator[] animators;
    public Animator bulletAnimator;
    public NetworkVariable<bool> hasShot = new(false);
    private bool canTrigger, canShoot, isTriggered;
    [SerializeField] private Transform targetAim;
    [SerializeField] private Hands fPHands;
    public GameObject gun;
    public NetworkVariable<bool> haveGun;
    [SerializeField] private Slap slapScript;

    void Awake()
    {
        inputActions = new PlayerInput();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner) enabled = false;
    }

    private void OnEnable()
    {
        EnableHasShotServerRpc(false);
        inputActions.Enable();
        hasShot.OnValueChanged += OnHasShotChangedServerRpc;

        HandsState(true);
        haveGun.Value = true;
        slapScript.enabled = false;
    }
    private void OnDisable()
    {
        inputActions.Disable();
        hasShot.OnValueChanged -= OnHasShotChangedServerRpc;
        
        HandsState(false);
        haveGun.Value = false;
        slapScript.enabled = true;
    }

    void Update()
    {
        Reload();
        Trigger();
        Shoot();
    }

    private void Reload()
    {
        if (inputActions.PlayerControls.Reload.triggered && !GameManager.Instance.isReloaded.Value)
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
        if (inputActions.PlayerControls.Trigger.triggered && !isTriggered && GameManager.Instance.isReloaded.Value && canTrigger)
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
        if (inputActions.PlayerControls.Shoot.triggered && canShoot && isTriggered)
        {
            if (GameManager.Instance.bulletPosition.Value == GameManager.Instance.randomBulletPosition.Value)
            {
                foreach (Animator animator in animators)
                {
                    animator.Play("Shooting");
                }
                // Notify the server to shoot and update hasShot on all clients
                ShootServerRpc();
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
    public void ShootServerRpc()
    {
        // Instantiate and spawn the bullet on the server
        GameManager.Instance.isReloaded.Value = false;
        bullet = Instantiate(bulletPrefab, spawnPt.position, Quaternion.identity);
        bullet.GetComponent<NetworkObject>().Spawn();

        if (bullet.TryGetComponent<Rigidbody>(out var bulletRigidbody))
        {
            Vector3 direction = (targetAim.position - spawnPt.position).normalized;
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
    }
    
}
