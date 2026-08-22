using UnityEngine;
using Steamworks;
using Unity.Netcode;

public class NetworkCosmetics : NetworkBehaviour
{
    [SerializeField] private Transform hat, accessorie, shirt;
    [SerializeField] private Transform shadowHat, shadowAcc, shadowShirt;
    [SerializeField] private GameObject[] hats, accessories, shirts, shadowShirts;

    private NetworkVariable<int> hatIndex = new(0), accessorieIndex = new(0), shirtIndex = new(0);
    private int localHatIndex;
    private int localAccessorieIndex;
    private int localShirtIndex;
    private const string SaveFileName = "cosmeticData.txt";

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            LoadCosmeticIndexes();
        }

        hatIndex.OnValueChanged += OnHatIndexChanged;
        accessorieIndex.OnValueChanged += OnAccessorieIndexChanged;
        shirtIndex.OnValueChanged += OnShirtIndexChanged;

            ChangeCosmetic(hats, shadowHat, hat, hatIndex.Value);
            ChangeCosmetic(accessories, shadowAcc, accessorie, accessorieIndex.Value);
            ChangeCosmetic(shirts, shadowShirts, shirtIndex.Value);
    }

    private void OnDisable() {
        hatIndex.OnValueChanged -= OnHatIndexChanged;
        accessorieIndex.OnValueChanged -= OnAccessorieIndexChanged;
        shirtIndex.OnValueChanged -= OnShirtIndexChanged;
    }

    private void OnHatIndexChanged(int oldValue, int newValue) => ChangeCosmetic(hats, shadowHat, hat, newValue);
    private void OnAccessorieIndexChanged(int oldValue, int newValue) => ChangeCosmetic(accessories, shadowAcc, accessorie, newValue);
    private void OnShirtIndexChanged(int oldValue, int newValue) => ChangeCosmetic(shirts, shadowShirts, newValue);

    private void ChangeCosmetic(GameObject[] items, Transform shadowParent, Transform parent, int newValue)
    {
        Debug.Log($"Cosmetic index changed to {newValue}");

        foreach (Transform child in parent)
        {
            Destroy(child.gameObject);
        }

        // shadowParent is unassigned on avatars that don't have a first-person "shadow double"
        // rig (e.g. the lobby's Character prefab, which is only ever viewed from outside) - skip
        // the shadow half instead of throwing and aborting the whole cosmetic apply.
        if (shadowParent != null)
        {
            foreach (Transform child in shadowParent)
            {
                Destroy(child.gameObject);
            }
        }

        if (newValue == 0) return;

        GameObject mainItem = Instantiate(items[newValue - 1], parent);
        Movement.ChangeLayerRecursively(mainItem, IsOwner ? 2 : 3);

        if (shadowParent != null)
        {
            GameObject shadowItem = Instantiate(items[newValue - 1], shadowParent);
            ApplyShadowOnlyMode(shadowItem);
            Movement.ChangeLayerRecursively(shadowItem, IsOwner ? 3 : 2);
        }
    }
    private void ChangeCosmetic(GameObject[] items, GameObject[] shadowitems, int newValue)
    {
        Debug.Log($"Cosmetic index changed to {newValue}");

        if (newValue == 0) return;

        GameObject mainItem = items[newValue - 1];
        Movement.ChangeLayerRecursively(mainItem, IsOwner ? 2 : 3);
        mainItem.SetActive(true);

        // Same as above: shadowitems can be shorter than items (or empty) on avatars without a
        // shadow rig - skip the shadow half instead of indexing out of range.
        if (shadowitems != null && newValue - 1 < shadowitems.Length && shadowitems[newValue - 1] != null)
        {
            GameObject shadowItem = shadowitems[newValue - 1];
            ApplyShadowOnlyMode(shadowItem);
            Movement.ChangeLayerRecursively(shadowItem, IsOwner ? 3 : 2);
            shadowItem.SetActive(true);
        }
    }

    private void LoadCosmeticIndexes()
    {
        if (SteamRemoteStorage.FileExists(SaveFileName))
        {
            byte[] fileData = SteamRemoteStorage.FileRead(SaveFileName);
            if (fileData != null)
            {
                string data = System.Text.Encoding.UTF8.GetString(fileData);
                string[] values = data.Split(',');

                if (values.Length >= 3 &&
                    int.TryParse(values[0], out localHatIndex) &&
                    int.TryParse(values[1], out localAccessorieIndex) &&
                    int.TryParse(values[2], out localShirtIndex))
                {
                    Debug.Log("Cosmetic indexes loaded successfully from Steam Cloud.");
                    ChangeNetVarsServerRpc(localHatIndex, localAccessorieIndex, localShirtIndex);
                }
                else
                {
                    Debug.Log("Failed to parse cosmetic indexes from Steam Cloud.");
                }
            }
            else
            {
                Debug.Log("Failed to read file data from Steam Cloud.");
            }
        }
        else
        {
            Debug.Log("No cosmetic indexes save file found in Steam Cloud; using default values.");
            ChangeNetVarsServerRpc(0, 0, 0);
        }
    }

    [ServerRpc]
    private void ChangeNetVarsServerRpc(int index1, int index2, int index3)
    {
        hatIndex.Value = index1;
        accessorieIndex.Value = index2;
        shirtIndex.Value = index3;
    }

    private void ApplyShadowOnlyMode(GameObject item)
    {
        if (item.TryGetComponent<Renderer>(out var itemRend))
        {
            itemRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
        // Apply ShadowsOnly to all renderers in the item hierarchy
        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }
    }
}
