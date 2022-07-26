Shader "XHH/TestClusterColor"
{
	Properties { }
	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

		Pass
		{

			Cull Back

			ZWrite On
			HLSLPROGRAM

			#pragma vertex vert
			#pragma fragment frag


			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ClusterLight.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"


			CBUFFER_START(UnityPerMaterial)



			CBUFFER_END


			SAMPLER(_CameraTerrainColor);

			struct Attributes
			{
				float4 positionOS: POSITION;
				float2 uv: TEXCOORD0;
				
				float3 normalOS: NORMAL;
			};


			struct Varyings
			{
				float4 positionCS: SV_POSITION;
				float3 positionWS: TEXCOORD1;
				float2 uv: TEXCOORD0;
				float3 normalWS: NORMAL;
				float4 vpos: TEXCOORD2;
			};



			Varyings vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);
				output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
				output.uv = input.uv;

				output.vpos = mul(GetWorldToViewMatrix(), output.positionWS);
				return output;
			}


			float4 frag(Varyings input): SV_Target
			{
				half3 additionalLight = ShadeAdditionalPoint(input.positionCS, input.positionWS, input.normalWS);
				return float4(additionalLight, 1);
			}

			ENDHLSL

		}
	}
	FallBack "Diffuse"
}