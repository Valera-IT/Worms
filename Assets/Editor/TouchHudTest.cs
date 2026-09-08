using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

/// Проверяет видимое управление касаниями: слой `TouchPad` построен, кнопки
/// объявили свои прямоугольники, а нажатия действительно двигают червя —
/// прыжок, наклон прицела, набор силы и выстрел. Заодно снимает панель HUD в
/// PNG: пиксели — единственный способ увидеть, что органы управления не уехали
/// за экран.
///
/// Запуск: меню **Worms → Тест управления касаниями** или
/// `Unity -executeMethod TouchHudTest.Run`.
public static class TouchHudTest
{
    const string Flag = "TouchHudTest.Running";
    static string Dir => System.Environment.GetEnvironmentVariable("WORMS_SHOT_DIR") ?? "/tmp";

    static double _start, _mark;
    static int _step;
    static bool _failed;
    static float _y0, _aim0;
    static int _shotFrame;

    /// Наводка ракеты: где стоял крестик до жеста и куда мы ткнули пальцем.
    static Vector2 _mark0, _tapWorld;

    static void Advance(int next) { _step = next; _mark = EditorApplication.timeSinceStartup - _start; }
    static void Fail(string msg) { Debug.LogError("TOUCH: " + msg); _failed = true; }

    [MenuItem("Worms/Тест управления касаниями")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _failed = false;
        SessionState.SetBool(Flag, true);
        App.AutostartMatch = true;
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

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        GameInput.Forced = null;
        if (!_failed) Debug.Log("TOUCH: OK");
        if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static Worm Active => GameManager.I != null ? GameManager.I.ActiveWorm : null;

    /// Настоящее касание виртуального тачскрина: кнопки HUD ходят мимо жестов,
    /// а стик и тычок по карте живут именно в них — проверить их подделкой
    /// нажатий на кнопки нельзя.
    static void Finger(int id, Vector2 pos, UnityEngine.InputSystem.TouchPhase phase)
    {
        var ts = Touchscreen.current;
        if (ts == null) return;
        InputSystem.QueueStateEvent(ts, new TouchState
        {
            touchId = id,
            position = pos,
            phase = phase
        });
        InputSystem.Update();
    }

    /// Точка на экране в долях безопасной области.
    static Vector2 SafePoint(float fx, float fy)
    {
        var safe = Screen.safeArea;
        return new Vector2(safe.xMin + safe.width * fx, safe.yMin + safe.height * fy);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        if (_step == 0 && t > 2.0)
        {
            Advance(1);

            // В редакторе тачскрина нет — заводим виртуальный, иначе схема Touch
            // недоступна и роутер её не отдаст.
            if (Touchscreen.current == null) InputSystem.AddDevice<Touchscreen>();
            GameInput.Forced = InputScheme.Touch;

            if (GameManager.I == null) { Fail("матч не поднялся"); Finish(); return; }
        }

        if (_step == 1 && t - _mark > 1.0)
        {
            Advance(2);
            Debug.Log($"TOUCH step1: схема={GameInput.I.Scheme} кнопок={TouchInput.ReservedCount}");
            if (GameInput.I.Scheme != InputScheme.Touch) Fail("схема не переключилась на касания");
            if (TouchInput.ReservedCount < 4) Fail("слой управления не построил кнопки");

            var w = Active;
            if (w == null) { Fail("нет активного червя"); Finish(); return; }
            _y0 = w.transform.position.y;
            TouchInput.UiJump();
        }

        if (_step == 2 && t - _mark > 0.5)
        {
            Advance(3);
            var w = Active;
            float dy = w.transform.position.y - _y0;
            Debug.Log($"TOUCH step2: прыжок dy={dy:0.00}");
            if (dy < 0.3f) Fail("кнопка прыжка не подняла червя");

            _aim0 = w.AimAngle;
            TouchInput.UiAim(1f);
        }

        if (_step == 3 && t - _mark > 0.6)
        {
            Advance(30);
            var w = Active;
            float d = w.AimAngle - _aim0;
            TouchInput.UiAim(0f);
            Debug.Log($"TOUCH step3: угол {_aim0:0.0} → {w.AimAngle:0.0} (Δ{d:0.0})");
            if (d < 5f) Fail("кнопка ▲ не подняла прицел");

            // Дальше — наводка ракеты, и туда же уходят обе оси стика.
            GameManager.I.SelectWeapon(Weapon.IndexOf(WeaponKind.Homing));
        }

        // Стик водит крестик обеими осями: кладём палец в кольцо и ведём его
        // вверх-вправо. Радиус кольца — доля меньшей стороны, поэтому берём
        // заведомо больше зоны покоя.
        if (_step == 30 && t - _mark > 0.4)
        {
            Advance(31);
            var w = Active;
            if (w == null || !w.AwaitingTarget)
            {
                Fail("ракета не попросила отметить цель");
                Advance(4);
            }
            else
            {
                _mark0 = w.MarkPoint;

                // Палец сперва ложится в само кольцо и только потом едет: если
                // первым же кадром показать его уже отклонённым, стик поднимется
                // под палец и отклонения не увидит вовсе.
                Finger(1, TouchInput.StickHome, UnityEngine.InputSystem.TouchPhase.Began);
                Advance(305);
            }
        }

        if (_step == 305 && t - _mark > 0.2)
        {
            Advance(31);
            float r = Mathf.Min(Screen.safeArea.width, Screen.safeArea.height) * 0.12f;
            Finger(1, TouchInput.StickHome + new Vector2(r, r),
                   UnityEngine.InputSystem.TouchPhase.Moved);
        }

        if (_step == 31 && t - _mark > 0.5)
        {
            Advance(32);
            var w = Active;
            Vector2 d = w.MarkPoint - _mark0;
            Debug.Log($"TOUCH step7: стик увёл крестик на {d.x:0.0};{d.y:0.0}");
            if (d.x < 1f || d.y < 1f) Fail($"стик не водит крестик обеими осями: {d.x:0.0};{d.y:0.0}");

            Finger(1, TouchInput.StickHome, UnityEngine.InputSystem.TouchPhase.Ended);

            // Второй способ: тычок по карте. Берём точку подальше от стика и от
            // плашки оружия, чтобы палец не перехватили ни кольцо, ни кнопки.
            var tap = SafePoint(0.72f, 0.62f);
            var cam = UnityEngine.Camera.main;
            _tapWorld = cam.ScreenToWorldPoint(new Vector3(tap.x, tap.y, -cam.transform.position.z));
            Finger(2, tap, UnityEngine.InputSystem.TouchPhase.Began);
        }

        if (_step == 32 && t - _mark > 0.4)
        {
            Advance(33);
            var w = Active;
            float miss = Vector2.Distance(w.MarkPoint, _tapWorld);
            Debug.Log($"TOUCH step8: тычок в {_tapWorld.x:0.0};{_tapWorld.y:0.0}, " +
                      $"крестик в {w.MarkPoint.x:0.0};{w.MarkPoint.y:0.0} (промах {miss:0.0})");
            if (miss > 1.5f) Fail($"тычок по экрану не поставил крестик: {miss:0.0} юнита");

            Finger(2, SafePoint(0.72f, 0.62f), UnityEngine.InputSystem.TouchPhase.Ended);
            TouchInput.UiFire(true);
        }

        if (_step == 33 && t - _mark > 0.3)
        {
            Advance(34);
            TouchInput.UiFire(false);
        }

        if (_step == 34 && t - _mark > 0.3)
        {
            Advance(4);
            var w = Active;
            Debug.Log($"TOUCH step9: цель отмечена={!w.AwaitingTarget}");
            if (w.AwaitingTarget) Fail("кнопка ОГОНЬ не поставила метку");

            // Дальше тест идёт прежним порядком, базукой.
            GameManager.I.SelectWeapon(0);
        }

        if (_step == 4 && t - _mark > 0.4)
        {
            Advance(5);
            TouchInput.UiFire(true);
        }

        if (_step == 5 && t - _mark > 0.5)
        {
            Advance(6);
            var w = Active;
            Debug.Log($"TOUCH step5: заряд={(w != null ? w.Charge : -1f):0.00}");
            if (w == null || !w.IsCharging || w.Charge <= 0.05f) Fail("удержание ОГОНЬ не копит силу");
            BeginShot();
            TouchInput.UiFire(false);
        }

        // Панель рисуется в текстуру без очистки: за лишние кадры полупрозрачные
        // кнопки накапливаются в чёрные пятна, а текст двоится. Поэтому забираем
        // ровно после одного кадра игры.
        if (_step == 6 && Time.frameCount > _shotFrame)
        {
            Advance(66);
            EndShot("touch-hud");
        }

        if (_step == 66 && t - _mark > 0.8)
        {
            Advance(7);
            var p = Object.FindAnyObjectByType<Projectile>();
            bool flying = p != null || GameManager.I.State != GameState.Aim;
            Debug.Log($"TOUCH step6: состояние={GameManager.I.State} снаряд={(p != null)}");
            if (!flying) Fail("отпускание ОГОНЬ не выстрелило");
        }

        if (_step == 7 && t - _mark > 0.5) Finish();

        if (t > 45) { Fail("тест не уложился в 45 секунд"); Finish(); }
    }

    /// Снимок самой панели HUD: она рисуется поверх экрана, камера её не видит,
    /// поэтому просим UI Toolkit рисовать панель в текстуру — на кадр, между двумя
    /// заходами этого метода.
    static RenderTexture _rt;
    static PanelSettings _shotPanel;

    static void BeginShot()
    {
        var hud = Hud.I;
        var doc = hud != null ? hud.GetComponent<UIDocument>() : null;
        if (doc == null || doc.panelSettings == null) { Debug.LogWarning("TOUCH: панель HUD не найдена"); return; }

        _rt = new RenderTexture(1334, 750, 24, RenderTextureFormat.ARGB32);   // альбомный iPhone
        var cam = Camera.main;
        if (cam != null)
        {
            var prev = cam.targetTexture;
            cam.targetTexture = _rt;
            cam.Render();
            cam.targetTexture = prev;
        }

        _shotFrame = Time.frameCount;
        _shotPanel = doc.panelSettings;
        _shotPanel.targetTexture = _rt;   // панель дорисуется поверх на следующем кадре
    }

    static void EndShot(string name)
    {
        if (_rt == null) return;
        if (_shotPanel != null) _shotPanel.targetTexture = null;

        RenderTexture.active = _rt;
        var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        string path = Path.Combine(Dir, name + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log("TOUCH: снимок " + path);

        Object.DestroyImmediate(tex);
        _rt.Release();
        Object.DestroyImmediate(_rt);
        _rt = null;
        _shotPanel = null;
    }
}
