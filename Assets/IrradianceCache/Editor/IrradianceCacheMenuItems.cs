using UnityEngine;
using UnityEditor;

namespace IrradianceCacheSystem.Editor
{
    /// <summary>
    /// 创建菜单项
    /// </summary>
    public static class IrradianceCacheMenuItems
    {
        [MenuItem("GameObject/Light/Irradiance Cache", false, 10)]
        public static void CreateIrradianceCache(MenuCommand menuCommand)
        {
            // 创建新的 GameObject
            GameObject go = new GameObject("Irradiance Cache");

            // 添加组件
            IrradianceCache volume = go.AddComponent<IrradianceCache>();

            // 尝试自动计算边界
            volume.bounds = volume.CalculateAutoVolumeBounds();

            // 设置父对象
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // 注册撤销
            Undo.RegisterCreatedObjectUndo(go, "Create Irradiance Cache");

            // 选中新创建的对象
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/Light/Irradiance Cache Debugger", false, 11)]
        public static void CreateIrradianceCacheDebugger(MenuCommand menuCommand)
        {
            // 创建新的 GameObject
            GameObject go = new GameObject("Irradiance Cache Debugger");

            // 添加组件
            IrradianceCacheDebugger debugger = go.AddComponent<IrradianceCacheDebugger>();

            // 尝试找到场景中的 Volume
            debugger.targetVolume = Object.FindObjectOfType<IrradianceCache>();

            // 设置父对象
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // 注册撤销
            Undo.RegisterCreatedObjectUndo(go, "Create Irradiance Cache Debugger");

            // 选中新创建的对象
            Selection.activeObject = go;
        }

        [MenuItem("Window/Irradiance Cache/Visualizer", false, 100)]
        public static void OpenVisualizerWindow()
        {
            OctreeVisualizerWindow.ShowWindow();
        }

        [MenuItem("Window/Irradiance Cache/GPU Sampler Test", false, 101)]
        public static void OpenGPUSamplerTestWindow()
        {
            GPUSamplerTestWindow.ShowWindow();
        }

        [MenuItem("Window/Irradiance Cache/Documentation", false, 200)]
        public static void OpenDocumentation()
        {
            // 尝试打开文档文件
            string[] guids = AssetDatabase.FindAssets("IrradianceCache_TechnicalDesign");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset);
                    return;
                }
            }

            Debug.Log("Documentation file not found. Please check Assets/Docs folder.");
        }

        // 快捷操作菜单
        [MenuItem("Tools/Irradiance Cache/Bake Selected Volume", false, 100)]
        public static void BakeSelectedVolume()
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select a GameObject with IrradianceCache component.", "OK");
                return;
            }

            IrradianceCache volume = Selection.activeGameObject.GetComponent<IrradianceCache>();
            if (volume == null)
            {
                EditorUtility.DisplayDialog("Error", "Selected GameObject does not have IrradianceCache component.", "OK");
                return;
            }

            // 选中 Volume 并打开 Inspector
            Selection.activeObject = volume;
            EditorGUIUtility.PingObject(volume);

            Debug.Log("Please click 'Bake Irradiance Cache' button in the Inspector.");
        }

        [MenuItem("Tools/Irradiance Cache/Upload All Volumes to GPU", false, 101)]
        public static void UploadAllVolumesToGPU()
        {
            IrradianceCache[] volumes = Object.FindObjectsOfType<IrradianceCache>();

            int uploadedCount = 0;
            foreach (var volume in volumes)
            {
                if (volume.bakedData != null)
                {
                    volume.UploadToGPU();
                    uploadedCount++;
                }
            }

            Debug.Log($"Uploaded {uploadedCount} volume(s) to GPU.");
        }

        [MenuItem("Tools/Irradiance Cache/Release All GPU Resources", false, 102)]
        public static void ReleaseAllGPUResources()
        {
            IrradianceCache[] volumes = Object.FindObjectsOfType<IrradianceCache>();

            foreach (var volume in volumes)
            {
                volume.ReleaseGPUResources();
            }

            Debug.Log($"Released GPU resources from {volumes.Length} volume(s).");
        }

        // Validation methods
        [MenuItem("GameObject/Light/Irradiance Cache", true)]
        public static bool ValidateCreateIrradianceCache()
        {
            return true;
        }

        [MenuItem("GameObject/Light/Irradiance Cache Debugger", true)]
        public static bool ValidateCreateIrradianceCacheDebugger()
        {
            return true;
        }

        [MenuItem("Tools/Irradiance Cache/Bake Selected Volume", true)]
        public static bool ValidateBakeSelectedVolume()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponent<IrradianceCache>() != null;
        }
    }
}
