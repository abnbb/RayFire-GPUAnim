using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GPUAnimationController))]
public class GPUAnimationControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GPUAnimationController controller = (GPUAnimationController)target;
        BakedClipsAsset bakedClipsAsset = controller.bakedClipsAsset;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("GPU Animation Controls", EditorStyles.boldLabel);

        if (bakedClipsAsset == null)
        {
            EditorGUILayout.HelpBox("Assign a BakedClipsAsset before playing.", MessageType.Info);
            return;
        }

        if (bakedClipsAsset.clips == null || bakedClipsAsset.clips.Count == 0)
        {
            EditorGUILayout.HelpBox("The BakedClipsAsset has no baked clips.", MessageType.Info);
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use the playback buttons.", MessageType.Info);
        }

        // EditorGUI.BeginDisabledGroup(false);
        for (int i = 0; i < bakedClipsAsset.clips.Count; i++)
        {
            BakedClipsAsset.clipInfo clipInfo = bakedClipsAsset.clips[i];
            if (GUILayout.Button("Play " + clipInfo.clipName))
            {
                if (controller.CheckPlayable())
                {
                    controller.RenderGPUAnimation(i);
                }
            }
        }

        if (GUILayout.Button("Stop Animation"))
        {
            controller.StopAnimation();
        }
        // EditorGUI.EndDisabledGroup();
    }
}
