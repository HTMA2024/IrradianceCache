#ifndef IRRADIANCE_CACHE_FUNCTIONS_INCLUDED
#define IRRADIANCE_CACHE_FUNCTIONS_INCLUDED

// ============================================================================
// IrradianceCacheFunctions.hlsl
// Shader Graph 兼容的自定义函数
// 用于 URP/HDRP Custom Function 节点
// 支持单 Volume 和多 Volume 模式
// ============================================================================

#include "IrradianceCache.hlsl"
#include "IrradianceCacheMulti.hlsl"

// ----------------------------------------------------------------------------
// Shader Graph Custom Functions
// ----------------------------------------------------------------------------

/// <summary>
/// 采样八叉树 Light Probe (自动选择单/多 Volume 模式)
/// 用于 Shader Graph Custom Function 节点
/// </summary>
/// <param name="WorldPosition">世界坐标</param>
/// <param name="WorldNormal">世界法线</param>
/// <param name="Color">输出颜色</param>
/// <param name="IsInside">是否在 Volume 内</param>
void SampleIrradianceCache_float(
    float3 WorldPosition,
    float3 WorldNormal,
    out float3 Color,
    out float IsInside)
{
    if (IsInsideAnyVolume(WorldPosition))
    {
        Color = SampleIrradianceCacheAuto(WorldPosition, normalize(WorldNormal));
        IsInside = 1.0;
    }
    else
    {
        Color = float3(0, 0, 0);
        IsInside = 0.0;
    }
}

// Half 精度版本
void SampleIrradianceCache_half(
    half3 WorldPosition,
    half3 WorldNormal,
    out half3 Color,
    out half IsInside)
{
    float3 colorFloat;
    float isInsideFloat;
    SampleIrradianceCache_float(WorldPosition, WorldNormal, colorFloat, isInsideFloat);
    Color = (half3)colorFloat;
    IsInside = (half)isInsideFloat;
}

/// <summary>
/// 多 Volume 采样 (带额外输出信息)
/// </summary>
/// <param name="WorldPosition">世界坐标</param>
/// <param name="WorldNormal">世界法线</param>
/// <param name="Color">输出颜色</param>
/// <param name="IsInside">是否在 Volume 内</param>
/// <param name="VolumeCount">包含该点的 Volume 数量</param>
void SampleMultiVolumeLightProbe_float(
    float3 WorldPosition,
    float3 WorldNormal,
    out float3 Color,
    out float IsInside,
    out int VolumeCount)
{
    if (_UseMultiVolumeMode && _ActiveVolumeCount > 0)
    {
        float totalWeight;
        Color = SampleMultiVolumeLightProbe(WorldPosition, normalize(WorldNormal), totalWeight, VolumeCount);
        IsInside = totalWeight > 0.0 ? 1.0 : 0.0;
    }
    else
    {
        // 回退到单 Volume 模式
        if (IsInsideOctreeVolume(WorldPosition))
        {
            Color = SampleIrradianceCache(WorldPosition, normalize(WorldNormal));
            IsInside = 1.0;
            VolumeCount = 1;
        }
        else
        {
            Color = float3(0, 0, 0);
            IsInside = 0.0;
            VolumeCount = 0;
        }
    }
}

void SampleMultiVolumeLightProbe_half(
    half3 WorldPosition,
    half3 WorldNormal,
    out half3 Color,
    out half IsInside,
    out int VolumeCount)
{
    float3 colorFloat;
    float isInsideFloat;
    SampleMultiVolumeLightProbe_float(WorldPosition, WorldNormal, colorFloat, isInsideFloat, VolumeCount);
    Color = (half3)colorFloat;
    IsInside = (half)isInsideFloat;
}

/// <summary>
/// 获取节点深度颜色（调试用）
/// </summary>
void GetOctreeNodeDepthColor_float(
    float3 WorldPosition,
    out float3 DepthColor,
    out float NodeDepth)
{
    if (IsInsideOctreeVolume(WorldPosition))
    {
        int nodeIndex = QueryOctreeNode(WorldPosition);
        if (nodeIndex >= 0)
        {
            OctreeNode node = GetOctreeNode(nodeIndex);
            float t = (float)node.depth / (float)_OctreeMaxDepth;
            DepthColor = lerp(float3(0, 1, 0), float3(1, 0, 0), t);
            NodeDepth = (float)node.depth;
            return;
        }
    }
    DepthColor = float3(0, 0, 0);
    NodeDepth = -1;
}

