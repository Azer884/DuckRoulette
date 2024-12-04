using TMPro;
using UnityEngine;

public class MaxPlayers : MonoBehaviour
{
    [SerializeField] private TMP_InputField inp;

    void Start()
    {
        inp.characterLimit = 1;
        inp.contentType = TMP_InputField.ContentType.IntegerNumber;
        inp.onEndEdit.AddListener(ValidateInput);
    }

    private void ValidateInput(string input)
    {
        if (int.TryParse(input, out int maxPlayers))
        {
            if (maxPlayers < 2)
            {
                inp.text = "2";
            }
            else if (maxPlayers > 6)
            {
                inp.text = "6";
            }
        }
        else
        {
            inp.text = "6"; // Sets to 6 if input is not a valid number
        }
    }
    private string ValidateInput(int maxPlayers)
    {
        string maxPlayersString = $"{maxPlayers}";
        if (maxPlayers < 2)
        {
            maxPlayersString = "2";
        }
        else if (maxPlayers > 6)
        {
            maxPlayersString = "6";
        }

        return maxPlayersString;
    }
    public void Up()
    {
        if (int.TryParse(inp.text, out int maxPlayers))
        {
            maxPlayers++;
            inp.text = ValidateInput(maxPlayers);
        }
    }
    public void Down()
    {
        if (int.TryParse(inp.text, out int maxPlayers))
        {
            maxPlayers--;
            inp.text = ValidateInput(maxPlayers);
        }
    }
}
