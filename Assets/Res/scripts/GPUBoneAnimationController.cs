using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

[ExecuteAlways]
public class GPUBoneAnimationController : MonoBehaviour
{
    public BakedClipsAsset bakedClipsAsset;
    private double playAStartTime;
    private int currentClipStartFrame;
    private Renderer[] renderers;
    public bool isplaying = false;
    public bool playByHand = false;
    [Range(0,1) ]public float frameplay = 0;
    private BakedClipsAsset.clipInfo currentClipInfo;
    private MaterialPropertyBlock propertyBlock;
    private bool animationTexturesAssigned;

    [NonSerialized] public NativeArray<float4x2> attchObjAnims_b;
    [NonSerialized] public Dictionary<string, NativeArray<float4x2>> attchObjAnims_d;
    private bool loggedAttachReadState;
    private GCHandle attachDataHandle;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private AtomicSafetyHandle attachDataSafetyHandle;
#endif

    // Start is called before the first frame update
    void OnEnable()
    {
        animationTexturesAssigned = false;
        renderers = GetComponentsInChildren<Renderer>(true);
        propertyBlock = new MaterialPropertyBlock();

        if (!Application.IsPlaying(gameObject) && !playByHand)
            return;

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogError("Renderer component not found on the GameObject.");
            return;
        }
        if (bakedClipsAsset == null)
        {
            Debug.LogError("BakedClipsAsset is not assigned.");
            return;
        }

