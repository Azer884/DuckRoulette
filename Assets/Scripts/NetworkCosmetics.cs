using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using Unity.Netcode;
using System.Collections;
using NUnit.Framework;

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

        hatIndex.OnValueChanged += (oldValue, newValue) => ChangeCosmetic(hats, shadowHat, hat, newValue);
        accessorieIndex.OnValueChanged += (oldValue, newValue) => ChangeCosmetic(accessories, shadowAcc, accessorie, newValue);
        shirtIndex.OnValueChanged += (oldValue, newValue) => ChangeCosmetic(shirts, shadowShirts, newValue);

            ChangeCosmetic(hats, shadowHat, hat, hatIndex.Value);
            ChangeCosmetic(accessories, shadowAcc, accessorie, accessorieIndex.Value);
            ChangeCosmetic(shirts, shadowShirts, shirtIndex.Value);
    }

    private void OnDisable() {
        hatIndex.OnValueChanged -= (oldValue, newValue) => ChangeCosmetic(hats, shadowHat, hat, newValue);
        accessorieIndex.OnValueChanged -= (oldValue, newValue) => ChangeCosmetic(accessories, shadowAcc, accessorie, newValue);
        shirtIndex.OnValueChanged -= (oldValue, newValue) => ChangeCosmetic(shirts, shadowShirts, newValue);
    }

    private void ChangeCosmetic(GameObject[] items, Transform shadowParent, Transform parent, int newValue)
    {
        Debug.Log($"Cosmetic index changed to {newValue}");

        if (newValue == 0) return;

        GameObject mainItem = Instantiate(items[newValue - 1], parent);
        GameObject shadowItem = Instantiate(items[newValue - 1], shadowParent);
        
        ApplyShadowOnlyMode(shadowItem);
        Movement.ChangeLayerRecursively(mainItem, IsOwner ? 2 : 3);
        Movement.ChangeLayerRecursively(shadowItem, IsOwner ? 3 : 2);
    }
    private void ChangeCosmetic(GameObject[] items, GameObject[] shadowitems, int newValue)
    {
        Debug.Log($"Cosmetic index changed to {newValue}");

        if (newValue == 0) return;

        GameObject mainItem = items[newValue - 1];
        GameObject shadowItem = shadowitems[newValue - 1];
        
        ApplyShadowOnlyMode(shadowItem);
        Movement.ChangeLayerRecursively(mainItem, IsOwner ? 2 : 3);
        Movement.ChangeLayerRecursively(shadowItem, IsOwner ? 3 : 2);

        mainItem.SetActive(true);
        shadowItem.SetActive(true);
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
