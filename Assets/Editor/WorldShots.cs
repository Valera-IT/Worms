using System.IO;
using UnityEditor;
using UnityEngine;

/// Снимает по кадру на каждый тип мира — визуальная проверка фазы 6.
/// Ландшафт строится вне PlayMode, поэтому снимок дешёвый; нужен только
/// графический контекст (запускать без -nographics).
/// Запуск: Unity -executeMethod WorldShots.Run
public static class WorldShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("WORMS_SHOT_DIR") ?? "/tmp";

    [MenuItem("Worms/Снимки миров")]
    public static void Run()
    {
        int seed = 20260901;

        foreach (TerrainKind kind in new[]
                 { TerrainKind.Island, TerrainKind.Cave, TerrainKind.Archipelago,
                   TerrainKind.Canyon, TerrainKind.Snow })
        {
            var root = new GameObject("ShotWorld");

            var tGo = new GameObject("Terrain");
            tGo.transform.SetParent(root.transform, false);
            tGo.AddComponent<SpriteRenderer>();
            var terrain = tGo.AddComponent<DestructibleTerrain>();
            terrain.Build(seed, kind);
            var style = terrain.Style;

            // Тот же задник, что и в бою: небо-градиент, хребты, облака и
            // украшения на поверхности. Параллакс в редакторе не крутится —
            // слои стоят там, где их поставил Scenery.
            Scenery.Build(root.transform, terrain, seed);

            if (style.HasWater)
            {
                var water = Sprites.Make("Water", Sprites.Square, style.Water, 15, root.transform);
                water.transform.position = new Vector3(DestructibleTerrain.WorldWidth * 0.5f, style.WaterLevel * 0.5f - 20f, 0f);
                water.transform.localScale = new Vector3(DestructibleTerrain.WorldWidth * 3f, style.WaterLevel + 40f, 1f);
            }

            // Отметки там, где встанут черви: не пробные колонки, а ровно та
            // раскладка, которую выдаст матч, — видно и этажи, и разброс.
            foreach (var p in GameManager.SpawnLayout(terrain, 16, seed))
            {
                var m = Sprites.Make("Spawn", Sprites.Circle, new Color(1f, 0.2f, 0.5f), 20, root.transform);
                m.transform.position = p;
            }

            Capture(root, style.Name, kind.ToString().ToLower());
            Object.DestroyImmediate(root);
        }

        Debug.Log("SHOTS: OK");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Capture(GameObject root, string title, string file)
    {
        var camGo = new GameObject("ShotCam");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = DestructibleTerrain.WorldHeight * 0.5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.position = new Vector3(
            DestructibleTerrain.WorldWidth * 0.5f, DestructibleTerrain.WorldHeight * 0.5f, -10f);

        int w = 1152, h = 576;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        string path = Path.Combine(Dir, "world_" + file + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log($"SHOT [{title}]: {path}");

        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
