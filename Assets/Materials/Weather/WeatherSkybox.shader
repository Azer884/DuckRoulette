// Panoramic skybox with three slots that cross-fade instead of one texture that snaps.
//
// Unity's stock Skybox/Panoramic has a single _MainTex, so "change the sky for the weather" can
// only ever mean swapping the material - which pops. This samples a day, a night and a storm
// panorama and blends between them, so DayNightCycle can drive dusk and a rolling storm as two
// independent 0..1 values and every transition is a lerp.
//
// _NightTex and _StormTex default to the day texture, so a project that only has one panorama
// degrades to tint-and-exposure grading with no visible seam.
Shader "Skybox/WeatherPanoramic"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (0.5, 0.5, 0.5, 1)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0

        [NoScaleOffset] _DayTex ("Day Panorama (latlong)", 2D) = "grey" {}
        [NoScaleOffset] _NightTex ("Night Panorama (latlong)", 2D) = "grey" {}
        [NoScaleOffset] _StormTex ("Storm Panorama (latlong)", 2D) = "grey" {}

        _DayNightBlend ("Day to Night", Range(0, 1)) = 0
        _StormBlend ("Storm", Range(0, 1)) = 0
        _StormTint ("Storm Tint", Color) = (0.34, 0.36, 0.4, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DayTex);     SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex);   SAMPLER(sampler_NightTex);
            TEXTURE2D(_StormTex);   SAMPLER(sampler_StormTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                float _Rotation;
                half _DayNightBlend;
                half _StormBlend;
                half4 _StormTint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 RotateAroundY(float3 vertex, float degrees)
            {
                float alpha = degrees * PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, vertex.xz), vertex.y).xzy;
            }

            // Latitude/longitude mapping, matching Unity's Skybox/Panoramic so the same textures
            // line up exactly when swapping this material in.
            float2 ToRadialCoords(float3 coords)
            {
                float3 normalizedCoords = normalize(coords);
                float latitude = acos(normalizedCoords.y);
                float longitude = atan2(normalizedCoords.z, normalizedCoords.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / PI, 1.0 / PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 rotated = RotateAroundY(input.positionOS.xyz, _Rotation);
                output.positionCS = TransformObjectToHClip(rotated);
                output.directionWS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 direction = RotateAroundY(input.directionWS, _Rotation);
                float2 uv = ToRadialCoords(direction);

                half3 day = SAMPLE_TEXTURE2D(_DayTex, sampler_DayTex, uv).rgb;
                half3 night = SAMPLE_TEXTURE2D(_NightTex, sampler_NightTex, uv).rgb;
                half3 storm = SAMPLE_TEXTURE2D(_StormTex, sampler_StormTex, uv).rgb;

                half3 sky = lerp(day, night, saturate(_DayNightBlend));

                // The storm slot is tinted rather than replacing outright, so a project with no
                // dedicated storm panorama still visibly overcasts instead of doing nothing.
                half3 stormSky = storm * _StormTint.rgb;
                sky = lerp(sky, stormSky, saturate(_StormBlend));

                // 2.0 is what unity_ColorSpaceDouble resolves to in linear space; URP's shader
                // library does not expose that macro, and the project renders in linear.
                sky *= _Tint.rgb * 2.0h * _Exposure;
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Skybox/Panoramic"
}
