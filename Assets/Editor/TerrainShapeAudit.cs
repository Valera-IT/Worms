using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// Страж против парящей земли: гоняет TerrainShape по всем типам мира и
/// пачке сидов и считает куски породы, не доходящие до воды (в пещере — ещё
/// и до кровли). Таких быть не должно ни одного: именно они читались на
/// экране как плоские обломки в небе. Заодно кладёт PNG-срезы масок в
/// Temp/TerrainAudit — глазами видно, что ярусы стоят на опорах.
/// Запуск: Worms → Terrain shape audit, или -executeMethod TerrainShapeAudit.Run
public static class TerrainShapeAudit
{
    const int Seeds = 8;

    [MenuItem("Worms/Terrain shape audit")]
    public static void Run()
    {
        int w = Mathf.RoundToInt(DestructibleTerrain.WorldWidth * DestructibleTerrain.PixelsPerUnit);
        int h = Mathf.RoundToInt(DestructibleTerrain.WorldHeight * DestructibleTerrain.PixelsPerUnit);
        string dir = "Temp/TerrainAudit";
        Directory.CreateDirectory(dir);

        int bad = 0;
        foreach (TerrainKind kind in new[]
                 { TerrainKind.Island, TerrainKind.Cave, TerrainKind.Archipelago,
                   TerrainKind.Canyon, TerrainKind.Snow })
        {
            var style = TerrainStyle.For(kind);
            int floats = 0, worst = 0;
            for (int s = 0; s < Seeds; s++)
            {
                int seed = 7919 * (s + 1);
                var solid = new bool[w * h];
                TerrainShape.Fill(solid, w, h, style, seed);
                Audit(solid, w, h, style, kind, ref floats, ref worst);
                if (s < 2) Dump(solid, w, h, $"{dir}/{kind}-{seed}.png");
            }
            bad += floats;
            Debug.Log($"{kind}: парящих кусков за {Seeds} сидов — {floats}, крупнейший {worst} пикс");
        }

        Debug.Log(bad == 0 ? "Terrain shape audit: чисто, парящей земли нет"
                           : $"Terrain shape audit: ПАРЯЩАЯ ЗЕМЛЯ, всего {bad}");
        if (Application.isBatchMode) EditorApplication.Exit(bad == 0 ? 0 : 1);
    }

    static void Audit(bool[] solid, int w, int h, TerrainStyle style, TerrainKind kind,
                      ref int floats, ref int worst)
    {
        float waterN = Mathf.Clamp01(style.WaterLevel / DestructibleTerrain.WorldHeight);
        int rooted = Mathf.RoundToInt(waterN * h + 3f);
        bool ceiling = kind == TerrainKind.Cave;

        var seen = new bool[w * h];
        var stack = new List<int>();
        var part = new List<int>();

        for (int start = 0; start < solid.Length; start++)
        {
            if (!solid[start] || seen[start]) continue;
            stack.Clear(); part.Clear();
            stack.Add(start); seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                part.Add(i);
                int x = i % w, y = i / w;
                if (x > 0 && solid[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Add(i - 1); }
                if (x < w - 1 && solid[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Add(i + 1); }
                if (y > 0 && solid[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Add(i - w); }
                if (y < h - 1 && solid[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Add(i + w); }
            }

            bool grounded = false;
            for (int k = 0; k < part.Count && !grounded; k++)
            {
                int y = part[k] / w;
                grounded = y <= rooted || (ceiling && y >= h - 2);
            }
            if (grounded) continue;
            floats++;
            worst = Mathf.Max(worst, part.Count);
        }
    }

    static void Dump(bool[] solid, int w, int h, string path)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++)
            px[i] = solid[i] ? new Color32(150, 120, 80, 255) : new Color32(120, 170, 210, 255);
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
