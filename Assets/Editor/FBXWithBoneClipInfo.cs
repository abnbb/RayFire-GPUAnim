using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
// using StreamHelper;

public static class FBXWithBoneClipInfo
{
    // private int BonesCount = 0;
    private const string MenuPath = "Assets/烘焙骨骼动画";
    private const string TextureFolder = "Assets/Resources/Tex";
    private const string PrefabFolder = "Assets/Res/prefab";
    private const string MaterialFolder = "Assets/Res/materials";
    private const string MeshFolder = "Assets/Resources/meshes";
    private const string ShaderName = "Custom/GPUBoneAnim";
    private const string AttachBoneLogPrefix = "[挂点烘焙]";
    private const int AttachBoneTRSStride = sizeof(float) * 8;
    // private List<string> attachBones = new List<string>();
    private struct ClipInfo
    {
        public string clipName;
        public float clipLength;
        public int frameCount;
        public float frameRate;
        public AnimationClip clip;
    }
    private struct AnimManager
    {
        public List<ClipInfo> clipInfos;
        public int totalFrameCount;
    }
    private struct ObjInfo
    {
        public string BoneName;
        public Matrix4x4 BindPose;
        public Transform ObjTrans;
        public int AttachBoneIndex;
    }
    private struct BonesManager
    {
        public List<ObjInfo> boneInfos;
        public int totalBoneCount;
        public List<string> attachBones;
        public List<BoneTRS> attachBoneTRS;
    }

    private struct BoneTRS
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
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
        //get attachbones from the selected asset
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

