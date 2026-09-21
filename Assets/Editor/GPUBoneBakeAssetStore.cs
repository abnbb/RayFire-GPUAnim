using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal sealed class GPUBoneBakeAssetStore
{
    private const string OwnershipPrefix = "GPUBoneBake:";
    private const string LogPrefix = "[骨骼烘焙资产]";

    [Serializable]
    private sealed class Ownership
    {
        public string sourceGuid;
        public string assetGuid;
    }

    private sealed class Output
    {
        public string legacyPath;
        public string path;
        public Type type;
    }

    private readonly string sourceGuid;
    private readonly string assetName;
    private readonly string[] folders;
    private readonly List<Output> outputs = new List<Output>();
    private readonly HashSet<string> previousGuids = new HashSet<string>();
    private readonly HashSet<string> writtenGuids = new HashSet<string>();
    private int createdCount;
    private int updatedCount;
    private int migratedCount;

    public string PrefabPath => folders[0] + "/" + assetName + ".prefab";
    public string MaterialPath => folders[1] + "/" + assetName + "_GPUBoneAnim.mat";
    public string ClipsPath => folders[3] + "/" + assetName + "_BakedClipsAsset.asset";

    public GPUBoneBakeAssetStore(string sourcePath, string modelName, string bakedName,
        string prefabFolder, string materialFolder, string meshFolder, string textureFolder, int meshCount)
    {
        sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
        assetName = bakedName;
        if (string.IsNullOrEmpty(sourceGuid) || string.IsNullOrWhiteSpace(modelName)
            || modelName.EndsWith(".", StringComparison.Ordinal) || modelName.EndsWith(" ", StringComparison.Ordinal)
            || modelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || assetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("源模型路径或名称无效，无法确定烘焙输出目录。");
        }

        folders = new[] { prefabFolder + "/" + modelName, materialFolder + "/" + modelName,
            meshFolder + "/" + modelName, textureFolder + "/" + modelName };
        AddOutput(prefabFolder + "/" + assetName + ".prefab", PrefabPath, typeof(GameObject));
        AddOutput(materialFolder + "/" + assetName + "_GPUBoneAnim.mat", MaterialPath, typeof(Material));
        AddOutput(textureFolder + "/" + assetName + "_BakedClipsAsset.asset", ClipsPath, typeof(BakedClipsAsset));
        foreach (string axis in new[] { "X", "Y", "Z" })
        {
            AddOutput(textureFolder + "/" + assetName + "_GPUTex" + axis + ".asset", TexturePath(axis), typeof(Texture2D));
        }
        for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            AddOutput(meshFolder + "/" + assetName + "/" + MeshName(meshIndex) + ".asset", MeshPath(meshIndex), typeof(Mesh));
        }

        string legacyMeshFolder = meshFolder + "/" + assetName;
        if (AssetDatabase.IsValidFolder(legacyMeshFolder))
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { legacyMeshFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                string prefix = assetName + "_GPUABonenimMesh_";
                if (Path.GetDirectoryName(path).Replace('\\', '/') == legacyMeshFolder
                    && fileName.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(fileName.Substring(prefix.Length), out int meshIndex) && meshIndex >= 0
                    && fileName == MeshName(meshIndex))
                {
                    AddOutput(path, MeshPath(meshIndex), typeof(Mesh));
                }
            }
        }

        if (!string.IsNullOrEmpty(ReadOwner(sourcePath)))
            throw new InvalidOperationException("不能将已烘焙生成的 Prefab 作为源模型，请选择原始模型。");

        foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (guid == sourceGuid || !string.IsNullOrEmpty(ReadOwner(path)))
                continue;

            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), modelName, StringComparison.OrdinalIgnoreCase)
                || (candidate != null && string.Equals(candidate.name + "_BakeInstance", assetName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"不同源模型名称相同，已停止烘焙。请先改名：{sourcePath} 与 {path}");
        }
    }

    public string TexturePath(string axis) => folders[3] + "/" + assetName + "_GPUTex" + axis + ".asset";
    public string MeshName(int meshIndex) => assetName + "_GPUABonenimMesh_" + meshIndex;
    public string MeshPath(int meshIndex) => folders[2] + "/" + MeshName(meshIndex) + ".asset";

    public void Prepare()
    {
        foreach (string folder in folders)
        {
            if (File.Exists(folder) || (Directory.Exists(folder) && !AssetDatabase.IsValidFolder(folder)))
                throw new InvalidOperationException($"模型输出目录被文件占用或尚未导入，请先处理：{folder}");
            if (!AssetDatabase.IsValidFolder(folder))
                continue;

            foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string owner = ReadOwner(path);
                if (!string.IsNullOrEmpty(owner) && owner != sourceGuid)
                    throw new InvalidOperationException($"模型目录包含其他源模型的产物，禁止覆盖：{path}");
                if (owner == sourceGuid && !AssetDatabase.IsValidFolder(path))
                    previousGuids.Add(guid);
            }
        }

        foreach (Output output in outputs)
        {
            bool legacyExists = Exists(output.legacyPath);
            bool targetExists = Exists(output.path);
            if (legacyExists && targetExists && output.legacyPath != output.path)
                throw new InvalidOperationException($"新旧路径同时存在资源，无法安全合并 GUID：{output.legacyPath} 与 {output.path}");
            if (legacyExists)
                ValidateExisting(output.legacyPath, output.type, true);
            if (targetExists)
                ValidateExisting(output.path, output.type, output.path == output.legacyPath);
        }

        foreach (string folder in folders)
            EnsureFolder(folder);

        foreach (Output output in outputs)
        {
            if (Exists(output.legacyPath) && output.legacyPath != output.path)
            {
                string error = AssetDatabase.MoveAsset(output.legacyPath, output.path);
                if (!string.IsNullOrEmpty(error))
                    throw new IOException($"迁移失败：{output.legacyPath} -> {output.path}：{error}");
                migratedCount++;
                Debug.Log($"{LogPrefix} 迁移并保留 GUID：{output.legacyPath} -> {output.path}");
            }
            if (Exists(output.path))
            {
                MarkOwned(output.path);
                previousGuids.Add(AssetDatabase.AssetPathToGUID(output.path));
            }
        }
    }

    public T Save<T>(T generatedAsset, string path) where T : UnityEngine.Object
    {
        try
        {
            ValidateOutputPath(path, typeof(T));
            T existingAsset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existingAsset != null)
            {
                EditorUtility.CopySerialized(generatedAsset, existingAsset);
                EditorUtility.SetDirty(existingAsset);
                RecordWrite(path, false);
                return existingAsset;
            }

            AssetDatabase.CreateAsset(generatedAsset, path);
            if (!AssetDatabase.Contains(generatedAsset))
                throw new IOException($"创建烘焙资产失败：{path}");
            RecordWrite(path, true);
            return generatedAsset;
        }
        finally
        {
            if (generatedAsset != null && !AssetDatabase.Contains(generatedAsset))
                UnityEngine.Object.DestroyImmediate(generatedAsset);
        }
    }

    public Material GetOrCreateMaterial(Shader shader)
    {
        ValidateOutputPath(MaterialPath, typeof(Material));
        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existingMaterial != null)
        {
            RecordWrite(MaterialPath, false);
            return existingMaterial;
        }
        return Save(new Material(shader) { name = assetName + "_GPUBoneAnim" }, MaterialPath);
    }

    public void SavePrefab(GameObject root)
    {
        ValidateOutputPath(PrefabPath, typeof(GameObject));
        bool existed = Exists(PrefabPath);
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
        if (!success)
            throw new IOException($"保存 Prefab 失败：{PrefabPath}");
        RecordWrite(PrefabPath, !existed);
    }

    public void DeleteUnusedOutputs()
    {
        int deletedCount = 0;
        foreach (string guid in previousGuids)
        {
            if (writtenGuids.Contains(guid))
                continue;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                continue;
            if (!IsInOutputFolders(path) || AssetDatabase.IsValidFolder(path) || ReadOwner(path) != sourceGuid)
                throw new InvalidOperationException($"废弃产物超出管理范围，拒绝删除：{path}");
            if (!AssetDatabase.DeleteAsset(path))
                throw new IOException($"删除废弃烘焙产物失败：{path}");
            deletedCount++;
            Debug.Log($"{LogPrefix} 删除废弃产物：{path}");
        }
        Debug.Log($"{LogPrefix} 保存完成：新增 {createdCount}，更新/复用 {updatedCount}，迁移 {migratedCount}，删除废弃产物 {deletedCount}。Prefab：{PrefabPath}");
    }

    private void AddOutput(string legacyPath, string path, Type type)
    {
        if (!outputs.Exists(output => output.path == path))
            outputs.Add(new Output { legacyPath = legacyPath, path = path, type = type });
    }

    private void ValidateOutputPath(string path, Type type)
    {
        if (!outputs.Exists(output => output.path == path && output.type == type))
            throw new InvalidOperationException($"未预检的烘焙输出：{path}");
        if (Exists(path))
            ValidateExisting(path, type, false);
    }

    private void ValidateExisting(string path, Type type, bool allowLegacy)
    {
        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null || asset.GetType() != type)
            throw new InvalidOperationException($"输出路径已被其他类型或无法读取的资源占用：{path}");
        string owner = ReadOwner(path);
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (owner == sourceGuid)
            return;
        if (!allowLegacy || !string.IsNullOrEmpty(owner) || importer == null || !string.IsNullOrEmpty(importer.userData))
            throw new InvalidOperationException($"资源不属于当前模型，禁止覆盖或迁移：{path}");
    }

    private void RecordWrite(string path, bool created)
    {
        MarkOwned(path);
        string guid = AssetDatabase.AssetPathToGUID(path);
        if (writtenGuids.Add(guid))
        {
            if (created) createdCount++;
            else updatedCount++;
        }
    }

    private void MarkOwned(string path)
    {
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new IOException($"无法记录烘焙资产来源：{path}");
        importer.userData = OwnershipPrefix + JsonUtility.ToJson(new Ownership
        {
            sourceGuid = sourceGuid,
            assetGuid = AssetDatabase.AssetPathToGUID(path)
        });
        EditorUtility.SetDirty(importer);
        AssetDatabase.WriteImportSettingsIfDirty(path);
    }

    private static string ReadOwner(string path)
    {
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (importer == null || string.IsNullOrEmpty(importer.userData)
            || !importer.userData.StartsWith(OwnershipPrefix, StringComparison.Ordinal))
            return null;
        Ownership ownership = JsonUtility.FromJson<Ownership>(importer.userData.Substring(OwnershipPrefix.Length));
        if (ownership == null || string.IsNullOrEmpty(ownership.sourceGuid))
            throw new InvalidOperationException($"烘焙资产来源标记损坏：{path}");
        if (ownership.assetGuid != AssetDatabase.AssetPathToGUID(path))
            return null;
        return ownership.sourceGuid;
    }

    private bool IsInOutputFolders(string path)
    {
        foreach (string folder in folders)
        {
            if (path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool Exists(string path)
    {
        return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets))
            || File.Exists(path) || Directory.Exists(path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))) || !AssetDatabase.IsValidFolder(path))
            throw new IOException($"创建输出目录失败：{path}");
    }
}
