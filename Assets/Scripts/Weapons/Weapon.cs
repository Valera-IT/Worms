using UnityEngine;

public enum WeaponKind
{
    Bazooka, Homing, Mortar, Grenade, Cluster, Banana,
    Shotgun, Uzi, FirePunch, Bat,
    Dynamite, Mine, Sheep, AirStrike,
    Rope, Teleport, Prod
}

/// Как оружие применяется. От этого зависит и ввод (набор силы или одно нажатие),
/// и то, берёт ли его бот: перебор пар «угол и сила» имеет смысл только для того,
/// что летит по баллистике.
public enum WeaponUse
{
    Charged,    // набор силы и выстрел по прицелу
    Hitscan,    // мгновенный луч
    Melee,      // удар вплотную
    Drop,       // положить под ноги
    Strike,     // налёт по указанной точке
    Rope,
    Teleport
}

/// Описание оружия. Держим в статическом массиве — прототипу хватает.
public class Weapon
{
    public WeaponKind Kind;
    public string Name;
    public WeaponUse Use = WeaponUse.Charged;
    public float LaunchSpeed;     // максимальная скорость при полном заряде
    public float BlastRadius;     // радиус воронки в юнитах
    public float Damage;          // урон в эпицентре
    public float Fuse;            // время до взрыва (0 = взрыв при взведении не нужен)
    public bool AffectedByWind;
    public bool Bouncy;
    public bool Contact = true;   // рвётся от касания; динамит и овца — нет
    public bool Homing;           // доворачивает на ближайшего врага
    public bool Walker;           // сам идёт по земле (овца)
    public int Cluster;           // на сколько осколков рассыпается при взрыве
    public int Burst = 1;         // сколько выстрелов в очереди у мгновенного оружия
    public float Spread;          // разброс очереди в градусах

    /// Вся очередь уходит одним нажатием, пулями подряд во времени (узи).
    /// Иначе каждый выстрел очереди — отдельное нажатие, и между ними червь
    /// стоит на месте с тем же стволом и может перецелиться (дробовик).
    public bool AutoBurst;

    /// Снаряжение, а не оружие: верёвка, телепорт и толчок никого не убивают.
    /// Верёвка и толчок вдобавок не заканчивают ход — после них можно ещё и
    /// выстрелить.
    public bool Utility;

    public Color Color;
    public int Ammo;              // -1 = бесконечно

    public bool Hitscan => Use == WeaponUse.Hitscan;

    /// Что бот берёт в руки. Баллистику и луч он считает моделью полёта, налёт —
    /// падением пятёрки бомб, удар вплотную — просто по тому, кто стоит рядом,
    /// закладку — воронкой под ногами с поправкой на то, куда он от неё убежит.
    /// Ракета до фазы 12 была вне списка; теперь модель полёта знает и доворот,
    /// так что вне списка остаётся только снаряжение без урона: верёвку бот
    /// бросает своим расчётом (BotPlanner.PlanSwing), телепорт — своим.
    public bool BotCanUse => Damage > 0f &&
                             (Use == WeaponUse.Charged || Use == WeaponUse.Hitscan
                           || Use == WeaponUse.Strike  || Use == WeaponUse.Melee
                           || Use == WeaponUse.Drop);

