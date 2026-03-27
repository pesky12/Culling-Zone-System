using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
using System.Reflection;
#endif

public class CameraCullingManager : UdonSharpBehaviour
#if !COMPILER_UDONSHARP && UNITY_EDITOR
    , VRC.SDKBase.IPreprocessCallbackBehaviour
#endif
{
    [Tooltip(
        "Culling distances for each of the 32 GameObject layers. A value of 0 for a layer means it will use the camera's FarClipPlane.")]
    [SerializeField]
    private float[] layerCullDistances = new float[32];

    [Tooltip(
        "Culling distances applied for the Android and iOS platforms. A value of 0 for a layer means it will use the camera's FarClipPlane.")]
    [SerializeField]
    private float[] layerCullDistancesMobile = new float[32];

    [Tooltip("Apply these culling distances to the user's screen camera.")] [SerializeField]
    private bool applyToScreenCamera = true;

    [Tooltip(
        "Apply these culling distances to the user's photo camera. Note: Settings will only update if the user has the photo camera enabled.")]
    [SerializeField]
    private bool applyToPhotoCamera;

    [Tooltip("Override near and far clip planes for desktop platforms.")]
    [SerializeField]
    private bool overrideClipPlanesDesktop;

    [Tooltip("Near clip plane for desktop platforms (clamped between 0.001 and 0.05).")]
    [SerializeField]
    private float nearClipDesktop = 0.01f;

    [Tooltip("Far clip plane for desktop platforms (at least near + 0.1).")]
    [SerializeField]
    private float farClipDesktop = 1000f;

    [Tooltip("Override near and far clip planes for mobile platforms.")]
    [SerializeField]
    private bool overrideClipPlanesMobile;

    [Tooltip("Near clip plane for mobile platforms (clamped between 0.001 and 0.05).")]
    [SerializeField]
    private float nearClipMobile = 0.01f;

    [Tooltip("Far clip plane for mobile platforms (at least near + 0.1).")]
    [SerializeField]
    private float farClipMobile = 1000f;

    [Tooltip("Force mobile (Quest) values on desktop for debugging purposes.")]
    [SerializeField]
    private bool forceMobileOnDesktop;

    [Header("Dynamic Zones")]
    [Tooltip("Culling zones that override the ambient settings when the local player is inside them.")]
    [SerializeField]
    private CameraCullingZone[] cullingZones = new CameraCullingZone[0];

    [Tooltip("Prefer the closest zone when two zones share the same priority.")]
    [SerializeField]
    private bool preferClosestWhenSamePriority = true;

    [Tooltip("Log whenever the active culling zone changes.")]
    [SerializeField]
    private bool logZoneTransitions;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
    public int PreprocessOrder => 0;

    public bool OnPreprocess()
    {
        Debug.Log($"[CameraCullingManager] Pre-processing build: Updating zones for {name}.");
        FindAllCullingZones();
        return true;
    }

    public void FindAllCullingZones()
    {
        Undo.RecordObject(this, "Find all culling zones");
        
        var zones = GameObject.FindObjectsOfType<CameraCullingZone>();
        cullingZones = zones;
        
        // Ensure all zones are valid and linked
        CacheZones();
        
        EditorUtility.SetDirty(this);
        Debug.Log($"[CameraCullingManager] Found and linked {cullingZones.Length} culling zones.");
    }
    
    private void CacheZonesEditor()
    {
        if (cullingZones == null || cullingZones.Length == 0) return;
        cachedZones = new CameraCullingZone[cullingZones.Length];
        zoneOccupancy = new bool[cullingZones.Length];
        for (int i = 0; i < cullingZones.Length; i++)
        {
            if (cullingZones[i] == null) continue;
            cachedZones[i] = cullingZones[i];
            cullingZones[i].ConfigureManager(this, i);
        }
    }
#endif

    private CameraCullingZone[] cachedZones = new CameraCullingZone[0];
    private bool[] zoneOccupancy = new bool[0];
    private CameraCullingZone currentZone;
    private VRCPlayerApi localPlayer;
    private bool awaitingInitialSync;

    // Named constants for clip plane bounds and tolerances
    private const float NearClipPlaneMin = 0.001f;
    private const float NearClipPlaneMax = 0.05f;
    private const float MinFarClipAboveNear = 0.1f;
    private const float ClipPlaneTolerance = 0.0001f;
    private const float InitialSyncDelaySeconds = 0.25f;

    private void Start()
    {
        ValidateAmbientSettings();
        CacheZones();
        ProcessCameraCulling(null);
        awaitingInitialSync = true;
        SendCustomEventDelayedSeconds(nameof(SynchronizeActiveZone), InitialSyncDelaySeconds);
    }

    private void OnValidate()
    {
        ValidateAmbientSettings();
        CacheZones();
    }
    
    public void changeCullingLayerDistances(int layer, float distance)
    {
        if (layer < 0 || layer >= layerCullDistances.Length)
        {
            Debug.LogError($"[CameraCullingManager] Invalid layer index: {layer}. Must be between 0 and {layerCullDistances.Length - 1}.");
            return;
        }

        layerCullDistances[layer] = distance;
        layerCullDistancesMobile[layer] = distance;
        Debug.Log($"[CameraCullingManager] Changed culling distance for layer {layer} to {distance}.");

        ValidateAmbientSettings();

        // Reapply culling distances after change using the current zone context
        ProcessCameraCulling(currentZone);
    }

    private void ProcessCameraCulling(CameraCullingZone zoneOverride)
    {
        ValidateAmbientSettings();

        // Check if the user has "Forced Camera Near Distance" enabled.
        // If so, we must not apply any custom culling settings to avoid breaking rendering.
        var checkScreenCam = VRCCameraSettings.ScreenCamera;
        if (checkScreenCam != null)
        {
            float originalNear = checkScreenCam.NearClipPlane;
            // Try to change the value to detect if it's forced
            float testNear = Mathf.Clamp(originalNear + 0.01f, NearClipPlaneMin, NearClipPlaneMax);
            if (Mathf.Abs(testNear - originalNear) < ClipPlaneTolerance)
            {
                testNear = Mathf.Clamp(originalNear - 0.01f, NearClipPlaneMin, NearClipPlaneMax);
            }

            checkScreenCam.NearClipPlane = testNear;
            bool isForced = Mathf.Abs(checkScreenCam.NearClipPlane - testNear) > ClipPlaneTolerance;

            // Restore original value if not forced (if forced, we can't change it anyway)
            if (!isForced)
            {
                checkScreenCam.NearClipPlane = originalNear;
            }
            else
            {
                if (logZoneTransitions)
                {
                    Debug.Log("[CameraCullingManager] User has 'Forced Camera Near Distance' enabled. Custom culling disabled.");
                }
                return;
            }
        }

        if (zoneOverride != null)
        {
            zoneOverride.EnsureSettingsValid();
        }

        float[] desktopDistances = zoneOverride != null ? zoneOverride.GetDesktopLayerCullDistances() : layerCullDistances;
        bool desktopOverrideClip = zoneOverride != null ? zoneOverride.ShouldOverrideDesktopClipPlanes() : overrideClipPlanesDesktop;
        float desktopNearClip = zoneOverride != null ? zoneOverride.GetDesktopNearClip() : nearClipDesktop;
        float desktopFarClip = zoneOverride != null ? zoneOverride.GetDesktopFarClip() : farClipDesktop;

        float[] mobileDistances = zoneOverride != null ? zoneOverride.GetMobileLayerCullDistances() : layerCullDistancesMobile;
        bool mobileOverrideClip = zoneOverride != null ? zoneOverride.ShouldOverrideMobileClipPlanes() : overrideClipPlanesMobile;
        float mobileNearClip = zoneOverride != null ? zoneOverride.GetMobileNearClip() : nearClipMobile;
        float mobileFarClip = zoneOverride != null ? zoneOverride.GetMobileFarClip() : farClipMobile;

        string contextLabel = zoneOverride != null ? zoneOverride.DisplayLabel : "Ambient";

        if (applyToScreenCamera)
        {
            var screenCamSettings = VRCCameraSettings.ScreenCamera;
            if (screenCamSettings != null)
            {
#if UNITY_ANDROID || UNITY_IOS
                screenCamSettings.LayerCullDistances = mobileDistances;

                if (mobileOverrideClip)
                {
                    screenCamSettings.NearClipPlane = mobileNearClip;
                    screenCamSettings.FarClipPlane = mobileFarClip;
                }
#endif

#if UNITY_STANDALONE_WIN
                screenCamSettings.LayerCullDistances = forceMobileOnDesktop ? mobileDistances : desktopDistances;

                bool overrideClip = forceMobileOnDesktop ? mobileOverrideClip : desktopOverrideClip;
                if (overrideClip)
                {
                    float nearClip = forceMobileOnDesktop ? mobileNearClip : desktopNearClip;
                    float farClip = forceMobileOnDesktop ? mobileFarClip : desktopFarClip;
                    screenCamSettings.NearClipPlane = nearClip;
                    screenCamSettings.FarClipPlane = farClip;
                }
#endif

                if (logZoneTransitions)
                {
                    Debug.Log($"[CameraCullingManager] Applied '{contextLabel}' settings to ScreenCamera.");
                }
            }
            else
            {
                Debug.LogError(
                    "[CameraCullingManager] VRC.SDKBase.VRCCameraSettings.ScreenCamera is null. Cannot apply culling distances.");
            }
        }

        if (applyToPhotoCamera)
        {
            var photoCamSettings = VRCCameraSettings.PhotoCamera;
            if (photoCamSettings != null)
            {
#if UNITY_ANDROID || UNITY_IOS
                photoCamSettings.LayerCullDistances = mobileDistances;

                if (mobileOverrideClip)
                {
                    photoCamSettings.NearClipPlane = mobileNearClip;
                    photoCamSettings.FarClipPlane = mobileFarClip;
                }
#endif

#if UNITY_STANDALONE_WIN
                photoCamSettings.LayerCullDistances = forceMobileOnDesktop ? mobileDistances : desktopDistances;

                bool overrideClip = forceMobileOnDesktop ? mobileOverrideClip : desktopOverrideClip;
                if (overrideClip)
                {
                    float nearClip = forceMobileOnDesktop ? mobileNearClip : desktopNearClip;
                    float farClip = forceMobileOnDesktop ? mobileFarClip : desktopFarClip;
                    photoCamSettings.NearClipPlane = nearClip;
                    photoCamSettings.FarClipPlane = farClip;
                }
#endif

                if (logZoneTransitions)
                {
                    Debug.Log($"[CameraCullingManager] Applied '{contextLabel}' settings to PhotoCamera.");
                }
            }
            else
            {
                Debug.LogWarning(
                    "[CameraCullingManager] VRC.SDKBase.VRCCameraSettings.PhotoCamera is null. Cannot apply culling distances. This is normal in ClientSim.");
            }
        }
    }

    private void ValidateAmbientSettings()
    {
        EnsureLayerArray(ref layerCullDistances);
        EnsureLayerArray(ref layerCullDistancesMobile);

        nearClipDesktop = Mathf.Clamp(nearClipDesktop, NearClipPlaneMin, NearClipPlaneMax);
        if (farClipDesktop < nearClipDesktop + MinFarClipAboveNear)
        {
            farClipDesktop = nearClipDesktop + MinFarClipAboveNear;
        }

        nearClipMobile = Mathf.Clamp(nearClipMobile, NearClipPlaneMin, NearClipPlaneMax);
        if (farClipMobile < nearClipMobile + MinFarClipAboveNear)
        {
            farClipMobile = nearClipMobile + MinFarClipAboveNear;
        }
    }

    private static void EnsureLayerArray(ref float[] array)
    {
        if (array == null || array.Length != 32)
        {
            array = new float[32];
        }
    }

    private void CacheZones()
    {
        if (cachedZones != null)
        {
            for (int i = 0; i < cachedZones.Length; i++)
            {
                CameraCullingZone zone = cachedZones[i];
                if (zone != null)
                {
                    zone.ClearManager(this);
                }
            }
        }

        if (cullingZones == null || cullingZones.Length == 0)
        {
            cachedZones = new CameraCullingZone[0];
            zoneOccupancy = new bool[0];
            return;
        }

        int count = 0;
        for (int i = 0; i < cullingZones.Length; i++)
        {
            CameraCullingZone zone = cullingZones[i];
            if (zone == null)
            {
                continue;
            }

            zone.EnsureSettingsValid();
            count++;
        }

        cachedZones = new CameraCullingZone[count];
        zoneOccupancy = new bool[count];
        int index = 0;
        for (int i = 0; i < cullingZones.Length; i++)
        {
            CameraCullingZone zone = cullingZones[i];
            if (zone == null)
            {
                continue;
            }

            cachedZones[index++] = zone;
            zone.ConfigureManager(this, index - 1);
        }

        currentZone = null;
    }

    public void NotifyZoneEnter(CameraCullingZone zone, VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            return;
        }

        localPlayer = player;
        awaitingInitialSync = false;

        SetZoneOccupancy(zone, true);
        EvaluateActiveZone(player.GetPosition());
    }

    public void NotifyZoneExit(CameraCullingZone zone, VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player) || !player.isLocal)
        {
            return;
        }

        localPlayer = player;
        awaitingInitialSync = false;

        SetZoneOccupancy(zone, false);
        EvaluateActiveZone(player.GetPosition());
    }

    public void SynchronizeActiveZone()
    {
        if (localPlayer == null)
        {
            localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                if (awaitingInitialSync)
                {
                    SendCustomEventDelayedSeconds(nameof(SynchronizeActiveZone), InitialSyncDelaySeconds);
                }
                return;
            }
        }

        awaitingInitialSync = false;

        for (int i = 0; i < zoneOccupancy.Length; i++)
        {
            zoneOccupancy[i] = false;
        }

        Vector3 playerPosition = localPlayer.GetPosition();
        int initialIndex = FindInitialZoneIndex(playerPosition);
        if (initialIndex >= 0 && initialIndex < zoneOccupancy.Length)
        {
            zoneOccupancy[initialIndex] = true;
        }

        EvaluateActiveZone(playerPosition);
    }

    private void SetZoneOccupancy(CameraCullingZone zone, bool inside)
    {
        if (zone == null)
        {
            return;
        }

        int index = zone.ManagerIndex;
        if (index < 0 || index >= zoneOccupancy.Length)
        {
            CacheZones();
            index = zone.ManagerIndex;
            if (index < 0 || index >= zoneOccupancy.Length)
            {
                return;
            }
        }

        zoneOccupancy[index] = inside;
    }

    private void EvaluateActiveZone(Vector3 playerPosition)
    {
        CameraCullingZone nextZone = null;
        int bestPriority = int.MinValue;
        float bestSqrDistance = float.MaxValue;

        for (int i = 0; i < zoneOccupancy.Length; i++)
        {
            if (!zoneOccupancy[i])
            {
                continue;
            }

            CameraCullingZone zone = cachedZones[i];
            if (zone == null)
            {
                continue;
            }

            if (!zone.gameObject.activeInHierarchy || !zone.enabled)
            {
                zoneOccupancy[i] = false;
                continue;
            }

            int priority = zone.Priority;
            if (nextZone == null || priority > bestPriority)
            {
                nextZone = zone;
                bestPriority = priority;
                bestSqrDistance = zone.GetSqrDistanceToCenter(playerPosition);
                continue;
            }

            if (priority == bestPriority && preferClosestWhenSamePriority)
            {
                float sqrDistance = zone.GetSqrDistanceToCenter(playerPosition);
                if (sqrDistance < bestSqrDistance)
                {
                    nextZone = zone;
                    bestSqrDistance = sqrDistance;
                }
            }
        }

        if (nextZone != currentZone)
        {
            currentZone = nextZone;
            if (logZoneTransitions)
            {
                Debug.Log($"[CameraCullingManager] Active culling zone set to '{(currentZone != null ? currentZone.DisplayLabel : "Ambient")}'.");
            }

            ProcessCameraCulling(currentZone);
        }
        else if (nextZone == null && currentZone == null)
        {
            ProcessCameraCulling(null);
        }
    }

    private int FindInitialZoneIndex(Vector3 playerPosition)
    {
        CameraCullingZone bestZone = null;
        int bestIndex = -1;
        int bestPriority = int.MinValue;
        float bestSqrDistance = float.MaxValue;

        for (int i = 0; i < cachedZones.Length; i++)
        {
            CameraCullingZone zone = cachedZones[i];
            if (zone == null)
            {
                continue;
            }

            if (!zone.Contains(playerPosition, out float sqrDistance))
            {
                continue;
            }

            int priority = zone.Priority;
            if (bestZone == null || priority > bestPriority)
            {
                bestZone = zone;
                bestIndex = i;
                bestPriority = priority;
                bestSqrDistance = sqrDistance;
                continue;
            }

            if (priority == bestPriority && preferClosestWhenSamePriority && sqrDistance < bestSqrDistance)
            {
                bestZone = zone;
                bestIndex = i;
                bestSqrDistance = sqrDistance;
            }
        }

        return bestIndex;
    }
}

