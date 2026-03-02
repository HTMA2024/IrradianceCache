using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 运行时调试可视化组件
    /// 用于在 Scene View 中可视化八叉树结构和采样结果
    /// </summary>
    [ExecuteInEditMode]
    public class IrradianceCacheDebugger : MonoBehaviour
    {
        [Header("Target Volume")]
        [Tooltip("要调试的 IrradianceCache")]
        public IrradianceCache targetVolume;

        [Header("Visualization Settings")]
        [Tooltip("启用可视化")]
        public bool enableVisualization = true;

        [Tooltip("显示八叉树节点")]
        public bool showOctreeNodes = true;

        [Tooltip("最大显示深度")]
        [Range(0, 8)]
        public int displayDepth = 4;

        [Tooltip("只显示叶节点")]
        public bool showLeafNodesOnly = true;

        [Tooltip("高亮稀疏节点")]
        public bool highlightSparseNodes = true;

        [Tooltip("节点线框透明度")]
        [Range(0.1f, 1f)]
        public float nodeAlpha = 0.5f;

        [Header("Sampling Visualization")]
        [Tooltip("启用采样可视化")]
        public bool showSamplingVisualization = false;

        [Tooltip("采样点数量")]
        [Range(10, 500)]
        public int samplePointCount = 100;

        [Tooltip("采样点大小")]
        [Range(0.05f, 0.5f)]
        public float samplePointSize = 0.1f;

        [Tooltip("显示采样射线")]
        public bool showSampleRays = true;

        [Tooltip("射线长度")]
        [Range(0.1f, 2f)]
        public float rayLength = 0.5f;

        [Header("Query Test")]
        [Tooltip("启用查询测试")]
        public bool enableQueryTest = false;

        [Tooltip("查询位置")]
        public Vector3 queryPosition = Vector3.zero;

        [Tooltip("查询法线")]
        public Vector3 queryNormal = Vector3.up;
        
        [Tooltip("查询Transform")]
        public Transform queryTransform = null;

        // 缓存的采样结果
        private Color lastSampledColor = Color.black;
        private int lastQueriedNodeIndex = -1;

        private void OnDrawGizmos()
        {
            if (!enableVisualization || targetVolume == null || targetVolume.bakedData == null)
                return;

            IrradianceCacheData data = targetVolume.bakedData;

            // 获取 Transform 信息
            bool hasTransform = targetVolume.transformMode != TransformMode.None;
            Quaternion rotation = targetVolume.GetEffectiveRotation();
            Vector3 effectivePos = targetVolume.GetEffectivePosition();
            float effectiveScale = targetVolume.GetEffectiveScale();

            // 绘制根节点边界（变换后）
            Gizmos.color = Color.white;
            Vector3 rootWorldCenter = data.rootCenter + effectivePos;
            Vector3 rootWorldHalfExtents = data.rootHalfExtents * effectiveScale;

            if (hasTransform && rotation != Quaternion.identity)
            {
                // 使用矩阵绘制旋转后的立方体
                Matrix4x4 oldMatrix = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(rootWorldCenter, rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, rootWorldHalfExtents * 2f);
                Gizmos.matrix = oldMatrix;
            }
            else
            {
                Gizmos.DrawWireCube(rootWorldCenter, rootWorldHalfExtents * 2f);
            }

            // 绘制节点
            if (showOctreeNodes)
            {
                if (data.storageMode == VolumeStorageMode.UniformGrid)
                    DrawUniformGridNodes(data, hasTransform, rotation, effectivePos, effectiveScale);
                else
                    DrawOctreeNodes(data, hasTransform, rotation, effectivePos, effectiveScale);
            }

            // 绘制采样可视化
            if (showSamplingVisualization)
            {
                DrawSamplingVisualization(data, hasTransform, rotation, effectivePos, effectiveScale);
            }

            // 绘制查询测试
            if (enableQueryTest)
            {
                DrawQueryTest(data, hasTransform, rotation, effectivePos, effectiveScale);
            }
        }

        private void DrawOctreeNodes(IrradianceCacheData data, bool hasTransform, Quaternion rotation, Vector3 effectivePos, float effectiveScale)
        {
            if (data.nodes == null)
                return;

            for (int i = 0; i < data.nodes.Length; i++)
            {
                OctreeNode node = data.nodes[i];

                // 过滤深度
                if (node.depth != displayDepth)
                    continue;

                // 过滤非叶节点
                if (showLeafNodesOnly && !node.IsLeaf)
                    continue;

                // 计算节点位置和尺寸（通过遍历父节点链）
                Vector3 localCenter;
                Vector3 localHalfExtents;
                data.GetNodeBounds(i, out localCenter, out localHalfExtents);

                // 应用 Transform 变换
                Vector3 worldCenter;
                Vector3 worldHalfExtents;
                if (hasTransform)
                {
                    Vector3 offsetFromBaked = localCenter - data.rootCenter;
                    Vector3 transformedOffset = rotation * (offsetFromBaked * effectiveScale);
                    worldCenter = data.rootCenter + effectivePos + transformedOffset;
                    worldHalfExtents = localHalfExtents * effectiveScale;
                }
                else
                {
                    worldCenter = localCenter;
                    worldHalfExtents = localHalfExtents;
                }

                // 选择颜色
                Color nodeColor = GetNodeColor(node, data.maxDepth);
                Gizmos.color = nodeColor;

                // 绘制线框（带旋转）
                if (hasTransform && rotation != Quaternion.identity)
                {
                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, worldHalfExtents * 2f);
                    Gizmos.matrix = oldMatrix;
                }
                else
                {
                    Gizmos.DrawWireCube(worldCenter, worldHalfExtents * 2f);
                }

                // 高亮稀疏节点
                if (highlightSparseNodes && node.IsLeaf && node.depth < data.maxDepth)
                {
                    Gizmos.color = new Color(1f, 1f, 0f, 0.8f);
                    if (hasTransform && rotation != Quaternion.identity)
                    {
                        Matrix4x4 oldMatrix = Gizmos.matrix;
                        Gizmos.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                        Gizmos.DrawWireCube(Vector3.zero, worldHalfExtents * 2.02f);
                        Gizmos.matrix = oldMatrix;
                    }
                    else
                    {
                        Gizmos.DrawWireCube(worldCenter, worldHalfExtents * 2.02f);
                    }

                    // 绘制中心标记
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawSphere(worldCenter, Mathf.Min(worldHalfExtents.x, Mathf.Min(worldHalfExtents.y, worldHalfExtents.z)) * 0.2f);
                }
            }
        }

        /// <summary>
        /// 绘制均匀网格节点（Grid 模式专用）
        /// 所有节点处于同一深度，无需深度过滤和稀疏高亮
        /// </summary>
        private void DrawUniformGridNodes(IrradianceCacheData data,
            bool hasTransform, Quaternion rotation, Vector3 effectivePos, float effectiveScale)
        {
            if (data.nodes == null)
                return;

            Color cellColor = Color.green;
            cellColor.a = nodeAlpha;

            for (int i = 0; i < data.nodes.Length; i++)
            {
                Vector3 localCenter;
                Vector3 localHalfExtents;
                data.GetNodeBounds(i, out localCenter, out localHalfExtents);

                // 应用 Transform 变换（与 DrawOctreeNodes 相同逻辑）
                Vector3 worldCenter;
                Vector3 worldHalfExtents;
                if (hasTransform)
                {
                    Vector3 offsetFromBaked = localCenter - data.rootCenter;
                    Vector3 transformedOffset = rotation * (offsetFromBaked * effectiveScale);
                    worldCenter = data.rootCenter + effectivePos + transformedOffset;
                    worldHalfExtents = localHalfExtents * effectiveScale;
                }
                else
                {
                    worldCenter = localCenter;
                    worldHalfExtents = localHalfExtents;
                }

                Gizmos.color = cellColor;

                if (hasTransform && rotation != Quaternion.identity)
                {
                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, worldHalfExtents * 2f);
                    Gizmos.matrix = oldMatrix;
                }
                else
                {
                    Gizmos.DrawWireCube(worldCenter, worldHalfExtents * 2f);
                }
            }
        }

        private Color GetNodeColor(OctreeNode node, int maxDepth)
        {
            if (maxDepth <= 0)
            {
                // Grid 模式：所有节点深度为 0，使用统一绿色
                Color color = Color.green;
                color.a = nodeAlpha;
                return color;
            }

            if (node.IsLeaf)
            {
                float t = (float)node.depth / maxDepth;
                Color color = Color.Lerp(Color.green, Color.red, t);
                color.a = nodeAlpha;
                return color;
            }
            else
            {
                return new Color(0.5f, 0.5f, 1f, nodeAlpha * 0.5f);
            }
        }

        private void DrawSamplingVisualization(IrradianceCacheData data, bool hasTransform, Quaternion rotation, Vector3 effectivePos, float effectiveScale)
        {
            // 使用固定随机种子保持一致性
            Random.InitState(42);

            for (int i = 0; i < samplePointCount; i++)
            {
                // 在 Volume 范围内随机采样（变换后的空间）
                Vector3 localOffset = new Vector3(
                    Random.Range(-1f, 1f) * data.rootHalfExtents.x,
                    Random.Range(-1f, 1f) * data.rootHalfExtents.y,
                    Random.Range(-1f, 1f) * data.rootHalfExtents.z
                );

                // 应用 Transform 变换
                Vector3 worldPos;
                if (hasTransform)
                {
                    Vector3 transformedOffset = rotation * (localOffset * effectiveScale);
                    worldPos = data.rootCenter + effectivePos + transformedOffset;
                }
                else
                {
                    worldPos = data.rootCenter + localOffset;
                }

                // 随机法线（世界空间）
                Vector3 worldNormal = Random.onUnitSphere;

                // 采样颜色（使用 Volume 的采样方法，支持 Transform）
                Color sampledColor = targetVolume.SampleLighting(worldPos, worldNormal);

                // 绘制采样点
                Gizmos.color = sampledColor;
                Gizmos.DrawSphere(worldPos, samplePointSize * effectiveScale);

                // 绘制法线射线
                if (showSampleRays)
                {
                    Gizmos.color = new Color(sampledColor.r, sampledColor.g, sampledColor.b, 0.5f);
                    Gizmos.DrawRay(worldPos, worldNormal * rayLength);
                }
            }
        }

        private void DrawQueryTest(IrradianceCacheData data, bool hasTransform, Quaternion rotation, Vector3 effectivePos, float effectiveScale)
        {
            // 绘制查询位置
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(queryPosition, 0.2f);

            // 绘制查询法线
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(queryPosition, queryNormal.normalized * rayLength);

            // 使用 Volume 的方法查询（支持 Transform）
            Vector3 localQueryPos = targetVolume.TransformWorldToLocal(queryPosition);
            int nodeIndex = data.QueryLeafNode(localQueryPos);
            lastQueriedNodeIndex = nodeIndex;

            if (nodeIndex >= 0 && nodeIndex < data.nodes.Length)
            {
                // 获取节点的局部坐标（使用 GetNodeBounds 兼容 Octree 和 Grid 模式）
                Vector3 localCenter;
                Vector3 localHalfExtents;
                data.GetNodeBounds(nodeIndex, out localCenter, out localHalfExtents);

                // 应用 Transform 变换
                Vector3 worldCenter;
                Vector3 worldHalfExtents;
                if (hasTransform)
                {
                    Vector3 offsetFromBaked = localCenter - data.rootCenter;
                    Vector3 transformedOffset = rotation * (offsetFromBaked * effectiveScale);
                    worldCenter = data.rootCenter + effectivePos + transformedOffset;
                    worldHalfExtents = localHalfExtents * effectiveScale;
                }
                else
                {
                    worldCenter = localCenter;
                    worldHalfExtents = localHalfExtents;
                }

                // 高亮查询到的节点（带旋转）
                Gizmos.color = Color.cyan;
                if (hasTransform && rotation != Quaternion.identity)
                {
                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, worldHalfExtents * 2f);
                    Gizmos.DrawWireCube(Vector3.zero, worldHalfExtents * 2.05f);
                    Gizmos.matrix = oldMatrix;
                }
                else
                {
                    Gizmos.DrawWireCube(worldCenter, worldHalfExtents * 2f);
                    Gizmos.DrawWireCube(worldCenter, worldHalfExtents * 2.05f);
                }

                // 绘制节点角点（带旋转）
                Gizmos.color = Color.white;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 cornerOffset = new Vector3(
                        (i & 1) != 0 ? worldHalfExtents.x : -worldHalfExtents.x,
                        (i & 2) != 0 ? worldHalfExtents.y : -worldHalfExtents.y,
                        (i & 4) != 0 ? worldHalfExtents.z : -worldHalfExtents.z
                    );

                    Vector3 cornerWorld;
                    if (hasTransform && rotation != Quaternion.identity)
                    {
                        cornerWorld = worldCenter + rotation * cornerOffset;
                    }
                    else
                    {
                        cornerWorld = worldCenter + cornerOffset;
                    }
                    Gizmos.DrawSphere(cornerWorld, 0.05f * effectiveScale);
                }

                // 采样并显示颜色（使用 Volume 的采样方法，支持 Transform）
                lastSampledColor = targetVolume.SampleLighting(queryPosition, queryNormal.normalized);

                // 绘制采样结果颜色球
                Gizmos.color = lastSampledColor;
                Gizmos.DrawSphere(queryPosition + Vector3.up * 0.5f, 0.15f);
            }
        }

        /// <summary>
        /// 获取上次查询的节点索引
        /// </summary>
        public int GetLastQueriedNodeIndex()
        {
            return lastQueriedNodeIndex;
        }

        /// <summary>
        /// 获取上次采样的颜色
        /// </summary>
        public Color GetLastSampledColor()
        {
            return lastSampledColor;
        }

        /// <summary>
        /// 设置查询位置为当前 Transform 位置
        /// </summary>
        public void SetQueryPositionToTransform()
        {
            queryPosition = transform.position;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // 确保法线归一化
            if (queryNormal.sqrMagnitude > 0)
            {
                queryNormal = queryNormal.normalized;
            }
            else
            {
                queryNormal = Vector3.up;
            }
        }

        private void Reset()
        {
            // 尝试自动找到 Volume
            targetVolume = FindObjectOfType<IrradianceCache>();
        }
#endif
    }
}
