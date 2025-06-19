using System;
using UnityEngine;

public class TutoBot : MonoBehaviour
{
    public Renderer[] renderers;

    public void Accept(Color color)
    {
        foreach (var renderer in renderers)
        {
            renderer.material.SetColor("_Outline_Color", color);
        }
    }
}
