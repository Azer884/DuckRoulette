using UnityEngine;

[CreateAssetMenu(fileName = "Items", menuName = "Scriptable Objects/Items")]
public class Items : ScriptableObject
{
    public string itemName;
    public float value;
    public GameObject gameobject;
    public Sprite ui;   
}
