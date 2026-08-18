using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GPUBoneAnimationController))]
public class GPUBoneAnimationControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GPUBoneAnimationController controller = (GPUBoneAnimationController)target;
        BakedClipsAsset bakedClipsAsset = controller.bakedClipsAsset;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("GPU Bone Animation Controls", EditorStyles.boldLabel);

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
        else if (controller.playByHand)
        {
            EditorGUILayout.HelpBox(
                "Manual mode is active. Drag Frameplay to select a frame from the baked animation.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Automatic mode is active. Use a clip button to start playback.",
                MessageType.Info);
        }

        EditorGUI.BeginDisabledGroup(!Application.isPlaying);

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

        EditorGUI.EndDisabledGroup();
    }
}
