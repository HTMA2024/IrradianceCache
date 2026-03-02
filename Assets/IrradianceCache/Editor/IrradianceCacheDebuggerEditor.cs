using UnityEngine;
using UnityEditor;

namespace IrradianceCacheSystem.Editor
{
    /// <summary>
    /// IrradianceCacheDebugger 的自定义 Inspector
    /// </summary>
    [CustomEditor(typeof(IrradianceCacheDebugger))]
    public class IrradianceCacheDebuggerEditor : UnityEditor.Editor
    {
        private SerializedProperty targetVolumeProp;
        private SerializedProperty enableVisualizationProp;
        private SerializedProperty showOctreeNodesProp;
        private SerializedProperty displayDepthProp;
        private SerializedProperty showLeafNodesOnlyProp;
        private SerializedProperty highlightSparseNodesProp;
        private SerializedProperty nodeAlphaProp;
        private SerializedProperty showSamplingVisualizationProp;
        private SerializedProperty samplePointCountProp;
        private SerializedProperty samplePointSizeProp;
        private SerializedProperty showSampleRaysProp;
        private SerializedProperty rayLengthProp;
        private SerializedProperty enableQueryTestProp;
        private SerializedProperty queryTransformProp;
        private SerializedProperty queryPositionProp;
        private SerializedProperty queryNormalProp;

        private bool showQueryResults = true;

