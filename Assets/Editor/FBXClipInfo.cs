using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class FBXClipInfo
{
    private const int AnimatedObjectCount = 1;
    private const string MenuPath = "Assets/烘焙物体动画";
    private const string TextureFolder = "Assets/Resources/Tex";
    private const string PrefabFolder = "Assets/Res/prefab";
    private const string MaterialFolder = "Assets/Res/materials";
    private const string ShaderName = "Custom/GPUAnim";

    private struct ClipInfo
    {
        public string clipName;
        public float clipLength;
        public int frameCount;
        public float frameRate;
        public AnimationClip clip;
    }

    [MenuItem(MenuPath)]
    private static void BakeGPUAnimation()
    {
        UnityEngine.Object selectedAsset = Selection.activeObject;
        if (selectedAsset == null)
        {
            Debug.LogWarning("Please select an FBX or model prefab asset.");
            return;
        }

        GameObject sourceObject = selectedAsset as GameObject;
        if (sourceObject == null)
        {
            Debug.LogWarning("The selected asset cannot be used as a GameObject.");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(selectedAsset);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogWarning("Only project assets can be baked.");
            return;
        }

        List<ClipInfo> clipInfos = LoadClipInfos(assetPath);
        int totalFrameCount = GetTotalFrameCount(clipInfos);
        if (totalFrameCount <= 0)
        {
            Debug.LogWarning("No valid AnimationClips were found in the selected asset.");
            return;
        }

        EnsureProjectFolders();

        Texture2D gpuTexX;
        Texture2D gpuTexY;
        Texture2D gpuTexZ;
        BakeClipMatrices(sourceObject, clipInfos, totalFrameCount, out gpuTexX, out gpuTexY, out gpuTexZ);

        string assetName = sourceObject.name;
        SaveTextureAsset(gpuTexX, TextureFolder + "/" + assetName + "_GPUTexX.asset");
        SaveTextureAsset(gpuTexY, TextureFolder + "/" + assetName + "_GPUTexY.asset");
        SaveTextureAsset(gpuTexZ, TextureFolder + "/" + assetName + "_GPUTexZ.asset");

        BakedClipsAsset bakedClipsAsset = CreateBakedClipsAsset(clipInfos, totalFrameCount, gpuTexX, gpuTexY, gpuTexZ);
        SaveBakedClipsAsset(bakedClipsAsset, TextureFolder + "/" + assetName + "_BakedClipsAsset.asset");
        GenerateGPUAnimationPrefab(sourceObject, bakedClipsAsset, PrefabFolder + "/" + assetName + ".prefab");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("GPU animation bake finished: " + assetName);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateBakeGPUAnimation()
    {
        UnityEngine.Object selectedAsset = Selection.activeObject;
        if (selectedAsset == null || !(selectedAsset is GameObject))
        {
            return false;
        }

        string assetPath = AssetDatabase.GetAssetPath(selectedAsset);
        return !string.IsNullOrEmpty(assetPath);
    }

    private static List<ClipInfo> LoadClipInfos(string assetPath)
    {
        List<ClipInfo> clipInfos = new List<ClipInfo>();
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

        foreach (UnityEngine.Object asset in assets)
        {
            AnimationClip clip = asset as AnimationClip;
            if (clip == null || IsPreviewClip(clip))
            {
                continue;
            }

            float frameRate = clip.frameRate;
            clipInfos.Add(new ClipInfo
            {
                clipName = clip.name,
                clipLength = clip.length,
                frameCount = Mathf.RoundToInt(clip.length * frameRate),
                frameRate = frameRate,
                clip = clip
            });
        }

        return clipInfos;
    }

    private static void BakeClipMatrices(
        GameObject sourceObject,
        List<ClipInfo> clipInfos,
        int totalFrameCount,
        out Texture2D gpuTexX,
        out Texture2D gpuTexY,
        out Texture2D gpuTexZ)
    {
        gpuTexX = CreateDataTexture(totalFrameCount);
        gpuTexY = CreateDataTexture(totalFrameCount);
        gpuTexZ = CreateDataTexture(totalFrameCount);

        Color[] texX = new Color[totalFrameCount];
        Color[] texY = new Color[totalFrameCount];
        Color[] texZ = new Color[totalFrameCount];

        GameObject instance = UnityEngine.Object.Instantiate(sourceObject, Vector3.zero, Quaternion.identity);
        int frameIndex = 0;

        foreach (ClipInfo clipInfo in clipInfos)
        {
            if (clipInfo.clip == null || clipInfo.frameRate <= 0f)
            {
                continue;
            }

            float frameDeltaTime = 1f / clipInfo.frameRate;
            int writtenFrameCount = 0;

            for (float time = 0f; time < clipInfo.clip.length && writtenFrameCount < clipInfo.frameCount; time += frameDeltaTime)
            {
                if (frameIndex >= totalFrameCount)
                {
                    break;
                }

                clipInfo.clip.SampleAnimation(instance, time);
                Matrix4x4 matrix = instance.transform.localToWorldMatrix;
                texX[frameIndex] = matrix.GetRow(0);
                texY[frameIndex] = matrix.GetRow(1);
                texZ[frameIndex] = matrix.GetRow(2);

                frameIndex++;
                writtenFrameCount++;
            }
        }

        UnityEngine.Object.DestroyImmediate(instance);

        gpuTexX.SetPixels(texX);
        gpuTexY.SetPixels(texY);
        gpuTexZ.SetPixels(texZ);
        gpuTexX.Apply();
        gpuTexY.Apply();
        gpuTexZ.Apply();
    }

    private static BakedClipsAsset CreateBakedClipsAsset(
        List<ClipInfo> clipInfos,
        int totalFrameCount,
        Texture2D gpuTexX,
        Texture2D gpuTexY,
        Texture2D gpuTexZ)
    {
        BakedClipsAsset bakedClipsAsset = ScriptableObject.CreateInstance<BakedClipsAsset>();
        bakedClipsAsset.AnimationsTexX = gpuTexX;
        bakedClipsAsset.AnimationsTexY = gpuTexY;
        bakedClipsAsset.AnimationsTexZ = gpuTexZ;
        bakedClipsAsset.totalFrames = totalFrameCount;

        foreach (ClipInfo clipInfo in clipInfos)
        {
            bakedClipsAsset.clips.Add(new BakedClipsAsset.clipInfo
            {
                clipName = clipInfo.clipName,
                startFrame = GetAnimationFrameOffset(clipInfos, clipInfo.clip),
                frameCount = clipInfo.frameCount,
                frameRate = clipInfo.frameRate
            });
        }

        return bakedClipsAsset;
    }

    private static void GenerateGPUAnimationPrefab(GameObject sourceObject, BakedClipsAsset bakedClipsAsset, string prefabPath)
    {
        GameObject instance = UnityEngine.Object.Instantiate(sourceObject, Vector3.zero, Quaternion.identity);
        Renderer targetRenderer = FindPlayableRenderer(instance);
        if (targetRenderer == null)
        {
            UnityEngine.Object.DestroyImmediate(instance);
            Debug.LogWarning("No Renderer with MeshFilter was found in the generated instance.");
            return;
        }

        GPUAnimationController controller = targetRenderer.GetComponent<GPUAnimationController>();
        if (controller == null)
        {
            controller = targetRenderer.gameObject.AddComponent<GPUAnimationController>();
        }

        controller.bakedClipsAsset = bakedClipsAsset;
        AssignGPUAnimationMaterial(targetRenderer, sourceObject.name);

        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(instance, uniquePath);
        UnityEngine.Object.DestroyImmediate(instance);
    }

    private static void AssignGPUAnimationMaterial(Renderer targetRenderer, string assetName)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning("Shader was not found: " + ShaderName);
            return;
        }

        Material material = new Material(shader);
        material.name = assetName + "_GPUAnim";
        string materialPath = AssetDatabase.GenerateUniqueAssetPath(MaterialFolder + "/" + material.name + ".mat");
        AssetDatabase.CreateAsset(material, materialPath);
        targetRenderer.sharedMaterial = material;
    }

    private static Renderer FindPlayableRenderer(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer candidate in renderers)
        {
            MeshFilter meshFilter = candidate.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                return candidate;
            }
        }

        return instance.GetComponent<Renderer>();
    }

    private static Texture2D CreateDataTexture(int totalFrameCount)
    {
        Texture2D texture = new Texture2D(totalFrameCount, AnimatedObjectCount, TextureFormat.RGBAHalf, false, true);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static void SaveTextureAsset(Texture2D texture, string path)
    {
        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
        AssetDatabase.CreateAsset(texture, uniquePath);
    }

    private static void SaveBakedClipsAsset(BakedClipsAsset bakedClipsAsset, string path)
    {
        string uniquePath = AssetDatabase.GenerateUniqueAssetPath(path);
        AssetDatabase.CreateAsset(bakedClipsAsset, uniquePath);
    }

    private static int GetTotalFrameCount(List<ClipInfo> clipInfos)
    {
        int totalFrameCount = 0;
        foreach (ClipInfo clipInfo in clipInfos)
        {
            totalFrameCount += clipInfo.frameCount;
        }

        return totalFrameCount;
    }

    private static int GetAnimationFrameOffset(List<ClipInfo> clipInfos, AnimationClip clip)
    {
        int offset = 0;
        foreach (ClipInfo clipInfo in clipInfos)
        {
            if (clipInfo.clip == clip)
            {
                break;
            }

            offset += clipInfo.frameCount;
        }

        return offset;
    }

    private static void EnsureProjectFolders()
    {
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "Tex");
        EnsureFolder("Assets", "Res");
        EnsureFolder("Assets/Res", "prefab");
        EnsureFolder("Assets/Res", "materials");
    }

    private static void EnsureFolder(string parentFolder, string folderName)
    {
        string folderPath = parentFolder + "/" + folderName;
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }
    }

    private static bool IsPreviewClip(AnimationClip clip)
    {
        if (clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (clip.name.IndexOf("preview", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return (clip.hideFlags & HideFlags.HideInHierarchy) != 0;
    }
}
