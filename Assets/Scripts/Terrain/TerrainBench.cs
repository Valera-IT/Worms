using System.Text;
using UnityEngine;

/// Замер стоимости `Explode` — тот самый, ради которого затевалась фаза 5.
/// Живёт в рантайме, поэтому его можно позвать и в редакторе, и в сборке
/// на телефоне: `Debug.Log(TerrainBench.Run())` и смотреть логи устройства.
public static class TerrainBench
{
    /// Радиусы взрывов (в юнитах) примерно как у базуки, гранаты и кластера.
    static readonly float[] Radii = { 1.6f, 2.6f, 3.5f };

    /// Гоняет взрывы по свежему ландшафту и возвращает отчёт.
    /// terrain == null — построит свой временный и снесёт после замера.
    public static string Run(int shots = 60, int seed = 12345, DestructibleTerrain terrain = null,
                             TerrainKind kind = TerrainKind.Island)
    {
        GameObject temp = null;
        if (terrain == null)
        {
            temp = new GameObject("BenchTerrain");
            temp.AddComponent<SpriteRenderer>();
            terrain = temp.AddComponent<DestructibleTerrain>();
            terrain.Build(seed, kind);
        }

        bool prevProfile = DestructibleTerrain.Profile;
        DestructibleTerrain.Profile = true;

        var rnd = new System.Random(seed);
        float total = 0f, worst = 0f, mask = 0f, paint = 0f, tex = 0f, coll = 0f;
        int chunks = 0, dirty = 0, counted = 0;

        for (int i = 0; i < shots; i++)
        {
            // Бьём по земле: ищем колонку с поверхностью и целимся чуть ниже неё.
            float wx = 8f + (float)rnd.NextDouble() * (DestructibleTerrain.WorldWidth - 16f);
            float h = terrain.SurfaceHeightWorld(wx);
            if (h < DestructibleTerrain.WaterLevel) continue;
            float wy = Mathf.Max(1f, h - (float)rnd.NextDouble() * 4f);
            float r = Radii[i % Radii.Length];

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            terrain.Explode(new Vector2(wx, wy), r);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

            float ms = (t1 - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            total += ms;
            if (ms > worst) worst = ms;
            mask += DestructibleTerrain.LastMaskMs;
            paint += DestructibleTerrain.LastPaintMs;
            tex += DestructibleTerrain.LastTexMs;
            coll += DestructibleTerrain.LastColliderMs;
            chunks += DestructibleTerrain.LastChunksRebuilt;
            dirty += DestructibleTerrain.LastDirtyPixels;
            counted++;
        }

        DestructibleTerrain.Profile = prevProfile;

        var sb = new StringBuilder();
        if (counted == 0)
        {
            sb.Append("BENCH: ни один взрыв не попал в землю");
        }
        else
        {
            float n = counted;
            sb.Append($"BENCH Explode [{terrain.Style.Name}]: {counted} взрывов, карта {terrain.W}x{terrain.H} ({terrain.W * terrain.H / 1000} тыс. пикселей)\n");
            sb.Append($"  среднее {total / n:0.00} мс, худшее {worst:0.00} мс\n");
            sb.Append($"  маска {mask / n:0.00} · покраска {paint / n:0.00} · текстура {tex / n:0.00} · коллайдер {coll / n:0.00} мс\n");
            sb.Append($"  грязных пикселей {dirty / counted} из {terrain.W * terrain.H} ({100f * dirty / counted / (terrain.W * terrain.H):0.0}%), чанков за взрыв {chunks / n:0.0}\n");
            sb.Append($"  контуров в коллайдере: {terrain.ColliderPathCount}");
        }

        if (temp != null)
        {
            if (Application.isPlaying) Object.Destroy(temp); else Object.DestroyImmediate(temp);
        }
        return sb.ToString();
    }
}
