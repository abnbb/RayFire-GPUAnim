using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FBXWithBoneClipInfo
{
    // private int BonesCount = 0;
    private const string MenuPath = "Assets/烘焙骨骼动画";
    private const string TextureFolder = "Assets/Resources/Tex";
    private const string PrefabFolder = "Assets/Res/prefab";
    private const string MaterialFolder = "Assets/Res/materials";
    private const string MeshFolder = "Assets/Resources/meshes";
    private const string ShaderName = "Custom/GPUBoneAnim";
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
        public List<float> attachBoneIndices;
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
        EnsureProjectFolders();
        //Root.WorlToLocalMatrix == I
        GameObject instance = UnityEngine.Object.Instantiate(sourceObject, Vector3.zero, Quaternion.identity);
        instance.name = sourceObject.name + "_BakeInstance";
        instance.transform.localScale = Vector3.one;


        BonesManager bonesManager = new BonesManager();
        bonesManager.boneInfos = new List<ObjInfo>();
        // ===== 赋值弹窗输入的挂点名字，原有逻辑完全不变 =====
        bonesManager.attachBones = attachBoneInputNames ?? new List<string>();
        bonesManager.attachBoneIndices = new List<float>();
        bonesManager.totalBoneCount = 0;
        CollectBonesAndInfos(instance, ref bonesManager);
        CollectAttachBones(instance.transform, ref bonesManager);
        if (bonesManager.totalBoneCount <= 0)
        {
            Debug.LogWarning("No animated bone objects were found in the selected asset.");
            UnityEngine.Object.DestroyImmediate(instance);
            return;
        }
        Texture2D gpuTexX;
        Texture2D gpuTexY;
        Texture2D gpuTexZ;
        List<float> attachBoneIndices = new List<float>();
        BakeClipMatrices(instance, animManager, ref bonesManager, out gpuTexX, out gpuTexY, out gpuTexZ);
        string assetName = instance.name;
        SaveTextureAsset(gpuTexX, TextureFolder + "/" + assetName + "_GPUTexX.asset");
        SaveTextureAsset(gpuTexY, TextureFolder + "/" + assetName + "_GPUTexY.asset");
        SaveTextureAsset(gpuTexZ, TextureFolder + "/" + assetName + "_GPUTexZ.asset");

        BakedClipsAsset bakedClipsAsset = CreateBakedClipsAsset(animManager, bonesManager, gpuTexX, gpuTexY, gpuTexZ);
        SaveBakedClipsAsset(bakedClipsAsset, TextureFolder + "/" + assetName + "_BakedClipsAsset.asset");

        GenerateGPUAnimationPrefab(instance, bakedClipsAsset, bonesManager, PrefabFolder + "/" + assetName + ".prefab");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("GPU animation bake finished: " + assetName);

        UnityEngine.Object.DestroyImmediate(instance);

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
    private static void ProcessMesh(GameObject instance, BonesManager boneManager, GameObject GameRoot)
    {
        if (boneManager.totalBoneCount == 0)
        {
            return;
        }
        string assetName = instance.name;

        int i = 0;
        foreach (var SKedMR in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            GameObject GameChild = new GameObject(assetName + "_" + i++);
            Mesh mesh = UnityEngine.Object.Instantiate(SKedMR.sharedMesh);
            mesh.name = assetName + "_GPUABonenimMesh_" + i;
            TranlateMeshSpace(SKedMR.transform.localToWorldMatrix, mesh);

            SaveBoneWeights(mesh, boneManager.totalBoneCount);

            SKedMR.sharedMesh = mesh;
            EnsureFolder(MeshFolder, assetName);
            SaveMeshAsset(mesh, MeshFolder + "/" + assetName + "/" + mesh.name + ".asset");
            GameChild.transform.SetParent(GameRoot.transform, false);
            GameChild.AddComponent<MeshFilter>().sharedMesh = mesh;
            var targetRenderer = GameChild.AddComponent<MeshRenderer>();
            AssignGPUAnimationMaterial(targetRenderer, instance.name);
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
    private static void BakeClipMatrices(
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

        var boneInfos = bonesManager.boneInfos;
        int c = 0;
        for (int i = 0; i < bonesManager.totalBoneCount; i++)
        {
            int frameIndex = 0;
            foreach (ClipInfo clipInfo in animManager.clipInfos)
            {
                if (clipInfo.clip == null || clipInfo.frameRate <= 0f)
                {
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
                    frameIndex++;
                    writtenFrameCount++;
                    
                }
            }
        }
        gpuTexX.SetPixels(texX);
        gpuTexY.SetPixels(texY);
        gpuTexZ.SetPixels(texZ);
        gpuTexX.Apply();
        gpuTexY.Apply();
        gpuTexZ.Apply();
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
        List<uint> attachBoneIndices = new List<uint>();
        foreach (var boneInfo in bonesManager.boneInfos)
        {
          if(boneInfo.AttachBoneIndex >= 0)
          {
            attachBoneIndices.Add((uint)boneInfo.AttachBoneIndex);
          }
        }
        bakedClipsAsset.AttachBoneIndices = attachBoneIndices;
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
    private static void GenerateGPUAnimationPrefab(GameObject instance, BakedClipsAsset bakedClipsAsset, BonesManager bonesManager, string prefabPath)
    {
        GameObject GameRoot = new GameObject(instance.name);
        foreach (var bonesName in bonesManager.attachBones)
        {
            GameObject attachBone = new GameObject(bonesName);
            attachBone.transform.SetParent(GameRoot.transform, false);
        }
        ProcessMesh(instance, bonesManager, GameRoot);
        GPUBoneAnimationController controller = GameRoot.GetComponent<GPUBoneAnimationController>();
        if (controller == null)
        {
            controller = GameRoot.AddComponent<GPUBoneAnimationController>();
        }
        controller.bakedClipsAsset = bakedClipsAsset;
        DeleteAssetIfExists(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(GameRoot, prefabPath);
        // UnityEngine.Object.DestroyImmediate(instance);
        UnityEngine.Object.DestroyImmediate(GameRoot);
    }
    private static void AssignGPUAnimationMaterial(Renderer targetRenderer, string assetName)
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning("Shader was not found: " + ShaderName);
            return;
        }
        string materialName = assetName + "_GPUBoneAnim";
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
    private static Texture2D CreateDataTexture(int totalFrameCount, int BonesCount)
    {
        Texture2D texture = new Texture2D(totalFrameCount, BonesCount, TextureFormat.RGBAHalf, false, true);
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