    /// Арсенал первых «Червей» 1995 года — без инструментов для перекапывания
    /// ландшафта (сварка, дрель, балка): они требуют своей работы с породой.
    public static readonly Weapon[] All =
    {
        new Weapon { Kind = WeaponKind.Bazooka,  Name = "Базука",   LaunchSpeed = 28f, BlastRadius = 3.0f, Damage = 45f, AffectedByWind = true,  Color = new Color(0.85f, 0.25f, 0.2f),  Ammo = -1 },
        new Weapon { Kind = WeaponKind.Homing,   Name = "Ракета",   LaunchSpeed = 22f, BlastRadius = 2.8f, Damage = 40f, Homing = true,          Color = new Color(0.95f, 0.45f, 0.15f), Ammo = 2  },
        new Weapon { Kind = WeaponKind.Mortar,   Name = "Миномёт",  LaunchSpeed = 24f, BlastRadius = 1.8f, Damage = 20f, AffectedByWind = true,  Cluster = 4, Color = new Color(0.55f, 0.6f, 0.65f), Ammo = 3 },
        new Weapon { Kind = WeaponKind.Grenade,  Name = "Граната",  LaunchSpeed = 24f, BlastRadius = 3.4f, Damage = 50f, Fuse = 3.5f, Bouncy = true, Color = new Color(0.35f, 0.7f, 0.3f), Ammo = -1 },
        new Weapon { Kind = WeaponKind.Cluster,  Name = "Кассета",  LaunchSpeed = 26f, BlastRadius = 2.2f, Damage = 25f, AffectedByWind = true,  Cluster = 5, Color = new Color(0.95f, 0.75f, 0.2f), Ammo = 4 },
        new Weapon { Kind = WeaponKind.Banana,   Name = "Банан",    LaunchSpeed = 24f, BlastRadius = 3.2f, Damage = 45f, Fuse = 4f, Bouncy = true, Cluster = 5, Color = new Color(0.98f, 0.85f, 0.25f), Ammo = 1 },

        new Weapon { Kind = WeaponKind.Shotgun,  Name = "Дробовик", Use = WeaponUse.Hitscan, BlastRadius = 1.6f, Damage = 15f, Burst = 2, Spread = 2.5f, Color = new Color(0.9f, 0.9f, 0.6f), Ammo = 3 },
        new Weapon { Kind = WeaponKind.Uzi,      Name = "Узи",      Use = WeaponUse.Hitscan, BlastRadius = 0.6f, Damage = 5f,  Burst = 8, Spread = 7f, AutoBurst = true, Color = new Color(0.75f, 0.75f, 0.8f), Ammo = 2 },
        new Weapon { Kind = WeaponKind.FirePunch,Name = "Кулак",    Use = WeaponUse.Melee,   BlastRadius = 1.1f, Damage = 30f, Color = new Color(1f, 0.55f, 0.2f), Ammo = 2 },
        new Weapon { Kind = WeaponKind.Bat,      Name = "Бита",     Use = WeaponUse.Melee,   BlastRadius = 0f,   Damage = 25f, Color = new Color(0.8f, 0.6f, 0.35f), Ammo = 2 },

        new Weapon { Kind = WeaponKind.Dynamite, Name = "Динамит",  Use = WeaponUse.Drop, BlastRadius = 4.2f, Damage = 75f, Fuse = 3f, Contact = false, Color = new Color(0.85f, 0.2f, 0.15f), Ammo = 2 },
        new Weapon { Kind = WeaponKind.Mine,     Name = "Мина",     Use = WeaponUse.Drop, BlastRadius = 2.4f, Damage = 35f, Contact = false, Color = new Color(0.5f, 0.5f, 0.55f), Ammo = 3 },
        new Weapon { Kind = WeaponKind.Sheep,    Name = "Овца",     Use = WeaponUse.Drop, BlastRadius = 3.2f, Damage = 50f, Fuse = 5f, Contact = false, Walker = true, Color = new Color(0.95f, 0.95f, 0.92f), Ammo = 1 },
        new Weapon { Kind = WeaponKind.AirStrike,Name = "Налёт",    Use = WeaponUse.Strike, BlastRadius = 2.2f, Damage = 28f, Burst = 5, Color = new Color(0.45f, 0.55f, 0.7f), Ammo = 1 },

        new Weapon { Kind = WeaponKind.Rope,     Name = "Верёвка",  Use = WeaponUse.Rope,     Utility = true, Color = new Color(0.72f, 0.68f, 0.62f), Ammo = 3 },
        new Weapon { Kind = WeaponKind.Teleport, Name = "Телепорт", Use = WeaponUse.Teleport, Utility = true, Color = new Color(0.65f, 0.45f, 0.95f), Ammo = 2 },

        // Толчок стоит последним, в ряду со снаряжением, а не среди кулака и
        // биты: урона у него нет, ход он не заканчивает и не кончается сам.
        // Место в конце списка выбрано ещё и затем, чтобы цифровые клавиши
        // 1-9 и 0 остались за прежними десятью стволами.
        new Weapon { Kind = WeaponKind.Prod,     Name = "Толчок",   Use = WeaponUse.Melee,    Utility = true, BlastRadius = 0f, Damage = 0f, Color = new Color(0.95f, 0.82f, 0.62f), Ammo = -1 }
    };

    /// Индекс в All по типу — чтобы ящик с припасами и меню не искали его руками.
    public static int IndexOf(WeaponKind kind)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Kind == kind) return i;
        return 0;
    }
}
