using System;
using UnityEngine;

[Serializable]
public class CameraCullingPlatformSettings
{
    [Tooltip("Culling distances for each of the 32 GameObject layers. 0 means the camera's FarClipPlane is used.")]
    public float[] layerCullDistances = new float[32];

    [Tooltip("Override near and far clip planes when enabled.")]
    public bool overrideClipPlanes;

    [Tooltip("Near clip plane value (clamped between 0.001 and 0.05).")]
    [Range(0.001f, 0.05f)]
    public float nearClip = 0.01f;

    [Tooltip("Far clip plane value (must be at least Near + 0.1).")]
    public float farClip = 1000f;

    public void EnsureValid()
    {
        if (layerCullDistances == null || layerCullDistances.Length != 32)
        {
            layerCullDistances = new float[32];
        }

        nearClip = Mathf.Clamp(nearClip, 0.001f, 0.05f);
        if (farClip < nearClip + 0.1f)
        {
            farClip = nearClip + 0.1f;
        }
    }
}
