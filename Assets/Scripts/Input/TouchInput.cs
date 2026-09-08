using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// Касания, доведённые до эргономики фазы 2: виртуальный джойстик от точки нажатия,
/// прицел протяжкой от червя с отдельным взводом выстрела, щипок для зума и
/// панорама пальцем. Все зоны считаются от Screen.safeArea, а не от всего экрана,
/// чтобы вырез и полоса-домой не срезали управление.
///
/// Левая нижняя четверть безопасной области — стик: вбок ведёт червя, резкий
/// рывок вверх — прыжок. Палец, начатый рядом с активным червём (радиус в
/// пикселях экрана, не в юнитах мира), — прицел: ведём — крутим угол; держим
/// дольше порога или протягиваем — пошёл набор силы; отпускаем — выстрел.
/// Мгновенный тычок по червю выстрелом не считается. Любой другой одиночный
/// палец таскает камеру, два пальца — щипок зума с той же панорамой по центру.
public class TouchInput : IHumanInput
{
    public InputScheme Scheme => InputScheme.Touch;
    public bool Available => Touchscreen.current != null;
    public bool Active { get; private set; }

    public float Move { get; private set; }
    public float AimAxis { get; private set; }
    public bool HasAimTarget { get; private set; }
    public Vector2 AimTarget { get; private set; }
    /// Крестик игрок водит сам: готовой точки у живого ввода нет.
    public bool HasMark => false;
    public Vector2 Mark => Vector2.zero;
    public bool JumpPressed { get; private set; }
    public bool FirePressed { get; private set; }
    public bool FireHeld { get; private set; }
    public bool FireReleased { get; private set; }
    public int WeaponRequest => -1;
    public int WeaponCycle => 0;
    // Оружие и выбор червя на касаниях выбираются кнопками HUD, а не жестом:
    // они идут прямо в GameManager через GameInput.Request*.
    public bool SelectWormPressed => false;

    public float ZoomDelta { get; private set; }
    public Vector2 PanDelta { get; private set; }
    public bool PanActive { get; private set; }
    public bool RestartPressed => false;

    // --- Состояние жестов для интерфейса фазы 3, всё в пикселях экрана. ---
    public bool StickActive { get; private set; }
    public Vector2 StickCenter { get; private set; }
    public Vector2 StickTip { get; private set; }
    public float StickRadiusPx { get; private set; }
    public bool AimActive { get; private set; }

    // --- нажатия экранных кнопок HUD ---------------------------------------
    // Кнопки на панели не читают касания сами: они складывают намерение сюда, а
    // разбирает его общий Tick. Так весь ввод по-прежнему собирается в одном месте,
    // а схема Touch остаётся активной, пока игрок жмёт кнопки, а не водит пальцем.
    static bool _uiJump, _uiFireDown, _uiFireUp, _uiFireHeld;
    static float _uiAim;

    public static void UiJump() => _uiJump = true;

    public static void UiFire(bool down)
    {
        if (down) { _uiFireDown = true; _uiFireHeld = true; }
        else if (_uiFireHeld) { _uiFireUp = true; _uiFireHeld = false; }
    }

    public static void UiAim(float axis) => _uiAim = Mathf.Clamp(axis, -1f, 1f);

    // --- прямоугольники кнопок, куда жестам хода нельзя --------------------
    static readonly List<Rect> _reserved = new List<Rect>();

    public static void ClearReserved() => _reserved.Clear();
    public static void Reserve(Rect screenRect) => _reserved.Add(screenRect);

    /// Сколько кнопок слой интерфейса объявил в этом кадре — по этому числу тест
    /// видит, что видимое управление действительно построено и живо.
    public static int ReservedCount => _reserved.Count;

    /// Палец у червя ловится в пределах этой доли меньшей стороны безопасной области.
    const float GrabScreenFraction = 0.16f;

    /// Сколько держать палец у червя, прежде чем пойдёт набор силы. Более короткое
    /// касание — промах по червю, а не выстрел. Прежние 0,12 с срабатывали от
    /// любого касания рядом с червём: полоса силы дёргалась там, где игрок
    /// всего лишь поправлял прицел.
    const float AimArmTime = 0.22f;

    /// Порог рывка вверх для прыжка, в радиусах стика в секунду.
    const float JumpFlickSpeed = 3.5f;

