using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    public List<Transform> characters = new List<Transform>(); // List of characters to position
    public static GridManager Instance;

    private void Awake()
    {
        if(Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Update()
    {
        ReassignCharactersToPrioritizedSlots();
    }

    public void ReassignCharactersToPrioritizedSlots()
    {
        int characterIndex = 0; // Track which character is being assigned

        // Iterate through all slots in order
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform slot = transform.GetChild(i);

            if (characterIndex < characters.Count)
            {
                // Make the character follow the slot's position and rotation
                if (characters[characterIndex] != null)
                {
                    Transform character = characters[characterIndex];

                    character.position = slot.position;
                    character.rotation = slot.rotation;
    
                    characterIndex++; // Move to the next character
                }
                else
                {
                    characters.Remove(characters[characterIndex]);
                }
            }
        }
    }
}
