Shader "Custom/PlanetProxyShader"
{
    Properties
    {
        _PlanetCenter ("Planet Center", Vector) = (0, 0, 0, 0)
        _PlanetRadius ("Planet Radius", Float) = 1000.0
        _SeaColor ("Sea Color", Color) = (0.12, 0.32, 0.60, 1)
        _LowlandColor ("Lowland Color", Color) = (0.18, 0.52, 0.22, 1)
        _HighlandColor ("Highland Color", Color) = (0.42, 0.38, 0.34, 1)
        _SnowColor ("Snow Color", Color) = (0.92, 0.94, 1.0, 1)
        _AtmosphereColor ("Atmosphere Color", Color) = (0.50, 0.70, 1.0, 1)
        _NoiseScale ("Noise Scale", Float) = 5.0
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.3
        _NoiseContrast ("Noise Contrast", Range(0, 3)) = 1.2
        _AtmosphereStrength ("Atmosphere Strength", Range(0, 1)) = 0.15
        _Opacity ("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200
        Cull Back
        Offset 1, 1

        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows vertex:vert
        #pragma target 3.0

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
            float4 color : COLOR;
        };

        float4 _PlanetCenter;
        float _PlanetRadius;
        fixed4 _SeaColor;
        fixed4 _LowlandColor;
        fixed4 _HighlandColor;
        fixed4 _SnowColor;
        fixed4 _AtmosphereColor;
        float _NoiseScale;
        float _NoiseStrength;
        float _NoiseContrast;
        float _AtmosphereStrength;
        float _Opacity;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.color = v.color;
        }

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        float Noise3(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);

            float n000 = Hash31(i + float3(0, 0, 0));
            float n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0));
            float n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1));
            float n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1));
            float n111 = Hash31(i + float3(1, 1, 1));

            float nx00 = lerp(n000, n100, f.x);
            float nx10 = lerp(n010, n110, f.x);
            float nx01 = lerp(n001, n101, f.x);
            float nx11 = lerp(n011, n111, f.x);
            float nxy0 = lerp(nx00, nx10, f.y);
            float nxy1 = lerp(nx01, nx11, f.y);
            return lerp(nxy0, nxy1, f.z);
        }

        float FBM(float3 p)
        {
            float sum = 0.0;
            float amp = 0.5;

            [unroll(4)]
            for (int i = 0; i < 4; i++)
            {
                sum += Noise3(p) * amp;
                p = p * 2.03 + 17.17;
                amp *= 0.5;
            }

            return sum;
        }

        float DitherMask(float3 worldPos)
        {
            float3 cell = floor(worldPos * 0.03);
            return Hash31(cell + _PlanetCenter.xyz * 0.00031);
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 radial = normalize(IN.worldPos - _PlanetCenter.xyz);
            float3 noisePos = radial * max(0.001, _NoiseScale) + _PlanetCenter.xyz * 0.00005;

            float macro = FBM(noisePos);
            float detail = FBM(noisePos * 2.35 + 13.7);
            float landMask = saturate((macro - 0.42) * (1.4 + _NoiseContrast));
            landMask = smoothstep(0.0, 1.0, landMask + (detail - 0.5) * _NoiseStrength);

            fixed3 landColor = lerp(_LowlandColor.rgb, _HighlandColor.rgb, saturate(detail * 1.15));
            fixed3 proceduralAlbedo = lerp(_SeaColor.rgb, landColor, landMask);

            float snowMask = smoothstep(0.72, 0.94, abs(radial.y) + detail * 0.14);
            proceduralAlbedo = lerp(proceduralAlbedo, _SnowColor.rgb, snowMask * landMask);

            float rim = pow(1.0 - saturate(dot(normalize(IN.viewDir), radial)), 4.0) * _AtmosphereStrength;
            clip(_Opacity - DitherMask(IN.worldPos));

            o.Albedo = proceduralAlbedo;
            o.Emission = _AtmosphereColor.rgb * rim;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
