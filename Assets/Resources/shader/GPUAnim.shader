Shader "Custom/GPUAnim"
{
    Properties
    {
        _MainTex1 ("Texture", 2D) = "white" {}
        _MainTex2 ("Texture", 2D) = "white" {}
        _MainTex3 ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
            #pragma exclude_renderers gles
            #pragma target 3.0            
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color :COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex1;
            sampler2D _MainTex2;
            sampler2D _MainTex3;
            float4 _MainTex1_ST;
            float4 _MainTex2_ST;
            float4 _MainTex3_ST;
            float _frameState;
            // float _CobjsOffset;

            void applyGPUAnim(in float4 vertex, in float boneIndices, out float3 worldPos)
            {
                float2 timestep = float2(_frameState, boneIndices);
                float4 r1 = tex2Dlod(_MainTex1, float4(timestep, 0, 0));
                float4 r2 = tex2Dlod(_MainTex2, float4(timestep, 0, 0));
                float4 r3 = tex2Dlod(_MainTex3, float4(timestep, 0, 0));
                float3x4 M = float3x4(r1, r2, r3);
                worldPos = mul(M, vertex);
            }

            v2f vert (appdata v)
            {
                v2f o;
                // float index = v.color.r;
                // float2 timestep = float2(_frameState, index);
                // float4 r1 = tex2Dlod(_MainTex1, float4(timestep, 0, 0));
                // float4 r2 = tex2Dlod(_MainTex2, float4(timestep, 0, 0));
                // float4 r3 = tex2Dlod(_MainTex3, float4(timestep, 0, 0));
                // float3x4 M = float3x4(r1, r2, r3);
                // float4 col3 = UNITY_MATRIX_M._m03_m13_m23_m33;
                // float4 rm1 = float4(1,0,0,col3.x);
                // float4 rm2 = float4(0,1,0,col3.y);
                // float4 rm3 = float4(0,0,1,col3.z);
                // float4 rm4 = float4(0,0,0,0);
                
                // float4x4 MM = float4x4(rm1, rm2, rm3,rm4);
                // float
                float3 worldPos;
                applyGPUAnim(v.vertex, v.color.r, worldPos);
                // float4 objVertex = mul(MM, float4(worldPos.xyz,1));
                //每个子物体有不同的UNITY_MATRIX_M矩阵，将原点位于0，0，0的子物体变化到正确的位置
                o.vertex = UnityObjectToClipPos(float4(worldPos.xyz, 1.0));
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                // fixed4 col = tex2D(_MainTex, i.uv);
                half4 col = half4(1,1,1,1);
                // apply fog
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
}
