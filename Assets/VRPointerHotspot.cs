using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRPointerHotspot : MonoBehaviour
{
    [Header("Tracking Settings")]
    [Tooltip("The transform to shoot the pointer ray from (VR Controller or Camera).")]
    public Transform rayOrigin;

    [Tooltip("Default distance of the pointer hotspot along the ray.")]
    public float defaultDistance = 3.2f;

    [Header("Magnetic Attraction & Meeting Settings")]
    [Tooltip("Distance at which the two hotspots begin to magnetically attract.")]
    public float magneticDistance = 2.2f;

    [Tooltip("Distance between centers to consider the two hotspots as 'meeting'.")]
    public float meetTriggerDistance = 0.80f;

    [Tooltip("Speed of the magnetic pull when approaching a destination hotspot.")]
    public float magneticSnapSpeed = 9.0f;

    [Header("Visual Colors")]
    public Color idleColor = new Color(0.0f, 0.85f, 1.0f, 0.35f);    // Translucent Cyan (unobtrusive)
    public Color attractColor = new Color(1.0f, 0.85f, 0.15f, 0.90f); // Warm Gold when near button
    public Color meetColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);     // Pure White Flash

    private VideoHotspot nearestHotspot;
    private VideoHotspot lastHoveredHotspot;
    private VRControllerVisualizer controllerVisualizer;
    private bool isMeetingActive = false;
    private float cooldownTimer = 1.5f; // Grace period on startup

    // Mouse drag vs click tracking to prevent accidental clicks while looking around
    private Vector2 mouseDownPos;
    private bool isMouseHeld = false;
    private bool isMouseDragged = false;

    private LineRenderer outerRing;
    private LineRenderer innerRing;
    private Transform visualRoot;
    private Camera mainCam;

    private void Awake()
    {
        mainCam = Camera.main;
        controllerVisualizer = FindFirstObjectByType<VRControllerVisualizer>();

        // Auto-find ray origin
        if (rayOrigin == null)
        {
            if (controllerVisualizer != null)
            {
                rayOrigin = controllerVisualizer.PointerTip;
            }
            else
            {
                GameObject rightCtrl = GameObject.Find("RightController");
                if (rightCtrl != null)
                {
                    rayOrigin = rightCtrl.transform;
                }
                else if (mainCam != null)
                {
                    rayOrigin = mainCam.transform;
                }
                else
                {
                    rayOrigin = transform;
                }
            }
        }

        BuildPointerVisuals();
    }

    private void Start()
    {
        if (controllerVisualizer == null)
        {
            controllerVisualizer = FindFirstObjectByType<VRControllerVisualizer>();
        }

        cooldownTimer = 1.2f;
    }

    private void Update()
    {
        if (mainCam == null) mainCam = Camera.main;

        if (controllerVisualizer == null)
        {
            controllerVisualizer = FindFirstObjectByType<VRControllerVisualizer>();
        }

        // Handle cooldown after room transition or startup
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            ResetHoverState();
            UpdatePointerPosition();
            return;
        }

        // 1. Determine effective origin (Controller Tip or Camera)
        Transform effectiveOrigin = rayOrigin;
        if (controllerVisualizer != null)
        {
            effectiveOrigin = controllerVisualizer.PointerTip;
        }
        else if (effectiveOrigin == null || !UnityEngine.XR.XRSettings.isDeviceActive)
        {
            if (mainCam != null) effectiveOrigin = mainCam.transform;
        }

        if (effectiveOrigin == null) return;

        // 2. Calculate raycast forward into 360 space
        Ray ray = new Ray(effectiveOrigin.position, effectiveOrigin.forward);
        Vector3 targetPoint = ray.origin + ray.direction * defaultDistance;

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 20f))
        {
            targetPoint = hit.point;
        }

        // 3. Find nearest active destination doorway hotspot
        nearestHotspot = FindNearestActiveHotspot(targetPoint);

        // 4. Magnetic attraction & Deliberate Meeting logic
        if (nearestHotspot != null && !isMeetingActive)
        {
            float distToHotspot = Vector3.Distance(targetPoint, nearestHotspot.transform.position);

            if (distToHotspot <= magneticDistance)
            {
                // Magnetic pull factor: 0 at outer edge, 1 at hotspot center
                float pullFactor = 1.0f - Mathf.Clamp01(distToHotspot / magneticDistance);

                // Smoothly pull pointer hotspot towards destination hotspot
                float pullStrength = Mathf.SmoothStep(0f, 1f, pullFactor * 1.3f);
                targetPoint = Vector3.Lerp(targetPoint, nearestHotspot.transform.position, pullStrength);

                UpdateVisualColor(Color.Lerp(idleColor, attractColor, pullFactor));
                SetRingScale(1.0f + pullFactor * 0.35f);

                if (nearestHotspot != lastHoveredHotspot)
                {
                    if (lastHoveredHotspot != null) lastHoveredHotspot.SetHoverState(false);
                    lastHoveredHotspot = nearestHotspot;
                    lastHoveredHotspot.SetHoverState(true);
                }

                // Check physical distance between the two hotspots
                float currentMeetingDist = Vector3.Distance(transform.position, nearestHotspot.transform.position);

                if (currentMeetingDist <= meetTriggerDistance)
                {
                    // Snap lock feedback: glow white to indicate ready to click
                    UpdateVisualColor(meetColor);
                    SetRingScale(1.20f);
                }
            }
            else
            {
                ResetHoverState();
            }
        }
        else if (!isMeetingActive)
        {
            ResetHoverState();
        }

        // 5. Intentional user click / trigger action (NO automatic clicking on hover)
        bool clickTriggered = false;
        if (controllerVisualizer != null && controllerVisualizer.IsTriggerPressed)
        {
            clickTriggered = true;
        }

        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                mouseDownPos = mouse.position.ReadValue();
                isMouseHeld = true;
                isMouseDragged = false;
            }
            else if (isMouseHeld && mouse.leftButton.isPressed)
            {
                // If moved more than 10 pixels, treat as camera pan drag (do NOT trigger click)
                if (Vector2.Distance(mouse.position.ReadValue(), mouseDownPos) > 10f)
                {
                    isMouseDragged = true;
                }
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                // Released cleanly without dragging = intentional user click!
                if (isMouseHeld && !isMouseDragged)
                {
                    clickTriggered = true;
                }
                isMouseHeld = false;
                isMouseDragged = false;
            }
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.enterKey.wasPressedThisFrame) clickTriggered = true;
        }

        // Only transition when the user deliberately clicked or pulled trigger while aiming at the button
        if (clickTriggered && nearestHotspot != null && !isMeetingActive && cooldownTimer <= 0f)
        {
            float dist = Vector3.Distance(transform.position, nearestHotspot.transform.position);
            if (dist <= meetTriggerDistance * 1.6f || (lastHoveredHotspot != null && lastHoveredHotspot == nearestHotspot))
            {
                StartCoroutine(ExecuteHotspotsMeetRoutine(nearestHotspot));
            }
        }

        // 6. Smooth pointer motion
        transform.position = Vector3.Lerp(transform.position, targetPoint, Time.deltaTime * 20f);

        // 7. Always billboard the rings toward the camera
        BillboardRings();
    }

    private void UpdatePointerPosition()
    {
        Transform origin = rayOrigin;
        if (controllerVisualizer != null) origin = controllerVisualizer.PointerTip;
        else if (origin == null || !UnityEngine.XR.XRSettings.isDeviceActive)
        {
            if (mainCam != null) origin = mainCam.transform;
        }

        if (origin != null)
        {
            Vector3 target = origin.position + origin.forward * defaultDistance;
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 20f);
            BillboardRings();
        }
    }

    private void BillboardRings()
    {
        if (visualRoot != null && mainCam != null)
        {
            Vector3 lookDir = transform.position - mainCam.transform.position;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                visualRoot.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
            }
        }
    }

    private void ResetHoverState()
    {
        if (lastHoveredHotspot != null)
        {
            lastHoveredHotspot.SetHoverState(false);
            lastHoveredHotspot = null;
        }
        UpdateVisualColor(idleColor);
        SetRingScale(1.0f);
    }

    /// <summary>
    /// Executes the animated fusion when the two hotspots meet.
    /// </summary>
    private IEnumerator ExecuteHotspotsMeetRoutine(VideoHotspot destination)
    {
        isMeetingActive = true;

        // Snap to exact center of destination hotspot
        transform.position = destination.transform.position;
        UpdateVisualColor(meetColor);

        // Expansion shockwave effect
        float elapsed = 0f;
        float duration = 0.22f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / duration;
            SetRingScale(Mathf.Lerp(1.2f, 2.0f, t));
            yield return null;
        }

        // Trigger destination transition to next 360 video!
        if (destination != null)
        {
            destination.SelectHotspot();
        }

        // Generous cooldown timer to prevent accidental immediate re-trigger in new scene
        cooldownTimer = 1.8f;
        yield return new WaitForSecondsRealtime(0.40f);

        isMeetingActive = false;
        SetRingScale(1.0f);
        UpdateVisualColor(idleColor);

        if (lastHoveredHotspot != null)
        {
            lastHoveredHotspot.SetHoverState(false);
            lastHoveredHotspot = null;
        }
    }

    public void SetCooldown(float seconds)
    {
        cooldownTimer = seconds;
        ResetHoverState();
    }

    private VideoHotspot FindNearestActiveHotspot(Vector3 point)
    {
        var all = FindObjectsByType<VideoHotspot>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        VideoHotspot closest = null;
        float minDist = float.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            float d = Vector3.Distance(point, all[i].transform.position);
            if (d < minDist)
            {
                minDist = d;
                closest = all[i];
            }
        }

        return closest;
    }

    private void UpdateVisualColor(Color c)
    {
        if (outerRing != null)
        {
            outerRing.startColor = c;
            outerRing.endColor = c;
        }
        if (innerRing != null)
        {
            innerRing.startColor = c;
            innerRing.endColor = c;
        }
    }

    private void SetRingScale(float scale)
    {
        if (visualRoot != null)
        {
            visualRoot.localScale = Vector3.one * scale;
        }
    }

    private void BuildPointerVisuals()
    {
        visualRoot = new GameObject("VisualRoot").transform;
        visualRoot.SetParent(transform, false);

        Material lineMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));

        // Outer Ring - Sleek hairline ring
        GameObject outerObj = new GameObject("OuterRing");
        outerObj.transform.SetParent(visualRoot, false);
        outerRing = outerObj.AddComponent<LineRenderer>();
        ConfigureLineRing(outerRing, lineMat, 0.045f, 0.003f);

        // Inner Ring - Subtle center dot/ring
        GameObject innerObj = new GameObject("InnerRing");
        innerObj.transform.SetParent(visualRoot, false);
        innerRing = innerObj.AddComponent<LineRenderer>();
        ConfigureLineRing(innerRing, lineMat, 0.012f, 0.0025f);

        UpdateVisualColor(idleColor);
    }

    private void ConfigureLineRing(LineRenderer lr, Material mat, float radius, float width)
    {
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.material = mat;

        int segments = 32;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }
}
