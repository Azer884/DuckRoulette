using System;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;

public class Cosmetics : MonoBehaviour
{
    [SerializeField] private Transform hatHolder, accessorieHolder, shirtHolder;
    [SerializeField] private List<GameObject> hats, accessories, shirts;

    private int hatIndex, accessorieIndex, shirtIndex;

    private const string SaveFileName = "cosmeticData.txt";

    private void Start()
    {
        LoadCosmeticIndexes();
    }

    public void ChangeHat(string symbol)
    {
        if (symbol == "-")
        {
            hatIndex--;
            if (hatIndex < 0)
            {
                hatIndex = hats.Count;
            }
        }
        else
        {
            hatIndex++;
            hatIndex %= hats.Count + 1;
        }

        Change(hats, hatHolder, hatIndex);
        SaveCosmeticIndexes();
    }

    public void ChangeAccessorie(string symbol)
    {
        if (symbol == "-")
        {
            accessorieIndex--;
            if (accessorieIndex < 0)
            {
                accessorieIndex = accessories.Count;
            }
        }
        else
        {
            accessorieIndex++;
            accessorieIndex %= accessories.Count + 1;
        }

        Change(accessories, accessorieHolder, accessorieIndex);
        SaveCosmeticIndexes();
    }

    public void ChangeShirt(string symbol)
    {
        if (symbol == "-")
        {
            shirtIndex--;
            if (shirtIndex < 0)
            {
                shirtIndex = shirts.Count;
            }
        }
        else
        {
            shirtIndex++;
            shirtIndex %= shirts.Count + 1;
        }

        Change(shirts, shirtHolder, shirtIndex);
        SaveCosmeticIndexes();
    }

    private void Change(List<GameObject> list, Transform holder, int index)
    {
        foreach (Transform child in holder)
        {
            Destroy(child.gameObject);
        }

        if (index != 0)
        {
            Instantiate(list[index - 1], holder);
        }
    }

    public void SaveCosmeticIndexes()
    {
        string data = $"{hatIndex},{accessorieIndex},{shirtIndex}";

        // Use SteamRemoteStorage to save data to Steam Cloud
        bool success = SteamRemoteStorage.FileWrite(SaveFileName, System.Text.Encoding.UTF8.GetBytes(data));
        if (!success)
        {
            Debug.Log("Failed to save cosmetic indexes to Steam Cloud.");
        }
        else
        {
            Debug.Log("Cosmetic indexes saved successfully to Steam Cloud.");
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
                    int.TryParse(values[0], out hatIndex) &&
                    int.TryParse(values[1], out accessorieIndex) &&
                    int.TryParse(values[2], out shirtIndex))
                {
                    Debug.Log("Cosmetic indexes loaded successfully from Steam Cloud.");
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
            hatIndex = accessorieIndex = shirtIndex = 0;
        }

        // Apply loaded cosmetics
        Change(hats, hatHolder, hatIndex);
        Change(accessories, accessorieHolder, accessorieIndex);
        Change(shirts, shirtHolder, shirtIndex);
    }
}