        private void OnEnable()
        {
            targetVolumeProp = serializedObject.FindProperty("targetVolume");
            enableVisualizationProp = serializedObject.FindProperty("enableVisualization");
            showOctreeNodesProp = serializedObject.FindProperty("showOctreeNodes");
            displayDepthProp = serializedObject.FindProperty("displayDepth");
            showLeafNodesOnlyProp = serializedObject.FindProperty("showLeafNodesOnly");
            highlightSparseNodesProp = serializedObject.FindProperty("highlightSparseNodes");
            nodeAlphaProp = serializedObject.FindProperty("nodeAlpha");
            showSamplingVisualizationProp = serializedObject.FindProperty("showSamplingVisualization");
            samplePointCountProp = serializedObject.FindProperty("samplePointCount");
            samplePointSizeProp = serializedObject.FindProperty("samplePointSize");
            showSampleRaysProp = serializedObject.FindProperty("showSampleRays");
            rayLengthProp = serializedObject.FindProperty("rayLength");
            enableQueryTestProp = serializedObject.FindProperty("enableQueryTest");
            queryTransformProp = serializedObject.FindProperty("queryTransform");
            queryPositionProp = serializedObject.FindProperty("queryPosition");
            queryNormalProp = serializedObject.FindProperty("queryNormal");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            IrradianceCacheDebugger debugger = (IrradianceCacheDebugger)target;

            // Target Volume
            EditorGUILayout.LabelField("Target Volume", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(targetVolumeProp);

            if (debugger.targetVolume == null)
            {
                EditorGUILayout.HelpBox("Please assign an IrradianceCache to debug.", MessageType.Warning);

                if (GUILayout.Button("Find Volume in Scene"))
                {
                    IrradianceCache volume = FindObjectOfType<IrradianceCache>();
                    if (volume != null)
                    {
                        debugger.targetVolume = volume;
                        EditorUtility.SetDirty(debugger);
                    }
                }
            }
            else if (debugger.targetVolume.bakedData == null)
            {
                EditorGUILayout.HelpBox("The target volume has no baked data.", MessageType.Warning);
            }

            EditorGUILayout.Space();

            // Visualization Settings
            EditorGUILayout.LabelField("Visualization Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableVisualizationProp);

            EditorGUI.BeginDisabledGroup(!debugger.enableVisualization);

            EditorGUILayout.PropertyField(showOctreeNodesProp);

            EditorGUI.BeginDisabledGroup(!debugger.showOctreeNodes);
            EditorGUI.indentLevel++;

            // Octree 特有的过滤选项（Grid 模式下隐藏）
            bool isGridMode = debugger.targetVolume != null &&
                              debugger.targetVolume.bakedData != null &&
                              debugger.targetVolume.bakedData.storageMode == VolumeStorageMode.UniformGrid;

            if (!isGridMode)
            {
                EditorGUILayout.PropertyField(displayDepthProp);
                EditorGUILayout.PropertyField(showLeafNodesOnlyProp);
                EditorGUILayout.PropertyField(highlightSparseNodesProp);
            }

            EditorGUILayout.PropertyField(nodeAlphaProp);
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            // Sampling Visualization
            EditorGUILayout.LabelField("Sampling Visualization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(showSamplingVisualizationProp);

            EditorGUI.BeginDisabledGroup(!debugger.showSamplingVisualization);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(samplePointCountProp);
            EditorGUILayout.PropertyField(samplePointSizeProp);
            EditorGUILayout.PropertyField(showSampleRaysProp);
            EditorGUILayout.PropertyField(rayLengthProp);
            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            // Query Test
            EditorGUILayout.LabelField("Query Test", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(enableQueryTestProp);

            EditorGUI.BeginDisabledGroup(!debugger.enableQueryTest);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(queryPositionProp);
            EditorGUILayout.PropertyField(queryNormalProp);
            EditorGUILayout.PropertyField(queryTransformProp);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Transform Position"))
            {
                debugger.queryPosition = debugger.queryTransform.position;
                EditorUtility.SetDirty(debugger);
            }
            if (GUILayout.Button("Use Camera Forward"))
            {
                if (SceneView.lastActiveSceneView != null)
                {
                    debugger.queryNormal = -SceneView.lastActiveSceneView.camera.transform.forward;
                    EditorUtility.SetDirty(debugger);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
            EditorGUI.EndDisabledGroup();

            EditorGUI.EndDisabledGroup();

            // Query Results
            if (debugger.enableQueryTest && debugger.targetVolume != null && debugger.targetVolume.bakedData != null)
            {
                EditorGUILayout.Space();
                showQueryResults = EditorGUILayout.Foldout(showQueryResults, "Query Results", true);

                if (showQueryResults)
                {
                    EditorGUI.indentLevel++;

                    IrradianceCacheData data = debugger.targetVolume.bakedData;
                    int nodeIndex = data.QueryLeafNode(debugger.queryPosition);

                    if (nodeIndex >= 0)
                    {
                        OctreeNode node = data.nodes[nodeIndex];
                        EditorGUILayout.LabelField("Node Index", nodeIndex.ToString());

                        if (data.storageMode == VolumeStorageMode.UniformGrid)
                        {
                            // Grid 模式：显示 cell 索引
                            int nx = data.gridResolution.x;
                            int ny = data.gridResolution.y;
                            int iz = nodeIndex / (nx * ny);
                            int remainder = nodeIndex % (nx * ny);
                            int iy = remainder / nx;
                            int ix = remainder % nx;
                            EditorGUILayout.LabelField("Cell Index", $"({ix}, {iy}, {iz})");
                            EditorGUILayout.LabelField("Storage Mode", "UniformGrid");
                        }
                        else
                        {
                            // Octree 模式：显示深度和 Morton Code
                            EditorGUILayout.LabelField("Node Depth", node.depth.ToString());
                            EditorGUILayout.LabelField("Morton Code", $"0x{node.mortonCode:X8}");
                            EditorGUILayout.LabelField("Is Leaf", node.IsLeaf.ToString());

                            if (node.IsLeaf && node.depth < data.maxDepth)
                            {
                                EditorGUILayout.LabelField("Type", "SPARSE");
                            }
                        }

                        Vector3 nodeCenter;
                        Vector3 nodeHalfExtents;
                        data.GetNodeBounds(nodeIndex, out nodeCenter, out nodeHalfExtents);
                        EditorGUILayout.LabelField("Node Center", nodeCenter.ToString("F3"));
                        EditorGUILayout.LabelField("Node Half Extents", nodeHalfExtents.ToString("F4"));

                        // Sample and display color
                        Color sampledColor = data.SampleLighting(debugger.queryPosition, debugger.queryNormal.normalized);
                        EditorGUILayout.ColorField("Sampled Color", sampledColor);

                        // Compare with Unity's built-in
                        UnityEngine.Rendering.SphericalHarmonicsL2 unitySH;
                        LightProbes.GetInterpolatedProbe(debugger.queryPosition, null, out unitySH);
                        Color unityColor = EvaluateUnitySH(unitySH, debugger.queryNormal.normalized);
                        EditorGUILayout.ColorField("Unity SH Color", unityColor);

                        // Difference
                        Color diff = new Color(
                            Mathf.Abs(sampledColor.r - unityColor.r),
                            Mathf.Abs(sampledColor.g - unityColor.g),
                            Mathf.Abs(sampledColor.b - unityColor.b),
                            1f
                        );
                        EditorGUILayout.ColorField("Difference", diff);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Position is outside volume bounds.");
                    }

                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();

            // Repaint scene view
            if (GUI.changed)
            {
                SceneView.RepaintAll();
            }
        }

        private Color EvaluateUnitySH(UnityEngine.Rendering.SphericalHarmonicsL2 sh, Vector3 normal)
        {
            // L1 基函数常数
            const float c0 = 0.886227f;  // L0: from Unity Shader
            const float c1 = 1.055000f;  // L1: from Unity Shader

            float basisL0 = c0;
            float basisL1x = c1 * normal.x;
            float basisL1y = c1 * normal.y;
            float basisL1z = c1 * normal.z;

            float r = sh[0, 0] * basisL0 + sh[0, 1] * basisL1x + sh[0, 2] * basisL1y + sh[0, 3] * basisL1z;
            float g = sh[1, 0] * basisL0 + sh[1, 1] * basisL1x + sh[1, 2] * basisL1y + sh[1, 3] * basisL1z;
            float b = sh[2, 0] * basisL0 + sh[2, 1] * basisL1x + sh[2, 2] * basisL1y + sh[2, 3] * basisL1z;

            return new Color(Mathf.Max(0, r), Mathf.Max(0, g), Mathf.Max(0, b), 1f);
        }

        private void OnSceneGUI()
        {
            IrradianceCacheDebugger debugger = (IrradianceCacheDebugger)target;

            if (!debugger.enableQueryTest || !debugger.enableVisualization)
                return;

            // 允许在 Scene View 中拖动查询位置
            EditorGUI.BeginChangeCheck();

            Vector3 newQueryPos = Handles.PositionHandle(debugger.queryPosition, Quaternion.identity);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(debugger, "Move Query Position");
                debugger.queryPosition = newQueryPos;
                EditorUtility.SetDirty(debugger);
            }

            // 绘制法线方向手柄
            Handles.color = Color.blue;
            Handles.ArrowHandleCap(
                0,
                debugger.queryPosition,
                Quaternion.LookRotation(debugger.queryNormal),
                debugger.rayLength,
                EventType.Repaint
            );
        }
    }
}
