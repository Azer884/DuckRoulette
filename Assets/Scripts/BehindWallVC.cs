using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;
using Unity.Netcode;

public class PlayerSound : NetworkBehaviour
{
    public List<Transform> otherPlayers; // List of other players in the game
    public LayerMask wallLayer;    // Layer mask for walls or obstacles
    public AudioLowPassFilter lowPassFilter;

    private readonly float maxDistance = 50f; // Max distance for sound checks
    private float maxDistanceSquared; // Precompute squared distance for optimization
    private readonly float checkInterval = 0.5f; // Check every half second
    private float nextCheckTime = 0f;

    void Start()
    {
        maxDistanceSquared = maxDistance * maxDistance; // Precompute the squared distance
    }

    void Update()
    {
        if (IsOwner && Time.time >= nextCheckTime)
        {
            CheckPlayersBehindWalls();
            nextCheckTime = Time.time + checkInterval; // Schedule next check
        }
    }

    void CheckPlayersBehindWalls()
    {
        foreach (Transform otherPlayer in otherPlayers)
        {
            if (otherPlayer != null && otherPlayer != transform)
            {
                // Use squared magnitude for distance checks
                float distanceSquared = (otherPlayer.position - transform.position).sqrMagnitude;

                if (distanceSquared <= maxDistanceSquared)
                {
                    bool isBehindWall = IsPlayerBehindWall(otherPlayer);

                    if (isBehindWall)
                    {
                        EnableLowPassFilter();
                    }
                    else
                    {
                        DisableLowPassFilter();
                    }
                }
            }
        }
    }

    bool IsPlayerBehindWall(Transform targetPlayer)
    {
        Vector3 direction = targetPlayer.position - transform.position;
        float distance = Vector3.Distance(transform.position, targetPlayer.position);
        Vector3 rayOrigin = transform.position + Vector3.up * 1.0f; // Chest level

        // SphereCast instead of multiple raycasts
        float radius = 0.5f; // Adjust the radius to the size of the gap you expect
        if (Physics.SphereCast(rayOrigin, radius, direction, out RaycastHit hit, distance, wallLayer))
        {
            if (hit.collider.CompareTag("Wall"))
            {
                return true; // Behind a wall
            }
        }

        return false; // No obstruction
    }

    void EnableLowPassFilter()
    {
        lowPassFilter.enabled = true;
    }

    void DisableLowPassFilter()
    {
        lowPassFilter.enabled = false;
    }
}
