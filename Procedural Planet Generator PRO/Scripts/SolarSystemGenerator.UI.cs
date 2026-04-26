using UnityEngine;
using UnityEngine.UI;

namespace PlanetGeneration
{
    /// <summary>
    /// UI-часть SolarSystemGenerator: warp HUD (OnGUI), Loading Screen, CloseWarpHUD.
    /// </summary>
    public partial class SolarSystemGenerator
    {
        void CloseWarpHUD()
        {
            _showWarpHUD      = false;
            GameUI.IsOpen     = false;
            Cursor.lockState  = CursorLockMode.Locked;
            Cursor.visible    = false;
        }

        void OnGUI()
        {
            // ── Подсказка "нажми Tab" ────────────────────────────────────────
            if (!_showWarpHUD && !_isWarping && !_isSpawning && player != null)
            {
                bool inSpace = true;
                foreach (var p in planets)
                {
                    if (p?.root == null || p.generator == null) continue;
                    float d = Vector3.Distance(player.position, p.root.transform.position);
                    if (d < p.generator.radius * 2.5f) { inSpace = false; break; }
                }

                if (inSpace)
                {
                    var hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                    hint.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                    GUI.Label(new Rect(Screen.width / 2f - 150f, Screen.height - 40f, 300f, 30f),
                              "[ Tab ] — Выбор планеты", hint);
                }
                return;
            }

            if (!_showWarpHUD) return;

            // ── Инициализация стилей ─────────────────────────────────────────
            if (!_stylesReady)
            {
                _hudBtnStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 18, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(18, 18, 10, 10),
                    margin  = new RectOffset(0, 0, 6, 6)
                };
                _hudBtnStyle.normal.textColor  = Color.white;
                _hudBtnStyle.hover.textColor   = Color.yellow;

                _hudLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22, fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _hudLabelStyle.normal.textColor = Color.white;
                _stylesReady = true;
            }

            // ── Затемнение экрана ────────────────────────────────────────────
            GUI.color = new Color(0f, 0f, 0f, 0.60f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // ── Панель ───────────────────────────────────────────────────────
            float panelW  = 420f;
            float panelH  = 80f + planets.Count * 66f;
            float panelX  = (Screen.width  - panelW) * 0.5f;
            float panelY  = (Screen.height - panelH) * 0.5f;

            GUI.Box(new Rect(panelX - 16, panelY - 16, panelW + 32, panelH + 32), "");
            GUI.Label(new Rect(panelX, panelY, panelW, 50f), "⬥ ВАРП-НАВИГАТОР ⬥", _hudLabelStyle);

            float btnY = panelY + 58f;
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i];
                if (p == null) continue;

                bool isCurrent = (_currentPlanet == p);
                string biome   = p.hasOcean ? "океан" : "суша";
                string trees   = p.hasTrees ? "🌲" : "";
                string label   = $"[{i + 1}]  {p.planetName}   ({biome}{trees})";
                if (isCurrent) label += "  ← здесь";

                GUI.enabled = !isCurrent && !_isWarping;
                if (GUI.Button(new Rect(panelX, btnY, panelW, 54f), label, _hudBtnStyle))
                {
                    CloseWarpHUD();
                    StartWarp(i);
                }
                GUI.enabled = true;
                btnY += 62f;
            }

            // ── Закрыть ──────────────────────────────────────────────────────
            if (GUI.Button(new Rect(panelX + panelW - 90f, panelY - 14f, 80f, 28f), "✕ Esc"))
                CloseWarpHUD();
        }

        void ShowLoadingScreen(string message = "Загрузка...")
        {
            if (travelCanvas == null) return;
            travelCanvas.gameObject.SetActive(true);

            var texts = travelCanvas.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t.name.ToLower().Contains("loading") || texts.Length == 1)
                { _loadingText = t; t.text = message; break; }
            }
        }

        void HideLoadingScreen()
        {
            if (travelCanvas != null) travelCanvas.gameObject.SetActive(false);
            _loadingText = null;
        }
    }
}
