Shader "Custom/AtmosphereController"
{
    Properties
    {
        _Color ("Atmosphere Color", Color) = (0.4,0.7,1,0.5)
        _Intensity ("Intensity", Range(0,5)) = 1.0
        _PlanetPos ("Planet Position", Vector) = (0,0,0,0)
        _PlanetRadius ("Planet Radius", Float) = 5000.0
        _AtmoHeight ("Atmosphere Height", Float) = 300.0

        // старые параметры оставлены для совместимости
        _VisibilityThreshold ("Visibility Threshold", Range(0,1)) = 0.05
        _VisibilitySmooth ("Visibility Smooth (blend range)", Range(0.001,0.5)) = 0.08
        _DirectLightBoost ("Direct Light Boost", Range(0,5)) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Cull Front
        ZWrite Off
        ZTest LEqual
        Offset 1, 1
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float4 _PlanetPos;
            float _PlanetRadius;
            float _AtmoHeight;

            float _VisibilityThreshold;
            float _VisibilitySmooth;
            float _DirectLightBoost;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 planetPos = _PlanetPos.xyz;
                float3 world = i.worldPos;

                float dist = distance(world, planetPos);
                float height = dist - _PlanetRadius; // оставлено для справки

                // ИСПРАВЛЕНИЕ: теперь плотность не зависит от радиуса меша
                // (работает и когда меш атмосферы больше планеты)
                float baseDensity = _Intensity;

                // Horizon factor (лимб-эффект)
                float3 viewDir = normalize(_WorldSpaceCameraPos - world);
                float3 normal = normalize(world - planetPos);
                float horizonFactor = 1.0 - saturate(dot(viewDir, normal) * 0.5 + 0.5);
                horizonFactor = pow(horizonFactor, 1.5);

                float alpha = baseDensity * (0.3 + 0.7 * horizonFactor);
                float finalAlpha = saturate(alpha * _Color.a);

                // Освещение (день/ночь)
                float3 lightVec = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = dot(normal, lightVec);
                float lightFactor = saturate(NdotL * _DirectLightBoost * 0.6 + 0.5);

                fixed3 col = _Color.rgb * lightFactor;
                return fixed4(col, finalAlpha);
            }
            ENDCG
        }
    }
    FallBack Off
}