#if !COMPILER_UDONSHARP && UNITY_EDITOR

[CustomEditor(typeof(CameraCullingManager))]
public class CameraCullingManagerEditor : Editor
{
    private SerializedProperty layerCullDistances;
    private SerializedProperty layerCullDistancesMobile;
    private SerializedProperty applyToScreenCamera;
    private SerializedProperty applyToPhotoCamera;

    private SerializedProperty overrideClipPlanesDesktop;
    private SerializedProperty nearClipDesktop;
    private SerializedProperty farClipDesktop;
    private SerializedProperty overrideClipPlanesMobile;
    private SerializedProperty nearClipMobile;
    private SerializedProperty farClipMobile;

    private SerializedProperty forceMobileOnDesktop;
    private SerializedProperty cullingZones;
    private SerializedProperty preferClosestWhenSamePriority;
    private SerializedProperty logZoneTransitions;

    private int newLayerDesktop = 0;
    private float newDistDesktop = 0f;
    private int newLayerMobile = 0;
    private float newDistMobile = 0f;

    private void OnEnable()
    {
        layerCullDistances = serializedObject.FindProperty("layerCullDistances");
        layerCullDistancesMobile = serializedObject.FindProperty("layerCullDistancesMobile");
        applyToScreenCamera = serializedObject.FindProperty("applyToScreenCamera");
        applyToPhotoCamera = serializedObject.FindProperty("applyToPhotoCamera");

        overrideClipPlanesDesktop = serializedObject.FindProperty("overrideClipPlanesDesktop");
        nearClipDesktop = serializedObject.FindProperty("nearClipDesktop");
        farClipDesktop = serializedObject.FindProperty("farClipDesktop");
        overrideClipPlanesMobile = serializedObject.FindProperty("overrideClipPlanesMobile");
        nearClipMobile = serializedObject.FindProperty("nearClipMobile");
        farClipMobile = serializedObject.FindProperty("farClipMobile");

        forceMobileOnDesktop = serializedObject.FindProperty("forceMobileOnDesktop");
        cullingZones = serializedObject.FindProperty("cullingZones");
        preferClosestWhenSamePriority = serializedObject.FindProperty("preferClosestWhenSamePriority");
        logZoneTransitions = serializedObject.FindProperty("logZoneTransitions");
    }

