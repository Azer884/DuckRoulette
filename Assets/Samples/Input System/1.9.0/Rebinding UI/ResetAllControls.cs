using UnityEngine;
using UnityEngine.InputSystem.Samples.RebindUI;

public class ResetAllControls : MonoBehaviour 
{
    [SerializeField] private Transform parent;

    public void ResetAll()
    {
        foreach (Transform child in parent)
        {
            if (child.TryGetComponent(out RebindActionUI reseter))
            {
                reseter.ResetToDefault();
            }
        }
    }
}