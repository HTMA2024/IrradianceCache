#ifndef UNIFORM_GRID_LIGHT_PROBE_OBSTACLE_FUNCTIONS_INCLUDED
#define UNIFORM_GRID_LIGHT_PROBE_OBSTACLE_FUNCTIONS_INCLUDED

// ============================================================================
// UniformGridLightProbeObstacleFunctions.hlsl
// Shader Graph 兼容的自定义函数 + OBB 障碍物遮挡
// 用于 URP/HDRP Custom Function 节点
// 支持单 Volume 和多 Volume 模式
// 函数签名与 UniformGridLightProbeFunctions.hlsl 保持一致，可直接替换
// ============================================================================

#include "UniformGridLightProbeObstacleMulti.hlsl"

// ----------------------------------------------------------------------------
// Shader Graph Custom Functions
// ----------------------------------------------------------------------------

/// <summary>
/// 采样均匀网格 Light Probe (自动选择单/多 Volume 模式) + 障碍物遮挡
/// 用于 Shader Graph Custom Function 节点
/// </summary>
void SampleGridLightProbe_float(
    float3 WorldPosition,
    float3 WorldNormal,
    out float3 Color,
    out float IsInside)
{
    if (IsInsideAnyGridVolume(WorldPosition))
    {
        Color = SampleGridLightProbeAuto(WorldPosition, normalize(WorldNormal));
        IsInside = 1.0;
    }
    else
    {
        Color = float3(0, 0, 0);
        IsInside = 0.0;
    }
}

// Half 精度版本
void SampleGridLightProbe_half(
    half3 WorldPosition,
    half3 WorldNormal,
    out half3 Color,
    out half IsInside)
{
    float3 colorFloat;
    float isInsideFloat;
    SampleGridLightProbe_float(WorldPosition, WorldNormal, colorFloat, isInsideFloat);
    Color = (half3)colorFloat;
    IsInside = (half)isInsideFloat;
}

