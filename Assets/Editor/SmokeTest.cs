using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Прогон игры в PlayMode без участия человека: проверяет, что мир строится,
/// ландшафт разрушается и ход переключается. Запуск: Unity -executeMethod SmokeTest.Run
public static class SmokeTest
{
    const string Flag = "SmokeTest.Running";

    static double _start;
    static int _step;

    [MenuItem("Worms/Smoke test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0;
        _step = 0;
        SessionState.SetBool(Flag, true);
        Autostart();
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    // Вход в PlayMode перезагружает домен и сбрасывает подписки — восстанавливаем их.
    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false))
        {
            Autostart();   // static сбросился при перезагрузке домена
            EditorApplication.update += Tick;
        }
    }

    /// Пропускаем меню и сразу в матч. Тип мира можно задать переменной
    /// окружения WORMS_TERRAIN (island, cave, archipelago, canyon, snow) —
    /// так один и тот же прогон проверяется на любом из пяти миров.
    static void Autostart()
    {
        App.AutostartMatch = true;

        string name = System.Environment.GetEnvironmentVariable("WORMS_TERRAIN");
        if (string.IsNullOrEmpty(name)) return;
        if (!System.Enum.TryParse<TerrainKind>(name, true, out var kind)) return;

        var cfg = MatchConfig.Hotseat();
        cfg.Terrain = kind;
        App.AutostartConfig = cfg;
    }

    static void Finish(int code)
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        var gm = GameManager.I;
        if (gm == null)
        {
            if (t > 8) { Debug.LogError("SMOKE: GameManager не создан"); Finish(1); }
            return;
        }

        if (_step == 0 && t > 2)
        {
            _step = 1;
            var terrain = gm.Terrain;
            Debug.Log($"SMOKE step1: мир «{terrain.Style.Name}» worms={gm.AllWorms().Count} teams={gm.Teams.Count} " +
                      $"terrain={terrain.W}x{terrain.H} paths={terrain.ColliderPathCount} state={gm.State} " +
                      $"active={(gm.ActiveWorm != null ? gm.ActiveWorm.WormName : "null")} wind={gm.Wind:0.00}");

            if (gm.ActiveWorm == null) { Debug.LogError("SMOKE: нет активного червя"); Finish(1); }

            // Команды должны стоять врозь: на разных островках, а не вперемешку.
            for (int i = 0; i < gm.Teams.Count; i++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var w in gm.Teams[i].Worms)
                {
                    lo = Mathf.Min(lo, w.transform.position.x);
                    hi = Mathf.Max(hi, w.transform.position.x);
                }
                Debug.Log($"SMOKE step1: команда «{gm.Teams[i].Name}» занимает x {lo:0.0}..{hi:0.0}");
            }
            foreach (var w in gm.AllWorms())
            {
                if (w.transform.position.y < DestructibleTerrain.WaterLevel)
                    Debug.LogError("SMOKE: червь заспавнен в воде: " + w.WormName);
                if (terrain.IsSolidWorld(w.transform.position))
                    Debug.LogError("SMOKE: червь заспавнен в камне: " + w.WormName);
            }
        }

        if (_step == 1 && t > 3.5)
        {
            _step = 2;
            var worm = gm.ActiveWorm;
            int before = gm.Terrain.ColliderPathCount;
            Vector2 target = (Vector2)worm.transform.position + new Vector2(4f, 0f);
            Combat.Detonate(target, 3.5f, 40f);
            Debug.Log($"SMOKE step2: взрыв, paths {before} -> {gm.Terrain.ColliderPathCount}, " +
                      $"solid_at_center={gm.Terrain.IsSolidWorld(target)}");
        }

        if (_step == 2 && t > 5)
        {
            _step = 3;
            var worm = gm.ActiveWorm;
            var w = Weapon.All[0];
            Projectile.Spawn(w, (Vector2)worm.transform.position + worm.AimDirection * 1.2f,
                             worm.AimDirection * 22f, worm);
            gm.OnWeaponFired();
            Debug.Log($"SMOKE step3: выстрел из базуки, state={gm.State}");
        }

        if (_step == 3 && t > 14)
        {
            _step = 4;
            Debug.Log($"SMOKE step4: state={gm.State} team={gm.CurrentTeam} " +
                      $"active={(gm.ActiveWorm != null ? gm.ActiveWorm.WormName : "null")} " +
                      $"hp0={gm.Teams[0].TotalHealth:0} hp1={gm.Teams[1].TotalHealth:0}");
            if (gm.State != GameState.Aim && gm.State != GameState.GameOver)
                Debug.LogError("SMOKE: ход не вернулся в Aim, застряли в " + gm.State);
            Debug.Log("SMOKE: OK");
            Finish(0);
        }
    }
}