    /// Отклик стика: до этой доли радиуса — покой, за этой — полный ход. Зона
    /// покоя маленькая, полный ход близко: с большими значениями стик отвечал
    /// на нажатие вяло, будто ход прилипал к центру.
    const float StickDead = 0.10f;
    const float StickFull = 0.65f;

    /// Наводка самонаводящейся ракеты. Пока крестик не поставлен, стик водит
    /// его обеими осями, а любой другой палец кладёт крестик прямо в точку,
    /// куда ткнули. Ходить, прыгать и целиться в это время всё равно нечем:
    /// червь стоит, и выстрел ещё не начат — значит, и жесты можно отдать
    /// целиком под наводку.
    static bool Marking
    {
        get
        {
            var gm = GameManager.I;
            return gm != null && gm.ActiveWorm != null && gm.ActiveWorm.AwaitingTarget;
        }
    }

    static Rect Safe => Screen.safeArea;
    static float StickRadius => Mathf.Min(Safe.width, Safe.height) * 0.12f;

    /// Где кольцо стика стоит, пока его никто не трогает. Отсюда же его берёт
    /// `TouchPad`: рисовать кольцо в одном месте, а считать ноль в другом нельзя —
    /// именно из-за этого нажатие на край кольца раньше не давало хода.
    public static Vector2 StickHome =>
        new Vector2(Safe.xMin + Safe.width * 0.17f, Safe.yMin + Safe.height * 0.28f);

    struct Finger
    {
        public int Id;
        public Vector2 Pos;
        public bool Began;
    }

    readonly List<Finger> _fingers = new List<Finger>();

    /// Пальцы, которые были на экране в прошлом кадре. Фаза `Began` живёт ровно
    /// один опрос, и на телефоне её легко потерять: система умеет отдать палец
    /// сразу как `Moved`, а просадка кадра съедает её целиком. Тогда палец так и
    /// оставался без роли — стик молчал до следующего нажатия. Поэтому новым
    /// считаем палец, которого не было в прошлом кадре, а не тот, у кого фаза.
    readonly HashSet<int> _known = new HashSet<int>();

    int _stickId = -1, _aimId = -1, _panA = -1, _panB = -1;
    Vector2 _stickOrigin, _stickPrev;
    bool _jumpedThisTouch;
    Vector2 _aimStart;
    float _aimHeld;
    bool _aimArmed;
    Vector2 _panPrev, _panPrevB;
    float _pinchPrev;

    public void Tick()
    {
        Move = 0f;
        HasAimTarget = false;
        JumpPressed = FirePressed = FireReleased = false;
        FireHeld = false;
        AimAxis = 0f;
        ZoomDelta = 0f;
        PanDelta = Vector2.zero;
        PanActive = false;
        Active = false;
        StickActive = false;
        AimActive = false;

        var ts = Touchscreen.current;
        if (ts == null) { Reset(); return; }

        Collect(ts);
        DropLostRoles();
        AssignRoles();

        if (_fingers.Count > 0) Active = true;

        Stick();
        Aim();
        Camera();
        Buttons();
    }

    /// Разбор нажатий с экранных кнопок. Идёт после жестов: протяжка от червя
    /// задаёт точку прицела и тогда наклон кнопками не нужен.
    void Buttons()
    {
        if (_uiJump) { _uiJump = false; JumpPressed = true; Active = true; }
        if (_uiFireDown) { _uiFireDown = false; FirePressed = true; Active = true; }
        if (_uiFireHeld) { FireHeld = true; Active = true; }
        if (_uiFireUp) { _uiFireUp = false; FireReleased = true; Active = true; }

        // Кнопки наклона — запасной ход: если вертикаль уже задана стиком
        // (наводка) или пальцем по карте, они молчат.
        if (!HasAimTarget && Mathf.Abs(AimAxis) < 0.01f && Mathf.Abs(_uiAim) > 0.01f)
        { AimAxis = _uiAim; Active = true; }
    }

    void Collect(Touchscreen ts)
    {
        _fingers.Clear();
        var touches = ts.touches;
        for (int i = 0; i < touches.Count; i++)
        {
            var t = touches[i];
            var phase = t.phase.ReadValue();
            if (phase != TouchPhase.Began && phase != TouchPhase.Moved && phase != TouchPhase.Stationary)
                continue;

            int id = t.touchId.ReadValue();
            _fingers.Add(new Finger
            {
                Id = id,
                Pos = t.position.ReadValue(),
                Began = !_known.Contains(id)
            });
        }

        _known.Clear();
        for (int i = 0; i < _fingers.Count; i++) _known.Add(_fingers[i].Id);
    }

