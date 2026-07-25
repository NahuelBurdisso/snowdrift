Shader "Snowdrift/GradientSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.35, 0.55, 0.85, 1)
        _HorizonColor ("Horizon", Color) = (0.95, 0.85, 0.80, 1)
        _Exponent ("Blend", Range(0.5, 4)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _TopColor, _HorizonColor; float _Exponent;
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.dir = a.pos.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                float t = pow(saturate(normalize(i.dir).y), _Exponent);
                return lerp(_HorizonColor, _TopColor, t);
            }
            ENDHLSL
        }
    }
}
