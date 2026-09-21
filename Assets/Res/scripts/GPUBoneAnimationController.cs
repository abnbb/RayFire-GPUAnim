using System.Collections;
using System.Collections.Generic;
// using System.ComponentModel.DataAnnotations;
using UnityEngine;

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

    public void RenderGPUAnimation(int clipIndex)
    {
        if (!Application.IsPlaying(gameObject))
            return;

        if (!CheckPlayable())
            return;

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
            }

            if (animationTexturesAssigned)
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
        UpdateAttachObject();
    }

    void UpdateAttachObject()
    {
        //Debug.Log($"worldTransM\n{this.transform.localToWorldMatrix}");
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
