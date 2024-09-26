using UnityEngine;

[CreateAssetMenu(fileName = "Items", menuName = "Scriptable Objects/Items")]
public class Items : ScriptableObject
{
    public string name;
    public float value;
    public GameObject gameobject;
    public Sprite ui;   
}
