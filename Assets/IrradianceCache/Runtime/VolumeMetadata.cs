using System.Runtime.InteropServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// Volume 元数据结构 - 用于多 Volume 管理
    /// GPU 兼容的内存布局，用于 StructuredBuffer
    ///
    /// 内存布局 (208 bytes):
    /// - nodeStartIndex: 4 bytes
    /// - nodeCount: 4 bytes
    /// - maxDepth: 4 bytes
    /// - priority: 4 bytes
    /// - rootCenter: 12 bytes (Vector3)
    /// - rootHalfExtents: 12 bytes (Vector3)
    /// - worldToLocal: 64 bytes (Matrix4x4)
    /// - rotationMatrix: 64 bytes (Matrix4x4)
    /// - blendDistance: 4 bytes
    /// - isActive: 4 bytes
    /// - useCompactFormat: 4 bytes
    /// - _padding: 4 bytes
    /// - useTransitionBlending: 4 bytes
    /// - transitionWidthRatio: 4 bytes
    /// - gridResolutionX: 4 bytes
    /// - gridResolutionY: 4 bytes
    /// - gridResolutionZ: 4 bytes
    /// - _padding2: 4 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VolumeMetadata
    {
        /// <summary>
        /// 在合并 buffer 中的节点起始索引
        /// </summary>
        public int nodeStartIndex;

        /// <summary>
        /// 该 Volume 的节点数量
        /// </summary>
        public int nodeCount;

        /// <summary>
        /// 八叉树最大深度
        /// </summary>
        public int maxDepth;

        /// <summary>
        /// 优先级 (0-100, 越高越优先)
        /// </summary>
        public int priority;

        /// <summary>
        /// Volume 根节点中心 (baked 坐标)
        /// </summary>
        public Vector3 rootCenter;

        /// <summary>
        /// Volume 根节点各轴半尺寸
        /// </summary>
        public Vector3 rootHalfExtents;

        /// <summary>
        /// World 到 Volume 局部空间的变换矩阵
        /// </summary>
        public Matrix4x4 worldToLocal;

        /// <summary>
        /// 旋转矩阵的逆（用于变换法线）
        /// </summary>
        public Matrix4x4 rotationMatrix;

        /// <summary>
        /// 边界混合距离
        /// </summary>
        public float blendDistance;

        /// <summary>
        /// 是否激活 (0 = 禁用, 1 = 激活)
        /// </summary>
        public int isActive;

        /// <summary>
        /// 是否使用压缩格式 (0 = Full, 1 = Compact)
        /// </summary>
        public int useCompactFormat;

        /// <summary>
        /// 对齐填充
        /// </summary>
        public int _padding;

        /// <summary>
        /// 使用深度混合
        /// </summary>
        public int useTransitionBlending;

        /// <summary>
        /// 深度混合距離
        /// </summary>
        public float transitionWidthRatio;

        /// <summary>
        /// 网格分辨率 X (UniformGrid 模式, Octree 模式为 0)
        /// </summary>
        public int gridResolutionX;

        /// <summary>
        /// 网格分辨率 Y (UniformGrid 模式, Octree 模式为 0)
        /// </summary>
        public int gridResolutionY;

        /// <summary>
        /// 网格分辨率 Z (UniformGrid 模式, Octree 模式为 0)
        /// </summary>
        public int gridResolutionZ;

        /// <summary>
        /// 对齐填充 (16 字节对齐)
        /// </summary>
        public int _padding2;
        
        /// <summary>
        /// 获取结构体的字节大小 (用于 ComputeBuffer)
        /// </summary>
        public static int GetStride()
        {
            return Marshal.SizeOf<VolumeMetadata>();
        }

        /// <summary>
        /// 从 IrradianceCache 创建 VolumeMetadata
        /// </summary>
        public static VolumeMetadata FromVolume(IrradianceCache volume, int nodeStartIndex)
        {
            VolumeMetadata metadata = new VolumeMetadata();

            if (volume == null || volume.bakedData == null)
            {
                metadata.isActive = 0;
                return metadata;
            }

            metadata.nodeStartIndex = nodeStartIndex;
            metadata.nodeCount = volume.bakedData.nodes.Length;
            metadata.maxDepth = volume.bakedData.maxDepth;
            metadata.priority = volume.priority;

            metadata.rootCenter = volume.bakedData.rootCenter;
            metadata.rootHalfExtents = volume.bakedData.rootHalfExtents;

            metadata.worldToLocal = volume.GetWorldToVolumeMatrix();
            metadata.rotationMatrix = Matrix4x4.Rotate(Quaternion.Inverse(volume.GetEffectiveRotation()));

            metadata.blendDistance = volume.blendDistance;
            metadata.isActive = 1;
            metadata.useCompactFormat = volume.useCompactFormat ? 1 : 0;
            metadata._padding = 0;

            metadata.useTransitionBlending = volume.enableTransitionBlending ? 1 : 0;
            metadata.transitionWidthRatio = volume.transitionWidthRatio;

            if (volume.bakedData.storageMode == VolumeStorageMode.UniformGrid)
            {
                metadata.gridResolutionX = volume.bakedData.gridResolution.x;
                metadata.gridResolutionY = volume.bakedData.gridResolution.y;
                metadata.gridResolutionZ = volume.bakedData.gridResolution.z;
            }
            else
            {
                metadata.gridResolutionX = 0;
                metadata.gridResolutionY = 0;
                metadata.gridResolutionZ = 0;
            }
            metadata._padding2 = 0;

            return metadata;
        }

        /// <summary>
        /// 创建一个空的/禁用的 VolumeMetadata
        /// </summary>
        public static VolumeMetadata CreateEmpty()
        {
            return new VolumeMetadata
            {
                nodeStartIndex = 0,
                nodeCount = 0,
                maxDepth = 0,
                priority = 0,
                rootCenter = Vector3.zero,
                rootHalfExtents = Vector3.zero,
                worldToLocal = Matrix4x4.identity,
                rotationMatrix = Matrix4x4.identity,
                blendDistance = 0,
                isActive = 0,
                useCompactFormat = 0,
                _padding = 0,
                useTransitionBlending = 0,
                transitionWidthRatio = 0,
                gridResolutionX = 0,
                gridResolutionY = 0,
                gridResolutionZ = 0,
                _padding2 = 0
            };
        }
    }
}
