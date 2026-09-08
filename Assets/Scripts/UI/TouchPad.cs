using UnityEngine;
using UnityEngine.UIElements;

/// Видимое управление для касаний. До этого жесты фазы 2 были невидимыми: экран
/// пустой, а ходить и стрелять предлагалось угадать. Здесь те же действия обретают
/// экранные органы — кольцо стика слева, кнопки прыжка, наклона прицела и выстрела
/// справа, кольцо-подсказка вокруг активного червя.
///
/// Слой живёт внутри HUD и показывается только когда активна схема Touch и ходит
/// живой игрок. Кнопки не читают касания сами: они дёргают статические «нажатия»
/// в `TouchInput`, и весь ввод по-прежнему собирается в одном месте. Свои
/// прямоугольники слой каждый кадр отдаёт в `TouchInput.Reserve`, чтобы палец на
/// кнопке не таскал заодно камеру.
public class TouchPad
{
    // Размеры в пикселях макета 1920×1080, как и остальной HUD.
    const float FireSize = 168f, SmallSize = 108f;
    const float RowBottom = 92f, Gap = 18f;

    static readonly Color Face    = new Color(0f, 0f, 0f, 0.34f);
    static readonly Color FaceHot = new Color(1f, 1f, 1f, 0.30f);
    static readonly Color Edge    = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color Hint    = new Color(1f, 1f, 1f, 0.28f);

    VisualElement _root;       // весь экран: тут живут стик и кольцо у червя
    VisualElement _layer;      // кнопки, внутри безопасной области
    VisualElement _stickRing, _stickKnob, _wormRing;
    Label _stickCaption, _wormCaption;
    VisualElement _fire, _jump, _aimUp, _aimDown;
    Label _fireLabel;

    bool _shown = true;

    public void Build(VisualElement root, VisualElement safe)
    {
        _root = root;

        var world = HudTheme.Box(Color.clear);
        HudTheme.Fill(world);
        root.Add(world);

        _wormRing = Ring(96f, Hint);
        _wormRing.style.display = DisplayStyle.None;
        world.Add(_wormRing);

        _wormCaption = HudTheme.Text("тяните от червя — прицел", 17, HudTheme.InkDim);
        _wormCaption.style.position = Position.Absolute;
        _wormCaption.style.display = DisplayStyle.None;
        world.Add(_wormCaption);

        _stickRing = Ring(190f, Edge);
        world.Add(_stickRing);

        _stickKnob = Ring(76f, Edge);
        _stickKnob.style.backgroundColor = Face;
        world.Add(_stickKnob);

        _stickCaption = HudTheme.Text("ходьба  ·  рывок ↑ прыжок", 17, HudTheme.InkDim);
        _stickCaption.style.position = Position.Absolute;
        world.Add(_stickCaption);

        _layer = HudTheme.Box(Color.clear);
        HudTheme.Fill(_layer);
        safe.Add(_layer);

        // Ряд справа снизу: выстрел крупнее прочего, слева от него — прицел и прыжок.
        float x = 40f;
        _fire = Button("ОГОНЬ", 20, FireSize, x, RowBottom);
        _fireLabel = (Label)_fire[0];
        x += FireSize + Gap;
        _aimUp = Button("▲", 30, SmallSize, x, RowBottom + SmallSize + 12f);
        _aimDown = Button("▼", 30, SmallSize, x, RowBottom);
        x += SmallSize + Gap;
        _jump = Button("прыжок", 16, SmallSize, x, RowBottom);

        Hold(_fire, down => TouchInput.UiFire(down));
        Hold(_aimUp, down => TouchInput.UiAim(down ? 1f : 0f));
        Hold(_aimDown, down => TouchInput.UiAim(down ? -1f : 0f));
        Hold(_jump, down => { if (down) TouchInput.UiJump(); });
    }

    VisualElement Ring(float size, Color edge)
    {
        var e = HudTheme.Box(Color.clear);
        var st = e.style;
        st.position = Position.Absolute;
        st.width = size; st.height = size;
        HudTheme.Round(e, size * 0.5f);
        st.borderTopWidth = st.borderBottomWidth = st.borderLeftWidth = st.borderRightWidth = 3f;
        st.borderTopColor = st.borderBottomColor = st.borderLeftColor = st.borderRightColor = edge;
        return e;
    }

