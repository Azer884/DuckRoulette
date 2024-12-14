using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    private void Update() {
        ReassignChildrenToPrioritizedSlots();
    }

    public void ReassignChildrenToPrioritizedSlots()
    {
        int firstEmptySlot = -1; // Track the first available empty slot
        
        // Iterate through the slots in order
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform slot = transform.GetChild(i);

            if (slot.childCount > 0) // Slot has a child
            {
                // Ensure the child is properly aligned
                Transform child = slot.GetChild(0);
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
            }
            else
            {
                // Found an empty slot
                if (firstEmptySlot == -1) firstEmptySlot = i;
            }

            // Move children from lower-priority slots to fill the firstEmptySlot
            if (firstEmptySlot != -1 && i > firstEmptySlot && slot.childCount > 0)
            {
                Transform childToMove = slot.GetChild(0);
                childToMove.SetParent(transform.GetChild(firstEmptySlot));
                childToMove.localPosition = Vector3.zero;
                childToMove.localRotation = Quaternion.identity;

                firstEmptySlot++; // Advance to the next empty slot
            }
        }
    }
}

