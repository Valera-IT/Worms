using System;
using UnityEngine;
using UnityEngine.UIElements;

/// Экраны меню на UI Toolkit: главное, выбор режима, настройка матча, настройки,
/// пауза, итоги. Панель и документ создаются в рантайме без ассетов — как HUD.
/// Логику переходов держит App, здесь только отрисовка и вызовы назад в App.
public class MenuUI : MonoBehaviour
{
    const int RefW = 1920, RefH = 1080;

    /// Сколько длится проявление из черноты на смене мира (фаза 13f).
    const float FadeTime = 0.45f;

    PanelSettings _panel;
    UIDocument _doc;
    VisualElement _docRoot;   // корень документа: под ним экраны и занавес
    VisualElement _root;       // слой экранов — его чистят и прячут переходы
    VisualElement _fade;       // чёрный занавес поверх всего, кликов не ловит
    float _fadeT;              // сколько черноты осталось, 0 — занавеса нет
    Action _pending;   // экран, запрошенный до того, как панель успела построиться

    void Awake()
    {
        _panel = UiPanels.Create("MenuPanel", 100);   // поверх боевого HUD

        _doc = gameObject.AddComponent<UIDocument>();
        _doc.panelSettings = _panel;
    }

    void OnDestroy()
    {
        if (_panel != null) Destroy(_panel);
    }

    bool Ready()
    {
        if (_root != null) return true;
        var r = _doc != null ? _doc.rootVisualElement : null;
        if (r == null) return false;
        _docRoot = r;
        _docRoot.style.flexGrow = 1f;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null) _docRoot.style.unityFontDefinition = FontDefinition.FromFont(font);

        // Экраны живут отдельным слоем: занавес обязан пережить и Clear,
        // и HideAll — иначе он пропадал бы ровно в тот момент, ради которого
        // и заведён, на переходе «меню → матч».
        _root = new VisualElement();
        _root.style.flexGrow = 1f;
        _docRoot.Add(_root);