    VisualElement Button(string caption, int fontSize, float size, float right, float bottom)
    {
        var b = Ring(size, Edge);
        b.style.backgroundColor = Face;
        b.style.right = right;
        b.style.bottom = bottom;
        b.style.alignItems = Align.Center;
        b.style.justifyContent = Justify.Center;
        b.pickingMode = PickingMode.Position;

        var l = HudTheme.Text(caption, fontSize, HudTheme.Ink, FontStyle.Bold);
        l.style.unityTextAlign = TextAnchor.MiddleCenter;
        b.Add(l);

        _layer.Add(b);
        return b;
    }

    /// Нажатие с удержанием: палец захватывается, чтобы отпускание пришло даже
    /// уехав с кнопки — иначе выстрел оставался бы взведённым навсегда.
    static void Hold(VisualElement e, System.Action<bool> set)
    {
        e.RegisterCallback<PointerDownEvent>(evt =>
        {
            e.CapturePointer(evt.pointerId);
            e.style.backgroundColor = FaceHot;
            set(true);
            evt.StopPropagation();
        });
        e.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (e.HasPointerCapture(evt.pointerId)) e.ReleasePointer(evt.pointerId);
            e.style.backgroundColor = Face;
            set(false);
            evt.StopPropagation();
        });
        e.RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            e.style.backgroundColor = Face;
            set(false);
        });
    }

    // --- обновление за кадр ---------------------------------------------

    public void Tick(InputScheme scheme, GameManager gm, Camera cam)
    {
        // В паузе слой прячем: экран паузы рисует своя панель поверх, и мимо её
        // кнопок палец попадал бы прямо в «ОГОНЬ».
        bool show = scheme == InputScheme.Touch && gm != null && gm.ActiveWorm != null
                    && !(gm.Teams.Count > 0 && gm.Teams[gm.CurrentTeam].IsBot)
                    && !(App.I != null && App.I.Paused);

        if (show != _shown)
        {
            _shown = show;
            var d = show ? DisplayStyle.Flex : DisplayStyle.None;
            _layer.style.display = d;
            _stickRing.style.display = d;
            _stickKnob.style.display = d;
            _stickCaption.style.display = d;
            if (!show)
            {
                _wormRing.style.display = DisplayStyle.None;
                _wormCaption.style.display = DisplayStyle.None;
                TouchInput.ClearReserved();
                TouchInput.UiAim(0f);
                TouchInput.UiFire(false);   // палец мог остаться на «ОГОНЬ»
            }
        }
        if (!show) return;

        var touch = GameInput.I != null ? GameInput.I.Touch : null;
        UpdateStick(touch);
        UpdateWormRing(touch, gm, cam);
        UpdateFire(gm);
        Reserve();
    }

    /// Кольцо стика стоит в своём углу, пока палец не опущен; с пальцем — переезжает
    /// туда, где палец коснулся, ровно как это делает сам `TouchInput`.
    void UpdateStick(TouchInput touch)
    {
        var safe = Screen.safeArea;
        // Домашняя точка и радиус приходят из слоя ввода: он же считает от них ноль
        // стика, и разъехаться этим двум местам нельзя.
        Vector2 homeScreen = TouchInput.StickHome;
        float radiusScreen = Mathf.Min(safe.width, safe.height) * 0.12f;

        Vector2 center = homeScreen, tip = homeScreen;
        if (touch != null && touch.StickActive)
        {
            center = touch.StickCenter;
            tip = touch.StickTip;
            radiusScreen = touch.StickRadiusPx;
        }

        float k = PanelScale();
        Place(_stickRing, ToPanel(center), radiusScreen * 2f * k);
        Place(_stickKnob, ToPanel(tip), radiusScreen * 0.8f * k);

        _stickRing.style.borderTopColor = _stickRing.style.borderBottomColor =
        _stickRing.style.borderLeftColor = _stickRing.style.borderRightColor =
            touch != null && touch.StickActive ? Edge : Hint;

        var cap = ToPanel(new Vector2(center.x, center.y - radiusScreen * 1.35f));
        _stickCaption.style.left = cap.x - 120f;
        _stickCaption.style.top = cap.y;
        _stickCaption.style.width = 240f;
        _stickCaption.style.unityTextAlign = TextAnchor.MiddleCenter;
    }

    /// Кольцо у червя показывает, откуда тянуть прицел. Пока тянут — гаснет,
    /// чтобы не мешать смотреть на траекторию.
    void UpdateWormRing(TouchInput touch, GameManager gm, Camera cam)
    {
        var worm = gm.ActiveWorm;
        bool aiming = touch != null && touch.AimActive;
        bool show = worm != null && cam != null && !aiming && !worm.IsCharging;

        _wormRing.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        _wormCaption.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) return;

        Vector3 p = worm.transform.position;
        if (cam.WorldToViewportPoint(p).z <= 0f)
        {
            _wormRing.style.display = DisplayStyle.None;
            _wormCaption.style.display = DisplayStyle.None;
            return;
        }

        Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, p, cam);
        float size = Mathf.Min(Screen.safeArea.width, Screen.safeArea.height) * 0.26f * PanelScale();
        Place(_wormRing, panel, size);

        // Пульс: кольцо дышит, иначе его принимают за элемент оформления червя.
        float a = 0.18f + 0.16f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2f));
        var c = new Color(1f, 1f, 1f, a);
        _wormRing.style.borderTopColor = _wormRing.style.borderBottomColor =
        _wormRing.style.borderLeftColor = _wormRing.style.borderRightColor = c;

        _wormCaption.style.left = panel.x - 140f;
        _wormCaption.style.top = panel.y + size * 0.5f + 8f;
        _wormCaption.style.width = 280f;
        _wormCaption.style.unityTextAlign = TextAnchor.MiddleCenter;
    }

    /// Подпись на кнопке огня зависит от оружия: верёвку и телепорт не заряжают.
    /// Пока ждут крестика — кнопка ставит метку, а не стреляет, и говорит об этом.
    void UpdateFire(GameManager gm)
    {
        var w = gm.CurrentWeapon;
        bool marking = gm.ActiveWorm != null && gm.ActiveWorm.AwaitingTarget;
        string s = marking ? "МЕТКА"
                 : w.Kind == WeaponKind.Rope ? "ВЕРЁВКА"
                 : w.Kind == WeaponKind.Teleport ? "ПРЫЖОК"
                 : w.Hitscan ? "ОГОНЬ" : "ДЕРЖАТЬ";
        if (_fireLabel.text != s) _fireLabel.text = s;
    }

    /// Отдаём прямоугольники кнопок слою ввода: под пальцем на кнопке не должно
    /// одновременно ехать поле боя.
    void Reserve()
    {
        TouchInput.ClearReserved();
        Reserve(_fire);
        Reserve(_jump);
        Reserve(_aimUp);
        Reserve(_aimDown);
    }

    void Reserve(VisualElement e)
    {
        var wb = e.worldBound;
        if (wb.width <= 0f || wb.height <= 0f) return;

        float kx = Screen.width / Mathf.Max(1f, _root.resolvedStyle.width);
        float ky = Screen.height / Mathf.Max(1f, _root.resolvedStyle.height);
        // Панель считает Y сверху вниз, касания — снизу вверх.
        var r = new Rect(wb.xMin * kx, Screen.height - wb.yMax * ky, wb.width * kx, wb.height * ky);
        TouchInput.Reserve(r);
    }

    // --- перевод координат ------------------------------------------------

    float PanelScale()
    {
        float h = _root.resolvedStyle.height;
        return h > 0f && Screen.height > 0 ? h / Screen.height : 1f;
    }

    /// Пиксели экрана (низ слева) → пиксели панели (верх слева).
    Vector2 ToPanel(Vector2 screen)
    {
        float w = _root.resolvedStyle.width, h = _root.resolvedStyle.height;
        if (w <= 0f || h <= 0f || Screen.width <= 0 || Screen.height <= 0) return screen;
        return new Vector2(screen.x / Screen.width * w, (1f - screen.y / Screen.height) * h);
    }

    static void Place(VisualElement e, Vector2 center, float size)
    {
        var st = e.style;
        st.width = size; st.height = size;
        HudTheme.Round(e, size * 0.5f);
        st.left = center.x - size * 0.5f;
        st.top = center.y - size * 0.5f;
    }
}