    bool Find(int id, out Vector2 pos)
    {
        for (int i = 0; i < _fingers.Count; i++)
            if (_fingers[i].Id == id) { pos = _fingers[i].Pos; return true; }
        pos = Vector2.zero;
        return false;
    }

    void DropLostRoles()
    {
        if (_stickId >= 0 && !Find(_stickId, out _)) _stickId = -1;

        // Палец, который целился, оторвался — это выстрел, но только если набор
        // силы успел начаться. Иначе это был промах по червю.
        if (_aimId >= 0 && !Find(_aimId, out _))
        {
            _aimId = -1;
            if (_aimArmed) { FireReleased = true; Active = true; }
            _aimHeld = 0f;
            _aimArmed = false;
        }

        // Первый пановый палец ушёл — второй занимает его место вместе со своей
        // прошлой позицией, чтобы камеру не дёрнуло скачком.
        if (_panA >= 0 && !Find(_panA, out _)) { _panA = _panB; _panPrev = _panPrevB; _panB = -1; _pinchPrev = 0f; }
        if (_panB >= 0 && !Find(_panB, out _)) { _panB = -1; _pinchPrev = 0f; }
    }

    void AssignRoles()
    {
        for (int i = 0; i < _fingers.Count; i++)
        {
            var f = _fingers[i];
            if (!f.Began) continue;
            if (f.Id == _stickId || f.Id == _aimId || f.Id == _panA || f.Id == _panB) continue;
            if (InReservedHud(f.Pos)) continue;

            if (_stickId < 0 && InStickZone(f.Pos))
            {
                _stickId = f.Id;
                // Палец, опущенный на само кольцо, берёт за ноль его центр: нажатие
                // на правый край кольца сразу ведёт вправо. Промах мимо кольца
                // по-прежнему поднимает стик под палец.
                Vector2 home = StickHome;
                _stickOrigin = Vector2.Distance(f.Pos, home) < StickRadius * 1.15f ? home : f.Pos;
                _stickPrev = f.Pos;
                _jumpedThisTouch = false;
                continue;
            }

            // В наводке крестик ставят тычком по любому месту карты, а не
            // протяжкой от червя: до червя ещё надо дотянуться, а цель может
            // быть в другом конце экрана.
            if (_aimId < 0 && (Marking || NearActiveWorm(f.Pos)))
            {
                _aimId = f.Id;
                _aimStart = f.Pos;
                _aimHeld = 0f;
                _aimArmed = false;
                continue;
            }

            if (_panA < 0) { _panA = f.Id; _panPrev = f.Pos; }
            else if (_panB < 0) { _panB = f.Id; _panPrevB = f.Pos; _pinchPrev = 0f; }
        }
    }

    static bool InStickZone(Vector2 p) =>
        p.x < Safe.xMin + Safe.width * 0.38f &&
        p.y < Safe.yMin + Safe.height * 0.5f;

    /// Нижняя центральная полоса отдана плашке оружия HUD, плюс прямоугольники
    /// экранных кнопок, которые слой интерфейса отдаёт сюда каждый кадр, — туда
    /// стик и камера не лезут.
    static bool InReservedHud(Vector2 p)
    {
        if (p.y < Safe.yMin + Safe.height * 0.11f &&
            Mathf.Abs(p.x - Safe.center.x) < Safe.width * 0.30f) return true;

        for (int i = 0; i < _reserved.Count; i++)
            if (_reserved[i].Contains(p)) return true;
        return false;
    }

    static bool NearActiveWorm(Vector2 screenPos)
    {
        if (!ActiveWormScreen(out var wormScreen)) return false;
        float grab = Mathf.Min(Safe.width, Safe.height) * GrabScreenFraction;
        return Vector2.Distance(screenPos, wormScreen) < grab;
    }

    static bool ActiveWormScreen(out Vector2 screen)
    {
        screen = default;
        var gm = GameManager.I;
        var cam = UnityEngine.Camera.main;
        if (gm == null || cam == null || gm.ActiveWorm == null) return false;

        Vector3 s = cam.WorldToScreenPoint(gm.ActiveWorm.transform.position);
        if (s.z < 0f) return false;
        screen = new Vector2(s.x, s.y);
        return true;
    }

