#ifndef MLS_BLENDING_STANDARD
#define MLS_BLENDING_STANDARD

SamplerState samplermls_bilinear_clamp_sampler;
SamplerState samplermls_trilinear_clamp_sampler;

// Lightmaps Processing
float _MLS_Lightmaps_Blend_Factor;

#ifdef MLS_TEXTURE2D_ARRAYS_ON
    UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_MLS_Lightmap_Color_Array);
    UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_MLS_Lightmap_Directional_Array);
    UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_MLS_Lightmap_ShadowMask_Array);

    UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_MLS_BakeryRNM_0_Array);
    UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_MLS_BakeryRNM_1_Array);
    UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(_MLS_BakeryRNM_2_Array);
#endif

#ifdef MLS_TEXTURECUBE_ARRAYS_ON
    UNITY_DECLARE_TEXCUBEARRAY(_MLS_Cubemap_Array);
#endif

UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_Lightmap_Color_Blend_From);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_Lightmap_Color_Blend_To);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_Lightmap_Dir_Blend_From);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_Lightmap_Dir_Blend_To);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_Lightmap_ShadowMask_Blend_From);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_Lightmap_ShadowMask_Blend_To);

UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_BakeryRNM0_From);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_BakeryRNM0_To);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_BakeryRNM1_From);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_BakeryRNM1_To);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_BakeryRNM2_From);
UNITY_DECLARE_TEX2D_NOSAMPLER(_MLS_BakeryRNM2_To);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolume0_From);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolume0_To);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolume1_From);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolume1_To);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolume2_From);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolume2_To);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolumeMask_From);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolumeMask_To);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolumeCompressed_From);
UNITY_DECLARE_TEX3D_NOSAMPLER(_MLS_BakeryVolumeCompressed_To);

struct BakeryVolumeData
{
    float4 Volume0Blended;
    float4 Volume1Blended;
    float4 Volume2Blended;
    float4 VolumeMaskBlended;
};

// Reflections Prcessing
float _MLS_Reflections_Blend_Factor;
int _MLS_ReflectionsFlag;

UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_Reflection_Blend_From_0);
UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_Reflection_Blend_To_0);
UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_Reflection_Blend_From_1);
UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_Reflection_Blend_To_1);
UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_SkyReflection_Blend_From);
UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_SkyReflection_Blend_To);

// Sky Cubemap Processing
float _MLS_Sky_Cubemap_Blend_Factor;
float _MLS_Sky_Blend_From_Rotation;
float _MLS_Sky_Blend_To_Rotation;
float _MLS_Sky_Blend_From_Exposure;
float _MLS_Sky_Blend_To_Exposure;
half4 _MLS_Sky_Blend_From_Tint;
half4 _MLS_Sky_Blend_To_Tint;

UNITY_DECLARE_TEXCUBE(_MLS_Sky_Cubemap_Blend_From);
UNITY_DECLARE_TEXCUBE_NOSAMPLER(_MLS_Sky_Cubemap_Blend_To);

// General
int _MLS_BAKERY_VOLUMES_SYNCH;
int _MLS_ENABLE_SKY_CUBEMAPS_BLENDING;
float4 _MLS_CURRENT_LIGHTMAP_PAIR;
float4 _MLS_OBJECT_BLENDING_DATA;

float4 BlendTwoTextures(int lightmapType, float2 uv)
{
    half4 textureFrom;
    half4 textureTo;
    
    #ifdef MLS_TEXTURE2D_ARRAYS_ON
        int indexFrom = (_MLS_CURRENT_LIGHTMAP_PAIR.x * _MLS_CURRENT_LIGHTMAP_PAIR.z) + _MLS_OBJECT_BLENDING_DATA.x;
        int indexTo = (_MLS_CURRENT_LIGHTMAP_PAIR.y * _MLS_CURRENT_LIGHTMAP_PAIR.z) + _MLS_OBJECT_BLENDING_DATA.x;
    #endif
    
    switch (lightmapType)
    {
        case 0:
            #ifdef MLS_TEXTURE2D_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_Color_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_Color_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_Color_Blend_From, mls_bilinear_clamp_sampler, uv.xy);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_Color_Blend_To, mls_bilinear_clamp_sampler, uv.xy); 
            #endif
            break;
        case 1:
            #ifdef MLS_TEXTURE2D_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_Directional_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_Directional_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_Dir_Blend_From, mls_bilinear_clamp_sampler, uv.xy);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_Dir_Blend_To, mls_bilinear_clamp_sampler, uv.xy); 
            #endif
            break;
        case 2:
            #ifdef MLS_TEXTURE2D_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_ShadowMask_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_ShadowMask_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_ShadowMask_Blend_From, mls_bilinear_clamp_sampler, uv.xy);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_ShadowMask_Blend_To, mls_bilinear_clamp_sampler, uv.xy);
            #endif
            break;
    }

    return lerp(textureFrom, textureTo, _MLS_Lightmaps_Blend_Factor);
}

