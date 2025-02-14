using UnityEngine;

public class ToonColorChanger : MonoBehaviour
{
    public Color toonColor = Color.white;
    public float ShadeIntensity = 0.5f;
    public Renderer toonMaterial;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        toonMaterial.material.SetColor("_BaseColor", toonColor);
        toonMaterial.material.SetColor("_1st_ShadeColor", ShadeColor(toonColor));
    }

    public Color ShadeColor(Color color)
    {
        return new Color(color.r * (1 - ShadeIntensity), color.g * (1 - ShadeIntensity), color.b * (1 - ShadeIntensity), color.a);
    }
}
