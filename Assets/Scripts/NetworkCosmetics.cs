using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using Unity.Netcode;
using System.Collections;

public class NetworkCosmetics : NetworkBehaviour
{
    [SerializeField] private Transform hat, accessorie, shirt;
    private GameObject[] hats, accessories, shirts;

    private NetworkVariable<int> hatIndex = new(0), accessorieIndex = new(0), shirtIndex = new(0);
    private int localHatIndex;
    private int localAccessorieIndex;
    private int localShirtIndex;
    private const string SaveFileName = "cosmeticData.txt";

    private IEnumerator DelayedLoadCosmeticIndexes()
    {
        Populate(hat, hats);
        Populate(accessorie, accessories);
        Populate(shirt, shirts);
        
        yield return new WaitForSeconds(0.1f); // adjust delay as needed
        LoadCosmeticIndexes();
    }

    public override void OnNetworkSpawn()
    {
        hatIndex.OnValueChanged += ChangeHat;
        accessorieIndex.OnValueChanged += ChangeAcc;
        shirtIndex.OnValueChanged += ChangeShirt;

        if (IsOwner)
        {
            StartCoroutine(DelayedLoadCosmeticIndexes());
        }
    }
    private void OnDisable() {
        hatIndex.OnValueChanged -= ChangeHat;
        accessorieIndex.OnValueChanged -= ChangeAcc;
        shirtIndex.OnValueChanged -= ChangeShirt;
    }
    private void ChangeHat(int oldValue, int newValue)
    {
        Debug.Log($"Hat index changed from {oldValue} to {newValue}");
        if (newValue != 0) hats[newValue - 1].SetActive(true);
    }

    private void ChangeAcc(int oldValue, int newValue)
    {
        Debug.Log($"Accessory index changed from {oldValue} to {newValue}");
        if (newValue != 0) accessories[newValue - 1].SetActive(true);
    }

    private void ChangeShirt(int oldValue, int newValue)
    {
        Debug.Log($"Shirt index changed from {oldValue} to {newValue}");
        if (newValue != 0) shirts[newValue - 1].SetActive(true);
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

        // Explicitly mark variables as dirty
        hatIndex.SetDirty(true);
        accessorieIndex.SetDirty(true);
        shirtIndex.SetDirty(true);
    }

    private void Populate(Transform parent, GameObject[] list)
    {
        int index = 0;
        foreach (Transform child in parent)
        {
            list[index] = child.gameObject;
            index++;
        }
    }

}
