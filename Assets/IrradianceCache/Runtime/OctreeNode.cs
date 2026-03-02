using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 八叉树节点数据结构
    /// 存储 Morton Code、8 个角点的球谐系数和子节点索引
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct OctreeNode
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
        /// 如果不是 -1，则子节点索引为 childStartIndex + 0 到 childStartIndex + 7
        /// </summary>
        public int childStartIndex;

        /// <summary>
        /// 打包的障碍物 OBB 索引（最多 4 个，每个 8 bits）
        /// 每个字节存储一个索引（0-254 有效，0xFF = 无效）
        /// byte0 = index0, byte1 = index1, byte2 = index2, byte3 = index3
        /// </summary>
        public uint obstacleIndices;

        /// <summary>
        /// 8 个角点的球谐系数
        /// 角点顺序:
        /// 0: (-, -, -)  1: (+, -, -)
        /// 2: (-, +, -)  3: (+, +, -)
        /// 4: (-, -, +)  5: (+, -, +)
        /// 6: (-, +, +)  7: (+, +, +)
        /// </summary>
        public SHCoefficients corner0;
        public SHCoefficients corner1;
        public SHCoefficients corner2;
        public SHCoefficients corner3;
        public SHCoefficients corner4;
        public SHCoefficients corner5;
        public SHCoefficients corner6;
        public SHCoefficients corner7;

        /// <summary>
        /// 是否为叶节点
        /// </summary>
        public bool IsLeaf => childStartIndex == -1;

        /// <summary>
        /// 获取指定索引的角点球谐系数
        /// </summary>
        public SHCoefficients GetCornerSH(int index)
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
        /// 设置指定索引的角点球谐系数
        /// </summary>
        public void SetCornerSH(int index, SHCoefficients sh)
        {
            switch (index)
            {
                case 0: corner0 = sh; break;
                case 1: corner1 = sh; break;
                case 2: corner2 = sh; break;
                case 3: corner3 = sh; break;
                case 4: corner4 = sh; break;
                case 5: corner5 = sh; break;
                case 6: corner6 = sh; break;
                case 7: corner7 = sh; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), "Corner index must be 0-7");
            }
        }

        /// <summary>
        /// 创建一个新的叶节点
        /// </summary>
        public static OctreeNode CreateLeaf(uint mortonCode, int depth)
        {
            return new OctreeNode
            {
                mortonCode = mortonCode,
                depth = depth,
                childStartIndex = -1,
                obstacleIndices = 0xFFFFFFFF
            };
        }

        /// <summary>
        /// 创建一个新的内部节点 (有子节点)
        /// </summary>
        public static OctreeNode CreateInternal(uint mortonCode, int depth, int childStartIndex)
        {
            return new OctreeNode
            {
                mortonCode = mortonCode,
                depth = depth,
                childStartIndex = childStartIndex,
                obstacleIndices = 0xFFFFFFFF
            };
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
            // 8 corners × 48 bytes = 384 bytes
            // Total: 400 bytes
            return sizeof(uint) + sizeof(int) * 2 + sizeof(uint) + SHCoefficients.GetStride() * 8;
        }

        /// <summary>
        /// 将最多 4 个 OBB 索引打包为单个 uint
        /// 每个索引占 8 bits（0-254 有效，255 = 无效）
        /// 未使用的槽位填充 0xFF
        /// </summary>
        public static uint PackObstacleIndices(int[] indices)
        {
            uint packed = 0xFFFFFFFF;
            if (indices == null) return packed;

            int count = Math.Min(indices.Length, 4);
            for (int i = 0; i < count; i++)
            {
                int idx = indices[i];
                byte b = (idx >= 0 && idx <= 254) ? (byte)idx : (byte)0xFF;
                // Clear the target byte and set the new value
                packed &= ~(0xFFu << (i * 8));
                packed |= (uint)b << (i * 8);
            }

            return packed;
        }

        /// <summary>
        /// 从打包的 uint 中解包 4 个 OBB 索引
        /// 返回的数组长度始终为 4，无效索引为 255
        /// </summary>
        public static void UnpackObstacleIndices(uint packed, out int[] indices)
        {
            indices = new int[4];
            indices[0] = (int)(packed & 0xFF);
            indices[1] = (int)((packed >> 8) & 0xFF);
            indices[2] = (int)((packed >> 16) & 0xFF);
            indices[3] = (int)((packed >> 24) & 0xFF);
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
        /// 获取节点的 8 个角点世界坐标
        /// </summary>
        public Vector3[] GetCornerPositions(Vector3 rootCenter, Vector3 rootHalfExtents)
        {
            Vector3 center = GetWorldCenter(rootCenter, rootHalfExtents);
            Vector3 halfExtents = GetHalfExtents(rootHalfExtents);

            return new Vector3[8]
            {
                center + new Vector3(-halfExtents.x, -halfExtents.y, -halfExtents.z),  // 0: ---
                center + new Vector3(+halfExtents.x, -halfExtents.y, -halfExtents.z),  // 1: +--
                center + new Vector3(-halfExtents.x, +halfExtents.y, -halfExtents.z),  // 2: -+-
                center + new Vector3(+halfExtents.x, +halfExtents.y, -halfExtents.z),  // 3: ++-
                center + new Vector3(-halfExtents.x, -halfExtents.y, +halfExtents.z),  // 4: --+
                center + new Vector3(+halfExtents.x, -halfExtents.y, +halfExtents.z),  // 5: +-+
                center + new Vector3(-halfExtents.x, +halfExtents.y, +halfExtents.z),  // 6: -++
                center + new Vector3(+halfExtents.x, +halfExtents.y, +halfExtents.z),  // 7: +++
            };
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

            // X 方向插值
            SHCoefficients c00 = SHCoefficients.Lerp(corner0, corner1, t.x);
            SHCoefficients c01 = SHCoefficients.Lerp(corner4, corner5, t.x);
            SHCoefficients c10 = SHCoefficients.Lerp(corner2, corner3, t.x);
            SHCoefficients c11 = SHCoefficients.Lerp(corner6, corner7, t.x);

            // Y 方向插值
            SHCoefficients c0 = SHCoefficients.Lerp(c00, c10, t.y);
            SHCoefficients c1 = SHCoefficients.Lerp(c01, c11, t.y);

            // Z 方向插值
            return SHCoefficients.Lerp(c0, c1, t.z);
        }
    }
}
