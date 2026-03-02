using UnityEngine;

namespace IrradianceCacheSystem
{
    /// <summary>
    /// GPU 采样器
    /// 使用 Compute Shader 在 GPU 上进行批量采样
    /// </summary>
    public class IrradianceCacheGPUSampler : System.IDisposable
    {
        private ComputeShader computeShader;
        private int kernelSingle;
        private int kernelBatch;

        private ComputeBuffer positionsBuffer;
        private ComputeBuffer normalsBuffer;
        private ComputeBuffer resultsBuffer;

        private int currentBufferSize = 0;
        private bool isInitialized = false;

        // Shader 属性 ID
        private static readonly int ResultsID = Shader.PropertyToID("_Results");
        private static readonly int SamplePositionsID = Shader.PropertyToID("_SamplePositions");
        private static readonly int SampleNormalsID = Shader.PropertyToID("_SampleNormals");
        private static readonly int SampleCountID = Shader.PropertyToID("_SampleCount");

        /// <summary>
        /// 初始化 GPU 采样器
        /// </summary>
        /// <param name="shader">IrradianceCacheSampler Compute Shader</param>
        public IrradianceCacheGPUSampler(ComputeShader shader)
        {
            if (shader == null)
            {
                Debug.LogError("Compute shader is null. GPU sampling will not work.");
                return;
            }

            computeShader = shader;
            kernelSingle = computeShader.FindKernel("CSMain");
            kernelBatch = computeShader.FindKernel("CSBatchSample");
            isInitialized = true;
        }

        /// <summary>
        /// 确保缓冲区大小足够
        /// </summary>
        private void EnsureBufferSize(int size)
        {
            if (size <= currentBufferSize && positionsBuffer != null)
                return;

            // 释放旧缓冲区
            ReleaseBuffers();

            // 创建新缓冲区
            currentBufferSize = Mathf.Max(size, 64); // 最小 64
            positionsBuffer = new ComputeBuffer(currentBufferSize, sizeof(float) * 4);
            normalsBuffer = new ComputeBuffer(currentBufferSize, sizeof(float) * 4);
            resultsBuffer = new ComputeBuffer(currentBufferSize, sizeof(float) * 4);
        }

        /// <summary>
        /// 释放缓冲区
        /// </summary>
        private void ReleaseBuffers()
        {
            if (positionsBuffer != null)
            {
                positionsBuffer.Release();
                positionsBuffer = null;
            }
            if (normalsBuffer != null)
            {
                normalsBuffer.Release();
                normalsBuffer = null;
            }
            if (resultsBuffer != null)
            {
                resultsBuffer.Release();
                resultsBuffer = null;
            }
            currentBufferSize = 0;
        }

        /// <summary>
        /// 单点采样
        /// </summary>
        public SampleResult Sample(Vector3 position, Vector3 normal)
        {
            if (!isInitialized)
                return new SampleResult();

            EnsureBufferSize(1);

            // 上传数据
            Vector4[] posData = new Vector4[] { new Vector4(position.x, position.y, position.z, 0) };
            Vector4[] normData = new Vector4[] { new Vector4(normal.x, normal.y, normal.z, 0) };

            positionsBuffer.SetData(posData);
            normalsBuffer.SetData(normData);

            // 设置 Compute Shader 参数
            computeShader.SetBuffer(kernelSingle, SamplePositionsID, positionsBuffer);
            computeShader.SetBuffer(kernelSingle, SampleNormalsID, normalsBuffer);
            computeShader.SetBuffer(kernelSingle, ResultsID, resultsBuffer);

            // 执行
            computeShader.Dispatch(kernelSingle, 1, 1, 1);

            // 读取结果
            Vector4[] results = new Vector4[1];
            resultsBuffer.GetData(results);

            return new SampleResult
            {
                color = new Color(results[0].x, results[0].y, results[0].z, 1f),
                nodeIndex = (int)results[0].w
            };
        }

        /// <summary>
        /// 批量采样
        /// </summary>
        public SampleResult[] SampleBatch(Vector3[] positions, Vector3[] normals)
        {
            if (!isInitialized || positions == null || normals == null)
                return new SampleResult[0];

            int count = Mathf.Min(positions.Length, normals.Length);
            if (count == 0)
                return new SampleResult[0];

            EnsureBufferSize(count);

            // 准备数据
            Vector4[] posData = new Vector4[count];
            Vector4[] normData = new Vector4[count];

            for (int i = 0; i < count; i++)
            {
                posData[i] = new Vector4(positions[i].x, positions[i].y, positions[i].z, 0);
                normData[i] = new Vector4(normals[i].x, normals[i].y, normals[i].z, 0);
            }

            positionsBuffer.SetData(posData);
            normalsBuffer.SetData(normData);

            // 设置 Compute Shader 参数
            computeShader.SetBuffer(kernelBatch, SamplePositionsID, positionsBuffer);
            computeShader.SetBuffer(kernelBatch, SampleNormalsID, normalsBuffer);
            computeShader.SetBuffer(kernelBatch, ResultsID, resultsBuffer);
            computeShader.SetInt(SampleCountID, count);

            // 执行 (64 线程一组)
            int threadGroups = Mathf.CeilToInt(count / 64f);
            computeShader.Dispatch(kernelBatch, threadGroups, 1, 1);

            // 读取结果
            Vector4[] rawResults = new Vector4[count];
            resultsBuffer.GetData(rawResults);

            SampleResult[] results = new SampleResult[count];
            for (int i = 0; i < count; i++)
            {
                results[i] = new SampleResult
                {
                    color = new Color(rawResults[i].x, rawResults[i].y, rawResults[i].z, 1f),
                    nodeIndex = (int)rawResults[i].w
                };
            }

            return results;
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            ReleaseBuffers();
            isInitialized = false;
        }

        /// <summary>
        /// 采样结果结构
        /// </summary>
        public struct SampleResult
        {
            public Color color;
            public int nodeIndex;
        }
    }
}