void GetOctreeNodeDepthColor_half(
    half3 WorldPosition,
    out half3 DepthColor,
    out half NodeDepth)
{
    float3 depthColorFloat;
    float nodeDepthFloat;
    GetOctreeNodeDepthColor_float(WorldPosition, depthColorFloat, nodeDepthFloat);
    DepthColor = (half3)depthColorFloat;
    NodeDepth = (half)nodeDepthFloat;
}

/// <summary>
/// 检查位置是否在 Volume 内 (支持多 Volume)
/// </summary>
void IsInsideOctreeVolume_float(
    float3 WorldPosition,
    out float IsInside)
{
    IsInside = IsInsideAnyVolume(WorldPosition) ? 1.0 : 0.0;
}

void IsInsideOctreeVolume_half(
    half3 WorldPosition,
    out half IsInside)
{
    IsInside = IsInsideAnyVolume(WorldPosition) ? 1.0h : 0.0h;
}

/// <summary>
/// 获取 Volume 边界信息 (单 Volume 模式)
/// </summary>
void GetOctreeVolumeBounds_float(
    out float3 Center,
    out float3 HalfExtents)
{
    Center = _OctreeRootCenter;
    HalfExtents = _OctreeRootHalfExtents;
}

void GetOctreeVolumeBounds_half(
    out half3 Center,
    out half3 HalfExtents)
{
    Center = (half3)_OctreeRootCenter;
    HalfExtents = (half3)_OctreeRootHalfExtents;
}

/// <summary>
/// 采样并混合 Unity 原生 Light Probe (支持多 Volume)
/// </summary>
void SampleIrradianceCacheWithFallback_float(
    float3 WorldPosition,
    float3 WorldNormal,
    float3 UnityLightProbeColor,
    float BlendFactor,
    out float3 FinalColor)
{
    if (IsInsideAnyVolume(WorldPosition))
    {
        float3 octreeColor = SampleIrradianceCacheAuto(WorldPosition, normalize(WorldNormal));
        FinalColor = lerp(UnityLightProbeColor, octreeColor, BlendFactor);
    }
    else
    {
        FinalColor = UnityLightProbeColor;
    }
}

void SampleIrradianceCacheWithFallback_half(
    half3 WorldPosition,
    half3 WorldNormal,
    half3 UnityLightProbeColor,
    half BlendFactor,
    out half3 FinalColor)
{
    float3 finalColorFloat;
    SampleIrradianceCacheWithFallback_float(
        WorldPosition, WorldNormal, UnityLightProbeColor, BlendFactor, finalColorFloat);
    FinalColor = (half3)finalColorFloat;
}

/// <summary>
/// 获取多 Volume 调试信息
/// </summary>
void GetMultiVolumeDebugInfo_float(
    float3 WorldPosition,
    out float3 DebugColor,
    out int VolumeCount,
    out float TotalWeight)
{
    if (_UseMultiVolumeMode && _ActiveVolumeCount > 0)
    {
        VolumeCount = GetContainingVolumeCount(WorldPosition);
        TotalWeight = GetTotalBlendWeight(WorldPosition);
        DebugColor = GetMultiVolumeDebugColor(WorldPosition);
    }
    else
    {
        VolumeCount = IsInsideOctreeVolume(WorldPosition) ? 1 : 0;
        TotalWeight = VolumeCount > 0 ? 1.0 : 0.0;
        DebugColor = VolumeCount > 0 ? float3(0, 1, 0) : float3(0, 0, 0);
    }
}

void GetMultiVolumeDebugInfo_half(
    half3 WorldPosition,
    out half3 DebugColor,
    out int VolumeCount,
    out half TotalWeight)
{
    float3 debugColorFloat;
    float totalWeightFloat;
    GetMultiVolumeDebugInfo_float(WorldPosition, debugColorFloat, VolumeCount, totalWeightFloat);
    DebugColor = (half3)debugColorFloat;
    TotalWeight = (half)totalWeightFloat;
}

#endif // IRRADIANCE_CACHE_FUNCTIONS_INCLUDED
