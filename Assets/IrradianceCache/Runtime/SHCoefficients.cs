using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// L1 球谐系数结构 (4 个系数 × 3 通道 RGB)
    /// 使用 Half 精度存储以节省内存
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SHCoefficients
    {
        // L1 球谐系数: L0(DC) + L1xyz (3 个方向分量)
        // 每个通道 4 个系数
        public Vector4 shR;  // R 通道的 4 个系数
        public Vector4 shG;  // G 通道的 4 个系数
        public Vector4 shB;  // B 通道的 4 个系数

        /// <summary>
        /// 从 Unity SphericalHarmonicsL2 转换为 L1 系数
        /// </summary>
        public static SHCoefficients FromSphericalHarmonicsL2(UnityEngine.Rendering.SphericalHarmonicsL2 sh)
        {
            SHCoefficients result = new SHCoefficients();

            // Unity SH L2 索引:
            // [channel, coefficient]
            // L0: [c, 0]
            // L1: [c, 1], [c, 2], [c, 3]
            // L2: [c, 4], [c, 5], [c, 6], [c, 7], [c, 8]

            result.shR = new Vector4(
                sh[0, 0],  // L0
                sh[0, 1],  // L1x
                sh[0, 2],  // L1y
                sh[0, 3]   // L1z
            );

            result.shG = new Vector4(
                sh[1, 0],
                sh[1, 1],
                sh[1, 2],
                sh[1, 3]
            );

            result.shB = new Vector4(
                sh[2, 0],
                sh[2, 1],
                sh[2, 2],
                sh[2, 3]
            );

            return result;
        }

        /// <summary>
        /// 使用法线方向评估球谐光照
        /// </summary>
        public Vector3 Evaluate(Vector3 normal)
        {
            // L1 基函数常数
            // const float c0 = 0.282095f;  // L0: 1/(2*sqrt(pi))
            // const float c1 = 0.488603f;  // L1: sqrt(3)/(2*sqrt(pi))

            const float c0 = 0.886227f;  // L0: from Unity
            const float c1 = 1.055000f;  // L1: from Unity
            
            Vector4 basis = new Vector4(
                c0,
                c1 * normal.x,
                c1 * normal.y,
                c1 * normal.z
            );

            Vector3 result = new Vector3(
                Vector4.Dot(shR, basis),
                Vector4.Dot(shG, basis),
                Vector4.Dot(shB, basis)
            );

            // 确保结果非负
            result.x = Mathf.Max(0, result.x);
            result.y = Mathf.Max(0, result.y);
            result.z = Mathf.Max(0, result.z);

            return result;
        }

        /// <summary>
        /// 两个 SH 系数的线性插值
        /// </summary>
        public static SHCoefficients Lerp(SHCoefficients a, SHCoefficients b, float t)
        {
            SHCoefficients result = new SHCoefficients();
            result.shR = Vector4.Lerp(a.shR, b.shR, t);
            result.shG = Vector4.Lerp(a.shG, b.shG, t);
            result.shB = Vector4.Lerp(a.shB, b.shB, t);
            return result;
        }

        /// <summary>
        /// 获取结构体的字节大小 (用于 ComputeBuffer)
        /// </summary>
        public static int GetStride()
        {
            // 3 个 Vector4 = 3 × 16 bytes = 48 bytes
            return sizeof(float) * 12;
        }

        /// <summary>
        /// 旋转 L1 球谐系数
        /// L0 (DC项) 各向同性，不需要旋转
        /// L1 (方向分量) 与 3D 向量旋转相同
        /// </summary>
        /// <param name="rotation">旋转四元数</param>
        /// <returns>旋转后的 SH 系数</returns>
        public SHCoefficients Rotate(Quaternion rotation)
        {
            SHCoefficients result = new SHCoefficients();

            // L0 不变
            result.shR.x = shR.x;
            result.shG.x = shG.x;
            result.shB.x = shB.x;

            // L1 分量作为向量旋转
            // L1 = (L1x, L1y, L1z) 对应 shR/G/B 的 (y, z, w) 分量
            Vector3 l1R = new Vector3(shR.y, shR.z, shR.w);
            Vector3 l1G = new Vector3(shG.y, shG.z, shG.w);
            Vector3 l1B = new Vector3(shB.y, shB.z, shB.w);

            // 旋转 L1 向量
            l1R = rotation * l1R;
            l1G = rotation * l1G;
            l1B = rotation * l1B;

            result.shR = new Vector4(result.shR.x, l1R.x, l1R.y, l1R.z);
            result.shG = new Vector4(result.shG.x, l1G.x, l1G.y, l1G.z);
            result.shB = new Vector4(result.shB.x, l1B.x, l1B.y, l1B.z);

            return result;
        }

        /// <summary>
        /// 使用旋转后的法线评估球谐光照
        /// 这在数学上等价于旋转 SH 系数后用原始法线评估
        /// </summary>
        /// <param name="normal">世界空间法线</param>
        /// <param name="inverseRotation">Volume 旋转的逆</param>
        /// <returns>评估后的颜色</returns>
        public Vector3 EvaluateWithRotation(Vector3 normal, Quaternion inverseRotation)
        {
            // 将法线变换到 Volume 局部空间
            Vector3 localNormal = inverseRotation * normal;
            return Evaluate(localNormal);
        }
    }
}
