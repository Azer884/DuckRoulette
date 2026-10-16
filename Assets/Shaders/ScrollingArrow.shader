// Unlit, transparent, and scrolls its texture along U by itself, so a LineRenderer in Tile mode
// gets arrows that crawl along the line in an endless loop (TruckPushObjective's path to the
// river). URP's particle shaders ignore texture offset, which is why the old arrows never moved.
// With no texture (white) and _ScrollSpeed 0 it is a plain glowing strip - the goal ring uses it
// that way.
Shader "DuckRoulette/ScrollingArrow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (2.4, 1.9, 0.35, 1)
        _ScrollSpeed ("Scroll Speed (tiles per second, + = towards the line's end)", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ScrollingArrow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _ScrollSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                // Subtracting moves the pattern towards +U, i.e. along the line.
                output.uv.x -= frac(_Time.y * _ScrollSpeed);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return tex * _Color * input.color;
            }
            ENDHLSL
        }
    }
}
