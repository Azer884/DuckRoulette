using UnityEngine;

[CreateAssetMenu(fileName = "Task", menuName = "Scriptable Objects/Task")]
public class Challenge : ScriptableObject
{
    public string taskName;
    public string taskDiscription;
    public enum Difficulty
    {
        Easy,
        Medium,
        Hard
    }
    public Difficulty difficulty;
    
    public enum TaskType
    {
        Useful,
        Useless, 
        ThreePlus
    }
    public TaskType taskType;
}
