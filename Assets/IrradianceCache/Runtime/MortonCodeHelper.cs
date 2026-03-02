using System.Runtime.CompilerServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// Morton Code (Z-order curve) 编码/解码工具类
    /// 用于将 3D 坐标映射到 1D，提升 GPU 缓存命中率
    /// </summary>
    public static class MortonCodeHelper
    {
        /// <summary>
        /// 将 3D 坐标编码为 Morton Code
        /// 支持最大 10 位坐标 (0-1023)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint EncodeMorton3D(uint x, uint y, uint z)
        {
            return Part1By2(x) | (Part1By2(y) << 1) | (Part1By2(z) << 2);
        }

        /// <summary>
        /// 从 Morton Code 解码出 3D 坐标
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeMorton3D(uint code, out uint x, out uint y, out uint z)
        {
            x = Compact1By2(code);
            y = Compact1By2(code >> 1);
            z = Compact1By2(code >> 2);
        }

        /// <summary>
        /// 将一个数的位分散开，每两位插入两个 0
        /// 例如: 0b101 -> 0b001000001
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Part1By2(uint n)
        {
            // n = ---- ---- ---- ---- ---- --98 7654 3210
            n = (n ^ (n << 16)) & 0xff0000ff;
            // n = ---- --98 ---- ---- ---- ---- 7654 3210
            n = (n ^ (n << 8)) & 0x0300f00f;
            // n = ---- --98 ---- ---- 7654 ---- ---- 3210
            n = (n ^ (n << 4)) & 0x030c30c3;
            // n = ---- --98 ---- 76-- --54 ---- 32-- --10
            n = (n ^ (n << 2)) & 0x09249249;
            // n = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
            return n;
        }

        /// <summary>
        /// Part1By2 的逆操作，将分散的位压缩回来
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Compact1By2(uint n)
        {
            n &= 0x09249249;
            // n = ---- 9--8 --7- -6-- 5--4 --3- -2-- 1--0
            n = (n ^ (n >> 2)) & 0x030c30c3;
            // n = ---- --98 ---- 76-- --54 ---- 32-- --10
            n = (n ^ (n >> 4)) & 0x0300f00f;
            // n = ---- --98 ---- ---- 7654 ---- ---- 3210
            n = (n ^ (n >> 8)) & 0xff0000ff;
            // n = ---- --98 ---- ---- ---- ---- 7654 3210
            n = (n ^ (n >> 16)) & 0x000003ff;
            // n = ---- ---- ---- ---- ---- --98 7654 3210
            return n;
        }

        /// <summary>
        /// 根据八叉树深度和节点在该深度的位置计算 Morton Code
        /// </summary>
        /// <param name="depth">节点深度 (0 = 根节点)</param>
        /// <param name="x">X 方向索引 (0 到 2^depth - 1)</param>
        /// <param name="y">Y 方向索引</param>
        /// <param name="z">Z 方向索引</param>
        public static uint CalculateMortonCode(int depth, uint x, uint y, uint z)
        {
            // 确保坐标在有效范围内
            uint maxCoord = (uint)(1 << depth) - 1;
            x = System.Math.Min(x, maxCoord);
            y = System.Math.Min(y, maxCoord);
            z = System.Math.Min(z, maxCoord);

            return EncodeMorton3D(x, y, z);
        }

        /// <summary>
        /// 从世界坐标计算在指定深度的网格索引
        /// </summary>
        /// <param name="worldPos">世界坐标</param>
        /// <param name="rootCenter">八叉树根节点中心</param>
        /// <param name="rootHalfExtents">八叉树根节点各轴半尺寸</param>
        /// <param name="depth">目标深度</param>
        /// <param name="x">输出 X 索引</param>
        /// <param name="y">输出 Y 索引</param>
        /// <param name="z">输出 Z 索引</param>
        public static void WorldToGridIndex(
            Vector3 worldPos,
            Vector3 rootCenter,
            Vector3 rootHalfExtents,
            int depth,
            out uint x, out uint y, out uint z)
        {
            // 转换到 [0, 1] 范围的归一化坐标 (各轴独立)
            Vector3 normalizedPos = new Vector3(
                (worldPos.x - rootCenter.x + rootHalfExtents.x) / (rootHalfExtents.x * 2f),
                (worldPos.y - rootCenter.y + rootHalfExtents.y) / (rootHalfExtents.y * 2f),
                (worldPos.z - rootCenter.z + rootHalfExtents.z) / (rootHalfExtents.z * 2f)
            );

            // 限制在 [0, 1] 范围内
            normalizedPos.x = Mathf.Clamp01(normalizedPos.x);
            normalizedPos.y = Mathf.Clamp01(normalizedPos.y);
            normalizedPos.z = Mathf.Clamp01(normalizedPos.z);

            // 计算网格分辨率
            uint resolution = (uint)(1 << depth);

            // 转换到网格索引
            x = (uint)Mathf.Min(normalizedPos.x * resolution, resolution - 1);
            y = (uint)Mathf.Min(normalizedPos.y * resolution, resolution - 1);
            z = (uint)Mathf.Min(normalizedPos.z * resolution, resolution - 1);
        }

        /// <summary>
        /// 从网格索引计算世界坐标 (节点中心)
        /// </summary>
        public static Vector3 GridIndexToWorld(
            uint x, uint y, uint z,
            int depth,
            Vector3 rootCenter,
            Vector3 rootHalfExtents)
        {
            uint resolution = (uint)(1 << depth);
            Vector3 cellSize = (rootHalfExtents * 2f) / resolution;

            Vector3 minCorner = rootCenter - rootHalfExtents;

            return new Vector3(
                minCorner.x + (x + 0.5f) * cellSize.x,
                minCorner.y + (y + 0.5f) * cellSize.y,
                minCorner.z + (z + 0.5f) * cellSize.z
            );
        }

        /// <summary>
        /// 计算子节点的 Morton Code
        /// </summary>
        /// <param name="parentMortonCode">父节点的 Morton Code</param>
        /// <param name="childIndex">子节点索引 (0-7)</param>
        /// <param name="parentDepth">父节点深度</param>
        public static uint GetChildMortonCode(uint parentMortonCode, int childIndex, int parentDepth)
        {
            // 解码父节点坐标
            DecodeMorton3D(parentMortonCode, out uint px, out uint py, out uint pz);

            // 子节点坐标 = 父节点坐标 * 2 + 偏移
            uint cx = px * 2 + (uint)(childIndex & 1);
            uint cy = py * 2 + (uint)((childIndex >> 1) & 1);
            uint cz = pz * 2 + (uint)((childIndex >> 2) & 1);

            return EncodeMorton3D(cx, cy, cz);
        }
    }
}