float4 BlendBakeryVolume(int volumeType, float3 uv)
{
    float4 textureFrom;
    float4 textureTo;

    #ifdef MLS_TEXTURE2D_ARRAYS_ON
        int indexFrom = (_MLS_CURRENT_LIGHTMAP_PAIR.x * _MLS_CURRENT_LIGHTMAP_PAIR.z) + _MLS_OBJECT_BLENDING_DATA.x;
        int indexTo = (_MLS_CURRENT_LIGHTMAP_PAIR.y * _MLS_CURRENT_LIGHTMAP_PAIR.z) + _MLS_OBJECT_BLENDING_DATA.x;
    #endif

    switch (volumeType)
    {
        case 0:
            textureFrom = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolume0_From, mls_trilinear_clamp_sampler, uv);
            textureTo = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolume0_To, mls_trilinear_clamp_sampler, uv);
            break;
        case 1:
            textureFrom = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolume1_From, mls_trilinear_clamp_sampler, uv);
            textureTo = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolume1_To, mls_trilinear_clamp_sampler, uv);
            break;
        case 2:
            textureFrom = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolume2_From, mls_trilinear_clamp_sampler, uv);
            textureTo = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolume2_To, mls_trilinear_clamp_sampler, uv);
            break;
        case 3:
            textureFrom = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolumeMask_From, mls_trilinear_clamp_sampler, uv);
            textureTo = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolumeMask_To, mls_trilinear_clamp_sampler, uv);
            break;
        case 4:
            textureFrom = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolumeCompressed_From, mls_trilinear_clamp_sampler, uv);
            textureTo = UNITY_SAMPLE_TEX3D_SAMPLER(_MLS_BakeryVolumeCompressed_To, mls_trilinear_clamp_sampler, uv);
        break;
    }

    if (_MLS_BAKERY_VOLUMES_SYNCH > 0)
    {
        return lerp(textureFrom, textureTo, _MLS_Reflections_Blend_Factor);
    }
    else
    {
        return lerp(textureFrom, textureTo, _MLS_Lightmaps_Blend_Factor);
    }
}

float4 BlendBakeryRNM(int lightmapType, float2 uv)
{
    half4 textureFrom;
    half4 textureTo;

    #ifdef MLS_TEXTURE2D_ARRAYS_ON
    int indexFrom = (_MLS_CURRENT_LIGHTMAP_PAIR.x * _MLS_CURRENT_LIGHTMAP_PAIR.z) + _MLS_OBJECT_BLENDING_DATA.x;
    int indexTo = (_MLS_CURRENT_LIGHTMAP_PAIR.y * _MLS_CURRENT_LIGHTMAP_PAIR.z) + _MLS_OBJECT_BLENDING_DATA.x;
    #endif

    switch (lightmapType)
    {
    case 0:
        #ifdef MLS_TEXTURE2D_ARRAYS_ON
            textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_BakeryRNM_0_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
            textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_BakeryRNM_0_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
        #else
            textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_BakeryRNM0_From, mls_trilinear_clamp_sampler, uv.xy);
            textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_BakeryRNM0_To, mls_trilinear_clamp_sampler, uv.xy);
        #endif
        break;
    case 1:
        #ifdef MLS_TEXTURE2D_ARRAYS_ON
            textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_BakeryRNM_1_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
            textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_BakeryRNM_1_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
        #else
            textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_BakeryRNM1_From, mls_trilinear_clamp_sampler, uv.xy);
            textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_BakeryRNM1_To, mls_trilinear_clamp_sampler, uv.xy);
        #endif
        break;
    case 2:
        #ifdef MLS_TEXTURE2D_ARRAYS_ON
            textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_BakeryRNM_2_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
            textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_BakeryRNM_2_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
        #else
            textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_BakeryRNM2_From, mls_trilinear_clamp_sampler, uv.xy);
            textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_BakeryRNM2_To, mls_trilinear_clamp_sampler, uv.xy);
        #endif
        break;
    case 3:
        #ifdef MLS_TEXTURE2D_ARRAYS_ON
            textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_Color_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexFrom));
            textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER(_MLS_Lightmap_Color_Array, mls_bilinear_clamp_sampler, float3(uv.xy, indexTo));
        #else
            textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_Color_Blend_From, mls_bilinear_clamp_sampler, uv.xy);
            textureTo = UNITY_SAMPLE_TEX2D_SAMPLER(_MLS_Lightmap_Color_Blend_To, mls_bilinear_clamp_sampler, uv.xy); 
        #endif
        break;
    }

    return lerp(textureFrom, textureTo, _MLS_Lightmaps_Blend_Factor);
}

