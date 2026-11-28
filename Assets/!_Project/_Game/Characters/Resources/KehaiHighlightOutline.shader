// The highlight outline (28 November): a line of even width round what you look at, traced on
// the screen from the thing's own shape, so nothing sticks out on thin or awkward shapes.
// Pass 0 marks the highlighted renderers' visible pixels in the mask's red channel; pass 2 marks
// their whole shape, hidden parts too, in green; pass 1 draws the line just outside the visible
// pixels but never inside the whole shape, so stock standing in front of a shelf doesn't get a
// ring of its own, and a thing behind a wall gets no line. HighlightOutlines.cs drives them.
Shader "Hidden/Kehai/HighlightOutline"
{
    Properties
    {
        _OutlineColor ("Colour", Color) = (1, 0.85, 0.1, 1)
        _OutlinePixels ("Width in pixels", Float) = 3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Mask"
            ZTest LEqual
            ZWrite Off
            Cull Off
            Offset -1, -1
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return half4(1, 0, 0, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _OutlineColor;
            float _OutlinePixels;
            float4 _MaskTexel;   // 1/width, 1/height

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.texcoord;
                // Inside the thing's whole shape: no fill, the thing shows as it is.
                if (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).g > 0.5) discard;
                // Outside: how much of the thing lies within the line's width, in 16 directions
                // at two distances. Bilinear taps soften the line's own edge.
                half best = 0;
                [unroll] for (int k = 0; k < 16; k++)
                {
                    float a = k * 0.39269908;
                    float2 d = float2(cos(a), sin(a)) * _OutlinePixels * _MaskTexel.xy;
                    best = max(best, SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d).r);
                    best = max(best, SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d * 0.5).r);
                }
                half alpha = smoothstep(0.05, 0.5, best) * _OutlineColor.a;
                if (alpha <= 0.001) discard;
                return half4(_OutlineColor.rgb, alpha);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Whole shape"
            ZTest Always
            ZWrite Off
            Cull Off
            ColorMask G

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target { return half4(0, 1, 0, 1); }
            ENDHLSL
        }
    }
    Fallback Off
}
