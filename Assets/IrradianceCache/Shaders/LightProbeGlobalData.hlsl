#ifndef IRRADIANCE_CACHE_GLOBAL
#define IRRADIANCE_CACHE_GLOBAL

#define MAX_VOLUME_COUNT 8

// ============================================================================
// UniformGridLightProbe.hlsl
// 均匀网格 Light Probe GPU 查询和插值 (单 Volume)
// 共享类型和工具函数通过 LightProbeTypes.hlsl 引入
// ============================================================================

#include "LightProbeTypes.hlsl"

// ----------------------------------------------------------------------------
// 多 Volume 数据结构
// ----------------------------------------------------------------------------

// Volume 元数据结构 (与 C# VolumeMetadata 对应)
struct VolumeMetadata
{
    int nodeStartIndex;      // 在合并 buffer 中的偏移
    int nodeCount;           // 节点数量
    int maxDepth;            // 树深度
    int priority;            // 优先级

    float3 rootCenter;       // Volume 中心
    float3 rootHalfExtents;  // 各轴半尺寸

    float4x4 worldToLocal;   // Transform 矩阵
    float4x4 rotationMatrix; // 旋转矩阵

    float blendDistance;     // 边界混合距离
    int isActive;            // 是否激活
    int useCompactFormat;    // 格式标记
    int _padding;            // 对齐填充

    int useTransitionBlending;  // 使用深度混合
    float transitionWidthRatio;
    int gridResolutionX;        // Grid 模式字段 (Octree 模式忽略，仅占位对齐)
    int gridResolutionY;
    int gridResolutionZ;
    int _padding2;
};

// ----------------------------------------------------------------------------
// 多 Volume 全局变量
// ----------------------------------------------------------------------------

// 合并后的节点 buffer (仅支持 Compact 格式)
StructuredBuffer<OctreeNodeCompact> _MergedNodesCompact;

// Volume 元数据 buffer
StructuredBuffer<VolumeMetadata> _VolumeMetadataBuffer;

// 激活的 Volume 数量
int _ActiveVolumeCount;

// 是否使用多 Volume 模式
int _UseMultiVolumeMode;

// 合并 buffer 中的总节点数（用于边界检查）
int _TotalMergedNodeCount;

// ----------------------------------------------------------------------------
// 天空球回退 SH 数据
// ----------------------------------------------------------------------------

// 天空球 SH 系数 (L1: 4 系数 × 3 通道, 格式 [L0, L1x, L1y, L1z])
float4 _SkyboxSH_R;
float4 _SkyboxSH_G;
float4 _SkyboxSH_B;

// 是否启用天空球回退
int _EnableSkyboxFallback;

// ----------------------------------------------------------------------------
// 多 Volume 工具函数
// ----------------------------------------------------------------------------

// 创建空的 VolumeMetadata（用于越界访问时返回安全值）
VolumeMetadata CreateEmptyVolumeMetadata()
{
    VolumeMetadata empty;
    empty.nodeStartIndex = 0;
    empty.nodeCount = 0;
    empty.maxDepth = 0;
    empty.priority = 0;
    empty.rootCenter = float3(0, 0, 0);
    empty.rootHalfExtents = float3(0, 0, 0);
    empty.worldToLocal = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
    empty.rotationMatrix = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
    empty.blendDistance = 0;
    empty.isActive = 0;
    empty.useCompactFormat = 0;
    empty._padding = 0;
    empty.useTransitionBlending = 0;
    empty.transitionWidthRatio = 0;
    empty.gridResolutionX = 0;
    empty.gridResolutionY = 0;
    empty.gridResolutionZ = 0;
    empty._padding2 = 0;
    return empty;
}

// 获取指定 Volume 的元数据（带边界检查）
VolumeMetadata GetVolumeMetadata(int volumeIndex)
{
    // 边界检查：防止显存越界访问导致DX11崩溃
    if (volumeIndex < 0 || volumeIndex >= _ActiveVolumeCount)
    {
        return CreateEmptyVolumeMetadata();
    }
    return _VolumeMetadataBuffer[volumeIndex];
}

// 将世界坐标变换到指定 Volume 的局部空间
float3 WorldToVolumeLocalMulti(float3 worldPos, VolumeMetadata meta)
{
    return mul(meta.worldToLocal, float4(worldPos, 1.0)).xyz;
}

