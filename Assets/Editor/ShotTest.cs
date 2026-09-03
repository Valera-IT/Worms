using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Снимает пару кадров игры для визуальной проверки.
public static class ShotTest
{
    const string Flag = "ShotTest.Running";
    static string Dir => System.Environment.GetEnvironmentVariable("WORMS_SHOT_DIR") ?? "/tmp";

    static double _start;
    static int _step;

    [MenuItem("Worms/Скриншот")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0;
        SessionState.SetBool(Flag, true);
        App.AutostartMatch = true;   // пропускаем меню, сразу в матч
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false))
        {
            App.AutostartMatch = true;
            EditorApplication.update += Tick;
        }
    }

    static void Capture(string name)
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogError("SHOT: нет камеры"); return; }

        int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prev;

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        string path = Path.Combine(Dir, name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log("SHOT: сохранён " + path + " (" + new FileInfo(path).Length + " байт)");
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;
        var gm = GameManager.I;
        if (gm == null) return;

        if (_step == 0 && t > 2.5)
        {
            _step = 1;
            // Отодвигаем камеру, чтобы в кадр попал весь остров.
            var cam = Camera.main;
            cam.orthographicSize = 26f;
            cam.transform.position = new Vector3(DestructibleTerrain.WorldWidth * 0.5f, 26f, -10f);
            Capture("worms_overview");
        }

        if (_step == 1 && t > 3.5)
        {
            _step = 2;
            var worm = gm.ActiveWorm;
            var cam = Camera.main;
            cam.orthographicSize = 11f;
            cam.transform.position = new Vector3(worm.transform.position.x, worm.transform.position.y + 2f, -10f);
            for (int i = 0; i < 3; i++)
                Combat.Detonate((Vector2)worm.transform.position + new Vector2(3f + i * 3.5f, i - 1f), 3.4f, 5f);
            Capture("worms_closeup");
        }

        if (_step == 2 && t > 5f)
        {
            SessionState.SetBool(Flag, false);
            Debug.Log("SHOT: OK");
            EditorApplication.update -= Tick;
            if (Application.isBatchMode) EditorApplication.Exit(0);
            else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
}