float4 BlendTwoCubeTextures(int probeIndex, float3 reflection, half mip)
{
    float4 textureFrom;
    float4 textureTo;
    float blendFactor;

    #ifdef MLS_TEXTURECUBE_ARRAYS_ON
        int indexFrom = (_MLS_CURRENT_LIGHTMAP_PAIR.x * _MLS_OBJECT_BLENDING_DATA.w) + _MLS_OBJECT_BLENDING_DATA.y;
        int indexTo = (_MLS_CURRENT_LIGHTMAP_PAIR.y * _MLS_OBJECT_BLENDING_DATA.w) + _MLS_OBJECT_BLENDING_DATA.y;
    #endif

    switch (probeIndex)
    {
        case 0:
            #ifdef MLS_TEXTURECUBE_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexFrom)), mip);
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexTo)), mip);
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_Reflection_Blend_From_0, mls_bilinear_clamp_sampler, reflection, mip);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_Reflection_Blend_To_0, mls_bilinear_clamp_sampler, reflection, mip);
            #endif

            blendFactor = _MLS_Reflections_Blend_Factor;
            break;
        case 1:
            #ifdef MLS_TEXTURECUBE_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexFrom)), mip);
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexTo)), mip);
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_Reflection_Blend_From_1, mls_bilinear_clamp_sampler, reflection, mip);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_Reflection_Blend_To_1, mls_bilinear_clamp_sampler, reflection, mip);
            #endif
        
            blendFactor = _MLS_Reflections_Blend_Factor;
            break;
        case 2:
            #ifdef MLS_TEXTURECUBE_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexFrom)), mip);
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexTo)), mip);
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_SkyReflection_Blend_From, mls_bilinear_clamp_sampler, reflection, mip);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_SkyReflection_Blend_To, mls_bilinear_clamp_sampler, reflection, mip);
            #endif
        
            blendFactor = _MLS_Reflections_Blend_Factor;
            break;
        case 3:
            #ifdef MLS_TEXTURECUBE_ARRAYS_ON
                textureFrom = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexFrom)), mip);
                textureTo = UNITY_SAMPLE_TEX2DARRAY_SAMPLER_LOD(_MLS_Cubemap_Array, mls_bilinear_clamp_sampler, float4(reflection.xyz, round(indexTo)), mip);
            #else
                textureFrom = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_Sky_Cubemap_Blend_From, mls_bilinear_clamp_sampler, reflection, mip);
                textureTo = UNITY_SAMPLE_TEX2D_SAMPLER_LOD(_MLS_Sky_Cubemap_Blend_To, mls_bilinear_clamp_sampler, reflection, mip);
            #endif

            blendFactor = _MLS_Sky_Cubemap_Blend_Factor;
            break;
    }

    return lerp(textureFrom, textureTo, blendFactor);
}

float4 BlendTwoSkyCubeTextures(float3 reflection_from, float3 reflection_to, half mip)
{
    float4 textureFrom;
    float4 textureTo;
    float blendFactor;

    textureFrom = UNITY_SAMPLE_TEXCUBE_SAMPLER_LOD(_MLS_Sky_Cubemap_Blend_From, _MLS_Sky_Cubemap_Blend_From, reflection_from, mip);
    textureTo = UNITY_SAMPLE_TEXCUBE_SAMPLER_LOD(_MLS_Sky_Cubemap_Blend_To, _MLS_Sky_Cubemap_Blend_From, reflection_to, mip);

    return lerp(textureFrom, textureTo, _MLS_Sky_Cubemap_Blend_Factor);
}

half BlendSkyExposure()
{
    return lerp(_MLS_Sky_Blend_From_Exposure, _MLS_Sky_Blend_To_Exposure, _MLS_Sky_Cubemap_Blend_Factor);
}

half4 BlendSkyTint()
{
    return lerp(_MLS_Sky_Blend_From_Tint, _MLS_Sky_Blend_To_Tint, _MLS_Sky_Cubemap_Blend_Factor);
}
#endif