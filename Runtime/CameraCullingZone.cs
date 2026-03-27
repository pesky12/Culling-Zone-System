using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[RequireComponent(typeof(Collider))]
public class CameraCullingZone : UdonSharpBehaviour
{
    private const float InsideTolerance = 0.0001f;

    [Header("Zone Setup")]
    [Tooltip("Optional explicit collider reference. Defaults to the collider on this GameObject.")]
    [SerializeField]
    private Collider zoneCollider;

    [Tooltip("Higher priority zones override lower priority zones when overlapping.")]
    [SerializeField]
    private int priority;

    [Tooltip("Optional debug-friendly name for logging.")]
    [SerializeField]
    private string label;

        [Header("Desktop Settings")]
        [SerializeField]
        private float[] desktopLayerCullDistances = new float[32];

        [SerializeField]
        private bool desktopOverrideClipPlanes;

        [SerializeField]
        private float desktopNearClip = 0.01f;

        [SerializeField]
        private float desktopFarClip = 1000f;

        [Header("Mobile Settings")]
        [SerializeField]
        private float[] mobileLayerCullDistances = new float[32];

        [SerializeField]
        private bool mobileOverrideClipPlanes;

        [SerializeField]
        private float mobileNearClip = 0.01f;

        [SerializeField]
        private float mobileFarClip = 1000f;

    // [HideInInspector]
    [SerializeField]
    private CameraCullingManager manager;

    [HideInInspector]
    [SerializeField]
    private int managerIndex = -1;

    public int Priority => priority;

    public string DisplayLabel => string.IsNullOrEmpty(label) ? name : label;

    public Collider ZoneCollider => zoneCollider;

    public int ManagerIndex => managerIndex;

    private void Awake()
    {
        ResolveCollider();
        EnsureSettingsValid();
    }

    private void OnValidate()
    {
        ResolveCollider();
        EnsureSettingsValid();
    }

    private void OnDisable()
    {
        if (manager != null)
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (Utilities.IsValid(player))
            {
                manager.NotifyZoneExit(this, player);
            }
        }
    }

    public void EnsureSettingsValid()
    {
        EnsureLayerArray(ref desktopLayerCullDistances);
        EnsureLayerArray(ref mobileLayerCullDistances);

        desktopNearClip = Mathf.Clamp(desktopNearClip, 0.001f, 0.05f);
        if (desktopFarClip < desktopNearClip + 0.1f)
        {
            desktopFarClip = desktopNearClip + 0.1f;
        }

        mobileNearClip = Mathf.Clamp(mobileNearClip, 0.001f, 0.05f);
        if (mobileFarClip < mobileNearClip + 0.1f)
        {
            mobileFarClip = mobileNearClip + 0.1f;
        }
    }

    public bool Contains(Vector3 position, out float sqrDistanceToCenter)
    {
        sqrDistanceToCenter = float.MaxValue;

        if (zoneCollider == null || !zoneCollider.enabled || !gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3 closestPoint = zoneCollider.ClosestPoint(position);
        bool inside = (closestPoint - position).sqrMagnitude <= InsideTolerance;
        if (!inside)
        {
            return false;
        }

        sqrDistanceToCenter = (zoneCollider.bounds.center - position).sqrMagnitude;
        return true;
    }

    public float GetSqrDistanceToCenter(Vector3 position)
    {
        if (zoneCollider == null)
        {
            return float.MaxValue;
        }

        Vector3 center = zoneCollider.bounds.center;
        return (center - position).sqrMagnitude;
    }

    public float[] GetDesktopLayerCullDistances()
    {
        return desktopLayerCullDistances;
    }

    public bool ShouldOverrideDesktopClipPlanes()
    {
        return desktopOverrideClipPlanes;
    }

    public float GetDesktopNearClip()
    {
        return desktopNearClip;
    }

    public float GetDesktopFarClip()
    {
        return desktopFarClip;
    }

    public float[] GetMobileLayerCullDistances()
    {
        return mobileLayerCullDistances;
    }

    public bool ShouldOverrideMobileClipPlanes()
    {
        return mobileOverrideClipPlanes;
    }

    public float GetMobileNearClip()
    {
        return mobileNearClip;
    }

    public float GetMobileFarClip()
    {
        return mobileFarClip;
    }

    public void ConfigureManager(CameraCullingManager owner, int index)
    {
        manager = owner;
        managerIndex = index;
    }

    public void ClearManager(CameraCullingManager owner)
    {
        if (manager == owner)
        {
            manager = null;
            managerIndex = -1;
        }
    }

    public override void OnPlayerTriggerEnter(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            return;
        }

        if (manager != null)
        {
            manager.NotifyZoneEnter(this, player);
        }
    }

    public override void OnPlayerTriggerExit(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            return;
        }

        if (manager != null)
        {
            manager.NotifyZoneExit(this, player);
        }
    }

    private void ResolveCollider()
    {
        if (zoneCollider == null)
        {
            zoneCollider = GetComponent<Collider>();
        }

        if (zoneCollider != null)
        {
            zoneCollider.isTrigger = true;
        }
    }

    private static void EnsureLayerArray(ref float[] array)
    {
        if (array == null || array.Length != 32)
        {
            array = new float[32];
        }
    }
}
