#ifndef UNIFORM_GRID_LIGHT_PROBE_OBSTACLE_INCLUDED
#define UNIFORM_GRID_LIGHT_PROBE_OBSTACLE_INCLUDED

// ============================================================================
// UniformGridLightProbeObstacle.hlsl
// UniformGrid 采样 + OBB 障碍物遮挡
// 函数签名与 UniformGridLightProbe.hlsl 保持一致，可直接替换
// ============================================================================

#include "LightProbeTypes.hlsl"

// ----------------------------------------------------------------------------
// Grid 特有的全局变量
// ----------------------------------------------------------------------------

int3 _GridResolution;   // 网格分辨率 (nx, ny, nz)

// ----------------------------------------------------------------------------
// 障碍物相关声明
// ----------------------------------------------------------------------------

struct ObstacleOBB
{
    float3 center;       // Volume 局部空间中心（烘焙时转换）
    float3 axisX;        // X 轴方向（归一化）
    float3 axisY;        // Y 轴方向（归一化）
    float3 halfExtents;  // 各轴半尺寸
};

StructuredBuffer<ObstacleOBB> _ObstacleBuffer;
int _ObstacleCount;

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
// 障碍物遮挡工具函数
// ----------------------------------------------------------------------------

// 从 node 的 obstacleIndices 字段解包 4 个索引
void UnpackObstacleIndices(uint packed, out int idx0, out int idx1, out int idx2, out int idx3)
{
    idx0 = (int)(packed & 0xFF);
    idx1 = (int)((packed >> 8) & 0xFF);
    idx2 = (int)((packed >> 16) & 0xFF);
    idx3 = (int)((packed >> 24) & 0xFF);
}

// 解析式 Ray-OBB 相交测试 (slab method)
bool RayOBBIntersect(float3 rayOrigin, float3 rayDir, ObstacleOBB obb, out float tMin, out float tMax)
{
    rayDir = normalize(rayDir);
    float3 localOrigin = rayOrigin - obb.center;
    float3 axisZ = cross(obb.axisX, obb.axisY);

    float3 o = float3(dot(localOrigin, obb.axisX), dot(localOrigin, obb.axisY), dot(localOrigin, axisZ));
    float3 d = float3(dot(rayDir, obb.axisX), dot(rayDir, obb.axisY), dot(rayDir, axisZ));

    float3 invD = 1.0 / d;
    float3 t0 = (-obb.halfExtents - o) * invD;
    float3 t1 = ( obb.halfExtents - o) * invD;

    float3 tNear = min(t0, t1);
    float3 tFar  = max(t0, t1);

    tMin = max(max(tNear.x, tNear.y), tNear.z);
    tMax = min(min(tFar.x, tFar.y), tFar.z);

    return tMax >= max(tMin, 0.0);
}

// 判断点是否在 OBB 内部
bool PointInsideOBB(float3 pt, ObstacleOBB obb)
{
    float3 localPoint = pt - obb.center;
    float3 axisZ = cross(obb.axisX, obb.axisY);

    float3 projected = float3(
        dot(localPoint, obb.axisX),
        dot(localPoint, obb.axisY),
        dot(localPoint, axisZ)
    );

    return all(abs(projected) <= obb.halfExtents);
}

// 计算 Probe 的遮挡权重 (1.0 = 未遮挡, 0.0 = 被遮挡)
// probeOrigin 和 samplePos 均为 Volume 局部空间坐标（OBB 数据已在烘焙时转为局部空间）
float GetObstacleWeight(float3 probeOrigin, float3 samplePos, OctreeNode node)
{
    int idx0, idx1, idx2, idx3;
    UnpackObstacleIndices(node.obstacleIndices, idx0, idx1, idx2, idx3);

    int indices[4] = { idx0, idx1, idx2, idx3 };
    float3 rayDir = samplePos - probeOrigin;
    float rayLength = length(rayDir);
    rayDir /= max(rayLength, 0.0001);

    for (int i = 0; i < 4; i++)
    {
        if (indices[i] >= 255) continue;

        ObstacleOBB obb = _ObstacleBuffer[indices[i]];

        // 如果 probe 在 OBB 内部，该 Probe 贡献归零
        if (PointInsideOBB(probeOrigin, obb))
            return 0.0;

        // 检查射线是否在到达采样点之前击中 OBB
        float tMin, tMax;
        if (RayOBBIntersect(probeOrigin, rayDir, obb, tMin, tMax))
        {
            // tMin < 0 表示射线起点在 OBB 内部（已由 PointInsideOBB 处理）
            // 使用 max(tMin, 0) 作为实际进入点，只要进入点在采样点之前就遮挡
            float tEntry = max(tMin, 0.0);
            if (tEntry < rayLength)
                return 0.0;
        }
    }

    return 1.0;
}


// ----------------------------------------------------------------------------
// Grid 基础采样（三线性插值 + 障碍物遮挡）
// ----------------------------------------------------------------------------

float3 SampleGridLightProbe(float3 worldPos, float3 worldNormal)
{
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 cellCenter, cellHalfExtents;
    int nodeIndex = QueryGridNode(localPos, cellCenter, cellHalfExtents);
    if (nodeIndex < 0) return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);
    float3 cellLocalPos = (localPos - cellCenter) / cellHalfExtents;
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
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

        float obstacleW = GetObstacleWeight(probeLocalPos, localPos, node);
        float weight = triWeight * obstacleW;

        SHCoefficients probeSH = GetCornerSH(node, i);
        accumColor += EvaluateSHL1(probeSH, localNormal) * weight;
        totalWeight += weight;
    }

    // 所有 Probe 均被遮挡时回退到标准三线性插值
    if (totalWeight <= 0.0)
    {
        SHCoefficients interpolatedSH = TrilinearInterpolateSH(node, cellLocalPos);
        return EvaluateSHL1(interpolatedSH, localNormal);
    }

    return accumColor / totalWeight;
}

// ----------------------------------------------------------------------------
// Grid 方向加权采样（+ 障碍物遮挡）
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
        float obstacleW = GetObstacleWeight(probeLocalPos, localPos, node);
        float weight = triWeight * dirWeight * obstacleW;

        SHCoefficients probeSH = GetCornerSH(node, i);
        accumColor += EvaluateSHL1(probeSH, localNormal) * weight;
        totalWeight += weight;
    }

    // 所有 Probe 均被遮挡时回退到标准三线性插值
    if (totalWeight <= 0.0)
    {
        SHCoefficients interpolatedSH = TrilinearInterpolateSH(node, cellLocalPos);
        return EvaluateSHL1(interpolatedSH, localNormal);
    }

    return accumColor / totalWeight;
}

#endif // UNIFORM_GRID_LIGHT_PROBE_OBSTACLE_INCLUDED