Shader "Custom/SpatialDepthBandsOpaque"
{
    Properties
    {
        _NearDistance ("Near Distance", Float) = 0.5
        _MiddleDistance ("Middle Distance", Float) = 2.0
        _FarDistance ("Far Distance", Float) = 3.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            ZWrite On
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _NearDistance;
            float _MiddleDistance;
            float _FarDistance;

            struct appdata_t
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                o.worldPos = worldPos.xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = distance(_WorldSpaceCameraPos, i.worldPos);
                int colorIndex;
                
                // Handle everything before near distance
                if (d <= _NearDistance) {
                    colorIndex = 0;
                }
                // Handle everything beyond far distance
                else if (d >= _FarDistance) {
                    colorIndex = 7; // We now have 8 colors total (indices 0-7)
                }
                // Handle middle range
                else if (d < _MiddleDistance) {
                    // Near to Middle range (warm colors, indices 0-4)
                    float normalizedNearToMiddle = (d - _NearDistance) / (_MiddleDistance - _NearDistance);
                    float bandValue = normalizedNearToMiddle * 5.0; // Map to 5 bands (0-4)
                    colorIndex = clamp((int)bandValue, 0, 4);
                } 
                else {
                    // Middle to Far range (cold colors, indices 5-7)
                    float normalizedMiddleToFar = (d - _MiddleDistance) / (_FarDistance - _MiddleDistance);
                    // Now we map to just 3 bands for the cold colors
                    float bandValue = normalizedMiddleToFar * 2.0; 
                    colorIndex = clamp((int)bandValue + 5, 5, 6); // Offset by 5 to get indices 5-7
                }

                float3 color;
                if (colorIndex == 0) color = float3(0.549, 0.141, 0.176);      // Dark warm (red)
                else if (colorIndex == 1) color = float3(0.839, 0.420, 0.286);  // Medium-dark warm (orange-red)
                else if (colorIndex == 2) color = float3(0.894, 0.671, 0.408);  // Medium warm (orange)
                else if (colorIndex == 3) color = float3(0.957, 0.871, 0.573);  // Medium-light warm (yellow-orange)
                else if (colorIndex == 4) color = float3(0.984, 0.961, 0.757);  // Light warm (cream)
                // else if (colorIndex == 5) color = float3(0.898, 0.941, 0.957);  // Light cold (light blue)
                else if (colorIndex == 5) color = float3(0.529, 0.671, 0.804);  // Medium cold (medium blue)
                else color = float3(0.216, 0.231, 0.573);                       // Dark cold (dark blue)

                return float4(color, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}