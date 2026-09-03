using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Проверка пяти типов мира (фаза 6). Ландшафт строится вне PlayMode —
/// поэтому тест дешёвый и гоняет по нескольку сидов на каждый тип.
/// Общее для всех: есть где высадить две команды по четыре червя, ни один
/// червь не оказывается внутри камня, взрыв по-прежнему рвёт дырку.
/// Отдельно — то, ради чего тип затевался: потолок в пещере, разделённые
/// островки в архипелаге, 4–7 пропастей в каньоне, арка в снежных холмах.
/// Запуск: Unity -executeMethod WorldTest.Run
public static class WorldTest
{
    const int Seeds = 4;
    const int NeedSpawns = 8;   // две команды по четыре червя

    static readonly StringBuilder Log = new StringBuilder();
    static int _fails;

    [MenuItem("Worms/World test")]
    public static void Run()
    {
        _fails = 0;
        Log.Clear();

        foreach (TerrainKind kind in new[]
                 { TerrainKind.Island, TerrainKind.Cave, TerrainKind.Archipelago,
                   TerrainKind.Canyon, TerrainKind.Snow })
        {
            for (int s = 0; s < Seeds; s++)
            {
                int seed = 20260901 + s * 7919;
                var go = new GameObject("TestTerrain");
                go.AddComponent<SpriteRenderer>();
                var terrain = go.AddComponent<DestructibleTerrain>();
                terrain.Build(seed, kind);

                Check(terrain, kind, seed);

                Object.DestroyImmediate(go);
            }
        }

        Debug.Log(Log.ToString());
        Debug.Log(_fails == 0 ? "WORLDTEST: OK" : $"WORLDTEST: ПРОВАЛОВ {_fails}");
        if (Application.isBatchMode) EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    static void Fail(TerrainKind kind, int seed, string what)
    {
        _fails++;
        Log.Append($"  ПРОВАЛ [{kind} сид {seed}]: {what}\n");
    }

    static void Check(DestructibleTerrain t, TerrainKind kind, int seed)
    {
        float water = DestructibleTerrain.WaterLevel;
        var spawns = Spawns(t);

        if (spawns.Count < NeedSpawns)
            Fail(kind, seed, $"мест под высадку {spawns.Count}, нужно {NeedSpawns}");

        // Мир обязан быть многоярусным: иначе черви снова садятся в линию.
        int tiers = Tiers(spawns);
        if (tiers < 6) Fail(kind, seed, $"колонок с двумя и более этажами {tiers}, нужно 6");

        // Ни один червь не должен оказаться в камне: точка свободна, над ней
        // свободно на его рост, под ней земля.
        foreach (var p in spawns)
        {
            if (t.IsSolidWorld(p) || t.IsSolidWorld(p + Vector2.up * 1.0f))
            { Fail(kind, seed, $"точка высадки в камне на x={p.x:0.0}"); break; }
            if (!t.IsSolidWorld(p + Vector2.down * 1.0f))
            { Fail(kind, seed, $"под точкой высадки пусто на x={p.x:0.0}"); break; }
            if (p.y < water)
            { Fail(kind, seed, $"точка высадки под водой на x={p.x:0.0}"); break; }
        }

        // Взрыв обязан пробивать дырку в любом мире.
        if (spawns.Count > 0)
        {
            var hit = spawns[spawns.Count / 2] + Vector2.down * 1.5f;
            t.Explode(hit, 2.2f);
            if (t.IsSolidWorld(hit)) Fail(kind, seed, "взрыв не пробил дырку");
        }

        switch (kind)
        {
            case TerrainKind.Cave:
                if (water > 0f) Fail(kind, seed, "в пещере не должно быть воды");
                // Над каждой площадкой должен быть потолок: иначе SurfaceHeightWorld
                // вернул кровлю, а не пол, и червь стоял бы в камне.
                foreach (var p in spawns)
                    if (!Roofed(t, p)) { Fail(kind, seed, $"нет потолка над x={p.x:0.0}"); break; }
                break;

            case TerrainKind.Archipelago:
                int islands = Islands(spawns);
                if (islands < 3) Fail(kind, seed, $"островков {islands}, нужно 3–5");
                break;

            case TerrainKind.Canyon:
                int chasms = Chasms(t, water);
                if (chasms < 4 || chasms > 7) Fail(kind, seed, $"пропастей {chasms}, нужно 4–7");
                break;

            case TerrainKind.Snow:
                int arch = Overhang(t);
                if (arch < 40) Fail(kind, seed, $"арки не видно: перекрытых колонок {arch}");
                break;
        }
    }

    /// Пригодные площадки — тем же способом, что и GameManager: все этажи,
    /// а не только верхний силуэт.
    static List<Vector2> Spawns(DestructibleTerrain t) => t.CollectSpawnPoints(1f);

    /// Сколько разных этажей: колонки, где пригодных площадок больше одной.
    /// Это и есть многоярусность — навесы, каверны, плиты в воздухе.
    static int Tiers(List<Vector2> spawns)
    {
        var byX = new Dictionary<float, int>();
        foreach (var p in spawns) byX[p.x] = byX.TryGetValue(p.x, out var n) ? n + 1 : 1;
        int multi = 0;
        foreach (var kv in byX) if (kv.Value > 1) multi++;
        return multi;
    }

    /// Островки: разрыв в цепочке пригодных колонок больше трёх юнитов.
    static int Islands(List<Vector2> spawns)
    {
        if (spawns.Count == 0) return 0;
        int n = 1;
        for (int i = 1; i < spawns.Count; i++)
            if (spawns[i].x - spawns[i - 1].x > 3f) n++;
        return n;
    }

    static bool Roofed(DestructibleTerrain t, Vector2 p)
    {
        int x = t.WorldToPixelX(p.x);
        for (int y = t.WorldToPixelY(p.y); y < t.H; y++)
            if (t.IsSolidPixel(x, y)) return true;
        return false;
    }

    /// Пропасти каньона: полосы колонок, где твёрдой суши выше воды не осталось.
    /// Края карты не считаем — там просто берег.
    static int Chasms(DestructibleTerrain t, float water)
    {
        int n = 0;
        bool inside = false;
        for (float x = 14f; x < DestructibleTerrain.WorldWidth - 14f; x += 0.5f)
        {
            bool dry = t.SurfaceHeightWorld(x) > water;
            if (!dry && !inside) { n++; inside = true; }
            else if (dry) inside = false;
        }
        return n;
    }

    /// Колонки, где над пустотой снова лежит камень, — это и есть арка.
    static int Overhang(DestructibleTerrain t)
    {
        int n = 0;
        for (int x = 0; x < t.W; x += 2)
        {
            int gap = 0;
            bool seenSurface = false;
            for (int y = t.H - 1; y >= 0; y--)
            {
                bool solid = t.IsSolidPixel(x, y);
                if (!seenSurface)
                {
                    if (solid) seenSurface = true;
                    continue;
                }
                if (!solid) { gap++; continue; }
                if (gap >= 10) { n += 2; break; }   // шаг по x равен двум
                gap = 0;
            }
        }
        return n;
    }
}
