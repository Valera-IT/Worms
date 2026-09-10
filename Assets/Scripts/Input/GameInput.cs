using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// Единая точка ввода для живого игрока. Держит три реализации, сама выбирает активную
/// и притворяется ею: остальной код видит только IGameInput и ISystemInput и не знает,
/// откуда пришло движение — с клавиши, стика или пальца.
[DefaultExecutionOrder(-100)]
public class GameInput : MonoBehaviour, IGameInput, ISystemInput
{
    public static GameInput I { get; private set; }

    /// Ввод живого игрока. У команд бота (фаза 8) будет своя реализация.
    public static IGameInput Player => I;

    /// Схема поменялась — интерфейсу пора переписать подсказки.
    public static event Action<InputScheme> SchemeChanged;

    /// Принудительная схема из настроек. null — авто (роутер выбирает сам).
    /// Если выбранное устройство недоступно, откатываемся к авто-выбору.
    public static InputScheme? Forced;

    public readonly KeyboardInput Keyboard = new KeyboardInput();
    public readonly GamepadInput Gamepad = new GamepadInput();
    public readonly TouchInput Touch = new TouchInput();

    IHumanInput _current;
    int _polledFrame = -1;

    public InputScheme Scheme => Fresh().Scheme;

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
        _current = DefaultInput();
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    /// Опрос в начале каждого кадра, а не по первому чтению свойства. Пока ввод
    /// опрашивался лениво, кадр без единого чтения — ход бота, пауза, пересчёт
    /// хода — проходил мимо, и касание, начатое в нём, теряло фазу нажатия:
    /// палец лежал на стике, а червь не шёл. Fresh() ниже остаётся защитой от
    /// второго опроса в том же кадре.
    void Update() => Fresh();

    IHumanInput DefaultInput()
    {
        if (Application.isMobilePlatform && Touch.Available) return Touch;
        if (Keyboard.Available) return Keyboard;
        if (Touch.Available) return Touch;
        if (Gamepad.Available) return Gamepad;
        return Keyboard;
    }

    /// Все свойства идут через это: опрос ровно один раз за кадр, кто бы ни спросил первым.
    /// Так слой ввода не зависит от порядка выполнения скриптов.
    IHumanInput Fresh()
    {
        if (_polledFrame == Time.frameCount) return _current;
        _polledFrame = Time.frameCount;
        Poll();
        return _current;
    }

    void Poll()
    {
        if (Keyboard.Available) Keyboard.Tick();
        if (Gamepad.Available) Gamepad.Tick();
        if (Touch.Available) Touch.Tick();

        // Настройки могут прибить схему намертво, пока её устройство на месте.
        if (Forced.HasValue)
        {
            var pinned = ImplFor(Forced.Value);
            if (pinned != null && pinned.Available) { Switch(pinned); return; }
        }

        if (_current == null || !_current.Available)
        {
            Switch(DefaultInput());
            return;
        }

        // Переключаемся только на осмысленное действие: случайный дрейф стика
        // не должен отбирать управление у клавиатуры посреди хода.
        if (_current.Active) return;

        if (Touch != _current && Touch.Available && Touch.Active) { Switch(Touch); return; }
        if (Gamepad != _current && Gamepad.Available && Gamepad.Active) { Switch(Gamepad); return; }
        if (Keyboard != _current && Keyboard.Available && Keyboard.Active) Switch(Keyboard);
    }

    IHumanInput ImplFor(InputScheme s) => s switch
    {
        InputScheme.Gamepad => Gamepad,
        InputScheme.Touch => Touch,
        _ => Keyboard
    };

    void Switch(IHumanInput next)
    {
        if (next == null || next == _current) return;
        _current = next;
        SchemeChanged?.Invoke(next.Scheme);
    }

    public float Move => Fresh().Move;
    public float AimAxis => Fresh().AimAxis;
    public bool HasAimTarget => Fresh().HasAimTarget;
    public Vector2 AimTarget => Fresh().AimTarget;
    public bool HasMark => Fresh().HasMark;
    public Vector2 Mark => Fresh().Mark;
    public bool JumpPressed => Fresh().JumpPressed;
    public bool FirePressed => Fresh().FirePressed;
    public bool FireHeld => Fresh().FireHeld;
    public bool FireReleased => Fresh().FireReleased;
    public int WeaponRequest => Fresh().WeaponRequest;
    public int WeaponCycle => Fresh().WeaponCycle;
    public bool SelectWormPressed => Fresh().SelectWormPressed;

    public float ZoomDelta => Fresh().ZoomDelta;
    public Vector2 PanDelta => Fresh().PanDelta;
    public bool PanActive => Fresh().PanActive;
    public bool RestartPressed => Fresh().RestartPressed;

    /// Выбор оружия кнопкой интерфейса, а не клавишей: клик и касание идут сюда.
    public static void RequestWeapon(int index)
    {
        if (GameManager.I != null) GameManager.I.SelectWeapon(index);
    }

    /// То же для выбора червя: кнопка HUD зовёт напрямую, минуя схему ввода —
    /// иначе нажатие мышью пришлось бы вкладывать в реализацию касаний.
    public static void RequestNextWorm()
    {
        if (GameManager.I != null) GameManager.I.SelectNextWorm();
    }

    /// «Пропустить ход» — кнопка HUD и только она: клавиши под это не заводим,
    /// у клавиатуры и геймпада для того же есть таймер хода и вся карта.
    public static void RequestSkipTurn()
    {
        if (GameManager.I != null) GameManager.I.SkipTurn();
    }
}
