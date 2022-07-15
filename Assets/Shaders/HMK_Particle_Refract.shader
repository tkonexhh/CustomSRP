Shader "HMK/Particle/Refract"
{
	Properties
	{
		[NoScaleOffset]_Noise ("Noise", 2D) = "white" { }
		_Noisetiling ("Tiling", range(0, 100)) = 1
		_Distortion ("Distortion", range(0, 1)) = 0
		_SelectChannel ("Channle", Vector) = (1, 0, 0, 0)
		_SpeedX ("SpeedX", range(-10, 10)) = 0
		_SpeedY ("SpeedY", range(-10, 10)) = 0
	}
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
			half _Distortion;
			half _Noisetiling;
			half4 _SelectChannel;
			half _SpeedX;
			half _SpeedY;


			CBUFFER_END

			TEXTURE2D(_Noise);SAMPLER(sampler_Noise);
			SAMPLER(_CameraOpaqueTexture);
			SAMPLER(_CameraTransparentTexture);

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
				half var_NoiseTex = dot(_SelectChannel, SAMPLE_TEXTURE2D(_Noise, sampler_Noise, input.uv * _Noisetiling + float2(_Time.y * _SpeedX, _Time.y * _SpeedY)));
				float circle = saturate(1 - pow(distance(input.uv, 0.5) * 2, 2));
				var_NoiseTex *= circle;
				float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
				screenUV = float2((screenUV.x + var_NoiseTex * _Distortion), screenUV.y);


				half4 colrefrac = tex2D(_CameraTransparentTexture, screenUV);


				return float4(colrefrac.rgb, 1);
			}

			ENDHLSL

		}
	}
	FallBack "Diffuse"
}