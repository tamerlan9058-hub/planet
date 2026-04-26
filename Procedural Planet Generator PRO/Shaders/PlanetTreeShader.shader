Shader "Custom/PlanetTreeShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _PlanetCenter ("Planet Center", Vector) = (0,0,0,0)
        _WrapLighting ("Wrap Lighting", Range(0,1)) = 0.2
        _RimColor ("Rim Color", Color) = (0.36, 0.48, 0.62, 1)
        _RimStrength ("Rim Strength", Range(0,1)) = 0.14
        _RimPower ("Rim Power", Range(1,8)) = 2.8
        _NightAmbient ("Night Ambient", Range(0,0.3)) = 0.02
        _HorizonSoftness ("Horizon Softness", Range(0.1, 5.0)) = 3.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        #include "UnityCG.cginc"

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            fixed4 color : COLOR;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        float4 _PlanetCenter;
        float _WrapLighting;
        float4 _RimColor;
        float _RimStrength;
        float _RimPower;
        float _NightAmbient;
        float _HorizonSoftness;

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color * IN.color;
            float3 planetNormal = normalize(IN.worldPos - _PlanetCenter.xyz);
            float3 leafNormal = normalize(IN.worldNormal);
            float3 lightDir = normalize(UnityWorldSpaceLightDir(IN.worldPos));
            float3 viewDir = normalize(IN.viewDir);

            float planetDay = smoothstep(-0.18, 0.30, dot(planetNormal, lightDir) * _HorizonSoftness);
            float wrapLight = saturate((dot(leafNormal, lightDir) + _WrapLighting) / (1.0 + _WrapLighting));
            float lightMask = max(wrapLight * planetDay, _NightAmbient);
            float rim = pow(1.0 - saturate(dot(viewDir, leafNormal)), _RimPower) * _RimStrength * planetDay;

            o.Albedo = c.rgb * lightMask;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Emission = c.rgb * (_NightAmbient * 0.35) + _RimColor.rgb * rim;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
