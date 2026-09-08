using UnityEngine;

public enum AppPhase { Menu, Match, Results }

/// Корень приложения и его стейт-машина: Меню → Матч → Итоги. Раньше Bootstrap
/// безусловно поднимал GameManager, а тот строил мир в Awake — вставить меню было
/// некуда. Теперь мир поднимается только из StartMatch, а пауза заменила рестарт
/// по клавише R.
public class App : MonoBehaviour
{
    public static App I { get; private set; }

    /// Редакторские тесты ставят это перед входом в PlayMode, чтобы пропустить
    /// меню и сразу оказаться в матче со стандартной конфигурацией.
    public static bool AutostartMatch;
    public static MatchConfig AutostartConfig;

    public AppPhase Phase { get; private set; } = AppPhase.Menu;
    public bool Paused { get; private set; }
    public GameSettings Settings { get; private set; }

    /// Черновик конфигурации матча, который правит экран «Настройка матча».
    public MatchConfig Draft;

    public GameManager Match { get; private set; }
    public MatchResults LastResults { get; private set; }

    MenuUI _menu;
    Camera _camera;

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;

        Physics2D.gravity = new Vector2(0f, -24f);
        Application.targetFrameRate = 60;

        Settings = GameSettings.Load();
        Settings.Apply();
        Music.Ensure();

        Draft = MatchConfig.Hotseat();

        EnsureCamera();
        _menu = gameObject.AddComponent<MenuUI>();

        if (AutostartMatch)
        {
            StartMatch(AutostartConfig ?? MatchConfig.Hotseat());
        }
        else
        {
            Phase = AppPhase.Menu;
            ConfigureCameraForMenu();
            _menu.ShowMain();
        }
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Update()
    {
        // R / Start / экранная кнопка паузы — во время матча открывают паузу
        // (это и есть замена прежнему перезапуску по R).
        if (Phase == AppPhase.Match && GameInput.I != null && GameInput.I.RestartPressed)
            TogglePause();
    }

    // --- переходы --------------------------------------------------------

    /// Каждая смена мира проявляется из черноты (фаза 13f): карта, задник и
    /// HUD возникают разом, и без занавеса переход читается как сбой картинки.
    public void StartMatch(MatchConfig cfg)
    {
        TeardownMatch();

        var go = new GameObject("GameManager");
        Match = go.AddComponent<GameManager>();
        go.AddComponent<Hud>();
        Match.Begin(cfg);

        Phase = AppPhase.Match;
        SetPaused(false);
        _menu.HideAll();
        _menu.FadeIn();
    }

    /// Конец игры — GameManager зовёт это из CheckGameOver.
    public void OnMatchOver(MatchResults results)
    {
        LastResults = results;
        Phase = AppPhase.Results;
        SetPaused(false);

        // HUD убираем, а застывший мир оставляем фоном под экраном итогов.
        if (Match != null)
        {
            var hud = Match.GetComponent<Hud>();
            if (hud != null) Destroy(hud);
        }
        _menu.ShowResults(results);
    }

    /// «Реванш» с экрана итогов — та же конфигурация, новая карта.
    public void Rematch()
    {
        if (Match == null) { StartMatch(Draft.Clone()); return; }
        if (Match.GetComponent<Hud>() == null) Match.gameObject.AddComponent<Hud>();
        Match.Restart();
        Phase = AppPhase.Match;
        SetPaused(false);
        _menu.HideAll();
        _menu.FadeIn();
    }

    /// «Новая карта» из паузы.
    public void NewMap()
    {
        if (Match == null) return;
        Match.Restart();
        SetPaused(false);
        _menu.HideAll();
        _menu.FadeIn();
    }

    public void ReturnToMenu()
    {
        TeardownMatch();
        Phase = AppPhase.Menu;
        SetPaused(false);
        ConfigureCameraForMenu();
        _menu.ShowMain();
        _menu.FadeIn();
    }

    public void TogglePause()
    {
        if (Phase != AppPhase.Match) return;
        if (Match == null || Match.State == GameState.GameOver) return;
        SetPaused(!Paused);
        if (Paused) _menu.ShowPause();
        else _menu.HideAll();
    }

    public void ResumeFromPause() => TogglePause();

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // --- служебное ------------------------------------------------------

    void SetPaused(bool value)
    {
        Paused = value;
        Time.timeScale = value ? 0f : 1f;
    }

    void TeardownMatch()
    {
        if (Match != null)
        {
            Destroy(Match.gameObject);
            Match = null;
        }
        Time.timeScale = 1f;
    }

    void EnsureCamera()
    {
        _camera = Camera.main;
        if (_camera == null)
        {
            var go = new GameObject("MainCamera") { tag = "MainCamera" };
            _camera = go.AddComponent<Camera>();
            _camera.orthographic = true;
        }
        if (_camera.GetComponent<CameraRig>() == null)
            _camera.gameObject.AddComponent<CameraRig>();
        // Без слушателя звук не играет, а движок каждый кадр пишет об этом в консоль.
        if (_camera.GetComponent<AudioListener>() == null)
            _camera.gameObject.AddComponent<AudioListener>();
    }

    void ConfigureCameraForMenu()
    {
        if (_camera == null) EnsureCamera();
        _camera.orthographic = true;
        _camera.orthographicSize = 16f;
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.10f, 0.13f, 0.20f);
        _camera.transform.position = new Vector3(
            DestructibleTerrain.WorldWidth * 0.5f, DestructibleTerrain.WorldHeight * 0.5f, -10f);
    }
}
