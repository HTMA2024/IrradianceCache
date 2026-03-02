using System;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 存储 Bake 后的八叉树数据的 ScriptableObject
    /// 八叉树使用广度优先（BFS）方式存储，每个父节点的 8 个子节点连续排列
    /// </summary>
    [CreateAssetMenu(fileName = "IrradianceCacheData", menuName = "Irradiance Cache/Data Asset")]
    public class IrradianceCacheData : ScriptableObject
    {
        [Header("Storage Mode")]
        [Tooltip("数据存储模式")]
        public VolumeStorageMode storageMode = VolumeStorageMode.Octree;

        [Header("Octree Metadata")]
        [Tooltip("八叉树最大深度")]
        public int maxDepth = 4;

        [Tooltip("根节点中心位置")]
        public Vector3 rootCenter;

        [Tooltip("根节点各轴半尺寸")]
        public Vector3 rootHalfExtents;

        [Header("Uniform Grid Settings")]
        [Tooltip("网格分辨率 (仅 UniformGrid 模式)")]
        public Vector3Int gridResolution = new Vector3Int(8, 8, 8);

        [Header("Node Data")]
        [Tooltip("所有八叉树节点数据")]
        public OctreeNode[] nodes;

        [Header("Obstacle Data")]
        [Tooltip("烘焙时的 OBB 障碍物数据（Volume 局部空间）")]
        public ObstacleOBBData[] bakedObstacles;

        [Header("Statistics")]
        [Tooltip("总节点数")]
        public int totalNodeCount;

        [Tooltip("叶节点数")]
        public int leafNodeCount;

        [Tooltip("稀疏节点数 (提前终止的叶节点)")]
        public int sparseNodeCount;

        /// <summary>
        /// 获取根节点的包围盒
        /// </summary>
        public Bounds GetRootBounds()
        {
            return new Bounds(rootCenter, rootHalfExtents * 2f);
        }

        /// <summary>
        /// 检查一个世界坐标是否在 Volume 范围内
        /// </summary>
        public bool ContainsPoint(Vector3 worldPos)
        {
            return GetRootBounds().Contains(worldPos);
        }
        
        /// <summary>
        /// 查询指定世界坐标所在的叶节点索引
        /// </summary>
        /// <param name="worldPos">世界坐标</param>
        /// <returns>叶节点索引，如果不在范围内返回 -1</returns>
        public int QueryLeafNode(Vector3 worldPos)
        {
            if (storageMode == VolumeStorageMode.UniformGrid)
                return QueryGridNode(worldPos);

            if (nodes == null || nodes.Length == 0)
                return -1;

            if (!ContainsPoint(worldPos))
                return -1;

            int currentIndex = 0;
            Vector3 nodeCenter = rootCenter;
            Vector3 nodeHalfExtents = rootHalfExtents;

            for (int depth = 0; depth <= maxDepth; depth++)
            {
                if (currentIndex >= nodes.Length)
                    return -1;

                OctreeNode node = nodes[currentIndex];

                // 如果是叶节点，返回当前索引和边界
                if (node.IsLeaf)
                {
                    return currentIndex;
                }

                // 计算该位置位于哪个子节点
                Vector3 localPos = worldPos - nodeCenter;
                int childIndex = 0;
                if (localPos.x > 0) childIndex |= 1;
                if (localPos.y > 0) childIndex |= 2;
                if (localPos.z > 0) childIndex |= 4;

                // 更新到子节点
                currentIndex = node.childStartIndex + childIndex;
                nodeCenter = nodes[currentIndex].GetWorldCenter(rootCenter, rootHalfExtents);
            }

            // 到达最大深度
            return currentIndex;
        }

        /// <summary>
        /// 采样指定世界坐标的球谐系数 (带三线性插值)
        /// </summary>
        public SHCoefficients SampleSH(Vector3 worldPos)
        {
            if (storageMode == VolumeStorageMode.UniformGrid)
            {
                int nodeIndex = QueryGridNode(worldPos);
                if (nodeIndex < 0)
                    return new SHCoefficients();

                Vector3 cellCenter, cellHalfExtents;
                GetGridNodeBounds(nodeIndex, out cellCenter, out cellHalfExtents);

                Vector3 localPos = new Vector3(
                    (worldPos.x - cellCenter.x) / cellHalfExtents.x,
                    (worldPos.y - cellCenter.y) / cellHalfExtents.y,
                    (worldPos.z - cellCenter.z) / cellHalfExtents.z
                );
                return nodes[nodeIndex].TrilinearInterpolate(localPos);
            }

            int octreeNodeIndex = QueryLeafNode(worldPos);

            if (octreeNodeIndex < 0)
                return new SHCoefficients();

            OctreeNode node = nodes[octreeNodeIndex];

            Vector3 nodeCenter = node.GetWorldCenter(rootCenter, rootHalfExtents);
            Vector3 nodeHalfExtents = node.GetHalfExtents(rootHalfExtents);

            // 计算局部坐标 [-1, 1] (各轴独立归一化)
            Vector3 localPos2 = new Vector3(
                (worldPos.x - nodeCenter.x) / nodeHalfExtents.x,
                (worldPos.y - nodeCenter.y) / nodeHalfExtents.y,
                (worldPos.z - nodeCenter.z) / nodeHalfExtents.z
            );

            // 三线性插值
            return node.TrilinearInterpolate(localPos2);
        }

        /// <summary>
        /// 采样指定世界坐标和法线方向的光照颜色
        /// </summary>
        public Color SampleLighting(Vector3 worldPos, Vector3 normal)
        {
            SHCoefficients sh = SampleSH(worldPos);
            Vector3 color = sh.Evaluate(normal);
            return new Color(color.x, color.y, color.z, 1f);
        }

        /// <summary>
        /// 通过节点索引获取节点的边界（通过遍历父节点链计算）
        /// </summary>
        public void GetNodeBounds(int nodeIndex, out Vector3 nodeCenter, out Vector3 nodeHalfExtents)
        {
            if (storageMode == VolumeStorageMode.UniformGrid)
            {
                GetGridNodeBounds(nodeIndex, out nodeCenter, out nodeHalfExtents);
                return;
            }
            OctreeBuilder.GetNodeBounds(nodeIndex, nodes, rootCenter, rootHalfExtents, out nodeCenter, out nodeHalfExtents);
        }

        /// <summary>
        /// 均匀网格模式：查询世界坐标所在的 cell 索引
        /// O(1) 直接计算，无需树遍历
        /// </summary>
        public int QueryGridNode(Vector3 worldPos)
        {
            if (nodes == null || nodes.Length == 0)
                return -1;

            Vector3 volumeMin = rootCenter - rootHalfExtents;
            Vector3 volumeSize = rootHalfExtents * 2f;

            Vector3 normalizedPos = new Vector3(
                (worldPos.x - volumeMin.x) / volumeSize.x,
                (worldPos.y - volumeMin.y) / volumeSize.y,
                (worldPos.z - volumeMin.z) / volumeSize.z
            );

            if (normalizedPos.x < 0f || normalizedPos.x > 1f ||
                normalizedPos.y < 0f || normalizedPos.y > 1f ||
                normalizedPos.z < 0f || normalizedPos.z > 1f)
                return -1;

            int nx = gridResolution.x;
            int ny = gridResolution.y;
            int nz = gridResolution.z;

            int ix = Mathf.Clamp(Mathf.FloorToInt(normalizedPos.x * nx), 0, nx - 1);
            int iy = Mathf.Clamp(Mathf.FloorToInt(normalizedPos.y * ny), 0, ny - 1);
            int iz = Mathf.Clamp(Mathf.FloorToInt(normalizedPos.z * nz), 0, nz - 1);

            int linearIndex = iz * (nx * ny) + iy * nx + ix;
            return (linearIndex < nodes.Length) ? linearIndex : -1;
        }

        /// <summary>
        /// 均匀网格模式：获取指定 cell 索引的中心和半尺寸
        /// </summary>
        public void GetGridNodeBounds(int linearIndex, out Vector3 cellCenter, out Vector3 cellHalfExtents)
        {
            int nx = gridResolution.x;
            int ny = gridResolution.y;

            int iz = linearIndex / (nx * ny);
            int remainder = linearIndex % (nx * ny);
            int iy = remainder / nx;
            int ix = remainder % nx;

            Vector3 cellSize = new Vector3(
                rootHalfExtents.x * 2f / gridResolution.x,
                rootHalfExtents.y * 2f / gridResolution.y,
                rootHalfExtents.z * 2f / gridResolution.z
            );
            cellHalfExtents = cellSize * 0.5f;

            Vector3 volumeMin = rootCenter - rootHalfExtents;
            cellCenter = volumeMin + new Vector3(
                (ix + 0.5f) * cellSize.x,
                (iy + 0.5f) * cellSize.y,
                (iz + 0.5f) * cellSize.z
            );
        }

        /// <summary>
        /// 计算数据的内存占用 (字节)
        /// </summary>
        public long GetMemoryUsageBytes()
        {
            if (nodes == null)
                return 0;

            return (long)nodes.Length * OctreeNode.GetStride();
        }

        /// <summary>
        /// 计算数据的内存占用 (MB)
        /// </summary>
        public float GetMemoryUsageMB()
        {
            return GetMemoryUsageBytes() / (1024f * 1024f);
        }

        /// <summary>
        /// 计算压缩格式的内存占用 (字节)
        /// </summary>
        public long GetCompactMemoryUsageBytes()
        {
            if (nodes == null)
                return 0;

            return (long)nodes.Length * OctreeNodeCompact.GetStride();
        }

        /// <summary>
        /// 计算压缩格式的内存占用 (MB)
        /// </summary>
        public float GetCompactMemoryUsageMB()
        {
            return GetCompactMemoryUsageBytes() / (1024f * 1024f);
        }

        /// <summary>
        /// 获取内存节省百分比 (使用压缩格式相比原始格式)
        /// </summary>
        public float GetMemorySavingsPercent()
        {
            long original = GetMemoryUsageBytes();
            if (original == 0)
                return 0f;

            long compact = GetCompactMemoryUsageBytes();
            return (1f - (float)compact / original) * 100f;
        }

        /// <summary>
        /// 验证数据完整性
        /// </summary>
        public bool ValidateData(out string errorMessage)
        {
            errorMessage = null;

            if (nodes == null || nodes.Length == 0)
            {
                errorMessage = "No nodes in data";
                return false;
            }

            // 验证子节点索引
            for (int i = 0; i < nodes.Length; i++)
            {
                OctreeNode node = nodes[i];

                if (!node.IsLeaf)
                {
                    if (node.childStartIndex < 0 || node.childStartIndex + 7 >= nodes.Length)
                    {
                        errorMessage = $"Node {i} has invalid child index: {node.childStartIndex}";
                        return false;
                    }

                    // 验证子节点深度
                    for (int c = 0; c < 8; c++)
                    {
                        int childIdx = node.childStartIndex + c;
                        if (nodes[childIdx].depth != node.depth + 1)
                        {
                            errorMessage = $"Node {i} child {c} has incorrect depth";
                            return false;
                        }
                    }
                }

                if (node.depth < 0 || node.depth > maxDepth)
                {
                    errorMessage = $"Node {i} has invalid depth: {node.depth}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 更新统计信息
        /// </summary>
        public void UpdateStatistics()
        {
            if (nodes == null)
            {
                totalNodeCount = 0;
                leafNodeCount = 0;
                sparseNodeCount = 0;
                return;
            }

            totalNodeCount = nodes.Length;
            leafNodeCount = 0;
            sparseNodeCount = 0;

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].IsLeaf)
                {
                    leafNodeCount++;
                    if (nodes[i].depth < maxDepth)
                        sparseNodeCount++;
                }
            }
        }
    }
}
