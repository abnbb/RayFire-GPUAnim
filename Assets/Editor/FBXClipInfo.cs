using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEditor;
using UnityEngine;

public static class FBXClipInfo
{
    // private int AnimatedObjectCount = 0;
    private const string MenuPath = "Assets/烘焙物体动画";
    private const string TextureFolder = "Assets/Resources/Tex";
    private const string PrefabFolder = "Assets/Res/prefab";
    private const string MaterialFolder = "Assets/Res/materials";
    private const string MeshFolder = "Assets/Resources/meshes";
    private const string ShaderName = "Custom/GPUAnim";

    private struct ClipInfo
    {
        public string clipName;
        public float clipLength;
        public int frameCount;
        public float frameRate;
        public AnimationClip clip;
    }

    private struct ObjInfo
    {
        public Matrix4x4 BindPose;
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
        int AnimatedObjectCount = CountRenderableObjec(sourceObject);
        if (AnimatedObjectCount <= 0)
        {
            Debug.LogWarning("No renderable MeshFilter objects were found in the selected asset.");
            return;
        }


        Texture2D gpuTexX;
        Texture2D gpuTexY;
        Texture2D gpuTexZ;
        BakeClipMatrices(sourceObject, clipInfos, totalFrameCount, AnimatedObjectCount, out gpuTexX, out gpuTexY, out gpuTexZ);

        string assetName = sourceObject.name;
        SaveTextureAsset(gpuTexX, TextureFolder + "/" + assetName + "_GPUTexX.asset");
        SaveTextureAsset(gpuTexY, TextureFolder + "/" + assetName + "_GPUTexY.asset");
        SaveTextureAsset(gpuTexZ, TextureFolder + "/" + assetName + "_GPUTexZ.asset");

        BakedClipsAsset bakedClipsAsset = CreateBakedClipsAsset(clipInfos, totalFrameCount,AnimatedObjectCount ,gpuTexX, gpuTexY, gpuTexZ);
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

    private static void SaveObjectIndex(GameObject instance, string assetName)
    {
        List<Transform> renderableObjects = CollectRenderableObjects(instance);
        int animatedObjectCount = renderableObjects.Count;
        if (animatedObjectCount == 0)
        {
            return;
        }

        for (int i = 0; i < animatedObjectCount; i++)
        {
            Transform childTransform = renderableObjects[i];
            MeshFilter meshFilter = childTransform.GetComponent<MeshFilter>();
            Mesh mesh = UnityEngine.Object.Instantiate(meshFilter.sharedMesh);
            mesh.name = assetName + "_" + childTransform.name + "_GPUAnimMesh_" + i;

            int vertexCount = mesh.vertexCount;
            List<Vector2> indexColors = new List<Vector2>();
            Vector2 indexColor = new Vector2((i + 0.5f) / animatedObjectCount, 0f);
            for (int j = 0; j < vertexCount; j++)
            {
                indexColors.Add(indexColor);
            }

            mesh.SetUVs(1, indexColors);
            meshFilter.sharedMesh = mesh;
            EnsureFolder(MeshFolder, assetName);
            SaveMeshAsset(mesh, MeshFolder + "/" + assetName + "/" + mesh.name + ".asset");
        }
    }

    private static int CountRenderableObjec(GameObject sourceObject)
    {
        return CollectRenderableObjects(sourceObject).Count;
    }

    private static List<Transform> CollectRenderableObjects(GameObject sourceObject)
    {
        List<Transform> renderableObjects = new List<Transform>();
        foreach (Transform child in sourceObject.GetComponentsInChildren<Transform>(true))
        {
            Renderer renderer = child.GetComponent<Renderer>();
            MeshFilter meshFilter = child.GetComponent<MeshFilter>();
            if (renderer != null && meshFilter != null && meshFilter.sharedMesh != null)
            {
                renderableObjects.Add(child);
            }
        }

        return renderableObjects;
    }
    private static List<ObjInfo> loadObjInfos(List<Transform> ObjTrans)
    {
        List<ObjInfo> objInfos = new List<ObjInfo>();
        foreach(var obj in ObjTrans)
        {
            ObjInfo oi = new ObjInfo();
            oi.BindPose = obj.worldToLocalMatrix;
            objInfos.Add(oi);
        }
        return objInfos;
    }

