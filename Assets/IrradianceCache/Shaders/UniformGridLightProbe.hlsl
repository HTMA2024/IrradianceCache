#ifndef UNIFORM_GRID_LIGHT_PROBE_INCLUDED
#define UNIFORM_GRID_LIGHT_PROBE_INCLUDED

// ============================================================================

// ----------------------------------------------------------------------------
// Grid 特有的全局变量
// ----------------------------------------------------------------------------

int3 _GridResolution;   // 网格分辨率 (nx, ny, nz)

// ----------------------------------------------------------------------------
// Grid O(1) 查询
// ----------------------------------------------------------------------------

// 查询 localPos 所在的 cell 索引，同时输出 cell 的中心和半尺寸
int QueryGridNode(float3 localPos, out float3 outCellCenter, out float3 outCellHalfExtents)
{
    float3 volumeMin = _OctreeRootCenter - _OctreeRootHalfExtents;
    float3 volumeSize = _OctreeRootHalfExtents * 2.0;
    float3 normalizedPos = (localPos - volumeMin) / volumeSize;

    if (any(normalizedPos < 0.0) || any(normalizedPos > 1.0))
    {
        outCellCenter = float3(0, 0, 0);
        outCellHalfExtents = float3(0, 0, 0);
        return -1;
    }

    int3 cellIdx = clamp(int3(normalizedPos * float3(_GridResolution)),
                         int3(0,0,0), _GridResolution - int3(1,1,1));
    int linearIndex = cellIdx.z * (_GridResolution.x * _GridResolution.y)
                    + cellIdx.y * _GridResolution.x
                    + cellIdx.x;

    if (linearIndex < 0 || linearIndex >= _OctreeNodeCount)
    {
        outCellCenter = float3(0, 0, 0);
        outCellHalfExtents = float3(0, 0, 0);
        return -1;
    }

    float3 cellSize = volumeSize / float3(_GridResolution);
    outCellHalfExtents = cellSize * 0.5;
    outCellCenter = volumeMin + cellSize * (float3(cellIdx) + 0.5);
    return linearIndex;
}

// 仅查询索引（不返回 bounds）
int QueryGridNodeSimple(float3 localPos)
{
    float3 dummyCenter, dummyHalf;
    return QueryGridNode(localPos, dummyCenter, dummyHalf);
}

// ----------------------------------------------------------------------------
// Grid 基础采样（三线性插值）
// ----------------------------------------------------------------------------

float3 SampleGridLightProbe(float3 worldPos, float3 worldNormal)
{
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 cellCenter, cellHalfExtents;
    int nodeIndex = QueryGridNode(localPos, cellCenter, cellHalfExtents);
    if (nodeIndex < 0) return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);
    float3 cellLocalPos = (localPos - cellCenter) / cellHalfExtents;

    SHCoefficients interpolatedSH = TrilinearInterpolateSH(node, cellLocalPos);
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
    return EvaluateSHL1(interpolatedSH, localNormal);
}

// ----------------------------------------------------------------------------
// Grid 方向加权采样
// ----------------------------------------------------------------------------

float3 SampleGridLightProbeDirectionWeighted(float3 worldPos, float3 worldNormal)
{
    float3 localPos = WorldToVolumeLocal(worldPos);
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);

    float3 cellCenter, cellHalfExtents;
    int nodeIndex = QueryGridNode(localPos, cellCenter, cellHalfExtents);
    if (nodeIndex < 0) return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);
    float3 cellLocalPos = (localPos - cellCenter) / cellHalfExtents;
    float3 t = saturate((cellLocalPos + 1.0) * 0.5);

    float3 accumColor = float3(0, 0, 0);
    float totalWeight = 0.0;

    [unroll]
    for (int i = 0; i < 8; i++)
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
// Grid SDF 增强采样
// ----------------------------------------------------------------------------

float3 SampleGridLightProbeSDFEnhanced(
    float3 worldPos, float3 worldNormal,
    float3 hitPosWorld, bool hasHit, float3 fallbackDirWorld)
{
    float3 localPos = WorldToVolumeLocal(worldPos);
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
    float3 hitPosLocal = WorldToVolumeLocal(hitPosWorld);
    float3 fallbackDirLocal = WorldNormalToVolumeLocal(fallbackDirWorld);

    float3 cellCenter, cellHalfExtents;
    int nodeIndex = QueryGridNode(localPos, cellCenter, cellHalfExtents);
    if (nodeIndex < 0) return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);
    float3 cellLocalPos = (localPos - cellCenter) / cellHalfExtents;
    float3 t = saturate((cellLocalPos + 1.0) * 0.5);

    float3 accumColor = float3(0, 0, 0);
    float totalWeight = 0.0;

    [unroll]
    for (int i = 0; i < 8; i++)
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

        float3 sampleDir;
        if (hasHit)
        {
            float3 toHit = hitPosLocal - probeLocalPos;
            float dist = length(toHit);
            sampleDir = (dist > 0.001) ? (toHit / dist) : fallbackDirLocal;
        }
        else
        {
            sampleDir = fallbackDirLocal;
        }

        float NdotL = saturate(dot(localNormal, sampleDir));
        float weight = triWeight * NdotL;
        if (weight <= 0.0) continue;

        SHCoefficients probeSH = GetCornerSH(node, i);
        accumColor += EvaluateSHL1(probeSH, sampleDir) * weight;
        totalWeight += weight;
    }

    if (totalWeight > 0.0)
        return accumColor / totalWeight;
    return SampleGridLightProbe(worldPos, fallbackDirWorld);
}

#endif // UNIFORM_GRID_LIGHT_PROBE_INCLUDED
