#ifndef LIGHT_PROBE_TYPES_INCLUDED
#define LIGHT_PROBE_TYPES_INCLUDED

// ============================================================================
// LightProbeTypes.hlsl
// 共享的数据类型、Buffer 访问和 SH 工具函数
// 被 IrradianceCache.hlsl 和 UniformGridLightProbe.hlsl 共同引用
// ============================================================================

// ----------------------------------------------------------------------------
// 数据结构定义
// ----------------------------------------------------------------------------

// SH 系数结构 (L1: 4 系数 × 3 通道) - Half 精度版本
struct SHCoefficients
{
    half4 shR; // R 通道的 4 个系数 (L0, L1x, L1y, L1z)
    half4 shG; // G 通道的 4 个系数
    half4 shB; // B 通道的 4 个系数
};

// SH 系数结构 - Half 精度版本 (内存优化)
// 使用 uint 打包两个 half 值
struct SHCoefficientsHalf
{
    uint shR01; // R: L0, L1x (两个 half 打包)
    uint shR23; // R: L1y, L1z
    uint shG01; // G: L0, L1x
    uint shG23; // G: L1y, L1z
    uint shB01; // B: L0, L1x
    uint shB23; // B: L1y, L1z
};

// 八叉树节点结构 - Full 精度版本
struct OctreeNode
{
    uint mortonCode;        // Morton Code
    int depth;              // 节点深度
    int childStartIndex;    // 子节点起始索引 (-1 = 叶节点)
    uint obstacleIndices;   // 打包 4 个 OBB 障碍物索引 (每个 8 bits, 0xFF = 无效)

    // 8 个角点的 SH 系数
    SHCoefficients corner0;
    SHCoefficients corner1;
    SHCoefficients corner2;
    SHCoefficients corner3;
    SHCoefficients corner4;
    SHCoefficients corner5;
    SHCoefficients corner6;
    SHCoefficients corner7;
};

// 八叉树节点结构 - Compact 精度版本 (内存优化)
struct OctreeNodeCompact
{
    uint mortonCode;
    int depth;
    int childStartIndex;
    uint obstacleIndices;

    SHCoefficientsHalf corner0;
    SHCoefficientsHalf corner1;
    SHCoefficientsHalf corner2;
    SHCoefficientsHalf corner3;
    SHCoefficientsHalf corner4;
    SHCoefficientsHalf corner5;
    SHCoefficientsHalf corner6;
    SHCoefficientsHalf corner7;
};

// ----------------------------------------------------------------------------
// 共享全局变量 (由 C# 设置)
// ----------------------------------------------------------------------------

// Compact 精度 buffer (内存优化)
StructuredBuffer<OctreeNodeCompact> _OctreeNodesCompact;

float3 _OctreeRootCenter;
float3 _OctreeRootHalfExtents;
int _OctreeNodeCount;

// Transform 矩阵
float4x4 _OctreeWorldToLocal;  // World 到 Volume 局部空间的变换矩阵
float4x4 _OctreeRotationMatrix; // 旋转矩阵的逆（用于变换法线）

// ----------------------------------------------------------------------------
// Transform 工具函数
// ----------------------------------------------------------------------------

// 将世界坐标变换到 Volume 局部空间
float3 WorldToVolumeLocal(float3 worldPos)
{
    return mul(_OctreeWorldToLocal, float4(worldPos, 1.0)).xyz;
}

// 将世界法线变换到 Volume 局部空间
float3 WorldNormalToVolumeLocal(float3 worldNormal)
{
    return normalize(mul((float3x3)_OctreeRotationMatrix, worldNormal));
}

// ----------------------------------------------------------------------------
// Half 精度转换工具函数
// ----------------------------------------------------------------------------

// 从打包的 uint 解包两个 half 值
float2 UnpackHalf2(uint packed)
{
    return float2(
        f16tof32(packed & 0xFFFF),
        f16tof32(packed >> 16)
    );
}

