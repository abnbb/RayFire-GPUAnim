using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BakedClipsAsset : ScriptableObject
{
    [System.Serializable]
    public struct clipInfo
    {
        public string clipName;
        public int startFrame;
        public float frameRate;
        public int frameCount;
    }

   public Texture2D AnimationsTexX;
   public Texture2D AnimationsTexY;
   public Texture2D AnimationsTexZ;
   public int totalFrames;

   public List<clipInfo> clips = new List<clipInfo>();
    
}
