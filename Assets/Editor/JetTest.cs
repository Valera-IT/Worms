using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Ранец под живым вводом: тяга на удержании, но сам ранец снимается только
/// посадкой или пустым баком, и всё это время кнопка «Огонь» принадлежит ему.
/// Запуск: меню Worms → Тест ранца.
public static class JetTest
{
    const string Flag = "JetTest.Running";

    static double _start;
    static int _step;
    static int _errors;
    static Worm _pilot;
    static bool _wasAloft;

    /// Подменный ввод: важна только кнопка огня, фронты считаются сами.
    class ScriptedInput : IGameInput
    {
        bool _fire;
        int _down = -9, _up = -9;

        /// Фронты считаем по номеру игрового кадра: EditorApplication.update и
        /// Update червя ходят по одному кадру, но порядок между ними не задан.
        public bool Fire
        {
            get => _fire;
            set
            {
                if (value != _fire) { if (value) _down = Time.frameCount; else _up = Time.frameCount; }
                _fire = value;
            }
        }

        public InputScheme Scheme => InputScheme.Keyboard;
        public float Move => 0f;
        public float AimAxis => 0f;
        public bool HasAimTarget => false;
        public Vector2 AimTarget => Vector2.zero;
        public bool HasMark => false;
        public Vector2 Mark => Vector2.zero;
        public bool JumpPressed => false;
        public bool FirePressed => _fire && Time.frameCount - _down <= 1;
        public bool FireHeld => _fire;
        public bool FireReleased => !_fire && Time.frameCount - _up <= 1;
        public int WeaponRequest => -1;
        public int WeaponCycle => 0;
        public bool SelectWormPressed => false;
    }

    static ScriptedInput _input;

    [MenuItem("Worms/Тест ранца")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _errors = 0; _pilot = null; _wasAloft = false;
        SessionState.SetBool(Flag, true);
        Autostart();
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false))
        {
            Autostart();
            EditorApplication.update += Tick;
        }
    }

    static void Autostart()
    {
        App.AutostartMatch = true;
        var cfg = MatchConfig.Hotseat();
        cfg.Terrain = TerrainKind.Cave;
        cfg.TurnTime = 90f;     // ход не должен кончиться посреди полёта
        cfg.FloodRound = 99;
        cfg.Crates = CratePlan.Off;
        App.AutostartConfig = cfg;
    }

    static void Fail(string message) { _errors++; Debug.LogError("JET: " + message); }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        Debug.Log(_errors == 0 ? "JET: OK" : $"JET: ПРОВАЛ ({_errors})");
        if (Application.isBatchMode) EditorApplication.Exit(_errors == 0 ? 0 : 1);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var gm = GameManager.I;
        if (gm == null) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        // Шаг 0: поднимаем активного червя в воздух и отдаём ему подменный ввод.
        if (_step == 0 && t > 1.5)
        {
            _pilot = gm.ActiveWorm;
            if (_pilot == null) { Fail("нет активного червя"); Finish(); return; }

            var terrain = gm.Terrain;
            Vector2 spot = Vector2.zero;
            bool found = false;
            for (float x = 8f; x < DestructibleTerrain.WorldWidth - 8f && !found; x += 2f)
            {
                float surf = terrain.SurfaceHeightWorld(x);
                if (surf <= DestructibleTerrain.WaterLevel + 2f) continue;
                bool clear = true;
                for (float dy = 1f; dy <= 12f && clear; dy += 0.5f)
                    clear = !terrain.IsSolidWorld(new Vector2(x, surf + dy));
                if (!clear) continue;
                spot = new Vector2(x, surf + 12f);
                found = true;
            }
            if (!found) { Fail("не нашлось колонки под полёт"); Finish(); return; }

            _pilot.PlaceAt(spot);
            _input = new ScriptedInput();
            gm.Teams[gm.CurrentTeam].Controller = _input;
            int jet = Weapon.IndexOf(WeaponKind.Jetpack);
            gm.SelectWeapon(jet);
            if (gm.SelectedWeapon != jet) { Fail("ранец не выбрался"); Finish(); return; }
            Debug.Log($"JET step0: {_pilot.WormName} на высоте {spot.y:0.0}, оружие {gm.CurrentWeapon.Name}");
            _step = 1;
        }

        // Шаг 1: нажали «Огонь» — ранец включился и даёт тягу.
        if (_step == 1)
        {
            _input.Fire = true;
            if (t > 2.8)
            {
                if (!_pilot.Jetting) { Fail("ранец не включился по нажатию"); Finish(); return; }
                if (!_pilot.Grounded) _wasAloft = true;
                Debug.Log($"JET step1: тяга, vy={_pilot.Velocity.y:0.0}, оружие {gm.CurrentWeapon.Name}");
                _step = 2;
            }
        }

        // Шаг 2: отпустили кнопку — ранец обязан остаться на черве.
        if (_step == 2 && t > 2.8)
        {
            _input.Fire = false;
            if (t > 3.05)
            {
                if (_pilot.Grounded) { Debug.Log("JET step2: уже приземлился — проверку отпускания пропускаем"); _step = 4; }
                else
                {
                    if (!_pilot.Jetting) Fail("отпускание кнопки сняло ранец");
                    Debug.Log($"JET step2: кнопка отпущена, ранец={_pilot.Jetting}, в воздухе");
                    _step = 3;
                }
            }
        }

        // Шаг 3: второе нажатие — это тяга, а не набор силы и не выстрел.
        if (_step == 3 && t > 3.05)
        {
            _input.Fire = true;
            if (t > 3.45)
            {
                if (_pilot.IsCharging) Fail($"второе нажатие завело набор силы ({_pilot.Charge:0.00})");
                if (gm.State != GameState.Aim) Fail($"второе нажатие ушло в выстрел: state={gm.State}");
                if (!_pilot.Jetting && !_pilot.Grounded) Fail("ранец пропал в воздухе после второго нажатия");
                Debug.Log($"JET step3: второе нажатие — заряд {_pilot.Charge:0.00}, state={gm.State}, ранец={_pilot.Jetting}");
                _input.Fire = false;
                _step = 4;
            }
        }

        // Шаг 4: посадка снимает ранец.
        if (_step == 4 && t > 3.45)
        {
            _input.Fire = false;
            if (!_pilot.Grounded) _wasAloft = true;
            if (_wasAloft && _pilot.Grounded && !_pilot.Jetting)
            {
                Debug.Log($"JET step4: приземлился — ранец снят, оружие {gm.CurrentWeapon.Name}");
                Finish();
                return;
            }
            if (t > 14.0)
            {
                if (_pilot.Jetting) Fail("ранец не снялся ни посадкой, ни пустым баком");
                else Debug.Log("JET step4: ранец снят до посадки (сухой бак)");
                Finish();
            }
        }
    }
}
