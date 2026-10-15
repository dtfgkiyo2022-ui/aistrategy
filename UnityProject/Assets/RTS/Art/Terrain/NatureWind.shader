Shader "Rts/NatureWind"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _WindAmount ("Wind", Float) = 0.04
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 _BaseColor; float _WindAmount;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionHCS : SV_POSITION; float4 color : COLOR; float3 normalWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output; float3 p = input.positionOS.xyz;
                float sway = sin(_Time.y * 1.4 + p.x * 0.8 + p.z * 0.5) * _WindAmount * saturate(p.y);
                p.x += sway; p.z += cos(_Time.y * 1.1 + p.z) * _WindAmount * 0.35 * saturate(p.y);
                output.positionHCS = TransformObjectToHClip(p); output.color = input.color * _BaseColor;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS); return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                Light sun = GetMainLight();
                half daylight = saturate(dot(normalize(input.normalWS), sun.direction));
                return half4(input.color.rgb * lerp(half3(0.48, 0.52, 0.62), sun.color.rgb, daylight), input.color.a);
            }
            ENDHLSL
        }
    }
}
