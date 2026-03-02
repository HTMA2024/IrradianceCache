using UnityEngine;
using UnityEditor;

namespace IrradianceCacheSystem.Editor
{
    /// <summary>
    /// IrradianceCacheManager 的自定义 Inspector
    /// 显示所有激活的 Volume 列表和统计信息
    /// </summary>
    [CustomEditor(typeof(IrradianceCacheManager))]
    public class IrradianceCacheManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty autoRebuildProp;
        private SerializedProperty enableSkyboxFallbackProp;
        private SerializedProperty manualAmbientColorProp;
        private SerializedProperty ambientColorProp;
        private SerializedProperty shDeringingStrengthProp;

        private bool showVolumeList = true;
        private bool showStatistics = true;
        private Vector2 volumeListScrollPos;

        private void OnEnable()
        {
            autoRebuildProp = serializedObject.FindProperty("autoRebuild");
            enableSkyboxFallbackProp = serializedObject.FindProperty("enableSkyboxFallback");
            manualAmbientColorProp = serializedObject.FindProperty("manualAmbientColor");
            ambientColorProp = serializedObject.FindProperty("ambientColor");
            shDeringingStrengthProp = serializedObject.FindProperty("shDeringingStrength");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            IrradianceCacheManager manager = (IrradianceCacheManager)target;

            // Manager Settings
            EditorGUILayout.LabelField("Manager Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(autoRebuildProp);
            EditorGUILayout.PropertyField(enableSkyboxFallbackProp);
            EditorGUILayout.PropertyField(manualAmbientColorProp);
            EditorGUILayout.PropertyField(ambientColorProp);

            // SH Deringing
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SH Deringing", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(shDeringingStrengthProp, new GUIContent("Deringing Strength", "0 = 无衰减, 1 = 最大 Hanning 窗口衰减，减少 SH 振铃伪影"));

            EditorGUILayout.Space();

            // Actions
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 1f, 0.5f);
            if (GUILayout.Button("Force Rebuild", GUILayout.Height(25)))
            {
                manager.ForceRebuild();
            }
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
            if (GUILayout.Button("Release GPU Resources", GUILayout.Height(25)))
            {
                manager.ReleaseGPUResources();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Statistics
            showStatistics = EditorGUILayout.Foldout(showStatistics, "Statistics", true);
            if (showStatistics)
            {
                EditorGUI.indentLevel++;
                DrawStatistics(manager);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // Volume List
            showVolumeList = EditorGUILayout.Foldout(showVolumeList, $"Active Volumes ({manager.ActiveVolumeCount}/{IrradianceCacheManager.MaxVolumeCount})", true);
            if (showVolumeList)
            {
                EditorGUI.indentLevel++;
                DrawVolumeList(manager);
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            // Auto repaint when playing
            if (Application.isPlaying)
            {
                Repaint();
            }
        }

        private void DrawStatistics(IrradianceCacheManager manager)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // GPU Status
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("GPU Status:", GUILayout.Width(100));
            if (manager.IsGPUDataUploaded)
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField("Uploaded");
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.yellow;
                EditorGUILayout.LabelField("Not Uploaded");
                GUI.color = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Active Volumes:", manager.ActiveVolumeCount.ToString());
            EditorGUILayout.LabelField("Total Nodes:", manager.TotalNodeCount.ToString("N0"));
            EditorGUILayout.LabelField("GPU Memory:", $"{manager.GPUMemoryUsageMB:F2} MB");

            // Memory breakdown
            if (manager.TotalNodeCount > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Memory Breakdown:", EditorStyles.boldLabel);

                float nodeMemory = manager.TotalNodeCount * OctreeNodeCompact.GetStride() / (1024f * 1024f);
                float metadataMemory = IrradianceCacheManager.MaxVolumeCount * VolumeMetadata.GetStride() / (1024f * 1024f);

                EditorGUILayout.LabelField($"  Nodes: {nodeMemory:F3} MB");
                EditorGUILayout.LabelField($"  Metadata: {metadataMemory:F3} MB");
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVolumeList(IrradianceCacheManager manager)
        {
            if (manager.ActiveVolumeCount == 0)
            {
                EditorGUILayout.HelpBox("No volumes registered. Add IrradianceCache components to the scene with 'Use Volume Manager' enabled.", MessageType.Info);
                return;
            }

            volumeListScrollPos = EditorGUILayout.BeginScrollView(volumeListScrollPos, GUILayout.MaxHeight(200));

            int index = 0;
            foreach (var volume in manager.ActiveVolumes)
            {
                if (volume == null)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // Header with index and name
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{index}]", GUILayout.Width(30));

                // Clickable name to select the volume
                if (GUILayout.Button(volume.name, EditorStyles.linkLabel))
                {
                    Selection.activeGameObject = volume.gameObject;
                    EditorGUIUtility.PingObject(volume.gameObject);
                }
                EditorGUILayout.EndHorizontal();

                // Volume info
                EditorGUI.indentLevel++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Priority:", GUILayout.Width(60));
                EditorGUILayout.LabelField(volume.priority.ToString(), GUILayout.Width(40));
                EditorGUILayout.LabelField("Blend:", GUILayout.Width(40));
                EditorGUILayout.LabelField($"{volume.blendDistance:F1}m");
                EditorGUILayout.EndHorizontal();

                if (volume.bakedData != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Nodes:", GUILayout.Width(60));
                    EditorGUILayout.LabelField(volume.bakedData.nodes.Length.ToString("N0"));
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField("No baked data");
                    GUI.color = Color.white;
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();

                index++;
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnSceneGUI()
        {
            IrradianceCacheManager manager = (IrradianceCacheManager)target;

            // Draw all volume bounds with labels
            int index = 0;
            foreach (var volume in manager.ActiveVolumes)
            {
                if (volume == null || volume.bakedData == null)
                    continue;

                Bounds bounds = volume.GetTransformedBounds();
                Quaternion rotation = volume.GetEffectiveRotation();

                // Volume color based on priority
                float priorityT = volume.priority / 100f;
                Color volumeColor = Color.Lerp(Color.cyan, Color.magenta, priorityT);
                volumeColor.a = 0.5f;

                Handles.color = volumeColor;

                // Draw rotated bounds
                Matrix4x4 oldMatrix = Handles.matrix;
                Matrix4x4 rotationMatrix = Matrix4x4.TRS(bounds.center, rotation, Vector3.one);
                Handles.matrix = rotationMatrix;
                Handles.DrawWireCube(Vector3.zero, bounds.size);
                Handles.matrix = oldMatrix;

                // Draw blend zone
                if (volume.blendDistance > 0f)
                {
                    Handles.color = new Color(volumeColor.r, volumeColor.g, volumeColor.b, 0.2f);
                    Vector3 innerSize = bounds.size - Vector3.one * volume.blendDistance * 2f;
                    if (innerSize.x > 0 && innerSize.y > 0 && innerSize.z > 0)
                    {
                        Handles.matrix = rotationMatrix;
                        Handles.DrawWireCube(Vector3.zero, innerSize);
                        Handles.matrix = oldMatrix;
                    }
                }

                // Draw label
                Handles.color = Color.white;
                string label = $"[{index}] {volume.name}\nP:{volume.priority}";
                Handles.Label(bounds.center + Vector3.up * bounds.size.y * 0.5f, label, EditorStyles.boldLabel);

                index++;
            }
        }
    }
}
