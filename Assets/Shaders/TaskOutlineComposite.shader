// TaskOutlineFeature, step 2: full-screen pass over the camera image. A pixel gets outline colour
// when it is NOT on the mask but some mask pixel lies within _OutlineWidth pixels of it, so every
// edge of every highlighted shape gets the same ring thickness. Blinks over time.
Shader "Hidden/DuckRoulette/TaskOutlineComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "TaskOutlineComposite"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float _OutlineWidth;
            float _BlinkSpeed;
            float _MinAlpha;
            float4 _MaskTexelSize;

            SAMPLER(s_point_clamp_sampler);

            #define RING_DIRECTIONS 16

            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, s_point_clamp_sampler, uv).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                // On the object itself: no fill, the object stays as it is.
                if (SampleMask(uv) > 0.5)
                {
                    discard;
                }

                // Coverage within the ring: checked at the full width and at two inner radii, so
                // thin parts of the shape still produce a solid band.
                float coverage = 0;
                [unroll]
                for (int i = 0; i < RING_DIRECTIONS; i++)
                {
                    float angle = i * (6.2831853 / RING_DIRECTIONS);
                    float2 direction = float2(cos(angle), sin(angle)) * _MaskTexelSize.xy;
                    coverage = max(coverage, SampleMask(uv + direction * _OutlineWidth));
                    coverage = max(coverage, SampleMask(uv + direction * _OutlineWidth * 0.66));
                    coverage = max(coverage, SampleMask(uv + direction * _OutlineWidth * 0.33));
                }

                if (coverage < 0.01)
                {
                    discard;
                }

                float blink = lerp(_MinAlpha, 1.0, 0.5 + 0.5 * sin(_Time.y * _BlinkSpeed));
                return half4(_OutlineColor.rgb, _OutlineColor.a * blink * coverage);
            }
            ENDHLSL
        }
    }
}
