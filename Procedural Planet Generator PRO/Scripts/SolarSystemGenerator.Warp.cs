using UnityEngine;
using System.Collections;

namespace PlanetGeneration
{
    /// <summary>
    /// Телепортация между планетами (warp): StartWarp, WarpCoroutine,
    /// PreparePlanetForArrival, SpawnPlayerOnPlanet, AlignPlayerToSurface.
    /// </summary>
    public partial class SolarSystemGenerator
    {
        public void StartWarp(int planetIndex)
        {
            if (_isWarping || _isSpawning) return;
            if (planetIndex < 0 || planetIndex >= planets.Count) return;
            var target = planets[planetIndex];
            if (target == null || target.root == null || target.generator == null) return;
            if (_currentPlanet != null && _currentPlanet == target) return;
            StartCoroutine(WarpCoroutine(target));
        }

        IEnumerator WarpCoroutine(PlanetEntry target)
        {
            _isWarping = true;

            // Деактивируем текущую планету
            if (_currentPlanet != null && _currentPlanet.generator != null)
            {
                _currentPlanet.generator.DeactivateStreaming();
                _currentPlanet.solarPlanet?.SetActivePlanet(false);
            }

            // Останавливаем физику игрока
            Rigidbody rb = player != null ? player.GetComponent<Rigidbody>() : null;
            bool hadKinematic = false, hadGravity = false;
            if (rb != null)
            {
                hadKinematic = rb.isKinematic;
                hadGravity   = rb.useGravity;
                rb.isKinematic = true;
                rb.useGravity  = false;
                rb.velocity          = Vector3.zero;
                rb.angularVelocity   = Vector3.zero;
            }

            ShowLoadingScreen("Варп...");
            if (seamlessWarp)
                HideLoadingScreen();
            else
                yield return new WaitForSeconds(0.3f);

            var controller = player?.GetComponent<PlanetaryFirstPersonController.PlanetaryFirstPersonController>();
            bool hadControllerEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;

            Vector3 up           = Vector3.up;
            Vector3 planetCenter = target.root.transform.position;

            if (!seamlessWarp)
            {
                float waitHeight = target.generator.radius + target.generator.heightMultiplier + 200f;
                player.position  = planetCenter + up * waitHeight;
                player.rotation  = AlignPlayerToSurface(player.rotation, up);
            }

            yield return StartCoroutine(PreparePlanetForArrival(target,
                seamlessWarp ? seamlessWarpTimeout : 25f));

            // Поиск точки приземления через Raycast
            float rayStart   = target.generator.radius + target.generator.heightMultiplier + 60f;
            Vector3 rayOrigin = planetCenter + up * rayStart;
            Vector3 spawnPos  = planetCenter + up * (target.generator.radius + target.generator.heightMultiplier + 5f);
            RaycastHit hit;
            float timeout = 10f, elapsed = 0f;
            Vector3 spawnUp = up;

            while (elapsed < timeout)
            {
                if (Physics.Raycast(rayOrigin, -up, out hit, rayStart * 2f))
                {
                    spawnUp  = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : up;
                    spawnPos = hit.point + spawnUp * 1.5f;
                    break;
                }
                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            player.position = spawnPos;
            player.rotation = AlignPlayerToSurface(player.rotation, spawnUp);

            if (rb != null)
            {
                rb.position        = spawnPos;
                rb.velocity        = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic     = hadKinematic;
                rb.useGravity      = hadGravity;
            }

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            if (!seamlessWarp)
            {
                HideLoadingScreen();
                player.gameObject.SetActive(true);
            }

            if (controller != null) controller.enabled = hadControllerEnabled;
            _currentPlanet = target;
            SetActivePlanet(target);
            ApplyPlanetVisualProfile(target);
            yield return new WaitForSeconds(0.2f);
            _isWarping = false;
        }

        IEnumerator PreparePlanetForArrival(PlanetEntry target, float timeout)
        {
            if (target.lodController != null)
                target.lodController.ForceTerrainMode(timeout);
            else if (target.generator != null)
                target.generator.ActivateStreaming();

            target.solarPlanet?.SetActivePlanet(true);
            ApplyPlanetVisualProfile(target);

            float elapsed = 0f;
            while (elapsed < timeout)
            {
                if (target.generator != null && target.generator.IsReady) yield break;
                elapsed += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }
        }

        IEnumerator SpawnPlayerOnPlanet(PlanetEntry target)
        {
            _isSpawning = true;
            if (player != null) player.gameObject.SetActive(false);
            ShowLoadingScreen("Генерация планеты...");

            target.solarPlanet?.SetActivePlanet(true);
            target.generator?.ActivateStreaming();
            ApplyPlanetVisualProfile(target);

            float elapsed = 0f;
            float timeout = 60f;
            while (elapsed < timeout)
            {
                if (target.generator != null && target.generator.IsReady) break;
                elapsed += 0.25f;
                yield return new WaitForSeconds(0.25f);
            }

            Vector3 up           = Vector3.up;
            Vector3 planetCenter = target.root.transform.position;
            float   rayStart     = target.generator.radius + target.generator.heightMultiplier + 80f;
            Vector3 rayOrigin    = planetCenter + up * rayStart;
            Vector3 spawnPos     = planetCenter + up * (target.generator.radius + target.generator.heightMultiplier + 5f);
            Vector3 spawnUp      = up;

            elapsed = 0f;
            while (elapsed < 10f)
            {
                if (Physics.Raycast(rayOrigin, -up, out RaycastHit hit, rayStart * 2f))
                {
                    spawnUp  = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : up;
                    spawnPos = hit.point + spawnUp * 1.5f;
                    break;
                }
                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }

            if (player != null)
            {
                player.position = spawnPos;
                player.rotation = AlignPlayerToSurface(player.rotation, spawnUp);
                player.gameObject.SetActive(true);
            }

            _currentPlanet = target;
            SetActivePlanet(target);
            HideLoadingScreen();
            _isSpawning = false;
        }

        Quaternion AlignPlayerToSurface(Quaternion currentRotation, Vector3 up)
        {
            up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(currentRotation * Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
            return Quaternion.LookRotation(forward.normalized, up);
        }
    }
}
