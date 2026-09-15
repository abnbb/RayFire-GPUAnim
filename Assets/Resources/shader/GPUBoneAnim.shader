Shader "Custom/GPUBoneAnim"
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
            HLSLPROGRAM
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
            #pragma exclude_renderers gles
            #pragma target 4.0            
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 weights : TEXCOORD1;
                float4 index :TEXCOORD2;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_FOG_COORDS(2)
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

            void sampleAnimM(in float index, out float3x4 M)
            {
                float2 timestep = float2(_frameState, index);
                float4 r1 = tex2Dlod(_MainTex1, float4(timestep, 0, 0));
                float4 r2 = tex2Dlod(_MainTex2, float4(timestep, 0, 0));
                float4 r3 = tex2Dlod(_MainTex3, float4(timestep, 0, 0));
                M = float3x4(r1, r2, r3);
            }

            void sampleAnimNormalM(in float index, out float3x3 M)
            {
                float2 timestep = float2(_frameState, index);
                float3 r1 = tex2Dlod(_MainTex1, float4(timestep, 0, 0)).xyz;
                float3 r2 = tex2Dlod(_MainTex2, float4(timestep, 0, 0)).xyz;
                float3 r3 = tex2Dlod(_MainTex3, float4(timestep, 0, 0)).xyz;
                M = float3x3(r1, r2, r3);
            }
            void InverseTranspose(in float3x3 M, out float3x3 invTransM)
            {
                // HLSL 中 M[0]、M[1]、M[2] 分别表示矩阵的行。
                float3 c0 = cross(M[1], M[2]);
                float3 c1 = cross(M[2], M[0]);
                float3 c2 = cross(M[0], M[1]);

                float det = dot(M[0], c0);

                // 奇异矩阵无法求逆；回退到单位矩阵，避免除零。
                if (abs(det) < 1e-8)
                {
                    invTransM = float3x3(
                        1, 0, 0,
                        0, 1, 0,
                        0, 0, 1
                    );
                    return;
                }

                // 余子式矩阵 / 行列式，已经是逆转置，无需再 transpose。
                invTransM = float3x3(c0, c1, c2) / det;
            }

            void applyGPUAnim(in float4 vertex, in float4 boneIndices, in float4 boneWeights, out float3 worldPos)
            {
                float3x4 M1,M2,M3,M4;
                sampleAnimM(boneIndices.x, M1);
                sampleAnimM(boneIndices.y, M2); 
                sampleAnimM(boneIndices.z, M3);
                sampleAnimM(boneIndices.w, M4);
                worldPos = mul(M1, vertex)*boneWeights.x + mul(M2, vertex)*boneWeights.y + mul(M3, vertex)*boneWeights.z + mul(M4, vertex)*boneWeights.w;
            }

            void applyGPUAnimNormal(in float3 n, in float4 boneIndices, in float4 boneWeights, out float3 animNormal)
            {
                float3x3 M1,M2,M3,M4,invTransM;
                sampleAnimNormalM(boneIndices.x, M1);
                sampleAnimNormalM(boneIndices.y, M2);
                sampleAnimNormalM(boneIndices.z, M3);
                sampleAnimNormalM(boneIndices.w, M4);

                float3x3 BoneM = M1*boneWeights.x + M2*boneWeights.y + M3*boneWeights.z + M4*boneWeights.w;
                InverseTranspose(BoneM, invTransM);

                animNormal = mul(invTransM, n);
            }

            v2f vert (appdata v)
            {
                v2f o;
                float3 worldPos;
                float3 animNormal;
                applyGPUAnim(v.vertex, v.index, v.weights, worldPos);
                applyGPUAnimNormal(v.normal, v.index, v.weights, animNormal);
                // float4 objVertex = mul(MM, float4(worldPos.xyz,1));
                //每个子物体有不同的UNITY_MATRIX_M矩阵，将原点位于0，0，0的子物体变化到正确的位置
                o.vertex = UnityObjectToClipPos(float4(worldPos.xyz, 1.0));
                o.worldNormal = animNormal;
                o.uv = v.uv;
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half3 normal = normalize(i.worldNormal);
                half3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                half ndotl = saturate(dot(normal, lightDir));
                half3 ambient = UNITY_LIGHTMODEL_AMBIENT.xyz;
                half3 diffuse = _LightColor0.rgb * ndotl;
                half4 col = half4(ambient + diffuse, 1.0);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDHLSL
        }
    }
}
