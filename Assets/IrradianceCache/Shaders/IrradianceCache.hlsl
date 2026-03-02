#ifndef IRRADIANCE_CACHE_INCLUDED
#define IRRADIANCE_CACHE_INCLUDED

#include "LightProbeGlobalData.hlsl"

// ----------------------------------------------------------------------------
// Octree 特有的全局变量
// ----------------------------------------------------------------------------

int _OctreeMaxDepth;

// 跨深度过渡混合参数
int _OctreeEnableTransitionBlending;    // 是否启用过渡混合 (0 = 禁用, 1 = 启用)
float _OctreeTransitionWidthRatio;      // 过渡宽度比例 (相对于节点尺寸, 默认 0.15)

// 采样模式
// 0 = Basic (基础三线性插值)
// 1 = WithBlending (跨深度过渡混合)
// 2 = DirectionWeighted (方向加权)
// 3 = DirectionWeightedWithBlending (方向加权 + 跨深度混合)
int _OctreeSamplingMode;

// ----------------------------------------------------------------------------
// Morton Code 工具函数
// ----------------------------------------------------------------------------

// 压缩函数：将分散的位压缩回来
uint Compact1By2(uint n)
{
    n &= 0x09249249;
    n = (n ^ (n >> 2)) & 0x030c30c3;
    n = (n ^ (n >> 4)) & 0x0300f00f;
    n = (n ^ (n >> 8)) & 0xff0000ff;
    n = (n ^ (n >> 16)) & 0x000003ff;
    return n;
}

// 从 Morton Code 解码 3D 坐标
void DecodeMorton3D(uint code, out uint x, out uint y, out uint z)
{
    x = Compact1By2(code);
    y = Compact1By2(code >> 1);
    z = Compact1By2(code >> 2);
}

// ----------------------------------------------------------------------------
// 节点位置计算
// ----------------------------------------------------------------------------

// 从节点计算世界空间中心
float3 GetNodeWorldCenter(OctreeNode node)
{
    uint gx, gy, gz;
    DecodeMorton3D(node.mortonCode, gx, gy, gz);

    uint resolution = 1u << node.depth;
    float3 cellSize = (_OctreeRootHalfExtents * 2.0) / resolution;

    float3 minCorner = _OctreeRootCenter - _OctreeRootHalfExtents;

    return float3(
        minCorner.x + (gx + 0.5) * cellSize.x,
        minCorner.y + (gy + 0.5) * cellSize.y,
        minCorner.z + (gz + 0.5) * cellSize.z
    );
}

// 计算节点半尺寸
float3 GetNodeHalfExtents(int depth)
{
    return _OctreeRootHalfExtents / (1 << depth);
}

// ----------------------------------------------------------------------------
// 八叉树查询
// ----------------------------------------------------------------------------

// 查询世界坐标所在的叶节点索引（内部使用，接受已变换的局部坐标）
int QueryOctreeNodeInternal(float3 localPos)
{
    // 检查是否在根节点范围内
    float3 localToRoot = localPos - _OctreeRootCenter;
    if (abs(localToRoot.x) > _OctreeRootHalfExtents.x ||
        abs(localToRoot.y) > _OctreeRootHalfExtents.y ||
        abs(localToRoot.z) > _OctreeRootHalfExtents.z)
    {
        return -1; // 超出范围
    }

    int currentIndex = 0;
    float3 nodeCenter = _OctreeRootCenter;
    float3 nodeHalfExtents = _OctreeRootHalfExtents;

    [loop]
    for (int depth = 0; depth < _OctreeMaxDepth; depth++)
    {
        if (currentIndex < 0 || currentIndex >= _OctreeNodeCount)
            return -1;

        OctreeNode node = GetOctreeNode(currentIndex);

        // 如果是叶节点，返回当前索引
        if (node.childStartIndex == -1)
            return currentIndex;

        // 计算该位置位于哪个子节点
        float3 relPos = localPos - nodeCenter;
        int childIndex = 0;
        if (relPos.x > 0) childIndex |= 1;
        if (relPos.y > 0) childIndex |= 2;
        if (relPos.z > 0) childIndex |= 4;

        // 更新到子节点
        currentIndex = node.childStartIndex + childIndex;

        // 边界检查：验证子节点索引有效性
        if (currentIndex < 0 || currentIndex >= _OctreeNodeCount)
            return -1;

        nodeHalfExtents *= 0.5;

        // 计算子节点中心
        float3 childOffset = float3(
            (childIndex & 1) ? nodeHalfExtents.x : -nodeHalfExtents.x,
            (childIndex & 2) ? nodeHalfExtents.y : -nodeHalfExtents.y,
            (childIndex & 4) ? nodeHalfExtents.z : -nodeHalfExtents.z
        );
        nodeCenter += childOffset;
    }

    return currentIndex;
}

// 查询世界坐标所在的叶节点索引（自动应用 Transform）
int QueryOctreeNode(float3 worldPos)
{
    float3 localPos = WorldToVolumeLocal(worldPos);
    return QueryOctreeNodeInternal(localPos);
}

