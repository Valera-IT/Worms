using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// Весь боевой интерфейс на UI Toolkit. Панель и её элементы строятся один раз,
/// каждый кадр меняются только тексты и размеры полосок — в отличие от старого
/// IMGUI ничего не аллоцируется в цикле отрисовки. Разметка задана в пикселях
/// макета 1920×1080 и масштабируется PanelSettings; чехол под элементы отступает
/// от Screen.safeArea, поэтому вырез и полоса-домой не режут интерфейс.
public class Hud : MonoBehaviour
{
    public static Hud I { get; private set; }

    const int RefW = 1920, RefH = 1080;

    /// Сколько секунд висит вылетевшая картинка оружия.
    const float PickShow = 1.35f;

    PanelSettings _panel;
    UIDocument _doc;
    VisualElement _root;

    VisualElement _safe;          // чехол под чрому, отодвинутый от выреза
    VisualElement _tagLayer;      // метки червей в мировых координатах
    VisualElement _floatLayer;    // всплывающие числа урона

    VisualElement _teamList;
    readonly List<TeamRow> _teamRows = new List<TeamRow>();

    VisualElement _turnPill;
    Label _turnText;

    VisualElement _deathPill;
    Label _deathText;

    VisualElement _windFill;

    VisualElement _powerWrap, _powerFill;

    readonly List<WeaponSlot> _weaponSlots = new List<WeaponSlot>();
    bool _weaponsDirty = true;

    VisualElement _pickWrap, _pickIcon;   // всплывающий выбор оружия — как в оригинале
    Label _pickName;
    int _pickWeapon = -1;
    float _pickTime;

    VisualElement _helpBox;

    VisualElement _swapBtn;       // «другой червь» — единственный способ выбрать червя пальцем
    bool _swapShown = true;

    readonly TouchPad _touch = new TouchPad();

    VisualElement _gameOver;
    Label _gameOverTitle, _gameOverHint;

    readonly Dictionary<Worm, WormTag> _tags = new Dictionary<Worm, WormTag>();
    readonly HashSet<Worm> _live = new HashSet<Worm>();
    readonly List<Worm> _stale = new List<Worm>();

    readonly List<Floater> _floaters = new List<Floater>();

    InputScheme _scheme = (InputScheme)(-1);
    Camera _cam;

    struct TeamRow { public VisualElement Root; public Label Name; public VisualElement HpFill; }
    struct WeaponSlot { public VisualElement Root, Icon; public Label Ammo, Key; public int ShownAmmo; public bool ShownSel; }
    class WormTag { public VisualElement Root, HpFill; public Label Name, Marker; public int ShownHp; }
    class Floater { public FloatingLabel Src; public Label View; }

    // --- жизненный цикл -----------------------------------------------------

    void Awake()
    {
        I = this;
        _cam = Camera.main;

        _panel = UiPanels.Create("HudPanel", 0);

        _doc = gameObject.AddComponent<UIDocument>();
        _doc.panelSettings = _panel;

        TryBuild();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (_panel != null) Destroy(_panel);
    }

    /// В батч-режиме без графики или до первого кадра rootVisualElement может быть
    /// ещё пуст — тогда достраиваемся в LateUpdate.
    bool TryBuild()
    {
        if (_root != null) return true;
        var r = _doc != null ? _doc.rootVisualElement : null;
        if (r == null) return false;
        Build(r);
        return true;
    }

    // --- сборка дерева ----------------------------------------------------

    void Build(VisualElement root)
    {
        _root = root;
        _root.pickingMode = PickingMode.Ignore;
        _root.style.flexGrow = 1f;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null) _root.style.unityFontDefinition = FontDefinition.FromFont(font);

        _tagLayer = HudTheme.Box(Color.clear);
        HudTheme.Fill(_tagLayer);
        _root.Add(_tagLayer);

        _floatLayer = HudTheme.Box(Color.clear);
        HudTheme.Fill(_floatLayer);
        _root.Add(_floatLayer);

