using UnityEngine;

public enum WeaponKind
{
    Bazooka, Homing, Mortar, Grenade, Cluster, Banana,
    Shotgun, Uzi, FirePunch, Bat,
    Dynamite, Mine, Sheep, AirStrike,
    Rope, Teleport, Prod,
    Drill, Blowtorch, Girder,
    Parachute, Jetpack,

    // Оружие-визитки: то, ради чего в оригинале берегли ход.
    HolyGrenade, SuperSheep, Anvil, Napalm, MineStrike, Donkey
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
    Teleport,
    Dig,        // прорезать коридор в породе (бур, паяльная лампа)
    Build,      // добавить породу (балка)
    Chute,      // парашют: раскрыть в полёте
    Jet         // реактивный ранец: тяга, пока держат кнопку
}

/// Описание оружия. Держим в статическом массиве — прототипу хватает.
public class Weapon
{
    public WeaponKind Kind;
    public string Name;
    public WeaponUse Use = WeaponUse.Charged;
    /// Максимальная скорость при полном заряде. Полная полоса силы бросает
    /// через полкарты: при гравитации 24 юнита в секунду за секунду дальность
    /// по ровному месту равна v²/g, и прежние 28 у базуки давали всего
    /// тридцать три юнита на карте шириной девяносто шесть — с острова на
    /// остров было не добросить ни при каком угле.
    public float LaunchSpeed;
    public float BlastRadius;     // радиус воронки в юнитах
    public float Damage;          // урон в эпицентре
    public float Fuse;            // время до взрыва (0 = взрыв при взведении не нужен)
    public bool AffectedByWind;
    public bool Bouncy;
    public bool Contact = true;   // рвётся от касания; динамит и овца — нет
    /// Самонаводящаяся ракета: цель отмечается на карте до выстрела, и уже
    /// после запуска ракета доворачивает на эту точку. Точка статична —
    /// сдвинувшийся червь ракету за собой не уводит.
    public bool Homing;
    /// Летит ракетой: в воздухе это корпус с оперением, а не круглешок, и за
    /// ним тянется дымный след. У самонаводящейся это подразумевается само —
    /// признак нужен обычной базуке.
    public bool Rocket;
    public bool Walker;           // сам идёт по земле (овца)
    public int Cluster;           // на сколько осколков рассыпается при взрыве
    public int Burst = 1;         // сколько выстрелов в очереди у мгновенного оружия
    public float Spread;          // разброс очереди в градусах

    /// Длина реза и его радиус для WeaponUse.Dig, в юнитах.
    public float DigLength;
    public float DigRadius;

    /// Бур режет строго вниз, лампа — по прицелу.
    public bool DigDown;

    /// Сколько секунд тяги у ранца.
    public float Fuel;

    /// Через сколько секунд ходьбы снаряд отрывается от земли (супер-овца).
    /// 0 — не отрывается никогда, как обычная овца.
    public float LiftAfter;

    /// Снаряд не рвётся о землю, а ложится на неё миной (минный удар).
    public bool Plants;

    /// Сколько раз снаряд пробивает породу насквозь, прежде чем взорваться
    /// (бетонный осёл). Каждый пробой — своя воронка по пути вниз.
    public int Punches;

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

    /// Оружие, которому нужна отметка на карте, а не угол ствола: ракета,
    /// налёт и телепорт. Как в оригинале — выбрал такое, и ход уходит крестику:
    /// он ездит по карте, камера едет за ним, нажатие ставит метку. Ракета
    /// после метки ещё набирает силу, налёт и телепорт срабатывают сразу.
    public bool Targeted => Homing || Use == WeaponUse.Strike || Use == WeaponUse.Teleport;

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

