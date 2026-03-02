using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace IrradianceCacheSystem.Editor
{
    /// <summary>
    /// Irradiance Cache 数据可视化窗口
    /// 提供详细的数据分析、节点浏览和导出功能
    /// </summary>
    public class OctreeVisualizerWindow : EditorWindow
    {
        private IrradianceCacheData selectedData;
        private IrradianceCache selectedVolume; // 用于获取 Transform 信息
        private Vector2 nodeListScrollPos;
        private Vector2 detailScrollPos;
        private int selectedNodeIndex = -1;

        // View options
        private bool showNodeList = true;
        private bool showSHCoefficients = false;
        private string searchFilter = "";
        private int filterDepth = -1; // -1 = all depths
        private bool filterLeafOnly = false;
        private bool filterSparseOnly = false;

        // Sampling test
        private Vector3 testPosition = Vector3.zero;
        private Vector3 testNormal = Vector3.up;
        private Color sampledColor = Color.black;

        [MenuItem("Window/Irradiance Cache/Visualizer")]
        public static void ShowWindow()
        {
            OctreeVisualizerWindow window = GetWindow<OctreeVisualizerWindow>("Octree Visualizer");
            window.minSize = new Vector2(400, 500);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            // Data selection
            EditorGUILayout.LabelField("Irradiance Cache Data Visualizer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            selectedData = (IrradianceCacheData)EditorGUILayout.ObjectField(
                "Baked Data",
                selectedData,
                typeof(IrradianceCacheData),
                false
            );

            // Try to get data from selected volume if none selected
            if (selectedData == null)
            {
                if (GUILayout.Button("Get from Selected Volume"))
                {
                    TryGetDataFromSelection();
                }

                EditorGUILayout.HelpBox(
                    "Select an IrradianceCacheData asset or a GameObject with IrradianceCache component.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space();

            // Tabs
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(showNodeList, "Node List", EditorStyles.toolbarButton))
            {
                showNodeList = true;
            }
            if (GUILayout.Toggle(!showNodeList, "Statistics & Tools", EditorStyles.toolbarButton))
            {
                showNodeList = false;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (showNodeList)
            {
                DrawNodeListView();
            }
            else
            {
                DrawStatisticsAndTools();
            }
        }

        private void TryGetDataFromSelection()
        {
            if (Selection.activeGameObject != null)
            {
                IrradianceCache volume = Selection.activeGameObject.GetComponent<IrradianceCache>();
                if (volume != null && volume.bakedData != null)
                {
                    selectedData = volume.bakedData;
                    selectedVolume = volume;
                }
            }
        }

        private void DrawNodeListView()
        {
            // Filters
            EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            searchFilter = EditorGUILayout.TextField(
                IsGridMode ? "Search Cell Index" : "Search Morton Code", searchFilter);
            if (GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                searchFilter = "";
            }
            EditorGUILayout.EndHorizontal();

            if (!IsGridMode)
            {
                EditorGUILayout.BeginHorizontal();
                filterDepth = EditorGUILayout.IntSlider("Filter Depth", filterDepth, -1, selectedData.maxDepth);
                EditorGUILayout.LabelField(filterDepth == -1 ? "(All)" : "", GUILayout.Width(40));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                filterLeafOnly = EditorGUILayout.Toggle("Leaf Nodes Only", filterLeafOnly);
                filterSparseOnly = EditorGUILayout.Toggle("Sparse Nodes Only", filterSparseOnly);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            // Node list and detail split view
            EditorGUILayout.BeginHorizontal();

            // Left panel: Node list
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.5f));
            EditorGUILayout.LabelField("Nodes", EditorStyles.boldLabel);

            nodeListScrollPos = EditorGUILayout.BeginScrollView(nodeListScrollPos, GUILayout.Height(300));
            DrawFilteredNodeList();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();

            // Right panel: Node details
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Node Details", EditorStyles.boldLabel);

            detailScrollPos = EditorGUILayout.BeginScrollView(detailScrollPos, GUILayout.Height(300));
            DrawNodeDetails();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFilteredNodeList()
        {
            if (selectedData.nodes == null)
                return;

            int displayCount = 0;
            int maxDisplay = 500; // Limit for performance

            for (int i = 0; i < selectedData.nodes.Length && displayCount < maxDisplay; i++)
            {
                OctreeNode node = selectedData.nodes[i];

                if (IsGridMode)
                {
                    // Grid 模式：按 cell 索引搜索
                    if (!string.IsNullOrEmpty(searchFilter))
                    {
                        Vector3Int cellIdx = GetGridCellIndex(i);
                        string cellStr = $"{cellIdx.x},{cellIdx.y},{cellIdx.z}";
                        if (!cellStr.Contains(searchFilter) && !i.ToString().Contains(searchFilter))
                            continue;
                    }

                    displayCount++;

                    Vector3Int cell = GetGridCellIndex(i);
                    string label = $"[{i}] Cell({cell.x},{cell.y},{cell.z})";

                    bool isSelected = selectedNodeIndex == i;
                    GUI.backgroundColor = isSelected ? Color.cyan : Color.white;

                    if (GUILayout.Button(label, EditorStyles.miniButton))
                    {
                        selectedNodeIndex = i;
                        FocusNodeInSceneView(node);
                    }

                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    // Octree 模式：原有逻辑
                    // Apply filters
                    if (filterDepth >= 0 && node.depth != filterDepth)
                        continue;

                    if (filterLeafOnly && !node.IsLeaf)
                        continue;

                    if (filterSparseOnly && !(node.IsLeaf && node.depth < selectedData.maxDepth))
                        continue;

                    if (!string.IsNullOrEmpty(searchFilter))
                    {
                        string mortonHex = node.mortonCode.ToString("X");
                        if (!mortonHex.Contains(searchFilter.ToUpper()))
                            continue;
                    }

                    displayCount++;

                    // Draw node entry
                    string label = $"[{i}] D{node.depth} M:0x{node.mortonCode:X8}";
                    if (node.IsLeaf)
                    {
                        if (node.depth < selectedData.maxDepth)
                            label += " [SPARSE]";
                        else
                            label += " [LEAF]";
                    }

                    bool isSelected = selectedNodeIndex == i;
                    GUI.backgroundColor = isSelected ? Color.cyan : Color.white;

                    if (GUILayout.Button(label, EditorStyles.miniButton))
                    {
                        selectedNodeIndex = i;
                        FocusNodeInSceneView(node);
                    }

                    GUI.backgroundColor = Color.white;
                }
            }

            if (displayCount >= maxDisplay)
            {
                EditorGUILayout.HelpBox($"Showing first {maxDisplay} matching nodes. Use filters to narrow down.", MessageType.Info);
            }

            if (displayCount == 0)
            {
                EditorGUILayout.LabelField("No nodes match the current filters.");
            }
        }

        private void DrawNodeDetails()
        {
            if (selectedNodeIndex < 0 || selectedNodeIndex >= selectedData.nodes.Length)
            {
                EditorGUILayout.LabelField("Select a node from the list.");
                return;
            }

            OctreeNode node = selectedData.nodes[selectedNodeIndex];

            EditorGUILayout.LabelField("Basic Info", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Index", selectedNodeIndex.ToString());

            if (IsGridMode)
            {
                // Grid 模式：显示 cell 索引
                Vector3Int cellIdx = GetGridCellIndex(selectedNodeIndex);
                EditorGUILayout.LabelField("Cell Index", $"({cellIdx.x}, {cellIdx.y}, {cellIdx.z})");
                EditorGUILayout.LabelField("Storage Mode", "UniformGrid");
            }
            else
            {
                // Octree 模式：显示 Morton Code 和深度
                EditorGUILayout.LabelField("Morton Code", $"0x{node.mortonCode:X8} ({node.mortonCode})");
                EditorGUILayout.LabelField("Depth", node.depth.ToString());
                EditorGUILayout.LabelField("Is Leaf", node.IsLeaf.ToString());

                if (!node.IsLeaf)
                {
                    EditorGUILayout.LabelField("Child Start Index", node.childStartIndex.ToString());
                }
                else if (node.depth < selectedData.maxDepth)
                {
                    EditorGUILayout.LabelField("Type", "SPARSE (early termination)");
                }

                // Decoded position
                MortonCodeHelper.DecodeMorton3D(node.mortonCode, out uint gx, out uint gy, out uint gz);
                EditorGUILayout.LabelField("Grid Position", $"({gx}, {gy}, {gz})");
            }

            // World position (calculated via parent chain traversal)
            Vector3 localCenter;
            Vector3 localHalfSize;
            selectedData.GetNodeBounds(selectedNodeIndex, out localCenter, out localHalfSize);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baked Position", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Local Center", localCenter.ToString("F3"));
            EditorGUILayout.LabelField("Half Size", localHalfSize.ToString("F4"));

            // 如果有 Volume 且启用了 Transform，显示变换后的位置
            if (selectedVolume != null && selectedVolume.transformMode != TransformMode.None)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Transformed Position", EditorStyles.boldLabel);

                Vector3 worldCenter;
                Vector3 worldHalfSize;
                GetTransformedNodeBounds(localCenter, localHalfSize, out worldCenter, out worldHalfSize);

                EditorGUILayout.LabelField("World Center", worldCenter.ToString("F3"));
                EditorGUILayout.LabelField("World Half Size", worldHalfSize.ToString("F4"));

                // 显示 Transform 信息
                EditorGUILayout.LabelField("Transform Mode", selectedVolume.transformMode.ToString());
            }

            EditorGUILayout.Space();

            // SH Coefficients
            showSHCoefficients = EditorGUILayout.Foldout(showSHCoefficients, "SH Coefficients (8 corners)", true);
            if (showSHCoefficients)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < 8; i++)
                {
                    SHCoefficients sh = node.GetCornerSH(i);
                    string cornerLabel = GetCornerLabel(i);

                    EditorGUILayout.LabelField($"Corner {i} ({cornerLabel}):");
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("R", sh.shR.ToString("F3"));
                    EditorGUILayout.LabelField("G", sh.shG.ToString("F3"));
                    EditorGUILayout.LabelField("B", sh.shB.ToString("F3"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // Actions
            if (GUILayout.Button("Focus in Scene View"))
            {
                FocusNodeInSceneView(node);
            }
        }

        private string GetCornerLabel(int index)
        {
            string[] labels = { "---", "+--", "-+-", "++-", "--+", "+-+", "-++", "+++" };
            return labels[index];
        }

        /// <summary>
        /// 从线性索引计算 Grid cell 的 (ix, iy, iz)
        /// </summary>
        private Vector3Int GetGridCellIndex(int linearIndex)
        {
            int nx = selectedData.gridResolution.x;
            int ny = selectedData.gridResolution.y;
            int iz = linearIndex / (nx * ny);
            int remainder = linearIndex % (nx * ny);
            int iy = remainder / nx;
            int ix = remainder % nx;
            return new Vector3Int(ix, iy, iz);
        }

        private bool IsGridMode
        {
            get { return selectedData != null && selectedData.storageMode == VolumeStorageMode.UniformGrid; }
        }

        /// <summary>
        /// 将节点的局部坐标变换到世界坐标（考虑 Volume 的 Transform）
        /// </summary>
        private void GetTransformedNodeBounds(Vector3 localCenter, Vector3 localHalfSize, out Vector3 worldCenter, out Vector3 worldHalfSize)
        {
            if (selectedVolume == null || selectedVolume.transformMode == TransformMode.None)
            {
                worldCenter = localCenter;
                worldHalfSize = localHalfSize;
                return;
            }

            Quaternion rotation = selectedVolume.GetEffectiveRotation();
            Vector3 effectivePos = selectedVolume.GetEffectivePosition();
            float effectiveScale = selectedVolume.GetEffectiveScale();

            // 将局部坐标变换到世界坐标
            Vector3 offsetFromBaked = localCenter - selectedData.rootCenter;
            Vector3 transformedOffset = rotation * (offsetFromBaked * effectiveScale);
            worldCenter = selectedData.rootCenter + effectivePos + transformedOffset;
            worldHalfSize = localHalfSize * effectiveScale;
        }

        private void FocusNodeInSceneView(OctreeNode node)
        {
            Vector3 localCenter;
            Vector3 localHalfSize;
            selectedData.GetNodeBounds(selectedNodeIndex, out localCenter, out localHalfSize);

            // 应用 Transform 变换
            Vector3 worldCenter;
            Vector3 worldHalfSize;
            GetTransformedNodeBounds(localCenter, localHalfSize, out worldCenter, out worldHalfSize);

            Vector3 size = worldHalfSize * 4f;

            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null)
            {
                sceneView.Frame(new Bounds(worldCenter, size), false);
            }
        }

        private void DrawStatisticsAndTools()
        {
            // Statistics
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Storage Mode", selectedData.storageMode.ToString());
            EditorGUILayout.LabelField("Total Nodes", selectedData.totalNodeCount.ToString());

            if (IsGridMode)
            {
                EditorGUILayout.LabelField("Grid Resolution",
                    $"{selectedData.gridResolution.x} x {selectedData.gridResolution.y} x {selectedData.gridResolution.z}");

                // Cell size
                if (selectedData.rootHalfExtents.x > 0)
                {
                    Vector3 cellSize = new Vector3(
                        selectedData.rootHalfExtents.x * 2f / selectedData.gridResolution.x,
                        selectedData.rootHalfExtents.y * 2f / selectedData.gridResolution.y,
                        selectedData.rootHalfExtents.z * 2f / selectedData.gridResolution.z
                    );
                    EditorGUILayout.LabelField("Cell Size", cellSize.ToString("F3"));
                }
            }
            else
            {
                EditorGUILayout.LabelField("Leaf Nodes", selectedData.leafNodeCount.ToString());
                EditorGUILayout.LabelField("Sparse Nodes", selectedData.sparseNodeCount.ToString());

                if (selectedData.leafNodeCount > 0)
                {
                    float sparseRatio = (float)selectedData.sparseNodeCount / selectedData.leafNodeCount * 100f;
                    EditorGUILayout.LabelField("Sparse Ratio", $"{sparseRatio:F2}%");

                    // Calculate theoretical full tree size
                    int theoreticalNodes = 0;
                    for (int d = 0; d <= selectedData.maxDepth; d++)
                    {
                        theoreticalNodes += (int)Mathf.Pow(8, d);
                    }
                    float compressionRatio = (1f - (float)selectedData.totalNodeCount / theoreticalNodes) * 100f;
                    EditorGUILayout.LabelField("Compression Ratio", $"{compressionRatio:F2}%");
                }
            }

            EditorGUILayout.LabelField("Memory Usage", $"{selectedData.GetMemoryUsageMB():F2} MB");

            if (!IsGridMode)
                EditorGUILayout.LabelField("Max Depth", selectedData.maxDepth.ToString());

            EditorGUILayout.LabelField("Root Center", selectedData.rootCenter.ToString("F3"));
            EditorGUILayout.LabelField("Root Half Extents", selectedData.rootHalfExtents.ToString("F3"));

            EditorGUILayout.Space();

            // Nodes per depth (Octree only)
            if (!IsGridMode)
            {
                EditorGUILayout.LabelField("Nodes per Depth", EditorStyles.boldLabel);
                if (selectedData.nodes != null)
                {
                    Dictionary<int, int> nodesPerDepth = new Dictionary<int, int>();
                    Dictionary<int, int> leafNodesPerDepth = new Dictionary<int, int>();

                    for (int i = 0; i < selectedData.nodes.Length; i++)
                    {
                        int depth = selectedData.nodes[i].depth;
                        if (!nodesPerDepth.ContainsKey(depth))
                        {
                            nodesPerDepth[depth] = 0;
                            leafNodesPerDepth[depth] = 0;
                        }
                        nodesPerDepth[depth]++;

                        if (selectedData.nodes[i].IsLeaf)
                            leafNodesPerDepth[depth]++;
                    }

                    foreach (var kvp in nodesPerDepth.OrderBy(x => x.Key))
                    {
                        int leafCount = leafNodesPerDepth.ContainsKey(kvp.Key) ? leafNodesPerDepth[kvp.Key] : 0;
                        EditorGUILayout.LabelField($"  Depth {kvp.Key}", $"{kvp.Value} nodes ({leafCount} leaves)");
                    }
                }
            }

            EditorGUILayout.Space();

            // Sampling Test
            EditorGUILayout.LabelField("Sampling Test", EditorStyles.boldLabel);

            // 显示 Volume Transform 信息
            if (selectedVolume != null && selectedVolume.transformMode != TransformMode.None)
            {
                EditorGUILayout.HelpBox(
                    $"Volume Transform 已启用 ({selectedVolume.transformMode})\n" +
                    "采样将自动应用 Transform 变换",
                    MessageType.Info);
            }

            testPosition = EditorGUILayout.Vector3Field("Test Position (World)", testPosition);
            testNormal = EditorGUILayout.Vector3Field("Test Normal (World)", testNormal).normalized;

            if (GUILayout.Button("Sample"))
            {
                // 如果有 Volume，使用 Volume 的采样方法（支持 Transform）
                if (selectedVolume != null)
                {
                    sampledColor = selectedVolume.SampleLighting(testPosition, testNormal);
                }
                else
                {
                    // 直接使用 Data 采样（不支持 Transform）
                    sampledColor = selectedData.SampleLighting(testPosition, testNormal);
                }
            }

            EditorGUILayout.ColorField("Sampled Color", sampledColor);

            EditorGUILayout.Space();

            // Tools
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);

            if (GUILayout.Button("Validate Data Integrity"))
            {
                ValidateData();
            }

            if (GUILayout.Button("Export to CSV"))
            {
                ExportToCSV();
            }

            if (GUILayout.Button("Refresh Statistics"))
            {
                selectedData.UpdateStatistics();
                EditorUtility.SetDirty(selectedData);
            }
        }

        private void ValidateData()
        {
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();

            // Check basic validity
            if (!selectedData.ValidateData(out string basicError))
            {
                errors.Add(basicError);
            }

            // Check octree structure (Octree mode only)
            if (selectedData.storageMode != VolumeStorageMode.UniformGrid)
            {
                List<string> octreeErrors;
                OctreeBuilder.ValidateOctree(selectedData.nodes, out octreeErrors);
                errors.AddRange(octreeErrors);

                // Check for duplicate Morton codes at same depth
                var mortonByDepth = new Dictionary<int, HashSet<uint>>();
                for (int i = 0; i < selectedData.nodes.Length; i++)
                {
                    var node = selectedData.nodes[i];
                    if (!mortonByDepth.ContainsKey(node.depth))
                        mortonByDepth[node.depth] = new HashSet<uint>();

                    if (!mortonByDepth[node.depth].Add(node.mortonCode))
                    {
                        warnings.Add($"Duplicate Morton code 0x{node.mortonCode:X8} at depth {node.depth}");
                    }
                }
            }
            else
            {
                // Grid mode validation
                int expectedNodes = selectedData.gridResolution.x * selectedData.gridResolution.y * selectedData.gridResolution.z;
                if (selectedData.nodes.Length != expectedNodes)
                {
                    errors.Add($"Grid node count mismatch: expected {expectedNodes} ({selectedData.gridResolution.x}x{selectedData.gridResolution.y}x{selectedData.gridResolution.z}), got {selectedData.nodes.Length}");
                }
            }

            // Display results
            string message = "";
            if (errors.Count > 0)
            {
                message += "ERRORS:\n" + string.Join("\n", errors.Take(10));
                if (errors.Count > 10)
                    message += $"\n... and {errors.Count - 10} more errors";
                message += "\n\n";
            }

            if (warnings.Count > 0)
            {
                message += "WARNINGS:\n" + string.Join("\n", warnings.Take(10));
                if (warnings.Count > 10)
                    message += $"\n... and {warnings.Count - 10} more warnings";
            }

            if (errors.Count == 0 && warnings.Count == 0)
            {
                EditorUtility.DisplayDialog("Validation", "Data integrity check passed!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Validation Results", message, "OK");
            }
        }

        private void ExportToCSV()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Octree Data to CSV",
                "",
                "octree_data.csv",
                "csv"
            );

            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                using (StreamWriter writer = new StreamWriter(path))
                {
                    if (IsGridMode)
                    {
                        // Grid mode CSV
                        writer.WriteLine("Index,CellX,CellY,CellZ,WorldX,WorldY,WorldZ,HalfSizeX,HalfSizeY,HalfSizeZ");

                        for (int i = 0; i < selectedData.nodes.Length; i++)
                        {
                            Vector3Int cellIdx = GetGridCellIndex(i);
                            Vector3 worldCenter;
                            Vector3 halfSize;
                            selectedData.GetNodeBounds(i, out worldCenter, out halfSize);

                            writer.WriteLine($"{i},{cellIdx.x},{cellIdx.y},{cellIdx.z},{worldCenter.x:F4},{worldCenter.y:F4},{worldCenter.z:F4},{halfSize.x:F4},{halfSize.y:F4},{halfSize.z:F4}");
                        }
                    }
                    else
                    {
                        // Octree mode CSV
                        writer.WriteLine("Index,MortonCode,MortonCodeHex,Depth,IsLeaf,IsSparse,ChildStartIndex,GridX,GridY,GridZ,WorldX,WorldY,WorldZ,HalfSize");

                        for (int i = 0; i < selectedData.nodes.Length; i++)
                        {
                            OctreeNode node = selectedData.nodes[i];
                            MortonCodeHelper.DecodeMorton3D(node.mortonCode, out uint gx, out uint gy, out uint gz);
                            Vector3 worldCenter;
                            Vector3 halfSize;
                            selectedData.GetNodeBounds(i, out worldCenter, out halfSize);

                            bool isSparse = node.IsLeaf && node.depth < selectedData.maxDepth;

                            writer.WriteLine($"{i},{node.mortonCode},0x{node.mortonCode:X8},{node.depth},{node.IsLeaf},{isSparse},{node.childStartIndex},{gx},{gy},{gz},{worldCenter.x:F4},{worldCenter.y:F4},{worldCenter.z:F4},{halfSize:F4}");
                        }
                    }
                }

                EditorUtility.DisplayDialog("Export Complete", $"Data exported to:\n{path}", "OK");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Export Error", e.Message, "OK");
            }
        }

        private void OnSelectionChange()
        {
            // Auto-select data from selected volume
            if (selectedData == null)
            {
                TryGetDataFromSelection();
                Repaint();
            }
        }

        private void OnEnable()
        {
            // 注册 Scene View 绘制回调
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            // 取消注册 Scene View 绘制回调
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        /// <summary>
        /// 在 Scene View 中绘制选中的节点（带 Transform 支持）
        /// </summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            if (selectedData == null || selectedData.nodes == null)
                return;

            // 绘制选中的节点
            if (selectedNodeIndex >= 0 && selectedNodeIndex < selectedData.nodes.Length)
            {
                DrawSelectedNodeInSceneView();
            }
        }

        /// <summary>
        /// 在 Scene View 中绘制选中的节点
        /// </summary>
        private void DrawSelectedNodeInSceneView()
        {
            OctreeNode node = selectedData.nodes[selectedNodeIndex];

            // 获取节点的局部坐标
            Vector3 localCenter;
            Vector3 localHalfSize;
            selectedData.GetNodeBounds(selectedNodeIndex, out localCenter, out localHalfSize);

            // 应用 Transform 变换
            Vector3 worldCenter;
            Vector3 worldHalfSize;
            GetTransformedNodeBounds(localCenter, localHalfSize, out worldCenter, out worldHalfSize);

            // 获取旋转
            Quaternion rotation = Quaternion.identity;
            if (selectedVolume != null && selectedVolume.transformMode != TransformMode.None)
            {
                rotation = selectedVolume.GetEffectiveRotation();
            }

            // 绘制选中节点（高亮显示）
            Handles.color = Color.cyan;
            if (rotation != Quaternion.identity)
            {
                Matrix4x4 oldMatrix = Handles.matrix;
                Handles.matrix = Matrix4x4.TRS(worldCenter, rotation, Vector3.one);
                Handles.DrawWireCube(Vector3.zero, worldHalfSize * 2f);
                Handles.matrix = oldMatrix;
            }
            else
            {
                Handles.DrawWireCube(worldCenter, worldHalfSize * 2f);
            }

            // 绘制节点中心点
            Handles.color = Color.yellow;
            Handles.SphereHandleCap(0, worldCenter, Quaternion.identity, worldHalfSize.magnitude * 0.2f, EventType.Repaint);

            // 绘制标签
            Handles.color = Color.white;
            if (IsGridMode)
            {
                Vector3Int cellIdx = GetGridCellIndex(selectedNodeIndex);
                Handles.Label(worldCenter + Vector3.up * worldHalfSize.magnitude,
                    $"Node {selectedNodeIndex}\nCell: ({cellIdx.x}, {cellIdx.y}, {cellIdx.z})");
            }
            else
            {
                Handles.Label(worldCenter + Vector3.up * worldHalfSize.magnitude,
                    $"Node {selectedNodeIndex}\nDepth: {node.depth}\nMorton: 0x{node.mortonCode:X8}");
            }

            // 绘制 8 个角点
            Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 cornerOffset = new Vector3(
                    (i & 1) != 0 ? worldHalfSize.x : -worldHalfSize.x,
                    (i & 2) != 0 ? worldHalfSize.y : -worldHalfSize.y,
                    (i & 4) != 0 ? worldHalfSize.z : -worldHalfSize.z
                );

                Vector3 cornerWorld;
                if (rotation != Quaternion.identity)
                {
                    cornerWorld = worldCenter + rotation * cornerOffset;
                }
                else
                {
                    cornerWorld = worldCenter + cornerOffset;
                }

                Handles.SphereHandleCap(0, cornerWorld, Quaternion.identity, worldHalfSize.magnitude * 0.08f, EventType.Repaint);
            }
        }
    }
}
