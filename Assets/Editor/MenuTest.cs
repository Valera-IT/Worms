using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Гоняет стейт-машину App без человека: Меню → Матч → Пауза → Матч → Итоги → Меню.
/// Проверяет, что мир поднимается и сносится в нужные моменты и что пауза
/// останавливает время. Запуск: Unity -executeMethod MenuTest.Run
public static class MenuTest
{
    const string Flag = "MenuTest.Running";

    static double _start;
    static double _mark;    // время входа в текущий шаг
    static int _step;
    static bool _failed;

    static void Advance(int next) { _step = next; _mark = EditorApplication.timeSinceStartup - _start; }

    [MenuItem("Worms/Тест меню")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _failed = false;
        SessionState.SetBool(Flag, true);
        App.AutostartMatch = false;   // именно через меню, не мимо него
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false))
        {
            App.AutostartMatch = false;
            EditorApplication.update += Tick;
        }
    }

    static void Fail(string msg) { Debug.LogError("MENU: " + msg); _failed = true; }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void KillTeam(int t)
    {
        var gm = GameManager.I;
        foreach (var w in gm.Teams[t].Worms)
            if (w != null && !w.IsDead) w.TakeDamage(999f);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        var app = App.I;
        if (app == null)
        {
            if (t > 8) { Fail("App не создан"); Finish(); }
            return;
        }

        if (_step == 0 && t > 1.5)
        {
            Advance(1);
            Debug.Log($"MENU step1: phase={app.Phase} gm={(GameManager.I != null)}");
            if (app.Phase != AppPhase.Menu) Fail("старт не в меню");
            if (GameManager.I != null) Fail("в меню не должно быть GameManager");

            // Прогоняем все шесть экранов — здесь ловятся исключения при их сборке
            // (степперы, ползунки, QualitySettings.names, Screen.resolutions).
            var menu = Object.FindAnyObjectByType<MenuUI>();
            if (menu == null) { Fail("MenuUI не создан"); Finish(); return; }
            try
            {
                menu.ShowModeSelect();
                menu.ShowMatchSetup();
                menu.ShowSettings();
                menu.ShowAbout();
                menu.ShowPause();
                menu.ShowResults(null);
                menu.ShowMain();
            }
            catch (System.Exception e) { Fail("экран меню упал при сборке: " + e); }
            Debug.Log("MENU step1: шесть экранов собраны без исключений");
        }

        if (_step == 1 && t - _mark > 1.0)
        {
            var cfg = MatchConfig.Hotseat();
            cfg.TurnTime = 2f;           // чтобы матч добежал до конца быстро
            cfg.WormsPerTeam = 2;
            app.StartMatch(cfg);
            Advance(11);
        }

        if (_step == 11 && t - _mark > 1.5)
        {
            Advance(2);
            var gm = GameManager.I;
            Debug.Log($"MENU step2: phase={app.Phase} worms={(gm != null ? gm.AllWorms().Count : -1)}");
            if (app.Phase != AppPhase.Match) Fail("StartMatch не перевёл в Match");
            if (gm == null || gm.AllWorms().Count != 4) Fail("мир матча не собрался по конфигу");

            app.TogglePause();
            Debug.Log($"MENU step2: пауза paused={app.Paused} timeScale={Time.timeScale}");
            if (!app.Paused || Time.timeScale != 0f) Fail("пауза не остановила время");
        }

        if (_step == 2 && t - _mark > 0.6)
        {
            Advance(3);
            app.TogglePause();
            Debug.Log($"MENU step3: снятие паузы paused={app.Paused} timeScale={Time.timeScale}");
            if (app.Paused || Time.timeScale != 1f) Fail("время не возобновилось");
            KillTeam(1);
            Debug.Log("MENU step3: команда 1 уничтожена, ждём экран итогов");
        }

        if (_step == 3 && app.Phase == AppPhase.Results)
        {
            Advance(4);
            var r = app.LastResults;
            Debug.Log($"MENU step4: итоги, winner={(r != null ? r.WinnerTeam : -99)} title={r?.Title}");
            if (r == null || r.WinnerTeam != 0) Fail("победителем должна быть команда 0");
            if (GameManager.I == null) Fail("застывший мир должен ещё стоять фоном под итогами");

            app.ReturnToMenu();
        }

        if (_step == 3 && t - _mark > 16)
        {
            Fail("матч не дошёл до экрана итогов за отведённое время");
            Finish();
        }

        if (_step == 4 && t - _mark > 0.5)
        {
            Advance(5);
            Debug.Log($"MENU step5: назад в меню phase={app.Phase} gm={(GameManager.I != null)}");
            if (app.Phase != AppPhase.Menu) Fail("ReturnToMenu не вернул в меню");
            if (GameManager.I != null) Fail("мир не снесён при выходе в меню");
            app.StartMatch(MatchConfig.Hotseat());   // повторный вход — teardown не должен ломать пересборку
        }

        if (_step == 5 && t - _mark > 1.5)
        {
            Debug.Log($"MENU step6: повторный матч phase={app.Phase} gm={(GameManager.I != null)}");
            if (app.Phase != AppPhase.Match || GameManager.I == null) Fail("повторный StartMatch не поднял мир");
            Debug.Log(_failed ? "MENU: ПРОВАЛ" : "MENU: OK");
            Finish();
        }
    }
}
