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
        inp.text = "6";
    }

    private void ValidateInput(string input)
    {
        if (int.TryParse(input, out int maxPlayers))
        {
            if (maxPlayers < 2 || maxPlayers > 6)
            {
                inp.text = "6"; // Sets to 6 if input is out of range
            }
        }
        else
        {
            inp.text = "6"; // Sets to 6 if input is not a valid number
        }
    }
}
