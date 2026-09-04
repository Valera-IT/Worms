using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// Проверяет слой ввода без человека: подсовывает виртуальную клавиатуру и подменный
/// IGameInput и смотрит, доходит ли это до червя. Запуск: Unity -executeMethod InputTest.Run
public static class InputTest
{
    const string Flag = "InputTest.Running";

    static double _start;
    static int _step;
    static Keyboard _kb;
    static float _aimBefore;
    static bool _failed;

    /// Подменный ввод: ровно то, что должен уметь бот фазы 8.
    class ScriptedInput : IGameInput
    {
        public InputScheme Scheme => InputScheme.Keyboard;
        public float Move { get; set; }
        public float AimAxis { get; set; }
        public bool HasAimTarget { get; set; }
        public Vector2 AimTarget { get; set; }
        public bool JumpPressed => false;
        public bool FirePressed => false;
        public bool FireHeld => false;
        public bool FireReleased => false;
        public int WeaponRequest => -1;
        public int WeaponCycle => 0;
        public bool SelectWormPressed { get; set; }
    }

    static ScriptedInput _scripted;

    [MenuItem("Worms/Тест ввода")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _failed = false;
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

    static void Fail(string msg)
    {
        Debug.LogError("INPUT: " + msg);
        _failed = true;
    }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Hold(params Key[] keys)
    {
        if (_kb == null) return;
        InputSystem.QueueStateEvent(_kb, new KeyboardState(keys));
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        var gm = GameManager.I;
        if (gm == null)
        {
            if (t > 8) { Fail("GameManager не создан"); Finish(); }
            return;
        }

        var worm = gm.ActiveWorm;

        if (_step == 0 && t > 2)
        {
            _step = 1;
            if (GameInput.I == null) { Fail("роутер ввода не поднят"); Finish(); return; }
            if (worm == null) { Fail("нет активного червя"); Finish(); return; }
            // В batchmode нет окна Game и нет фокуса — иначе ввод отбрасывается на подходе.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _kb = InputSystem.GetDevice<Keyboard>() ?? InputSystem.AddDevice<Keyboard>();
            Debug.Log($"INPUT step1: схема={GameInput.I.Scheme} клавиатура={(_kb != null)} червь={worm.WormName}");

            // Подменный ввод вместо живого игрока — так же придёт бот.
            _scripted = new ScriptedInput { Move = -1f };
            gm.Teams[gm.CurrentTeam].Controller = _scripted;
        }

        if (_step == 1 && t > 3.0)
        {
            _step = 2;
            if (worm == null) { Fail("червь пропал"); Finish(); return; }
            Debug.Log($"INPUT step2: подменный ввод, facing={worm.Facing}");
            if (worm.Facing != -1) Fail("подменный IGameInput не развернул червя влево");
            gm.Teams[gm.CurrentTeam].Controller = null;
        }

        // Дальше — живая клавиатура через KeyboardInput.
        if (_step == 2 && t > 3.2)
        {
            Hold(Key.D);
            if (t > 3.9)
            {
                _step = 3;
                Debug.Log($"INPUT step3: D зажата, facing={worm.Facing}");
                if (worm.Facing != 1) Fail("клавиша D не развернула червя вправо");
                _aimBefore = worm.AimAngle;
            }
        }

        if (_step == 3 && t > 4.1)
        {
            Hold(Key.W);
            if (t > 4.8)
            {
                _step = 4;
                Debug.Log($"INPUT step4: W зажата, угол {_aimBefore:0.0} -> {worm.AimAngle:0.0}");
                if (worm.AimAngle <= _aimBefore + 1f) Fail("клавиша W не подняла прицел");
            }
        }

        if (_step == 4 && t > 5.0)
        {
            Hold(Key.Space);
            if (t > 5.5)
            {
                _step = 5;
                Debug.Log($"INPUT step5: Space зажат, заряд={worm.Charge:0.00} charging={worm.IsCharging}");
                if (!worm.IsCharging) Fail("Space не начал набор силы");
            }
        }

        if (_step == 5 && t > 5.7)
        {
            Hold(); // отпустили всё
            if (t > 6.4)
            {
                _step = 6;
                Debug.Log($"INPUT step6: отпустили Space, state={gm.State}");
                if (gm.State == GameState.Aim) Fail("выстрел по отпусканию Space не прошёл");
                Debug.Log(_failed ? "INPUT: ПРОВАЛ" : "INPUT: OK");
                Finish();
            }
        }
    }
}