/// <summary>
/// 多 Volume 采样 (带额外输出信息) + 障碍物遮挡
/// </summary>
void SampleMultiGridLightProbe_float(
    float3 WorldPosition,
    float3 WorldNormal,
    out float3 Color,
    out float IsInside,
    out int VolumeCount)
{
    if (_UseMultiVolumeMode && _ActiveVolumeCount > 0)
    {
        float totalWeight;
        Color = SampleMultiGridLightProbe(WorldPosition, normalize(WorldNormal), totalWeight, VolumeCount);
        IsInside = totalWeight > 0.0 ? 1.0 : 0.0;
    }
    else
    {
        if (IsInsideGridVolume(WorldPosition))
        {
            Color = SampleGridLightProbe(WorldPosition, normalize(WorldNormal));
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

void SampleMultiGridLightProbe_half(
    half3 WorldPosition,
    half3 WorldNormal,
    out half3 Color,
    out half IsInside,
    out int VolumeCount)
{
    float3 colorFloat;
    float isInsideFloat;
    SampleMultiGridLightProbe_float(WorldPosition, WorldNormal, colorFloat, isInsideFloat, VolumeCount);
    Color = (half3)colorFloat;
    IsInside = (half)isInsideFloat;
}

/// <summary>
/// 检查位置是否在 Volume 内 (支持多 Volume)
/// </summary>
void IsInsideGridVolume_float(
    float3 WorldPosition,
    out float IsInside)
{
    IsInside = IsInsideAnyGridVolume(WorldPosition) ? 1.0 : 0.0;
}

void IsInsideGridVolume_half(
    half3 WorldPosition,
    out half IsInside)
{
    IsInside = IsInsideAnyGridVolume(WorldPosition) ? 1.0h : 0.0h;
}

/// <summary>
/// 获取 Volume 边界信息 (单 Volume 模式)
/// </summary>
void GetGridVolumeBounds_float(
    out float3 Center,
    out float3 HalfExtents)
{
    Center = _OctreeRootCenter;
    HalfExtents = _OctreeRootHalfExtents;
}

void GetGridVolumeBounds_half(
    out half3 Center,
    out half3 HalfExtents)
{
    Center = (half3)_OctreeRootCenter;
    HalfExtents = (half3)_OctreeRootHalfExtents;
}

/// <summary>
/// 采样并混合 Unity 原生 Light Probe (支持多 Volume) + 障碍物遮挡
/// </summary>
void SampleGridLightProbeWithFallback_float(
    float3 WorldPosition,
    float3 WorldNormal,
    float3 UnityLightProbeColor,
    float BlendFactor,
    out float3 FinalColor)
{
    if (IsInsideAnyGridVolume(WorldPosition))
    {
        float3 gridColor = SampleGridLightProbeAuto(WorldPosition, normalize(WorldNormal));
        FinalColor = lerp(UnityLightProbeColor, gridColor, BlendFactor);
    }
    else
    {
        FinalColor = UnityLightProbeColor;
    }
}

void SampleGridLightProbeWithFallback_half(
    half3 WorldPosition,
    half3 WorldNormal,
    half3 UnityLightProbeColor,
    half BlendFactor,
    out half3 FinalColor)
{
    float3 finalColorFloat;
    SampleGridLightProbeWithFallback_float(
        WorldPosition, WorldNormal, UnityLightProbeColor, BlendFactor, finalColorFloat);
    FinalColor = (half3)finalColorFloat;
}

/// <summary>
/// 获取多 Volume 调试信息
/// </summary>
void GetMultiGridVolumeDebugInfo_float(
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
        VolumeCount = IsInsideGridVolume(WorldPosition) ? 1 : 0;
        TotalWeight = VolumeCount > 0 ? 1.0 : 0.0;
        DebugColor = VolumeCount > 0 ? float3(0, 1, 0) : float3(0, 0, 0);
    }
}

void GetMultiGridVolumeDebugInfo_half(
    half3 WorldPosition,
    out half3 DebugColor,
    out int VolumeCount,
    out half TotalWeight)
{
    float3 debugColorFloat;
    float totalWeightFloat;
    GetMultiGridVolumeDebugInfo_float(WorldPosition, debugColorFloat, VolumeCount, totalWeightFloat);
    DebugColor = (half3)debugColorFloat;
    TotalWeight = (half)totalWeightFloat;
}

/// <summary>
/// SDF 增强采样 (自动选择单/多 Volume 模式) + 障碍物遮挡
/// </summary>
void SampleGridLightProbeSDFEnhanced_float(
    float3 WorldPosition,
    float3 WorldNormal,
    float3 HitPosition,
    float HasHit,
    float3 FallbackDirection,
    out float3 Color,
    out float IsInside)
{
    if (IsInsideAnyGridVolume(WorldPosition))
    {
        Color = SampleGridLightProbeAutoSDFEnhanced(
            WorldPosition, normalize(WorldNormal),
            HitPosition, HasHit > 0.5, normalize(FallbackDirection));
        IsInside = 1.0;
    }
    else
    {
        Color = float3(0, 0, 0);
        IsInside = 0.0;
    }
}

void SampleGridLightProbeSDFEnhanced_half(
    half3 WorldPosition,
    half3 WorldNormal,
    half3 HitPosition,
    half HasHit,
    half3 FallbackDirection,
    out half3 Color,
    out half IsInside)
{
    float3 colorFloat;
    float isInsideFloat;
    SampleGridLightProbeSDFEnhanced_float(
        WorldPosition, WorldNormal, HitPosition, HasHit, FallbackDirection,
        colorFloat, isInsideFloat);
    Color = (half3)colorFloat;
    IsInside = (half)isInsideFloat;
}

#endif // UNIFORM_GRID_LIGHT_PROBE_OBSTACLE_FUNCTIONS_INCLUDED