// 查询并返回节点中心和半尺寸（内部使用，接受已变换的局部坐标）
int QueryOctreeNodeWithBoundsInternal(float3 localPos, out float3 outNodeCenter, out float3 outNodeHalfExtents)
{
    outNodeCenter = _OctreeRootCenter;
    outNodeHalfExtents = _OctreeRootHalfExtents;

    // 检查是否在根节点范围内
    float3 localToRoot = localPos - _OctreeRootCenter;
    if (abs(localToRoot.x) > _OctreeRootHalfExtents.x ||
        abs(localToRoot.y) > _OctreeRootHalfExtents.y ||
        abs(localToRoot.z) > _OctreeRootHalfExtents.z)
    {
        return -1;
    }

    int currentIndex = 0;
    float3 nodeCenter = _OctreeRootCenter;
    float3 nodeHalfExtents = _OctreeRootHalfExtents;

    [loop]
    for (int depth = 0; depth < _OctreeMaxDepth; depth++)
    {
        if (currentIndex < 0 || currentIndex >= _OctreeNodeCount)
            return -1;

        OctreeNode node = GetOctreeNode(currentIndex);

        if (node.childStartIndex == -1)
        {
            outNodeCenter = nodeCenter;
            outNodeHalfExtents = nodeHalfExtents;
            return currentIndex;
        }

        float3 relPos = localPos - nodeCenter;
        int childIndex = 0;
        if (relPos.x > 0) childIndex |= 1;
        if (relPos.y > 0) childIndex |= 2;
        if (relPos.z > 0) childIndex |= 4;

        currentIndex = node.childStartIndex + childIndex;

        // 边界检查：验证子节点索引有效性
        if (currentIndex < 0 || currentIndex >= _OctreeNodeCount)
            return -1;

        nodeHalfExtents *= 0.5;

        float3 childOffset = float3(
            (childIndex & 1) ? nodeHalfExtents.x : -nodeHalfExtents.x,
            (childIndex & 2) ? nodeHalfExtents.y : -nodeHalfExtents.y,
            (childIndex & 4) ? nodeHalfExtents.z : -nodeHalfExtents.z
        );
        nodeCenter += childOffset;
    }

    outNodeCenter = nodeCenter;
    outNodeHalfExtents = nodeHalfExtents;
    return currentIndex;
}

// 查询并返回节点中心和半尺寸（自动应用 Transform）
int QueryOctreeNodeWithBounds(float3 worldPos, out float3 outNodeCenter, out float3 outNodeHalfExtents)
{
    float3 localPos = WorldToVolumeLocal(worldPos);
    return QueryOctreeNodeWithBoundsInternal(localPos, outNodeCenter, outNodeHalfExtents);
}


// ----------------------------------------------------------------------------
// 主要采样函数
// ----------------------------------------------------------------------------

// 采样八叉树 Light Probe (完整版，自动应用 Transform)
float3 SampleIrradianceCache(float3 worldPos, float3 worldNormal)
{
    // 1. 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 nodeCenter;
    float3 nodeHalfExtents;

    // 2. 使用局部坐标查询节点
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0); // 超出范围，返回黑色

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 3. 计算节点内的局部坐标 [-1, 1]
    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;

    // 4. 三线性插值 SH 系数
    SHCoefficients interpolatedSH = TrilinearInterpolateSH(node, nodeLocalPos);

    // 5. 变换法线到 Volume 局部空间
    // 这等价于用旋转后的 SH 系数评估原始法线
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);

    // 6. 用局部法线评估球谐光照
    return EvaluateSHL1(interpolatedSH, localNormal);
}

// 采样八叉树 Light Probe (简化版，不带插值)
float3 SampleIrradianceCacheSimple(float3 worldPos, float3 worldNormal)
{
    // 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);

    int nodeIndex = QueryOctreeNodeInternal(localPos);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 使用节点中心的 SH (corner0 和 corner7 的平均)
    SHCoefficients centerSH = LerpSH(node.corner0, node.corner7, 0.5);

    // 变换法线到 Volume 局部空间
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);

    return EvaluateSHL1(centerSH, localNormal);
}

// 采样八叉树 Light Probe 并返回 SH 系数
SHCoefficients SampleIrradianceCacheSH(float3 worldPos)
{
    // 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 nodeCenter;
    float3 nodeHalfExtents;

    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    SHCoefficients result;
    result.shR = float4(0, 0, 0, 0);
    result.shG = float4(0, 0, 0, 0);
    result.shB = float4(0, 0, 0, 0);

    if (nodeIndex < 0)
        return result;

    OctreeNode node = GetOctreeNode(nodeIndex);

    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;

    return TrilinearInterpolateSH(node, nodeLocalPos);
}

// ----------------------------------------------------------------------------
// 调试函数
// ----------------------------------------------------------------------------

// ----------------------------------------------------------------------------
// 跨深度过渡混合采样 (解决 T-Junction 接缝问题)
// ----------------------------------------------------------------------------

