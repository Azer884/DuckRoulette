using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public partial class TutorialManager
{
    public GameObject bulletPrefab, vfxPrefab;
    private GameObject bullet;
    public Transform spawnPt;
    public Animator bulletAnimator;
    public GameObject gun, shadowGun;
    public Transform withGunParent, withoutGunParent;
    public bool canTrigger, canShoot, isTriggered, isReloaded;
    private bool haveGun = false, onlySlap = false;
    private bool canSwitch = true;
    private int bulletPos = 0;
    private InputAction reloadAction, triggerAction, shootAction;

    private void CacheShootingInputActions()
    {
        reloadAction = inputActions.FindAction("Reload");
        triggerAction = inputActions.FindAction("Trigger");
        shootAction = inputActions.FindAction("Shoot");
    }

    private void Reload()
    {
        if (reloadAction.triggered && canShoot && !isReloaded)
        {
            isReloaded = true;
            bulletPos = 0;
            foreach (Animator animator in animators)
            {
                animator.Play("Reload");
            }
            if (!reloaded)
            {
                reloaded = true;
                OnReload?.Invoke();
            }
            bulletAnimator.Play("Reload");
        }
        if (animators[2].GetCurrentAnimatorStateInfo(0).IsName("Reload"))
        {
            canTrigger = false;
            canSwitch = false;
        }
        else
        {
            canTrigger = true;
            canSwitch = true;
        }
    }
    private void Trigger()
    {
        if (triggerAction.triggered && !isTriggered && canTrigger && canShoot && isReloaded)
        {
            isTriggered = true;
            foreach (Animator animator in animators)
            {
                animator.SetBool("Triggered", isTriggered);
            }
            if (!triggered)
            {
                triggered = true;
                OnTrigger?.Invoke();
            }
        }
        if (animators[2].GetCurrentAnimatorStateInfo(0).IsName("Trigger"))
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
        if (shootAction.triggered && canShoot && isTriggered && isReloaded)
        {
            if (bulletPos == 1)
            {
                foreach (Animator animator in animators)
                {
                    animator.Play("Shooting");
                }

                if (!gunShot)
                {
                    StartCoroutine(DisableGunWithDelay(2f));
                    OnGunShot?.Invoke();
                }
                Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                Vector3 pos;
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    pos = hit.point;
                }
                else
                {
                    pos = ray.GetPoint(100f);
                }

                ShootServerRpc(spawnPt.position, Quaternion.identity, pos);

            }
            bulletPos++;
            for (int i = 0; i < animators.Length - 1; i++)
            {
                animators[i].Play("Shooting");
            }
            StartCoroutine(Triggering());
        }
    }
    private IEnumerator Triggering()
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

    private IEnumerator DisableGunWithDelay(float delay = 1f)
    {
        yield return new WaitForSeconds(delay);

        gunShot = true;
        onlySlap = true;
        haveGun = false;
        SwitchParent(false);
    }
    public void ShootServerRpc(Vector3 spawnPoint, Quaternion rot, Vector3 targetAim)
    {
        bullet = Instantiate(bulletPrefab, spawnPoint, rot);

        Vector3 direction = (targetAim - spawnPoint).normalized;

        if (bullet.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = direction * 15f;
        }
        Destroy(bullet, 5f);
        GameObject vfx = Instantiate(vfxPrefab, spawnPoint, rot);
        Destroy(vfx, 1f);
    }
    public void SwitchParent(bool state)
    {
        gun.SetActive(state);
        shadowGun.SetActive(state);
        if (state)
        {
            Hands.transform.parent = withGunParent;
            Hands.transform.localPosition = Vector3.zero;
            Hands.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Hands.transform.parent = withoutGunParent;
            Hands.transform.localPosition = Vector3.zero;
            Hands.transform.localRotation = Quaternion.identity;
        }
    }
}