// 从 SHCoefficientsHalf 解包为 SHCoefficients
SHCoefficients UnpackSHCoefficients(SHCoefficientsHalf shHalf)
{
    SHCoefficients result;

    half2 r01 = UnpackHalf2(shHalf.shR01);
    half2 r23 = UnpackHalf2(shHalf.shR23);
    result.shR = half4(r01.x, r01.y, r23.x, r23.y);

    half2 g01 = UnpackHalf2(shHalf.shG01);
    half2 g23 = UnpackHalf2(shHalf.shG23);
    result.shG = half4(g01.x, g01.y, g23.x, g23.y);

    half2 b01 = UnpackHalf2(shHalf.shB01);
    half2 b23 = UnpackHalf2(shHalf.shB23);
    result.shB = half4(b01.x, b01.y, b23.x, b23.y);

    return result;
}

// 从 OctreeNodeCompact 获取完整的 OctreeNode 数据
OctreeNode UnpackOctreeNode(OctreeNodeCompact compact)
{
    OctreeNode result;
    result.mortonCode = compact.mortonCode;
    result.depth = compact.depth;
    result.childStartIndex = compact.childStartIndex;
    result.obstacleIndices = compact.obstacleIndices;

    result.corner0 = UnpackSHCoefficients(compact.corner0);
    result.corner1 = UnpackSHCoefficients(compact.corner1);
    result.corner2 = UnpackSHCoefficients(compact.corner2);
    result.corner3 = UnpackSHCoefficients(compact.corner3);
    result.corner4 = UnpackSHCoefficients(compact.corner4);
    result.corner5 = UnpackSHCoefficients(compact.corner5);
    result.corner6 = UnpackSHCoefficients(compact.corner6);
    result.corner7 = UnpackSHCoefficients(compact.corner7);

    return result;
}

// 创建空节点（用于越界访问时返回安全值）
OctreeNode CreateEmptyNode()
{
    OctreeNode emptyNode;
    emptyNode.mortonCode = 0;
    emptyNode.depth = 0;
    emptyNode.childStartIndex = -1;
    emptyNode.obstacleIndices = 0xFFFFFFFF;

    SHCoefficients emptySH;
    emptySH.shR = half4(0, 0, 0, 0);
    emptySH.shG = half4(0, 0, 0, 0);
    emptySH.shB = half4(0, 0, 0, 0);

    emptyNode.corner0 = emptySH;
    emptyNode.corner1 = emptySH;
    emptyNode.corner2 = emptySH;
    emptyNode.corner3 = emptySH;
    emptyNode.corner4 = emptySH;
    emptyNode.corner5 = emptySH;
    emptyNode.corner6 = emptySH;
    emptyNode.corner7 = emptySH;

    return emptyNode;
}

// 获取节点 (自动选择格式，带边界检查)
OctreeNode GetOctreeNode(int index)
{
    // 边界检查：防止显存越界访问导致DX11崩溃
    if (index < 0 || index >= _OctreeNodeCount)
    {
        return CreateEmptyNode();
    }

    return UnpackOctreeNode(_OctreeNodesCompact[index]);
}

// ----------------------------------------------------------------------------
// SH 系数插值
// ----------------------------------------------------------------------------

// 获取节点的角点 SH 系数
SHCoefficients GetCornerSH(OctreeNode node, int cornerIndex)
{
    switch (cornerIndex)
    {
        case 0: return node.corner0;
        case 1: return node.corner1;
        case 2: return node.corner2;
        case 3: return node.corner3;
        case 4: return node.corner4;
        case 5: return node.corner5;
        case 6: return node.corner6;
        case 7: return node.corner7;
        default: return node.corner0;
    }
}

// SH 系数线性插值
SHCoefficients LerpSH(SHCoefficients a, SHCoefficients b, float t)
{
    SHCoefficients result;
    result.shR = lerp(a.shR, b.shR, t);
    result.shG = lerp(a.shG, b.shG, t);
    result.shB = lerp(a.shB, b.shB, t);
    return result;
}

// 三线性插值 SH 系数
SHCoefficients TrilinearInterpolateSH(OctreeNode node, float3 localPos)
{
    // localPos: [-1, 1]^3 范围
    // 转换到 [0, 1]
    float3 t = saturate((localPos + 1.0) * 0.5);

    // X 方向插值
    SHCoefficients c00 = LerpSH(node.corner0, node.corner1, t.x);
    SHCoefficients c01 = LerpSH(node.corner4, node.corner5, t.x);
    SHCoefficients c10 = LerpSH(node.corner2, node.corner3, t.x);
    SHCoefficients c11 = LerpSH(node.corner6, node.corner7, t.x);

    // Y 方向插值
    SHCoefficients c0 = LerpSH(c00, c10, t.y);
    SHCoefficients c1 = LerpSH(c01, c11, t.y);

    // Z 方向插值
    return LerpSH(c0, c1, t.z);
}

