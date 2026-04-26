using UnityEngine;
using UnityEngine.Serialization;
using PlanetGeneration;

namespace PlanetaryFirstPersonController
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlanetaryFirstPersonController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("If autoDetectPlanets = true, this parameter will be overwritten by the nearest planet.")]
        public Transform planet;
        public Transform playerCamera;
        public LayerMask groundLayers = ~0;

        [Header("Movement")]
        public float moveSpeed = 6f;
        public float sprintMultiplier = 1.8f;
        public float jumpHeight = 1.6f;
        [Range(0f, 1f)] public float airControl = 0.6f;
        [Tooltip("Small downward speed used to keep the player grounded.")]
        public float groundStickSpeed = 0.5f;

        [Header("Mouse Look")]
        [Tooltip("General mouse sensitivity applied to both X and Y. Recommended range: 2 - 5.")]
        public float mouseSensitivity = 3.5f;
        [Tooltip("0 = no smoothing, >0 = smoothing speed (1/s).")]
        public float mouseSmoothing = 12f;
        public float maxLookAngle = 85f;
        public bool invertY = false;

        // Legacy serialized fields — hidden and used only for safe migration.
        [SerializeField, HideInInspector, FormerlySerializedAs("mouseSensitivityX")]
        private float legacyMouseSensitivityX;
        [SerializeField, HideInInspector, FormerlySerializedAs("mouseSensitivityY")]
        private float legacyMouseSensitivityY;
        [SerializeField, HideInInspector]
        private bool legacyMouseSensitivityMigrated = false;

        [Header("Gravity")]
        public bool useInverseSquare = false;
        public float gravityAtSurface = 9.81f;
        public float planetRadius = 1000f; // Adapted to your radius from PlanetGenerator
        [Tooltip("Distance used for ground checks from the player's feet.")]
        public float groundCheckDistance = 0.15f;

        [Header("Auto-detect multiple planets")]
        [Tooltip("If true, the script will automatically find the nearest planet (PlanetGenerator) and use it.")]
        public bool autoDetectPlanets = true;
        [Tooltip("Influence zone = planet.radius * gravityRangeMultiplier")]
        public float gravityRangeMultiplier = 2.5f;
        [Tooltip("How often (sec) to update the list of planets in the scene (FindObjectsOfType)")]
        public float planetListRefreshInterval = 2.0f;

        [Header("Rotation / Smoothing")]
        [Tooltip("Speed for aligning body to the surface normal.")]
        public float alignSpeed = 10f;

        [Header("Spawn / Safety")]
        [Tooltip("If the player starts intersecting geometry, offset outward by this clearance.")]
        public float spawnClearance = 0.05f;
        [Tooltip("Maximum distance to search for surface during spawn resolution.")]
        public float spawnSearchDistance = 2000f;

        [Header("PC Controls")]
        [Tooltip("Enable keyboard/mouse control.")]
        public bool enablePCControls = true;

        // internals
        Rigidbody rb;
        CapsuleCollider col;

        // input
        float inputX, inputZ;
        bool wantJump;
        bool wantSprint;

        // mouse state
        float smoothedMouseX = 0f;
        float smoothedMouseY = 0f;
        float pitch = 0f;    // camera pitch (local)
        Vector3 surfaceForward = Vector3.forward;
        bool surfaceForwardInitialized = false;

        // physics state
        bool isGrounded = false;
        Vector3 lastGroundNormal = Vector3.up;
        float verticalVelAlongUp = 0f;

        // cache for desired rotation (keeps Update/FixedUpdate consistent)
        Quaternion desiredRotationCache = Quaternion.identity;

        // planet detection cache
        private PlanetGenerator[] cachedPlanets = new PlanetGenerator[0];
        private float lastPlanetListRefresh = -999f;
        private PlanetGenerator currentPlanetGen = null;
        private Transform lastAssignedPlanetTransform = null;
        readonly RaycastHit[] groundHitBuffer = new RaycastHit[12];
        readonly RaycastHit[] raycastBuffer = new RaycastHit[12];
        readonly Collider[] overlapBuffer = new Collider[16];

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            col = GetComponent<CapsuleCollider>();

            // Rigidbody setup
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            col.direction = 1; // Y

            // lock cursor only if PC controls enabled
            if (enablePCControls)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (autoDetectPlanets)
                RefreshPlanetListImmediate();

            if (planet != null)
            {
                AlignInitialRotationAndYaw();
                ResolveSpawnIfInsideGeometry();
            }

            rb.velocity = Vector3.zero;
        }

        void OnValidate()
        {
            // Safe migration from legacy mouseSensitivityX/Y to single mouseSensitivity.
            if (!legacyMouseSensitivityMigrated)
            {
                if (legacyMouseSensitivityX > 1e-6f || legacyMouseSensitivityY > 1e-6f)
                {
                    float converted = 0f;
                    int cnt = 0;
                    if (legacyMouseSensitivityX > 1e-6f)
                    {
                        converted += (legacyMouseSensitivityX > 20f) ? legacyMouseSensitivityX / 60f : legacyMouseSensitivityX;
                        cnt++;
                    }
                    if (legacyMouseSensitivityY > 1e-6f)
                    {
                        converted += (legacyMouseSensitivityY > 20f) ? legacyMouseSensitivityY / 60f : legacyMouseSensitivityY;
                        cnt++;
                    }
                    if (cnt > 0)
                    {
                        mouseSensitivity = converted / cnt;
                    }

                    legacyMouseSensitivityX = 0f;
                    legacyMouseSensitivityY = 0f;
                }

                legacyMouseSensitivityMigrated = true;
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus && enablePCControls && !PlanetGeneration.GameUI.IsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void Update()
        {
            // reset inputs
            inputX = 0f;
            inputZ = 0f;
            wantSprint = false;

            // Planet detection always runs regardless of UI state
            if (autoDetectPlanets)
                UpdateNearestPlanet();

            if (planet == null) return;

            // While UI is open – force cursor free every frame and skip all input
            if (PlanetGeneration.GameUI.IsOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
                return;
            }

            // PC input only if enabled
            if (enablePCControls)
            {
                inputX = Input.GetAxisRaw("Horizontal");
                inputZ = Input.GetAxisRaw("Vertical");
                wantJump = wantJump || Input.GetButtonDown("Jump");
                wantSprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }

            // Mouse input
            float rawX = 0f;
            float rawY = 0f;

            if (enablePCControls)
            {
                rawX += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                rawY += Input.GetAxisRaw("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);
            }

            // Smoothing
            if (mouseSmoothing > 1e-5f)
            {
                float alpha = 1f - Mathf.Exp(-mouseSmoothing * Time.deltaTime);
                smoothedMouseX = Mathf.Lerp(smoothedMouseX, rawX, alpha);
                smoothedMouseY = Mathf.Lerp(smoothedMouseY, rawY, alpha);

                float settleSpeed = Mathf.Max(12f, mouseSmoothing * 2.4f);
                if (Mathf.Abs(rawX) < 0.0001f)
                    smoothedMouseX = Mathf.MoveTowards(smoothedMouseX, 0f, settleSpeed * Time.deltaTime);
                if (Mathf.Abs(rawY) < 0.0001f)
                    smoothedMouseY = Mathf.MoveTowards(smoothedMouseY, 0f, settleSpeed * Time.deltaTime);
            }
            else
            {
                smoothedMouseX = rawX;
                smoothedMouseY = rawY;
            }

            if (Mathf.Abs(smoothedMouseX) < 0.001f) smoothedMouseX = 0f;
            if (Mathf.Abs(smoothedMouseY) < 0.001f) smoothedMouseY = 0f;

            // Camera pitch
            pitch += smoothedMouseY;
            pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);
            if (playerCamera) playerCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Compute desired rotation
            Vector3 toCenter = (planet.position - transform.position);
            Vector3 gravityDir = toCenter.normalized;
            Vector3 desiredUp = -gravityDir;

            EnsureSurfaceForward(desiredUp);
            ApplyYawToSurfaceForward(desiredUp, smoothedMouseX);

            desiredRotationCache = Quaternion.LookRotation(surfaceForward, desiredUp);
            transform.rotation = desiredRotationCache;
        }

        void FixedUpdate()
        {
            if (planet == null) return;
            float dt = Time.fixedDeltaTime;

            // Gravity direction
            Vector3 toCenter = (planet.position - transform.position);
            float distance = toCenter.magnitude;
            Vector3 gravityDir = toCenter.normalized;
            Vector3 desiredUp = -gravityDir;

            // Ground check
            Vector3 centerWorld = transform.TransformPoint(col.center);
            float sphereRadius = Mathf.Max(0.01f, col.radius * 0.9f);
            float castDist = Mathf.Max(0.01f, (col.height * 0.5f - col.radius) + groundCheckDistance);

            RaycastHit hit;
            isGrounded = TrySphereCastGround(centerWorld, sphereRadius, gravityDir, castDist, out hit);

            Vector3 groundNormal = lastGroundNormal;
            if (isGrounded)
            {
                groundNormal = hit.normal;
                lastGroundNormal = groundNormal;
            }

            EnsureSurfaceForward(desiredUp);
            Quaternion targetRotation = Quaternion.LookRotation(surfaceForward, desiredUp);
            float alignAlpha = 1f - Mathf.Exp(-Mathf.Max(1f, alignSpeed) * dt);
            desiredRotationCache = Quaternion.Slerp(rb.rotation, targetRotation, alignAlpha);

            // Apply to Rigidbody
            rb.MoveRotation(desiredRotationCache);

            // Movement
            Vector3 tangentForward = Vector3.ProjectOnPlane(surfaceForward, desiredUp).normalized;
            if (tangentForward.sqrMagnitude < 1e-6f)
                tangentForward = GetFallbackSurfaceForward(desiredUp);
            Vector3 tangentRight = Vector3.Cross(desiredUp, tangentForward).normalized;

            Vector3 inputDir = (tangentRight * inputX + tangentForward * inputZ);
            if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();
            float speed = moveSpeed * (wantSprint ? sprintMultiplier : 1f);
            Vector3 desiredHorVel = inputDir * speed;

            // Gravity
            float g = gravityAtSurface;
            if (useInverseSquare)
            {
                float r = Mathf.Max(distance, 1e-4f);
                g = gravityAtSurface * (planetRadius * planetRadius) / (r * r);
            }

            // Vertical
            if (isGrounded)
            {
                if (wantJump)
                {
                    verticalVelAlongUp = Mathf.Sqrt(2f * g * Mathf.Max(0.001f, jumpHeight));
                    isGrounded = false;
                }
                else
                {
                    if (verticalVelAlongUp < 0f) verticalVelAlongUp = -groundStickSpeed;
                    desiredHorVel = Vector3.ProjectOnPlane(desiredHorVel, groundNormal);
                }
            }
            else
            {
                verticalVelAlongUp -= g * dt;
                desiredHorVel *= airControl;
            }

            Vector3 finalVel = desiredHorVel + desiredUp * verticalVelAlongUp;
            rb.velocity = finalVel;

            wantJump = false;
        }

        void AlignInitialRotationAndYaw()
        {
            if (planet == null) return;

            Vector3 toCenter = (planet.position - transform.position);
            Vector3 gravityDir = toCenter.normalized;
            Vector3 desiredUp = -gravityDir;

            Vector3 projectedForward = Vector3.ProjectOnPlane(transform.forward, desiredUp);
            if (projectedForward.sqrMagnitude < 1e-6f)
                projectedForward = GetFallbackSurfaceForward(desiredUp);
            else
                projectedForward.Normalize();

            surfaceForward = projectedForward;
            surfaceForwardInitialized = true;
            Quaternion desiredRotation = Quaternion.LookRotation(surfaceForward, desiredUp);
            transform.rotation = desiredRotation;
            rb.rotation = desiredRotation;
            desiredRotationCache = desiredRotation;
        }

        void ResolveSpawnIfInsideGeometry()
        {
            if (planet == null) return;

            Vector3 centerWorld = transform.TransformPoint(col.center);
            Vector3 upWorld = transform.up;
            float half = Mathf.Max(0.01f, col.height * 0.5f - col.radius);
            Vector3 pTop = centerWorld + upWorld * half;
            Vector3 pBot = centerWorld - upWorld * half;

            if (HasBlockingOverlap(pBot, pTop, col.radius))
            {
                Vector3 toCenter = (planet.position - transform.position);
                Vector3 gravityDir = toCenter.normalized;
                Vector3 desiredUp = -gravityDir;

                RaycastHit hitUp;
                if (TryRaycastSurface(transform.position, desiredUp, spawnSearchDistance, out hitUp))
                {
                    Vector3 wantedPos = hitUp.point + desiredUp * (col.height * 0.5f + spawnClearance);
                    transform.position = wantedPos;
                    rb.position = wantedPos;
                    rb.velocity = Vector3.zero;
                    return;
                }

                RaycastHit hitDown;
                if (TryRaycastSurface(transform.position, -desiredUp, spawnSearchDistance, out hitDown))
                {
                    Vector3 wantedPos = hitDown.point + desiredUp * (col.height * 0.5f + spawnClearance);
                    transform.position = wantedPos;
                    rb.position = wantedPos;
                    rb.velocity = Vector3.zero;
                    return;
                }

                for (int i = 0; i < 20; i++)
                {
                    transform.position += desiredUp * (spawnClearance + 0.02f);
                    centerWorld = transform.TransformPoint(col.center);
                    pTop = centerWorld + upWorld * half;
                    pBot = centerWorld - upWorld * half;
                    if (!HasBlockingOverlap(pBot, pTop, col.radius))
                    {
                        rb.position = transform.position;
                        rb.velocity = Vector3.zero;
                        return;
                    }
                }
            }
        }

        void RefreshPlanetListImmediate()
        {
            cachedPlanets = FindObjectsOfType<PlanetGenerator>();
            lastPlanetListRefresh = Time.time;
        }

        void UpdateNearestPlanet()
        {
            if (Time.time - lastPlanetListRefresh > planetListRefreshInterval || cachedPlanets == null || cachedPlanets.Length == 0)
                RefreshPlanetListImmediate();

            if (cachedPlanets == null || cachedPlanets.Length == 0)
            {
                AssignPlanet(null, null);
                return;
            }

            float bestDist = float.MaxValue;
            PlanetGenerator best = null;
            for (int i = 0; i < cachedPlanets.Length; i++)
            {
                var pg = cachedPlanets[i];
                if (pg == null) continue;
                float d = Vector3.Distance(transform.position, pg.transform.position);
                if (d < bestDist) { bestDist = d; best = pg; }
            }

            if (best == null)
            {
                AssignPlanet(null, null);
                return;
            }

            float influence = Mathf.Max(0.0001f, best.radius) * gravityRangeMultiplier;

            if (bestDist <= influence)
            {
                AssignPlanet(best.transform, best);
            }
            else
            {
                AssignPlanet(null, null);
            }
        }

        void AssignPlanet(Transform newPlanetTransform, PlanetGenerator newPlanetGen)
        {
            if (newPlanetTransform == lastAssignedPlanetTransform) return;

            lastAssignedPlanetTransform = newPlanetTransform;
            currentPlanetGen = newPlanetGen;
            planet = newPlanetTransform;

            if (currentPlanetGen != null)
            {
                planetRadius = Mathf.Max(0.0001f, currentPlanetGen.radius);
                AlignInitialRotationAndYaw();
                ResolveSpawnIfInsideGeometry();
            }
            else
            {
                verticalVelAlongUp = 0f;
                lastGroundNormal = Vector3.up;
                surfaceForwardInitialized = false;
            }
        }

        bool IsSelfCollider(Collider candidate)
        {
            if (candidate == null)
                return false;

            if (candidate == col)
                return true;

            Transform candidateTransform = candidate.transform;
            return candidateTransform == transform || candidateTransform.IsChildOf(transform);
        }

        bool TrySphereCastGround(Vector3 origin, float sphereRadius, Vector3 direction, float maxDistance, out RaycastHit bestHit)
        {
            bestHit = new RaycastHit();
            int hitCount = Physics.SphereCastNonAlloc(origin, sphereRadius, direction, groundHitBuffer, maxDistance, groundLayers.value, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = groundHitBuffer[i];
                if (hit.collider == null || IsSelfCollider(hit.collider))
                    continue;

                if (!found || hit.distance < bestDistance)
                {
                    bestHit = hit;
                    bestDistance = hit.distance;
                    found = true;
                }
            }

            return found;
        }

        bool TryRaycastSurface(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit bestHit)
        {
            bestHit = new RaycastHit();
            int hitCount = Physics.RaycastNonAlloc(origin, direction, raycastBuffer, maxDistance, groundLayers.value, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = raycastBuffer[i];
                if (hit.collider == null || IsSelfCollider(hit.collider))
                    continue;

                if (!found || hit.distance < bestDistance)
                {
                    bestHit = hit;
                    bestDistance = hit.distance;
                    found = true;
                }
            }

            return found;
        }

        bool HasBlockingOverlap(Vector3 pointA, Vector3 pointB, float radius)
        {
            int hitCount = Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, overlapBuffer, groundLayers.value, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider overlap = overlapBuffer[i];
                if (overlap == null || IsSelfCollider(overlap))
                    continue;

                return true;
            }

            return false;
        }

        void EnsureSurfaceForward(Vector3 desiredUp)
        {
            Vector3 projectedForward = surfaceForwardInitialized
                ? Vector3.ProjectOnPlane(surfaceForward, desiredUp)
                : Vector3.zero;

            if (projectedForward.sqrMagnitude < 1e-6f)
                projectedForward = Vector3.ProjectOnPlane(transform.forward, desiredUp);

            if (projectedForward.sqrMagnitude < 1e-6f)
                projectedForward = GetFallbackSurfaceForward(desiredUp);
            else
                projectedForward.Normalize();

            surfaceForward = projectedForward;
            surfaceForwardInitialized = true;
        }

        void ApplyYawToSurfaceForward(Vector3 desiredUp, float yawDelta)
        {
            if (Mathf.Abs(yawDelta) < 1e-5f)
                return;

            surfaceForward = Quaternion.AngleAxis(yawDelta, desiredUp) * surfaceForward;
            surfaceForward = Vector3.ProjectOnPlane(surfaceForward, desiredUp);

            if (surfaceForward.sqrMagnitude < 1e-6f)
                surfaceForward = GetFallbackSurfaceForward(desiredUp);
            else
                surfaceForward.Normalize();
        }

        Vector3 GetFallbackSurfaceForward(Vector3 desiredUp)
        {
            Vector3 candidate = Vector3.ProjectOnPlane(transform.forward, desiredUp);
            if (candidate.sqrMagnitude > 1e-6f)
                return candidate.normalized;

            candidate = Vector3.ProjectOnPlane(transform.right, desiredUp);
            if (candidate.sqrMagnitude > 1e-6f)
                return candidate.normalized;

            if (planet != null)
            {
                candidate = Vector3.ProjectOnPlane(planet.up, desiredUp);
                if (candidate.sqrMagnitude > 1e-6f)
                    return candidate.normalized;

                candidate = Vector3.ProjectOnPlane(planet.forward, desiredUp);
                if (candidate.sqrMagnitude > 1e-6f)
                    return candidate.normalized;

                candidate = Vector3.ProjectOnPlane(planet.right, desiredUp);
                if (candidate.sqrMagnitude > 1e-6f)
                    return candidate.normalized;
            }

            candidate = Vector3.ProjectOnPlane(Vector3.up, desiredUp);
            if (candidate.sqrMagnitude > 1e-6f)
                return candidate.normalized;

            candidate = Vector3.ProjectOnPlane(Vector3.forward, desiredUp);
            if (candidate.sqrMagnitude > 1e-6f)
                return candidate.normalized;

            candidate = Vector3.Cross(desiredUp, Vector3.right);
            if (candidate.sqrMagnitude < 1e-6f)
                candidate = Vector3.Cross(desiredUp, Vector3.forward);

            return candidate.normalized;
        }

        void OnDrawGizmosSelected()
        {
            if (col == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, transform.position + transform.up * 1.5f);

            if (planet != null)
            {
                Vector3 toCenter = (planet.position - transform.position);
                Vector3 gravityDir = toCenter.normalized;
                Vector3 desiredUp = -gravityDir;

                Vector3 fwdProj = Vector3.ProjectOnPlane(transform.forward, desiredUp).normalized;
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, transform.position + fwdProj * 1.5f);

                Vector3 tangentRight = Vector3.Cross(desiredUp, fwdProj).normalized;
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(transform.position, transform.position + tangentRight * 1.5f);

                Vector3 centerWorld = transform.TransformPoint(col.center);
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(centerWorld, col.radius * 0.9f);
            }

#if UNITY_EDITOR
            if (cachedPlanets != null)
            {
                Gizmos.color = new Color(0f, 0.5f, 1f, 0.08f);
                foreach (var pg in cachedPlanets)
                {
                    if (pg == null) continue;
                    float inf = Mathf.Max(0.0001f, pg.radius) * gravityRangeMultiplier;
                    Gizmos.DrawSphere(pg.transform.position, inf);
                }
            }
#endif
        }
    }
}