        if (CheckPlayable())
        {
            animationTexturesAssigned = ApplyAnimationTextures();
        }
    }

    void OnDisable()
    {
        isplaying = false;
        ReleaseAttachObjAnimData();
    }

    public bool CheckPlayable()
    {
        if (bakedClipsAsset == null)
        {
            Debug.LogError("BakedClipsAsset is not assigned.");
            return false;
        }
        else
        {
            if(bakedClipsAsset.totalFrames <= 0
            || bakedClipsAsset.AnimationsTexX == null || bakedClipsAsset.AnimationsTexY == null || bakedClipsAsset.AnimationsTexZ == null
            || bakedClipsAsset.clips == null || bakedClipsAsset.clips.Count == 0)
            {
                Debug.LogError("BakedClipsAsset is not properly configured.");
                return false;
            }
            long count = 0;
            foreach (var clip in bakedClipsAsset.clips)
            {
                if (!IsValidClip(clip))
                {
                    Debug.LogError("BakedClipsAsset contains an invalid clip range.");
                    return false;
                }

                count+= clip.frameCount;
            }
            if(count != bakedClipsAsset.totalFrames)
            {
                Debug.LogError("Total frames in clips do not match the totalFrames in BakedClipsAsset.");
                return false;
            }
        }

        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogError("Renderer component not found on the GameObject.");
            return false;
        }
        
        var MeshRenderer = this.GetComponentsInChildren<MeshRenderer>();
        if(MeshRenderer == null || MeshRenderer.Length == 0)
        {
            Debug.LogError("MeshRenderer component not found on the GameObject.");
            return false;
        }
        // if (renderer.sharedMaterial == null)
        // {
        //     Debug.LogError("Material is not assigned to the Renderer.");
        //     return false;
        // }
        return true;
    }

    bool ensureAttachObjects()
    {
        for(int i = 0; i < bakedClipsAsset.AttachBones.Count; i++)
        {
            string boneName = bakedClipsAsset.AttachBones[i];
            Transform boneTransform = this.transform.Find(boneName);
            if (boneTransform == null)
            {
                Debug.LogError($"Attach point '{boneName}' not found in the hierarchy.");
                return false;
            }
        }
        return true;
    }

    public void RenderGPUAnimation(int clipIndex)
    {
        if (!Application.IsPlaying(gameObject))
            return;

        if (!CheckPlayable())
            return;
        GetAttachObjAnimData();
        
        if (clipIndex < 0 || clipIndex >= bakedClipsAsset.clips.Count)
        {
            Debug.LogWarning($"Animation clip index is out of range: {clipIndex}.", this);
            return;
        }

        animationTexturesAssigned = ApplyAnimationTextures();
        if (!animationTexturesAssigned)
            return;

        currentClipInfo = bakedClipsAsset.clips[clipIndex];
        playAStartTime = Time.time;
        playByHand = false;
        isplaying = true;
    }

    public void StopAnimation()
    {
        isplaying = false;
    }

    void Update()
    {
        if (playByHand)
        {
            if (!HasAnimationTextures() || bakedClipsAsset.totalFrames <= 0
                || float.IsNaN(frameplay) || float.IsInfinity(frameplay)
                || frameplay < 0f || frameplay > 1f)
                return;

            if (!Application.IsPlaying(gameObject))
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                animationTexturesAssigned = false;
            }

            if (!animationTexturesAssigned)
            {
                animationTexturesAssigned = ApplyAnimationTextures();
            }else
            {
                int manualFrame = Mathf.RoundToInt(frameplay * (bakedClipsAsset.totalFrames - 1));
                UpdateShaderFrame(ToTextureFrameCoordinate(manualFrame));
            }

            return;
        }
        
        if (!Application.IsPlaying(gameObject) || !isplaying)
            return;

        if (!HasAnimationTextures() || !IsValidClip(currentClipInfo))
            return;
        

        double elapsedTime = Time.time - playAStartTime;
        double frameProgress = elapsedTime * currentClipInfo.frameRate;
        if (double.IsNaN(frameProgress) || double.IsInfinity(frameProgress)
            || frameProgress < 0d || frameProgress > int.MaxValue)
            return;

        int clipFrame = (int)(elapsedTime * currentClipInfo.frameRate);
        if (clipFrame >= currentClipInfo.frameCount)
        {
            clipFrame = currentClipInfo.frameCount - 1;
            isplaying = false;
        }

        int currentFrame = currentClipInfo.startFrame + clipFrame;
        float frameIndex = ToTextureFrameCoordinate(currentFrame);
        Debug.Log($"Current Frame: {currentFrame}, Frame Index: {frameIndex}");
        UpdateShaderFrame(frameIndex);
        UpdateAttachObject(currentFrame);
    }

    void UpdateShaderFrame(float frameIndex)
    {
        if (renderers == null || renderers.Length == 0
            || float.IsNaN(frameIndex) || float.IsInfinity(frameIndex)
            || frameIndex < 0f || frameIndex > 1f)
            return;

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_frameState", frameIndex);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    unsafe void GetAttachObjAnimData()
    {
        ReleaseAttachObjAnimData();
        var attachObjs = bakedClipsAsset.AttachBones;
        int framesCount = bakedClipsAsset.totalFrames;
        byte[] data = bakedClipsAsset.AttachBoneTRSData;
        int dataBytes = data?.Length ?? -1;
        long expectedBytes = (long)(attachObjs?.Count ?? 0) * framesCount * 32;
        Debug.Log($"[挂点诊断] 初始化：asset={bakedClipsAsset.name}，挂点=[{(attachObjs == null ? "<null>" : string.Join(", ", attachObjs))}]，帧数={framesCount}，数据字节数={dataBytes}，预期字节数={expectedBytes}。", this);
        loggedAttachReadState = false;
        if (attachObjs == null || framesCount <= 0 || data == null || dataBytes != expectedBytes)
            throw new InvalidOperationException($"[挂点诊断] 挂点数据无效：实际字节数={dataBytes}，预期字节数={expectedBytes}。");

        attchObjAnims_d = new Dictionary<string, NativeArray<float4x2>>();
        if (attachObjs.Count == 0)
            return;

        try
        {
            attachDataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            attachDataSafetyHandle = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(attachDataSafetyHandle, true);
#endif
            float4x2* ptr = (float4x2*)attachDataHandle.AddrOfPinnedObject().ToPointer();
            for (int i = 0; i < attachObjs.Count; i++)
            {
                var attachObjAnim = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float4x2>
                    (ptr + framesCount * i, framesCount, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref attachObjAnim, attachDataSafetyHandle);
#endif
                attchObjAnims_d.Add(attachObjs[i], attachObjAnim);
            }
        }
        catch
        {
            ReleaseAttachObjAnimData();
            throw;
        }
        Debug.Log($"[挂点诊断] 已创建 {attchObjAnims_d.Count} 个 NativeArray 视图。", this);
    }

    void ReleaseAttachObjAnimData()
    {
        attchObjAnims_d?.Clear();
        attchObjAnims_d = null;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (AtomicSafetyHandle.IsValidNonDefaultHandle(attachDataSafetyHandle))
        {
            AtomicSafetyHandle.Release(attachDataSafetyHandle);
            attachDataSafetyHandle = default;
        }
#endif
        if (attachDataHandle.IsAllocated)
            attachDataHandle.Free();
    }

    void UpdateAttachObject(int frameIndex)
    {
        if (!loggedAttachReadState)
        {
            loggedAttachReadState = true;
            Debug.Log($"[挂点诊断] 首次读取：frame={frameIndex}，总帧数={bakedClipsAsset?.totalFrames ?? -1}，子节点数={transform.childCount}，字典状态={(attchObjAnims_d == null ? "null" : attchObjAnims_d.Count.ToString())}，数据字节数={bakedClipsAsset?.AttachBoneTRSData?.Length ?? -1}。", this);
        }

        for(int i = 0; i< this.transform.childCount; i++)
        {
            Transform child = this.transform.GetChild(i);
            NativeArray<float4x2> anim = default;
            float4x2 animframe;
            try
            {
                anim = attchObjAnims_d[child.name];
                animframe = anim[frameIndex];
            }
            catch (Exception exception)
            {
                Debug.LogError($"[挂点诊断] 读取失败：child={child.name}，frame={frameIndex}，总帧数={bakedClipsAsset?.totalFrames ?? -1}，字典为空={attchObjAnims_d == null}，存在键={attchObjAnims_d?.ContainsKey(child.name) ?? false}，视图已创建={anim.IsCreated}，视图长度={anim.Length}，数据字节数={bakedClipsAsset?.AttachBoneTRSData?.Length ?? -1}，异常={exception.GetType().Name}: {exception.Message}", this);
                isplaying = false;
                throw;
            }

            float3 pos = animframe.c0.xyz;
            float scale = animframe.c0.w;
            float4 rota = animframe.c1;
            child.localPosition  = pos;
            child.localRotation = new Quaternion(rota.x, rota.y, rota.z, rota.w);
            child.localScale = new Vector3(scale, scale, scale);
            Debug.Log($"{child.name} 更新挂点 TRS：T={pos} R={rota} S={scale}，帧索引：{frameIndex}。");
        }
    }

    private bool ApplyAnimationTextures()
    {
        if (!HasAnimationTextures() || renderers == null || renderers.Length == 0)
            return false;

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        bool texturesApplied = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture("_MainTex1", bakedClipsAsset.AnimationsTexX);
            propertyBlock.SetTexture("_MainTex2", bakedClipsAsset.AnimationsTexY);
            propertyBlock.SetTexture("_MainTex3", bakedClipsAsset.AnimationsTexZ);
            renderer.SetPropertyBlock(propertyBlock);
            texturesApplied = true;
        }

        return texturesApplied;
    }

    private bool HasAnimationTextures()
    {
        return bakedClipsAsset != null
            && bakedClipsAsset.AnimationsTexX != null
            && bakedClipsAsset.AnimationsTexY != null
            && bakedClipsAsset.AnimationsTexZ != null;
    }

    private bool IsValidClip(BakedClipsAsset.clipInfo clip)
    {
        return bakedClipsAsset != null && bakedClipsAsset.totalFrames > 0
            && clip.frameCount > 0 && clip.frameRate > 0f
            && !float.IsNaN(clip.frameRate) && !float.IsInfinity(clip.frameRate)
            && clip.startFrame >= 0
            && (long)clip.startFrame + clip.frameCount <= bakedClipsAsset.totalFrames;
    }

    private float ToTextureFrameCoordinate(int frame)
    {
        return (frame + 0.5f) / bakedClipsAsset.totalFrames;
    }
}
