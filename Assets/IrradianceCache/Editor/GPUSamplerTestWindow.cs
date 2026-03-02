using UnityEngine;
using UnityEditor;

namespace IrradianceCacheSystem.Editor
{
    /// <summary>
    /// GPU 采样测试窗口
    /// 用于验证 GPU 端查询与 CPU 端查询的一致性
    /// </summary>
    public class GPUSamplerTestWindow : EditorWindow
    {
        private IrradianceCache targetVolume;
        private ComputeShader samplerShader;

        private Vector3 testPosition = Vector3.zero;
        private Vector3 testNormal = Vector3.up;

        private Color cpuResult = Color.black;
        private Color gpuResult = Color.black;
        private int cpuNodeIndex = -1;
        private int gpuNodeIndex = -1;

        private bool showBatchTest = false;
        private int batchTestCount = 100;
        private float maxDifference = 0f;
        private float avgDifference = 0f;

        [MenuItem("Window/Irradiance Cache/GPU Sampler Test")]
        public static void ShowWindow()
        {
            GPUSamplerTestWindow window = GetWindow<GPUSamplerTestWindow>("GPU Sampler Test");
            window.minSize = new Vector2(350, 400);
        }

        private void OnEnable()
        {
            // 尝试找到 Compute Shader
            string[] guids = AssetDatabase.FindAssets("IrradianceCacheSampler t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                samplerShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GPU Sampler Test", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Target Volume
            targetVolume = (IrradianceCache)EditorGUILayout.ObjectField(
                "Target Volume",
                targetVolume,
                typeof(IrradianceCache),
                true
            );

            if (targetVolume == null)
            {
                EditorGUILayout.HelpBox("Please select an IrradianceCache.", MessageType.Info);

                if (GUILayout.Button("Find in Scene"))
                {
                    targetVolume = FindObjectOfType<IrradianceCache>();
                }
                return;
            }

            if (targetVolume.bakedData == null)
            {
                EditorGUILayout.HelpBox("The target volume has no baked data.", MessageType.Warning);
                return;
            }

            // Compute Shader
            samplerShader = (ComputeShader)EditorGUILayout.ObjectField(
                "Sampler Shader",
                samplerShader,
                typeof(ComputeShader),
                false
            );

            EditorGUILayout.Space();

            // GPU Upload Status
            EditorGUILayout.LabelField("GPU Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Data Uploaded", targetVolume.IsGPUDataUploaded ? "Yes" : "No");

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Upload to GPU"))
            {
                targetVolume.UploadToGPU();
            }
            if (GUILayout.Button("Release GPU"))
            {
                targetVolume.ReleaseGPUResources();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Single Point Test
            EditorGUILayout.LabelField("Single Point Test", EditorStyles.boldLabel);
            testPosition = EditorGUILayout.Vector3Field("Position", testPosition);
            testNormal = EditorGUILayout.Vector3Field("Normal", testNormal);

            if (GUILayout.Button("Sample"))
            {
                PerformSingleSample();
            }

            // Results
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("CPU Result:");
            EditorGUILayout.ColorField(cpuResult);
            EditorGUILayout.LabelField($"Node: {cpuNodeIndex}");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("GPU Result:");
            EditorGUILayout.ColorField(gpuResult);
            EditorGUILayout.LabelField($"Node: {gpuNodeIndex}");
            EditorGUILayout.EndHorizontal();

            // Difference
            Color diff = new Color(
                Mathf.Abs(cpuResult.r - gpuResult.r),
                Mathf.Abs(cpuResult.g - gpuResult.g),
                Mathf.Abs(cpuResult.b - gpuResult.b),
                1f
            );
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Difference:");
            EditorGUILayout.ColorField(diff);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Batch Test
            showBatchTest = EditorGUILayout.Foldout(showBatchTest, "Batch Test", true);
            if (showBatchTest)
            {
                EditorGUI.indentLevel++;
                batchTestCount = EditorGUILayout.IntSlider("Sample Count", batchTestCount, 10, 1000);

                if (GUILayout.Button("Run Batch Test"))
                {
                    PerformBatchTest();
                }

                EditorGUILayout.LabelField("Max Difference", maxDifference.ToString("F6"));
                EditorGUILayout.LabelField("Avg Difference", avgDifference.ToString("F6"));

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // Quick Actions
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("Use Scene View Camera Position"))
            {
                if (SceneView.lastActiveSceneView != null)
                {
                    testPosition = SceneView.lastActiveSceneView.camera.transform.position;
                }
            }

            if (GUILayout.Button("Random Position in Volume"))
            {
                IrradianceCacheData data = targetVolume.bakedData;
                testPosition = data.rootCenter + new Vector3(
                    Random.Range(-1f, 1f) * data.rootHalfExtents.x,
                    Random.Range(-1f, 1f) * data.rootHalfExtents.y,
                    Random.Range(-1f, 1f) * data.rootHalfExtents.z
                );
                testNormal = Random.onUnitSphere;
            }
        }

        private void PerformSingleSample()
        {
            if (targetVolume == null || targetVolume.bakedData == null)
                return;

            // CPU 采样
            cpuResult = targetVolume.bakedData.SampleLighting(testPosition, testNormal.normalized);
            cpuNodeIndex = targetVolume.bakedData.QueryLeafNode(testPosition);

            // GPU 采样
            if (samplerShader != null && targetVolume.IsGPUDataUploaded)
            {
                using (var sampler = new IrradianceCacheGPUSampler(samplerShader))
                {
                    var result = sampler.Sample(testPosition, testNormal.normalized);
                    gpuResult = result.color;
                    gpuNodeIndex = result.nodeIndex;
                }
            }
            else
            {
                gpuResult = Color.black;
                gpuNodeIndex = -1;

                if (!targetVolume.IsGPUDataUploaded)
                {
                    Debug.LogWarning("GPU data not uploaded. Click 'Upload to GPU' first.");
                }
            }
        }

        private void PerformBatchTest()
        {
            if (targetVolume == null || targetVolume.bakedData == null)
                return;

            if (samplerShader == null || !targetVolume.IsGPUDataUploaded)
            {
                Debug.LogWarning("GPU sampler not available. Make sure shader is assigned and data is uploaded.");
                return;
            }

            IrradianceCacheData data = targetVolume.bakedData;

            // 生成随机测试点
            Vector3[] positions = new Vector3[batchTestCount];
            Vector3[] normals = new Vector3[batchTestCount];

            Random.InitState(42);
            for (int i = 0; i < batchTestCount; i++)
            {
                positions[i] = data.rootCenter + new Vector3(
                    Random.Range(-1f, 1f) * data.rootHalfExtents.x,
                    Random.Range(-1f, 1f) * data.rootHalfExtents.y,
                    Random.Range(-1f, 1f) * data.rootHalfExtents.z
                );
                normals[i] = Random.onUnitSphere;
            }

            // GPU 批量采样
            IrradianceCacheGPUSampler.SampleResult[] gpuResults;
            using (var sampler = new IrradianceCacheGPUSampler(samplerShader))
            {
                gpuResults = sampler.SampleBatch(positions, normals);
            }

            // CPU 采样并比较
            maxDifference = 0f;
            float totalDifference = 0f;

            for (int i = 0; i < batchTestCount; i++)
            {
                Color cpuColor = data.SampleLighting(positions[i], normals[i]);
                Color gpuColor = gpuResults[i].color;

                float diff = Mathf.Abs(cpuColor.r - gpuColor.r) +
                            Mathf.Abs(cpuColor.g - gpuColor.g) +
                            Mathf.Abs(cpuColor.b - gpuColor.b);

                maxDifference = Mathf.Max(maxDifference, diff);
                totalDifference += diff;
            }

            avgDifference = totalDifference / batchTestCount;

            Debug.Log($"Batch test complete. Max diff: {maxDifference:F6}, Avg diff: {avgDifference:F6}");
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (targetVolume == null || targetVolume.bakedData == null)
                return;

            // 绘制测试点
            Handles.color = Color.magenta;
            Handles.SphereHandleCap(0, testPosition, Quaternion.identity, 0.2f, EventType.Repaint);

            Handles.color = Color.blue;
            Handles.ArrowHandleCap(0, testPosition, Quaternion.LookRotation(testNormal), 0.5f, EventType.Repaint);
        }
    }
}
