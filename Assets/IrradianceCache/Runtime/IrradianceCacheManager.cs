using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 多 Volume 管理器 - 单例模式
    /// 负责管理所有激活的 IrradianceCache，合并数据并上传到 GPU
    /// </summary>
    [ExecuteInEditMode]
    [DefaultExecutionOrder(-100)] // 确保在其他脚本之前执行
    public class IrradianceCacheManager : MonoBehaviour
    {
        /// <summary>
        /// 最大支持的 Volume 数量
        /// </summary>
        public const int MaxVolumeCount = 64;

        [Tooltip("是否自动重建合并 Buffer")]
        public bool autoRebuild = true;

        [Tooltip("启用天空球回退 (Volume 外部使用环境光)")]
        public bool enableSkyboxFallback = true;
        
        [Tooltip("手动指定Ambient颜色")]
        public bool manualAmbientColor = true;
        [ColorUsage(false, true)]public Color ambientColor = Color.black;

        [Tooltip("SH Deringing 强度 (0 = 无衰减, 1 = 最大 Hanning 窗口衰减)")]
        [Range(0f, 1f)]
        public float shDeringingStrength = 0f;

        // 单例实例
        private static IrradianceCacheManager instance;
        public static IrradianceCacheManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<IrradianceCacheManager>();
                }
                return instance;
            }
        }

        /// <summary>
        /// 检查 Manager 是否存在且激活
        /// </summary>
        public static bool IsAvailable => Instance != null && Instance.isActiveAndEnabled;

        // 已注册的 Volume 列表
        private List<IrradianceCache> activeVolumes = new List<IrradianceCache>();

        // GPU 资源
        private ComputeBuffer mergedNodeBuffer;
        private ComputeBuffer volumeMetadataBuffer;
        private ComputeBuffer obstacleBuffer;

        // 状态标记
        private bool isDirty = false;
        private bool isGPUDataUploaded = false;

        // 统计信息
        private int totalNodeCount = 0;
        private int validVolumeCount = 0; // 有效 volume 数量（有 bakedData 的）
        private float gpuMemoryUsageMB = 0f;

        // Shader 属性 ID
        private static readonly int MergedNodesCompactID = Shader.PropertyToID("_MergedNodesCompact");
        private static readonly int VolumeMetadataBufferID = Shader.PropertyToID("_VolumeMetadataBuffer");
        private static readonly int ActiveVolumeCountID = Shader.PropertyToID("_ActiveVolumeCount");
        private static readonly int UseMultiVolumeModeID = Shader.PropertyToID("_UseMultiVolumeMode");
        private static readonly int SkyboxSH_R_ID = Shader.PropertyToID("_SkyboxSH_R");
        private static readonly int SkyboxSH_G_ID = Shader.PropertyToID("_SkyboxSH_G");
        private static readonly int SkyboxSH_B_ID = Shader.PropertyToID("_SkyboxSH_B");
        private static readonly int EnableSkyboxFallbackID = Shader.PropertyToID("_EnableSkyboxFallback");
        private static readonly int ObstacleBufferID = Shader.PropertyToID("_ObstacleBuffer");
        private static readonly int ObstacleCountID = Shader.PropertyToID("_ObstacleCount");
        private static readonly int SHDeringingStrengthID = Shader.PropertyToID("_SHDeringingStrength");

        /// <summary>
        /// 获取已注册的 Volume 数量
        /// </summary>
        public int ActiveVolumeCount => activeVolumes.Count;

        /// <summary>
        /// 获取已注册的 Volume 列表（只读）
        /// </summary>
        public IReadOnlyList<IrradianceCache> ActiveVolumes => activeVolumes;

        /// <summary>
        /// 获取总节点数
        /// </summary>
        public int TotalNodeCount => totalNodeCount;

        /// <summary>
        /// 获取 GPU 内存占用 (MB)
        /// </summary>
        public float GPUMemoryUsageMB => gpuMemoryUsageMB;

        /// <summary>
        /// 是否有数据已上传到 GPU
        /// </summary>
        public bool IsGPUDataUploaded => isGPUDataUploaded;

        private void Awake()
        {
            // 单例检查
            if (instance != null && instance != this)
            {
                Debug.LogWarning("Multiple IrradianceCacheManager instances detected. Destroying duplicate.");
                DestroyImmediate(this);
                return;
            }
            instance = this;
        }

        private void OnEnable()
        {
            instance = this;

            // 设置多 Volume 模式标记
            Shader.SetGlobalInt(UseMultiVolumeModeID, 1);

            // 上传天空球 SH 数据
            UploadSkyboxSH();
        }

        private void OnDisable()
        {
            // 清除多 Volume 模式标记
            Shader.SetGlobalInt(UseMultiVolumeModeID, 0);

            ReleaseGPUResources();

            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseGPUResources();

            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            // 自动重建
            if (autoRebuild && isDirty)
            {
                RebuildMergedBuffer();
            }

            // 实时更新 Deringing 参数
            Shader.SetGlobalFloat(SHDeringingStrengthID, shDeringingStrength);
        }

        /// <summary>
        /// 注册一个 Volume
        /// </summary>
        public void RegisterVolume(IrradianceCache volume)
        {
            if (volume == null || activeVolumes.Contains(volume))
                return;

            if (activeVolumes.Count >= MaxVolumeCount)
            {
                Debug.LogWarning($"Maximum volume count ({MaxVolumeCount}) reached. Cannot register more volumes.");
                return;
            }

            activeVolumes.Add(volume);
            isDirty = true;

            Debug.Log($"Volume registered: {volume.name}. Total active volumes: {activeVolumes.Count}");
        }

        /// <summary>
        /// 注销一个 Volume
        /// </summary>
        public void UnregisterVolume(IrradianceCache volume)
        {
            if (volume == null || !activeVolumes.Contains(volume))
                return;

            activeVolumes.Remove(volume);
            isDirty = true;

            Debug.Log($"Volume unregistered: {volume.name}. Total active volumes: {activeVolumes.Count}");

            // 如果没有 Volume 了，清理 GPU 资源
            if (activeVolumes.Count == 0)
            {
                ReleaseGPUResources();
            }
        }

        /// <summary>
        /// 标记需要重建
        /// </summary>
        public void MarkDirty()
        {
            isDirty = true;
        }

        /// <summary>
        /// 重建合并的 Buffer
        /// </summary>
        public void RebuildMergedBuffer()
        {
            isDirty = false;

            if (activeVolumes.Count == 0)
            {
                ReleaseGPUResources();
                return;
            }

            // 按优先级排序（高优先级在前）
            activeVolumes.Sort((a, b) => b.priority.CompareTo(a.priority));

            // 计算总节点数
            totalNodeCount = 0;
            foreach (var volume in activeVolumes)
            {
                if (volume.bakedData != null && volume.bakedData.nodes != null)
                {
                    totalNodeCount += volume.bakedData.nodes.Length;
                }
            }

            if (totalNodeCount == 0)
            {
                ReleaseGPUResources();
                return;
            }

            // 释放旧资源
            // ReleaseGPUResources();

            // 创建合并的节点数组
            OctreeNodeCompact[] mergedNodes = new OctreeNodeCompact[totalNodeCount];
            VolumeMetadata[] metadataArray = new VolumeMetadata[MaxVolumeCount];

            // 初始化所有 metadata 为空
            for (int i = 0; i < MaxVolumeCount; i++)
            {
                metadataArray[i] = VolumeMetadata.CreateEmpty();
            }

            // 复制并重映射节点
            int currentOffset = 0;
            int validVolumeIndex = 0; // 添加有效 volume 索引计数器
            for (int i = 0; i < activeVolumes.Count; i++)
            {
                var volume = activeVolumes[i];
                if (volume.bakedData == null || volume.bakedData.nodes == null)
                    continue;

                // 创建 metadata - 使用 validVolumeIndex 而不是循环索引
                metadataArray[validVolumeIndex] = VolumeMetadata.FromVolume(volume, currentOffset);

                // 复制并重映射节点
                CopyAndRemapNodes(volume.bakedData.nodes, mergedNodes, currentOffset);

                currentOffset += volume.bakedData.nodes.Length;
                validVolumeIndex++; // 只在处理有效 volume 后递增
            }

            // 保存有效 volume 数量
            validVolumeCount = validVolumeIndex;

            // 创建 GPU Buffer
            mergedNodeBuffer = new ComputeBuffer(totalNodeCount, OctreeNodeCompact.GetStride());
            mergedNodeBuffer.SetData(mergedNodes);

            volumeMetadataBuffer = new ComputeBuffer(MaxVolumeCount, VolumeMetadata.GetStride());
            volumeMetadataBuffer.SetData(metadataArray);

            // 上传到 GPU
            UploadToGPU();

            // 计算内存占用
            gpuMemoryUsageMB = (totalNodeCount * OctreeNodeCompact.GetStride() +
                               MaxVolumeCount * VolumeMetadata.GetStride()) / (1024f * 1024f);

            Debug.Log($"Multi-Volume buffer rebuilt. Valid volumes: {validVolumeCount}/{activeVolumes.Count}, " +
                     $"Total nodes: {totalNodeCount}, Memory: {gpuMemoryUsageMB:F2} MB");

            // 上传障碍物 Buffer
            UploadObstacleBuffer();
        }

        /// <summary>
        /// 复制节点并重映射子节点索引
        /// </summary>
        private void CopyAndRemapNodes(OctreeNode[] source, OctreeNodeCompact[] dest, int destOffset)
        {
            for (int i = 0; i < source.Length; i++)
            {
                OctreeNodeCompact compact = OctreeNodeCompact.FromOctreeNode(source[i]);

                // 关键：重映射子节点索引
                if (compact.childStartIndex != -1)
                {
                    compact.childStartIndex += destOffset;
                }

                dest[destOffset + i] = compact;
            }
        }

        /// <summary>
        /// 上传数据到 GPU
        /// </summary>
        private void UploadToGPU()
        {
            if (mergedNodeBuffer == null || volumeMetadataBuffer == null)
                return;

            Shader.SetGlobalBuffer(MergedNodesCompactID, mergedNodeBuffer);
            Shader.SetGlobalBuffer(VolumeMetadataBufferID, volumeMetadataBuffer);
            Shader.SetGlobalInt(ActiveVolumeCountID, validVolumeCount); // 使用有效 volume 数量
            Shader.SetGlobalInt(UseMultiVolumeModeID, 1);
            // 设置总节点数，用于Shader边界检查
            Shader.SetGlobalInt(Shader.PropertyToID("_TotalMergedNodeCount"), totalNodeCount);
            // 设置 SH Deringing 强度
            Shader.SetGlobalFloat(SHDeringingStrengthID, shDeringingStrength);

            isGPUDataUploaded = true;

            // 上传天空球 SH 数据
            UploadSkyboxSH();
        }

        /// <summary>
        /// 更新所有 Volume 的 Transform 矩阵
        /// </summary>
        public void UpdateAllTransformMatrices()
        {
            if (!isGPUDataUploaded || volumeMetadataBuffer == null)
                return;

            VolumeMetadata[] metadataArray = new VolumeMetadata[MaxVolumeCount];

            // 初始化所有 metadata 为空
            for (int i = 0; i < MaxVolumeCount; i++)
            {
                metadataArray[i] = VolumeMetadata.CreateEmpty();
            }

            // 更新每个 Volume 的 metadata
            int currentOffset = 0;
            int validVolumeIndex = 0; // 添加有效 volume 索引计数器
            for (int i = 0; i < activeVolumes.Count; i++)
            {
                var volume = activeVolumes[i];
                if (volume.bakedData == null || volume.bakedData.nodes == null)
                    continue;

                metadataArray[validVolumeIndex] = VolumeMetadata.FromVolume(volume, currentOffset);
                currentOffset += volume.bakedData.nodes.Length;
                validVolumeIndex++; // 只在处理有效 volume 后递增
            }

            volumeMetadataBuffer.SetData(metadataArray);
        }

        /// <summary>
        /// 上传天空球 SH 系数到 GPU
        /// 从 RenderSettings.ambientProbe 提取 L1 SH 系数
        /// </summary>
        private void UploadSkyboxSH()
        {
            if (!enableSkyboxFallback)
            {
                Shader.SetGlobalInt(EnableSkyboxFallbackID, 0);
                return;
            }

            Vector4 shR, shG, shB;
            var probe = new SphericalHarmonicsL2();
            if (!manualAmbientColor)
            {
                probe = RenderSettings.ambientProbe;
                // Unity SH 系数排列: [L0, L1_-1(Y), L1_0(Z), L1_1(X), ...]
                // 映射到我们的格式: [L0, L1x, L1y, L1z]
                shR = new Vector4(probe[0, 0], probe[0, 3], probe[0, 1], probe[0, 2]);
                shG = new Vector4(probe[1, 0], probe[1, 3], probe[1, 1], probe[1, 2]);
                shB = new Vector4(probe[2, 0], probe[2, 3], probe[2, 1], probe[2, 2]);
            }
            else
            {
                probe.AddAmbientLight(ambientColor);
                
                shR = new Vector4(probe[0, 0], probe[0, 3], probe[0, 1], probe[0, 2]);
                shG = new Vector4(probe[1, 0], probe[1, 3], probe[1, 1], probe[1, 2]);
                shB = new Vector4(probe[2, 0], probe[2, 3], probe[2, 1], probe[2, 2]);
            }

            Shader.SetGlobalVector(SkyboxSH_R_ID, shR);
            Shader.SetGlobalVector(SkyboxSH_G_ID, shG);
            Shader.SetGlobalVector(SkyboxSH_B_ID, shB);
            Shader.SetGlobalInt(EnableSkyboxFallbackID, 1);
        }

        /// <summary>
        /// 收集场景中所有激活的 ObstacleCubeComponent，上传 OBB 数据到 GPU
        /// </summary>
        private void UploadObstacleBuffer()
        {
            // 释放旧 buffer
            if (obstacleBuffer != null)
            {
                obstacleBuffer.Release();
                obstacleBuffer = null;
            }

            // 从所有激活 Volume 的 bakedData 中收集障碍物
            var allObstacles = new System.Collections.Generic.List<ObstacleOBBData>();
            foreach (var volume in activeVolumes)
            {
                if (volume.bakedData != null && volume.bakedData.bakedObstacles != null)
                {
                    allObstacles.AddRange(volume.bakedData.bakedObstacles);
                }
            }

            if (allObstacles.Count == 0)
            {
                Shader.SetGlobalInt(ObstacleCountID, 0);
                return;
            }

            var dataArray = allObstacles.ToArray();
            obstacleBuffer = new ComputeBuffer(dataArray.Length, ObstacleOBBData.GetStride());
            obstacleBuffer.SetData(dataArray);

            Shader.SetGlobalBuffer(ObstacleBufferID, obstacleBuffer);
            Shader.SetGlobalInt(ObstacleCountID, dataArray.Length);
        }

        /// <summary>
        /// 手动更新天空球 SH 数据（场景环境光变化时调用）
        /// </summary>
        public void UpdateSkyboxSH()
        {
            UploadSkyboxSH();
        }

        /// <summary>
        /// 释放 GPU 资源
        /// </summary>
        public void ReleaseGPUResources()
        {
            // 先清除全局Shader引用，防止Shader访问已释放的Buffer
            Shader.SetGlobalInt(ActiveVolumeCountID, 0);
            Shader.SetGlobalInt(UseMultiVolumeModeID, 0);
            // 重置总节点数，防止Shader访问无效数据
            Shader.SetGlobalInt(Shader.PropertyToID("_TotalMergedNodeCount"), 0);

            if (isGPUDataUploaded)
            {
                Shader.SetGlobalBuffer(MergedNodesCompactID, (ComputeBuffer)null);
                Shader.SetGlobalBuffer(VolumeMetadataBufferID, (ComputeBuffer)null);
            }

            // 然后释放Buffer
            if (mergedNodeBuffer != null)
            {
                mergedNodeBuffer.Release();
                mergedNodeBuffer = null;
            }

            if (volumeMetadataBuffer != null)
            {
                volumeMetadataBuffer.Release();
                volumeMetadataBuffer = null;
            }

            if (obstacleBuffer != null)
            {
                obstacleBuffer.Release();
                obstacleBuffer = null;
            }
            Shader.SetGlobalInt(ObstacleCountID, 0);

            isGPUDataUploaded = false;
            totalNodeCount = 0;
            gpuMemoryUsageMB = 0f;
        }

        /// <summary>
        /// 强制重建（用于 Editor）
        /// </summary>
        public void ForceRebuild()
        {
            RebuildMergedBuffer();
        }

        /// <summary>
        /// 检查点是否在任意 Volume 内
        /// </summary>
        public bool ContainsPoint(Vector3 worldPos)
        {
            foreach (var volume in activeVolumes)
            {
                if (volume.ContainsPoint(worldPos))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 获取包含指定点的所有 Volume
        /// </summary>
        public List<IrradianceCache> GetVolumesContainingPoint(Vector3 worldPos)
        {
            List<IrradianceCache> result = new List<IrradianceCache>();
            foreach (var volume in activeVolumes)
            {
                if (volume.ContainsPoint(worldPos))
                    result.Add(volume);
            }
            return result;
        }

        /// <summary>
        /// CPU 端多 Volume 采样（用于调试）
        /// </summary>
        public Color SampleLighting(Vector3 worldPos, Vector3 normal)
        {
            var containingVolumes = GetVolumesContainingPoint(worldPos);

            if (containingVolumes.Count == 0)
                return Color.black;

            if (containingVolumes.Count == 1)
                return containingVolumes[0].SampleLighting(worldPos, normal);

            // 多 Volume 混合
            Color result = Color.black;
            float totalWeight = 0f;

            foreach (var volume in containingVolumes)
            {
                float weight = CalculateBlendWeight(volume, worldPos);
                result += volume.SampleLighting(worldPos, normal) * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0f)
                result /= totalWeight;

            return result;
        }

        /// <summary>
        /// 计算混合权重
        /// </summary>
        private float CalculateBlendWeight(IrradianceCache volume, Vector3 worldPos)
        {
            if (volume.bakedData == null)
                return 0f;

            Vector3 localPos = volume.TransformWorldToLocal(worldPos);
            Vector3 localToRoot = localPos - volume.bakedData.rootCenter;

            Vector3 halfExtents = volume.bakedData.rootHalfExtents;
            float blendDist = volume.blendDistance;

            // 计算到边界的距离
            float distX = halfExtents.x - Mathf.Abs(localToRoot.x);
            float distY = halfExtents.y - Mathf.Abs(localToRoot.y);
            float distZ = halfExtents.z - Mathf.Abs(localToRoot.z);
            float minDist = Mathf.Min(distX, Mathf.Min(distY, distZ));

            // 在混合区域内进行平滑过渡
            if (blendDist > 0f && minDist < blendDist)
            {
                return Mathf.SmoothStep(0f, 1f, minDist / blendDist);
            }

            return 1f;
        }
    }
}
