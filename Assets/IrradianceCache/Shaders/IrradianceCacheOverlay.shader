// Made with Amplify Shader Editor v1.9.9.4
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "IrradianceCache/Overlay"
{
	Properties
	{
		[KeywordEnum( OCTREE,UNIFORM )] _LPVMode( "LPVMode", Float ) = 1
		_IndirectIntensity( "IndirectIntensity", Float ) = 1
		[ToggleUI] _IndirectOnly( "IndirectOnly", Float ) = 0

	}

	SubShader
	{
		

		Tags { "RenderType"="Opaque" }

	LOD 0

		

		Blend One Zero
		AlphaToMask Off
		Cull Front
		ColorMask RGBA
		ZWrite Off
		ZTest Always
		Offset 0 , 0
		

		CGINCLUDE
			#pragma target 3.5

			float4 ComputeClipSpacePosition( float2 screenPosNorm, float deviceDepth )
			{
				float4 positionCS = float4( screenPosNorm * 2.0 - 1.0, deviceDepth, 1.0 );
			#if UNITY_UV_STARTS_AT_TOP
				positionCS.y = -positionCS.y;
			#endif
				return positionCS;
			}
		ENDCG

		GrabPass{ }

		Pass
		{
			Name "Unlit"

			CGPROGRAM
				#define ASE_VERSION 19904
				#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
				#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex);
				#else
				#define ASE_DECLARE_SCREENSPACE_TEXTURE(tex) UNITY_DECLARE_SCREENSPACE_TEXTURE(tex)
				#endif

				#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
					#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
				#endif
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_instancing
				#include "UnityCG.cginc"

				#include "UnityShaderVariables.cginc"
				#define ASE_NEEDS_FRAG_SCREEN_POSITION_NORMALIZED
				#pragma shader_feature_fragment _LPVMODE_OCTREE _LPVMODE_UNIFORM
				#include "Assets/IrradianceCache/Shaders/LightProbeGlobalData.hlsl"
				#include "Assets/IrradianceCache/Shaders/IrradianceCacheMulti.hlsl"
				#include "Assets/IrradianceCache/Shaders/UniformGridLightProbeObstacleMulti.hlsl"


				struct appdata
				{
					float4 vertex : POSITION;
					
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float4 pos : SV_POSITION;
					
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};

				uniform float _IndirectOnly;
				ASE_DECLARE_SCREENSPACE_TEXTURE( _GrabTexture )
				uniform sampler2D _CameraGBufferTexture0;
				UNITY_DECLARE_DEPTH_TEXTURE( _CameraDepthTexture );
				uniform float4 _CameraDepthTexture_TexelSize;
				uniform sampler2D _CameraGBufferTexture2;
				uniform float _IndirectIntensity;


				float2 UnStereo( float2 UV )
				{
					#if UNITY_SINGLE_PASS_STEREO
					float4 scaleOffset = unity_StereoScaleOffset[ unity_StereoEyeIndex ];
					UV.xy = (UV.xy - scaleOffset.zw) / scaleOffset.xy;
					#endif
					return UV;
				}
				
				float3 InvertDepthDir72_g1( float3 In )
				{
					float3 result = In;
					#if !defined(ASE_SRP_VERSION) || ASE_SRP_VERSION <= 70301
					result *= float3(1,1,-1);
					#endif
					return result;
				}
				
				float4 IRRADIANCECACHE19( float3 worldPos, float3 normal )
				{
					float3 shColor = SampleIrradianceCacheAuto(worldPos, normal);
					return fixed4(shColor, 1);
				}
				
				float4 UNIFORMLIGHTPROBEVOLUME3( float3 worldPos, float3 normal )
				{
					float3 shColor = SampleGridLightProbeAuto(worldPos, normal);
					return fixed4(shColor, 1);
				}
				

				v2f vert ( appdata v )
				{
					v2f o;
					UNITY_SETUP_INSTANCE_ID( v );
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO( o );
					UNITY_TRANSFER_INSTANCE_ID( v, o );

					

					float3 vertexValue = float3( 0, 0, 0 );
					#if ASE_ABSOLUTE_VERTEX_POS
						vertexValue = v.vertex.xyz;
					#endif
					vertexValue = vertexValue;
					#if ASE_ABSOLUTE_VERTEX_POS
						v.vertex.xyz = vertexValue;
					#else
						v.vertex.xyz += vertexValue;
					#endif

					o.pos = UnityObjectToClipPos( v.vertex );
					return o;
				}

				half4 frag( v2f IN  ) : SV_Target
				{
					UNITY_SETUP_INSTANCE_ID( IN );
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX( IN );
					half4 finalColor;

					float4 ScreenPosNorm = float4( IN.pos.xy * ( _ScreenParams.zw - 1.0 ), IN.pos.zw );
					float4 ClipPos = ComputeClipSpacePosition( ScreenPosNorm.xy, IN.pos.z ) * IN.pos.w;
					float4 ScreenPos = ComputeScreenPos( ClipPos );

					float4 screenColor30 = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_GrabTexture,ScreenPosNorm.xy);
					float4 Albedo20 = tex2D( _CameraGBufferTexture0, ScreenPosNorm.xy );
					float2 UV22_g3 = ScreenPosNorm.xy;
					float2 localUnStereo22_g3 = UnStereo( UV22_g3 );
					float2 break64_g1 = localUnStereo22_g3;
					float depth01_69_g1 = SAMPLE_DEPTH_TEXTURE( _CameraDepthTexture, ScreenPosNorm.xy );
					#ifdef UNITY_REVERSED_Z
					float staticSwitch38_g1 = ( 1.0 - depth01_69_g1 );
					#else
					float staticSwitch38_g1 = depth01_69_g1;
					#endif
					float3 appendResult39_g1 = (float3(break64_g1.x , break64_g1.y , staticSwitch38_g1));
					float4 appendResult42_g1 = (float4((appendResult39_g1*2.0 + -1.0) , 1.0));
					float4 temp_output_43_0_g1 = mul( unity_CameraInvProjection, appendResult42_g1 );
					float3 temp_output_46_0_g1 = ( (temp_output_43_0_g1).xyz / (temp_output_43_0_g1).w );
					float3 In72_g1 = temp_output_46_0_g1;
					float3 localInvertDepthDir72_g1 = InvertDepthDir72_g1( In72_g1 );
					float4 appendResult49_g1 = (float4(localInvertDepthDir72_g1 , 1.0));
					float4 temp_output_5_0 = mul( unity_CameraToWorld, appendResult49_g1 );
					float3 worldPos19 = temp_output_5_0.xyz;
					float3 WorldNormal17 = (tex2D( _CameraGBufferTexture2, ScreenPosNorm.xy ).rgb*2.0 + -1.0);
					float3 normal19 = WorldNormal17;
					float4 localIRRADIANCECACHE19 = IRRADIANCECACHE19( worldPos19 , normal19 );
					float3 worldPos3 = temp_output_5_0.xyz;
					float3 normal3 = WorldNormal17;
					float4 localUNIFORMLIGHTPROBEVOLUME3 = UNIFORMLIGHTPROBEVOLUME3( worldPos3 , normal3 );
					#if defined( _LPVMODE_OCTREE )
					float4 staticSwitch4 = localIRRADIANCECACHE19;
					#elif defined( _LPVMODE_UNIFORM )
					float4 staticSwitch4 = localUNIFORMLIGHTPROBEVOLUME3;
					#else
					float4 staticSwitch4 = localUNIFORMLIGHTPROBEVOLUME3;
					#endif
					float4 IndirectIrradiance22 = ( staticSwitch4 * _IndirectIntensity );
					

					finalColor = (( _IndirectOnly )?( IndirectIrradiance22 ):( ( screenColor30 + ( Albedo20 * IndirectIrradiance22 ) ) ));

					return finalColor;
				}
			ENDCG
		}
	}
	CustomEditor "AmplifyShaderEditor.MaterialInspector"
	
	Fallback Off
}
/*ASEBEGIN
Version=19904
Node;AmplifyShaderEditor.TexturePropertyNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;14;-2240,-944;Inherit;True;Global;_CameraGBufferTexture2;_CameraGBufferTexture2;1;0;Create;True;0;0;0;True;0;False;None;None;False;white;Auto;Texture2D;False;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.ScreenPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;13;-2240,-688;Float;False;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;15;-1840,-944;Inherit;True;Property;_TextureSample1;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.ScaleAndOffsetNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;16;-1376,-928;Inherit;False;3;0;FLOAT3;0,0,0;False;1;FLOAT;2;False;2;FLOAT;-1;False;1;FLOAT3;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;17;-1056,-928;Inherit;False;WorldNormal;-1;True;1;0;FLOAT3;0,0,0;False;1;FLOAT3;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;18;-2208,-128;Inherit;False;17;WorldNormal;1;0;OBJECT;;False;1;FLOAT3;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;5;-2256,-352;Inherit;False;Reconstruct World Position From Depth;-1;;1;e7094bcbcc80eb140b2a3dbe6a861de8;0;0;1;FLOAT4;0
Node;AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;19;-1776,-352;Inherit;False;if (IsInsideVolume(worldPos))${$    float3 shColor = SampleIrradianceCacheAuto(worldPos, normal)@$    return fixed4(shColor, 1)@$}$return 0@;4;Create;2;True;worldPos;FLOAT3;0,0,0;In;;Inherit;False;True;normal;FLOAT3;0,0,0;In;;Inherit;False;IRRADIANCE CACHE;True;False;0;;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;3;-1792,-160;Inherit;False;if (IsInsideVolume(worldPos))${$    float3 shColor = SampleGridLightProbeAuto(worldPos, normal)@$    return fixed4(shColor, 1)@$}$return 0@;4;Create;2;True;worldPos;FLOAT3;0,0,0;In;;Inherit;False;True;normal;FLOAT3;0,0,0;In;;Inherit;False;UNIFORM  LIGHT PROBE VOLUME;True;False;0;;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ScreenPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;11;-2240,-1168;Float;False;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.TexturePropertyNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;10;-2240,-1424;Inherit;True;Global;_CameraGBufferTexture0;_CameraGBufferTexture0;2;0;Create;True;0;0;0;True;0;False;None;None;False;white;Auto;Texture2D;False;-1;0;2;SAMPLER2D;0;SAMPLERSTATE;1
Node;AmplifyShaderEditor.RangedFloatNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;25;-1296,-176;Inherit;False;Property;_IndirectIntensity;IndirectIntensity;2;0;Create;True;0;0;0;False;0;False;1;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;4;-1376,-352;Inherit;False;Property;_LPVMode;LPVMode;0;0;Create;True;0;0;0;True;0;False;0;1;1;True;;KeywordEnum;2;OCTREE;UNIFORM;Create;False;True;Fragment;9;1;FLOAT4;0,0,0,0;False;0;FLOAT4;0,0,0,0;False;2;FLOAT4;0,0,0,0;False;3;FLOAT4;0,0,0,0;False;4;FLOAT4;0,0,0,0;False;5;FLOAT4;0,0,0,0;False;6;FLOAT4;0,0,0,0;False;7;FLOAT4;0,0,0,0;False;8;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SamplerNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;9;-1840,-1424;Inherit;True;Property;_TextureSample0;Texture Sample 0;1;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;False;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;24;-1024,-352;Inherit;False;2;2;0;FLOAT4;0,0,0,0;False;1;FLOAT;0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;20;-1504,-1424;Inherit;False;Albedo;-1;True;1;0;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;22;-400,-352;Inherit;False;IndirectIrradiance;-1;True;1;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;21;-48,-1408;Inherit;False;20;Albedo;1;0;OBJECT;;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;23;-48,-1312;Inherit;False;22;IndirectIrradiance;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ScreenPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;33;-448,-1728;Float;False;0;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;12;240,-1408;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.ScreenColorNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;30;-80,-1728;Inherit;False;Global;_GrabScreen0;Grab Screen 0;3;0;Create;True;0;0;0;False;0;False;Object;-1;False;False;False;False;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleAddOpNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;31;416,-1616;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.GetLocalVarNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;34;-48,-1504;Inherit;False;22;IndirectIrradiance;1;0;OBJECT;;False;1;FLOAT4;0
Node;AmplifyShaderEditor.ToggleSwitchNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;32;592,-1616;Inherit;False;Property;_IndirectOnly;IndirectOnly;3;0;Create;True;0;0;0;False;0;False;0;False;Create;2;0;FLOAT4;0,0,0,0;False;1;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;6;928,-1424;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;5;IrradianceCache/Overlay;0770190933193b94aaa3065e307002fa;True;Unlit;0;0;Unlit;2;True;True;1;1;False;;0;False;;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;True;True;1;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;True;True;2;False;;True;7;False;;True;True;0;False;;0;False;;True;1;RenderType=Opaque=RenderType;True;3;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;4;Include;;False;;Native;False;0;0;;Include;;True;a911c0b1daaf98a4ca5479a4cb7464f5;Custom;False;0;0;;Include;;True;3f87309b3d6910041b66b8672268d0e2;Custom;False;0;0;;Include;;True;387ea08e22acd284396d38e2417350bf;Custom;False;0;0;;;0;0;Standard;1;Vertex Position;1;0;0;1;True;False;;False;0
WireConnection;15;0;14;0
WireConnection;15;1;13;0
WireConnection;16;0;15;5
WireConnection;17;0;16;0
WireConnection;19;0;5;0
WireConnection;19;1;18;0
WireConnection;3;0;5;0
WireConnection;3;1;18;0
WireConnection;4;1;19;0
WireConnection;4;0;3;0
WireConnection;9;0;10;0
WireConnection;9;1;11;0
WireConnection;24;0;4;0
WireConnection;24;1;25;0
WireConnection;20;0;9;0
WireConnection;22;0;24;0
WireConnection;12;0;21;0
WireConnection;12;1;23;0
WireConnection;30;0;33;0
WireConnection;31;0;30;0
WireConnection;31;1;12;0
WireConnection;32;0;31;0
WireConnection;32;1;34;0
WireConnection;6;0;32;0
ASEEND*/
//CHKSM=8C40549486437AC0B88CFD80C1574AD3AD15EC26