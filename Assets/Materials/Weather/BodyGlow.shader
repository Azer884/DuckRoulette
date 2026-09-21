// Soft corona for the sun and the moon.
//
// A plain scaled-up sphere would read as a hard-edged ball around the body. This fades out
// towards the silhouette instead: alpha is driven by how much the surface faces the camera, so
// the glow is densest over the body's centre and gone by its rim. Additive and depth-writing off,
// so several of these stack without popping against the skybox.
//
// _GlowColor is HDR on purpose - values above 1 are what makes URP's bloom bleed the light out
// past the geometry, which is the part that actually reads as "a sun" rather than "a disc".
Shader "Weather/BodyGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.9, 0.6, 1)
        _Falloff ("Edge Falloff", Range(0.25, 8)) = 2.5
        _CoreBoost ("Core Boost", Range(0, 4)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            // Front faces only: the far hemisphere would double the brightness through the middle.
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Falloff;
                half _CoreBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positions.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                // pow() on the facing term: 1 dead centre, falling to 0 at the silhouette, and the
                // exponent controls how tight the corona is.
                half intensity = pow(facing, _Falloff) * _CoreBoost;
                return half4(_GlowColor.rgb * intensity * _GlowColor.a, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