// Smootherstep: 五次多项式，比 smoothstep 更平滑
// 导数在端点处为0，二阶导数也为0，提供更平滑的过渡
float Smootherstep(float t)
{
    t = saturate(t);
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// Smoothstep 的七次多项式版本，提供更高阶的平滑度
float Smootherstep7(float t)
{
    t = saturate(t);
    return t * t * t * t * (t * (t * (70.0 - 20.0 * t) - 84.0) + 35.0);
}

// 余弦插值：使用余弦函数实现平滑过渡
float CosineInterpolation(float t)
{
    t = saturate(t);
    return 0.5 * (1.0 - cos(3.14159265359 * t));
}

// 双向感知的过渡宽度调整
// 根据节点深度差和方向自适应调整过渡宽度
float GetAdaptiveTransitionWidth(float baseWidth, int currentDepth, int neighborDepth)
{
    int depthDiff = neighborDepth - currentDepth;

    // 子节点到父节点：使用更宽的过渡区域
    if (depthDiff < 0)
    {
        return baseWidth * (1.0 + 0.5 * abs(depthDiff));
    }
    // 父节点到子节点：标准过渡区域
    else
    {
        return baseWidth * (1.0 + 0.2 * depthDiff);
    }
}

// 深度感知的权重调整
// 深度差越大，混合越平滑
float GetDepthAwareWeight(float baseWeight, int depthDiff)
{
    // 深度差越大，权重曲线越平缓
    float depthFactor = 1.0 / (1.0 + 0.3 * abs(depthDiff));
    return pow(baseWeight, depthFactor);
}

// 预过滤权重：减少高频噪声
float ApplyPrefilter(float weight, float smoothness)
{
    // 应用低通滤波器减少权重的高频变化
    weight = saturate(weight);
    float filtered = weight * weight * (3.0 - 2.0 * weight); // smoothstep
    return lerp(weight, filtered, smoothness);
}



// 深度感知的多邻居过渡混合采样
// 支持角落（3个邻居）、边（2个邻居）和面（1个邻居）的混合
float3 SampleIrradianceCacheWithBlending(float3 worldPos, float3 worldNormal)
{
    // 1. 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 nodeCenter;
    float3 nodeHalfExtents;

    // 2. 查询当前节点
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 3. 计算节点内的局部坐标 [-1, 1]
    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;

    // 4. 基础采样
    SHCoefficients baseSH = TrilinearInterpolateSH(node, nodeLocalPos);

    // 5. 检查是否启用过渡混合，以及是否为稀疏节点（深度未达到最大值）
    // if (!_OctreeEnableTransitionBlending)
    // {
    //     float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
    //     return EvaluateSHL1(baseSH, localNormal);
    // }_

    #if defined (_OCTREE_SAMPLING_MODE_WITH_BLENDING_ON)
    if (!_OctreeEnableTransitionBlending)
    {
        float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
        return EvaluateSHL1(baseSH, localNormal);
    }
    // 6. 计算到边界的距离
    float3 relPos = localPos - nodeCenter;
    float3 absRelPos = abs(relPos);
    float3 distToFace = nodeHalfExtents - absRelPos;

    // 7. 自适应过渡宽度：使用配置的比例
    float3 transitionWidth = nodeHalfExtents * _OctreeTransitionWidthRatio;

    // 8. 检查每个方向是否在过渡区域内
    bool3 inTransition = distToFace < transitionWidth;

    // Early-out：如果所有方向都不在过渡区域
    if (!any(inTransition))
    {
        float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
        return EvaluateSHL1(baseSH, localNormal);
    }
    // 9. 计算累积的 SH 和权重
    SHCoefficients accumulatedSH = baseSH;
    float totalWeight = 1.0;

    // 10. 处理每个可能的邻居方向
    // X 方向邻居
    if (inTransition.x)
    {
        float3 xNeighborDir = float3(sign(relPos.x), 0, 0);
        float3 xNeighborQueryPos = localPos + xNeighborDir * nodeHalfExtents.x * 0.5;

        float3 xNeighborCenter;
        float3 xNeighborHalfExtents;
        int xNeighborIndex = QueryOctreeNodeWithBoundsInternal(xNeighborQueryPos, xNeighborCenter, xNeighborHalfExtents);

        if (xNeighborIndex >= 0)
        {
            OctreeNode xNeighborNode = GetOctreeNode(xNeighborIndex);
            if (xNeighborNode.depth > node.depth)
            {
                float3 xNeighborLocalPos = (localPos - xNeighborCenter) / xNeighborHalfExtents;
                xNeighborLocalPos = clamp(xNeighborLocalPos, -0.99, 0.99);

                SHCoefficients xNeighborSH = TrilinearInterpolateSH(xNeighborNode, xNeighborLocalPos);

                // 使用 Smootherstep 获得更平滑的过渡
                float xT = distToFace.x / transitionWidth.x;
                float xWeight = 1.0 - smoothstep(0,1,xT);

                // 应用深度感知权重调整
                int depthDiff = abs(xNeighborNode.depth - node.depth);
                xWeight = GetDepthAwareWeight(xWeight, depthDiff);

                accumulatedSH = LerpSH(accumulatedSH, xNeighborSH, xWeight );
                totalWeight += xWeight;
            }
        }
    }

    // Y 方向邻居
    if (inTransition.y)
    {
        float3 yNeighborDir = float3(0, sign(relPos.y), 0);
        float3 yNeighborQueryPos = localPos + yNeighborDir * nodeHalfExtents.y * 0.5;

        float3 yNeighborCenter;
        float3 yNeighborHalfExtents;
        int yNeighborIndex = QueryOctreeNodeWithBoundsInternal(yNeighborQueryPos, yNeighborCenter, yNeighborHalfExtents);

        if (yNeighborIndex >= 0)
        {
            OctreeNode yNeighborNode = GetOctreeNode(yNeighborIndex);
            if (yNeighborNode.depth > node.depth)
            {
                float3 yNeighborLocalPos = (localPos - yNeighborCenter) / yNeighborHalfExtents;
                yNeighborLocalPos = clamp(yNeighborLocalPos, -0.99, 0.99);

                SHCoefficients yNeighborSH = TrilinearInterpolateSH(yNeighborNode, yNeighborLocalPos);

                // 使用 Smootherstep 获得更平滑的过渡
                float yT = distToFace.y / transitionWidth.y;
                float yWeight = 1.0 - smoothstep(0,1,yT);

                // 应用深度感知权重调整
                int depthDiff = abs(yNeighborNode.depth - node.depth);
                yWeight = GetDepthAwareWeight(yWeight, depthDiff);

                accumulatedSH = LerpSH(accumulatedSH, yNeighborSH, yWeight );
                totalWeight += yWeight;
            }
        }
    }

    // Z 方向邻居
    if (inTransition.z)
    {
        float3 zNeighborDir = float3(0, 0, sign(relPos.z));
        float3 zNeighborQueryPos = localPos + zNeighborDir * nodeHalfExtents.z * 0.5;

        float3 zNeighborCenter;
        float3 zNeighborHalfExtents;
        int zNeighborIndex = QueryOctreeNodeWithBoundsInternal(zNeighborQueryPos, zNeighborCenter, zNeighborHalfExtents);

        if (zNeighborIndex >= 0)
        {
            OctreeNode zNeighborNode = GetOctreeNode(zNeighborIndex);
            if (zNeighborNode.depth > node.depth)
            {
                float3 zNeighborLocalPos = (localPos - zNeighborCenter) / zNeighborHalfExtents;
                zNeighborLocalPos = clamp(zNeighborLocalPos, -0.99, 0.99);

                SHCoefficients zNeighborSH = TrilinearInterpolateSH(zNeighborNode, zNeighborLocalPos);

                // 使用 Smootherstep 获得更平滑的过渡
                float zT = distToFace.z / transitionWidth.z;
                float zWeight = 1.0 - smoothstep(0,1,zT);

                // 应用深度感知权重调整
                int depthDiff = abs(zNeighborNode.depth - node.depth);
                zWeight = GetDepthAwareWeight(zWeight, depthDiff);

                accumulatedSH = LerpSH(accumulatedSH, zNeighborSH, zWeight );
                totalWeight += zWeight;
            }
        }
    }

    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
    return EvaluateSHL1(accumulatedSH, localNormal);
    #else
        float3 localNormal = WorldNormalToVolumeLocal(worldNormal);
        return EvaluateSHL1(baseSH, localNormal);
    #endif
}

// ----------------------------------------------------------------------------
// 调试函数
// ----------------------------------------------------------------------------

// 获取节点深度颜色 (用于可视化)
float3 GetNodeDepthColor(float3 worldPos)
{
    // 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);

    int nodeIndex = QueryOctreeNodeInternal(localPos);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 深度颜色：绿色(浅) -> 红色(深)
    float t = (float)node.depth / (float)_OctreeMaxDepth;
    return lerp(float3(0, 1, 0), float3(1, 0, 0), t);
}

// 检查位置是否在八叉树范围内（自动应用 Transform）
bool IsInsideOctreeVolume(float3 worldPos)
{
    // 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 localToRoot = localPos - _OctreeRootCenter;
    return abs(localToRoot.x) <= _OctreeRootHalfExtents.x &&
           abs(localToRoot.y) <= _OctreeRootHalfExtents.y &&
           abs(localToRoot.z) <= _OctreeRootHalfExtents.z;
}

// ----------------------------------------------------------------------------
// 跨深度过渡混合调试可视化
// ----------------------------------------------------------------------------

// 获取过渡区域可视化颜色
// 返回不同颜色显示混合的邻居数量和深度差
float3 GetTransitionZoneDebugColor(float3 worldPos)
{
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 如果是最深的节点，不需要混合
    if (node.depth >= _OctreeMaxDepth - 1)
        return float3(0, 1, 0); // 绿色

    // 计算到边界的距离
    float3 relPos = localPos - nodeCenter;
    float3 absRelPos = abs(relPos);
    float3 distToFace = nodeHalfExtents - absRelPos;

    float3 transitionWidth = nodeHalfExtents * _OctreeTransitionWidthRatio;

    // 检查每个方向是否在过渡区域内
    bool3 inTransition = distToFace < transitionWidth;

    // 计算在过渡区域内的方向数量
    int transitionCount = (int)inTransition.x + (int)inTransition.y + (int)inTransition.z;

    // 不在过渡区域
    if (transitionCount == 0)
        return float3(0, 1, 0); // 绿色

    // 统计更深邻居的数量和最大深度差
    int deeperNeighborCount = 0;
    int maxDepthDiff = 0;

    // 检查 X 方向邻居
    if (inTransition.x)
    {
        float3 xNeighborDir = float3(sign(relPos.x), 0, 0);
        float3 xNeighborQueryPos = localPos + xNeighborDir * nodeHalfExtents.x * 0.5;

        float3 xNeighborCenter;
        float3 xNeighborHalfExtents;
        int xNeighborIndex = QueryOctreeNodeWithBoundsInternal(xNeighborQueryPos, xNeighborCenter, xNeighborHalfExtents);

        if (xNeighborIndex >= 0)
        {
            OctreeNode xNeighborNode = GetOctreeNode(xNeighborIndex);
            if (xNeighborNode.depth > node.depth)
            {
                deeperNeighborCount++;
                maxDepthDiff = max(maxDepthDiff, xNeighborNode.depth - node.depth);
            }
        }
    }

    // 检查 Y 方向邻居
    if (inTransition.y)
    {
        float3 yNeighborDir = float3(0, sign(relPos.y), 0);
        float3 yNeighborQueryPos = localPos + yNeighborDir * nodeHalfExtents.y * 0.5;

        float3 yNeighborCenter;
        float3 yNeighborHalfExtents;
        int yNeighborIndex = QueryOctreeNodeWithBoundsInternal(yNeighborQueryPos, yNeighborCenter, yNeighborHalfExtents);

        if (yNeighborIndex >= 0)
        {
            OctreeNode yNeighborNode = GetOctreeNode(yNeighborIndex);
            if (yNeighborNode.depth > node.depth)
            {
                deeperNeighborCount++;
                maxDepthDiff = max(maxDepthDiff, yNeighborNode.depth - node.depth);
            }
        }
    }

    // 检查 Z 方向邻居
    if (inTransition.z)
    {
        float3 zNeighborDir = float3(0, 0, sign(relPos.z));
        float3 zNeighborQueryPos = localPos + zNeighborDir * nodeHalfExtents.z * 0.5;

        float3 zNeighborCenter;
        float3 zNeighborHalfExtents;
        int zNeighborIndex = QueryOctreeNodeWithBoundsInternal(zNeighborQueryPos, zNeighborCenter, zNeighborHalfExtents);

        if (zNeighborIndex >= 0)
        {
            OctreeNode zNeighborNode = GetOctreeNode(zNeighborIndex);
            if (zNeighborNode.depth > node.depth)
            {
                deeperNeighborCount++;
                maxDepthDiff = max(maxDepthDiff, zNeighborNode.depth - node.depth);
            }
        }
    }

    // 根据混合的邻居数量选择颜色
    // 1个邻居: 黄色
    // 2个邻居: 橙色
    // 3个邻居: 红色
    // 深度差>=2: 紫色
    if (maxDepthDiff >= 2)
        return float3(1, 0, 1); // 紫色：深度差>=2
    else if (deeperNeighborCount == 3)
        return float3(1, 0, 0); // 红色：3个邻居（角落）
    else if (deeperNeighborCount == 2)
        return float3(1, 0.5, 0); // 橙色：2个邻居（边）
    else if (deeperNeighborCount == 1)
        return float3(1, 1, 0); // 黄色：1个邻居（面）
    else
        return float3(0, 0.5, 1); // 青色：在过渡区但无更深邻居
}

// 获取混合权重可视化（灰度）
float GetBlendWeightDebug(float3 worldPos)
{
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return 0;

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 如果是最深的节点，不需要混合
    if (node.depth >= _OctreeMaxDepth - 1)
        return 1.0;

    float3 relPos = localPos - nodeCenter;
    float3 distToFace = nodeHalfExtents - abs(relPos);

    float3 transitionWidth = nodeHalfExtents * _OctreeTransitionWidthRatio;

    // 检查每个方向是否在过渡区域内
    bool3 inTransition = distToFace < transitionWidth;

    if (!any(inTransition))
        return 1.0; // 完全使用当前节点

    // 计算综合的混合权重
    float totalNeighborWeight = 0;
    float totalWeight = 1.0;

    // X 方向权重
    if (inTransition.x)
    {
        float xT = saturate(distToFace.x / transitionWidth.x);
        float xWeight = 1.0 - (xT * xT * (3.0 - 2.0 * xT)); // 1 - smoothstep
        totalNeighborWeight += xWeight;
        totalWeight += xWeight;
    }

    // Y 方向权重
    if (inTransition.y)
    {
        float yT = saturate(distToFace.y / transitionWidth.y);
        float yWeight = 1.0 - (yT * yT * (3.0 - 2.0 * yT)); // 1 - smoothstep
        totalNeighborWeight += yWeight;
        totalWeight += yWeight;
    }

    // Z 方向权重
    if (inTransition.z)
    {
        float zT = saturate(distToFace.z / transitionWidth.z);
        float zWeight = 1.0 - (zT * zT * (3.0 - 2.0 * zT)); // 1 - smoothstep
        totalNeighborWeight += zWeight;
        totalWeight += zWeight;
    }

    // 返回当前节点的权重比例
    return 1.0 / totalWeight;
}

// 获取多邻居混合的贡献度可视化
// 返回 RGB，每个通道代表一个方向的贡献度
float3 GetMultiNeighborContributionDebug(float3 worldPos)
{
    float3 localPos = WorldToVolumeLocal(worldPos);

    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 如果是最深的节点，不需要混合
    if (node.depth >= _OctreeMaxDepth - 1)
        return float3(0, 0, 0);

    float3 relPos = localPos - nodeCenter;
    float3 absRelPos = abs(relPos);
    float3 distToFace = nodeHalfExtents - absRelPos;

    float3 transitionWidth = nodeHalfExtents * _OctreeTransitionWidthRatio;

    // 检查每个方向是否在过渡区域内
    bool3 inTransition = distToFace < transitionWidth;

    float3 contribution = float3(0, 0, 0);

    // X 方向贡献度（红色通道）
    if (inTransition.x)
    {
        float xT = saturate(distToFace.x / transitionWidth.x);
        contribution.r = 1.0 - (xT * xT * (3.0 - 2.0 * xT));
    }

    // Y 方向贡献度（绿色通道）
    if (inTransition.y)
    {
        float yT = saturate(distToFace.y / transitionWidth.y);
        contribution.g = 1.0 - (yT * yT * (3.0 - 2.0 * yT));
    }

    // Z 方向贡献度（蓝色通道）
    if (inTransition.z)
    {
        float zT = saturate(distToFace.z / transitionWidth.z);
        contribution.b = 1.0 - (zT * zT * (3.0 - 2.0 * zT));
    }

    return contribution;
}

// ----------------------------------------------------------------------------
// 方向加权采样 (Direction Weighted Sampling)
// 通过方向系数抑制背面probe的贡献，减少漏光
// ----------------------------------------------------------------------------

// 方向加权的 Light Probe 采样
// 使用三线性系数 × 方向系数的组合权重
float3 SampleIrradianceCacheDirectionWeighted(float3 worldPos, float3 worldNormal)
{
    // 1. 变换到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);

    // 2. 查询节点
    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 3. 计算节点内归一化位置 [-1, 1]^3 -> [0, 1]^3
    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;
    float3 t = saturate((nodeLocalPos + 1.0) * 0.5);

    // 4. 对8个角点进行加权采样
    float3 accumColor = float3(0, 0, 0);
    float totalWeight = 0.0;

    [unroll]
    for (int i = 0; i < 8; i++)
    {
        // 4.1 计算三线性权重
        float3 cornerBit = float3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
        float triWeight =
            (cornerBit.x > 0.5 ? t.x : (1.0 - t.x)) *
            (cornerBit.y > 0.5 ? t.y : (1.0 - t.y)) *
            (cornerBit.z > 0.5 ? t.z : (1.0 - t.z));

        // 4.2 计算角点坐标（局部空间）
        float3 cornerOffset = float3(
            (i & 1) ? nodeHalfExtents.x : -nodeHalfExtents.x,
            (i & 2) ? nodeHalfExtents.y : -nodeHalfExtents.y,
            (i & 4) ? nodeHalfExtents.z : -nodeHalfExtents.z
        );
        float3 probeLocalPos = nodeCenter + cornerOffset;

        // 4.3 计算方向权重
        float dirWeight = GetDirectionWeight(probeLocalPos, localPos, localNormal);

        // 4.4 组合权重：三线性 × 方向
        float weight = triWeight * dirWeight;

        // 4.5 获取 SH 并评估
        SHCoefficients probeSH = GetCornerSH(node, i);
        float3 probeColor = EvaluateSHL1(probeSH, localNormal);

        accumColor += probeColor * weight;
        totalWeight += weight;
    }

    // 5. 归一化，确保能量守恒
    return accumColor / max(totalWeight, 0.0001);
}

// 方向加权采样 + 跨深度过渡混合
// 结合方向加权和邻居节点混合，提供更好的漏光抑制和连续性
float3 SampleIrradianceCacheDirectionWeightedWithBlending(float3 worldPos, float3 worldNormal)
{
    // 1. 变换到 Volume 局部空间
    float3 localPos = WorldToVolumeLocal(worldPos);
    float3 localNormal = WorldNormalToVolumeLocal(worldNormal);

    float3 nodeCenter;
    float3 nodeHalfExtents;

    // 2. 查询当前节点
    int nodeIndex = QueryOctreeNodeWithBoundsInternal(localPos, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetOctreeNode(nodeIndex);

    // 3. 计算节点内归一化位置
    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;
    float3 t = saturate((nodeLocalPos + 1.0) * 0.5);

    // 4. 基础方向加权采样
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
            (i & 1) ? nodeHalfExtents.x : -nodeHalfExtents.x,
            (i & 2) ? nodeHalfExtents.y : -nodeHalfExtents.y,
            (i & 4) ? nodeHalfExtents.z : -nodeHalfExtents.z
        );
        float3 probeLocalPos = nodeCenter + cornerOffset;

        float dirWeight = GetDirectionWeight(probeLocalPos, localPos, localNormal);
        float weight = triWeight * dirWeight;

        SHCoefficients probeSH = GetCornerSH(node, i);
        float3 probeColor = EvaluateSHL1(probeSH, localNormal);

        accumColor += probeColor * weight;
        totalWeight += weight;
    }

    float3 baseColor = accumColor / max(totalWeight, 0.0001);

    // 5. 检查是否启用过渡混合
    if (!_OctreeEnableTransitionBlending)
    {
        return baseColor;
    }

    // 6. 计算到边界的距离
    float3 relPos = localPos - nodeCenter;
    float3 absRelPos = abs(relPos);
    float3 distToFace = nodeHalfExtents - absRelPos;

    float3 transitionWidth = nodeHalfExtents * _OctreeTransitionWidthRatio;

    // 7. 检查每个方向是否在过渡区域内
    bool3 inTransition = distToFace < transitionWidth;

    // Early-out：如果所有方向都不在过渡区域
    if (!any(inTransition))
    {
        return baseColor;
    }

    // 8. 混合邻居节点（使用方向加权采样）
    float3 blendedColor = baseColor;
    float blendTotalWeight = 1.0;

    // X 方向邻居
    if (inTransition.x)
    {
        float3 xNeighborDir = float3(sign(relPos.x), 0, 0);
        float3 xNeighborQueryPos = localPos + xNeighborDir * nodeHalfExtents.x * 0.5;

        float3 xNeighborCenter;
        float3 xNeighborHalfExtents;
        int xNeighborIndex = QueryOctreeNodeWithBoundsInternal(xNeighborQueryPos, xNeighborCenter, xNeighborHalfExtents);

        if (xNeighborIndex >= 0)
        {
            OctreeNode xNeighborNode = GetOctreeNode(xNeighborIndex);
            if (xNeighborNode.depth > node.depth)
            {
                // 对邻居节点也使用方向加权采样
                float3 xNeighborLocalPos = (localPos - xNeighborCenter) / xNeighborHalfExtents;
                float3 xT = saturate((clamp(xNeighborLocalPos, -0.99, 0.99) + 1.0) * 0.5);

                float3 xNeighborColor = float3(0, 0, 0);
                float xNeighborTotalWeight = 0.0;

                [unroll]
                for (int j = 0; j < 8; j++)
                {
                    float3 cBit = float3(j & 1, (j >> 1) & 1, (j >> 2) & 1);
                    float tW =
                        (cBit.x > 0.5 ? xT.x : (1.0 - xT.x)) *
                        (cBit.y > 0.5 ? xT.y : (1.0 - xT.y)) *
                        (cBit.z > 0.5 ? xT.z : (1.0 - xT.z));

                    float3 cOffset = float3(
                        (j & 1) ? xNeighborHalfExtents.x : -xNeighborHalfExtents.x,
                        (j & 2) ? xNeighborHalfExtents.y : -xNeighborHalfExtents.y,
                        (j & 4) ? xNeighborHalfExtents.z : -xNeighborHalfExtents.z
                    );
                    float3 pPos = xNeighborCenter + cOffset;

                    float dW = GetDirectionWeight(pPos, localPos, localNormal);
                    float w = tW * dW;

                    SHCoefficients pSH = GetCornerSH(xNeighborNode, j);
                    float3 pColor = EvaluateSHL1(pSH, localNormal);

                    xNeighborColor += pColor * w;
                    xNeighborTotalWeight += w;
                }

                xNeighborColor /= max(xNeighborTotalWeight, 0.0001);

                float xBlendT = distToFace.x / transitionWidth.x;
                float xWeight = 1.0 - smoothstep(0, 1, xBlendT);

                blendedColor = lerp(blendedColor, xNeighborColor, xWeight / (blendTotalWeight + xWeight));
                blendTotalWeight += xWeight;
            }
        }
    }

    // Y 方向邻居
    if (inTransition.y)
    {
        float3 yNeighborDir = float3(0, sign(relPos.y), 0);
        float3 yNeighborQueryPos = localPos + yNeighborDir * nodeHalfExtents.y * 0.5;

        float3 yNeighborCenter;
        float3 yNeighborHalfExtents;
        int yNeighborIndex = QueryOctreeNodeWithBoundsInternal(yNeighborQueryPos, yNeighborCenter, yNeighborHalfExtents);

        if (yNeighborIndex >= 0)
        {
            OctreeNode yNeighborNode = GetOctreeNode(yNeighborIndex);
            if (yNeighborNode.depth > node.depth)
            {
                float3 yNeighborLocalPos = (localPos - yNeighborCenter) / yNeighborHalfExtents;
                float3 yT = saturate((clamp(yNeighborLocalPos, -0.99, 0.99) + 1.0) * 0.5);

                float3 yNeighborColor = float3(0, 0, 0);
                float yNeighborTotalWeight = 0.0;

                [unroll]
                for (int j = 0; j < 8; j++)
                {
                    float3 cBit = float3(j & 1, (j >> 1) & 1, (j >> 2) & 1);
                    float tW =
                        (cBit.x > 0.5 ? yT.x : (1.0 - yT.x)) *
                        (cBit.y > 0.5 ? yT.y : (1.0 - yT.y)) *
                        (cBit.z > 0.5 ? yT.z : (1.0 - yT.z));

                    float3 cOffset = float3(
                        (j & 1) ? yNeighborHalfExtents.x : -yNeighborHalfExtents.x,
                        (j & 2) ? yNeighborHalfExtents.y : -yNeighborHalfExtents.y,
                        (j & 4) ? yNeighborHalfExtents.z : -yNeighborHalfExtents.z
                    );
                    float3 pPos = yNeighborCenter + cOffset;

                    float dW = GetDirectionWeight(pPos, localPos, localNormal);
                    float w = tW * dW;

                    SHCoefficients pSH = GetCornerSH(yNeighborNode, j);
                    float3 pColor = EvaluateSHL1(pSH, localNormal);

                    yNeighborColor += pColor * w;
                    yNeighborTotalWeight += w;
                }

                yNeighborColor /= max(yNeighborTotalWeight, 0.0001);

                float yBlendT = distToFace.y / transitionWidth.y;
                float yWeight = 1.0 - smoothstep(0, 1, yBlendT);

                blendedColor = lerp(blendedColor, yNeighborColor, yWeight / (blendTotalWeight + yWeight));
                blendTotalWeight += yWeight;
            }
        }
    }

    // Z 方向邻居
    if (inTransition.z)
    {
        float3 zNeighborDir = float3(0, 0, sign(relPos.z));
        float3 zNeighborQueryPos = localPos + zNeighborDir * nodeHalfExtents.z * 0.5;

        float3 zNeighborCenter;
        float3 zNeighborHalfExtents;
        int zNeighborIndex = QueryOctreeNodeWithBoundsInternal(zNeighborQueryPos, zNeighborCenter, zNeighborHalfExtents);

        if (zNeighborIndex >= 0)
        {
            OctreeNode zNeighborNode = GetOctreeNode(zNeighborIndex);
            if (zNeighborNode.depth > node.depth)
            {
                float3 zNeighborLocalPos = (localPos - zNeighborCenter) / zNeighborHalfExtents;
                float3 zT = saturate((clamp(zNeighborLocalPos, -0.99, 0.99) + 1.0) * 0.5);

                float3 zNeighborColor = float3(0, 0, 0);
                float zNeighborTotalWeight = 0.0;

                [unroll]
                for (int j = 0; j < 8; j++)
                {
                    float3 cBit = float3(j & 1, (j >> 1) & 1, (j >> 2) & 1);
                    float tW =
                        (cBit.x > 0.5 ? zT.x : (1.0 - zT.x)) *
                        (cBit.y > 0.5 ? zT.y : (1.0 - zT.y)) *
                        (cBit.z > 0.5 ? zT.z : (1.0 - zT.z));

                    float3 cOffset = float3(
                        (j & 1) ? zNeighborHalfExtents.x : -zNeighborHalfExtents.x,
                        (j & 2) ? zNeighborHalfExtents.y : -zNeighborHalfExtents.y,
                        (j & 4) ? zNeighborHalfExtents.z : -zNeighborHalfExtents.z
                    );
                    float3 pPos = zNeighborCenter + cOffset;

                    float dW = GetDirectionWeight(pPos, localPos, localNormal);
                    float w = tW * dW;

                    SHCoefficients pSH = GetCornerSH(zNeighborNode, j);
                    float3 pColor = EvaluateSHL1(pSH, localNormal);

                    zNeighborColor += pColor * w;
                    zNeighborTotalWeight += w;
                }

                zNeighborColor /= max(zNeighborTotalWeight, 0.0001);

                float zBlendT = distToFace.z / transitionWidth.z;
                float zWeight = 1.0 - smoothstep(0, 1, zBlendT);

                blendedColor = lerp(blendedColor, zNeighborColor, zWeight / (blendTotalWeight + zWeight));
                blendTotalWeight += zWeight;
            }
        }
    }

    return blendedColor;
}

// ----------------------------------------------------------------------------
// 统一采样入口函数
// 根据 _OctreeSamplingMode 自动选择对应的采样方法
// ----------------------------------------------------------------------------

// // 统一采样入口 - 根据全局采样模式自动选择
// float3 SampleIrradianceCacheAuto(float3 worldPos, float3 worldNormal)
// {
//     switch (_OctreeSamplingMode)
//     {
//         case OCTREE_SAMPLING_MODE_WITH_BLENDING:
//             return SampleIrradianceCacheWithBlending(worldPos, worldNormal);
//
//         case OCTREE_SAMPLING_MODE_DIRECTION_WEIGHTED:
//             return SampleIrradianceCacheDirectionWeighted(worldPos, worldNormal);
//
//         case OCTREE_SAMPLING_MODE_DIRECTION_WEIGHTED_WITH_BLENDING:
//             return SampleIrradianceCacheDirectionWeightedWithBlending(worldPos, worldNormal);
//
//         case OCTREE_SAMPLING_MODE_BASIC:
//         default:
//             return SampleIrradianceCache(worldPos, worldNormal);
//     }
// }

#endif // IRRADIANCE_CACHE_INCLUDED
