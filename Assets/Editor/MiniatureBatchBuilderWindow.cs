using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MiniatureAutomation
{
    public class MiniatureBatchBuilderWindow : EditorWindow
    {
        const string PrefKeyPrefix = "MiniatureAutomation.";

        GameObject sourcePrefabObject;
        int rendererIndex;
        int materialSlotIndex;
        string texturePropertyOverride = "";
        string pngSourceFolder = "";
        string generatedAssetsRoot = "Assets/GeneratedMinis";
        string bundleOutputRoot = "AssetBundles";
        bool buildWindows64 = true;
        bool buildOSX;
        bool buildLinux64;
        bool overwriteExisting = true;

        string[] rendererLabels = new string[0];
        string[] materialLabels = new string[0];

        [MenuItem("Tools/Miniature Batch Builder")]
        public static void Open()
        {
            GetWindow<MiniatureBatchBuilderWindow>("Miniature Batch Builder");
        }

        void OnEnable()
        {
            var prefabPath = EditorPrefs.GetString(PrefKeyPrefix + "sourcePrefabPath", "");
            if (!string.IsNullOrEmpty(prefabPath))
            {
                sourcePrefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }
            rendererIndex = EditorPrefs.GetInt(PrefKeyPrefix + "rendererIndex", 0);
            materialSlotIndex = EditorPrefs.GetInt(PrefKeyPrefix + "materialSlotIndex", 0);
            texturePropertyOverride = EditorPrefs.GetString(PrefKeyPrefix + "texProperty", "");
            pngSourceFolder = EditorPrefs.GetString(PrefKeyPrefix + "pngFolder", "");
            generatedAssetsRoot = EditorPrefs.GetString(PrefKeyPrefix + "generatedRoot", generatedAssetsRoot);
            bundleOutputRoot = EditorPrefs.GetString(PrefKeyPrefix + "bundleRoot", bundleOutputRoot);
            buildWindows64 = EditorPrefs.GetBool(PrefKeyPrefix + "buildWin", true);
            buildOSX = EditorPrefs.GetBool(PrefKeyPrefix + "buildOSX", false);
            buildLinux64 = EditorPrefs.GetBool(PrefKeyPrefix + "buildLinux", false);
            overwriteExisting = EditorPrefs.GetBool(PrefKeyPrefix + "overwrite", true);

            RefreshRendererOptions();
        }

        void SaveConfigToPrefs()
        {
            EditorPrefs.SetString(PrefKeyPrefix + "sourcePrefabPath", sourcePrefabObject != null ? AssetDatabase.GetAssetPath(sourcePrefabObject) : "");
            EditorPrefs.SetInt(PrefKeyPrefix + "rendererIndex", rendererIndex);
            EditorPrefs.SetInt(PrefKeyPrefix + "materialSlotIndex", materialSlotIndex);
            EditorPrefs.SetString(PrefKeyPrefix + "texProperty", texturePropertyOverride);
            EditorPrefs.SetString(PrefKeyPrefix + "pngFolder", pngSourceFolder);
            EditorPrefs.SetString(PrefKeyPrefix + "generatedRoot", generatedAssetsRoot);
            EditorPrefs.SetString(PrefKeyPrefix + "bundleRoot", bundleOutputRoot);
            EditorPrefs.SetBool(PrefKeyPrefix + "buildWin", buildWindows64);
            EditorPrefs.SetBool(PrefKeyPrefix + "buildOSX", buildOSX);
            EditorPrefs.SetBool(PrefKeyPrefix + "buildLinux", buildLinux64);
            EditorPrefs.SetBool(PrefKeyPrefix + "overwrite", overwriteExisting);
        }

        void RefreshRendererOptions()
        {
            if (sourcePrefabObject == null)
            {
                rendererLabels = new string[0];
                materialLabels = new string[0];
                return;
            }

            var renderers = sourcePrefabObject.GetComponentsInChildren<Renderer>(true);
            rendererLabels = renderers.Select((r, i) => $"{i}: {r.name}").ToArray();
            rendererIndex = Mathf.Clamp(rendererIndex, 0, Mathf.Max(0, renderers.Length - 1));

            if (renderers.Length > 0)
            {
                var mats = renderers[rendererIndex].sharedMaterials;
                materialLabels = mats.Select((m, i) => $"{i}: {(m != null ? m.name : "None")}").ToArray();
                materialSlotIndex = Mathf.Clamp(materialSlotIndex, 0, Mathf.Max(0, mats.Length - 1));
            }
            else
            {
                materialLabels = new string[0];
            }
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Source (already working) prefab", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            sourcePrefabObject = (GameObject)EditorGUILayout.ObjectField("Source Prefab", sourcePrefabObject, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                rendererIndex = 0;
                materialSlotIndex = 0;
                RefreshRendererOptions();
            }

            if (sourcePrefabObject != null && rendererLabels.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                rendererIndex = EditorGUILayout.Popup("Renderer", rendererIndex, rendererLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    materialSlotIndex = 0;
                    RefreshRendererOptions();
                }

                materialSlotIndex = EditorGUILayout.Popup("Material Slot", materialSlotIndex, materialLabels);
            }
            else if (sourcePrefabObject != null)
            {
                EditorGUILayout.HelpBox("Prefab has no Renderer components.", MessageType.Warning);
            }

            texturePropertyOverride = EditorGUILayout.TextField(
                new GUIContent("Texture Property (optional)", "Leave blank to auto-detect (_BaseMap / _MainTex)."),
                texturePropertyOverride);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Batch of amended PNGs", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            pngSourceFolder = EditorGUILayout.TextField("PNG Folder", pngSourceFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                var picked = EditorUtility.OpenFolderPanel("Folder with amended PNGs", pngSourceFolder, "");
                if (!string.IsNullOrEmpty(picked)) pngSourceFolder = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            generatedAssetsRoot = EditorGUILayout.TextField("Generated Assets Root", generatedAssetsRoot);
            bundleOutputRoot = EditorGUILayout.TextField("Asset Bundle Output Folder", bundleOutputRoot);
            overwriteExisting = EditorGUILayout.Toggle("Regenerate Existing Minis", overwriteExisting);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build Targets", EditorStyles.boldLabel);
            buildWindows64 = EditorGUILayout.Toggle("Windows64 (Tabletop Simulator)", buildWindows64);
            buildOSX = EditorGUILayout.Toggle("macOS", buildOSX);
            buildLinux64 = EditorGUILayout.Toggle("Linux64", buildLinux64);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(sourcePrefabObject == null || string.IsNullOrEmpty(pngSourceFolder)))
            {
                if (GUILayout.Button("Generate Miniatures + Build Asset Bundles", GUILayout.Height(32)))
                {
                    SaveConfigToPrefs();
                    RunBuild();
                }
            }
        }

        void RunBuild()
        {
            var config = new MiniatureBuildConfig
            {
                sourcePrefabPath = AssetDatabase.GetAssetPath(sourcePrefabObject),
                rendererIndex = rendererIndex,
                materialSlotIndex = materialSlotIndex,
                texturePropertyName = texturePropertyOverride,
                pngSourceFolder = pngSourceFolder,
                generatedAssetsRoot = generatedAssetsRoot,
                bundleOutputRoot = bundleOutputRoot,
                buildWindows64 = buildWindows64,
                buildOSX = buildOSX,
                buildLinux64 = buildLinux64,
                overwriteExisting = overwriteExisting,
            };

            var generated = MiniatureBundlePipeline.Run(config, out var errors);

            if (errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Miniature Batch Builder",
                    $"Completed with {errors.Count} issue(s):\n\n{string.Join("\n", errors)}\n\nGenerated/updated {generated.Count} miniature(s).",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Miniature Batch Builder",
                    $"Done. Generated/updated {generated.Count} miniature prefab(s) and built asset bundle(s) to '{bundleOutputRoot}'.",
                    "OK");
            }
        }
    }
}
