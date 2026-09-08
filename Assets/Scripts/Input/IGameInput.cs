using UnityEngine;

/// Чем игрок управляет прямо сейчас. Меняется на лету: воткнули геймпад — переключились.
public enum InputScheme { Keyboard, Gamepad, Touch }

/// Ввод для одного червя, выраженный игровыми понятиями, а не клавишами.
/// Реализуют клавиатура, геймпад, касания — и, с фазы 8, бот.
public interface IGameInput
{
    InputScheme Scheme { get; }

    /// Ход влево-вправо, -1..1.
    float Move { get; }

    /// Скорость изменения угла прицела, -1..1. Игнорируется, если задан HasAimTarget.
    float AimAxis { get; }

    /// Прицел задан точкой в мире, а не скоростью: палец, мышь, расчёт бота.
    bool HasAimTarget { get; }
    Vector2 AimTarget { get; }

    /// Точка, отмеченная крестиком: ею наводятся ракета, налёт и телепорт
    /// (Weapon.Targeted). У живого игрока её ставит сам крестик — он ездит по
    /// карте и подтверждается «Огнём», — поэтому клавиатура, геймпад и касания
    /// отвечают false. Бот крестик не водит: он приносит готовую точку из
    /// расчёта, и червь ставит метку по ней одним кадром.
    bool HasMark { get; }
    Vector2 Mark { get; }

    bool JumpPressed { get; }

    /// Набор силы: нажали — начали, держим — копим, отпустили — выстрел.
    bool FirePressed { get; }
    bool FireHeld { get; }
    bool FireReleased { get; }

    /// Индекс оружия или -1, если запроса нет.
    int WeaponRequest { get; }

    /// Перебор оружия: -1 назад, +1 вперёд, 0 — ничего.
    int WeaponCycle { get; }

    /// Передать ход другому червю своей команды. Работает только до первого
    /// действия — дальше GameManager.CanSelectWorm закрывает окно.
    bool SelectWormPressed { get; }
}

/// Всё, что не относится к червю: камера и служебные команды.
public interface ISystemInput
{
    /// Изменение ортографического размера камеры в юнитах за кадр.
    float ZoomDelta { get; }

    /// Сдвиг камеры в пикселях экрана за кадр.
    Vector2 PanDelta { get; }
    bool PanActive { get; }

    bool RestartPressed { get; }
}

/// Живой игрок за конкретным устройством. Роутер спрашивает Available и Active,
/// чтобы понять, кому отдать управление.
public interface IHumanInput : IGameInput, ISystemInput
{
    /// Устройство подключено.
    bool Available { get; }

    /// В этом кадре с устройства пришло что-то осмысленное.
    bool Active { get; }

    /// Раз в кадр, до чтения свойств.
    void Tick();
}
