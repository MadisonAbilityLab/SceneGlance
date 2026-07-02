Shader "Custom/SpatialDepthBands"
{
    Properties
    {
        _NearDistance ("Near Distance", Float) = 0.5
        _FarDistance ("Far Distance", Float) = 3.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert

        struct Input
        {
            float3 worldPos;
        };

        float _NearDistance;
        float _FarDistance;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
            o.worldPos = worldPos.xyz;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float d = distance(_WorldSpaceCameraPos, IN.worldPos);
            float normalized = saturate((d - _NearDistance) / (_FarDistance - _NearDistance));
            float bandValue = normalized * 10.0;
            int bandIndex = clamp(int(bandValue + 0.5), 0, 9);

            float3 color;
            if (bandIndex == 0) color = float3(0.549, 0.141, 0.176);
            else if (bandIndex == 1) color = float3(0.839, 0.420, 0.286);
            else if (bandIndex == 2) color = float3(0.894, 0.671, 0.408);
            else if (bandIndex == 3) color = float3(0.957, 0.871, 0.573);
            else if (bandIndex == 4) color = float3(0.984, 0.961, 0.757);
            else if (bandIndex == 5) color = float3(0.898, 0.941, 0.957);
            else if (bandIndex == 6) color = float3(0.718, 0.839, 0.918);
            else if (bandIndex == 7) color = float3(0.529, 0.671, 0.804);
            else if (bandIndex == 8) color = float3(0.341, 0.459, 0.694);
            else color = float3(0.216, 0.231, 0.573);

            o.Albedo = color;
            o.Alpha = 1.0;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
