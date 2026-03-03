using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace IrradianceCacheSystem.Editor
{
    /// <summary>
    /// IrradianceCache 的自定义 Inspector
    /// 包含可视化设置和 Bake 功能
    /// </summary>
    [CustomEditor(typeof(IrradianceCache))]
    public class IrradianceCacheEditor : UnityEditor.Editor
    {
        private SerializedProperty boundsProp;
        private SerializedProperty storageModeProp;
        private SerializedProperty maxDepthProp;
        private SerializedProperty gridResolutionProp;
        private SerializedProperty paddingProp;
        private SerializedProperty allowEditBoundsProp;
        private SerializedProperty bakedDataProp;
        private SerializedProperty autoUploadToGPUProp;
        private SerializedProperty uploadInEditorProp;
        private SerializedProperty lightProbeGroupListProp;

        // Transform properties
        private SerializedProperty transformModeProp;
        private SerializedProperty positionOffsetProp;
        private SerializedProperty rotationEulerProp;
        private SerializedProperty uniformScaleProp;

        // Multi-Volume properties
        private SerializedProperty priorityProp;
        private SerializedProperty blendDistanceProp;
        private SerializedProperty useVolumeManagerProp;

        // Transition Blending properties
        private SerializedProperty enableTransitionBlendingProp;
        private SerializedProperty transitionWidthRatioProp;

        // Obstacle properties
        private SerializedProperty applyObstacleProp;

        // SH Deringing property
        private SerializedProperty shDeringingStrengthProp;


        // Foldout states
        private bool showStatistics = true;
        private bool showDebugInfo = false;
        private bool showVisualization = true;
        private bool showTransformSettings = true;
        private bool showMultiVolumeSettings = true;
        private bool showTransitionBlendingSettings = true;
        private bool showInvalidCornerFixSettings = true;

        // Visualization settings (static to persist across selection changes)
        private static bool visualizationEnabled = false;
        private static int depthToShow = 6;
        private static bool showLeafNodesOnly = false;
        private static bool highlightSparseNodes = true;
        private static bool showNodeLabels = false;
        private static float nodeAlpha = 0.3f;

        private void OnEnable()
        {
            boundsProp = serializedObject.FindProperty("bounds");
            storageModeProp = serializedObject.FindProperty("storageMode");
            maxDepthProp = serializedObject.FindProperty("maxDepth");
            gridResolutionProp = serializedObject.FindProperty("gridResolution");
            paddingProp = serializedObject.FindProperty("padding");
            allowEditBoundsProp = serializedObject.FindProperty("allowEditBounds");
            bakedDataProp = serializedObject.FindProperty("bakedData");
            autoUploadToGPUProp = serializedObject.FindProperty("autoUploadToGPU");
            uploadInEditorProp = serializedObject.FindProperty("uploadInEditor");
            lightProbeGroupListProp = serializedObject.FindProperty("lightProbeGroupList");

            // Transform properties
            transformModeProp = serializedObject.FindProperty("transformMode");
            positionOffsetProp = serializedObject.FindProperty("positionOffset");
            rotationEulerProp = serializedObject.FindProperty("rotationEuler");
            uniformScaleProp = serializedObject.FindProperty("uniformScale");

            // Multi-Volume properties
            priorityProp = serializedObject.FindProperty("priority");
            blendDistanceProp = serializedObject.FindProperty("blendDistance");
            useVolumeManagerProp = serializedObject.FindProperty("useVolumeManager");

            // Transition Blending properties
            enableTransitionBlendingProp = serializedObject.FindProperty("enableTransitionBlending");
            transitionWidthRatioProp = serializedObject.FindProperty("transitionWidthRatio");

            // Obstacle properties
            applyObstacleProp = serializedObject.FindProperty("applyObstacle");

            // SH Deringing property
            shDeringingStrengthProp = serializedObject.FindProperty("shDeringingStrength");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            IrradianceCache volume = (IrradianceCache)target;

            // Volume Settings
            EditorGUILayout.LabelField("Volume Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(allowEditBoundsProp);

            EditorGUILayout.PropertyField(boundsProp);
            EditorGUILayout.PropertyField(paddingProp);

            // Storage Mode
            EditorGUILayout.PropertyField(storageModeProp, new GUIContent("Storage Mode"));
            VolumeStorageMode currentMode = (VolumeStorageMode)storageModeProp.enumValueIndex;

            if (currentMode == VolumeStorageMode.Octree)
            {
                EditorGUILayout.PropertyField(maxDepthProp);
            }
            else if (currentMode == VolumeStorageMode.UniformGrid)
            {
                EditorGUILayout.PropertyField(gridResolutionProp, new GUIContent("Grid Resolution"));

                // 预估信息
                Vector3Int res = gridResolutionProp.vector3IntValue;
                if (res.x > 0 && res.y > 0 && res.z > 0)
                {
                    int totalNodes = res.x * res.y * res.z;
                    float compactMemoryMB = (totalNodes * (float)OctreeNodeCompact.GetStride()) / (1024f * 1024f);
                    EditorGUILayout.LabelField("Total Nodes", totalNodes.ToString());
                    EditorGUILayout.LabelField("Est. GPU Memory (Compact)", $"{compactMemoryMB:F2} MB");

                    if (volume.bounds.size.x > 0 && volume.bounds.size.y > 0 && volume.bounds.size.z > 0)
                    {
                        Vector3 cellSize = new Vector3(
                            volume.bounds.size.x / res.x,
                            volume.bounds.size.y / res.y,
                            volume.bounds.size.z / res.z);
                        EditorGUILayout.LabelField("Cell Size", cellSize.ToString("F3"));
                    }
                }

                EditorGUILayout.PropertyField(applyObstacleProp, new GUIContent("Apply Obstacle"));
            }

            EditorGUILayout.Space();

            // Actions
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Auto Calculate Bounds"))
            {
                Undo.RecordObject(volume, "Auto Calculate Bounds");
                volume.bounds = volume.CalculateAutoVolumeBounds();
                EditorUtility.SetDirty(volume);
            }

            if (GUILayout.Button("Reset Bounds"))
            {
                Undo.RecordObject(volume, "Reset Bounds");
                volume.bounds = new Bounds(volume.transform.position, Vector3.one * 10f);
                EditorUtility.SetDirty(volume);
            }
            
            if (GUILayout.Button("Export Corners to LightProbeGroup"))
            {
                ExportCornersToLightProbeGroup(volume);
            }
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.Space();

            // Baked Data
            EditorGUILayout.LabelField("Light Probe Group List", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(lightProbeGroupListProp);
            
            // Baked Data
            EditorGUILayout.LabelField("Baked Data", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(bakedDataProp);

            EditorGUILayout.BeginHorizontal();
            // Bake Button
            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            string bakeButtonText = currentMode == VolumeStorageMode.UniformGrid
                ? "Bake Uniform Grid Irradiance Cache"
                : "Bake Irradiance Cache";
            if (GUILayout.Button(bakeButtonText, GUILayout.Height(30)))
            {
                BakeOctreeVolume(volume);
            }
            GUI.backgroundColor = Color.white;
            
            // Update Button
            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            string updateToGPUText = "Update To GPU";
            if (GUILayout.Button(updateToGPUText, GUILayout.Height(30)))
            {
                volume.UploadToGPU();
            }
            GUI.backgroundColor = Color.white;

            // Refresh Light Probe Group List Button
            GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
            if (GUILayout.Button("Refresh LPG List", GUILayout.Height(30)))
            {
                RefreshLightProbeGroupList(volume);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "工作流程:\n" +
                "1. 在Unity中构建LightProbeGroup并烘焙光照\n" +
                "2. 在上面LightProbeGroup属性中选中用于烘焙的LightProbeGroup\n" +
                "3. 点击AutoCalculateBounds，这将会缩放Volume体积保持和LightProbeGroup一致\n" +
                "4. 烘焙光照存储结构(Bake)，这将会花费10秒左右\n" +
                "5. (可选)点击UpdateToGPU上传至GPU，可预览结果",
                MessageType.Info);

            EditorGUILayout.Space();
            
            // Transform Settings
            EditorGUILayout.Space();
            showTransformSettings = EditorGUILayout.Foldout(showTransformSettings, "Transform Settings", true);
            if (showTransformSettings)
            {
                EditorGUI.indentLevel++;
                DrawTransformSettings(volume);
                EditorGUI.indentLevel--;
            }

            // Multi-Volume Settings
            EditorGUILayout.Space();
            showMultiVolumeSettings = EditorGUILayout.Foldout(showMultiVolumeSettings, "Multi-Volume Settings", true);
            if (showMultiVolumeSettings)
            {
                EditorGUI.indentLevel++;
                DrawMultiVolumeSettings(volume);
                EditorGUI.indentLevel--;
            }

            // Runtime Settings
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(autoUploadToGPUProp);
            EditorGUILayout.PropertyField(uploadInEditorProp);

            // SH Deringing
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SH Deringing", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(shDeringingStrengthProp, new GUIContent("Deringing Strength", "0 = 无衰减, 1 = 最大 Hanning 窗口衰减，减少 SH 振铃伪影"));
            
            // GPU Status
            IrradianceCache volume2 = (IrradianceCache)target;
            if (volume2.IsGPUDataUploaded)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("GPU Status", "Uploaded");
                EditorGUILayout.LabelField("(Compact)");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("GPU Memory", $"{volume2.GetGPUMemoryUsageMB():F2} MB");
            }

            // Transition Blending Settings (仅 Octree 模式)
            if (volume.storageMode == VolumeStorageMode.Octree)
            {
                EditorGUILayout.Space();
                showTransitionBlendingSettings = EditorGUILayout.Foldout(showTransitionBlendingSettings, "Transition Blending (跨深度接缝消除)", true);
                if (showTransitionBlendingSettings)
                {
                    EditorGUI.indentLevel++;
                    DrawTransitionBlendingSettings(volume);
                    EditorGUI.indentLevel--;
                }
            }

            // Statistics
            if (volume.bakedData != null)
            {
                // Visualization Settings
                EditorGUILayout.Space();
                showVisualization = EditorGUILayout.Foldout(showVisualization, "Visualization Settings", true);
                if (showVisualization)
                {
                    EditorGUI.indentLevel++;
                    DrawVisualizationSettings();
                    EditorGUI.indentLevel--;
                }
                
                showStatistics = EditorGUILayout.Foldout(showStatistics, "Statistics", true);
                if (showStatistics)
                {
                    EditorGUI.indentLevel++;
                    DrawStatistics(volume.bakedData);
                    EditorGUI.indentLevel--;
                }

                showDebugInfo = EditorGUILayout.Foldout(showDebugInfo, "Debug Info", true);
                if (showDebugInfo)
                {
                    EditorGUI.indentLevel++;
                    DrawDebugInfo(volume.bakedData);
                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();

            // Repaint scene view when visualization settings change
            if (GUI.changed && visualizationEnabled)
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawVisualizationSettings()
        {
            EditorGUI.BeginChangeCheck();

            visualizationEnabled = EditorGUILayout.Toggle("Enable Visualization", visualizationEnabled);

            EditorGUI.BeginDisabledGroup(!visualizationEnabled);
            depthToShow = EditorGUILayout.IntSlider("DepthToShow", depthToShow, 0, 8);
            showLeafNodesOnly = EditorGUILayout.Toggle("Show Leaf Nodes Only", showLeafNodesOnly);
            highlightSparseNodes = EditorGUILayout.Toggle("Highlight Sparse Nodes", highlightSparseNodes);
            showNodeLabels = EditorGUILayout.Toggle("Show Node Labels", showNodeLabels);
            nodeAlpha = EditorGUILayout.Slider("Node Alpha", nodeAlpha, 0.1f, 1f);

            EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            // Open Visualizer Window button
            EditorGUILayout.Space();
            if (GUILayout.Button("Open Visualizer Window"))
            {
                OctreeVisualizerWindow.ShowWindow();
            }
        }

        private void DrawTransformSettings(IrradianceCache volume)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(transformModeProp, new GUIContent("Transform Mode"));

            TransformMode mode = (TransformMode)transformModeProp.enumValueIndex;

            if (mode == TransformMode.UseGameObject)
            {
                EditorGUILayout.HelpBox(
                    "Volume 将跟随 GameObject 的 Transform。\n" +
                    "移动、旋转、缩放 GameObject 来变换 Volume。",
                    MessageType.Info);

                // 显示当前 Transform 信息（只读）
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.Vector3Field("Position", volume.transform.position);
                EditorGUILayout.Vector3Field("Rotation", volume.transform.eulerAngles);
                EditorGUILayout.Vector3Field("Scale", volume.transform.lossyScale);
                EditorGUI.EndDisabledGroup();
            }
            else if (mode == TransformMode.Manual)
            {
                EditorGUILayout.PropertyField(positionOffsetProp, new GUIContent("Position Offset", "相对于 baked 位置的偏移"));
                EditorGUILayout.PropertyField(rotationEulerProp, new GUIContent("Rotation", "旋转（欧拉角）"));
                EditorGUILayout.PropertyField(uniformScaleProp, new GUIContent("Uniform Scale", "均匀缩放"));

                // Reset 按钮
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset Transform", GUILayout.Width(120)))
                {
                    Undo.RecordObject(volume, "Reset Transform");
                    volume.positionOffset = Vector3.zero;
                    volume.rotationEuler = Vector3.zero;
                    volume.uniformScale = 1f;
                    EditorUtility.SetDirty(volume);
                }
                EditorGUILayout.EndHorizontal();
            }
            else // None
            {
                EditorGUILayout.HelpBox(
                    "Volume 使用 baked 时的原始位置。\n" +
                    "选择其他模式以启用运行时变换。",
                    MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();

                // 如果 GPU 数据已上传，更新 Transform 矩阵
                if (volume.IsGPUDataUploaded)
                {
                    volume.UpdateTransformMatrices();
                }

                SceneView.RepaintAll();
            }
        }

        private void DrawMultiVolumeSettings(IrradianceCache volume)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(useVolumeManagerProp, new GUIContent("Use Volume Manager",
                "启用后，此 Volume 将注册到全局管理器，支持多 Volume 混合"));

            bool useManager = useVolumeManagerProp.boolValue;

            // 显示管理器状态
            if (useManager)
            {
                if (IrradianceCacheManager.IsAvailable)
                {
                    EditorGUILayout.HelpBox(
                        $"已连接到 Volume Manager\n" +
                        $"激活的 Volume 数量: {IrradianceCacheManager.Instance.ActiveVolumeCount}",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "场景中没有 IrradianceCacheManager。\n" +
                        "请添加一个 Manager 组件以启用多 Volume 功能。",
                        MessageType.Warning);

                    if (GUILayout.Button("Create Volume Manager"))
                    {
                        CreateVolumeManager();
                    }
                }
            }

            EditorGUILayout.Space();

            // Priority slider
            EditorGUILayout.PropertyField(priorityProp, new GUIContent("Priority",
                "优先级 (0-100)。在重叠区域，高优先级的 Volume 权重更高"));

            // Priority visualization
            Rect priorityRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(4));
            priorityRect = EditorGUI.IndentedRect(priorityRect);
            float priorityT = priorityProp.intValue / 100f;
            EditorGUI.DrawRect(priorityRect, Color.gray);
            priorityRect.width *= priorityT;
            EditorGUI.DrawRect(priorityRect, Color.Lerp(Color.cyan, Color.magenta, priorityT));

            EditorGUILayout.Space();

            // Blend distance
            EditorGUILayout.PropertyField(blendDistanceProp, new GUIContent("Blend Distance",
                "边界混合距离 (米)。在此距离内，Volume 边缘会平滑过渡"));

            if (blendDistanceProp.floatValue > 0f && volume.bakedData != null)
            {
                float maxBlend = Mathf.Min(volume.bakedData.rootHalfExtents.x, Mathf.Min(volume.bakedData.rootHalfExtents.y, volume.bakedData.rootHalfExtents.z)) * 0.5f;
                if (blendDistanceProp.floatValue > maxBlend)
                {
                    EditorGUILayout.HelpBox(
                        $"混合距离过大，建议不超过 {maxBlend:F1}m (Volume 半尺寸的一半)",
                        MessageType.Warning);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();

                // 通知管理器需要更新
                if (useManager && IrradianceCacheManager.IsAvailable)
                {
                    IrradianceCacheManager.Instance.MarkDirty();
                }

                SceneView.RepaintAll();
            }
        }

        private void DrawTransitionBlendingSettings(IrradianceCache volume)
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(enableTransitionBlendingProp,
                new GUIContent("Enable Transition Blending",
                    "启用跨深度过渡混合，消除不同深度节点边界处的接缝（需要TA开启编译这个功能才能使用，否则不会生效）"));

            EditorGUI.BeginDisabledGroup(!enableTransitionBlendingProp.boolValue);

            EditorGUILayout.PropertyField(transitionWidthRatioProp,
                new GUIContent("Transition Width Ratio",
                    "过渡区域宽度比例 (相对于节点尺寸)"));

            // 显示过渡宽度的实际值（如果有 baked 数据）
            if (volume.bakedData != null)
            {
                float minNodeSize = Mathf.Min(volume.bakedData.rootHalfExtents.x, Mathf.Min(volume.bakedData.rootHalfExtents.y, volume.bakedData.rootHalfExtents.z)) / (1 << volume.bakedData.maxDepth);
                float transitionWidth = minNodeSize * transitionWidthRatioProp.floatValue;
                EditorGUILayout.LabelField("Min Transition Width", $"{transitionWidth:F3}m (at max depth)");
            }

            EditorGUI.EndDisabledGroup();

            // 性能提示
            if (enableTransitionBlendingProp.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "过渡混合会在边界区域进行额外的节点查询和 SH 插值，\n" +
                    "约增加 30% 的采样开销（仅在过渡区域内）。\n" +
                    "（需要TA开启编译这个功能才能使用，否则不会生效）",
                    MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();

                // 如果 GPU 数据已上传，更新过渡混合参数
                if (volume.IsGPUDataUploaded)
                {
                    volume.UpdateTransitionBlendingParams();
                }
                
                bool useManager = useVolumeManagerProp.boolValue;
                // 通知管理器需要更新
                if (useManager && IrradianceCacheManager.IsAvailable)
                {
                    IrradianceCacheManager.Instance.MarkDirty();
                }

                SceneView.RepaintAll();
            }
        }

        private void CreateVolumeManager()
        {
            GameObject managerObj = new GameObject("IrradianceCacheManager");
            managerObj.AddComponent<IrradianceCacheManager>();
            Undo.RegisterCreatedObjectUndo(managerObj, "Create Volume Manager");
            Selection.activeGameObject = managerObj;
        }

        private void DrawStatistics(IrradianceCacheData data)
        {
            // 显示存储模式
            EditorGUILayout.LabelField("Storage Mode", data.storageMode.ToString());

            if (data.storageMode == VolumeStorageMode.UniformGrid)
            {
                EditorGUILayout.LabelField("Grid Resolution",
                    $"{data.gridResolution.x} x {data.gridResolution.y} x {data.gridResolution.z}");
            }

            EditorGUILayout.LabelField("Total Nodes", data.totalNodeCount.ToString());

            if (data.storageMode == VolumeStorageMode.Octree)
            {
                EditorGUILayout.LabelField("Leaf Nodes", data.leafNodeCount.ToString());
                EditorGUILayout.LabelField("Sparse Nodes", data.sparseNodeCount.ToString());

                if (data.leafNodeCount > 0)
                {
                    float sparseRatio = (float)data.sparseNodeCount / data.leafNodeCount * 100f;
                    EditorGUILayout.LabelField("Sparse Ratio", $"{sparseRatio:F2}%");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Memory Usage", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Full (Float)", $"{data.GetMemoryUsageMB():F2} MB");
            EditorGUILayout.LabelField("Compact (Half)", $"{data.GetCompactMemoryUsageMB():F2} MB");

            // 显示节省的内存
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Memory Savings", $"{data.GetMemorySavingsPercent():F1}%");
            float savedMB = data.GetMemoryUsageMB() - data.GetCompactMemoryUsageMB();
            EditorGUILayout.LabelField($"({savedMB:F2} MB saved)");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Max Depth", data.maxDepth.ToString());
        }

        private void DrawDebugInfo(IrradianceCacheData data)
        {
            EditorGUILayout.LabelField("Root Center", data.rootCenter.ToString("F3"));
            EditorGUILayout.LabelField("Root Half Extents", data.rootHalfExtents.ToString("F3"));

            if (data.nodes != null && data.nodes.Length > 0)
            {
                // 统计每层节点数
                Dictionary<int, int> nodesPerDepth = new Dictionary<int, int>();
                for (int i = 0; i < data.nodes.Length; i++)
                {
                    int depth = data.nodes[i].depth;
                    if (!nodesPerDepth.ContainsKey(depth))
                        nodesPerDepth[depth] = 0;
                    nodesPerDepth[depth]++;
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Nodes per Depth:");
                foreach (var kvp in nodesPerDepth.OrderBy(x => x.Key))
                {
                    EditorGUILayout.LabelField($"  Depth {kvp.Key}", kvp.Value.ToString());
                }
            }

            // Validate button
            EditorGUILayout.Space();
            if (GUILayout.Button("Validate Data"))
            {
                if (data.ValidateData(out string errorMessage))
                {
                    EditorUtility.DisplayDialog("Validation", "Data is valid!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Validation Error", errorMessage, "OK");
                }
            }
        }

        /// <summary>
        /// 执行 Bake 流程
        /// </summary>
        private void BakeOctreeVolume(IrradianceCache volume)
        {
            // 检查 Light Probes
            Vector3[] lightProbePositions = OctreeBuilder.GetLightProbePositions(volume.lightProbeGroupList);
            if (lightProbePositions.Length == 0)
            {
                EditorUtility.DisplayDialog("Error",
                    "No Light Probes found in scene. Please add Light Probes and bake lighting first.",
                    "OK");
                return;
            }
            try
            {
                Vector3 rootCenter = volume.bounds.center;
                Vector3 rootHalfExtents = volume.bounds.extents;
                OctreeNode[] nodes;
                List<ObstacleOBBData> obstacles = null;

                if (volume.storageMode == VolumeStorageMode.UniformGrid)
                {
                    EditorUtility.DisplayProgressBar("Baking Uniform Grid Light Probe", "Building uniform grid...", 0f);

                    if (volume.applyObstacle)
                    {
                        var obstacleComponents = Object.FindObjectsOfType<ObstacleCubeComponent>();
                        if (obstacleComponents.Length > 0)
                        {
                            obstacles = new List<ObstacleOBBData>(obstacleComponents.Length);
                            foreach (var comp in obstacleComponents)
                            {
                                obstacles.Add(comp.GetOBBData());
                            }
                            Debug.Log($"[IrradianceCacheEditor] Collected {obstacles.Count} obstacle(s) for baking.");
                        }
                    }

                    nodes = OctreeBuilder.BuildUniformGrid(
                        rootCenter,
                        rootHalfExtents,
                        volume.gridResolution,
                        obstacles
                    );
                }
                else
                {
                    EditorUtility.DisplayProgressBar("Baking Irradiance Cache", "Building octree structure...", 0f);

                    nodes = OctreeBuilder.BuildOctree(
                        rootCenter,
                        rootHalfExtents,
                        volume.maxDepth,
                        lightProbePositions
                    );

                    EditorUtility.DisplayProgressBar("Baking Irradiance Cache", "Computing SH coefficients...", 0.3f);

                    OctreeBuilder.ComputeAllCornerSH(
                        nodes,
                        rootCenter,
                        rootHalfExtents);
                }

                EditorUtility.DisplayProgressBar("Baking Light Probe Volume", "Saving data...", 0.95f);

                // 创建或更新 ScriptableObject
                IrradianceCacheData data = volume.bakedData;
                bool createNew = data == null;

                if (createNew)
                {
                    data = ScriptableObject.CreateInstance<IrradianceCacheData>();
                }

                data.nodes = nodes;
                data.rootCenter = rootCenter;
                data.rootHalfExtents = rootHalfExtents;
                data.storageMode = volume.storageMode;

                // 存储烘焙时的障碍物数据（已在 baked 坐标空间中）
                if (obstacles != null && obstacles.Count > 0)
                {
                    data.bakedObstacles = obstacles.ToArray();
                }
                else
                {
                    data.bakedObstacles = null;
                }

                if (volume.storageMode == VolumeStorageMode.UniformGrid)
                {
                    data.maxDepth = 0;
                    data.gridResolution = volume.gridResolution;
                }
                else
                {
                    data.maxDepth = volume.maxDepth;
                    data.gridResolution = new Vector3Int(0, 0, 0);
                }

                data.UpdateStatistics();

                // 保存资产
                if (createNew)
                {
                    string path = GetDefaultBakeDataPath(volume);
                    if (!string.IsNullOrEmpty(path))
                    {
                        // 确保 Lighting 文件夹存在
                        string directory = System.IO.Path.GetDirectoryName(path);
                        if (!AssetDatabase.IsValidFolder(directory))
                        {
                            string parentDir = System.IO.Path.GetDirectoryName(directory);
                            string folderName = System.IO.Path.GetFileName(directory);
                            AssetDatabase.CreateFolder(parentDir, folderName);
                        }

                        // 如果已有同名文件，先删除再创建（覆写）
                        if (AssetDatabase.LoadAssetAtPath<IrradianceCacheData>(path) != null)
                        {
                            AssetDatabase.DeleteAsset(path);
                        }

                        AssetDatabase.CreateAsset(data, path);
                        volume.bakedData = data;
                        EditorUtility.SetDirty(volume);
                    }
                }
                else
                {
                    EditorUtility.SetDirty(data);
                }

                AssetDatabase.SaveAssets();

                // 验证数据 (仅 Octree 模式)
                if (volume.storageMode == VolumeStorageMode.Octree)
                {
                    List<string> errors;
                    if (!OctreeBuilder.ValidateOctree(nodes, out errors))
                    {
                        Debug.LogWarning("Octree validation warnings:\n" + string.Join("\n", errors));
                    }
                }

                // 输出统计信息
                string modeStr = volume.storageMode == VolumeStorageMode.UniformGrid
                    ? $"UniformGrid ({volume.gridResolution.x}x{volume.gridResolution.y}x{volume.gridResolution.z})"
                    : $"Octree (maxDepth={volume.maxDepth})";

                Debug.Log($"Light Probe Bake Complete! Mode: {modeStr}\n" +
                    $"Total Nodes: {data.totalNodeCount}\n" +
                    $"Leaf Nodes: {data.leafNodeCount}\n" +
                    $"Memory (Full): {data.GetMemoryUsageMB():F2} MB\n" +
                    $"Memory (Compact): {data.GetCompactMemoryUsageMB():F2} MB (saves {data.GetMemorySavingsPercent():F1}%)\n" +
                    $"Light Probes in scene: {lightProbePositions.Length}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 获取 Bake 数据的默认保存路径：场景所在文件夹/Lighting/IrradianceCacheData_{物体名}.asset
        /// </summary>
        private string GetDefaultBakeDataPath(IrradianceCache volume)
        {
            // 获取当前场景路径
            string scenePath = volume.gameObject.scene.path;
            if (string.IsNullOrEmpty(scenePath))
            {
                // 场景未保存，回退到弹窗选择
                return EditorUtility.SaveFilePanelInProject(
                    "Save Light Probe Data",
                    "IrradianceCacheData",
                    "asset",
                    "Save baked light probe data"
                );
            }

            string sceneDir = System.IO.Path.GetDirectoryName(scenePath);
            string lightingDir = System.IO.Path.Combine(sceneDir, "Lighting").Replace("\\", "/");
            string fileName = $"IrradianceCacheData_{volume.gameObject.name}.asset";
            return $"{lightingDir}/{fileName}";
        }

        /// <summary>
        /// 刷新 LightProbeGroup 列表：收集场景中所有 LightProbeGroup 并填充到列表
        /// </summary>
        private void RefreshLightProbeGroupList(IrradianceCache volume)
        {
            LightProbeGroup[] allGroups = Object.FindObjectsOfType<LightProbeGroup>();

            Undo.RecordObject(volume, "Refresh Light Probe Group List");

            if (volume.lightProbeGroupList == null)
            {
                volume.lightProbeGroupList = new List<LightProbeGroup>();
            }
            else
            {
                volume.lightProbeGroupList.Clear();
            }

            foreach (var group in allGroups)
            {
                volume.lightProbeGroupList.Add(group);
            }

            EditorUtility.SetDirty(volume);
            Debug.Log($"[IrradianceCacheEditor] Refreshed LightProbeGroup list: found {allGroups.Length} group(s) in scene.");
        }

        private void OnSceneGUI()
        {
            IrradianceCache volume = (IrradianceCache)target;

            // 绘制可视化
            if (visualizationEnabled && volume.bakedData != null)
            {
                DrawOctreeVisualization(volume);
            }

            // 绘制变换后的 Volume 边界
            if (volume.bakedData != null && volume.transformMode != TransformMode.None)
            {
                DrawTransformedVolumeBounds(volume);
            }

            // 绘制混合区域
            if (volume.bakedData != null && volume.blendDistance > 0f)
            {
                DrawBlendZone(volume);
            }

            // 允许在 Scene View 中编辑边界
            if (volume.allowEditBounds)
            {
                DrawBoundsHandles(volume);
            }
        }

        private void DrawBlendZone(IrradianceCache volume)
        {
            Bounds bounds = volume.GetTransformedBounds();
            Quaternion rotation = volume.GetEffectiveRotation();
            float blendDist = volume.blendDistance;

            // 计算内部边界（混合区域的内边界）
            Vector3 innerSize = bounds.size - Vector3.one * blendDist * 2f;

            // 只有当内部尺寸有效时才绘制
            if (innerSize.x <= 0 || innerSize.y <= 0 || innerSize.z <= 0)
                return;

            Matrix4x4 oldMatrix = Handles.matrix;
            Matrix4x4 rotationMatrix = Matrix4x4.TRS(bounds.center, rotation, Vector3.one);
            Handles.matrix = rotationMatrix;

            // 绘制混合区域内边界（黄色虚线效果）
            Handles.color = new Color(1f, 1f, 0f, 0.6f);
            Handles.DrawWireCube(Vector3.zero, innerSize);

            // 绘制混合区域标签
            Handles.matrix = oldMatrix;
            Handles.color = Color.yellow;
            Vector3 labelPos = bounds.center + rotation * new Vector3(bounds.size.x * 0.5f + 0.5f, bounds.size.y * 0.5f, 0);
            Handles.Label(labelPos, $"Blend: {blendDist:F1}m");
        }

        private void DrawTransformedVolumeBounds(IrradianceCache volume)
        {
            Bounds transformedBounds = volume.GetTransformedBounds();
            Quaternion rotation = volume.GetEffectiveRotation();

            // 绘制变换后的边界（带旋转）
            Handles.color = new Color(1f, 0.5f, 0f, 0.8f); // 橙色

            // 使用矩阵来绘制旋转后的立方体
            Matrix4x4 oldMatrix = Handles.matrix;
            Matrix4x4 rotationMatrix = Matrix4x4.TRS(transformedBounds.center, rotation, Vector3.one);
            Handles.matrix = rotationMatrix;

            // 绘制线框立方体（以原点为中心，因为已经通过矩阵平移了）
            Handles.DrawWireCube(Vector3.zero, transformedBounds.size);

            // 恢复矩阵
            Handles.matrix = oldMatrix;

            // 绘制原始 baked 边界（用于对比）
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.3f); // 灰色半透明
            Handles.DrawWireCube(volume.bakedData.rootCenter, volume.bakedData.rootHalfExtents * 2f);

            // 绘制标签
            Handles.color = Color.white;
            Handles.Label(transformedBounds.center + Vector3.up * transformedBounds.size.y * 0.5f,
                "Transformed Volume", EditorStyles.boldLabel);
        }

        private void DrawOctreeVisualization(IrradianceCache volume)
        {
            IrradianceCacheData data = volume.bakedData;
            if (data.nodes == null || data.nodes.Length == 0)
                return;

            // 获取 Transform 信息
            bool hasTransform = volume.transformMode != TransformMode.None;
            Quaternion rotation = volume.GetEffectiveRotation();
            Vector3 effectivePos = volume.GetEffectivePosition();
            float effectiveScale = volume.GetEffectiveScale();

            for (int i = 0; i < data.nodes.Length; i++)
            {
                OctreeNode node = data.nodes[i];

                // 过滤深度
                if (node.depth != depthToShow )
                    continue;

                // 如果只显示叶节点
                if (showLeafNodesOnly && !node.IsLeaf)
                    continue;

                // 计算节点位置和尺寸（通过遍历父节点链）
                Vector3 localCenter;
                Vector3 localHalfSize;
                data.GetNodeBounds(i, out localCenter, out localHalfSize);

                // 应用 Transform 变换
                Vector3 worldCenter;
                Vector3 worldHalfSize;
                if (hasTransform)
                {
                    // 将局部坐标变换到世界坐标
                    // 1. 相对于 baked 中心的偏移
                    Vector3 offsetFromBaked = localCenter - data.rootCenter;
                    // 2. 应用旋转和缩放
                    Vector3 transformedOffset = rotation * (offsetFromBaked * effectiveScale);
                    // 3. 加上新的中心位置
                    worldCenter = data.rootCenter + effectivePos + transformedOffset;
                    worldHalfSize = localHalfSize * effectiveScale;
                }
                else
                {
                    worldCenter = localCenter;
                    worldHalfSize = localHalfSize;
                }

                // 选择颜色
                Color nodeColor = GetNodeColor(node, data.maxDepth);

                // 绘制节点（带旋转）
                Handles.color = nodeColor;
                if (hasTransform && rotation != Quaternion.identity)
                {
                    // 使用矩阵绘制旋转后的立方体
                    Matrix4x4 oldMatrix = Handles.matrix;
                    Handles.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                    Handles.DrawWireCube(Vector3.zero, worldHalfSize * 2f);
                    Handles.matrix = oldMatrix;
                }
                else
                {
                    Handles.DrawWireCube(worldCenter, worldHalfSize * 2f);
                }

                // 高亮稀疏节点
                if (highlightSparseNodes && node.IsLeaf && node.depth < data.maxDepth)
                {
                    Handles.color = new Color(1f, 1f, 0f, 0.5f);
                    if (hasTransform && rotation != Quaternion.identity)
                    {
                        Matrix4x4 oldMatrix = Handles.matrix;
                        Handles.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                        Handles.DrawWireCube(Vector3.zero, worldHalfSize * 2.05f);
                        Handles.matrix = oldMatrix;
                    }
                    else
                    {
                        Handles.DrawWireCube(worldCenter, worldHalfSize * 2.05f);
                    }

                    // 绘制稀疏节点标记
                    Handles.color = Color.yellow;
                    Handles.SphereHandleCap(0, worldCenter, Quaternion.identity, worldHalfSize.magnitude * 0.3f, EventType.Repaint);
                }

                // 显示节点标签
                if (showNodeLabels)
                {
                    Handles.Label(worldCenter, $"D{node.depth}\nM:{node.mortonCode:X}");
                }
            }

            // 绘制根节点边界（变换后）
            Handles.color = Color.white;
            if (hasTransform)
            {
                Vector3 rootWorldCenter = data.rootCenter + effectivePos;
                Vector3 rootWorldHalfExtents = data.rootHalfExtents * effectiveScale;
                Matrix4x4 oldMatrix = Handles.matrix;
                Handles.matrix = Matrix4x4.TRS(rootWorldCenter, rotation, Vector3.one);
                Handles.DrawWireCube(Vector3.zero, rootWorldHalfExtents * 2f);
                Handles.matrix = oldMatrix;
            }
            else
            {
                Handles.DrawWireCube(data.rootCenter, data.rootHalfExtents * 2f);
            }
        }

        private Color GetNodeColor(OctreeNode node, int maxDepth)
        {
            if (node.IsLeaf)
            {
                // 叶节点：根据深度使用渐变色 (绿色 -> 红色)
                float t = (float)node.depth / maxDepth;
                Color color = Color.Lerp(Color.green, Color.red, t);
                color.a = nodeAlpha;
                return color;
            }
            else
            {
                // 非叶节点：半透明蓝色
                return new Color(0.5f, 0.5f, 1f, nodeAlpha * 0.5f);
            }
        }

        private void DrawBoundsHandles(IrradianceCache volume)
        {
            EditorGUI.BeginChangeCheck();

            Bounds newBounds = volume.bounds;

            // 绘制可编辑的边界框
            Handles.color = Color.green;

            // 中心点移动手柄
            Vector3 newCenter = Handles.PositionHandle(newBounds.center, Quaternion.identity);
            if (newCenter != newBounds.center)
            {
                newBounds.center = newCenter;
            }

            // 尺寸调整手柄
            Vector3 newSize = Handles.ScaleHandle(
                newBounds.size,
                newBounds.center,
                Quaternion.identity,
                HandleUtility.GetHandleSize(newBounds.center)
            );
            if (newSize != newBounds.size)
            {
                newBounds.size = Vector3.Max(newSize, Vector3.one * 0.1f);
            }

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(volume, "Modify Volume Bounds");
                volume.bounds = newBounds;
                EditorUtility.SetDirty(volume);
            }
        }

        /// <summary>
        /// 导出角点到LightProbeGroup
        /// </summary>
        private void ExportCornersToLightProbeGroup(IrradianceCache volume)
        {
            // 检查 Light Probes
            Vector3[] lightProbePositions = OctreeBuilder.GetLightProbePositions(volume.lightProbeGroupList);
            if (lightProbePositions.Length == 0)
            {
                EditorUtility.DisplayDialog("Error",
                    "No Light Probes found in scene. Please add Light Probes and bake lighting first.",
                    "OK");
                return;
            }

            Vector3 rootCenter = volume.bounds.center;
            Vector3 rootHalfExtents = volume.bounds.extents;

            try
            {
                EditorUtility.DisplayProgressBar("Export Corners", "Building structure...", 0.1f);

                Vector3[] cornerPositions;

                if (volume.storageMode == VolumeStorageMode.UniformGrid)
                {
                    // Uniform Grid: 直接收集角点
                    cornerPositions = OctreeBuilder.CollectUniformGridCornerPositions(
                        rootCenter, rootHalfExtents, volume.gridResolution);
                }
                else
                {
                    // Octree: 构建八叉树后收集角点
                    OctreeNode[] nodes = OctreeBuilder.BuildOctree(
                        rootCenter, rootHalfExtents, volume.maxDepth, lightProbePositions);

                    EditorUtility.DisplayProgressBar("Export Corners", "Collecting corner positions...", 0.5f);

                    List<OctreeBuilder.CornerMapping>[] cornerMappings;
                    OctreeBuilder.CollectUniqueCornerPositions(
                        nodes, rootCenter, rootHalfExtents,
                        out cornerPositions, out cornerMappings);
                }

                EditorUtility.DisplayProgressBar("Export Corners", "Creating LightProbeGroup...", 0.8f);

                // 查找或创建LightProbeGroup
                LightProbeGroup lightProbeGroup = FindOrCreateLightProbeGroup(volume);

                // 设置角点位置
                OctreeBuilder.SetLightProbeGroupPositions(lightProbeGroup, cornerPositions);

                EditorUtility.SetDirty(lightProbeGroup);

                string modeStr = volume.storageMode == VolumeStorageMode.UniformGrid ? "UniformGrid" : "Octree";
                Debug.Log($"Exported {cornerPositions.Length} corner positions to LightProbeGroup ({modeStr} mode).\n" +
                    "Please bake lighting in Unity, then use 'Import from Baked LightProbes' to apply the data.");

                // 选中创建的LightProbeGroup
                Selection.activeGameObject = lightProbeGroup.gameObject;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// 查找或创建LightProbeGroup
        /// </summary>
        private LightProbeGroup FindOrCreateLightProbeGroup(IrradianceCache volume)
        {
            // 先查找子对象中是否已有LightProbeGroup
            LightProbeGroup existing = volume.GetComponentInChildren<LightProbeGroup>();
            if (existing != null)
                return existing;

            // 创建新的GameObject和LightProbeGroup
            GameObject lpgObj = new GameObject("OctreeCornerLightProbes");
            lpgObj.transform.SetParent(volume.transform);
            lpgObj.transform.localPosition = Vector3.zero;
            lpgObj.transform.localRotation = Quaternion.identity;
            lpgObj.transform.localScale = Vector3.one;

            LightProbeGroup lpg = lpgObj.AddComponent<LightProbeGroup>();
            Undo.RegisterCreatedObjectUndo(lpgObj, "Create Corner LightProbeGroup");

            return lpg;
        }

        /// <summary>
        /// 从烘焙的LightProbes导入SH数据
        /// </summary>
        private void ImportFromBakedLightProbes(IrradianceCache volume)
        {
            if (volume.bakedData == null)
            {
                EditorUtility.DisplayDialog("Error", "No baked data found. Please bake the octree first.", "OK");
                return;
            }

            IrradianceCacheData data = volume.bakedData;

            try
            {
                EditorUtility.DisplayProgressBar("Import Baked Data", "Collecting corner positions...", 0.2f);

                // 收集角点位置和映射
                Vector3[] cornerPositions;
                List<OctreeBuilder.CornerMapping>[] cornerMappings;
                OctreeBuilder.CollectUniqueCornerPositions(
                    data.nodes,
                    data.rootCenter,
                    data.rootHalfExtents,
                    out cornerPositions,
                    out cornerMappings
                );

                EditorUtility.DisplayProgressBar("Import Baked Data", "Applying baked SH data...", 0.5f);

                // 应用烘焙数据
                int appliedCount = OctreeBuilder.ApplyBakedLightProbeData(
                    data.nodes,
                    cornerPositions,
                    cornerMappings
                );

                EditorUtility.DisplayProgressBar("Import Baked Data", "Saving...", 0.9f);

                // 标记数据已修改
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();

                // 如果GPU数据已上传，重新上传
                if (volume.IsGPUDataUploaded)
                {
                    volume.UploadToGPU();
                }

                float successRate = ((float)appliedCount / (float)cornerPositions.Length) * 100f;
                Debug.Log($"Imported baked Light Probe data.\n" +
                    $"Applied: {appliedCount}/{cornerPositions.Length} corners ({successRate:F1}%)\n" +
                    $"Note: Corners not found in baked data will use interpolated values.");

                if (appliedCount < cornerPositions.Length)
                {
                    EditorUtility.DisplayDialog("Import Complete",
                        $"Applied {appliedCount}/{cornerPositions.Length} corners ({successRate:F1}%).\n\n" +
                        "Some corners were not found in the baked Light Probes.\n" +
                        "Make sure you exported corners and baked lighting before importing.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Import Complete",
                        $"Successfully applied all {appliedCount} corner positions!",
                        "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