// 将世界法线变换到指定 Volume 的局部空间
float3 WorldNormalToVolumeLocalMulti(float3 worldNormal, VolumeMetadata meta)
{
    return normalize(mul((float3x3)meta.rotationMatrix, worldNormal));
}

// 从合并 buffer 获取节点（带边界检查）
OctreeNode GetMergedNode(int index)
{
    if (index < 0 || index >= _TotalMergedNodeCount)
    {
        return CreateEmptyNode();
    }
    return UnpackOctreeNode(_MergedNodesCompact[index]);
}

// ----------------------------------------------------------------------------
// 天空球 SH 评估
// ----------------------------------------------------------------------------

// 使用天空球 SH 评估间接光照
float3 EvaluateSkyboxSH(float3 worldNormal)
{
    float4 basis = float4(
        SH_C0,
        SH_C1 * worldNormal.x,
        SH_C1 * worldNormal.y,
        SH_C1 * worldNormal.z
    );

    float3 result;
    result.r = dot(_SkyboxSH_R, basis);
    result.g = dot(_SkyboxSH_G, basis);
    result.b = dot(_SkyboxSH_B, basis);

    return max(0, result);
}

// ----------------------------------------------------------------------------
// 边界检测与混合权重
// ----------------------------------------------------------------------------

// 检查位置是否在 Volume 内
bool IsInsideVolume(float3 worldPos)
{
    float3 localPos = WorldToVolumeLocal(worldPos);
    float3 d = abs(localPos - _OctreeRootCenter);
    return d.x <= _OctreeRootHalfExtents.x &&
           d.y <= _OctreeRootHalfExtents.y &&
           d.z <= _OctreeRootHalfExtents.z;
}

// 检查点是否在指定 Volume 内
bool IsInsideVolume(float3 worldPos, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return false;

    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);
    float3 d = abs(localPos - meta.rootCenter);
    return all(d <= meta.rootHalfExtents);
}

// 计算点在 Volume 内的混合权重 (边界渐变)
float GetVolumeBlendWeight(float3 worldPos, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return 0.0;

    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);
    float3 localToRoot = localPos - meta.rootCenter;

    float blendDist = meta.blendDistance;

    // 计算到边界的最小距离
    float distX = meta.rootHalfExtents.x - abs(localToRoot.x);
    float distY = meta.rootHalfExtents.y - abs(localToRoot.y);
    float distZ = meta.rootHalfExtents.z - abs(localToRoot.z);
    float minDist = min(distX, min(distY, distZ));

    // 如果在 Volume 外部
    if (minDist < 0.0)
        return 0.0;

    // 在混合区域内进行平滑过渡
    if (blendDist > 0.0 && minDist < blendDist)
    {
        return smoothstep(0.0, 1.0, minDist / blendDist);
    }

    return 1.0;
}

// ----------------------------------------------------------------------------
// 调试函数
// ----------------------------------------------------------------------------

// 获取包含指定点的 Volume 数量
int GetContainingVolumeCount(float3 worldPos)
{
    int count = 0;

    [loop]
    for (int i = 0; i < _ActiveVolumeCount && i < MAX_VOLUME_COUNT; i++)
    {
        VolumeMetadata meta = GetVolumeMetadata(i);
        if (IsInsideVolume(worldPos, meta))
            count++;
    }

    return count;
}

// 获取指定点的混合权重总和 (用于调试可视化)
float GetTotalBlendWeight(float3 worldPos)
{
    float totalWeight = 0.0;

    [loop]
    for (int i = 0; i < _ActiveVolumeCount && i < MAX_VOLUME_COUNT; i++)
    {
        VolumeMetadata meta = GetVolumeMetadata(i);
        totalWeight += GetVolumeBlendWeight(worldPos, meta);
    }

    return totalWeight;
}

// 获取多 Volume 调试颜色 (根据包含的 Volume 数量着色)
float3 GetMultiVolumeDebugColor(float3 worldPos)
{
    int count = GetContainingVolumeCount(worldPos);

    if (count == 0)
        return float3(0, 0, 0);      // 黑色 - 不在任何 Volume 内
    else if (count == 1)
        return float3(0, 1, 0);      // 绿色 - 单 Volume
    else if (count == 2)
        return float3(1, 1, 0);      // 黄色 - 2 Volume 重叠
    else
        return float3(1, 0, 0);      // 红色 - 3+ Volume 重叠
}
#endif