using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// OBB 障碍物组件，挂载在 GameObject 上定义有向包围盒障碍物。
    /// 跟随 GameObject 的 Transform（位置、旋转、缩放）自动计算世界空间 OBB 参数。
    /// </summary>
    [ExecuteInEditMode]
    public class ObstacleCubeComponent : MonoBehaviour
    {
        [Tooltip("OBB 局部空间各轴半尺寸")]
        public Vector3 halfExtents = Vector3.one * 0.5f;

        /// <summary>
        /// 导出当前 OBB 的 GPU 数据。
        /// center = transform.position
        /// axisX = transform.rotation * Vector3.right
        /// axisY = transform.rotation * Vector3.up
        /// halfExtents = Vector3.Scale(halfExtents, transform.lossyScale)
        /// </summary>
        public ObstacleOBBData GetOBBData()
        {
            return new ObstacleOBBData
            {
                center = transform.position,
                axisX = transform.rotation * Vector3.right,
                axisY = transform.rotation * Vector3.up,
                halfExtents = Vector3.Scale(halfExtents, transform.lossyScale)
            };
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            DrawOBBWireCube();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 1f);
            DrawOBBWireCube();
        }

        private void DrawOBBWireCube()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, halfExtents * 2f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
