Shader "Rts/UnitSilhouette"
{
    // Lives under Resources so the player build keeps it (it is only reached through Shader.Find).
    // 11-10 test play: villagers at the forest edge vanished under the tree tops. This extra material is drawn on top
    // of a villager or soldier only where something stands in front of it (ZTest Greater), as a see-through shape
    // in the faction colour. The shape is pulled towards the camera first, so the unit's own arm or shield in front
    // of its body (well under a metre) does not count as cover; a tree top or a roof (metres away) does. Display only.
    Properties
    {
        _Color ("Colour", Color) = (0.25, 0.55, 1, 0.55)
        _Pull ("Pull towards the camera (m)", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Pull;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; float rim : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                float3 toEye = normalize(GetCameraPositionWS() - world);
                float3 normal = normalize(TransformObjectToWorldNormal(input.normalOS));
                output.rim = 1.0 - saturate(dot(normal, toEye));
                output.positionHCS = TransformWorldToHClip(world + toEye * _Pull);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // A brighter outline than middle, so a crowd under the trees still reads as separate people.
                half edge = input.rim * input.rim;
                half3 colour = lerp(_Color.rgb, 1.0.xxx, edge * 0.45);
                return half4(colour, saturate(_Color.a * (0.65 + edge * 0.7)));
            }
            ENDHLSL
        }
    }
}
