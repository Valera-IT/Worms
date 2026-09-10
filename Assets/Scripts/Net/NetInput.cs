using UnityEngine;

/// Ввод удалённого игрока. Для GameManager это такой же IGameInput, как
/// клавиатура или бот, — ровно тот же приём, которым в фазе 8 в игру вошёл
/// BotInput: команда получает Controller, и вся остальная игра не знает,
/// что за ним стоит провод.
///
/// Заполняется из пакетов, которые присылает хозяин команды. Между пакетами
/// поля держатся: потерянный кадр ввода означает «делай то же, что делал»,
/// а не «отпусти всё» — иначе на любой потере червь дёргался бы.
public class NetInput : IGameInput
{
    public InputScheme Scheme => InputScheme.Touch;

    public float Move { get; private set; }
    public float AimAxis { get; private set; }
    public bool HasAimTarget { get; private set; }
    public Vector2 AimTarget { get; private set; }
    public bool HasMark { get; private set; }
    public Vector2 Mark { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool FirePressed { get; private set; }
    public bool FireHeld { get; private set; }
    public bool FireReleased { get; private set; }
    public int WeaponRequest { get; private set; }
    public int WeaponCycle { get; private set; }
    public bool SelectWormPressed { get; private set; }

    /// Номер последнего применённого кадра. Пакеты по ненадёжному каналу
    /// приходят как угодно, и опоздавший старый кадр не должен затирать
    /// свежий.
    ushort _frame;
    bool _seenAny;

    /// Разовые нажатия (прыжок, выстрел, смена оружия) живут ровно один кадр
    /// игры — как у живого ввода. Иначе один присланный прыжок повторялся бы
    /// каждый кадр, пока не придёт следующий пакет.
    public void ConsumeEdges()
    {
        JumpPressed = false;
        FirePressed = false;
        FireReleased = false;
        SelectWormPressed = false;
        WeaponRequest = -1;
        WeaponCycle = 0;
    }

    /// Всё отпустить: хозяин команды отвалился, а червь не должен остаться
    /// бегущим в стену до конца хода.
    public void Clear()
    {
        Move = 0f;
        AimAxis = 0f;
        HasAimTarget = false;
        HasMark = false;
        FireHeld = false;
        ConsumeEdges();
    }

    // --- формат кадра ввода ----------------------------------------------
    // Один кадр — от 6 до 14 байт. Оси и признаки лежат в битовой маске,
    // а точки прицела и метки дописываются только когда они есть.

    const byte FJump = 1 << 0;
    const byte FFirePressed = 1 << 1;
    const byte FFireHeld = 1 << 2;
    const byte FFireReleased = 1 << 3;
    const byte FAimTarget = 1 << 4;
    const byte FMark = 1 << 5;
    const byte FSelectWorm = 1 << 6;

    /// Упаковать ввод живого игрока для отправки хосту.
    public static void Write(ref NetWriter w, IGameInput src, ushort frame)
    {
        byte flags = 0;
        if (src.JumpPressed) flags |= FJump;
        if (src.FirePressed) flags |= FFirePressed;
        if (src.FireHeld) flags |= FFireHeld;
        if (src.FireReleased) flags |= FFireReleased;
        if (src.HasAimTarget) flags |= FAimTarget;
        if (src.HasMark) flags |= FMark;
        if (src.SelectWormPressed) flags |= FSelectWorm;

        w.U16(frame);
        w.U8(flags);
        w.Unit(src.Move);
        w.Unit(src.AimAxis);
        w.I8((sbyte)Mathf.Clamp(src.WeaponRequest, -1, 127));
        w.I8((sbyte)Mathf.Clamp(src.WeaponCycle, -1, 1));
        if (src.HasAimTarget) w.Vec(src.AimTarget);
        if (src.HasMark) w.Vec(src.Mark);
    }

    /// Разобрать присланный кадр. Возвращает false, если кадр устарел или
    /// пакет оборван, — такой просто не применяем.
    public bool Read(ref NetReader r)
    {
        ushort frame = r.U16();
        byte flags = r.U8();
        float move = r.Unit();
        float aim = r.Unit();
        int weapon = r.I8();
        int cycle = r.I8();

        bool hasAim = (flags & FAimTarget) != 0;
        bool hasMark = (flags & FMark) != 0;
        Vector2 aimTarget = hasAim ? r.Vec() : Vector2.zero;
        Vector2 mark = hasMark ? r.Vec() : Vector2.zero;

        if (!r.Ok) return false;
        if (_seenAny && !Newer(frame, _frame)) return false;

        _frame = frame;
        _seenAny = true;

        Move = move;
        AimAxis = aim;
        HasAimTarget = hasAim;
        AimTarget = aimTarget;
        HasMark = hasMark;
        Mark = mark;
        FireHeld = (flags & FFireHeld) != 0;

        // Разовые нажатия накапливаем, а не присваиваем: если за один кадр
        // игры пришло два пакета, нажатие из первого не должно пропасть.
        JumpPressed |= (flags & FJump) != 0;
        FirePressed |= (flags & FFirePressed) != 0;
        FireReleased |= (flags & FFireReleased) != 0;
        SelectWormPressed |= (flags & FSelectWorm) != 0;
        if (weapon >= 0) WeaponRequest = weapon;
        if (cycle != 0) WeaponCycle = cycle;
        return true;
    }

    /// Номера кадров кольцевые: после 65535 идёт 0, и это не «устарел».
    static bool Newer(ushort a, ushort b) => (short)(a - b) > 0;
}
