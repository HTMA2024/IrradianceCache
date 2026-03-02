using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// Transform 模式枚举
    /// </summary>
    public enum TransformMode
    {
        None,           // 不使用变换，使用 baked 原始位置
        UseGameObject,  // 使用 GameObject 的 Transform
        Manual          // 使用手动设置的参数
    }

    /// <summary>
    /// IrradianceCache 主组件
    /// 管理 Volume 边界、触发 Bake 流程、运行时加载和上传数据至 GPU
    /// </summary>
    [ExecuteInEditMode]
    public class IrradianceCache : MonoBehaviour
    {
        [Tooltip("Volume 边界")]
        public Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10f);

        [Header("Storage Mode")]
        [Tooltip("数据存储模式")]
        public VolumeStorageMode storageMode = VolumeStorageMode.UniformGrid;

        [Tooltip("八叉树最大深度 (推荐 2 - 3)")]
        [Range(1, 4)]
        public int maxDepth = 4;

        [Tooltip("均匀网格各轴分辨率 (仅 UniformGrid 模式)")]
        public Vector3Int gridResolution = new Vector3Int(8, 8, 8);
        
        [Range(0, 10)]
        public float padding = 0.0f;

        [Tooltip("是否允许编辑Bounds")]
        public bool allowEditBounds = false;

        [Tooltip("Bake 所依赖的LightProbeGroup")]
        public List<LightProbeGroup> lightProbeGroupList;
        
        [Tooltip("Bake 后的数据资产")]
        public IrradianceCacheData bakedData;

        [Header("Transform Settings")]
        [Tooltip("Transform 模式")]
        public TransformMode transformMode = TransformMode.None;

        [Tooltip("位置偏移（相对于 baked 位置）")]
        public Vector3 positionOffset = Vector3.zero;

        [Tooltip("旋转（欧拉角）")]
        public Vector3 rotationEuler = Vector3.zero;

        [Tooltip("均匀缩放")]
        [Range(0.1f, 10f)]
        public float uniformScale = 1f;

        [Tooltip("优先级 (0-100, 越高越优先)")]
        [Range(0, 100)]
        public int priority = 50;

        [Tooltip("边界混合距离 (用于多 Volume 重叠区域的平滑过渡)")]
        [Min(0f)]
        public float blendDistance = 1.0f;

        [Tooltip("是否使用 Volume 管理器 (多 Volume 模式)")]
        public bool useVolumeManager = true;

        [Tooltip("是否在运行时自动上传数据到 GPU")]
        public bool autoUploadToGPU = true;

        [Tooltip("在编辑器中也上传到 GPU (用于预览)")]
        public bool uploadInEditor = true;

        [Tooltip("使用 Half 精度压缩格式上传到 GPU (节省约 48% 内存)")]
        public bool useCompactFormat = true;

        [Tooltip("启用障碍物遮挡 (仅 UniformGrid 模式)")]
        public bool applyObstacle = false;

        [Tooltip("SH Deringing 强度 (0 = 无衰减, 1 = 最大 Hanning 窗口衰减)")]
        [Range(0f, 1f)]
        public float shDeringingStrength = 0f;

        [Header("Transition Blending (跨深度接缝消除)")]
        [Tooltip("启用跨深度过渡混合，消除不同深度节点边界处的接缝")]
        public bool enableTransitionBlending = true;

        [Tooltip("过渡区域宽度比例 (相对于节点尺寸，推荐 0.1-0.2)")]
        [Range(0.05f, 1.0f)]
        public float transitionWidthRatio = 0.15f;

        // GPU 资源
        private ComputeBuffer nodeBuffer;
        private ComputeBuffer obstacleBuffer;
        private bool isGPUDataUploaded = false;

        // Transform 缓存（用于检测变化）
        private Vector3 cachedPosition;
        private Quaternion cachedRotation;
        private Vector3 cachedScale;
        private TransformMode cachedTransformMode;
        private Vector3 cachedPositionOffset;
        private Vector3 cachedRotationEuler;
        private float cachedUniformScale;

        // 当前活动的 Volume (用于全局访问)
        private static IrradianceCache activeVolume;
        public static IrradianceCache ActiveVolume => activeVolume;

        // Shader 属性 ID
        private static readonly int ObstacleBufferID = Shader.PropertyToID("_ObstacleBuffer");
        private static readonly int ObstacleCountID = Shader.PropertyToID("_ObstacleCount");
        private static readonly int OctreeNodesID = Shader.PropertyToID("_OctreeNodes");
        private static readonly int OctreeNodesCompactID = Shader.PropertyToID("_OctreeNodesCompact");
        private static readonly int OctreeMaxDepthID = Shader.PropertyToID("_OctreeMaxDepth");
        private static readonly int OctreeRootCenterID = Shader.PropertyToID("_OctreeRootCenter");
        private static readonly int OctreeRootHalfExtentsID = Shader.PropertyToID("_OctreeRootHalfExtents");
        private static readonly int OctreeNodeCountID = Shader.PropertyToID("_OctreeNodeCount");
        private static readonly int OctreeWorldToLocalID = Shader.PropertyToID("_OctreeWorldToLocal");
        private static readonly int OctreeRotationMatrixID = Shader.PropertyToID("_OctreeRotationMatrix");
        private static readonly int OctreeEnableTransitionBlendingID = Shader.PropertyToID("_OctreeEnableTransitionBlending");
        private static readonly int OctreeTransitionWidthRatioID = Shader.PropertyToID("_OctreeTransitionWidthRatio");
        private static readonly int GridResolutionID = Shader.PropertyToID("_GridResolution");
        private static readonly int SHDeringingStrengthID = Shader.PropertyToID("_SHDeringingStrength");

        private static readonly string OctreeSamplingBlendMode = "_OCTREE_SAMPLING_MODE_WITH_BLENDING_ON";
        private static readonly string OctreeLPVMode = "_LPVMODE_OCTREE";
        private static readonly string UniformLPVMode = "_LPVMODE_UNIFORM";

        private void OnEnable()
        {
            // 尝试使用 Volume 管理器 (多 Volume 模式)
            if (useVolumeManager && IrradianceCacheManager.IsAvailable)
            {
                IrradianceCacheManager.Instance.RegisterVolume(this);
            }
            else
            {
                // 回退到单 Volume 模式
                activeVolume = this;

                // 初始化 Transform 缓存
                CacheTransformState();

                if (Application.isPlaying && autoUploadToGPU)
                {
                    UploadToGPU();
                }
                else if (!Application.isPlaying && uploadInEditor)
                {
                    UploadToGPU();
                }
            }
        }

        private void Update()
        {
            // 检测 Transform 变化并更新矩阵
            if (HasTransformChanged())
            {
                CacheTransformState();

                // 如果使用管理器模式，通知管理器更新
                if (useVolumeManager && IrradianceCacheManager.IsAvailable)
                {
                    IrradianceCacheManager.Instance.UpdateAllTransformMatrices();
                }
                else if (isGPUDataUploaded)
                {
                    UpdateTransformMatrices();
                }
            }

            // 实时更新 Deringing 参数（单 Volume 模式）
            if (isGPUDataUploaded && !useVolumeManager)
            {
                Shader.SetGlobalFloat(SHDeringingStrengthID, shDeringingStrength);
            }
        }

        /// <summary>
        /// 缓存当前 Transform 状态
        /// </summary>
        private void CacheTransformState()
        {
            cachedPosition = transform.position;
            cachedRotation = transform.rotation;
            cachedScale = transform.lossyScale;
            cachedTransformMode = transformMode;
            cachedPositionOffset = positionOffset;
            cachedRotationEuler = rotationEuler;
            cachedUniformScale = uniformScale;
        }

        /// <summary>
        /// 检测 Transform 是否发生变化
        /// </summary>
        private bool HasTransformChanged()
        {
            if (cachedTransformMode != transformMode)
                return true;

            if (transformMode == TransformMode.UseGameObject)
            {
                return cachedPosition != transform.position ||
                       cachedRotation != transform.rotation ||
                       cachedScale != transform.lossyScale;
            }
            else if (transformMode == TransformMode.Manual)
            {
                return cachedPositionOffset != positionOffset ||
                       cachedRotationEuler != rotationEuler ||
                       cachedUniformScale != uniformScale;
            }

            return false;
        }

        private void OnDisable()
        {
            // 从管理器注销
            if (useVolumeManager && IrradianceCacheManager.IsAvailable)
            {
                IrradianceCacheManager.Instance.UnregisterVolume(this);
            }

            if (activeVolume == this)
            {
                activeVolume = null;
            }
            ReleaseGPUResources();
        }

        private void OnDestroy()
        {
            // 从管理器注销
            if (useVolumeManager && IrradianceCacheManager.IsAvailable)
            {
                IrradianceCacheManager.Instance.UnregisterVolume(this);
            }

            if (activeVolume == this)
            {
                activeVolume = null;
            }
            ReleaseGPUResources();
        }

        /// <summary>
        /// 根据场景中的 Light Probe 自动计算 Volume 边界
        /// </summary>
        public Bounds CalculateAutoVolumeBounds()
        {
            if (lightProbeGroupList == null)
            {
                Debug.LogWarning("No Light Probes Group found in scene. Using default bounds.");
                return new Bounds(transform.position, Vector3.one * 10f);
            }

            List<Vector3> lightProbePositions = new List<Vector3>();
            foreach (var lightProbeGroup in lightProbeGroupList)
            {
                var probePosArray = lightProbeGroup.probePositions.AsSpan();
                lightProbeGroup.transform.TransformPoints(probePosArray);
                lightProbePositions.AddRange(probePosArray.ToArray());
            }
            Vector3[] positions = lightProbePositions.ToArray();

            // 计算包围盒
            Vector3 min = positions[0];
            Vector3 max = positions[0];

            foreach (Vector3 pos in positions)
            {
                min = Vector3.Min(min, pos);
                max = Vector3.Max(max, pos);
            }

            // 添加边界扩展 
            // Vector3 size = max - min;
            Vector3 actPadding = Vector3.one * (0.5f * padding);
            
            // 确保最小尺寸
            // actPadding = Vector3.Max(actPadding, Vector3.one * 0.5f);
            
            min -= actPadding;
            max += actPadding;

            Vector3 center = (min + max) * 0.5f;
            Vector3 finalSize = max - min;

            return new Bounds(center, finalSize);
        }

        /// <summary>
        /// 上传数据到 GPU
        /// </summary>
        public void UploadToGPU()
        {
            if (bakedData == null || bakedData.nodes == null || bakedData.nodes.Length == 0)
            {
                Debug.LogWarning("No baked data to upload to GPU.");
                return;
            }

            // 释放旧资源
            ReleaseGPUResources();

            // 使用压缩格式 (Half 精度)
            OctreeNodeCompact[] compactNodes = OctreeNodeCompact.FromOctreeNodeArray(bakedData.nodes);

            nodeBuffer = new ComputeBuffer(
                compactNodes.Length,
                OctreeNodeCompact.GetStride()
            );
            nodeBuffer.SetData(compactNodes);

            // 上传烘焙的障碍物数据
            var obstacles = bakedData.bakedObstacles;
            if (obstacles != null && obstacles.Length > 0)
            {
                obstacleBuffer = new ComputeBuffer(obstacles.Length, ObstacleOBBData.GetStride());
                obstacleBuffer.SetData(obstacles);
                Shader.SetGlobalBuffer(ObstacleBufferID, obstacleBuffer);
                Shader.SetGlobalInt(ObstacleCountID, obstacles.Length);
            }
            else
            {
                Shader.SetGlobalInt(ObstacleCountID, 0);
            }

            float memoryMB = (compactNodes.Length * OctreeNodeCompact.GetStride()) / (1024f * 1024f);

            // 设置全局 Shader 参数
            Shader.SetGlobalBuffer(OctreeNodesCompactID, nodeBuffer);
            Shader.SetGlobalInt(OctreeMaxDepthID, bakedData.maxDepth);
            Shader.SetGlobalVector(OctreeRootCenterID, bakedData.rootCenter);
            Shader.SetGlobalVector(OctreeRootHalfExtentsID, bakedData.rootHalfExtents);
            Shader.SetGlobalInt(OctreeNodeCountID, bakedData.nodes.Length);

            // 设置过渡混合参数
            Shader.SetGlobalInt(OctreeEnableTransitionBlendingID, enableTransitionBlending ? 1 : 0);
            Shader.SetGlobalFloat(OctreeTransitionWidthRatioID, transitionWidthRatio);

            // 设置 SH Deringing 强度
            Shader.SetGlobalFloat(SHDeringingStrengthID, shDeringingStrength);

            // 设置采样模式
            // if (samplingMode == LightProbeSamplingMode.WithBlending)
            // {
            //     Shader.EnableKeyword(OctreeSamplingBlendMode);
            // }
            // else
            // {
            //     Shader.DisableKeyword(OctreeSamplingBlendMode);
            // }

            // 设置网格分辨率 (UniformGrid 模式)
            if (bakedData.storageMode == VolumeStorageMode.UniformGrid)
            {
                Shader.SetGlobalVector(GridResolutionID,
                    new Vector4(bakedData.gridResolution.x, bakedData.gridResolution.y,
                                bakedData.gridResolution.z, 0));
                // Shader.DisableKeyword(OctreeLPVMode);
                // Shader.EnableKeyword(UniformLPVMode);
            }else if (bakedData.storageMode == VolumeStorageMode.Octree)
            {
                // Shader.DisableKeyword(UniformLPVMode);
                // Shader.EnableKeyword(OctreeLPVMode);
            }

            isGPUDataUploaded = true;

            // 更新 Transform 矩阵
            UpdateTransformMatrices();

            string formatStr = "Compact (Half)";
            string modeStr = bakedData.storageMode == VolumeStorageMode.UniformGrid ? "UniformGrid" : "Octree";
            Debug.Log($"Uploaded {bakedData.nodes.Length} {modeStr} nodes to GPU. Format: {formatStr}, Memory: {memoryMB:F2} MB");
        }

        /// <summary>
        /// 上传数据到指定材质 (用于单独材质而非全局)
        /// </summary>
        public void UploadToMaterial(Material material)
        {
            if (bakedData == null || bakedData.nodes == null || bakedData.nodes.Length == 0)
            {
                Debug.LogWarning("No baked data to upload to material.");
                return;
            }

            if (nodeBuffer == null)
            {
                OctreeNodeCompact[] compactNodes = OctreeNodeCompact.FromOctreeNodeArray(bakedData.nodes);
                nodeBuffer = new ComputeBuffer(
                    compactNodes.Length,
                    OctreeNodeCompact.GetStride()
                );
                nodeBuffer.SetData(compactNodes);
            }

            material.SetBuffer(OctreeNodesCompactID, nodeBuffer);
            material.SetInt(OctreeMaxDepthID, bakedData.maxDepth);
            material.SetVector(OctreeRootCenterID, bakedData.rootCenter);
            material.SetVector(OctreeRootHalfExtentsID, bakedData.rootHalfExtents);
            material.SetInt(OctreeNodeCountID, bakedData.nodes.Length);

            // 设置过渡混合参数
            material.SetInt(OctreeEnableTransitionBlendingID, enableTransitionBlending ? 1 : 0);
            material.SetFloat(OctreeTransitionWidthRatioID, transitionWidthRatio);

            // 更新 Transform 矩阵到材质
            UpdateTransformMatricesToMaterial(material);
        }

        /// <summary>
        /// 获取有效的旋转四元数
        /// </summary>
        public Quaternion GetEffectiveRotation()
        {
            switch (transformMode)
            {
                case TransformMode.UseGameObject:
                    return transform.rotation;
                case TransformMode.Manual:
                    return Quaternion.Euler(rotationEuler);
                default:
                    return Quaternion.identity;
            }
        }

        /// <summary>
        /// 获取有效的位置偏移
        /// </summary>
        public Vector3 GetEffectivePosition()
        {
            switch (transformMode)
            {
                case TransformMode.UseGameObject:
                    return transform.position;
                case TransformMode.Manual:
                    return positionOffset;
                default:
                    return Vector3.zero;
            }
        }

        /// <summary>
        /// 获取有效的缩放
        /// </summary>
        public float GetEffectiveScale()
        {
            switch (transformMode)
            {
                case TransformMode.UseGameObject:
                    // 使用均匀缩放（取最大分量）
                    Vector3 scale = transform.lossyScale;
                    return Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                case TransformMode.Manual:
                    return uniformScale;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// 获取 World 到 Volume 局部空间的变换矩阵
        /// </summary>
        public Matrix4x4 GetWorldToVolumeMatrix()
        {
            if (bakedData == null || transformMode == TransformMode.None)
            {
                return Matrix4x4.identity;
            }

            Vector3 bakedCenter = bakedData.rootCenter;
            Quaternion effectiveRot = GetEffectiveRotation();
            float effectiveScale = GetEffectiveScale();

            Matrix4x4 worldToVolume;

            switch (transformMode)
            {
                case TransformMode.UseGameObject:
                {
                    // 使用 GameObject 的 Transform
                    // 假设 bake 时 GameObject 在原点，bakedCenter 是相对于原点的偏移
                    // 现在 GameObject 移动到了 transform.position

                    // 构建从 baked 空间到世界空间的变换
                    // 1. 从 baked 坐标系原点开始（数据是相对于 bakedCenter 存储的）
                    // 2. 应用 GameObject 的变换（位置、旋转、缩放）
                    Matrix4x4 bakedToWorld = transform.localToWorldMatrix;

                    // 考虑 bakedCenter 的偏移
                    // 如果 bake 时 Volume 中心不在 GameObject 原点，需要先平移
                    Matrix4x4 centerOffset = Matrix4x4.Translate(bakedCenter);
                    Matrix4x4 volumeToWorld = bakedToWorld * centerOffset;

                    // 取逆得到世界到 Volume 的变换
                    worldToVolume = volumeToWorld.inverse;

                    // 最后需要平移回 bakedCenter，因为节点数据是相对于 bakedCenter 存储的
                    Matrix4x4 translateToBaked = Matrix4x4.Translate(bakedCenter);
                    worldToVolume = translateToBaked * worldToVolume;
                    break;
                }

                case TransformMode.Manual:
                {
                    // 使用手动设置的参数
                    // positionOffset 是相对于 baked 位置的偏移
                    Vector3 volumeCenter = bakedCenter + positionOffset;
                    Matrix4x4 volumeToWorld = Matrix4x4.TRS(volumeCenter, effectiveRot, Vector3.one * effectiveScale);

                    // 我们需要的是 World 到 Volume 的矩阵
                    worldToVolume = volumeToWorld.inverse;

                    // 最后平移回 baked 中心（因为八叉树数据是相对于 baked 中心存储的）
                    Matrix4x4 translateToBaked = Matrix4x4.Translate(bakedCenter);
                    worldToVolume = translateToBaked * worldToVolume;
                    break;
                }

                default:
                    return Matrix4x4.identity;
            }

            return worldToVolume;
        }

        /// <summary>
        /// 更新 Transform 矩阵到 GPU
        /// </summary>
        public void UpdateTransformMatrices()
        {
            Matrix4x4 worldToLocal = GetWorldToVolumeMatrix();
            Quaternion effectiveRot = GetEffectiveRotation();

            // 旋转矩阵的逆（用于变换法线）
            // 因为我们要将世界法线变换到 Volume 局部空间
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(Quaternion.Inverse(effectiveRot));

            Shader.SetGlobalMatrix(OctreeWorldToLocalID, worldToLocal);
            Shader.SetGlobalMatrix(OctreeRotationMatrixID, rotationMatrix);
        }

        /// <summary>
        /// 更新过渡混合参数到 GPU
        /// </summary>
        public void UpdateTransitionBlendingParams()
        {
            Shader.SetGlobalInt(OctreeEnableTransitionBlendingID, enableTransitionBlending ? 1 : 0);
            Shader.SetGlobalFloat(OctreeTransitionWidthRatioID, transitionWidthRatio);
        }

        /// <summary>
        /// 更新 Transform 矩阵到指定材质
        /// </summary>
        private void UpdateTransformMatricesToMaterial(Material material)
        {
            Matrix4x4 worldToLocal = GetWorldToVolumeMatrix();
            Quaternion effectiveRot = GetEffectiveRotation();
            Matrix4x4 rotationMatrix = Matrix4x4.Rotate(Quaternion.Inverse(effectiveRot));

            material.SetMatrix(OctreeWorldToLocalID, worldToLocal);
            material.SetMatrix(OctreeRotationMatrixID, rotationMatrix);
        }

        /// <summary>
        /// 获取变换后的 Volume 边界
        /// </summary>
        public Bounds GetTransformedBounds()
        {
            if (bakedData == null || transformMode == TransformMode.None)
            {
                return new Bounds(bakedData != null ? bakedData.rootCenter : bounds.center,
                                  bakedData != null ? bakedData.rootHalfExtents * 2f : bounds.size);
            }

            Vector3 bakedCenter = bakedData.rootCenter;
            Vector3 bakedHalfExtents = bakedData.rootHalfExtents;

            Vector3 effectivePos = GetEffectivePosition();
            float effectiveScale = GetEffectiveScale();

            Vector3 newCenter = bakedCenter + effectivePos;
            Vector3 newHalfExtents = bakedHalfExtents * effectiveScale;

            return new Bounds(newCenter, newHalfExtents * 2f);
        }

        /// <summary>
        /// 释放 GPU 资源
        /// </summary>
        public void ReleaseGPUResources()
        {
            // 先清除全局Shader引用，防止Shader访问已释放的Buffer
            if (isGPUDataUploaded)
            {
                // 重置节点数量，防止Shader访问无效数据
                Shader.SetGlobalBuffer(OctreeNodesCompactID, (ComputeBuffer)null);
                Shader.SetGlobalInt(OctreeNodeCountID, 0);
            }

            // 释放旧 buffer
            if (obstacleBuffer != null)
            {
                obstacleBuffer.Release();
                obstacleBuffer = null;
            }
            
            // 然后释放Buffer
            if (nodeBuffer != null)
            {
                nodeBuffer.Release();
                nodeBuffer = null;
            }
            isGPUDataUploaded = false;
        }

        /// <summary>
        /// 检查 GPU 数据是否已上传
        /// </summary>
        public bool IsGPUDataUploaded => isGPUDataUploaded;

        /// <summary>
        /// 获取当前 GPU 内存占用 (MB)
        /// </summary>
        public float GetGPUMemoryUsageMB()
        {
            if (!isGPUDataUploaded || bakedData == null)
                return 0f;

            int stride = OctreeNodeCompact.GetStride();
            return (bakedData.nodes.Length * stride) / (1024f * 1024f);
        }

        /// <summary>
        /// CPU 端采样光照 (用于调试或不支持 Compute Buffer 的平台)
        /// 支持 Transform 变换
        /// </summary>
        public Color SampleLighting(Vector3 worldPos, Vector3 normal)
        {
            if (bakedData == null)
                return Color.black;

            // 变换坐标到 Volume 局部空间
            Vector3 localPos = TransformWorldToLocal(worldPos);

            // 变换法线到 Volume 局部空间
            Vector3 localNormal = TransformNormalToLocal(normal);

            return bakedData.SampleLighting(localPos, localNormal);
        }

        /// <summary>
        /// CPU 端采样球谐系数
        /// 支持 Transform 变换
        /// </summary>
        public SHCoefficients SampleSH(Vector3 worldPos)
        {
            if (bakedData == null)
                return new SHCoefficients();

            // 变换坐标到 Volume 局部空间
            Vector3 localPos = TransformWorldToLocal(worldPos);

            return bakedData.SampleSH(localPos);
        }

        /// <summary>
        /// 将世界坐标变换到 Volume 局部空间 (CPU 端)
        /// </summary>
        public Vector3 TransformWorldToLocal(Vector3 worldPos)
        {
            if (bakedData == null || transformMode == TransformMode.None)
                return worldPos;

            Matrix4x4 worldToLocal = GetWorldToVolumeMatrix();
            return worldToLocal.MultiplyPoint3x4(worldPos);
        }

        /// <summary>
        /// 将世界法线变换到 Volume 局部空间 (CPU 端)
        /// </summary>
        public Vector3 TransformNormalToLocal(Vector3 worldNormal)
        {
            if (transformMode == TransformMode.None)
                return worldNormal;

            Quaternion inverseRot = Quaternion.Inverse(GetEffectiveRotation());
            return (inverseRot * worldNormal).normalized;
        }

        /// <summary>
        /// 检查点是否在 Volume 内（支持 Transform）
        /// </summary>
        public bool ContainsPoint(Vector3 worldPos)
        {
            if (bakedData == null)
                return bounds.Contains(worldPos);

            // 变换坐标到 Volume 局部空间
            Vector3 localPos = TransformWorldToLocal(worldPos);

            // 检查是否在 baked 边界内
            Vector3 localToRoot = localPos - bakedData.rootCenter;
            return Mathf.Abs(localToRoot.x) <= bakedData.rootHalfExtents.x &&
                   Mathf.Abs(localToRoot.y) <= bakedData.rootHalfExtents.y &&
                   Mathf.Abs(localToRoot.z) <= bakedData.rootHalfExtents.z;
        }

        private void OnDrawGizmosSelected()
        {
            // 绘制 Volume 边界
            Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.3f);
            Gizmos.DrawCube(bounds.center, bounds.size);

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

        private void OnDrawGizmos()
        {
            // 始终绘制线框
            Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 在 Inspector 中点击 Auto Calculate Bounds 按钮时调用
        /// </summary>
        public void EditorAutoCalculateBounds()
        {
            bounds = CalculateAutoVolumeBounds();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
