Shader "Custom/GPUAnimVertexColor"
{
    Properties
    {
        _Tex("Texture", 2D) = "white" {}
        _Color1 ("Color1", Color) = (1,1,1,1)
        _Color2 ("Color2", Color) = (0,0,0,1)
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
                float2 objIndex : TEXCOORD1;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                float4 vertex : SV_POSITION;
                float4 color : TEXCOORD1;
            };

            sampler2D _MainTex1;
            sampler2D _MainTex2;
            sampler2D _MainTex3;
            sampler2D _Tex;
            float4 _MainTex1_ST;
            float4 _MainTex2_ST;
            float4 _MainTex3_ST;
            float _frameState;
            float4 _Color1;
            float4 _Color2;
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
                float3 worldPos;
                float4 pos = mul(UNITY_MATRIX_M, v.vertex);
                // float4 pos = mul(unity_ObjectToWorld, v.vertex);
                applyGPUAnim( v.vertex, v.objIndex.x, worldPos);
                //每个子物体有不同的UNITY_MATRIX_M矩阵，将原点位于0，0，0的子物体变化到正确的位置
                o.vertex = UnityObjectToClipPos(float4(worldPos.xyz, 1.0));
                o.uv = v.uv;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // sample the texture
                // fixed4 col = tex2D(_MainTex, i.uv);
                half4 col = lerp(_Color1, _Color2, i.color.r);
                half4 texCol = tex2D(_Tex, i.uv);
                return texCol*col;
            }
            ENDCG
        }
    }
}