        _safe = HudTheme.Box(Color.clear);
        _safe.style.position = Position.Absolute;
        _root.Add(_safe);

        BuildTeamList();
        BuildTurn();
        BuildFlood();
        BuildWind();
        BuildPower();
        BuildWeapons();
        BuildWeaponPick();
        BuildHelp();
        BuildPauseButton();
        BuildSwapButton();
        _touch.Build(_root, _safe);
        BuildGameOver();
    }

    /// Экранная кнопка паузы — единственный способ открыть паузу с касаний,
    /// и удобная мишень для мыши. С клавиатуры и геймпада работают R/Esc и Start.
    void BuildPauseButton()
    {
        var btn = HudTheme.Box(HudTheme.Panel);
        HudTheme.Round(btn, 8);
        btn.pickingMode = PickingMode.Position;
        var st = btn.style;
        st.position = Position.Absolute;
        st.right = 24; st.bottom = 20;
        st.width = 56; st.height = 56;
        st.alignItems = Align.Center;
        st.justifyContent = Justify.Center;
        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (App.I != null) App.I.TogglePause();
            evt.StopPropagation();
        });

        // Две полоски рисуем элементами: глиф паузы есть не во всех шрифтах,
        // на устройстве вместо него вылезали пустые квадраты.
        var bars = HudTheme.Box(Color.clear);
        bars.style.flexDirection = FlexDirection.Row;
        for (int i = 0; i < 2; i++)
        {
            var bar = HudTheme.Box(HudTheme.Ink);
            HudTheme.Round(bar, 2);
            bar.style.width = 6; bar.style.height = 22;
            if (i > 0) bar.style.marginLeft = 6;
            bars.Add(bar);
        }
        btn.Add(bars);
        _safe.Add(btn);
    }

    /// Кнопка «другой червь»: рядом с паузой, показывается только пока выбор
    /// открыт — то есть в начале своего хода, до первого шага и выстрела.
    /// С клавиатуры и геймпада то же делают Tab и Y.
    void BuildSwapButton()
    {
        var btn = HudTheme.Box(HudTheme.Panel);
        HudTheme.Round(btn, 8);
        btn.pickingMode = PickingMode.Position;
        var st = btn.style;
        st.position = Position.Absolute;
        st.right = 92; st.bottom = 20;      // левее кнопки паузы, через тот же зазор
        st.width = 56; st.height = 56;
        st.alignItems = Align.Center;
        st.justifyContent = Justify.Center;
        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            GameInput.RequestNextWorm();
            evt.StopPropagation();
        });

        // Стрелка вправо, собранная из трёх полосок: глифам в шрифте здесь
        // доверять нельзя ровно так же, как и в кнопке паузы.
        var shaft = HudTheme.Box(HudTheme.Ink);
        HudTheme.Round(shaft, 2);
        shaft.style.position = Position.Absolute;
        shaft.style.width = 26; shaft.style.height = 5;
        btn.Add(shaft);

        for (int i = 0; i < 2; i++)
        {
            var barb = HudTheme.Box(HudTheme.Ink);
            HudTheme.Round(barb, 2);
            barb.style.position = Position.Absolute;
            barb.style.width = 14; barb.style.height = 5;
            barb.style.left = 30; barb.style.top = i == 0 ? 20 : 30;
            barb.style.rotate = new Rotate(i == 0 ? 40f : -40f);
            btn.Add(barb);
        }

        _swapBtn = btn;
        _safe.Add(btn);
    }

    void BuildTeamList()
    {
        _teamList = HudTheme.Box(Color.clear);
        var st = _teamList.style;
        st.position = Position.Absolute;
        st.left = 24; st.top = 24;
        _safe.Add(_teamList);
    }

    void BuildTurn()
    {
        _turnPill = HudTheme.Box(HudTheme.Panel);
        HudTheme.Round(_turnPill, 8);
        HudTheme.Pad(_turnPill, 8, 18);
        var st = _turnPill.style;
        st.position = Position.Absolute;
        st.top = 24; st.left = Length.Percent(50);
        st.translate = new Translate(Length.Percent(-50), 0, 0);
        st.alignItems = Align.Center;
        st.minWidth = 160;

        _turnText = HudTheme.Text("", 30, HudTheme.Ink, FontStyle.Bold);
        _turnText.style.unityTextAlign = TextAnchor.MiddleCenter;
        _turnPill.Add(_turnText);
        _safe.Add(_turnPill);
    }

    /// Плашка потопа. Висит под таймером и появляется только когда есть о чём
    /// предупреждать: за три раунда до воды и всё время, пока она прибывает.
    void BuildFlood()
    {
        _deathPill = HudTheme.Box(new Color(0.55f, 0.12f, 0.14f, 0.85f));
        HudTheme.Round(_deathPill, 8);
        HudTheme.Pad(_deathPill, 5, 14);
        var st = _deathPill.style;
        st.position = Position.Absolute;
        st.top = 84; st.left = Length.Percent(50);
        st.translate = new Translate(Length.Percent(-50), 0, 0);
        st.alignItems = Align.Center;
        st.display = DisplayStyle.None;

        _deathText = HudTheme.Text("", 18, HudTheme.Ink, FontStyle.Bold);
        _deathPill.Add(_deathText);
        _safe.Add(_deathPill);
    }

    void BuildWind()
    {
        var box = HudTheme.Box(HudTheme.Panel);
        HudTheme.Round(box, 8);
        HudTheme.Pad(box, 8, 12);
        var st = box.style;
        st.position = Position.Absolute;
        st.top = 24; st.right = 24;
        st.width = 240;

        box.Add(HudTheme.Text("Ветер", 15, HudTheme.Ink));

        var track = HudTheme.Box(HudTheme.Track);
        HudTheme.Round(track, 4);
        track.style.height = 12;
        track.style.marginTop = 6;

        _windFill = HudTheme.Box(HudTheme.WindPos);
        _windFill.style.position = Position.Absolute;
        _windFill.style.top = 0; _windFill.style.bottom = 0;
        HudTheme.Round(_windFill, 4);
        track.Add(_windFill);

        var pivot = HudTheme.Box(HudTheme.Ink);
        pivot.style.position = Position.Absolute;
        pivot.style.top = -3; pivot.style.bottom = -3;
        pivot.style.width = 2;
        pivot.style.left = Length.Percent(50);
        track.Add(pivot);

        box.Add(track);
        _safe.Add(box);
    }

    void BuildPower()
    {
        _powerWrap = HudTheme.Box(HudTheme.Curtain);
        HudTheme.Round(_powerWrap, 4);
        var st = _powerWrap.style;
        st.position = Position.Absolute;
        // Панель оружия стала выше (иконки плюс название), полоска силы уходит над ней.
        st.bottom = 178; st.left = Length.Percent(50);
        st.translate = new Translate(Length.Percent(-50), 0, 0);
        st.width = 320; st.height = 22;
        st.paddingLeft = 3; st.paddingRight = 3; st.paddingTop = 3; st.paddingBottom = 3;
        st.display = DisplayStyle.None;

        _powerFill = HudTheme.Box(new Color(1f, 0.9f, 0.3f));
        _powerFill.style.height = Length.Percent(100);
        _powerFill.style.width = Length.Percent(0);
        HudTheme.Round(_powerFill, 3);
        _powerWrap.Add(_powerFill);
        _safe.Add(_powerWrap);
    }

    /// Панель оружия: ряд иконок в рамке. Названия под ней больше нет — его
    /// говорит всплывающий выбор оружия, как в оригинале.
    void BuildWeapons()
    {
        var column = HudTheme.Box(Color.clear);
        var cs = column.style;
        cs.position = Position.Absolute;
        cs.bottom = 16; cs.left = Length.Percent(50);
        cs.translate = new Translate(Length.Percent(-50), 0, 0);
        cs.alignItems = Align.Center;

        var bar = HudTheme.Box(HudTheme.Panel);
        HudTheme.Round(bar, 8);
        HudTheme.Pad(bar, 6, 6);

        // Весь арсенал в один ряд не влезает ни на телефоне, ни на мониторе,
        // поэтому панель — сетка в два ряда: половина слотов в ряду (при
        // семнадцати стволах — девять и восемь). Числом 8 ряд был прибит
        // намертво, и семнадцатое оружие открывало третий ряд из одной иконки.
        var rows = new List<VisualElement>();
        int perRow = (Weapon.All.Length + 1) / 2;
        for (int r = 0; r * perRow < Weapon.All.Length; r++)
        {
            var row = HudTheme.Box(Color.clear);
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;
            rows.Add(row);
            bar.Add(row);
        }

        for (int i = 0; i < Weapon.All.Length; i++)
        {
            int idx = i;
            var slot = HudTheme.Box(HudTheme.Slot);
            HudTheme.Round(slot, 6);
            slot.pickingMode = PickingMode.Position;   // оружие выбирается пальцем и мышью
            var ss = slot.style;
            ss.width = 76; ss.height = 76;
            ss.marginLeft = 3; ss.marginRight = 3;
            ss.marginTop = 2; ss.marginBottom = 2;
            ss.overflow = Overflow.Hidden;
            HudTheme.Border(slot, 2, HudTheme.SlotEdge);
            slot.RegisterCallback<PointerDownEvent>(evt => { GameInput.RequestWeapon(idx); evt.StopPropagation(); });

            // Сама картинка: рисуется кодом в WeaponIcons, растягивается на слот.
            var icon = HudTheme.Box(Color.clear);
            HudTheme.Fill(icon);
            icon.style.left = 6; icon.style.right = 6; icon.style.top = 6; icon.style.bottom = 6;
            icon.style.backgroundImage = new StyleBackground(WeaponIcons.Get(Weapon.All[i].Kind));
            slot.Add(icon);

            var ammo = HudTheme.Text("", 17, HudTheme.Ink, FontStyle.Bold);
            ammo.style.position = Position.Absolute;
            ammo.style.right = 5; ammo.style.bottom = 2;
            slot.Add(ammo);

            var key = HudTheme.Text("", 14, HudTheme.InkDim);
            key.style.position = Position.Absolute;
            key.style.left = 5; key.style.top = 1;
            slot.Add(key);

            rows[i / perRow].Add(slot);
            _weaponSlots.Add(new WeaponSlot { Root = slot, Icon = icon, Ammo = ammo, Key = key, ShownAmmo = int.MinValue, ShownSel = false });
        }

        column.Add(bar);
        _safe.Add(column);
    }

    /// Выбор оружия, как в оригинале на Sega: выбранное оружие на секунду
    /// вылетает на середину экрана огромной картинкой, под ней — название.
    /// Ряд иконок внизу остаётся: с него оружие выбирают пальцем и мышью.
    void BuildWeaponPick()
    {
        _pickWrap = HudTheme.Box(Color.clear);
        HudTheme.Fill(_pickWrap);
        var ws = _pickWrap.style;
        ws.justifyContent = Justify.Center;
        ws.alignItems = Align.Center;
        ws.display = DisplayStyle.None;

        _pickIcon = HudTheme.Box(Color.clear);
        _pickIcon.style.width = 320;
        _pickIcon.style.height = 320;
        _pickWrap.Add(_pickIcon);

        _pickName = HudTheme.Text("", 68, HudTheme.Ink, FontStyle.Bold);
        _pickName.style.letterSpacing = 10;
        _pickName.style.marginTop = 8;
        // Тень пожирнее: подпись лежит поверх ландшафта, а не поверх плашки.
        _pickName.style.textShadow = new TextShadow
        {
            offset = new Vector2(3f, 3f), blurRadius = 0f, color = new Color(0f, 0f, 0f, 0.85f)
        };
        _pickWrap.Add(_pickName);

        _safe.Add(_pickWrap);
    }

    /// Показ живёт секунду с небольшим: сначала картинка садится с наплыва,
    /// в конце уходит в прозрачность.
    void UpdateWeaponPick(GameManager gm)
    {
        if (gm.SelectedWeapon != _pickWeapon)
        {
            _pickWeapon = gm.SelectedWeapon;
            var w = Weapon.All[_pickWeapon];
            _pickIcon.style.backgroundImage = new StyleBackground(WeaponIcons.Get(w.Kind));
            _pickName.text = w.Name.ToUpperInvariant();
            _pickTime = PickShow;
        }

        if (_pickTime <= 0f)
        {
            if (_pickWrap.style.display != DisplayStyle.None) _pickWrap.style.display = DisplayStyle.None;
            return;
        }

        _pickTime -= Time.deltaTime;
        _pickWrap.style.display = DisplayStyle.Flex;

        float shown = PickShow - _pickTime;
        float pop = Mathf.Clamp01(shown / 0.16f);
        float scale = Mathf.Lerp(1.35f, 1f, pop * pop);
        _pickWrap.style.scale = new Scale(new Vector2(scale, scale));
        _pickWrap.style.opacity = Mathf.Clamp01(_pickTime / 0.4f);
    }

    void BuildHelp()
    {
        _helpBox = HudTheme.Box(new Color(0f, 0f, 0f, 0.3f));
        HudTheme.Round(_helpBox, 6);
        HudTheme.Pad(_helpBox, 6, 10);
        var st = _helpBox.style;
        st.position = Position.Absolute;
        st.left = 24; st.bottom = 20;
        st.maxWidth = 760;
        _safe.Add(_helpBox);
    }

    void BuildGameOver()
    {
        _gameOver = HudTheme.Box(Color.clear);
        HudTheme.Fill(_gameOver);
        _gameOver.pickingMode = PickingMode.Position;
        _gameOver.style.justifyContent = Justify.Center;
        _gameOver.style.alignItems = Align.Center;
        _gameOver.style.display = DisplayStyle.None;
        // Экран итогов теперь рисует App поверх HUD; этот оверлей — лишь мгновенный
        // фолбэк, если App почему-то не поднят.
        _gameOver.RegisterCallback<PointerDownEvent>(_ =>
        {
            if (App.I != null) App.I.Rematch();
            else if (GameManager.I != null) GameManager.I.Restart();
        });

        var band = HudTheme.Box(HudTheme.Curtain);
        band.style.width = Length.Percent(100);
        band.style.paddingTop = 40; band.style.paddingBottom = 40;
        band.style.alignItems = Align.Center;

        _gameOverTitle = HudTheme.Text("", 46, HudTheme.Ink, FontStyle.Bold);
        _gameOverTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        _gameOverHint = HudTheme.Text("", 20, HudTheme.Ink);
        _gameOverHint.style.unityTextAlign = TextAnchor.MiddleCenter;
        _gameOverHint.style.marginTop = 14;

        band.Add(_gameOverTitle);
        band.Add(_gameOverHint);
        _gameOver.Add(band);
        _root.Add(_gameOver);
    }

    // --- обновление за кадр ----------------------------------------------

    void LateUpdate()
    {
        if (!TryBuild()) return;

        var gm = GameManager.I;
        if (gm == null || gm.Teams.Count == 0) { _root.style.display = DisplayStyle.None; return; }
        _root.style.display = DisplayStyle.Flex;
        if (_cam == null) _cam = Camera.main;

        LayoutSafeArea();
        SyncScheme();
        UpdateTeams(gm);
        UpdateTurn(gm);
        UpdateFlood(gm);
        UpdateWind(gm);
        UpdatePower(gm);
        UpdateWeapons(gm);
        UpdateWeaponPick(gm);
        UpdateSwapButton(gm);
        UpdateTags(gm);
        UpdateFloaters();
        _touch.Tick(_scheme, gm, _cam);
        UpdateGameOver(gm);
    }

    void LayoutSafeArea()
    {
        float w = Screen.width, h = Screen.height;
        if (w <= 0f || h <= 0f) return;
        var sa = Screen.safeArea;
        var st = _safe.style;
        st.left = Length.Percent(100f * sa.xMin / w);
        st.right = Length.Percent(100f * (w - sa.xMax) / w);
        st.top = Length.Percent(100f * (h - sa.yMax) / h);
        st.bottom = Length.Percent(100f * sa.yMin / h);
    }

    void SyncScheme()
    {
        var s = GameInput.I != null ? GameInput.I.Scheme : InputScheme.Keyboard;
        if (s == _scheme) return;
        _scheme = s;
        _weaponsDirty = true;

        var lines = s switch
        {
            InputScheme.Gamepad => GamepadHelp,
            InputScheme.Touch => TouchHelp,
            _ => KeyboardHelp
        };
        _helpBox.Clear();
        foreach (var line in lines)
        {
            var l = HudTheme.Text(line, 15, HudTheme.Ink);
            l.style.marginTop = 2; l.style.marginBottom = 2;
            _helpBox.Add(l);
        }
    }

    /// Кнопку показываем ровно тогда, когда нажатие что-то сделает: свой ход,
    /// живой игрок, ничего ещё не сделано и в команде есть кем ходить.
    void UpdateSwapButton(GameManager gm)
    {
        bool show = gm.CanSelectWorm
                 && !(App.I != null && App.I.Paused)
                 && !(gm.Teams[gm.CurrentTeam].IsBot);
        if (show == _swapShown) return;
        _swapShown = show;
        _swapBtn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    void UpdateTeams(GameManager gm)
    {
        if (_teamRows.Count != gm.Teams.Count)
        {
            _teamList.Clear();
            _teamRows.Clear();
            for (int i = 0; i < gm.Teams.Count; i++)
            {
                var row = HudTheme.Box(HudTheme.Panel);
                HudTheme.Round(row, 6);
                HudTheme.Pad(row, 6, 10);
                row.style.width = 360;
                row.style.marginBottom = 6;

                var name = HudTheme.Text("", 22, HudTheme.Ink, FontStyle.Bold);
                row.Add(name);

                var track = HudTheme.Box(HudTheme.Track);
                HudTheme.Round(track, 3);
                track.style.height = 12;
                track.style.marginTop = 4;

                var fill = HudTheme.Box(HudTheme.Ink);
                fill.style.height = Length.Percent(100);
                HudTheme.Round(fill, 3);
                track.Add(fill);
                row.Add(track);

                _teamList.Add(row);
                _teamRows.Add(new TeamRow { Root = row, Name = name, HpFill = fill });
            }
        }

        for (int i = 0; i < _teamRows.Count; i++)
        {
            var team = gm.Teams[i];
            var r = _teamRows[i];
            r.Name.text = team.Name + (team.IsBot ? " (бот)" : "") + (i == gm.CurrentTeam ? "  ◄ ход" : "");
            r.Name.style.color = team.Color;

            float max = 100f * team.Worms.Count;
            float frac = max > 0f ? Mathf.Clamp01(team.TotalHealth / max) : 0f;
            r.HpFill.style.width = Length.Percent(frac * 100f);
            r.HpFill.style.backgroundColor = team.Color;
        }
    }

    void UpdateTurn(GameManager gm)
    {
        string s = gm.State switch
        {
            GameState.Aim => Mathf.CeilToInt(Mathf.Max(0f, gm.TurnTimeLeft)) + " сек",
            GameState.Projectile => "выстрел…",
            GameState.Retreat => "отход " + Mathf.CeilToInt(Mathf.Max(0f, gm.TurnTimeLeft)) + " сек",
            GameState.Settle => "…",
            _ => ""
        };
        bool show = s.Length > 0;
        _turnPill.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show) _turnText.text = s;
    }

    void UpdateFlood(GameManager gm)
    {
        int left = gm.RoundsToFlood;
        string s = null;
        if (gm.Flooding) s = "≈ Потоп — вода прибывает каждый ход";
        else if (left >= 0 && left <= 3) s = "≈ Потоп через " + left;

        bool show = s != null;
        _deathPill.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show) _deathText.text = s;
    }

    void UpdateWind(GameManager gm)
    {
        float mag = Mathf.Clamp01(Mathf.Abs(gm.Wind));
        var st = _windFill.style;
        st.backgroundColor = gm.Wind >= 0f ? HudTheme.WindPos : HudTheme.WindNeg;
        st.width = Length.Percent(50f * mag);
        if (gm.Wind >= 0f) { st.left = Length.Percent(50); st.right = StyleKeyword.Auto; }
        else               { st.right = Length.Percent(50); st.left = StyleKeyword.Auto; }
    }

    void UpdatePower(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        bool show = worm != null && worm.IsCharging;
        _powerWrap.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) return;
        _powerFill.style.width = Length.Percent(worm.Charge * 100f);
        _powerFill.style.backgroundColor =
            Color.Lerp(new Color(1f, 0.9f, 0.3f), new Color(1f, 0.25f, 0.2f), worm.Charge);
    }

    void UpdateWeapons(GameManager gm)
    {
        bool keyboard = _scheme == InputScheme.Keyboard;
        for (int i = 0; i < _weaponSlots.Count; i++)
        {
            var slot = _weaponSlots[i];
            var w = Weapon.All[i];
            bool sel = i == gm.SelectedWeapon;
            int ammo = gm.AmmoOf(i);
            bool has = ammo != 0;

            if (!_weaponsDirty && ammo == slot.ShownAmmo && sel == slot.ShownSel) continue;

            slot.Root.style.backgroundColor = sel ? HudTheme.SlotSel : HudTheme.Slot;
            HudTheme.Border(slot.Root, sel ? 3 : 2, sel ? HudTheme.Ink : HudTheme.SlotEdge);

            // Кончившееся оружие гасим, а не прячем: место в ряду остаётся за ним.
            slot.Icon.style.unityBackgroundImageTintColor = has ? Color.white : HudTheme.NoAmmo;
            slot.Ammo.text = ammo < 0 ? "∞" : ammo.ToString();
            slot.Ammo.style.color = has ? HudTheme.Ink : HudTheme.Spent;
            slot.Key.text = keyboard && i < 10 ? ((i + 1) % 10).ToString() : "";

            slot.ShownAmmo = ammo;
            slot.ShownSel = sel;
            _weaponSlots[i] = slot;
        }

        _weaponsDirty = false;
    }

    void UpdateTags(GameManager gm)
    {
        var worms = gm.AllWorms();

        _live.Clear();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w != null && !w.IsDead) _live.Add(w);
        }

        _stale.Clear();
        foreach (var kv in _tags)
            if (!_live.Contains(kv.Key)) _stale.Add(kv.Key);
        for (int i = 0; i < _stale.Count; i++)
        {
            _tags[_stale[i]].Root.RemoveFromHierarchy();
            _tags.Remove(_stale[i]);
        }

        if (_cam == null) return;

        foreach (var worm in _live)
        {
            if (!_tags.TryGetValue(worm, out var tag))
            {
                tag = NewTag();
                _tags[worm] = tag;
            }

            Vector3 head = worm.transform.position + Vector3.up * 1.05f;
            if (_cam.WorldToViewportPoint(head).z <= 0f) { tag.Root.style.display = DisplayStyle.None; continue; }

            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, head, _cam);
            tag.Root.style.display = DisplayStyle.Flex;
            tag.Root.style.left = p.x;
            tag.Root.style.top = p.y;

            tag.HpFill.style.width = Length.Percent(Mathf.Clamp01(worm.Health / 100f) * 100f);
            tag.HpFill.style.backgroundColor = worm.Team.Color;

            int hp = Mathf.CeilToInt(worm.Health);
            bool active = worm == gm.ActiveWorm;
            if (hp != tag.ShownHp) { tag.Name.text = worm.WormName + " " + hp; tag.ShownHp = hp; }
            tag.Name.style.color = active ? HudTheme.Ink : HudTheme.InkDim;
            tag.Marker.style.display = active && gm.State == GameState.Aim ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    WormTag NewTag()
    {
        var rootEl = HudTheme.Box(Color.clear);
        rootEl.style.position = Position.Absolute;
        rootEl.style.alignItems = Align.Center;
        // Точка привязки — над головой червя: центр по X, низ метки на точке.
        rootEl.style.translate = new Translate(Length.Percent(-50), Length.Percent(-100), 0);

        var marker = HudTheme.Text("▼", 15, HudTheme.Ink);
        rootEl.Add(marker);

        var name = HudTheme.Text("", 15, HudTheme.Ink);
        rootEl.Add(name);

        var track = HudTheme.Box(HudTheme.Curtain);
        track.style.width = 92; track.style.height = 8;
        track.style.marginTop = 1;
        var fill = HudTheme.Box(HudTheme.Ink);
        fill.style.height = Length.Percent(100);
        track.Add(fill);
        rootEl.Add(track);

        _tagLayer.Add(rootEl);
        return new WormTag { Root = rootEl, HpFill = fill, Name = name, Marker = marker, ShownHp = int.MinValue };
    }

    void UpdateGameOver(GameManager gm)
    {
        bool over = gm.State == GameState.GameOver;
        _gameOver.style.display = over ? DisplayStyle.Flex : DisplayStyle.None;
        if (!over) return;
        _gameOverTitle.text = gm.WinnerText ?? "";
        _gameOverHint.text = RestartHint(_scheme);
    }

    // --- всплывающие числа урона ---------------------------------------

    public void RegisterFloater(FloatingLabel src)
    {
        if (!TryBuild()) return;
        var view = HudTheme.Text(src.Text, 20, src.Color, FontStyle.Bold);
        view.style.position = Position.Absolute;
        view.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50), 0);
        _floatLayer.Add(view);
        _floaters.Add(new Floater { Src = src, View = view });
    }

    public void UnregisterFloater(FloatingLabel src)
    {
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            if (_floaters[i].Src != src) continue;
            _floaters[i].View.RemoveFromHierarchy();
            _floaters.RemoveAt(i);
        }
    }

    void UpdateFloaters()
    {
        if (_cam == null) return;
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            if (f.Src == null) { f.View.RemoveFromHierarchy(); _floaters.RemoveAt(i); continue; }

            Vector3 pos = f.Src.transform.position;
            if (_cam.WorldToViewportPoint(pos).z <= 0f) { f.View.style.display = DisplayStyle.None; continue; }

            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, pos, _cam);
            f.View.style.display = DisplayStyle.Flex;
            f.View.style.left = p.x;
            f.View.style.top = p.y;
            var c = f.Src.Color; c.a = f.Src.Alpha;
            f.View.style.color = c;
        }
    }

    // --- подсказки по схеме ввода -------------------------------------

    static string RestartHint(InputScheme scheme) => scheme switch
    {
        InputScheme.Gamepad => "Start — пауза",
        InputScheme.Touch => "Кнопка паузы справа внизу",
        _ => "R или Esc — пауза"
    };

    static readonly string[] KeyboardHelp =
    {
        "←/→ — идти,  ↑/↓ — прицел,  Enter — прыжок",
        "Space — держать и отпустить для выстрела",
        "1-9 и 0 — оружие,  Q/E — листать весь арсенал,  Tab — другой червь",
        "колесо — зум, СКМ — камера",
        "Верёвка: ←/→ — раскачка, ↑/↓ — длина, Enter — отцеп",
        "R или Esc — пауза"
    };

    static readonly string[] GamepadHelp =
    {
        "Левый стик — ход и прицел,  A — прыжок",
        "X — держать и отпустить для выстрела",
        "Бамперы — листать оружие,  Y — другой червь",
        "Триггеры — зум, правый стик — камера",
        "Верёвка: стик — раскачка и длина,  A — отцеп",
        "Start — пауза"
    };

    static readonly string[] TouchHelp =
    {
        "Прицел ещё можно тянуть пальцем от червя",
        "Сетка внизу — оружие,  два пальца — зум,  кнопка справа — пауза",
        "Стрелка рядом с паузой — сходить другим червём"
    };
}
