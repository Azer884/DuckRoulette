using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Audio;
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
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip triggerClip;
    [SerializeField] private AudioClip shootClip;
    [SerializeField] private AudioClip emptyShotClip;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

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

    [Obsolete("This is a necessary Update method for handling input. Do not remove.")]
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
            PlayReloadSound(gun != null ? gun.transform.position : transform.position);

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
            PlayTriggerSound(gun != null ? gun.transform.position : transform.position);

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
            // Check if this is a valid hit
            bool isValidShot = GameManager.Instance.bulletPosition.Value == GameManager.Instance.randomBulletPosition.Value;
            
            // Play shooting animation once
            
            if (isValidShot)
            {
                foreach (Animator animator in animators)
                {
                    animator.Play("Shooting");
                }
                
                OnGunShot?.Invoke();
                if (!CanUseNetcode())
                {
                    PlayShootSound(spawnPt != null ? spawnPt.position : transform.position);
                }
                
                // Notify the server to shoot and update hasShot on all clients
                ShootServerRpc(spawnPt.position, Quaternion.identity, targetAim.position);
                
                shotCounter++;
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    animators[i].Play("Shooting");
                }
                
                // This was an empty shot
                emptyShots++;
                PlayEmptyShotSound(spawnPt != null ? spawnPt.position : transform.position);
            }
            
            hasShot.Value = true;
            StartCoroutine(Triggering());
        }
    }
    private System.Collections.IEnumerator Triggering()
    {
        // Wait until the "Shooting" animation has finished playing
        while (animators[4].GetCurrentAnimatorStateInfo(0).IsName("Shooting"))
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
        StartCoroutine(DestroyVfxAfterDelay(networkVfx, GetVfxLifetime(vfx)));

        PlayShootSoundClientRpc(spawnPoint);
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

    private void PlayReloadSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayReloadSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(reloadClip, position);
    }

    private void PlayTriggerSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayTriggerSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(triggerClip, position);
    }

    private void PlayShootSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayShootSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(shootClip, position);
    }

    private void PlayEmptyShotSound(Vector3 position)
    {
        if (CanUseNetcode())
        {
            PlayEmptyShotSoundServerRpc(position);
            return;
        }

        PlayLocalOneShot(emptyShotClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayReloadSoundServerRpc(Vector3 position)
    {
        PlayReloadSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayReloadSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(reloadClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayTriggerSoundServerRpc(Vector3 position)
    {
        PlayTriggerSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayTriggerSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(triggerClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayShootSoundServerRpc(Vector3 position)
    {
        PlayShootSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayShootSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(shootClip, position);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayEmptyShotSoundServerRpc(Vector3 position)
    {
        PlayEmptyShotSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayEmptyShotSoundClientRpc(Vector3 position)
    {
        PlayLocalOneShot(emptyShotClip, position);
    }

    private void PlayLocalOneShot(AudioClip clip, Vector3 position)
    {
        if (clip == null)
        {
            return;
        }

        GameObject audioObject = new GameObject($"{clip.name}_OneShot");
        audioObject.transform.position = position;

        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.clip = clip;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.playOnAwake = false;
        audioSource.outputAudioMixerGroup = sfxMixerGroup;
        audioSource.Play();

        Destroy(audioObject, clip.length);
    }

    private bool CanUseNetcode()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
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

    private float GetVfxLifetime(GameObject vfx)
    {
        if (vfx != null)
        {
            ParticleSystem ps = vfx.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                return Mathf.Max(main.duration, main.startLifetime.constantMax) + 0.25f;
            }
        }

        return 1f;
    }
}
