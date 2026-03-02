#ifndef UNIFORM_GRID_LIGHT_PROBE_MULTI_INCLUDED
#define UNIFORM_GRID_LIGHT_PROBE_MULTI_INCLUDED

// ============================================================================
// UniformGridLightProbeMulti.hlsl
// 均匀网格 Light Probe 多 Volume 支持
// ============================================================================

#include "LightProbeGlobalData.hlsl"
#include "UniformGridLightProbe.hlsl"

// ----------------------------------------------------------------------------
// Grid 多 Volume 查询
// ----------------------------------------------------------------------------

// 在指定 Volume 中查询 Grid 节点
int QueryVolumeGridNode(float3 localPos, VolumeMetadata meta,
    out float3 outCellCenter, out float3 outCellHalfExtents)
{
    int3 gridRes = int3(meta.gridResolutionX, meta.gridResolutionY, meta.gridResolutionZ);

    float3 volumeMin = meta.rootCenter - meta.rootHalfExtents;
    float3 volumeSize = meta.rootHalfExtents * 2.0;
    float3 normalizedPos = (localPos - volumeMin) / volumeSize;

    if (any(normalizedPos < 0.0) || any(normalizedPos > 1.0))
    {
        outCellCenter = float3(0, 0, 0);
        outCellHalfExtents = float3(0, 0, 0);
        return -1;
    }

    int3 cellIdx = clamp(int3(normalizedPos * float3(gridRes)),
                         int3(0,0,0), gridRes - int3(1,1,1));
    int linearIndex = cellIdx.z * (gridRes.x * gridRes.y)
                    + cellIdx.y * gridRes.x + cellIdx.x;
    int globalIndex = meta.nodeStartIndex + linearIndex;

    if (linearIndex < 0 || linearIndex >= meta.nodeCount ||
        globalIndex < 0 || globalIndex >= _TotalMergedNodeCount)
    {
        outCellCenter = float3(0, 0, 0);
        outCellHalfExtents = float3(0, 0, 0);
        return -1;
    }

    float3 cellSize = volumeSize / float3(gridRes);
    outCellHalfExtents = cellSize * 0.5;
    outCellCenter = volumeMin + cellSize * (float3(cellIdx) + 0.5);
    return globalIndex;
}

// ----------------------------------------------------------------------------
// 多 Volume SH 采样
// ----------------------------------------------------------------------------

// 从指定 Volume 采样光照颜色
float3 SampleVolumeLighting(float3 worldPos, float3 worldNormal, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return float3(0, 0, 0);

    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);

    float3 cellCenter, cellHalfExtents;
    int nodeIndex = QueryVolumeGridNode(localPos, meta, cellCenter, cellHalfExtents);
    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetMergedNode(nodeIndex);
    float3 cellLocalPos = (localPos - cellCenter) / cellHalfExtents;
    SHCoefficients interpolatedSH = TrilinearInterpolateSH(node, cellLocalPos);

    float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);
    return EvaluateSHL1(interpolatedSH, localNormal);
}

// 从指定 Volume 采样 - 方向加权版本
float3 SampleVolumeLightingDirectionWeighted(
    float3 worldPos, float3 worldNormal, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return float3(0, 0, 0);

    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);
    float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);

    float3 cellCenter, cellHalfExtents;
    int nodeIndex = QueryVolumeGridNode(localPos, meta, cellCenter, cellHalfExtents);
    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetMergedNode(nodeIndex);
    float3 cellLocalPos = (localPos - cellCenter) / cellHalfExtents;
    float3 t = saturate((cellLocalPos + 1.0) * 0.5);

    float3 accumColor = float3(0, 0, 0);
    float totalWeight = 0.0;

    [unroll]
    for (int i = 0; i < MAX_VOLUME_COUNT; i++)
    {
        float3 cornerBit = float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
        float triWeight =
            (cornerBit.x > 0.5 ? t.x : (1.0 - t.x)) *
            (cornerBit.y > 0.5 ? t.y : (1.0 - t.y)) *
            (cornerBit.z > 0.5 ? t.z : (1.0 - t.z));

        float3 cornerOffset = float3(
            (i & 1) ? cellHalfExtents.x : -cellHalfExtents.x,
            (i & 2) ? cellHalfExtents.y : -cellHalfExtents.y,
            (i & 4) ? cellHalfExtents.z : -cellHalfExtents.z
        );
        float3 probeLocalPos = cellCenter + cornerOffset;
        float dirWeight = GetDirectionWeight(probeLocalPos, localPos, localNormal);
        float weight = triWeight * dirWeight;

        SHCoefficients probeSH = GetCornerSH(node, i);
        accumColor += EvaluateSHL1(probeSH, localNormal) * weight;
        totalWeight += weight;
    }

    return accumColor / max(totalWeight, 0.0001);
}

// ----------------------------------------------------------------------------
// 多 Volume 混合采样 (主要函数)
// ----------------------------------------------------------------------------

// 多 Volume 加权混合采样
float3 SampleMultiGridLightProbe(float3 worldPos, float3 worldNormal,
    out float totalWeight, out int volumeCount)
{
    float3 result = float3(0, 0, 0);
    totalWeight = 0.0;
    volumeCount = 0;

    [loop]
    for (int i = 0; i < _ActiveVolumeCount && i < MAX_VOLUME_COUNT; i++)
    {
        VolumeMetadata meta = GetVolumeMetadata(i);

        if (meta.isActive == 0)
            continue;

        float weight = GetVolumeBlendWeight(worldPos, meta);

        if (weight > 0.0)
        {
            float3 volumeColor = SampleVolumeLighting(worldPos, worldNormal, meta);

            // 优先级加权 (priority 0-100 映射到 1.0-2.0)
            float priorityWeight = 1.0 + (float)meta.priority / 100.0;
            weight *= priorityWeight;

            result += volumeColor * weight;
            totalWeight += weight;
            volumeCount++;
        }
    }

    if (totalWeight > 0.0)
    {
        result /= totalWeight;
    }

    // 天空球回退混合：当 totalWeight 不足 1.0 时，用天空球补充
    if (_EnableSkyboxFallback)
    {
        float volumeInfluence = saturate(totalWeight);
        if (volumeInfluence < 1.0)
        {
            float3 skyboxColor = EvaluateSkyboxSH(worldNormal);
            result = lerp(skyboxColor, result, volumeInfluence);
        }
    }

    return result;
}

// 简化版多 Volume 采样 (不返回额外信息)
float3 SampleMultiGridLightProbeSimple(float3 worldPos, float3 worldNormal)
{
    float totalWeight;
    int volumeCount;
    return SampleMultiGridLightProbe(worldPos, worldNormal, totalWeight, volumeCount);
}


// ----------------------------------------------------------------------------
// 自动模式选择
// ----------------------------------------------------------------------------

// 自动选择单/多 Volume 模式的采样函数
float3 SampleGridLightProbeAuto(float3 worldPos, float3 worldNormal)
{
    if (_UseMultiVolumeMode && _ActiveVolumeCount > 0)
    {
        return SampleMultiGridLightProbeSimple(worldPos, worldNormal);
    }
    else
    {
        if (IsInsideVolume(worldPos))
        {
            return SampleGridLightProbe(worldPos, worldNormal);
        }

        if (_EnableSkyboxFallback)
        {
            return EvaluateSkyboxSH(worldNormal);
        }

        return float3(0, 0, 0);
    }
}


#endif // UNIFORM_GRID_LIGHT_PROBE_MULTI_INCLUDED
