// Made with Amplify Shader Editor v1.9.9.4
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "IrradianceCache/Visualizer"
{
	Properties
	{
		[KeywordEnum( OCTREE,UNIFORM )] _LPVMode( "LPVMode", Float ) = 0
		[Toggle( _OCTREE_SAMPLING_MODE_WITH_BLENDING_ON )] _OCTREE_SAMPLING_MODE_WITH_BLENDING( "OCTREE_SAMPLING_MODE_WITH_BLENDING", Float ) = 0

	}

	SubShader
	{
		

		Tags { "RenderType"="Opaque" }

	LOD 0

		

		Blend Off
		AlphaToMask Off
		Cull Back
		ColorMask RGBA
		ZWrite On
		ZTest LEqual
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

		
		Pass
		{
			Name "Unlit"

			CGPROGRAM
				#define ASE_VERSION 19904

				#ifndef UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
					#define UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input)
				#endif
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_instancing
				#include "UnityCG.cginc"

				#pragma shader_feature_fragment _OCTREE_SAMPLING_MODE_WITH_BLENDING_ON
				#pragma shader_feature _LPVMODE_OCTREE _LPVMODE_UNIFORM
				#include "Assets/IrradianceCache/Shaders/LightProbeGlobalData.hlsl"
				#include "Assets/IrradianceCache/Shaders/IrradianceCacheMulti.hlsl"
				#include "Assets/IrradianceCache/Shaders/UniformGridLightProbeObstacleMulti.hlsl"


				struct appdata
				{
					float4 vertex : POSITION;
					float3 ase_normal : NORMAL;
					UNITY_VERTEX_INPUT_INSTANCE_ID
				};

				struct v2f
				{
					float4 pos : SV_POSITION;
					float4 ase_texcoord : TEXCOORD0;
					float4 ase_texcoord1 : TEXCOORD1;
					UNITY_VERTEX_INPUT_INSTANCE_ID
					UNITY_VERTEX_OUTPUT_STEREO
				};

				

				float4 IRRADIANCECACHE3( float3 worldPos, float3 normal )
				{
				    float3 shColor = SampleIrradianceCacheAuto(worldPos, normal);
				    return fixed4(shColor, 1);
				}
				
				float4 UNIFORMLIGHTPROBEVOLUME6( float3 worldPos, float3 normal )
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

					float3 ase_positionWS = mul( unity_ObjectToWorld, float4( ( v.vertex ).xyz, 1 ) ).xyz;
					o.ase_texcoord.xyz = ase_positionWS;
					float3 ase_normalWS = UnityObjectToWorldNormal( v.ase_normal );
					o.ase_texcoord1.xyz = ase_normalWS;
					
					
					//setting value to unused interpolator channels and avoid initialization warnings
					o.ase_texcoord.w = 0;
					o.ase_texcoord1.w = 0;

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

					float3 ase_positionWS = IN.ase_texcoord.xyz;
					float3 worldPos3 = ase_positionWS;
					float3 ase_normalWS = IN.ase_texcoord1.xyz;
					float3 normal3 = ase_normalWS;
					float4 localIRRADIANCECACHE3 = IRRADIANCECACHE3( worldPos3 , normal3 );
					float3 worldPos6 = ase_positionWS;
					float3 normal6 = ase_normalWS;
					float4 localUNIFORMLIGHTPROBEVOLUME6 = UNIFORMLIGHTPROBEVOLUME6( worldPos6 , normal6 );
					#if defined( _LPVMODE_OCTREE )
					float4 staticSwitch2 = localIRRADIANCECACHE3;
					#elif defined( _LPVMODE_UNIFORM )
					float4 staticSwitch2 = localUNIFORMLIGHTPROBEVOLUME6;
					#else
					float4 staticSwitch2 = localIRRADIANCECACHE3;
					#endif
					

					finalColor = staticSwitch2;

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
Node;AmplifyShaderEditor.WorldPosInputsNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;7;-1344,-208;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldNormalVector, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;8;-1344,-16;Inherit;False;False;1;0;FLOAT3;0,0,1;False;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;6;-992,32;Inherit;False;if (IsInsideVolume(worldPos))${$    float3 shColor = SampleGridLightProbeAuto(worldPos, normal)@$    return fixed4(shColor, 1)@$}$return 0@;4;Create;2;True;worldPos;FLOAT3;0,0,0;In;;Inherit;False;True;normal;FLOAT3;0,0,0;In;;Inherit;False;UNIFORM  LIGHT PROBE VOLUME;True;False;0;;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.CustomExpressionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;3;-992,-224;Inherit;False;if (IsInsideVolume(worldPos))${$    float3 shColor = SampleIrradianceCacheAuto(worldPos, normal)@$    return fixed4(shColor, 1)@$}$return 0@;4;Create;2;True;worldPos;FLOAT3;0,0,0;In;;Inherit;False;True;normal;FLOAT3;0,0,0;In;;Inherit;False;IRRADIANCE CACHE;True;False;0;;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;9;-576,0;Inherit;False;Property;_OCTREE_SAMPLING_MODE_WITH_BLENDING;OCTREE_SAMPLING_MODE_WITH_BLENDING;1;0;Create;True;0;0;0;True;0;False;0;0;1;True;;Toggle;2;Key0;Key1;Create;False;True;Fragment;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;2;-496,-224;Inherit;False;Property;_LPVMode;LPVMode;0;0;Create;True;0;0;0;True;0;False;0;0;1;True;;KeywordEnum;2;OCTREE;UNIFORM;Create;False;True;All;9;1;FLOAT4;0,0,0,0;False;0;FLOAT4;0,0,0,0;False;2;FLOAT4;0,0,0,0;False;3;FLOAT4;0,0,0,0;False;4;FLOAT4;0,0,0,0;False;5;FLOAT4;0,0,0,0;False;6;FLOAT4;0,0,0,0;False;7;FLOAT4;0,0,0,0;False;8;FLOAT4;0,0,0,0;False;1;FLOAT4;0
Node;AmplifyShaderEditor.FunctionNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;10;-1024,-512;Inherit;False;Reconstruct World Position From Depth;-1;;1;e7094bcbcc80eb140b2a3dbe6a861de8;0;0;1;FLOAT4;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode, AmplifyShaderEditor, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null;1;288,-224;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;5;IrradianceCache/Visualizer;0770190933193b94aaa3065e307002fa;True;Unlit;0;0;Unlit;2;True;True;0;1;False;;0;False;;0;1;False;;0;False;;True;0;False;;0;False;;False;False;False;False;False;False;False;False;False;True;0;False;;True;True;0;False;;False;True;True;True;True;True;0;False;;False;False;False;False;False;False;False;True;False;0;False;;255;False;;255;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;True;True;1;False;;True;0;False;;True;True;0;False;;0;False;;True;1;RenderType=Opaque=RenderType;True;3;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;4;Include;;False;;Native;False;0;0;;Include;;True;a911c0b1daaf98a4ca5479a4cb7464f5;Custom;False;0;0;;Include;;True;3f87309b3d6910041b66b8672268d0e2;Custom;False;0;0;;Include;;True;387ea08e22acd284396d38e2417350bf;Custom;False;0;0;;;0;0;Standard;1;Vertex Position;1;0;0;1;True;False;;False;0
WireConnection;6;0;7;0
WireConnection;6;1;8;0
WireConnection;3;0;7;0
WireConnection;3;1;8;0
WireConnection;2;1;3;0
WireConnection;2;0;6;0
WireConnection;1;0;2;0
ASEEND*/
//CHKSM=3BEFCFE6674312746701DBF8144879631C76AF72