    void Stick()
    {
        if (_stickId < 0 || !Find(_stickId, out var pos)) return;

        float r = StickRadius;
        Vector2 d = pos - _stickOrigin;

        float h = Mathf.Clamp(d.x / r, -1f, 1f);
        float mag = Mathf.Abs(h);
        // Прямая кривая с короткой зоной покоя: ход набирается сразу, а не после
        // половины радиуса, как было со SmoothStep.
        Move = mag < StickDead
             ? 0f
             : Mathf.Sign(h) * Mathf.Clamp01((mag - StickDead) / (StickFull - StickDead));

        if (Marking)
        {
            // Вторая ось стика — вертикаль крестика, с той же кривой, что и ход.
            // Рывок вверх в наводке прыжком не считаем: прыгать всё равно
            // некуда, а крестик от него дёргался бы вверх скачком.
            float v = Mathf.Clamp(d.y / r, -1f, 1f);
            float vmag = Mathf.Abs(v);
            AimAxis = vmag < StickDead
                    ? 0f
                    : Mathf.Sign(v) * Mathf.Clamp01((vmag - StickDead) / (StickFull - StickDead));
        }
        else
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float upSpeed = (pos.y - _stickPrev.y) / dt / r;
            if (!_jumpedThisTouch && d.y > r * 0.30f && upSpeed > JumpFlickSpeed)
            {
                JumpPressed = true;
                _jumpedThisTouch = true;
            }
        }
        _stickPrev = pos;

        // Стик подтягивается за пальцем, иначе с него не вернуться в ноль.
        if (d.magnitude > r) _stickOrigin = pos - d.normalized * r;

        StickActive = true;
        StickCenter = _stickOrigin;
        StickTip = _stickOrigin + Vector2.ClampMagnitude(pos - _stickOrigin, r);
        StickRadiusPx = r;
        Active = true;
    }

    void Aim()
    {
        if (_aimId < 0 || !Find(_aimId, out var pos)) return;

        var cam = UnityEngine.Camera.main;
        if (cam == null) return;

        _aimHeld += Mathf.Max(Time.deltaTime, 0f);

        // Прицел ведём всегда — червь поворачивается за пальцем ещё до взвода.
        HasAimTarget = true;
        AimActive = true;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(pos.x, pos.y, -cam.transform.position.z));
        AimTarget = world;
        Active = true;

        // В наводке палец только возит крестик. Взвод тут был бы ловушкой:
        // подтверждение цели уходило бы случайным отрывом пальца, а отменить
        // его нечем. Метку ставит кнопка «Огонь».
        if (Marking) return;

        // Набор силы начинается, только когда касание подтвердилось выдержкой или
        // заметной протяжкой.
        float armDist = Mathf.Min(Safe.width, Safe.height) * 0.04f;
        if (!_aimArmed && (_aimHeld >= AimArmTime || Vector2.Distance(pos, _aimStart) >= armDist))
        {
            _aimArmed = true;
            FirePressed = true;
        }
        if (_aimArmed) FireHeld = true;
        Active = true;
    }

    void Camera()
    {
        if (_panA < 0 || !Find(_panA, out var a)) return;

        if (_panB >= 0 && Find(_panB, out var b))
        {
            float dist = Vector2.Distance(a, b);
            if (_pinchPrev > 0f)
            {
                var cam = UnityEngine.Camera.main;
                float unitsPerPixel = cam != null ? cam.orthographicSize * 2f / Screen.height : 0.02f;
                ZoomDelta = (dist - _pinchPrev) * unitsPerPixel;
            }
            _pinchPrev = dist;

            Vector2 mid = (a + b) * 0.5f;
            Vector2 midPrev = (_panPrev + _panPrevB) * 0.5f;
            PanDelta = mid - midPrev;
            PanActive = true;
            _panPrev = a;
            _panPrevB = b;
            return;
        }

        PanDelta = a - _panPrev;
        PanActive = true;
        _panPrev = a;
    }

    void Reset()
    {
        _fingers.Clear();
        _known.Clear();
        _stickId = _aimId = _panA = _panB = -1;
        _pinchPrev = 0f;
        _aimHeld = 0f;
        _aimArmed = false;
    }
}
