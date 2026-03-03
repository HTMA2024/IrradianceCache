#ifndef IRRADIANCE_CACHE_MULTI_INCLUDED
#define IRRADIANCE_CACHE_MULTI_INCLUDED

// ============================================================================
// IrradianceCacheMulti.hlsl
// 多 Volume 支持的八叉树 Light Probe GPU 查询和混合
// ============================================================================

#include "LightProbeGlobalData.hlsl"
#include "IrradianceCache.hlsl"

// ----------------------------------------------------------------------------
// 多 Volume 节点查询
// ----------------------------------------------------------------------------

// 在指定 Volume 中查询节点
int QueryVolumeNode(float3 localPos, VolumeMetadata meta, out float3 outNodeCenter, out float3 outNodeHalfExtents)
{
    outNodeCenter = meta.rootCenter;
    outNodeHalfExtents = meta.rootHalfExtents;

    // 检查元数据有效性
    if (meta.nodeCount <= 0 || any(meta.rootHalfExtents <= 0))
        return -1;

    // 检查是否在根节点范围内
    float3 localToRoot = localPos - meta.rootCenter;
    if (abs(localToRoot.x) > meta.rootHalfExtents.x ||
        abs(localToRoot.y) > meta.rootHalfExtents.y ||
        abs(localToRoot.z) > meta.rootHalfExtents.z)
    {
        return -1;
    }

    int currentIndex = meta.nodeStartIndex;
    float3 nodeCenter = meta.rootCenter;
    float3 nodeHalfExtents = meta.rootHalfExtents;

    [loop]
    for (int depth = 0; depth < meta.maxDepth; depth++)
    {
        // 边界检查：验证当前索引在Volume范围内
        if (currentIndex < meta.nodeStartIndex ||
            currentIndex >= meta.nodeStartIndex + meta.nodeCount)
            return -1;

        // 边界检查：验证当前索引在全局Buffer范围内
        if (currentIndex < 0 || currentIndex >= _TotalMergedNodeCount)
            return -1;

        OctreeNode node = GetMergedNode(currentIndex);

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
        if (currentIndex < 0 || currentIndex >= _TotalMergedNodeCount)
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

// ----------------------------------------------------------------------------
// 多 Volume SH 采样
// ----------------------------------------------------------------------------

// 从指定 Volume 采样 SH 系数
SHCoefficients SampleOctreeVolumeSH(float3 worldPos, VolumeMetadata meta)
{
    SHCoefficients result;
    result.shR = float4(0, 0, 0, 0);
    result.shG = float4(0, 0, 0, 0);
    result.shB = float4(0, 0, 0, 0);

    if (meta.isActive == 0)
        return result;

    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);

    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryVolumeNode(localPos, meta, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return result;

    OctreeNode node = GetMergedNode(nodeIndex);

    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;

    return TrilinearInterpolateSH(node, nodeLocalPos);
}

// 深度感知的多邻居过渡混合采样
// 支持角落（3个邻居）、边（2个邻居）和面（1个邻居）的混合
float3 SampleIrradianceCacheWithBlending(float3 worldPos, float3 worldNormal, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return float3(0, 0, 0);
    
    // 1. 变换坐标到 Volume 局部空间
    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);

    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryVolumeNode(localPos, meta, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetMergedNode(nodeIndex);

    // 3. 计算节点内的局部坐标 [-1, 1]
    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;

    // 4. 基础采样
    SHCoefficients baseSH = TrilinearInterpolateSH(node, nodeLocalPos);

    // 5. 检查是否启用过渡混合，以及是否为稀疏节点（深度未达到最大值）
    #if defined (_OCTREE_SAMPLING_MODE_WITH_BLENDING_ON)
    if (!meta.useTransitionBlending)
    {
        float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);
        return EvaluateSHL1(baseSH, localNormal);
    }
    
    // 6. 计算到边界的距离
    float3 relPos = localPos - nodeCenter;
    float3 absRelPos = abs(relPos);
    float3 distToFace = nodeHalfExtents - absRelPos;

    // 7. 自适应过渡宽度：使用配置的比例
    float3 transitionWidth = nodeHalfExtents * meta.transitionWidthRatio;

    // 8. 检查每个方向是否在过渡区域内
    bool3 inTransition = distToFace < transitionWidth;

    // Early-out：如果所有方向都不在过渡区域
    if (!any(inTransition))
    {
        float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);
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
        float3 xNeighborQueryPos = localPos + xNeighborDir * (distToFace.x + 1e-5);

        float3 xNeighborCenter;
        float3 xNeighborHalfExtents;
        int xNeighborIndex = QueryVolumeNode(xNeighborQueryPos, meta, xNeighborCenter, xNeighborHalfExtents);

        if (xNeighborIndex >= 0)
        {
            OctreeNode xNeighborNode = GetMergedNode(xNeighborIndex);
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
        float3 yNeighborQueryPos = localPos + yNeighborDir * (distToFace.y + 1e-5);

        float3 yNeighborCenter;
        float3 yNeighborHalfExtents;
        int yNeighborIndex = QueryVolumeNode(yNeighborQueryPos, meta, yNeighborCenter, yNeighborHalfExtents);

        if (yNeighborIndex >= 0)
        {
            OctreeNode yNeighborNode = GetMergedNode(yNeighborIndex);
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
        float3 zNeighborQueryPos = localPos + zNeighborDir * (distToFace.z + 1e-5);

        float3 zNeighborCenter;
        float3 zNeighborHalfExtents;
        int zNeighborIndex = QueryVolumeNode(zNeighborQueryPos, meta, zNeighborCenter, zNeighborHalfExtents);

        if (zNeighborIndex >= 0)
        {
            OctreeNode zNeighborNode = GetMergedNode(zNeighborIndex);
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

    float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);
    return EvaluateSHL1(accumulatedSH, localNormal);
    #else
        float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);
        return EvaluateSHL1(baseSH, localNormal);
    #endif

}

// 从指定 Volume 采样光照颜色
float3 SampleOctreeVolumeLighting(float3 worldPos, float3 worldNormal, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return float3(0, 0, 0);

    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);

    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryVolumeNode(localPos, meta, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetMergedNode(nodeIndex);

    float3 nodeLocalPos = (localPos - nodeCenter) / nodeHalfExtents;
    SHCoefficients interpolatedSH = TrilinearInterpolateSH(node, nodeLocalPos);

    float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);

    return EvaluateSHL1(interpolatedSH, localNormal);
}

// ----------------------------------------------------------------------------
// 方向加权采样 (多 Volume 版本)
// ----------------------------------------------------------------------------

// 从指定 Volume 采样 - 方向加权版本
float3 SampleOctreeVolumeLightingDirectionWeighted(float3 worldPos, float3 worldNormal, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return float3(0, 0, 0);

    // 1. 变换到 Volume 局部空间
    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);
    float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);

    // 2. 查询节点
    float3 nodeCenter;
    float3 nodeHalfExtents;
    int nodeIndex = QueryVolumeNode(localPos, meta, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetMergedNode(nodeIndex);

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

// 从指定 Volume 采样 - 方向加权 + 跨深度混合版本
float3 SampleOctreeVolumeLightingDirectionWeightedWithBlending(float3 worldPos, float3 worldNormal, VolumeMetadata meta)
{
    if (meta.isActive == 0)
        return float3(0, 0, 0);

    // 1. 变换到 Volume 局部空间
    float3 localPos = WorldToVolumeLocalMulti(worldPos, meta);
    float3 localNormal = WorldNormalToVolumeLocalMulti(worldNormal, meta);

    float3 nodeCenter;
    float3 nodeHalfExtents;

    // 2. 查询当前节点
    int nodeIndex = QueryVolumeNode(localPos, meta, nodeCenter, nodeHalfExtents);

    if (nodeIndex < 0)
        return float3(0, 0, 0);

    OctreeNode node = GetMergedNode(nodeIndex);

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
    if (!meta.useTransitionBlending)
    {
        return baseColor;
    }

    // 6. 计算到边界的距离
    float3 relPos = localPos - nodeCenter;
    float3 absRelPos = abs(relPos);
    float3 distToFace = nodeHalfExtents - absRelPos;

    float3 transitionWidth = nodeHalfExtents * meta.transitionWidthRatio;

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
        float3 xNeighborQueryPos = localPos + xNeighborDir * (distToFace.x + 1e-5);

        float3 xNeighborCenter;
        float3 xNeighborHalfExtents;
        int xNeighborIndex = QueryVolumeNode(xNeighborQueryPos, meta, xNeighborCenter, xNeighborHalfExtents);

        if (xNeighborIndex >= 0)
        {
            OctreeNode xNeighborNode = GetMergedNode(xNeighborIndex);
            if (xNeighborNode.depth > node.depth)
            {
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
        float3 yNeighborQueryPos = localPos + yNeighborDir * (distToFace.y + 1e-5);

        float3 yNeighborCenter;
        float3 yNeighborHalfExtents;
        int yNeighborIndex = QueryVolumeNode(yNeighborQueryPos, meta, yNeighborCenter, yNeighborHalfExtents);

        if (yNeighborIndex >= 0)
        {
            OctreeNode yNeighborNode = GetMergedNode(yNeighborIndex);
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
        float3 zNeighborQueryPos = localPos + zNeighborDir * (distToFace.z + 1e-5);

        float3 zNeighborCenter;
        float3 zNeighborHalfExtents;
        int zNeighborIndex = QueryVolumeNode(zNeighborQueryPos, meta, zNeighborCenter, zNeighborHalfExtents);

        if (zNeighborIndex >= 0)
        {
            OctreeNode zNeighborNode = GetMergedNode(zNeighborIndex);
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
// 多 Volume 混合采样 (主要函数)
// ----------------------------------------------------------------------------

// 根据采样模式从指定 Volume 采样
// float3 SampleOctreeVolumeLightingWithMode(float3 worldPos, float3 worldNormal, VolumeMetadata meta)
// {
//     #if defined (OCTREE_SAMPLING_MODE_WITH_BLENDING)
//             return SampleIrradianceCacheWithBlending(worldPos, worldNormal, meta);
//     #elif defined (OCTREE_SAMPLING_MODE_DIRECTION_WEIGHTED)
//             return SampleOctreeVolumeLightingDirectionWeighted(worldPos, worldNormal, meta);
//     #elif defined (OCTREE_SAMPLING_MODE_DIRECTION_WEIGHTED_WITH_BLENDING)
//             return SampleOctreeVolumeLightingDirectionWeightedWithBlending(worldPos, worldNormal, meta);
//     #else
//             return SampleOctreeVolumeLighting(worldPos, worldNormal, meta);
//     #endif 
// }

// 多 Volume 加权混合采样
float3 SampleMultiVolumeLightProbe(float3 worldPos, float3 worldNormal, out float totalWeight, out int volumeCount)
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
            float3 volumeColor = SampleIrradianceCacheWithBlending(worldPos, worldNormal, meta);

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
float3 SampleMultiVolumeLightProbeSimple(float3 worldPos, float3 worldNormal)
{
    float totalWeight;
    int volumeCount;
    return SampleMultiVolumeLightProbe(worldPos, worldNormal, totalWeight, volumeCount);
}

// ----------------------------------------------------------------------------
// 自动模式选择
// ----------------------------------------------------------------------------

// 自动选择单/多 Volume 模式的采样函数
// 根据 _OctreeSamplingMode 自动选择对应的采样方法
float3 SampleIrradianceCacheAuto(float3 worldPos, float3 worldNormal)
{
    if (_UseMultiVolumeMode && _ActiveVolumeCount > 0)
    {
        return SampleMultiVolumeLightProbeSimple(worldPos, worldNormal);
    }
    else
    {
        // 单 Volume 模式：检查是否在 Volume 内
        if (IsInsideVolume(worldPos))
        {
            return SampleIrradianceCacheWithBlending(worldPos, worldNormal);
        }
        
        // 不在 Volume 内，回退到天空球
        if (_EnableSkyboxFallback)
        {
            return EvaluateSkyboxSH(worldNormal);
        }
        
        return float3(0, 0, 0);
    }
}

// 自动模式 - 检查是否在任意 Volume 内
bool IsInsideAnyVolume(float3 worldPos)
{
    if (_UseMultiVolumeMode && _ActiveVolumeCount > 0)
    {
        [loop]
        for (int i = 0; i < _ActiveVolumeCount && i < MAX_VOLUME_COUNT; i++)
        {
            VolumeMetadata meta = GetVolumeMetadata(i);
            if (IsInsideVolume(worldPos, meta))
                return true;
        }
        return false;
    }
    else
    {
        return IsInsideVolume(worldPos);
    }
}





#endif // IRRADIANCE_CACHE_MULTI_INCLUDED
