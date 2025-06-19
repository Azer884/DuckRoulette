using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class StatTracker : MonoBehaviour
{
    public static StatTracker Instance;
    public float timeSurvived;
    public int kills;
    public int coinsWon;
    public float timeTeamedUp;
    public int teamUpsCount;
    public int exitTeamUpCount;
    public int shotsCount;
    public int emptyShotsCount;
    public float accuracy;
    public float luck;
    public float avgFPS, currentFPS;
    public float avgPing, currentPing;
    public int slapsRecivedCount;
    public int slapsCount;
    public float percMapExplored;

    private NetworkObject playerObj;
    private TeamUp teamUpComponent;
    private bool isAlive = true;
    private float fpsSum, samples;
    private float pingSum;

    public Vector2 mapMin = new Vector2(0, 0);
    public Vector2 mapMax = new Vector2(100, 100);
    public float cellSize = 5f;
    private HashSet<Vector2Int> visitedCells = new HashSet<Vector2Int>();
    private int totalCells;
    private float mapSampleTimer = 0f;
    public float mapSampleInterval = 1f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(this);

            int cellsX = Mathf.CeilToInt((mapMax.x - mapMin.x) / cellSize);
            int cellsY = Mathf.CeilToInt((mapMax.y - mapMin.y) / cellSize);
            totalCells = cellsX * cellsY;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private IEnumerator WaitForPlayerSpawn()
    {
        while (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null || NetworkManager.Singleton.LocalClient.PlayerObject == null)
            yield return null;

        playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj.TryGetComponent(out Death deathComponent))
        {
            deathComponent.isDead.OnValueChanged += OnPlayerDeath;
        }
        if (playerObj.TryGetComponent(out Slap slapComponent))
        {
            slapComponent.OnSlapTriggered += () => slapsCount++;
            slapComponent.OnSlapRecived += () => slapsRecivedCount++;
        }
        if (playerObj.TryGetComponent(out teamUpComponent))
        {
            teamUpComponent.OnTeamUp += () => teamUpsCount++;
            teamUpComponent.OnExitTeamUp += () => exitTeamUpCount++;
        }
    }
    private void OnEnable()
    {
        StartCoroutine(WaitForPlayerSpawn());
    }

    private void OnPlayerDeath(bool previousValue, bool newValue)
    {
        isAlive = !newValue;
    }

    void Update()
    {
        if (playerObj != null)
        {
            TimeSurvived();
            TimeTeamedUp();

            SumFPS();
            SumPing();
            samples++;

            mapSampleTimer += Time.deltaTime;
            if (mapSampleTimer >= mapSampleInterval)
            {
                mapSampleTimer = 0f;
                SampleExploration();
            }
        }
    }
    private void TimeSurvived()
    {
        if (isAlive)
        {
            timeSurvived += Time.deltaTime;
        }
    }
    private void TimeTeamedUp()
    {
        if (teamUpComponent != null && teamUpComponent.isTeamedUp)
        {
            timeTeamedUp += Time.deltaTime;
        }
    }
    private void SumFPS()
    {
        currentFPS = 1f / Time.unscaledDeltaTime;
        fpsSum += currentFPS;
    }
    private void SumPing()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            currentPing = (NetworkManager.Singleton.ServerTime - NetworkManager.Singleton.LocalTime).Tick;
            pingSum += currentPing;
        }
    }
    private void SampleExploration()
    {
        Vector3 pos = playerObj.transform.position;
        if (pos.x >= mapMin.x && pos.x <= mapMax.x && pos.z >= mapMin.y && pos.z <= mapMax.y)
        {
            var cell = new Vector2Int(
                Mathf.FloorToInt((pos.x - mapMin.x) / cellSize),
                Mathf.FloorToInt((pos.z - mapMin.y) / cellSize)
            );
            visitedCells.Add(cell);
        }
    }

    public void FinalizeStats()
    {
        avgFPS = samples > 0 ? fpsSum / samples : 0f;
        avgPing = samples > 0 ? pingSum / samples : 0f;

        if (playerObj.TryGetComponent(out Shooting shootingComponent))
        {
            shotsCount = shootingComponent.shotCounter;
            emptyShotsCount = shootingComponent.emptyShots;
            accuracy = shotsCount > 0 ? kills / (float)shotsCount * 100f : 0f;
            luck = emptyShotsCount > 0 ? shotsCount / (float)emptyShotsCount * 100f : 0f;
        }
        percMapExplored = totalCells > 0 ? visitedCells.Count / (float)totalCells * 100f : 0f;
    }

    public void OnDestroy()
    {
        if (playerObj != null)
        {
            if (playerObj.TryGetComponent(out Death deathComponent))
            {
                deathComponent.isDead.OnValueChanged -= OnPlayerDeath;
            }
            if (playerObj.TryGetComponent(out Slap slapComponent))
            {
                slapComponent.OnSlapTriggered -= () => slapsCount++;
                slapComponent.OnSlapRecived -= () => slapsRecivedCount++;
            }
            if (playerObj.TryGetComponent(out teamUpComponent))
            {
                teamUpComponent.OnTeamUp -= () => teamUpsCount++;
                teamUpComponent.OnExitTeamUp -= () => exitTeamUpCount++;
            }
        }
    }

}