    public override void OnInspectorGUI()
    {
        CameraCullingManager manager = (CameraCullingManager)target;
        serializedObject.Update();

        EditorGUILayout.PropertyField(applyToScreenCamera);
        EditorGUILayout.PropertyField(applyToPhotoCamera);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Dynamic Zones", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Assign culling zones to override the ambient settings when players enter their trigger volumes.", MessageType.Info);
        
        GUI.backgroundColor = Color.magenta;
        if (GUILayout.Button("Find all culling zones in scene"))
        {
            manager.FindAllCullingZones();
        }
        GUI.backgroundColor = Color.white;
        
        EditorGUILayout.PropertyField(cullingZones, true);
        EditorGUILayout.PropertyField(preferClosestWhenSamePriority);
        EditorGUILayout.PropertyField(logZoneTransitions);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Debug Options", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(forceMobileOnDesktop);

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Desktop Culling Overrides", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Only overridden layers (non-zero distances) are shown. 0 uses the camera's FarClipPlane.", MessageType.Info);

        for (int i = 0; i < 32; i++)
        {
            SerializedProperty elem = layerCullDistances.GetArrayElementAtIndex(i);
            if (elem.floatValue != 0f)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(LayerMask.LayerToName(i), GUILayout.Width(150));
                EditorGUILayout.PropertyField(elem, GUIContent.none);
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    elem.floatValue = 0f;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Add/Update Override");
        newLayerDesktop = EditorGUILayout.LayerField("Layer", newLayerDesktop);
        newDistDesktop = EditorGUILayout.FloatField("Distance", newDistDesktop);
        if (GUILayout.Button("Apply"))
        {
            if (newLayerDesktop >= 0 && newLayerDesktop < 32)
            {
                layerCullDistances.GetArrayElementAtIndex(newLayerDesktop).floatValue = newDistDesktop;
                newDistDesktop = 0f; // Reset field for next use
            }
            else
            {
                Debug.LogWarning("Invalid layer selected.");
            }
        }

        EditorGUILayout.Space(20);

        EditorGUILayout.LabelField("Mobile Culling Overrides", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Only overridden layers (non-zero distances) are shown. 0 uses the camera's FarClipPlane.", MessageType.Info);

        for (int i = 0; i < 32; i++)
        {
            SerializedProperty elem = layerCullDistancesMobile.GetArrayElementAtIndex(i);
            if (elem.floatValue != 0f)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(LayerMask.LayerToName(i), GUILayout.Width(150));
                EditorGUILayout.PropertyField(elem, GUIContent.none);
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    elem.floatValue = 0f;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Add/Update Override");
        newLayerMobile = EditorGUILayout.LayerField("Layer", newLayerMobile);
        newDistMobile = EditorGUILayout.FloatField("Distance", newDistMobile);
        if (GUILayout.Button("Apply"))
        {
            if (newLayerMobile >= 0 && newLayerMobile < 32)
            {
                layerCullDistancesMobile.GetArrayElementAtIndex(newLayerMobile).floatValue = newDistMobile;
                newDistMobile = 0f; // Reset field for next use
            }
            else
            {
                Debug.LogWarning("Invalid layer selected.");
            }
        }

        EditorGUILayout.Space(20);

        EditorGUILayout.LabelField("Desktop Clip Planes Override", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Enable to override near and far clip planes for desktop platforms. Note: Values are clamped by VRChat (Near: 0.001-0.05, Far: Near + 0.1 min). May interact with user settings.", MessageType.Info);
        EditorGUILayout.PropertyField(overrideClipPlanesDesktop);
        if (overrideClipPlanesDesktop.boolValue)
        {
            EditorGUILayout.PropertyField(nearClipDesktop);
            EditorGUILayout.PropertyField(farClipDesktop);
        }

        EditorGUILayout.Space(20);

        EditorGUILayout.LabelField("Mobile Clip Planes Override", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Enable to override near and far clip planes for mobile platforms. Note: Values are clamped by VRChat (Near: 0.001-0.05, Far: Near + 0.1 min). May interact with user settings.", MessageType.Info);
        EditorGUILayout.PropertyField(overrideClipPlanesMobile);
        if (overrideClipPlanesMobile.boolValue)
        {
            EditorGUILayout.PropertyField(nearClipMobile);
            EditorGUILayout.PropertyField(farClipMobile);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
