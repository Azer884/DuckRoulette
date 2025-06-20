using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TutoBot : MonoBehaviour
{
    public Renderer[] renderers;
    public TextMeshProUGUI messageText;
    public Animator moveAnimator, bodyAnimator;
    public AudioSource audioSource;

    public void Accept(Color color)
    {
        foreach (var renderer in renderers)
        {
            renderer.material.SetColor("_Outline_Color", color);
        }
        messageText.color = color;
    }

    public void Move()
    {
        moveAnimator.enabled = true;
        bodyAnimator.SetTrigger("Walk");
    }
    public void Talk()
    {
        audioSource.Play();
    }
    public void StopMovement()
    {
        bodyAnimator.SetTrigger("Idle");
    }
}
