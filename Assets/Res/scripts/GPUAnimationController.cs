using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GPUAnimationController : MonoBehaviour
{
    public BakedClipsAsset bakedClipsAsset;
    private double playAStartTime;
    private int currentClipStartFrame;
    private Renderer renderer;
    public bool isplaying = false;
    private BakedClipsAsset.clipInfo currentClipInfo;
    private MaterialPropertyBlock propertyBlock;

    // Start is called before the first frame update
    void OnEnable()
    {
        renderer = GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError("Renderer component not found on the GameObject.");
            return;
        }
        if (bakedClipsAsset == null)
        {
            Debug.LogError("BakedClipsAsset is not assigned.");
            return;
        }
        propertyBlock = new MaterialPropertyBlock();
        CheckPlayable();
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
            || bakedClipsAsset.clips.Count == 0)
            {
                Debug.LogError("BakedClipsAsset is not properly configured.");
                return false;
            }
            int count = 0;
            foreach (var clip in bakedClipsAsset.clips)
            {
                count+= clip.frameCount;
            }
            if(count != bakedClipsAsset.totalFrames)
            {
                Debug.LogError("Total frames in clips do not match the totalFrames in BakedClipsAsset.");
                return false;
            }
        }
        if (renderer == null)
        {
            Debug.LogError("Renderer component not found on the GameObject.");
            return false;
        }
        
        var meshFilter = this.GetComponent<MeshFilter>();
        if(meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("MeshFilter component not found on the GameObject.");
            return false;
        }
        if (renderer.sharedMaterial == null)
        {
            Debug.LogError("Material is not assigned to the Renderer.");
            return false;
        }
        return true;
    }

    public void RenderGPUAnimation(int clipIndex)
    {
        if (bakedClipsAsset == null)
            return;

        // Set the textures to the material
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetTexture("_MainTex1", bakedClipsAsset.AnimationsTexX);
        propertyBlock.SetTexture("_MainTex2", bakedClipsAsset.AnimationsTexY);
        propertyBlock.SetTexture("_MainTex3", bakedClipsAsset.AnimationsTexZ);

        // Apply the property block to the renderer
        if (renderer != null)
        {
            renderer.SetPropertyBlock(propertyBlock);
        }
        currentClipInfo = bakedClipsAsset.clips[clipIndex];
        playAStartTime = Time.time;
        isplaying = true;
    }

    public void StopAnimation()
    {
        isplaying = false;
    }

    void Update()
    {
        if(!isplaying)
            return;

        double elapsedTime = Time.time - playAStartTime;
        int currentFrame = (int)(elapsedTime * currentClipInfo.frameRate)+currentClipInfo.startFrame;
        currentFrame %= bakedClipsAsset.totalFrames;
        float frameIndex = ((float)currentFrame+0.5f) / bakedClipsAsset.totalFrames;
        Debug.Log($"Current Frame: {currentFrame}, Frame Index: {frameIndex}");
        UpdateShaderFrame(frameIndex);
        if(currentFrame+1 >= bakedClipsAsset.totalFrames || currentFrame >= currentClipInfo.startFrame + currentClipInfo.frameCount)
        {
            isplaying = false;
        }
    }

    void UpdateShaderFrame(float frameIndex)
    {
        if(renderer == null)
            return;

        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat("_frameState", frameIndex);
        renderer.SetPropertyBlock(propertyBlock);
    }
}
