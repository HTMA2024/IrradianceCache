using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// 障碍物几何算法静态工具类
    /// 提供 AABB-OBB 相交测试、Ray-OBB 相交测试、点在 OBB 内判定等方法
    /// </summary>
    public static class ObstacleGeometry
    {
        /// <summary>
        /// 使用分离轴定理（SAT）测试 AABB 与 OBB 是否相交。
        /// 测试 15 个潜在分离轴：3 个 AABB 轴 + 3 个 OBB 轴 + 9 个叉积轴。
        /// </summary>
        /// <param name="aabbCenter">AABB 中心点</param>
        /// <param name="aabbHalfExtents">AABB 各轴半尺寸</param>
        /// <param name="obb">OBB 数据</param>
        /// <returns>true 表示相交，false 表示分离</returns>
        public static bool AABBOBBIntersect(Vector3 aabbCenter, Vector3 aabbHalfExtents, ObstacleOBBData obb)
        {
            // OBB 三个轴方向
            Vector3 obbAxisX = obb.axisX;
            Vector3 obbAxisY = obb.axisY;
            Vector3 obbAxisZ = Vector3.Cross(obbAxisX, obbAxisY);

            // AABB 三个轴方向（世界空间轴对齐）
            Vector3 aabbAxisX = Vector3.right;   // (1, 0, 0)
            Vector3 aabbAxisY = Vector3.up;      // (0, 1, 0)
            Vector3 aabbAxisZ = Vector3.forward;  // (0, 0, 1)

            // 从 AABB 中心到 OBB 中心的向量
            Vector3 t = obb.center - aabbCenter;

            // 预计算 OBB 轴在 AABB 轴上的投影绝对值矩阵（3x3）
            // absC[i,j] = |dot(aabbAxis[i], obbAxis[j])|
            // 由于 AABB 轴是标准基向量，absC[i,j] = |obbAxis[j][i]|
            float absC00 = Mathf.Abs(obbAxisX.x);
            float absC01 = Mathf.Abs(obbAxisY.x);
            float absC02 = Mathf.Abs(obbAxisZ.x);
            float absC10 = Mathf.Abs(obbAxisX.y);
            float absC11 = Mathf.Abs(obbAxisY.y);
            float absC12 = Mathf.Abs(obbAxisZ.y);
            float absC20 = Mathf.Abs(obbAxisX.z);
            float absC21 = Mathf.Abs(obbAxisY.z);
            float absC22 = Mathf.Abs(obbAxisZ.z);

            // 添加小 epsilon 防止叉积轴退化时的浮点误差
            const float epsilon = 1e-6f;
            float absC00e = absC00 + epsilon;
            float absC01e = absC01 + epsilon;
            float absC02e = absC02 + epsilon;
            float absC10e = absC10 + epsilon;
            float absC11e = absC11 + epsilon;
            float absC12e = absC12 + epsilon;
            float absC20e = absC20 + epsilon;
            float absC21e = absC21 + epsilon;
            float absC22e = absC22 + epsilon;

            float ra, rb;

            // --- 测试 3 个 AABB 轴 ---

            // 轴 A0 = (1, 0, 0)
            ra = aabbHalfExtents.x;
            rb = obb.halfExtents.x * absC00e + obb.halfExtents.y * absC01e + obb.halfExtents.z * absC02e;
            if (Mathf.Abs(t.x) > ra + rb) return false;

            // 轴 A1 = (0, 1, 0)
            ra = aabbHalfExtents.y;
            rb = obb.halfExtents.x * absC10e + obb.halfExtents.y * absC11e + obb.halfExtents.z * absC12e;
            if (Mathf.Abs(t.y) > ra + rb) return false;

            // 轴 A2 = (0, 0, 1)
            ra = aabbHalfExtents.z;
            rb = obb.halfExtents.x * absC20e + obb.halfExtents.y * absC21e + obb.halfExtents.z * absC22e;
            if (Mathf.Abs(t.z) > ra + rb) return false;

            // --- 测试 3 个 OBB 轴 ---

            // 轴 B0 = obbAxisX
            ra = aabbHalfExtents.x * absC00e + aabbHalfExtents.y * absC10e + aabbHalfExtents.z * absC20e;
            rb = obb.halfExtents.x;
            if (Mathf.Abs(Vector3.Dot(t, obbAxisX)) > ra + rb) return false;

            // 轴 B1 = obbAxisY
            ra = aabbHalfExtents.x * absC01e + aabbHalfExtents.y * absC11e + aabbHalfExtents.z * absC21e;
            rb = obb.halfExtents.y;
            if (Mathf.Abs(Vector3.Dot(t, obbAxisY)) > ra + rb) return false;

            // 轴 B2 = obbAxisZ
            ra = aabbHalfExtents.x * absC02e + aabbHalfExtents.y * absC12e + aabbHalfExtents.z * absC22e;
            rb = obb.halfExtents.z;
            if (Mathf.Abs(Vector3.Dot(t, obbAxisZ)) > ra + rb) return false;

            // --- 测试 9 个叉积轴 ---
            // 叉积轴 = AABB轴[i] × OBB轴[j]
            // 由于 AABB 轴是标准基向量，叉积可以简化

            // c[i,j] = dot(aabbAxis[i], obbAxis[j])（带符号版本）
            float c00 = obbAxisX.x;
            float c01 = obbAxisY.x;
            float c02 = obbAxisZ.x;
            float c10 = obbAxisX.y;
            float c11 = obbAxisY.y;
            float c12 = obbAxisZ.y;
            float c20 = obbAxisX.z;
            float c21 = obbAxisY.z;
            float c22 = obbAxisZ.z;

            // A0 × B0 = (1,0,0) × obbAxisX = (0, -obbAxisX.z, obbAxisX.y) = (0, -c20, c10)
            ra = aabbHalfExtents.y * absC20e + aabbHalfExtents.z * absC10e;
            rb = obb.halfExtents.y * absC02e + obb.halfExtents.z * absC01e;
            if (Mathf.Abs(t.z * c10 - t.y * c20) > ra + rb) return false;

            // A0 × B1 = (1,0,0) × obbAxisY = (0, -obbAxisY.z, obbAxisY.y) = (0, -c21, c11)
            ra = aabbHalfExtents.y * absC21e + aabbHalfExtents.z * absC11e;
            rb = obb.halfExtents.x * absC02e + obb.halfExtents.z * absC00e;
            if (Mathf.Abs(t.z * c11 - t.y * c21) > ra + rb) return false;

            // A0 × B2 = (1,0,0) × obbAxisZ = (0, -obbAxisZ.z, obbAxisZ.y) = (0, -c22, c12)
            ra = aabbHalfExtents.y * absC22e + aabbHalfExtents.z * absC12e;
            rb = obb.halfExtents.x * absC01e + obb.halfExtents.y * absC00e;
            if (Mathf.Abs(t.z * c12 - t.y * c22) > ra + rb) return false;

            // A1 × B0 = (0,1,0) × obbAxisX = (obbAxisX.z, 0, -obbAxisX.x) = (c20, 0, -c00)
            ra = aabbHalfExtents.x * absC20e + aabbHalfExtents.z * absC00e;
            rb = obb.halfExtents.y * absC12e + obb.halfExtents.z * absC11e;
            if (Mathf.Abs(t.x * c20 - t.z * c00) > ra + rb) return false;

            // A1 × B1 = (0,1,0) × obbAxisY = (obbAxisY.z, 0, -obbAxisY.x) = (c21, 0, -c01)
            ra = aabbHalfExtents.x * absC21e + aabbHalfExtents.z * absC01e;
            rb = obb.halfExtents.x * absC12e + obb.halfExtents.z * absC10e;
            if (Mathf.Abs(t.x * c21 - t.z * c01) > ra + rb) return false;

            // A1 × B2 = (0,1,0) × obbAxisZ = (obbAxisZ.z, 0, -obbAxisZ.x) = (c22, 0, -c02)
            ra = aabbHalfExtents.x * absC22e + aabbHalfExtents.z * absC02e;
            rb = obb.halfExtents.x * absC11e + obb.halfExtents.y * absC10e;
            if (Mathf.Abs(t.x * c22 - t.z * c02) > ra + rb) return false;

            // A2 × B0 = (0,0,1) × obbAxisX = (-obbAxisX.y, obbAxisX.x, 0) = (-c10, c00, 0)
            ra = aabbHalfExtents.x * absC10e + aabbHalfExtents.y * absC00e;
            rb = obb.halfExtents.y * absC22e + obb.halfExtents.z * absC21e;
            if (Mathf.Abs(t.y * c00 - t.x * c10) > ra + rb) return false;

            // A2 × B1 = (0,0,1) × obbAxisY = (-obbAxisY.y, obbAxisY.x, 0) = (-c11, c01, 0)
            ra = aabbHalfExtents.x * absC11e + aabbHalfExtents.y * absC01e;
            rb = obb.halfExtents.x * absC22e + obb.halfExtents.z * absC20e;
            if (Mathf.Abs(t.y * c01 - t.x * c11) > ra + rb) return false;

            // A2 × B2 = (0,0,1) × obbAxisZ = (-obbAxisZ.y, obbAxisZ.x, 0) = (-c12, c02, 0)
            ra = aabbHalfExtents.x * absC12e + aabbHalfExtents.y * absC02e;
            rb = obb.halfExtents.x * absC21e + obb.halfExtents.y * absC20e;
            if (Mathf.Abs(t.y * c02 - t.x * c12) > ra + rb) return false;

            // 所有 15 个轴都没有分离，因此相交
            return true;
        }

        /// <summary>
        /// 解析式 Ray-OBB 相交测试。
        /// 将射线变换到 OBB 局部空间后使用 slab method 与轴对齐包围盒求交。
        /// </summary>
        /// <param name="origin">射线原点（世界空间）</param>
        /// <param name="dir">射线方向（世界空间，无需归一化）</param>
        /// <param name="obb">OBB 数据</param>
        /// <param name="tMin">输出：射线进入 OBB 的参数值</param>
        /// <param name="tMax">输出：射线离开 OBB 的参数值</param>
        /// <returns>true 表示射线与 OBB 相交（tMax >= max(tMin, 0)）</returns>
        public static bool RayOBBIntersect(Vector3 origin, Vector3 dir, ObstacleOBBData obb, out float tMin, out float tMax)
        {
            // 将射线变换到 OBB 局部空间
            Vector3 localOrigin = origin - obb.center;
            Vector3 axisZ = Vector3.Cross(obb.axisX, obb.axisY);

            // 投影到 OBB 局部坐标系
            Vector3 o = new Vector3(
                Vector3.Dot(localOrigin, obb.axisX),
                Vector3.Dot(localOrigin, obb.axisY),
                Vector3.Dot(localOrigin, axisZ)
            );
            Vector3 d = new Vector3(
                Vector3.Dot(dir, obb.axisX),
                Vector3.Dot(dir, obb.axisY),
                Vector3.Dot(dir, axisZ)
            );

            // Slab method 与 AABB [-halfExtents, +halfExtents] 求交
            float invDx = 1.0f / d.x;
            float invDy = 1.0f / d.y;
            float invDz = 1.0f / d.z;

            float t0x = (-obb.halfExtents.x - o.x) * invDx;
            float t1x = ( obb.halfExtents.x - o.x) * invDx;
            float t0y = (-obb.halfExtents.y - o.y) * invDy;
            float t1y = ( obb.halfExtents.y - o.y) * invDy;
            float t0z = (-obb.halfExtents.z - o.z) * invDz;
            float t1z = ( obb.halfExtents.z - o.z) * invDz;

            float tNearX = Mathf.Min(t0x, t1x);
            float tFarX  = Mathf.Max(t0x, t1x);
            float tNearY = Mathf.Min(t0y, t1y);
            float tFarY  = Mathf.Max(t0y, t1y);
            float tNearZ = Mathf.Min(t0z, t1z);
            float tFarZ  = Mathf.Max(t0z, t1z);

            tMin = Mathf.Max(Mathf.Max(tNearX, tNearY), tNearZ);
            tMax = Mathf.Min(Mathf.Min(tFarX, tFarY), tFarZ);

            return tMax >= Mathf.Max(tMin, 0.0f);
        }

        /// <summary>
        /// 判断点是否在 OBB 内部。
        /// 将点变换到 OBB 局部空间，检查各轴投影是否在 [-halfExtents, +halfExtents] 范围内。
        /// </summary>
        /// <param name="point">待检测点（世界空间）</param>
        /// <param name="obb">OBB 数据</param>
        /// <returns>true 表示点在 OBB 内部</returns>
        public static bool PointInsideOBB(Vector3 point, ObstacleOBBData obb)
        {
            Vector3 localPoint = point - obb.center;
            Vector3 axisZ = Vector3.Cross(obb.axisX, obb.axisY);

            float projX = Vector3.Dot(localPoint, obb.axisX);
            float projY = Vector3.Dot(localPoint, obb.axisY);
            float projZ = Vector3.Dot(localPoint, axisZ);

            return Mathf.Abs(projX) <= obb.halfExtents.x
                && Mathf.Abs(projY) <= obb.halfExtents.y
                && Mathf.Abs(projZ) <= obb.halfExtents.z;
        }

        /// <summary>
        /// 计算遮挡权重。检查 probe 原点是否在关联 OBB 内部，或从 probe 到采样点的射线是否被 OBB 阻挡。
        /// </summary>
        /// <param name="probeOrigin">Probe 原点（世界空间）</param>
        /// <param name="samplePos">采样点（世界空间）</param>
        /// <param name="obstacles">所有 OBB 障碍物数据</param>
        /// <param name="obstacleIndices">该节点关联的 OBB 索引（长度 4，255 = 无效）</param>
        /// <returns>1.0f 表示未被遮挡，0.0f 表示被遮挡</returns>
        public static float GetObstacleWeight(Vector3 probeOrigin, Vector3 samplePos, ObstacleOBBData[] obstacles, int[] obstacleIndices)
        {
            Vector3 rayDir = samplePos - probeOrigin;
            float rayLength = rayDir.magnitude;
            rayDir /= Mathf.Max(rayLength, 0.0001f);

            for (int i = 0; i < obstacleIndices.Length; i++)
            {
                int idx = obstacleIndices[i];
                if (idx >= 255 || idx >= obstacles.Length) continue;

                ObstacleOBBData obb = obstacles[idx];

                // 如果 probe 在 OBB 内部，该 Probe 贡献归零
                if (PointInsideOBB(probeOrigin, obb))
                    return 0.0f;

                // 检查射线是否在到达采样点之前击中 OBB
                float tMin, tMax;
                if (RayOBBIntersect(probeOrigin, rayDir, obb, out tMin, out tMax))
                {
                    // tMin < 0 表示射线起点在 OBB 内部（已由 PointInsideOBB 处理）
                    // 使用 max(tMin, 0) 作为实际进入点，只要进入点在采样点之前就遮挡
                    float tEntry = Mathf.Max(tMin, 0.0f);
                    if (tEntry < rayLength)
                        return 0.0f;
                }
            }

            return 1.0f; // 未被遮挡
        }


    }
}
