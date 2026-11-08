Shader "Rts/GrassCard"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // Near grass (NearGrassField, 11-09): crossed cards with a painted tuft of blades, cut out by alpha, swaying
    // in the wind, tinted per tuft by the vertex colour. Like Rts/NatureWind's near grass, the cards shrink into the
    // ground towards the edge of the built field and as the camera rises (_FadeRange / _FadeCentre).
    Properties
    {
        _BaseMap ("Blades", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Float) = 0.45
        _WindAmount ("Wind", Float) = 0.06
        _FadeRange ("Fade Range", Vector) = (0,0,0,1)
        _FadeCentre ("Fade Centre", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        float _Cutoff; float _WindAmount; float4 _FadeRange; float4 _FadeCentre;
        float _RtsLook; float4 _RtsSky; float4 _RtsGround;

        // uv.y is 0 at the root and 1 at the tip; only the upper part moves.
        float3 GrassMove(float3 p, float2 uv)
        {
            float bend = uv.y * uv.y;
            p.x += sin(_Time.y * 1.6 + p.x * 0.6 + p.z * 0.4) * _WindAmount * bend;
            p.z += cos(_Time.y * 1.2 + p.z * 0.7) * _WindAmount * 0.5 * bend;
            if (_FadeRange.z > 0.5)
            {
                float3 w = TransformObjectToWorld(p);
                float d = distance(w.xz, _FadeCentre.xz);
                float k = saturate((_FadeRange.y - d) / max(0.001, _FadeRange.y - _FadeRange.x)) * _FadeRange.w;
                p.y *= k; // the roots sit on the ground (y close to 0), so the card sinks into it
            }
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(GrassMove(input.positionOS.xyz, input.uv));
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 blades = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(blades.a - _Cutoff);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = lerp(1.0h, sun.shadowAttenuation, 0.85h);
                half directAo = 1.0h, indirectAo = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(input.positionHCS));
                    directAo = ao.directAmbientOcclusion;
                    indirectAo = ao.indirectAmbientOcclusion;
                #endif
                // Blades are lit as if they faced up (they are thin and many); darker at the root, where they crowd.
                half daylight = saturate(sun.direction.y) * shadow;
                half3 sky = _RtsLook > 0.5 ? _RtsSky.rgb : half3(0.48, 0.52, 0.62);
                half3 lit = lerp(sky * indirectAo, sun.color.rgb * directAo, daylight);
                half root = lerp(0.62h, 1.0h, saturate(input.uv.y * 1.5h));
                return half4(blades.rgb * input.color.rgb * lit * root, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            struct DepthAttributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct DepthVaryings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };
            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionHCS = TransformObjectToHClip(GrassMove(input.positionOS.xyz, input.uv));
                output.uv = input.uv;
                return output;
            }
            half DepthFrag(DepthVaryings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return input.positionHCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            struct NormalsAttributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct NormalsVaryings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };
            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                NormalsVaryings output;
                output.positionHCS = TransformObjectToHClip(GrassMove(input.positionOS.xyz, input.uv));
                output.uv = input.uv;
                return output;
            }
            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
                return half4(0.0, 1.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }
}
