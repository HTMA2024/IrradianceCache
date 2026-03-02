using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 角点采样信息
    /// </summary>
    public struct CornerSampleInfo
    {
        public Vector3 worldPosition; // 世界坐标
        public SHCoefficients shCoeff; // 球谐系数
        public bool isValid; // 是否有效（不在物体内部）
        public int nodeIndex; // 所属节点索引
        public int cornerIndex; // 角点索引 (0-7)
    }
    /// <summary>
    /// 八叉树构建器
    /// 负责构建八叉树结构、采样 Light Probe 数据、计算角点球谐系数
    /// 使用广度优先（BFS）方式构建，确保每个父节点的 8 个子节点连续存储
    /// </summary>
    public static class OctreeBuilder
    {
        /// <summary>
        /// 用于 BFS 构建的临时节点信息
        /// </summary>
        private struct BuildNodeInfo
        {
            public Vector3 center;
            public Vector3 halfExtents;
            public int depth;
            public int nodeIndex; // 在最终数组中的索引
        }

        /// <summary>
        /// 构建八叉树并计算所有节点的球谐系数
        /// 使用广度优先（BFS）方式构建，确保子节点连续存储
        /// </summary>
        /// <param name="rootCenter">根节点中心</param>
        /// <param name="rootHalfExtents">根节点各轴半尺寸</param>
        /// <param name="maxDepth">最大深度</param>
        /// <param name="lightProbePositions">场景中 Light Probe 的位置数组</param>
        /// <returns>构建好的节点数组</returns>
        public static OctreeNode[] BuildOctree(
            Vector3 rootCenter,
            Vector3 rootHalfExtents,
            int maxDepth,
            Vector3[] lightProbePositions)
        {
            List<OctreeNode> nodes = new List<OctreeNode>();

            // BFS 队列：存储待处理的节点信息
            Queue<BuildNodeInfo> buildQueue = new Queue<BuildNodeInfo>();

            // 创建根节点
            OctreeNode rootNode = OctreeNode.CreateLeaf(0, 0);
            nodes.Add(rootNode);

            // 将根节点加入队列
            buildQueue.Enqueue(new BuildNodeInfo
            {
                center = rootCenter,
                halfExtents = rootHalfExtents,
                depth = 0,
                nodeIndex = 0
            });

            // BFS 遍历构建
            while(buildQueue.Count > 0)
            {
                BuildNodeInfo current = buildQueue.Dequeue();

                // 检查是否需要细分
                bool shouldSubdivide = current.depth < maxDepth &&
                                       ShouldSubdivide(current.center, current.halfExtents, lightProbePositions);

                if (shouldSubdivide)
                {
                    // 记录子节点起始索引（当前数组末尾）
                    int childStartIndex = nodes.Count;

                    // 更新当前节点为内部节点
                    OctreeNode node = nodes[current.nodeIndex];
                    node.childStartIndex = childStartIndex;
                    nodes[current.nodeIndex] = node;

                    Vector3 childHalfExtents = current.halfExtents * 0.5f;
                    int childDepth = current.depth + 1;

                    // 创建 8 个子节点（连续添加）
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 childCenter = current.center +
                                              new Vector3(
                                                  (i & 1) != 0 ? childHalfExtents.x : -childHalfExtents.x,
                                                  (i & 2) != 0 ? childHalfExtents.y : -childHalfExtents.y,
                                                  (i & 4) != 0 ? childHalfExtents.z : -childHalfExtents.z
                                              );

                        // 计算子节点的 Morton Code
                        uint childMortonCode = MortonCodeHelper.GetChildMortonCode(
                            nodes[current.nodeIndex].mortonCode, i, current.depth);

                        // 创建子节点（初始为叶节点）
                        OctreeNode childNode = OctreeNode.CreateLeaf(childMortonCode, childDepth);
                        int childNodeIndex = nodes.Count;
                        nodes.Add(childNode);

                        // 将子节点加入队列以便后续处理
                        buildQueue.Enqueue(new BuildNodeInfo
                        {
                            center = childCenter,
                            halfExtents = childHalfExtents,
                            depth = childDepth,
                            nodeIndex = childNodeIndex
                        });
                    }
                }
            }

            return nodes.ToArray();
        }

        /// <summary>
        /// 判断是否需要细分节点
        /// 如果节点区域内包含 Light Probe，则需要细分
        /// </summary>
        private static bool ShouldSubdivide(Vector3 center, Vector3 halfExtents, Vector3[] lightProbePositions)
        {
            if (lightProbePositions == null || lightProbePositions.Length == 0)
                return false;

            return true; // 强制细分所有分支
            Bounds bounds = new Bounds(center, halfExtents * 2f);

            foreach (Vector3 probePos in lightProbePositions)
            {
                if (bounds.Contains(probePos))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 为所有节点计算角点球谐系数（带无效角点修复）
        /// </summary>
        /// <param name="nodes">节点数组</param>
        /// <param name="rootCenter">根节点中心</param>
        /// <param name="rootHalfExtents">根节点各轴半尺寸</param>
        /// <param name="config">无效角点修复配置</param>
        public static void ComputeAllCornerSH(
            OctreeNode[] nodes,
            Vector3 rootCenter,
            Vector3 rootHalfExtents)
        {
            // 第一阶段：采样所有角点
            CornerSampleInfo[] allCorners = SampleAllCorners(nodes, rootCenter, rootHalfExtents);
            ApplyCornerSHToNodes(nodes, allCorners);
        }

        /// <summary>
        /// 采样所有节点的所有角点
        /// </summary>
        public static CornerSampleInfo[] SampleAllCorners(
            OctreeNode[] nodes,
            Vector3 rootCenter,
            Vector3 rootHalfExtents)
        {
            int totalCorners = nodes.Length * 8;
            CornerSampleInfo[] corners = new CornerSampleInfo[totalCorners];

            for (int nodeIdx = 0; nodeIdx < nodes.Length; nodeIdx++)
            {
                Vector3 nodeCenter;
                Vector3 nodeHalfExtents;
                GetNodeBounds(nodeIdx, nodes, rootCenter, rootHalfExtents, out nodeCenter, out nodeHalfExtents);

                for (int cornerIdx = 0; cornerIdx < 8; cornerIdx++)
                {
                    Vector3 cornerOffset = new Vector3(
                        (cornerIdx & 1) != 0 ? nodeHalfExtents.x : -nodeHalfExtents.x,
                        (cornerIdx & 2) != 0 ? nodeHalfExtents.y : -nodeHalfExtents.y,
                        (cornerIdx & 4) != 0 ? nodeHalfExtents.z : -nodeHalfExtents.z
                    );
                    Vector3 worldPos = nodeCenter + cornerOffset;

                    // 采样 SH
                    SphericalHarmonicsL2 sh;
                    LightProbes.GetInterpolatedProbe(worldPos, null, out sh);
                    SHCoefficients shCoeff = SHCoefficients.FromSphericalHarmonicsL2(sh);

                    int globalIdx = nodeIdx * 8 + cornerIdx;
                    corners[globalIdx] = new CornerSampleInfo
                    {
                        worldPosition = worldPos,
                        shCoeff = shCoeff,
                        isValid = true, // 稍后判定
                        nodeIndex = nodeIdx,
                        cornerIndex = cornerIdx
                    };
                }
            }

            return corners;
        }
        /// <summary>
        /// 将角点 SH 数据应用到节点
        /// </summary>
        private static void ApplyCornerSHToNodes(
            OctreeNode[] nodes,
            CornerSampleInfo[] corners)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                CornerSampleInfo corner = corners[i];
                nodes[corner.nodeIndex].SetCornerSH(corner.cornerIndex, corner.shCoeff);
            }
        }

        /// <summary>
        /// 为单个节点计算角点球谐系数
        /// 使用 BFS 遍历方式计算正确的节点中心位置
        /// </summary>
        private static void ComputeNodeCornerSH(
            ref OctreeNode node,
            int nodeIndex,
            OctreeNode[] allNodes,
            Vector3 rootCenter,
            Vector3 rootHalfExtents)
        {
            // 计算节点的实际中心和半尺寸
            Vector3 nodeCenter;
            Vector3 nodeHalfExtents;
            GetNodeBounds(nodeIndex, allNodes, rootCenter, rootHalfExtents, out nodeCenter, out nodeHalfExtents);

            // 计算 8 个角点位置并采样
            for (int i = 0; i < 8; i++)
            {
                Vector3 cornerOffset = new Vector3(
                    (i & 1) != 0 ? nodeHalfExtents.x : -nodeHalfExtents.x,
                    (i & 2) != 0 ? nodeHalfExtents.y : -nodeHalfExtents.y,
                    (i & 4) != 0 ? nodeHalfExtents.z : -nodeHalfExtents.z
                );
                Vector3 worldPos = nodeCenter + cornerOffset;

                // 使用 Unity API 获取插值后的球谐系数
                SphericalHarmonicsL2 sh;
                LightProbes.GetInterpolatedProbe(worldPos, null, out sh);

                // 转换为 L1 系数
                SHCoefficients shCoeff = SHCoefficients.FromSphericalHarmonicsL2(sh);
                node.SetCornerSH(i, shCoeff);
            }
        }

        /// <summary>
        /// 通过遍历父节点链来计算节点的实际边界
        /// </summary>
        public static void GetNodeBounds(
            int nodeIndex,
            OctreeNode[] nodes,
            Vector3 rootCenter,
            Vector3 rootHalfExtents,
            out Vector3 nodeCenter,
            out Vector3 nodeHalfExtents)
        {
            if (nodeIndex == 0)
            {
                nodeCenter = rootCenter;
                nodeHalfExtents = rootHalfExtents;
                return;
            }

            // 构建从根到目标节点的路径
            List<int> pathChildIndices = new List<int>();
            int currentIndex = nodeIndex;

            // 向上查找父节点，记录每一步是第几个子节点
            while(currentIndex > 0)
            {
                // 找到父节点
                int parentIndex = FindParentIndex(currentIndex, nodes);
                if (parentIndex < 0)
                {
                    // 找不到父节点，使用默认值
                    nodeCenter = rootCenter;
                    nodeHalfExtents = rootHalfExtents;
                    return;
                }

                // 计算当前节点是父节点的第几个子节点
                int childOffset = currentIndex - nodes[parentIndex].childStartIndex;
                pathChildIndices.Add(childOffset);
                currentIndex = parentIndex;
            }

            // 从根节点开始，沿路径向下计算
            nodeCenter = rootCenter;
            nodeHalfExtents = rootHalfExtents;

            // 反向遍历路径（从根到叶）
            for (int i = pathChildIndices.Count - 1; i >= 0; i--)
            {
                int childIndex = pathChildIndices[i];
                nodeHalfExtents *= 0.5f;

                Vector3 childOffset = new Vector3(
                    (childIndex & 1) != 0 ? nodeHalfExtents.x : -nodeHalfExtents.x,
                    (childIndex & 2) != 0 ? nodeHalfExtents.y : -nodeHalfExtents.y,
                    (childIndex & 4) != 0 ? nodeHalfExtents.z : -nodeHalfExtents.z
                );
                nodeCenter += childOffset;
            }
        }

        /// <summary>
        /// 查找节点的父节点索引
        /// </summary>
        private static int FindParentIndex(int nodeIndex, OctreeNode[] nodes)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodes[i].IsLeaf)
                {
                    int childStart = nodes[i].childStartIndex;
                    if (nodeIndex >= childStart && nodeIndex < childStart + 8)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// 获取场景中所有 Light Probe 的位置
        /// </summary>
        public static Vector3[] GetLightProbePositions(List<LightProbeGroup> lightProbeGroupList)
        {
            List<Vector3> lightProbePositions = new List<Vector3>();
            foreach (var lightProbeGroup in lightProbeGroupList)
            {
                var probePosArray = lightProbeGroup.probePositions.AsSpan();
                lightProbeGroup.transform.TransformPoints(probePosArray);
                lightProbePositions.AddRange(probePosArray.ToArray());
            }
            
            Vector3[] positions = lightProbePositions.ToArray();
            if (lightProbeGroupList.Count == 0)
            {
                return new Vector3[0];
            }
            return positions;
        }

        /// <summary>
        /// 计算构建统计信息
        /// </summary>
        public static void CalculateStatistics(
            OctreeNode[] nodes,
            int maxDepth,
            out int totalNodes,
            out int leafNodes,
            out int sparseNodes,
            out Dictionary<int, int> nodesPerDepth)
        {
            totalNodes = nodes.Length;
            leafNodes = 0;
            sparseNodes = 0;
            nodesPerDepth = new Dictionary<int, int>();

            for (int i = 0; i < nodes.Length; i++)
            {
                OctreeNode node = nodes[i];

                // 统计每层节点数
                if (!nodesPerDepth.ContainsKey(node.depth))
                    nodesPerDepth[node.depth] = 0;
                nodesPerDepth[node.depth]++;

                // 统计叶节点
                if (node.IsLeaf)
                {
                    leafNodes++;

                    // 稀疏节点：深度未达到最大值的叶节点
                    if (node.depth < maxDepth)
                        sparseNodes++;
                }
            }
        }

        /// <summary>
        /// 角点到节点的映射信息
        /// </summary>
        public struct CornerMapping
        {
            public int nodeIndex;
            public int cornerIndex;
        }

        /// <summary>
        /// 收集所有节点的唯一角点位置
        /// </summary>
        /// <param name="nodes">节点数组</param>
        /// <param name="rootCenter">根节点中心</param>
        /// <param name="rootHalfExtents">根节点各轴半尺寸</param>
        /// <param name="cornerPositions">输出：唯一角点位置数组</param>
        /// <param name="cornerMappings">输出：每个角点对应的节点和角点索引列表</param>
        public static void CollectUniqueCornerPositions(
            OctreeNode[] nodes,
            Vector3 rootCenter,
            Vector3 rootHalfExtents,
            out Vector3[] cornerPositions,
            out List<CornerMapping>[] cornerMappings)
        {
            // 使用字典来去重，key是位置的哈希，value是角点索引
            Dictionary<Vector3Int, int> positionToIndex = new Dictionary<Vector3Int, int>();
            List<Vector3> uniquePositions = new List<Vector3>();
            List<List<CornerMapping>> mappingsList = new List<List<CornerMapping>>();

            // 精度：使用毫米级精度进行去重
            const float precision = 1000f;

            for (int nodeIdx = 0; nodeIdx < nodes.Length; nodeIdx++)
            {
                Vector3 nodeCenter;
                Vector3 nodeHalfExtents;
                GetNodeBounds(nodeIdx, nodes, rootCenter, rootHalfExtents, out nodeCenter, out nodeHalfExtents);

                // 遍历8个角点
                for (int cornerIdx = 0; cornerIdx < 8; cornerIdx++)
                {
                    Vector3 cornerOffset = new Vector3(
                        (cornerIdx & 1) != 0 ? nodeHalfExtents.x : -nodeHalfExtents.x,
                        (cornerIdx & 2) != 0 ? nodeHalfExtents.y : -nodeHalfExtents.y,
                        (cornerIdx & 4) != 0 ? nodeHalfExtents.z : -nodeHalfExtents.z
                    );
                    Vector3 worldPos = nodeCenter + cornerOffset;

                    // 量化位置用于去重
                    Vector3Int quantized = new Vector3Int(
                        Mathf.RoundToInt(worldPos.x * precision),
                        Mathf.RoundToInt(worldPos.y * precision),
                        Mathf.RoundToInt(worldPos.z * precision)
                    );

                    int posIndex;
                    if (!positionToIndex.TryGetValue(quantized, out posIndex))
                    {
                        // 新的唯一位置
                        posIndex = uniquePositions.Count;
                        positionToIndex[quantized] = posIndex;
                        uniquePositions.Add(worldPos);
                        mappingsList.Add(new List<CornerMapping>());
                    }

                    // 添加映射
                    mappingsList[posIndex].Add(new CornerMapping
                    {
                        nodeIndex = nodeIdx,
                        cornerIndex = cornerIdx
                    });
                }
            }

            cornerPositions = uniquePositions.ToArray();
            cornerMappings = mappingsList.ToArray();
        }

        /// <summary>
        /// 构建均匀网格并计算所有节点的球谐系数
        /// </summary>
        /// <param name="rootCenter">根节点中心</param>
        /// <param name="rootHalfExtents">根节点各轴半尺寸</param>
        /// <param name="gridResolution">网格分辨率 (nx, ny, nz)</param>
        /// <returns>构建好的节点数组</returns>
        public static OctreeNode[] BuildUniformGrid(
            Vector3 rootCenter,
            Vector3 rootHalfExtents,
            Vector3Int gridResolution,
            List<ObstacleOBBData> obstacles = null)
        {
            int nx = gridResolution.x;
            int ny = gridResolution.y;
            int nz = gridResolution.z;
            int totalNodes = nx * ny * nz;

            OctreeNode[] nodes = new OctreeNode[totalNodes];

            Vector3 cellSize = new Vector3(
                rootHalfExtents.x * 2f / nx,
                rootHalfExtents.y * 2f / ny,
                rootHalfExtents.z * 2f / nz
            );
            Vector3 cellHalfExtents = cellSize * 0.5f;
            Vector3 volumeMin = rootCenter - rootHalfExtents;

            for (int iz = 0; iz < nz; iz++)
            {
                for (int iy = 0; iy < ny; iy++)
                {
                    for (int ix = 0; ix < nx; ix++)
                    {
                        int linearIndex = iz * (nx * ny) + iy * nx + ix;
                        OctreeNode node = OctreeNode.CreateLeaf(0, 0);

                        Vector3 cellCenter = volumeMin + new Vector3(
                            (ix + 0.5f) * cellSize.x,
                            (iy + 0.5f) * cellSize.y,
                            (iz + 0.5f) * cellSize.z
                        );

                        for (int c = 0; c < 8; c++)
                        {
                            Vector3 cornerOffset = new Vector3(
                                (c & 1) != 0 ? cellHalfExtents.x : -cellHalfExtents.x,
                                (c & 2) != 0 ? cellHalfExtents.y : -cellHalfExtents.y,
                                (c & 4) != 0 ? cellHalfExtents.z : -cellHalfExtents.z
                            );
                            Vector3 cornerWorldPos = cellCenter + cornerOffset;

                            SphericalHarmonicsL2 sh;
                            LightProbes.GetInterpolatedProbe(cornerWorldPos, null, out sh);
                            SHCoefficients shCoeff = SHCoefficients.FromSphericalHarmonicsL2(sh);
                            node.SetCornerSH(c, shCoeff);
                        }

                        nodes[linearIndex] = node;
                    }
                }
            }

            // 障碍物相交检测：对每个 Grid Node 的 AABB 执行 AABB-OBB 相交测试
            if (obstacles != null && obstacles.Count > 0)
            {
                for (int i = 0; i < totalNodes; i++)
                {
                    int iz = i / (nx * ny);
                    int iy = (i % (nx * ny)) / nx;
                    int ix = i % nx;

                    Vector3 cellCenter = volumeMin + new Vector3(
                        (ix + 0.5f) * cellSize.x,
                        (iy + 0.5f) * cellSize.y,
                        (iz + 0.5f) * cellSize.z
                    );

                    List<int> intersectingIndices = new List<int>();
                    for (int j = 0; j < obstacles.Count; j++)
                    {
                        if (ObstacleGeometry.AABBOBBIntersect(cellCenter, cellHalfExtents, obstacles[j]))
                        {
                            intersectingIndices.Add(j);
                        }
                    }

                    if (intersectingIndices.Count > 4)
                    {
                        Debug.LogWarning(
                            $"[OctreeBuilder] Grid Node at ({ix},{iy},{iz}) center={cellCenter} intersects {intersectingIndices.Count} OBBs, " +
                            $"exceeding the maximum of 4. Only the first 4 will be stored.");
                    }

                    if (intersectingIndices.Count > 0)
                    {
                        OctreeNode node = nodes[i];
                        node.obstacleIndices = OctreeNode.PackObstacleIndices(intersectingIndices.ToArray());
                        nodes[i] = node;
                    }
                }
            }

            return nodes;
        }

        /// <summary>
        /// 收集均匀网格的所有唯一角点位置
        /// 唯一角点数量 = (nx+1) * (ny+1) * (nz+1)
        /// </summary>
        public static Vector3[] CollectUniformGridCornerPositions(
            Vector3 rootCenter,
            Vector3 rootHalfExtents,
            Vector3Int gridResolution)
        {
            int nx = gridResolution.x;
            int ny = gridResolution.y;
            int nz = gridResolution.z;

            int totalCorners = (nx + 1) * (ny + 1) * (nz + 1);
            Vector3[] corners = new Vector3[totalCorners];

            Vector3 cellSize = new Vector3(
                rootHalfExtents.x * 2f / nx,
                rootHalfExtents.y * 2f / ny,
                rootHalfExtents.z * 2f / nz
            );
            Vector3 volumeMin = rootCenter - rootHalfExtents;

            int index = 0;
            for (int iz = 0; iz <= nz; iz++)
                for (int iy = 0; iy <= ny; iy++)
                    for (int ix = 0; ix <= nx; ix++)
                        corners[index++] = volumeMin + new Vector3(
                            ix * cellSize.x, iy * cellSize.y, iz * cellSize.z);

            return corners;
        }

        /// <summary>
        /// 验证八叉树结构的完整性
        /// </summary>
        public static bool ValidateOctree(OctreeNode[] nodes, out List<string> errors)
        {
            errors = new List<string>();

            if (nodes == null || nodes.Length == 0)
            {
                errors.Add("Node array is null or empty");
                return false;
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                OctreeNode node = nodes[i];

                // 检查子节点索引有效性
                if (!node.IsLeaf)
                {
                    if (node.childStartIndex < 0)
                    {
                        errors.Add($"Node {i}: Negative child start index {node.childStartIndex}");
                    }
                    else if (node.childStartIndex + 7 >= nodes.Length)
                    {
                        errors.Add($"Node {i}: Child index out of bounds (start: {node.childStartIndex}, array length: {nodes.Length})");
                    }

                    // 验证子节点的深度
                    for (int c = 0; c < 8; c++)
                    {
                        int childIdx = node.childStartIndex + c;
                        if (childIdx < nodes.Length && nodes[childIdx].depth != node.depth + 1)
                        {
                            errors.Add($"Node {i}: Child {c} has incorrect depth {nodes[childIdx].depth}, expected {node.depth + 1}");
                        }
                    }
                }

                // 检查深度有效性
                if (node.depth < 0)
                {
                    errors.Add($"Node {i}: Negative depth {node.depth}");
                }
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// 将角点位置设置到LightProbeGroup
        /// </summary>
        /// <param name="lightProbeGroup">目标LightProbeGroup</param>
        /// <param name="cornerPositions">角点位置数组</param>
        public static void SetLightProbeGroupPositions(
            LightProbeGroup lightProbeGroup,
            Vector3[] cornerPositions)
        {
            if (lightProbeGroup == null || cornerPositions == null)
                return;

            // LightProbeGroup使用局部坐标，需要转换
            Transform transform = lightProbeGroup.transform;
            Vector3[] localPositions = new Vector3[cornerPositions.Length];

            for (int i = 0; i < cornerPositions.Length; i++)
            {
                localPositions[i] = transform.InverseTransformPoint(cornerPositions[i]);
            }

            lightProbeGroup.probePositions = localPositions;
        }

        /// <summary>
        /// 从烘焙的LightProbes数据中读取角点SH系数并应用到节点
        /// </summary>
        /// <param name="nodes">节点数组</param>
        /// <param name="cornerPositions">角点位置数组</param>
        /// <param name="cornerMappings">角点到节点的映射</param>
        /// <returns>成功应用的角点数量</returns>
        public static int ApplyBakedLightProbeData(
            OctreeNode[] nodes,
            Vector3[] cornerPositions,
            List<CornerMapping>[] cornerMappings)
        {
            LightProbes lightProbes = LightmapSettings.lightProbes;
            if (lightProbes == null || lightProbes.bakedProbes == null)
            {
                Debug.LogError("No baked Light Probes found. Please bake lighting first.");
                return 0;
            }

            Vector3[] bakedPositions = lightProbes.positions;
            SphericalHarmonicsL2[] bakedProbes = lightProbes.bakedProbes;

            if (bakedPositions == null || bakedProbes == null)
            {
                Debug.LogError("Baked Light Probe data is invalid.");
                return 0;
            }

            // 精度：使用毫米级精度进行匹配
            const float precision = 100f;

            // 构建烘焙位置的查找字典
            Dictionary<Vector3Int, int> bakedPosToIndex = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < bakedPositions.Length; i++)
            {
                Vector3Int quantized = new Vector3Int(
                    Mathf.RoundToInt(bakedPositions[i].x * precision),
                    Mathf.RoundToInt(bakedPositions[i].y * precision),
                    Mathf.RoundToInt(bakedPositions[i].z * precision)
                );
                bakedPosToIndex[quantized] = i;
            }

            int appliedCount = 0;

            // 遍历所有角点位置
            for (int posIdx = 0; posIdx < cornerPositions.Length; posIdx++)
            {
                Vector3 pos = cornerPositions[posIdx];
                Vector3Int quantized = new Vector3Int(
                    Mathf.RoundToInt(pos.x * precision),
                    Mathf.RoundToInt(pos.y * precision),
                    Mathf.RoundToInt(pos.z * precision)
                );

                int bakedIndex;
                if (bakedPosToIndex.TryGetValue(quantized, out bakedIndex))
                {
                    // 找到匹配的烘焙数据
                    SphericalHarmonicsL2 sh = bakedProbes[bakedIndex];
                    SHCoefficients shCoeff = SHCoefficients.FromSphericalHarmonicsL2(sh);

                    // 应用到所有使用这个角点的节点
                    List<CornerMapping> mappings = cornerMappings[posIdx];
                    for (int m = 0; m < mappings.Count; m++)
                    {
                        CornerMapping mapping = mappings[m];
                        nodes[mapping.nodeIndex].SetCornerSH(mapping.cornerIndex, shCoeff);
                    }

                    appliedCount++;
                }
            }

            return appliedCount;
        }
    }
}