        // ============【新增：弹出窗口输入挂点名称】============
        AttachBoneInputWindow window = EditorWindow.GetWindow<AttachBoneInputWindow>("烘焙挂点设置");
        window.Init((attachBoneNames) =>
        {
            DoBakeLogic(sourceObject, assetPath, attachBoneNames);
        });
        window.ShowModalUtility();
    }

    /// <summary>
    /// 原BakeGPUAnimation全部业务逻辑抽到此，不修改任何原有逻辑，接收弹窗传入的挂点名称列表
    /// </summary>
    private static void DoBakeLogic(GameObject sourceObject, string assetPath, List<string> attachBoneInputNames)
    {
        string name = Path.GetFileNameWithoutExtension(assetPath);
        string animFolder = "Assets/Resources/anima/" + name;

        if (!AssetDatabase.IsValidFolder(animFolder))
        {
            Debug.LogWarning("The selected asset does not contain any AnimationClips.");
            return;
        }
        string[] animationClipFbx = Directory.GetFiles(animFolder, "*.fbx", SearchOption.AllDirectories);
        AnimManager animManager = new AnimManager();
        animManager.clipInfos = new List<ClipInfo>();
        animManager.totalFrameCount = 0;
        foreach (string clipPath in animationClipFbx)
        {
            LoadClipInfos(clipPath, ref animManager);
        }
        if (animManager.totalFrameCount <= 0)
        {
            Debug.LogWarning("No valid AnimationClips were found in the selected asset.");
            return;
        }
        GameObject instance = null;
        Texture2D gpuTexX = null;
        Texture2D gpuTexY = null;
        Texture2D gpuTexZ = null;
        BakedClipsAsset bakedClipsAsset = null;
        try
        {
            SkinnedMeshRenderer[] sourceRenderers = sourceObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer sourceRenderer in sourceRenderers)
            {
                if (sourceRenderer.sharedMesh == null)
                    throw new InvalidOperationException($"源模型包含空网格：{sourceRenderer.name}");
            }
            if (Shader.Find(ShaderName) == null)
                throw new InvalidOperationException($"Shader was not found: {ShaderName}");

            string assetName = sourceObject.name + "_BakeInstance";
            GPUBoneBakeAssetStore assetStore = new GPUBoneBakeAssetStore(assetPath, name, assetName,
                PrefabFolder, MaterialFolder, MeshFolder, TextureFolder, sourceRenderers.Length);
            instance = UnityEngine.Object.Instantiate(sourceObject, Vector3.zero, Quaternion.identity);
            instance.name = assetName;
            instance.transform.localScale = Vector3.one;

            BonesManager bonesManager = new BonesManager();
            bonesManager.boneInfos = new List<ObjInfo>();
            bonesManager.attachBones = attachBoneInputNames ?? new List<string>();
            bonesManager.attachBoneTRS = new List<BoneTRS>();
            bonesManager.totalBoneCount = 0;
            CollectBonesAndInfos(instance, ref bonesManager);
            CollectAttachBones(instance.transform, ref bonesManager);
            bool attachBoneDataValid = LogAttachBoneCollection(instance.transform, bonesManager);
            if (bonesManager.totalBoneCount <= 0)
            {
                Debug.LogWarning("No animated bone objects were found in the selected asset.");
                return;
            }

            attachBoneDataValid &= BakeClipMatrices(instance, animManager, ref bonesManager, out gpuTexX, out gpuTexY, out gpuTexZ);
            assetStore.Prepare();
            gpuTexX = assetStore.Save(gpuTexX, assetStore.TexturePath("X"));
            gpuTexY = assetStore.Save(gpuTexY, assetStore.TexturePath("Y"));
            gpuTexZ = assetStore.Save(gpuTexZ, assetStore.TexturePath("Z"));

            bakedClipsAsset = CreateBakedClipsAsset(animManager, bonesManager, gpuTexX, gpuTexY, gpuTexZ);
            bakedClipsAsset = assetStore.Save(bakedClipsAsset, assetStore.ClipsPath);
            GenerateGPUAnimationPrefab(instance, bakedClipsAsset, bonesManager, assetStore);
            AssetDatabase.SaveAssets();
            assetStore.DeleteUnusedOutputs();
            AssetDatabase.Refresh();
            LogSavedAttachBoneData(bakedClipsAsset, assetStore.ClipsPath, bonesManager, animManager.totalFrameCount, attachBoneDataValid);
            Debug.Log("GPU animation bake finished: " + assetName);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[骨骼烘焙资产] 烘焙未完成：{exception.Message}。请检查冲突或写入错误后重试。");
        }
        finally
        {
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
            DestroyTemporaryAsset(gpuTexX);
            DestroyTemporaryAsset(gpuTexY);
            DestroyTemporaryAsset(gpuTexZ);
            DestroyTemporaryAsset(bakedClipsAsset);
        }
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
    private static void ProcessMesh(GameObject instance, BonesManager boneManager, GameObject GameRoot, GPUBoneBakeAssetStore assetStore)
    {
        if (boneManager.totalBoneCount == 0)
        {
            return;
        }
        SkinnedMeshRenderer[] sourceRenderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int meshIndex = 0; meshIndex < sourceRenderers.Length; meshIndex++)
        {
            SkinnedMeshRenderer SKedMR = sourceRenderers[meshIndex];
            Mesh mesh = UnityEngine.Object.Instantiate(SKedMR.sharedMesh);
            mesh.name = assetStore.MeshName(meshIndex);
            TranlateMeshSpace(SKedMR.transform.localToWorldMatrix, mesh);

            SaveBoneWeights(mesh, boneManager.totalBoneCount);

            mesh = assetStore.Save(mesh, assetStore.MeshPath(meshIndex));
            GameObject meshObject = GameRoot;
            if (sourceRenderers.Length > 1)
            {
                meshObject = new GameObject(SKedMR.name + "_" + meshIndex);
                meshObject.transform.SetParent(GameRoot.transform, false);
            }
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var targetRenderer = meshObject.AddComponent<MeshRenderer>();
            AssignGPUAnimationMaterial(targetRenderer, assetStore);
        }
    }
    private static void SaveBoneWeights(Mesh mesh, int BonesCount)
    {
        int vertexCount = mesh.vertexCount;
        List<Vector4> boneIndices = new List<Vector4>(vertexCount);
        List<Vector4> boneWeight = new List<Vector4>(vertexCount);
        for (int j = 0; j < vertexCount; j++)
        {
            var boneWeights = mesh.boneWeights[j];
            Vector4 Indices = new Vector4(boneWeights.boneIndex0, boneWeights.boneIndex1, boneWeights.boneIndex2, boneWeights.boneIndex3);
            Indices += new Vector4(0.5f, 0.5f, 0.5f, 0.5f);
            Indices /= BonesCount; // Normalize to [0, 1] range
            boneIndices.Add(Indices);
            boneWeight.Add(new Vector4(boneWeights.weight0, boneWeights.weight1, boneWeights.weight2, boneWeights.weight3));
        }
        mesh.SetUVs(1, boneWeight);
        mesh.SetUVs(2, boneIndices);
    }
    private static void TranlateMeshSpace(in Matrix4x4 localToWorldMatrix, Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = localToWorldMatrix.MultiplyPoint3x4(vertices[i]);
            vertices[i] = worldPos;
        }
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
    }
    private static int CountBones(GameObject sourceObject)
    {
        var meshRenderer = sourceObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (meshRenderer.Length <= 0)
        {
            return 0;
        }
        int i = 0;
        foreach (var renderer in meshRenderer)
        {
            if (renderer.bones != null)
            {
                i += renderer.bones.Length;
            }
        }
        return i;
    }
    private static void CollectBonesAndInfos(GameObject sourceObject, ref BonesManager bonesManager)
    {
        var meshRenderer = sourceObject.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (meshRenderer.Length <= 0)
        {
            return;
        }
        if (bonesManager.boneInfos == null)
        {
            bonesManager.boneInfos = new List<ObjInfo>();
        }
        int i = 0;
        foreach (var renderer in meshRenderer)
        {
            if (renderer.bones != null)
            {
                foreach (var bone in renderer.bones)
                {
                    if (bone != null)
                    {
                        // bones.Add(bone);
                        bonesManager.boneInfos.Add(new ObjInfo
                        {
                            BoneName = bone.name,
                            ObjTrans = bone,
                            BindPose = bone.worldToLocalMatrix,//绑定姿态下的逆骨骼世界矩阵
                            AttachBoneIndex = -1
                            // isAttachBone = bonesManager.attachBones != null && bonesManager.attachBones.Contains(bone.name),
                            //挂点物体时受骨骼影响的物体而不属于骨骼
                            // BindPose = renderer.sharedMesh.bindposes[Array.IndexOf(renderer.bones, bone)]
                        });
                        // Debug.Log($"Collected bone: {bone.name}");
                        i++;
                    }
                }
            }
        }
        bonesManager.totalBoneCount = i;
        Debug.Log("Total bones collected: " + bonesManager.totalBoneCount);
    }

    private static void CollectAttachBones(Transform Object, ref BonesManager bonesManager)
    {
        if (Object == null)
        {
            return;
        }
        for(int i = 0; i< Object.childCount; i++)
        {
            Transform child = Object.GetChild(i);
            CollectAttachBones(child, ref bonesManager);
            
            if (bonesManager.attachBones.Contains(child.name))
            {
                bonesManager.boneInfos.Add(new ObjInfo
                {
                    BoneName = child.name,
                    ObjTrans = child,
                    BindPose = child.worldToLocalMatrix,
                    AttachBoneIndex = bonesManager.totalBoneCount
                });
                // Debug.Log($"Collected attach bone: {child.name}");
                bonesManager.totalBoneCount++;
            }
            
        }
    }
    private static bool LogAttachBoneCollection(Transform root, BonesManager bonesManager)
    {
        if (bonesManager.attachBones.Count == 0)
        {
            Debug.Log($"{AttachBoneLogPrefix} 未指定挂点，本次不保存挂点动画数据。");
            return true;
        }

        Debug.Log($"{AttachBoneLogPrefix} 请求 {bonesManager.attachBones.Count} 个挂点：{string.Join(", ", bonesManager.attachBones)}。数据按实际采集顺序、动画片段、帧排列；每条 {AttachBoneTRSStride} 字节（position.xyz + rotation.xyzw + scale.x）。");
        bool valid = true;
        List<string> collectedNames = new List<string>();
        foreach (ObjInfo boneInfo in bonesManager.boneInfos)
        {
            if (boneInfo.AttachBoneIndex < 0)
            {
                continue;
            }

            string hierarchyPath = AnimationUtility.CalculateTransformPath(boneInfo.ObjTrans, root);
            Debug.Log($"{AttachBoneLogPrefix} 匹配挂点：{boneInfo.BoneName}，路径：{hierarchyPath}，数据块索引：{collectedNames.Count}，骨骼索引：{boneInfo.AttachBoneIndex}。");
            collectedNames.Add(boneInfo.BoneName);
        }

        HashSet<string> requestedNames = new HashSet<string>();
        foreach (string boneName in bonesManager.attachBones)
        {
            if (!requestedNames.Add(boneName))
            {
                Debug.LogWarning($"{AttachBoneLogPrefix} 重复输入挂点名：{boneName}，名称列表可能与数据块数量不一致。");
                valid = false;
                continue;
            }

            int matchCount = collectedNames.FindAll(collectedName => collectedName == boneName).Count;
            if (matchCount != 1)
            {
                Debug.LogWarning($"{AttachBoneLogPrefix} 挂点 {boneName} 匹配到 {matchCount} 个对象，预期 1 个。请检查名称、大小写或层级中的同名对象；当前采集不包含模型根节点。");
                valid = false;
            }
        }

        bool orderMatches = collectedNames.Count == bonesManager.attachBones.Count;
        for (int boneIndex = 0; orderMatches && boneIndex < collectedNames.Count; boneIndex++)
        {
            orderMatches = collectedNames[boneIndex] == bonesManager.attachBones[boneIndex];
        }
        if (!orderMatches)
        {
            Debug.LogWarning($"{AttachBoneLogPrefix} 实际数据顺序 [{string.Join(", ", collectedNames)}] 与保存的 AttachBones 列表 [{string.Join(", ", bonesManager.attachBones)}] 不一致，按名称列表索引读取可能错位。");
            valid = false;
        }
        return valid;
    }

    private static void LogSavedAttachBoneData(BakedClipsAsset bakedClipsAsset, string path, BonesManager bonesManager, int totalFrames, bool dataValid)
    {
        BakedClipsAsset savedAsset = AssetDatabase.LoadAssetAtPath<BakedClipsAsset>(path);
        if (savedAsset == null || savedAsset != bakedClipsAsset || !File.Exists(path))
        {
            Debug.LogError($"{AttachBoneLogPrefix} 保存校验失败：无法在目标路径确认烘焙资源。路径：{path}");
            return;
        }

        long expectedRecords = (long)bonesManager.attachBones.Count * totalFrames;
        long expectedBytes = expectedRecords * AttachBoneTRSStride;
        int actualRecords = bonesManager.attachBoneTRS.Count;
        int actualBytes = savedAsset.AttachBoneTRSData == null ? 0 : savedAsset.AttachBoneTRSData.Length;
        if (actualRecords != expectedRecords || actualBytes != expectedBytes)
        {
            Debug.LogError($"{AttachBoneLogPrefix} 数据数量校验失败：TRS 实际/预期 = {actualRecords}/{expectedRecords}，字节实际/预期 = {actualBytes}/{expectedBytes}。路径：{path}", savedAsset);
            dataValid = false;
        }

        string summary = $"挂点数：{bonesManager.attachBones.Count}，每挂点预期帧数：{totalFrames}，TRS 条数：{actualRecords}，数据大小：{actualBytes} 字节，路径：{path}";
        if (dataValid)
        {
            Debug.Log($"{AttachBoneLogPrefix} 资源已保存，挂点映射、采样数量和字节数校验通过。{summary}", savedAsset);
        }
        else
        {
            Debug.LogWarning($"{AttachBoneLogPrefix} 资源已保存，但挂点数据存在异常，请检查前面的日志。{summary}", savedAsset);
        }
    }

    private static void LoadClipInfos(string assetPath, ref AnimManager animManager)
    {
        // List<ClipInfo> clipInfos = new List<ClipInfo>();
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        int totalFrameCount = 0;
        foreach (UnityEngine.Object asset in assets)
        {
            AnimationClip clip = asset as AnimationClip;
            if (clip == null || IsPreviewClip(clip))
            {
                continue;
            }
            float frameRate = clip.frameRate;
            int frameCount = Mathf.RoundToInt(clip.length * frameRate);
            animManager.totalFrameCount += frameCount;
            animManager.clipInfos.Add(new ClipInfo
            {
                clipName = clip.name,
                clipLength = clip.length,
                frameCount = frameCount,
                frameRate = frameRate,
                clip = clip
            });
        }
    }
    private static bool BakeClipMatrices(
        GameObject instance,
        AnimManager animManager,
        ref BonesManager bonesManager,
        out Texture2D gpuTexX,
        out Texture2D gpuTexY,
        out Texture2D gpuTexZ)
    {
        gpuTexX = CreateDataTexture(animManager.totalFrameCount, bonesManager.totalBoneCount);
        gpuTexY = CreateDataTexture(animManager.totalFrameCount, bonesManager.totalBoneCount);
        gpuTexZ = CreateDataTexture(animManager.totalFrameCount, bonesManager.totalBoneCount);
        Color[] texX = new Color[bonesManager.totalBoneCount * animManager.totalFrameCount];
        Color[] texY = new Color[texX.Length];
        Color[] texZ = new Color[texX.Length];

        // List<Transform> ObjTrans = new List<Transform>();

        bool attachBoneDataValid = true;
        var boneInfos = bonesManager.boneInfos;
        int c = 0;
        for (int i = 0; i < bonesManager.totalBoneCount; i++)
        {
            int frameIndex = 0;
            int firstRecordIndex = bonesManager.attachBoneTRS.Count;
            int nonUniformScaleFrames = 0;
            foreach (ClipInfo clipInfo in animManager.clipInfos)
            {
                if (clipInfo.clip == null || clipInfo.frameRate <= 0f)
                {
                    if (boneInfos[i].AttachBoneIndex >= 0)
                    {
                        Debug.LogWarning($"{AttachBoneLogPrefix} 挂点 {boneInfos[i].BoneName} 跳过无效片段 {clipInfo.clipName}：动画为空或帧率无效。");
                        attachBoneDataValid = false;
                    }
                    continue;
                }
                float frameDeltaTime = 1f / clipInfo.frameRate;
                int writtenFrameCount = 0;
                for (float time = 0f; time < clipInfo.clip.length && writtenFrameCount < clipInfo.frameCount; time += frameDeltaTime)
                {
                    if (frameIndex >= animManager.totalFrameCount)
                    {
                        break;
                    }
                    clipInfo.clip.SampleAnimation(instance, time);
                    Matrix4x4 matrix = boneInfos[i].ObjTrans.localToWorldMatrix * boneInfos[i].BindPose;
                    var index = i * animManager.totalFrameCount + frameIndex;
                    texX[index] = matrix.GetRow(0);
                    texY[index] = matrix.GetRow(1);
                    texZ[index] = matrix.GetRow(2);

                    if(boneInfos[i].AttachBoneIndex >= 0)
                    {
                        // Store the TRS of the attach bone at this frame
                        BoneTRS trs;
                        trs.position = boneInfos[i].ObjTrans.position;
                        trs.rotation = boneInfos[i].ObjTrans.rotation;
                        trs.scale = boneInfos[i].ObjTrans.localScale;
                        bonesManager.attachBoneTRS.Add(trs);
                        if (!Mathf.Approximately(trs.scale.x, trs.scale.y) || !Mathf.Approximately(trs.scale.x, trs.scale.z))
                        {
                            nonUniformScaleFrames++;
                        }
                    }

                    frameIndex++;
                    writtenFrameCount++;
                }
                if (boneInfos[i].AttachBoneIndex >= 0)
                {
                    string sampleSummary = $"{AttachBoneLogPrefix} 挂点 {boneInfos[i].BoneName}，片段 {clipInfo.clipName}，采样帧数实际/预期：{writtenFrameCount}/{clipInfo.frameCount}。";
                    if (writtenFrameCount == clipInfo.frameCount)
                    {
                        Debug.Log(sampleSummary);
                    }
                    else
                    {
                        Debug.LogError(sampleSummary);
                        attachBoneDataValid = false;
                    }
                }
            }
            int recordCount = bonesManager.attachBoneTRS.Count - firstRecordIndex;
            if (recordCount > 0)
            {
                BoneTRS firstTRS = bonesManager.attachBoneTRS[firstRecordIndex];
                BoneTRS lastTRS = bonesManager.attachBoneTRS[bonesManager.attachBoneTRS.Count - 1];
                Debug.Log($"{AttachBoneLogPrefix} 挂点 {boneInfos[i].BoneName} 共 {recordCount} 条 TRS，起始字节偏移：{(long)firstRecordIndex * AttachBoneTRSStride}。首帧 T={firstTRS.position.ToString("F4")} R={firstTRS.rotation.ToString("F4")} S={firstTRS.scale.ToString("F4")}；末帧 T={lastTRS.position.ToString("F4")} R={lastTRS.rotation.ToString("F4")} S={lastTRS.scale.ToString("F4")}。T/R 为世界空间，S 为局部空间，实际仅保存 S.x。");
            }
            if (nonUniformScaleFrames > 0)
            {
                Debug.LogWarning($"{AttachBoneLogPrefix} 挂点 {boneInfos[i].BoneName} 有 {nonUniformScaleFrames} 帧非均匀缩放，当前仅保存 scale.x，无法完整还原 scale.y/z。");
                attachBoneDataValid = false;
            }
        }
        gpuTexX.SetPixels(texX);
        gpuTexY.SetPixels(texY);
        gpuTexZ.SetPixels(texZ);
        gpuTexX.Apply();
        gpuTexY.Apply();
        gpuTexZ.Apply();
        return attachBoneDataValid;
    }
    private static BakedClipsAsset CreateBakedClipsAsset(
        AnimManager animManager,
        BonesManager bonesManager,
        Texture2D gpuTexX,
        Texture2D gpuTexY,
        Texture2D gpuTexZ)
    {
        BakedClipsAsset bakedClipsAsset = ScriptableObject.CreateInstance<BakedClipsAsset>();
        bakedClipsAsset.AnimationsTexX = gpuTexX;
        bakedClipsAsset.AnimationsTexY = gpuTexY;
        bakedClipsAsset.AnimationsTexZ = gpuTexZ;
        bakedClipsAsset.totalFrames = animManager.totalFrameCount;
        bakedClipsAsset.AmountOfObjects = bonesManager.totalBoneCount;
        
        bakedClipsAsset.AttachBones = bonesManager.attachBones;

        // List<uint> attachBoneIndices = new List<uint>();
        // foreach (var boneInfo in bonesManager.boneInfos)
        // {
        //   if(boneInfo.AttachBoneIndex >= 0)
        //   {
        //     attachBoneIndices.Add((uint)boneInfo.AttachBoneIndex);
        //   }
        // }
        // bakedClipsAsset.AttachBoneIndices = attachBoneIndices;

        var streamHelper = new StreamHelper();
        for(int i= 0;i< bonesManager.attachBoneTRS.Count; i++)
        {
            streamHelper.writeFloat3(bonesManager.attachBoneTRS[i].position);
            streamHelper.writeWriteQuaternion(bonesManager.attachBoneTRS[i].rotation);
            streamHelper.writeFloat(bonesManager.attachBoneTRS[i].scale.x);
        }
        streamHelper.Capacity = (int)streamHelper.Length;
        bakedClipsAsset.AttachBoneTRSData = streamHelper.GetBuffer();

        foreach (ClipInfo clipInfo in animManager.clipInfos)
        {
            bakedClipsAsset.clips.Add(new BakedClipsAsset.clipInfo
            {
                clipName = clipInfo.clipName,
                startFrame = GetAnimationFrameOffset(animManager.clipInfos, clipInfo.clip),
                frameCount = clipInfo.frameCount,
                frameRate = clipInfo.frameRate
            });
        }
        return bakedClipsAsset;
    }
    private static void GenerateGPUAnimationPrefab(GameObject instance, BakedClipsAsset bakedClipsAsset, BonesManager bonesManager, GPUBoneBakeAssetStore assetStore)
    {
        GameObject GameRoot = new GameObject(instance.name);
        GameRoot.SetActive(false);
        try
        {
            foreach (var bonesName in bonesManager.attachBones)
            {
                GameObject attachBone = new GameObject(bonesName);
                attachBone.transform.SetParent(GameRoot.transform, false);
            }
            ProcessMesh(instance, bonesManager, GameRoot, assetStore);
            GPUBoneAnimationController controller = GameRoot.AddComponent<GPUBoneAnimationController>();
            controller.bakedClipsAsset = bakedClipsAsset;
            GameRoot.SetActive(true);
            assetStore.SavePrefab(GameRoot);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(GameRoot);
        }
    }
    private static void AssignGPUAnimationMaterial(Renderer targetRenderer, GPUBoneBakeAssetStore assetStore)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning("Shader was not found: " + ShaderName);
            return;
        }
        targetRenderer.sharedMaterial = assetStore.GetOrCreateMaterial(shader);
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
    private static Texture2D CreateDataTexture(int totalFrameCount, int BonesCount)
    {
        Texture2D texture = new Texture2D(totalFrameCount, BonesCount, TextureFormat.RGBAHalf, false, true);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }
    private static void DestroyTemporaryAsset(UnityEngine.Object asset)
    {
        if (asset != null && !AssetDatabase.Contains(asset))
            UnityEngine.Object.DestroyImmediate(asset);
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

    #region 【新增Editor弹窗窗口】
    /// <summary>
    /// 模态弹窗，输入挂点骨骼名称，多个名称用英文逗号隔开
    /// </summary>
    public class AttachBoneInputWindow : EditorWindow
    {
        private System.Action<List<string>> _onConfirmCallback;
        private string _inputText = "";

        public void Init(System.Action<List<string>> callback)
        {
            _onConfirmCallback = callback;
            _inputText = "";
            minSize = new Vector2(420, 140);
            maxSize = new Vector2(420, 140);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("填写挂点骨骼名称，多个名称使用【英文逗号】分隔", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            _inputText = EditorGUILayout.TextArea(_inputText, GUILayout.Height(60));
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("确认开始烘焙", GUILayout.Height(30)))
                {
                    List<string> resultList = new List<string>();
                    if (!string.IsNullOrWhiteSpace(_inputText))
                    {
                        string[] splitArr = _inputText.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var s in splitArr)
                        {
                            string trimName = s.Trim();
                            if (!string.IsNullOrEmpty(trimName))
                            {
                                resultList.Add(trimName);
                            }
                        }
                    }
                    _onConfirmCallback?.Invoke(resultList);
                    this.Close();
                }

                if (GUILayout.Button("取消", GUILayout.Height(30)))
                {
                    this.Close();
                }
            }
        }
    }
    #endregion
}
