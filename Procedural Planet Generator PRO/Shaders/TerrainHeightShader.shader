Shader "Custom/TerrainHeightShader"
{
    Properties
    {
        _PlanetCenter ("Planet Center", Vector) = (0, 0, 0, 0)
        _PlanetRadius ("Planet Radius", Float) = 1000.0
        _NormalBlend ("Terrain Normal Blend", Range(0, 1)) = 0.32
        _WrapLighting ("Wrap Lighting", Range(0, 1)) = 0.3
        _RimColor ("Rim Color", Color) = (0.36, 0.48, 0.62, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.18
        _RimPower ("Rim Power", Range(1, 8)) = 3.5
        _SaturationBoost ("Saturation Boost", Range(0.5, 2.0)) = 1.08
        _ShadowTint ("Shadow Tint", Color) = (0.26, 0.29, 0.34, 1)
        _DetailNormalMap ("Surface Normal Map", 2D) = "bump" {}
        _DetailNormalTiling ("Detail Normal Tiling", Range(1, 128)) = 48
        _DetailNormalStrength ("Detail Normal Strength", Range(0, 2)) = 0.92
        _DetailMicroNormalTiling ("Micro Normal Tiling", Range(1, 256)) = 164
        _DetailMicroNormalStrength ("Micro Normal Strength", Range(0, 2)) = 0.28
        _DetailCavityStrength ("Detail Cavity Strength", Range(0, 2)) = 0.82

        [Header(Night Settings)]
        _NightAmbient    ("Night Ambient",    Range(0, 0.15)) = 0.04
        _HorizonSoftness ("Horizon Softness", Range(0.5, 10.0)) = 4.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 250

        Cull Back
        Offset -1, -1

        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows addshadow vertex:vert
        #pragma target 3.0
        #include "UnityCG.cginc"

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            float4 color : COLOR;
        };

        float4 _PlanetCenter;
        float _PlanetRadius;
        float _NormalBlend;
        float _WrapLighting;
        float4 _RimColor;
        float _RimStrength;
        float _RimPower;
        float _SaturationBoost;
        float4 _ShadowTint;
        float _NightAmbient;
        float _HorizonSoftness;
        sampler2D _DetailNormalMap;
        float _DetailNormalTiling;
        float _DetailNormalStrength;
        float _DetailMicroNormalTiling;
        float _DetailMicroNormalStrength;
        float _DetailCavityStrength;

        float AxisSign(float v)
        {
            return v >= 0.0 ? 1.0 : -1.0;
        }

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.color = v.color;
        }

        float3 SampleAxisNormal(float2 uv, float signAxis)
        {
            float3 tangentNormal = UnpackNormal(tex2D(_DetailNormalMap, uv));
            tangentNormal.z = max(0.0001, tangentNormal.z);
            tangentNormal.x *= signAxis;
            return normalize(tangentNormal);
        }

        float3 SampleTriplanarNormal(float3 worldPos, float3 baseNormal, float tiling)
        {
            float textureScale = max(0.0001, tiling) * 0.001;
            float3 blend = pow(abs(baseNormal), 4.0);
            blend /= max(1e-4, blend.x + blend.y + blend.z);

            float signX = AxisSign(baseNormal.x);
            float signY = AxisSign(baseNormal.y);
            float signZ = AxisSign(baseNormal.z);

            float3 nx = SampleAxisNormal(worldPos.zy * textureScale, signX);
            float3 ny = SampleAxisNormal(worldPos.xz * textureScale, signY);
            float3 nz = SampleAxisNormal(worldPos.xy * textureScale, signZ);

            float3 worldNX = normalize(float3(nx.z * signX, nx.y, nx.x));
            float3 worldNY = normalize(float3(ny.x, ny.z * signY, ny.y));
            float3 worldNZ = normalize(float3(nz.x, nz.y, nz.z * signZ));

            return normalize(worldNX * blend.x + worldNY * blend.y + worldNZ * blend.z);
        }

        float ComputeDetailCavity(float3 sampledNormal, float3 baseNormal)
        {
            return saturate(1.0 - dot(normalize(sampledNormal), normalize(baseNormal)));
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 radialNormal = normalize(IN.worldPos - _PlanetCenter.xyz);
            float3 meshNormal = normalize(IN.worldNormal);
            float3 smoothNormal = normalize(lerp(radialNormal, meshNormal, _NormalBlend));

            float3 detailNormal = SampleTriplanarNormal(IN.worldPos, smoothNormal, _DetailNormalTiling);
            float3 microNormal = SampleTriplanarNormal(IN.worldPos + smoothNormal * 7.13, smoothNormal, _DetailMicroNormalTiling);
            float slopeMask = saturate(1.0 - dot(smoothNormal, radialNormal));
            float3 lightingNormal = normalize(
                smoothNormal
                + (detailNormal - smoothNormal) * _DetailNormalStrength
                + (microNormal - smoothNormal) * _DetailMicroNormalStrength);
            float detailCavity = ComputeDetailCavity(detailNormal, smoothNormal);
            float microCavity = ComputeDetailCavity(microNormal, smoothNormal);
            float cavityMask = saturate(
                (detailCavity * 0.95 + microCavity * 0.45)
                * (0.55 + slopeMask * 0.85)
                * _DetailCavityStrength);

            float3 lightDir = normalize(UnityWorldSpaceLightDir(IN.worldPos));
            float3 viewDir = normalize(IN.viewDir);

            float planetDay = smoothstep(-0.18, 0.30, dot(radialNormal, lightDir) * _HorizonSoftness);
            float wrapLight = saturate((dot(lightingNormal, lightDir) + _WrapLighting) / (1.0 + _WrapLighting));
            float lightMask = max(wrapLight * planetDay, _NightAmbient);

            fixed3 baseColor = saturate(IN.color.rgb);
            float luminance = dot(baseColor, float3(0.299, 0.587, 0.114));
            baseColor = lerp(luminance.xxx, baseColor, _SaturationBoost);
            baseColor = lerp(baseColor, baseColor * 0.72, cavityMask * 0.55);

            float detailShadow = saturate(0.65 + dot(lightingNormal, smoothNormal) * 0.35);
            fixed3 litColor = lerp(baseColor * _ShadowTint.rgb, baseColor, lightMask) * detailShadow;
            litColor = lerp(litColor, litColor * 0.74, cavityMask * (0.48 + (1.0 - lightMask) * 0.22));
            float rim = pow(1.0 - saturate(dot(viewDir, radialNormal)), _RimPower) * _RimStrength * planetDay;

            o.Albedo = litColor;
            o.Emission = baseColor * (_NightAmbient * 0.55) + _RimColor.rgb * rim;
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