        _fade = new VisualElement();
        _fade.style.position = Position.Absolute;
        _fade.style.left = 0; _fade.style.right = 0; _fade.style.top = 0; _fade.style.bottom = 0;
        _fade.style.backgroundColor = Color.black;
        _fade.style.display = DisplayStyle.None;
        _fade.pickingMode = PickingMode.Ignore;
        _docRoot.Add(_fade);
        return true;
    }

    /// Закрыть экран чернотой и проявить обратно. Зовётся App'ом на всякой
    /// смене мира: без этого матч возникал в кадре мгновенно, вместе со
    /// вспышкой сменившегося задника.
    public void FadeIn()
    {
        _fadeT = FadeTime;
        Paint();
    }

    void Paint()
    {
        if (_fade == null) return;
        float a = FadeTime > 0f ? Mathf.Clamp01(_fadeT / FadeTime) : 0f;
        _fade.style.display = a > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        // Гаснет не линейно: чернота сходит быстро, последние кадры — мягко.
        _fade.style.opacity = a * a;
    }

    void LateUpdate()
    {
        if (_pending != null && Ready())
        {
            var p = _pending;
            _pending = null;
            p();
        }

        if (_fadeT > 0f && Ready())
        {
            // Время матча может стоять (пауза), занавес — нет.
            _fadeT = Mathf.Max(0f, _fadeT - Time.unscaledDeltaTime);
            Paint();
        }
    }

    void Set(Action build)
    {
        if (!Ready()) { _pending = build; return; }
        _root.Clear();
        _root.style.display = DisplayStyle.Flex;
        build();
    }

    public void HideAll()
    {
        _pending = null;
        if (_root == null) return;
        _root.Clear();
        _root.style.display = DisplayStyle.None;
    }

    /// Есть ли сейчас чернота на экране — этим тест видит переход.
    public bool Fading => _fadeT > 0f;

    // --- экраны ---------------------------------------------------------

    public void ShowMain() => Set(BuildMain);
    public void ShowPause() => Set(BuildPause);
    public void ShowResults(MatchResults r) => Set(() => BuildResults(r));
    public void ShowModeSelect() => Set(BuildModeSelect);
    public void ShowMatchSetup() => Set(BuildMatchSetup);
    public void ShowSettings() => Set(() => BuildSettings(BuildMain));
    public void ShowAbout() => Set(BuildAbout);

    /// Титульный экран сделан по образцу оригинала 1995 года: мшистый задник,
    /// объёмный красный логотип, строчки меню без плашек и подпись внизу.
    void BuildMain()
    {
        var screen = MenuTheme.Screen();

        var column = new VisualElement();
        column.style.alignItems = Align.Center;
        column.style.width = Length.Percent(100);

        column.Add(MenuTheme.Logo("ЧЕРВЯКИ"));

        var tagline = MenuTheme.Text("● ● ●   КОМУ СЕГОДНЯ НЕ ПОВЕЗЁТ?   ● ● ●", 26, MenuTheme.Ink, FontStyle.Bold);
        tagline.style.letterSpacing = 3;
        tagline.style.marginTop = 12;
        tagline.style.marginBottom = 50;
        column.Add(tagline);

        // Первая строка белая, остальные бирюзовые — как в оригинале.
        column.Add(MenuTheme.Item("НАЧАТЬ БОЙ", () => App.I.StartMatch(MatchConfig.QuickFight())));
        column.Add(MenuTheme.Item("НАСТРОЙКА МАТЧА", () => { App.I.Draft = MatchConfig.Setup(); Set(BuildMatchSetup); }, MenuTheme.Accent));
        column.Add(MenuTheme.Item("НАСТРОЙКИ!", () => OpenSettings(BuildMain), MenuTheme.Accent));
        column.Add(MenuTheme.Item("ОБ ИГРЕ!", () => Set(BuildAbout), MenuTheme.Accent));
        if (Application.platform != RuntimePlatform.IPhonePlayer)
            column.Add(MenuTheme.Item("ВЫХОД!", () => App.I.QuitGame(), MenuTheme.Accent));

        screen.Add(column);

        var footer = MenuTheme.Text("ЧЕРВЯКИ  (C) 2026  VALERA GAMES", 22, MenuTheme.Ink, FontStyle.Bold);
        footer.style.position = Position.Absolute;
        footer.style.bottom = 28;
        footer.style.left = 0; footer.style.right = 0;
        footer.style.letterSpacing = 4;
        footer.style.unityTextAlign = TextAnchor.MiddleCenter;
        screen.Add(footer);

        _root.Add(screen);
    }

    void BuildModeSelect()
    {
        _root.Add(MenuTheme.Screen());
        var (layer, body) = MenuTheme.Sheet("Режим", 720f);

        body.Add(MenuTheme.Button("Быстрый бой против бота",
            () => App.I.StartMatch(MatchConfig.QuickBot())));
        body.Add(MenuTheme.Note("Сложность бота задаётся в «Настройке матча»."));

        body.Add(MenuTheme.Button("Хотсит — двое за одним устройством",
            () => App.I.StartMatch(MatchConfig.Hotseat())));
        body.Add(MenuTheme.Button("Настройка матча", () => { App.I.Draft = MatchConfig.Setup(); Set(BuildMatchSetup); }));

        var back = MenuTheme.Button("Назад", () => Set(BuildMain));
        back.style.marginTop = 18;
        body.Add(back);
        _root.Add(layer);
    }

    void BuildMatchSetup()
    {
        _root.Add(MenuTheme.Screen());
        var cfg = App.I.Draft;
        var (layer, body) = MenuTheme.Sheet("Настройка матча", 820f);

        body.Add(MenuTheme.Stepper("Команд", () => cfg.TeamCount.ToString(),
            () => cfg.SetTeamCount(cfg.TeamCount - 1),
            () => cfg.SetTeamCount(cfg.TeamCount + 1)));

        body.Add(MenuTheme.Stepper("Червей в команде", () => cfg.WormsPerTeam.ToString(),
            () => cfg.WormsPerTeam = Mathf.Clamp(cfg.WormsPerTeam - 1, 1, 8),
            () => cfg.WormsPerTeam = Mathf.Clamp(cfg.WormsPerTeam + 1, 1, 8)));

        body.Add(MenuTheme.Stepper("Время хода", () => Mathf.RoundToInt(cfg.TurnTime) + " с",
            () => cfg.TurnTime = Mathf.Clamp(cfg.TurnTime - 5f, 15f, 90f),
            () => cfg.TurnTime = Mathf.Clamp(cfg.TurnTime + 5f, 15f, 90f)));

        // Ботов можно завести столько же, сколько команд: тогда за устройством
        // не остаётся никого и матч играется сам собой.
        body.Add(MenuTheme.Stepper("Ботов", () => cfg.BotCount.ToString(),
            () => cfg.SetBotCount(cfg.BotCount - 1),
            () => cfg.SetBotCount(cfg.BotCount + 1)));

        body.Add(MenuTheme.Stepper("Сложность бота", () => DifficultyName(cfg.Difficulty),
            () => cfg.Difficulty = (BotDifficulty)Wrap((int)cfg.Difficulty - 1, 3),
            () => cfg.Difficulty = (BotDifficulty)Wrap((int)cfg.Difficulty + 1, 3)));

        body.Add(MenuTheme.Stepper("Тип мира", () => TerrainName(cfg.Terrain),
            () => cfg.Terrain = (TerrainKind)Wrap((int)cfg.Terrain - 1, 6),
            () => cfg.Terrain = (TerrainKind)Wrap((int)cfg.Terrain + 1, 6)));

        body.Add(MenuTheme.Stepper("Боезапас", () => AmmoName(cfg.Ammo),
            () => cfg.Ammo = (AmmoPlan)Wrap((int)cfg.Ammo - 1, 3),
            () => cfg.Ammo = (AmmoPlan)Wrap((int)cfg.Ammo + 1, 3)));

        body.Add(MenuTheme.Stepper("Ящики с припасами", () => CrateName(cfg.Crates),
            () => cfg.Crates = (CratePlan)Wrap((int)cfg.Crates - 1, 4),
            () => cfg.Crates = (CratePlan)Wrap((int)cfg.Crates + 1, 4)));

        body.Add(MenuTheme.Stepper("Потоп",
            () => cfg.FloodRound <= 0 ? "выкл" : "с " + cfg.FloodRound + " раунда",
            () => cfg.FloodRound = Mathf.Clamp(cfg.FloodRound - 1, 0, 30),
            () => cfg.FloodRound = Mathf.Clamp(cfg.FloodRound + 1, 0, 30)));

        body.Add(MenuTheme.Note("Ботов не больше, чем команд; когда их поровну — все команды под ботом."));
        body.Add(MenuTheme.Note("Потоп поднимает воду каждый ход, и черви внизу тонут."));
        body.Add(MenuTheme.Note("«Случайный» выбирает тип из сида карты — «Новая карта» в паузе даёт другой мир."));

        var go = MenuTheme.Button("Начать бой", () => App.I.StartMatch(cfg.Clone()));
        go.style.marginTop = 16;
        body.Add(go);
        body.Add(MenuTheme.Button("Назад", () => Set(BuildModeSelect)));
        _root.Add(layer);
    }

    void OpenSettings(Action back)
    {
        Set(() => BuildSettings(back));
    }

    void BuildSettings(Action back)
    {
        // Из меню настройки лежат на мшистом заднике, из паузы — поверх боя.
        if (back == (Action)BuildMain) _root.Add(MenuTheme.Screen());

        var s = App.I.Settings;
        var (layer, body) = MenuTheme.Sheet("Настройки", 820f);

        body.Add(MenuTheme.Slider("Громкость звука", () => s.SoundVolume, v => { s.SoundVolume = v; s.Apply(); }));
        body.Add(MenuTheme.Slider("Громкость музыки", () => s.MusicVolume, v => { s.MusicVolume = v; s.Apply(); }));

        body.Add(MenuTheme.Stepper("Управление", () => InputName(s.Input),
            () => { s.Input = (InputPref)Wrap((int)s.Input - 1, 4); s.Apply(); },
            () => { s.Input = (InputPref)Wrap((int)s.Input + 1, 4); s.Apply(); }));

        body.Add(MenuTheme.Stepper("Язык", () => s.Language == LanguagePref.Russian ? "Русский" : "English",
            () => s.Language = (LanguagePref)Wrap((int)s.Language - 1, 2),
            () => s.Language = (LanguagePref)Wrap((int)s.Language + 1, 2)));

        if (GameSettings.CanVibrate)
            body.Add(MenuTheme.Toggle("Вибрация", () => s.Vibration, v => s.Vibration = v));

        var qn = QualitySettings.names;
        body.Add(MenuTheme.Stepper("Качество",
            () => (s.Quality >= 0 && s.Quality < qn.Length) ? qn[s.Quality] : "—",
            () => { s.Quality = Wrap(s.Quality - 1, qn.Length); s.Apply(); },
            () => { s.Quality = Wrap(s.Quality + 1, qn.Length); s.Apply(); }));

        if (GameSettings.CanChangeWindow)
        {
            body.Add(MenuTheme.Toggle("Полноэкранный", () => s.Fullscreen, v => { s.Fullscreen = v; s.Apply(); }));

            var res = Screen.resolutions;
            body.Add(MenuTheme.Stepper("Разрешение",
                () => (s.ResolutionIndex >= 0 && s.ResolutionIndex < res.Length)
                        ? res[s.ResolutionIndex].width + "×" + res[s.ResolutionIndex].height
                        : "как в системе",
                () => { s.ResolutionIndex = Mathf.Clamp(s.ResolutionIndex - 1, -1, res.Length - 1); s.Apply(); },
                () => { s.ResolutionIndex = Mathf.Clamp(s.ResolutionIndex + 1, -1, res.Length - 1); s.Apply(); }));
        }
        else
        {
            body.Add(MenuTheme.Note("Разрешение и окно настраиваются только на PC и Mac."));
        }

        var done = MenuTheme.Button("Готово", () => { s.Apply(); s.Save(); Set(back); });
        done.style.marginTop = 16;
        body.Add(done);
        _root.Add(layer);
    }

    void BuildPause()
    {
        var (layer, body) = MenuTheme.Sheet("Пауза", 560f);
        body.Add(MenuTheme.Button("Продолжить", () => App.I.ResumeFromPause()));
        body.Add(MenuTheme.Button("Настройки", () => OpenSettings(BuildPause)));
        body.Add(MenuTheme.Button("Новая карта", () => App.I.NewMap()));
        body.Add(MenuTheme.Button("В меню", () => App.I.ReturnToMenu()));
        _root.Add(layer);
    }

    void BuildAbout()
    {
        _root.Add(MenuTheme.Screen());
        var (layer, body) = MenuTheme.Sheet("Об игре", 720f);
        var p = MenuTheme.Text(
            "Червяки — пошаговая 2D-артиллерия в духе Worms. Весь мир, спрайты и\n" +
            "интерфейс рисуются из кода: в проекте нет ни одного бинарного ассета.\n\n" +
            "Управление подхватывается само: клавиатура, геймпад или касания.",
            18, MenuTheme.Ink);
        p.style.whiteSpace = WhiteSpace.Normal;
        p.style.marginBottom = 16;
        body.Add(p);
        body.Add(MenuTheme.Button("Назад", () => Set(BuildMain)));
        _root.Add(layer);
    }

    void BuildResults(MatchResults r)
    {
        var (layer, body) = MenuTheme.Sheet(r != null ? r.Title : "Итоги", 760f);

        if (r != null)
        {
            foreach (var row in r.Teams)
            {
                var line = MenuTheme.Row();
                line.style.height = 52;
                line.style.justifyContent = Justify.SpaceBetween;

                var name = MenuTheme.Text((row.IsWinner ? "● " : "") + row.Name, 22,
                    row.Color, row.IsWinner ? FontStyle.Bold : FontStyle.Normal);
                var stat = MenuTheme.Text(
                    $"живых {row.WormsAlive}/{row.WormsTotal}   урон {Mathf.RoundToInt(row.DamageDealt)}",
                    18, MenuTheme.InkDim);
                line.Add(name); line.Add(stat);
                body.Add(line);
            }
        }

        var rematch = MenuTheme.Button("Реванш", () => App.I.Rematch());
        rematch.style.marginTop = 18;
        body.Add(rematch);
        body.Add(MenuTheme.Button("В меню", () => App.I.ReturnToMenu()));
        _root.Add(layer);
    }

    // --- подписи -------------------------------------------------------

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    static string DifficultyName(BotDifficulty d) => d switch
    {
        BotDifficulty.Easy => "Лёгкая",
        BotDifficulty.Hard => "Трудная",
        _ => "Обычная"
    };

    static string TerrainName(TerrainKind k) => k switch
    {
        TerrainKind.Cave => "Пещера",
        TerrainKind.Archipelago => "Архипелаг",
        TerrainKind.Canyon => "Каньон",
        TerrainKind.Snow => "Снежные холмы",
        TerrainKind.Random => "Случайный",
        _ => "Остров"
    };

    static string CrateName(CratePlan c) => c switch
    {
        CratePlan.Off => "нет",
        CratePlan.Rare => "редко",
        CratePlan.Plenty => "часто",
        _ => "обычно"
    };

    static string AmmoName(AmmoPlan a) => a switch
    {
        AmmoPlan.Generous => "Щедрый",
        AmmoPlan.Unlimited => "Безлимит",
        _ => "Базовый"
    };

    static string InputName(InputPref p) => p switch
    {
        InputPref.Keyboard => "Клавиатура",
        InputPref.Gamepad => "Геймпад",
        InputPref.Touch => "Касания",
        _ => "Авто"
    };
}