    /// Арсенал первых «Червей» 1995 года. Инструменты для перекапывания
    /// ландшафта (бур, паяльная лампа, балка) добавлены шагом 3 «что дальше»:
    /// они режут и достраивают породу, а не рвут её воронкой.
    public static readonly Weapon[] All =
    {
        new Weapon { Kind = WeaponKind.Bazooka,  Name = "Базука",   LaunchSpeed = 56f, BlastRadius = 3.0f, Damage = 45f, AffectedByWind = true,  Rocket = true, Color = new Color(0.85f, 0.25f, 0.2f),  Ammo = -1 },
        new Weapon { Kind = WeaponKind.Homing,   Name = "Самонаводящаяся ракета", LaunchSpeed = 44f, BlastRadius = 2.8f, Damage = 40f, Homing = true,          Color = new Color(0.95f, 0.45f, 0.15f), Ammo = 2  },
        new Weapon { Kind = WeaponKind.Mortar,   Name = "Миномёт",  LaunchSpeed = 48f, BlastRadius = 1.8f, Damage = 20f, AffectedByWind = true,  Cluster = 4, Color = new Color(0.55f, 0.6f, 0.65f), Ammo = 3 },
        new Weapon { Kind = WeaponKind.Grenade,  Name = "Граната",  LaunchSpeed = 48f, BlastRadius = 3.4f, Damage = 50f, Fuse = 3.5f, Bouncy = true, Color = new Color(0.35f, 0.7f, 0.3f), Ammo = -1 },
        new Weapon { Kind = WeaponKind.Cluster,  Name = "Кассета",  LaunchSpeed = 52f, BlastRadius = 2.2f, Damage = 25f, AffectedByWind = true,  Cluster = 5, Color = new Color(0.95f, 0.75f, 0.2f), Ammo = 4 },
        new Weapon { Kind = WeaponKind.Banana,   Name = "Банан",    LaunchSpeed = 48f, BlastRadius = 3.2f, Damage = 45f, Fuse = 4f, Bouncy = true, Cluster = 5, Color = new Color(0.98f, 0.85f, 0.25f), Ammo = 1 },

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
        new Weapon { Kind = WeaponKind.Prod,     Name = "Толчок",   Use = WeaponUse.Melee,    Utility = true, BlastRadius = 0f, Damage = 0f, Color = new Color(0.95f, 0.82f, 0.62f), Ammo = -1 },

        // Инструменты земли. Урона у них нет, поэтому бот их не берёт
        // (BotCanUse требует Damage > 0), а ход они заканчивают, как оружие:
        // прокопался — и отходи, иначе бур стал бы бесплатным способом
        // разъехаться по карте на любое расстояние.
        new Weapon { Kind = WeaponKind.Drill,     Name = "Бур",      Use = WeaponUse.Dig,   Utility = true, DigLength = 5.5f, DigRadius = 0.62f, DigDown = true, Color = new Color(0.72f, 0.74f, 0.8f),  Ammo = 3 },
        new Weapon { Kind = WeaponKind.Blowtorch, Name = "Лампа",    Use = WeaponUse.Dig,   Utility = true, DigLength = 6.5f, DigRadius = 0.58f, Color = new Color(0.98f, 0.66f, 0.25f), Ammo = 3 },
        new Weapon { Kind = WeaponKind.Girder,    Name = "Балка",    Use = WeaponUse.Build, Utility = true, Color = new Color(0.62f, 0.66f, 0.72f), Ammo = 4 },

        // Средства передвижения по воздуху. Ход не заканчивают: парашют спасает
        // падение, из которого ещё надо успеть выстрелить, а ранец — способ
        // добраться до позиции, а не сам ход.
        new Weapon { Kind = WeaponKind.Parachute, Name = "Парашют", Use = WeaponUse.Chute, Utility = true, Color = new Color(0.85f, 0.9f, 0.98f), Ammo = 2 },
        new Weapon { Kind = WeaponKind.Jetpack,   Name = "Ранец",   Use = WeaponUse.Jet,   Utility = true, Fuel = 3.2f, Color = new Color(0.95f, 0.6f, 0.35f), Ammo = 1 },

        // Визитки оригинала: по одной штуке на матч, каждая решает партию.
        // Стоят в конце списка по той же причине, что и толчок, — цифровые
        // клавиши 1-9 и 0 закреплены за первой десяткой стволов и не должны
        // разъезжаться от каждого нового оружия.
        //
        // Ни одно из шести не заводит своего WeaponUse: святая граната — та же
        // скачущая граната с фитилём, супер-овца — та же закладка-ходок, а
        // наковальня, напалм, минный удар и осёл идут налётом. Разница вся
        // в полезной нагрузке и в цифрах.
        new Weapon { Kind = WeaponKind.HolyGrenade, Name = "Святая граната", LaunchSpeed = 46f, BlastRadius = 6.8f, Damage = 110f, Fuse = 5f, Bouncy = true, Color = new Color(0.96f, 0.9f, 0.55f), Ammo = 1 },

        // Овца с ранцем: две с половиной секунды бежит ногами, потом уходит
        // в небо волной и рвётся обо всё, чего коснётся. Фитиль длинный —
        // иначе она догорала бы, не долетев до соседнего острова.
        new Weapon { Kind = WeaponKind.SuperSheep, Name = "Супер-овца", Use = WeaponUse.Drop, BlastRadius = 4.6f, Damage = 70f, Fuse = 9f, Contact = false, Walker = true, LiftAfter = 2.5f, Color = new Color(0.86f, 0.92f, 1f), Ammo = 1 },

        // Наковальни падают отвесно: ветер их не сносит, воронка узкая, а
        // урон в ней больше, чем у бомбы налёта, — это удар по темени, а не
        // ковровое бомбометание.
        new Weapon { Kind = WeaponKind.Anvil, Name = "Наковальня", Use = WeaponUse.Strike, BlastRadius = 1.7f, Damage = 60f, Burst = 3, Color = new Color(0.42f, 0.45f, 0.52f), Ammo = 1 },

        // Напалм: четыре бака, каждый рассыпается шестью каплями. Порознь
        // капли почти безобидны, но накрывают склон целиком и достают из-за
        // укрытия, куда прямой выстрел не проходит.
        new Weapon { Kind = WeaponKind.Napalm, Name = "Напалм", Use = WeaponUse.Strike, BlastRadius = 1.4f, Damage = 14f, Burst = 4, Cluster = 6, Color = new Color(0.98f, 0.55f, 0.15f), Ammo = 1 },

        // Минный удар: те же пять бомб, но они не рвутся, а ложатся минами
        // там, где упали. Ход он не выигрывает — он портит карту противнику.
        new Weapon { Kind = WeaponKind.MineStrike, Name = "Минный удар", Use = WeaponUse.Strike, BlastRadius = 2.4f, Damage = 35f, Burst = 5, Plants = true, Color = new Color(0.6f, 0.62f, 0.66f), Ammo = 1 },

        // Бетонный осёл: падает с неба и уходит сквозь остров, пробивая по
        // воронке за раз. Не оружие против червя, а способ разрезать карту:
        // десяти пробоев хватает, чтобы шахта прошла толщу насквозь и вода
        // добралась до того, кто прятался под ней. Меньшим числом осёл
        // застревал в породе и оставлял просто глубокую воронку.
        new Weapon { Kind = WeaponKind.Donkey, Name = "Бетонный осёл", Use = WeaponUse.Strike, BlastRadius = 3.2f, Damage = 60f, Burst = 1, Punches = 10, Color = new Color(0.66f, 0.62f, 0.58f), Ammo = 1 }
    };

    /// Индекс в All по типу — чтобы ящик с припасами и меню не искали его руками.
    public static int IndexOf(WeaponKind kind)
    {
        for (int i = 0; i < All.Length; i++)
            if (All[i].Kind == kind) return i;
        return 0;
    }
}
