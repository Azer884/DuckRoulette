using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Steamworks;

public class FeedbackSender : MonoBehaviour
{
    const string formBaseUrl = "https://docs.google.com/forms/d/e/1FAIpQLSfyYqYpT5LGhY380fZdZMRh7Vg_fDWGynnWmzdMDmtvuFw6DQ/viewform?usp=pp_url";

    public void SendStatsAndOpenForm()
    {
        StatTracker s = StatTracker.Instance;
        s.FinalizeStats();

        Dictionary<string, string> formFields = new()
        {
            { "entry.351559539",      SteamClient.SteamId.ToString() },
            { "entry.355789309",      s.timeSurvived.ToString("F2") },
            { "entry.1604634731",     s.kills.ToString() },
            { "entry.1119847128",     s.coinsWon.ToString() },
            { "entry.792828744",      s.timeTeamedUp.ToString("F2") },
            { "entry.1604097517",     s.teamUpsCount.ToString() },
            { "entry.881170284",      s.exitTeamUpCount.ToString() },
            { "entry.475731609",      s.shotsCount.ToString() },
            { "entry.646491514",     s.emptyShotsCount.ToString() },
            { "entry.1690233609",     s.accuracy.ToString("F2") + "%"},
            { "entry.1126083093",     s.luck.ToString("F2") + "%"},
            { "entry.1713294138",     s.avgFPS.ToString("F1") },
            { "entry.528764704",      s.avgPing.ToString("F1") },
            { "entry.870008036",      s.slapsRecivedCount.ToString() },
            { "entry.1059864556",     s.slapsCount.ToString() },
            { "entry.1112608244",     s.percMapExplored.ToString("F1") + "%"}
        };

        string url = formBaseUrl;
        foreach (var field in formFields)
            url += $"&{field.Key}={UnityWebRequest.EscapeURL(field.Value)}";

        url += "&pageHistory=0,1,2";
        Application.OpenURL(url);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            SendStatsAndOpenForm();
        }
    }
    private void OnDestroy()
    {
        SendStatsAndOpenForm();
    }
}
