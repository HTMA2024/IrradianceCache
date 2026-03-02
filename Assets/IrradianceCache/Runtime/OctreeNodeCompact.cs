using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 八叉树节点数据结构 - GPU 压缩版本 (内存优化)
    /// 使用 Half 精度存储球谐系数，减少约 48% 内存占用
    ///
    /// 内存布局:
    /// - mortonCode: 4 bytes
    /// - depth: 4 bytes
    /// - childStartIndex: 4 bytes
    /// - obstacleIndices: 4 bytes
    /// - 8 corners × 24 bytes = 192 bytes
    /// - 总计: 208 bytes (原 400 bytes)
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct OctreeNodeCompact
    {
        /// <summary>
        /// Morton Code (用于空间定位和缓存优化)
        /// </summary>
        public uint mortonCode;

        /// <summary>
        /// 节点深度 (0 = 根节点)
        /// </summary>
        public int depth;

        /// <summary>
        /// 子节点起始索引 (-1 表示叶节点)
        /// </summary>
        public int childStartIndex;

        /// <summary>
        /// 打包的障碍物 OBB 索引（最多 4 个，每个 8 bits）
        /// 每个字节存储一个索引（0-254 有效，0xFF = 无效）
        /// byte0 = index0, byte1 = index1, byte2 = index2, byte3 = index3
        /// </summary>
        public uint obstacleIndices;

        /// <summary>
        /// 8 个角点的球谐系数 (Half 精度)
        /// </summary>
        public SHCoefficientsHalf corner0;
        public SHCoefficientsHalf corner1;
        public SHCoefficientsHalf corner2;
        public SHCoefficientsHalf corner3;
        public SHCoefficientsHalf corner4;
        public SHCoefficientsHalf corner5;
        public SHCoefficientsHalf corner6;
        public SHCoefficientsHalf corner7;

        /// <summary>
        /// 是否为叶节点
        /// </summary>
        public bool IsLeaf => childStartIndex == -1;

        /// <summary>
        /// 从 OctreeNode 转换为 OctreeNodeCompact
        /// </summary>
        public static OctreeNodeCompact FromOctreeNode(OctreeNode node)
        {
            OctreeNodeCompact result = new OctreeNodeCompact();

            result.mortonCode = node.mortonCode;
            result.depth = node.depth;
            result.childStartIndex = node.childStartIndex;
            result.obstacleIndices = node.obstacleIndices;

            result.corner0 = SHCoefficientsHalf.FromSHCoefficients(node.corner0);
            result.corner1 = SHCoefficientsHalf.FromSHCoefficients(node.corner1);
            result.corner2 = SHCoefficientsHalf.FromSHCoefficients(node.corner2);
            result.corner3 = SHCoefficientsHalf.FromSHCoefficients(node.corner3);
            result.corner4 = SHCoefficientsHalf.FromSHCoefficients(node.corner4);
            result.corner5 = SHCoefficientsHalf.FromSHCoefficients(node.corner5);
            result.corner6 = SHCoefficientsHalf.FromSHCoefficients(node.corner6);
            result.corner7 = SHCoefficientsHalf.FromSHCoefficients(node.corner7);

            return result;
        }

        /// <summary>
        /// 转换为 OctreeNode
        /// </summary>
        public OctreeNode ToOctreeNode()
        {
            OctreeNode result = new OctreeNode();

            result.mortonCode = mortonCode;
            result.depth = depth;
            result.childStartIndex = childStartIndex;
            result.obstacleIndices = obstacleIndices;

            result.corner0 = corner0.ToSHCoefficients();
            result.corner1 = corner1.ToSHCoefficients();
            result.corner2 = corner2.ToSHCoefficients();
            result.corner3 = corner3.ToSHCoefficients();
            result.corner4 = corner4.ToSHCoefficients();
            result.corner5 = corner5.ToSHCoefficients();
            result.corner6 = corner6.ToSHCoefficients();
            result.corner7 = corner7.ToSHCoefficients();

            return result;
        }

        /// <summary>
        /// 批量转换 OctreeNode 数组为 OctreeNodeCompact 数组
        /// </summary>
        public static OctreeNodeCompact[] FromOctreeNodeArray(OctreeNode[] nodes)
        {
            if (nodes == null)
                return null;

            OctreeNodeCompact[] result = new OctreeNodeCompact[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                result[i] = FromOctreeNode(nodes[i]);
            }
            return result;
        }

        /// <summary>
        /// 获取指定索引的角点球谐系数
        /// </summary>
        public SHCoefficientsHalf GetCornerSH(int index)
        {
            switch (index)
            {
                case 0: return corner0;
                case 1: return corner1;
                case 2: return corner2;
                case 3: return corner3;
                case 4: return corner4;
                case 5: return corner5;
                case 6: return corner6;
                case 7: return corner7;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), "Corner index must be 0-7");
            }
        }

        /// <summary>
        /// 获取结构体的字节大小 (用于 ComputeBuffer)
        /// </summary>
        public static int GetStride()
        {
            // mortonCode: 4 bytes
            // depth: 4 bytes
            // childStartIndex: 4 bytes
            // obstacleIndices: 4 bytes
            // 8 corners × 24 bytes = 192 bytes
            // Total: 208 bytes
            return sizeof(uint) + sizeof(int) * 2 + sizeof(uint) + SHCoefficientsHalf.GetStride() * 8;
        }

        /// <summary>
        /// 计算节点的世界空间中心位置
        /// </summary>
        public Vector3 GetWorldCenter(Vector3 rootCenter, Vector3 rootHalfExtents)
        {
            MortonCodeHelper.DecodeMorton3D(mortonCode, out uint x, out uint y, out uint z);
            return MortonCodeHelper.GridIndexToWorld(x, y, z, depth, rootCenter, rootHalfExtents);
        }

        /// <summary>
        /// 计算节点的各轴半尺寸
        /// </summary>
        public Vector3 GetHalfExtents(Vector3 rootHalfExtents)
        {
            return rootHalfExtents / (1 << depth);
        }

        /// <summary>
        /// 在节点内进行三线性插值获取球谐系数
        /// </summary>
        /// <param name="localPos">节点内的局部坐标 [-1, 1]^3</param>
        public SHCoefficients TrilinearInterpolate(Vector3 localPos)
        {
            // 将 [-1, 1] 转换到 [0, 1]
            Vector3 t = (localPos + Vector3.one) * 0.5f;
            t.x = Mathf.Clamp01(t.x);
            t.y = Mathf.Clamp01(t.y);
            t.z = Mathf.Clamp01(t.z);

            // 转换为 float 进行插值
            SHCoefficients c0 = corner0.ToSHCoefficients();
            SHCoefficients c1 = corner1.ToSHCoefficients();
            SHCoefficients c2 = corner2.ToSHCoefficients();
            SHCoefficients c3 = corner3.ToSHCoefficients();
            SHCoefficients c4 = corner4.ToSHCoefficients();
            SHCoefficients c5 = corner5.ToSHCoefficients();
            SHCoefficients c6 = corner6.ToSHCoefficients();
            SHCoefficients c7 = corner7.ToSHCoefficients();

            // X 方向插值
            SHCoefficients c00 = SHCoefficients.Lerp(c0, c1, t.x);
            SHCoefficients c01 = SHCoefficients.Lerp(c4, c5, t.x);
            SHCoefficients c10 = SHCoefficients.Lerp(c2, c3, t.x);
            SHCoefficients c11 = SHCoefficients.Lerp(c6, c7, t.x);

            // Y 方向插值
            SHCoefficients cy0 = SHCoefficients.Lerp(c00, c10, t.y);
            SHCoefficients cy1 = SHCoefficients.Lerp(c01, c11, t.y);

            // Z 方向插值
            return SHCoefficients.Lerp(cy0, cy1, t.z);
        }
    }
}