    // private static ObjInfo loadObjInfo(GameObject instance)
    // {
    //     var o = new ObjInfo();
    //     o.BindPose = instance.GetComponent<Transform>().worldToLocalMatrix;
    //     return ;
    // }

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
        int AnimatedObjectCount,
        out Texture2D gpuTexX,
        out Texture2D gpuTexY,
        out Texture2D gpuTexZ)
    {
        gpuTexX = CreateDataTexture(totalFrameCount, AnimatedObjectCount);
        gpuTexY = CreateDataTexture(totalFrameCount, AnimatedObjectCount);
        gpuTexZ = CreateDataTexture(totalFrameCount, AnimatedObjectCount);

        Color[] texX = new Color[AnimatedObjectCount*totalFrameCount];
        Color[] texY = new Color[texX.Length];
        Color[] texZ = new Color[texX.Length];

        GameObject instance = UnityEngine.Object.Instantiate(sourceObject, Vector3.zero, Quaternion.identity);
        
        List<Transform> ObjTrans = CollectRenderableObjects(instance);
        List<ObjInfo> objInfos = loadObjInfos(ObjTrans);


        for (int i = 0; i < ObjTrans.Count; i++)
        {
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
                    Matrix4x4 matrix = ObjTrans[i].localToWorldMatrix;
                    var index = i * totalFrameCount + frameIndex;
                    texX[index] = matrix.GetRow(0);
                    texY[index] = matrix.GetRow(1);
                    texZ[index] = matrix.GetRow(2);

                    frameIndex++;
                    writtenFrameCount++;
                }
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
        int amountOfObjects,
        Texture2D gpuTexX,
        Texture2D gpuTexY,
        Texture2D gpuTexZ)
    {
        BakedClipsAsset bakedClipsAsset = ScriptableObject.CreateInstance<BakedClipsAsset>();
        bakedClipsAsset.AnimationsTexX = gpuTexX;
        bakedClipsAsset.AnimationsTexY = gpuTexY;
        bakedClipsAsset.AnimationsTexZ = gpuTexZ;
        bakedClipsAsset.totalFrames = totalFrameCount;
        bakedClipsAsset.AmountOfObjects = amountOfObjects;

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
        SaveObjectIndex(instance, sourceObject.name);

        GPUAnimationController controller = instance.GetComponent<GPUAnimationController>();
        if (controller == null)
        {
            controller = instance.AddComponent<GPUAnimationController>();
        }

        controller.bakedClipsAsset = bakedClipsAsset;

        foreach(Transform child in CollectRenderableObjects(instance))
        {
            Renderer targetRenderer = child.GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                continue;
            }

            
            AssignGPUAnimationMaterial(targetRenderer, sourceObject.name);
            Debug.Log($"Assigned GPU animation material to {child.name} in prefab {sourceObject.name}");
        }
        DeleteAssetIfExists(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
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
        string materialName = assetName + "_GPUAnim";
        string materialPath = MaterialFolder + "/" + materialName + ".mat";
        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (existingMaterial != null)
        {
            targetRenderer.sharedMaterial = existingMaterial;
            return;
        }

        Material material = new Material(shader);
        material.name = materialName;
        DeleteAssetIfExists(materialPath);
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

    private static Texture2D CreateDataTexture(int totalFrameCount,int AnimatedObjectCount)
    {
        Texture2D texture = new Texture2D(totalFrameCount, AnimatedObjectCount, TextureFormat.RGBAHalf, false, true);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static void SaveTextureAsset(Texture2D texture, string path)
    {
        DeleteAssetIfExists(path);
        AssetDatabase.CreateAsset(texture, path);
    }

    private static void SaveBakedClipsAsset(BakedClipsAsset bakedClipsAsset, string path)
    {
        DeleteAssetIfExists(path);
        AssetDatabase.CreateAsset(bakedClipsAsset, path);
    }

    private static void SaveMeshAsset(Mesh mesh, string path)
    {
        DeleteAssetIfExists(path);
        AssetDatabase.CreateAsset(mesh, path);
    }

    private static void DeleteAssetIfExists(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
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
        EnsureFolder("Assets/Resources", "meshes");
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
