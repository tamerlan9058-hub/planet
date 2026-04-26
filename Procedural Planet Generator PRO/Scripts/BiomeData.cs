using UnityEngine;

namespace PlanetGeneration
{
    /// <summary>
    /// Данные биомов: BiomePreset, встроенная таблица Biomes[], GameUI helper.
    /// Используется SolarSystemGenerator и PlanetGenerator.
    /// </summary>

    public struct BiomePreset
    {
        public string name;
        public Color  deep, shallow, sand, grass, forest, rock, snow, atmo;

        public BiomePreset(string name,
            Color deep, Color shallow, Color sand,
            Color grass, Color forest, Color rock,
            Color snow,  Color atmo)
        {
            this.name    = name;
            this.deep    = deep;    this.shallow = shallow;
            this.sand    = sand;    this.grass   = grass;
            this.forest  = forest;  this.rock    = rock;
            this.snow    = snow;    this.atmo    = atmo;
        }
    }

    /// <summary>
    /// Таблица предустановленных биомов, используемая SolarSystemGenerator.
    /// </summary>
    public static class BiomeTable
    {
        public static readonly BiomePreset[] All = new BiomePreset[]
        {
            // deep,             shallow,           sand,              grass(primary),    forest(dark),      rock,              snow,              atmo
            new BiomePreset("Terran",
                new Color(0.05f,0.15f,0.45f), new Color(0.15f,0.42f,0.70f),
                new Color(0.78f,0.72f,0.52f), new Color(0.15f,0.58f,0.08f),
                new Color(0.03f,0.22f,0.03f), new Color(0.38f,0.35f,0.30f),
                new Color(0.95f,0.95f,1.00f), new Color(0.4f,0.6f,1.0f,0.5f)),

            new BiomePreset("Desert",
                new Color(0.35f,0.22f,0.05f), new Color(0.60f,0.42f,0.12f),
                new Color(0.90f,0.72f,0.35f), new Color(0.88f,0.52f,0.08f),
                new Color(0.55f,0.25f,0.03f), new Color(0.52f,0.38f,0.22f),
                new Color(0.92f,0.85f,0.68f), new Color(1.0f,0.75f,0.4f,0.45f)),

            new BiomePreset("Volcanic",
                new Color(0.45f,0.06f,0.02f), new Color(0.72f,0.18f,0.02f),
                new Color(0.38f,0.18f,0.10f), new Color(0.75f,0.04f,0.01f),
                new Color(0.12f,0.04f,0.02f), new Color(0.28f,0.20f,0.16f),
                new Color(0.90f,0.84f,0.80f), new Color(1.0f,0.35f,0.1f,0.55f)),

            new BiomePreset("Arctic",
                new Color(0.08f,0.22f,0.55f), new Color(0.28f,0.58f,0.88f),
                new Color(0.72f,0.82f,0.90f), new Color(0.48f,0.72f,0.95f),
                new Color(0.18f,0.40f,0.72f), new Color(0.42f,0.48f,0.55f),
                new Color(0.97f,0.97f,1.00f), new Color(0.6f,0.85f,1.0f,0.4f)),

            new BiomePreset("Alien",
                new Color(0.28f,0.04f,0.48f), new Color(0.52f,0.12f,0.72f),
                new Color(0.65f,0.52f,0.15f), new Color(0.68f,0.04f,0.78f),
                new Color(0.22f,0.02f,0.32f), new Color(0.30f,0.22f,0.38f),
                new Color(0.92f,0.78f,1.00f), new Color(0.8f,0.3f,1.0f,0.5f)),

            new BiomePreset("Jungle",
                new Color(0.02f,0.28f,0.18f), new Color(0.08f,0.55f,0.38f),
                new Color(0.62f,0.55f,0.25f), new Color(0.05f,0.68f,0.05f),
                new Color(0.01f,0.25f,0.01f), new Color(0.28f,0.32f,0.22f),
                new Color(0.88f,0.96f,0.82f), new Color(0.3f,0.9f,0.5f,0.45f)),

            new BiomePreset("Ocean",
                new Color(0.01f,0.08f,0.45f), new Color(0.05f,0.38f,0.80f),
                new Color(0.70f,0.65f,0.48f), new Color(0.08f,0.62f,0.68f),
                new Color(0.02f,0.32f,0.42f), new Color(0.32f,0.38f,0.42f),
                new Color(0.92f,0.96f,1.00f), new Color(0.2f,0.5f,1.0f,0.6f)),
        };
    }

    /// <summary>
    /// Глобальный флаг открытости любого UI — используется, чтобы блокировать
    /// ввод персонажа пока открыто меню варпа или другой HUD.
    /// </summary>
    public static class GameUI
    {
        public static bool IsOpen = false;
    }
}