// ----------------------------------------------------------------------------
// L1 球谐评估
// ----------------------------------------------------------------------------

// L1 球谐基函数常数 (与 Unity SH 兼容)
#define SH_C0 0.886227f    // L0 系数
#define SH_C1 1.023326f    // L1 系数

// Deringing 参数 (由 C# 设置, 0 = 无 deringing, 1 = 最大 deringing)
float _SHDeringingStrength;

// 对 SH 系数应用 Deringing (窗口化)
// 使用 Hanning 窗口衰减 L1 band，保留 L0 (DC) 不变
// 这可以有效减少 L1 SH 的振铃伪影 (ringing artifacts)
SHCoefficients ApplySHDeringing(SHCoefficients sh, float strength)
{
    // Hanning 窗口: w(l) = 0.5 * (1 + cos(pi * l / N))
    // 对于 L1 (l=1, N=2): w(1) = 0.5 * (1 + cos(pi/2)) = 0.5
    // strength 控制从无衰减 (0) 到完全 Hanning 窗口 (1) 的插值
    float l1Window = lerp(1.0, 0.5, strength);

    SHCoefficients result;
    // L0 (x 分量) 保持不变, L1 (yzw 分量) 应用窗口
    result.shR = half4(sh.shR.x, sh.shR.yzw * l1Window);
    result.shG = half4(sh.shG.x, sh.shG.yzw * l1Window);
    result.shB = half4(sh.shB.x, sh.shB.yzw * l1Window);
    return result;
}

// 使用法线方向评估 L1 球谐光照
float3 EvaluateSHL1(SHCoefficients sh, float3 normal)
{
    // 应用 Deringing
    SHCoefficients deringedSH = ApplySHDeringing(sh, _SHDeringingStrength);

    // L1 基函数
    float4 basis = float4(
        SH_C0,
        SH_C1 * normal.x,
        SH_C1 * normal.y,
        SH_C1 * normal.z
    );

    float3 result;
    result.r = dot(deringedSH.shR, basis);
    result.g = dot(deringedSH.shG, basis);
    result.b = dot(deringedSH.shB, basis);

    return max(0, result);
}

// 评估 L1 球谐光照 (带 ambient 项)
float3 EvaluateSHL1WithAmbient(SHCoefficients sh, float3 normal, float3 ambient)
{
    float3 shColor = EvaluateSHL1(sh, normal);
    return shColor + ambient;
}

// ----------------------------------------------------------------------------
// 方向权重工具函数
// ----------------------------------------------------------------------------

// 方向权重计算
float GetDirectionWeight(float3 probePos, float3 shadingPos, float3 normal)
{
    // 计算从着色点指向probe的方向
    float3 probeDir = probePos - shadingPos;
    float dist = length(probeDir);

    // 防止距离过近导致方向不稳定
    if (dist < 0.0001)
        return 1.0;

    probeDir /= dist;

    // 计算方向与法线的点积
    float NdotD = dot(probeDir, normal);

    // 将 [-1, 1] 映射到 [0, 1]
    float backfaceWeight = max(0.0001, (NdotD + 1.0) * 0.5);

    // 平方增强衰减效果
    return backfaceWeight * backfaceWeight;
}

// 计算角点在节点内的偏移
float3 GetCornerOffset(int cornerIndex, float3 halfExtents)
{
    return float3(
        (cornerIndex & 1) ? halfExtents.x : -halfExtents.x,
        (cornerIndex & 2) ? halfExtents.y : -halfExtents.y,
        (cornerIndex & 4) ? halfExtents.z : -halfExtents.z
    );
}

// ----------------------------------------------------------------------------
// 采样模式常量
// ----------------------------------------------------------------------------

#define OCTREE_SAMPLING_MODE_BASIC                           0
#define OCTREE_SAMPLING_MODE_WITH_BLENDING                   1
#define OCTREE_SAMPLING_MODE_DIRECTION_WEIGHTED               2
#define OCTREE_SAMPLING_MODE_DIRECTION_WEIGHTED_WITH_BLENDING 3

#endif // LIGHT_PROBE_TYPES_INCLUDED
