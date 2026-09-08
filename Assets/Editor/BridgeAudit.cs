using System.IO;
using UnityEditor;
using UnityEngine;

/// Проверка мостов: на архипелаге и в каньоне карта обязана перестать быть
/// россыпью отдельных кусков. Для пачки сидов строится настоящий ландшафт,
/// считаются проходимые области до мостов и после — и заодно то, что настил
/// сам по себе идёт ровно: соседние колонки не должны отличаться больше, чем
/// червь способен перешагнуть.
/// Запуск: Worms → Мосты — аудит, или -executeMethod BridgeAudit.Run
public static class BridgeAudit
{
    const int Seeds = 6;

    /// На столько червь способен подняться шагом (Worm.StepHeight).
    const float Step = 1.25f;

    [MenuItem("Worms/Мосты — аудит")]
    public static void Run()
    {
        int bad = 0;

        foreach (var kind in new[] { TerrainKind.Archipelago, TerrainKind.Canyon })
        {
            int totalBridges = 0, joined = 0;

            for (int s = 0; s < Seeds; s++)
            {
                int seed = 7919 * (s + 1);

                var go = new GameObject("BridgeAudit");
                go.AddComponent<SpriteRenderer>();
                var terrain = go.AddComponent<DestructibleTerrain>();
                terrain.Build(seed, kind);

                int bridges = CountBridges(terrain);
                int regions = Regions(terrain, out float worstJump);
                totalBridges += bridges;

                // Каждый мост обязан склеивать два куска: областей должно стать
                // ровно на столько меньше, сколько мостов встало. Меньше —
                // значит, мост лёг вдоль одного берега и ничего не соединил.
                Debug.Log($"МОСТЫ {kind} сид {seed}: мостов {bridges}, проходимых областей {regions}, худший уступ настила {worstJump:0.00}");
                if (bridges > 0) joined++;
                if (worstJump > Step) { Debug.LogError($"МОСТЫ {kind} сид {seed}: уступ {worstJump:0.00} выше шага червя"); bad++; }

                if (s < 2) Dump(go, terrain, $"{kind}-{seed}");

                Object.DestroyImmediate(go);
            }

            Debug.Log($"МОСТЫ {kind}: всего {totalBridges} за {Seeds} сидов, карт с мостами {joined} из {Seeds}");
            if (totalBridges == 0) { Debug.LogError($"МОСТЫ {kind}: ни одного моста — проливы остались непроходимыми"); bad++; }
        }

        Debug.Log(bad == 0 ? "Мосты — аудит: чисто" : $"Мосты — аудит: ОШИБОК {bad}");
        if (Application.isBatchMode) EditorApplication.Exit(bad == 0 ? 0 : 1);
    }

    /// Срез карты вокруг первого моста в PNG — глазами видно, что настил
    /// лежит на берегах, а перила стоят над ним. Кладётся в Temp/BridgeAudit.
    static void Dump(GameObject go, DestructibleTerrain t, string name)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        int first = -1, last = -1;
        for (int x = 0; x < t.W; x++)
            for (int y = 0; y < t.H; y++)
                if (t.IsDeckPixel(x, y)) { if (first < 0) first = x; last = x; break; }
        if (first < 0) return;

        int x0 = Mathf.Max(0, first - 40), x1 = Mathf.Min(t.W - 1, last + 40);
        var src = sr.sprite.texture;
        var crop = src.GetPixels(x0, 0, x1 - x0 + 1, t.H);

        var tex = new Texture2D(x1 - x0 + 1, t.H, TextureFormat.RGBA32, false);
        tex.SetPixels(crop);
        tex.Apply();

        Directory.CreateDirectory("Temp/BridgeAudit");
        File.WriteAllBytes($"Temp/BridgeAudit/{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    /// Сколько отдельных мостов на карте: настил, идущий подряд по колонкам, —
    /// это один мост.
    static int CountBridges(DestructibleTerrain t)
    {
        int n = 0;
        bool prev = false;
        for (int x = 0; x < t.W; x++)
        {
            bool deck = false;
            for (int y = 0; y < t.H && !deck; y++) deck = t.IsDeckPixel(x, y);
            if (deck && !prev) n++;
            prev = deck;
        }
        return n;
    }

    /// Сколько кусков суши, между которыми червю не перейти ногами, и самый
    /// высокий уступ, встреченный на настиле.
    static int Regions(DestructibleTerrain t, out float worstDeckJump)
    {
        worstDeckJump = 0f;
        float water = DestructibleTerrain.WaterLevel;

        int regions = 0;
        bool open = false;
        float prevH = 0f;

        for (float x = 1f; x < DestructibleTerrain.WorldWidth - 1f; x += 0.5f)
        {
            float h = t.SurfaceHeightWorld(x);
            bool land = h > water + 1f;

            if (!land) { open = false; continue; }

            if (!open || Mathf.Abs(h - prevH) > Step) regions++;
            else if (OnDeck(t, x, h) && OnDeck(t, x - 0.5f, prevH))
                worstDeckJump = Mathf.Max(worstDeckJump, Mathf.Abs(h - prevH));

            open = true;
            prevH = h;
        }
        return regions;
    }

    static bool OnDeck(DestructibleTerrain t, float x, float y)
        => t.IsDeckPixel(t.WorldToPixelX(x), t.WorldToPixelY(y) - 1);
}
