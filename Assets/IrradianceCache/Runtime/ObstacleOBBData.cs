using System.Runtime.InteropServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// GPU 端 OBB 障碍物数据结构
    /// 存储世界空间中心、两个轴方向（Z 轴由 cross(X, Y) 推导）和各轴半尺寸
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ObstacleOBBData
    {
        /// <summary>
        /// 世界空间中心
        /// </summary>
        public Vector3 center;

        /// <summary>
        /// X 轴方向（归一化）
        /// </summary>
        public Vector3 axisX;

        /// <summary>
        /// Y 轴方向（归一化）
        /// </summary>
        public Vector3 axisY;

        /// <summary>
        /// 各轴半尺寸（已含 scale）
        /// </summary>
        public Vector3 halfExtents;

        /// <summary>
        /// 获取结构体的字节大小 (用于 ComputeBuffer)
        /// center: 12 bytes + axisX: 12 bytes + axisY: 12 bytes + halfExtents: 12 bytes = 48 bytes
        /// </summary>
        public static int GetStride()
        {
            return 48;
        }
    }
}
