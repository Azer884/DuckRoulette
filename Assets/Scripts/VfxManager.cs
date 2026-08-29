using System;
using System.Collections.Generic;
using UnityEngine;

// Central home for VFX prefabs shared across the game, mirroring SFXManager's role for audio
// clips. Individual scripts that legitimately need a different asset per scene/context (e.g.
// TutorialManager's offline muzzle flash, Tutorial's own run-dust prefab) keep a local override
// field and fall back to this manager's default when that field is left unset.
public class VfxManager : MonoBehaviour
{
    public static VfxManager Instance { get; private set; }

    [Serializable]
    public class SurfaceVfxEntry
    {
        public string surfaceTag;
        public string physicMaterialName;
        public GameObject vfxPrefab;
    }

    [Header("Slap Impact")]
    public GameObject slapImpactVfxPrefab;
    public float slapImpactVfxLifetime = 1.5f;

    [Header("Shooting Muzzle")]
    public GameObject shootMuzzleVfxPrefab;

    [Header("Ground Parry")]
    public GameObject groundParryVfxPrefab;
    public float groundParryVfxScale = 1f;
    public float groundParryVfxLifetime = 1.5f;

    [Header("Ground Dust (Run / Footstep default)")]
    public GameObject defaultGroundVfxPrefab;
    public List<SurfaceVfxEntry> groundVfxBySurface = new();

    [Header("Bullet Impact")]
    public GameObject bulletImpactVfxPrefab;
    public float bulletImpactVfxLifetime = 1.5f;

    [Header("Death")]
    public GameObject deathVfxPrefab;
    public float deathVfxLifetime = 2f;

    [Header("Slap Stun")]
    public GameObject stunVfxPrefab;
    public float stunVfxLifetime = 2f;
    public Vector3 stunVfxHeadOffset = new(0f, 1.8f, 0f);

    [Header("Team Up Dap")]
    public GameObject teamUpDapVfxPrefab;
    public float teamUpDapVfxLifetime = 1.5f;

    // Shared by every simple "spawn a one-shot particle burst here, clean it up after a fixed
    // lifetime" caller (bullet impact, death, stun, team-up dap) so each doesn't reimplement the
    // Instantiate/Clear/Play/Destroy boilerplate.
    public static GameObject SpawnOneShot(GameObject prefab, Vector3 position, float lifetime)
    {
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = Instantiate(prefab, position, Quaternion.identity);
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Clear(true);
            ps.Play(true);
        }

        Destroy(instance, lifetime);
        return instance;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Shared by Movement's run-vfx and FootStepScript's footstep-vfx so both features tag the
    // same ground the same way. instanceDefaultOverride lets a caller (e.g. the Tutorial rig)
    // keep its own default without a per-surface match, falling back to defaultGroundVfxPrefab
    // only when it doesn't supply one either.
    public GameObject ResolveGroundVfx(RaycastHit hit, GameObject instanceDefaultOverride = null)
    {
        for (int i = 0; i < groundVfxBySurface.Count; i++)
        {
            SurfaceVfxEntry entry = groundVfxBySurface[i];
            if (entry == null || entry.vfxPrefab == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(entry.surfaceTag) && hit.collider.CompareTag(entry.surfaceTag))
            {
                return entry.vfxPrefab;
            }

            if (!string.IsNullOrWhiteSpace(entry.physicMaterialName) && hit.collider.sharedMaterial != null &&
                string.Equals(hit.collider.sharedMaterial.name, entry.physicMaterialName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.vfxPrefab;
            }
        }

        return instanceDefaultOverride != null ? instanceDefaultOverride : defaultGroundVfxPrefab;
    }

    public GameObject ResolveGroundVfx(string surfaceTag, string physicMaterialName, GameObject instanceDefaultOverride = null)
    {
        for (int i = 0; i < groundVfxBySurface.Count; i++)
        {
            SurfaceVfxEntry entry = groundVfxBySurface[i];
            if (entry == null || entry.vfxPrefab == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(surfaceTag) && !string.IsNullOrWhiteSpace(entry.surfaceTag) &&
                string.Equals(surfaceTag, entry.surfaceTag, StringComparison.OrdinalIgnoreCase))
            {
                return entry.vfxPrefab;
            }

            if (!string.IsNullOrWhiteSpace(physicMaterialName) && !string.IsNullOrWhiteSpace(entry.physicMaterialName) &&
                string.Equals(physicMaterialName, entry.physicMaterialName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.vfxPrefab;
            }
        }

        return instanceDefaultOverride != null ? instanceDefaultOverride : defaultGroundVfxPrefab;
    }
}
