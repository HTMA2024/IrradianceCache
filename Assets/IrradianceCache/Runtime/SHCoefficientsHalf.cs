using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// L1 球谐系数结构 - Half 精度版本 (内存优化)
    /// 使用 uint 打包两个 half 精度浮点数，与 GPU 端格式匹配
    /// 每个通道 4 个系数，共 3 通道 = 6 个 uint = 24 bytes
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SHCoefficientsHalf
    {
        // R 通道: L0+L1x 打包, L1y+L1z 打包
        public uint shR01;
        public uint shR23;
        // G 通道
        public uint shG01;
        public uint shG23;
        // B 通道
        public uint shB01;
        public uint shB23;

        /// <summary>
        /// 从 float 转换为 half (IEEE 754 半精度)
        /// </summary>
        public static ushort FloatToHalf(float value)
        {
            return Mathf.FloatToHalf(value);
        }

        /// <summary>
        /// 从 half 转换为 float
        /// </summary>
        public static float HalfToFloat(ushort value)
        {
            return Mathf.HalfToFloat(value);
        }

        /// <summary>
        /// 将两个 half 打包为一个 uint
        /// </summary>
        public static uint PackHalf2(float a, float b)
        {
            ushort ha = FloatToHalf(a);
            ushort hb = FloatToHalf(b);
            return (uint)ha | ((uint)hb << 16);
        }

        /// <summary>
        /// 从 uint 解包两个 half
        /// </summary>
        public static void UnpackHalf2(uint packed, out float a, out float b)
        {
            a = HalfToFloat((ushort)(packed & 0xFFFF));
            b = HalfToFloat((ushort)(packed >> 16));
        }

        /// <summary>
        /// 从 SHCoefficients (float) 转换为 SHCoefficientsHalf
        /// </summary>
        public static SHCoefficientsHalf FromSHCoefficients(SHCoefficients sh)
        {
            SHCoefficientsHalf result = new SHCoefficientsHalf();

            result.shR01 = PackHalf2(sh.shR.x, sh.shR.y);
            result.shR23 = PackHalf2(sh.shR.z, sh.shR.w);

            result.shG01 = PackHalf2(sh.shG.x, sh.shG.y);
            result.shG23 = PackHalf2(sh.shG.z, sh.shG.w);

            result.shB01 = PackHalf2(sh.shB.x, sh.shB.y);
            result.shB23 = PackHalf2(sh.shB.z, sh.shB.w);

            return result;
        }

        /// <summary>
        /// 转换为 SHCoefficients (float)
        /// </summary>
        public SHCoefficients ToSHCoefficients()
        {
            SHCoefficients result = new SHCoefficients();

            UnpackHalf2(shR01, out float r0, out float r1);
            UnpackHalf2(shR23, out float r2, out float r3);
            result.shR = new Vector4(r0, r1, r2, r3);

            UnpackHalf2(shG01, out float g0, out float g1);
            UnpackHalf2(shG23, out float g2, out float g3);
            result.shG = new Vector4(g0, g1, g2, g3);

            UnpackHalf2(shB01, out float b0, out float b1);
            UnpackHalf2(shB23, out float b2, out float b3);
            result.shB = new Vector4(b0, b1, b2, b3);

            return result;
        }

        /// <summary>
        /// 从 Unity SphericalHarmonicsL2 转换为 L1 系数 (Half 精度)
        /// </summary>
        public static SHCoefficientsHalf FromSphericalHarmonicsL2(UnityEngine.Rendering.SphericalHarmonicsL2 sh)
        {
            SHCoefficientsHalf result = new SHCoefficientsHalf();

            result.shR01 = PackHalf2(sh[0, 0], sh[0, 1]);
            result.shR23 = PackHalf2(sh[0, 2], sh[0, 3]);

            result.shG01 = PackHalf2(sh[1, 0], sh[1, 1]);
            result.shG23 = PackHalf2(sh[1, 2], sh[1, 3]);

            result.shB01 = PackHalf2(sh[2, 0], sh[2, 1]);
            result.shB23 = PackHalf2(sh[2, 2], sh[2, 3]);

            return result;
        }

        /// <summary>
        /// 使用法线方向评估球谐光照
        /// </summary>
        public Vector3 Evaluate(Vector3 normal)
        {
            SHCoefficients sh = ToSHCoefficients();
            return sh.Evaluate(normal);
        }

        /// <summary>
        /// 两个 SH 系数的线性插值
        /// </summary>
        public static SHCoefficientsHalf Lerp(SHCoefficientsHalf a, SHCoefficientsHalf b, float t)
        {
            SHCoefficients aFloat = a.ToSHCoefficients();
            SHCoefficients bFloat = b.ToSHCoefficients();
            SHCoefficients result = SHCoefficients.Lerp(aFloat, bFloat, t);
            return FromSHCoefficients(result);
        }

        /// <summary>
        /// 获取结构体的字节大小 (用于 ComputeBuffer)
        /// </summary>
        public static int GetStride()
        {
            // 6 个 uint = 6 × 4 bytes = 24 bytes
            return sizeof(uint) * 6;
        }
    }
}
