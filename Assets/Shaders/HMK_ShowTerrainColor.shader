Shader "XHH/ShowTerrainColor"
{
	Properties { }
	SubShader
	{
		Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+100" }
		// Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			Tags { "LightMode" = "Refract" }

			Cull Back

			ZWrite off
			HLSLPROGRAM

			#pragma vertex vert
			#pragma fragment frag


			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
				float2 uv: TEXCOORD0;

				float3 normalWS: NORMAL;
			};



			Varyings vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);


				output.uv = input.uv;


				return output;
			}


			float4 frag(Varyings input): SV_Target
			{

				float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);


				half4 colrefrac = tex2D(_CameraTerrainColor, screenUV);


				return float4(colrefrac.rgb, 1);
			}

			ENDHLSL

		}
	}
	FallBack "Diffuse"
}