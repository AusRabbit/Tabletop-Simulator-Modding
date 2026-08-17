using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniatureAutomation
{
    [Serializable]
    public class MiniatureBuildConfig
    {
        public string sourcePrefabPath;
        public int rendererIndex;
        public int materialSlotIndex;
        public string texturePropertyName = "";
        public string pngSourceFolder;
        public string generatedAssetsRoot = "Assets/GeneratedMinis";
        public string bundleOutputRoot = "AssetBundles";
        public bool buildWindows64 = true;
        public bool buildOSX;
        public bool buildLinux64;
        public bool overwriteExisting = true;
    }

    public static class MiniatureBundlePipeline
    {
        class Item
        {
            public string pngFile;
            public string safeName;
            public string folder;
            public string texturePath;
            public string materialPath;
            public string prefabPath;
        }

        public static List<string> Run(MiniatureBuildConfig config, out List<string> errors)
        {
            errors = new List<string>();
            var generatedPrefabPaths = new List<string>();

            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(config.sourcePrefabPath);
            if (sourcePrefab == null)
            {
                errors.Add($"Source prefab not found at '{config.sourcePrefabPath}'.");
                return generatedPrefabPaths;
            }

            var renderers = sourcePrefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                errors.Add("Source prefab has no Renderer components.");
                return generatedPrefabPaths;
            }
            if (config.rendererIndex < 0 || config.rendererIndex >= renderers.Length)
            {
                errors.Add($"Renderer index {config.rendererIndex} is out of range (prefab has {renderers.Length}).");
                return generatedPrefabPaths;
            }

            var sourceMaterials = renderers[config.rendererIndex].sharedMaterials;
            if (config.materialSlotIndex < 0 || config.materialSlotIndex >= sourceMaterials.Length)
            {
                errors.Add($"Material slot {config.materialSlotIndex} is out of range (renderer has {sourceMaterials.Length}).");
                return generatedPrefabPaths;
            }

            var sourceMaterial = sourceMaterials[config.materialSlotIndex];
            if (sourceMaterial == null)
            {
                errors.Add("Selected material slot is empty.");
                return generatedPrefabPaths;
            }
            var sourceMaterialPath = AssetDatabase.GetAssetPath(sourceMaterial);

            var texProperty = config.texturePropertyName;
            if (string.IsNullOrEmpty(texProperty))
            {
                texProperty = AutoDetectTextureProperty(sourceMaterial);
                if (texProperty == null)
                {
                    errors.Add("Could not auto-detect a texture property on the source material; set Texture Property Name explicitly.");
                    return generatedPrefabPaths;
                }
            }
            else if (!sourceMaterial.HasProperty(texProperty))
            {
                errors.Add($"Source material has no texture property named '{texProperty}'.");
                return generatedPrefabPaths;
            }

            var referenceTexture = sourceMaterial.GetTexture(texProperty);
            var referenceTexturePath = referenceTexture != null ? AssetDatabase.GetAssetPath(referenceTexture) : null;

            if (!Directory.Exists(config.pngSourceFolder))
            {
                errors.Add($"PNG source folder '{config.pngSourceFolder}' does not exist.");
                return generatedPrefabPaths;
            }

            var pngFiles = Directory.GetFiles(config.pngSourceFolder, "*.png", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pngFiles.Count == 0)
            {
                errors.Add($"No .png files found in '{config.pngSourceFolder}'.");
                return generatedPrefabPaths;
            }

            var items = new List<Item>();
            var seenNames = new HashSet<string>();
            foreach (var pngFile in pngFiles)
            {
                var baseName = Path.GetFileNameWithoutExtension(pngFile);
                var safeName = Sanitize(baseName);
                if (string.IsNullOrEmpty(safeName))
                {
                    errors.Add($"Skipping '{Path.GetFileName(pngFile)}': produces an empty bundle name after sanitizing.");
                    continue;
                }
                if (!seenNames.Add(safeName))
                {
                    errors.Add($"Skipping '{Path.GetFileName(pngFile)}': name collides with another PNG after sanitizing to '{safeName}'.");
                    continue;
                }

                var folder = $"{config.generatedAssetsRoot}/{safeName}";
                items.Add(new Item
                {
                    pngFile = pngFile,
                    safeName = safeName,
                    folder = folder,
                    texturePath = $"{folder}/{safeName}.png",
                    materialPath = $"{folder}/{safeName}_mat.mat",
                    prefabPath = $"{folder}/{safeName}.prefab",
                });
            }

            Directory.CreateDirectory(config.generatedAssetsRoot);
            foreach (var item in items)
            {
                Directory.CreateDirectory(item.folder);
            }
            AssetDatabase.Refresh();

            var builds = new List<AssetBundleBuild>();

            foreach (var item in items)
            {
                var prefabExists = File.Exists(item.prefabPath);
                if (!config.overwriteExisting && prefabExists)
                {
                    generatedPrefabPaths.Add(item.prefabPath);
                    builds.Add(new AssetBundleBuild { assetBundleName = item.safeName, assetNames = new[] { item.prefabPath } });
                    continue;
                }

                File.Copy(item.pngFile, item.texturePath, true);
                AssetDatabase.ImportAsset(item.texturePath, ImportAssetOptions.ForceUpdate);

                if (!string.IsNullOrEmpty(referenceTexturePath))
                {
                    CopyTextureImportSettings(referenceTexturePath, item.texturePath);
                }

                var newTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(item.texturePath);
                if (newTexture == null)
                {
                    errors.Add($"Failed to import texture for '{item.safeName}'.");
                    continue;
                }

                if (File.Exists(item.materialPath))
                {
                    AssetDatabase.DeleteAsset(item.materialPath);
                }
                AssetDatabase.CopyAsset(sourceMaterialPath, item.materialPath);
                var newMaterial = AssetDatabase.LoadAssetAtPath<Material>(item.materialPath);
                if (newMaterial == null)
                {
                    errors.Add($"Failed to duplicate material for '{item.safeName}'.");
                    continue;
                }
                newMaterial.SetTexture(texProperty, newTexture);
                EditorUtility.SetDirty(newMaterial);

                var root = PrefabUtility.LoadPrefabContents(config.sourcePrefabPath);
                try
                {
                    var targetRenderers = root.GetComponentsInChildren<Renderer>(true);
                    var targetRenderer = targetRenderers[config.rendererIndex];
                    var mats = targetRenderer.sharedMaterials;
                    mats[config.materialSlotIndex] = newMaterial;
                    targetRenderer.sharedMaterials = mats;

                    PrefabUtility.SaveAsPrefabAsset(root, item.prefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                var prefabImporter = AssetImporter.GetAtPath(item.prefabPath);
                prefabImporter.SetAssetBundleNameAndVariant(item.safeName, "");

                generatedPrefabPaths.Add(item.prefabPath);
                builds.Add(new AssetBundleBuild { assetBundleName = item.safeName, assetNames = new[] { item.prefabPath } });
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (builds.Count == 0)
            {
                errors.Add("No prefabs were generated or reused; skipping asset bundle build.");
                return generatedPrefabPaths;
            }

            var targets = new List<BuildTarget>();
            if (config.buildWindows64) targets.Add(BuildTarget.StandaloneWindows64);
            if (config.buildOSX) targets.Add(BuildTarget.StandaloneOSX);
            if (config.buildLinux64) targets.Add(BuildTarget.StandaloneLinux64);

            if (targets.Count == 0)
            {
                errors.Add("No build target selected; prefabs were generated but no bundles were built.");
                return generatedPrefabPaths;
            }

            foreach (var target in targets)
            {
                var outDir = Path.Combine(config.bundleOutputRoot, target.ToString());
                Directory.CreateDirectory(outDir);
                var manifest = BuildPipeline.BuildAssetBundles(outDir, builds.ToArray(), BuildAssetBundleOptions.None, target);
                if (manifest == null)
                {
                    errors.Add($"Asset bundle build failed for target {target}.");
                }
            }

            return generatedPrefabPaths;
        }

        static string AutoDetectTextureProperty(Material material)
        {
            foreach (var candidate in new[] { "_BaseMap", "_MainTex" })
            {
                if (material.HasProperty(candidate)) return candidate;
            }

            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    return shader.GetPropertyName(i);
                }
            }
            return null;
        }

        static void CopyTextureImportSettings(string sourcePath, string destPath)
        {
            var src = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            var dst = AssetImporter.GetAtPath(destPath) as TextureImporter;
            if (src == null || dst == null) return;

            dst.textureType = src.textureType;
            dst.sRGBTexture = src.sRGBTexture;
            dst.alphaIsTransparency = src.alphaIsTransparency;
            dst.alphaSource = src.alphaSource;
            dst.mipmapEnabled = src.mipmapEnabled;
            dst.wrapMode = src.wrapMode;
            dst.filterMode = src.filterMode;
            dst.maxTextureSize = src.maxTextureSize;
            dst.textureCompression = src.textureCompression;
            dst.crunchedCompression = src.crunchedCompression;
            dst.isReadable = src.isReadable;
            dst.SaveAndReimport();
        }

        public static string Sanitize(string name)
        {
            var lowered = name.ToLowerInvariant();
            var replaced = Regex.Replace(lowered, @"[^a-z0-9_\-]+", "_");
            return replaced.Trim('_');
        }
    }
}
