using UnityEngine;

/// Что за предмет лежит на карте. Ящик, мина и бочка живут в разных классах,
/// но по проводу они одно и то же: номер, место и то, что с ними случилось.
public enum NetProp : byte { Crate = 0, Mine, Barrel }

/// Почему предмет ушёл с карты. Нужен не хосту, а картинке у клиента: у
/// подобранного щёлкает подбор, у взорванного встаёт взрыв, у утонувшего —
/// всплеск.
public enum PropGone : byte { Taken = 0, Blown, Sunk }

/// Шов между предметами на карте и сетью — тот же приём, что и
/// DestructibleTerrain.OpSink: сами ящики, мины и бочки про сеть не знают,
/// они только зовут отсюда.
///
/// Правило то же, что и во всём бою: хост считает, клиент повторяет. Предмет
/// заводится и пропадает только у хоста; у клиента он появляется по объявлению
/// и по сверке, а сам по себе не детонирует, не подбирается и не тонет —
/// иначе к концу боя у двоих были бы разные карты.
public static class NetProps
{
    /// Номера раздаёт тот, кто считает бой. Клиент своих номеров не выдумывает:
    /// он берёт их из объявления хоста, поэтому счётчик у него просто едет
    /// следом за пришедшими номерами.
    static int _next;

    public static int NextId() => ++_next;
    public static void Reset() => _next = 0;
    public static void Seen(int id) { if (id > _next) _next = id; }

    static NetGame Net => NetGame.I;
    static NetMatch Match => Net != null ? Net.Match : null;

    /// true — на этой машине предметы не заводятся и не пропадают сами.
    /// Смотрим на роль, а не на NetSim.Authority: мир строится раньше, чем
    /// матч объявляет авторитет, а раскладка мин и бочек идёт как раз при
    /// постройке.
    public static bool Mirror => Net != null && Net.Online && !Net.IsHost;

    /// Есть кому слать: хост в бою.
    static bool Live => Net != null && Net.IsHost && Net.Phase == NetPhase.Match && Match != null;

    /// На карте появился предмет. a и b — начинка: у ящика это его род и
    /// оружие внутри, у мины и бочки они пустые.
    public static void Spawned(NetProp kind, int id, Vector2 pos, int a = 0, int b = 0)
    {
        if (Live) Match.SendPropSpawn(kind, id, pos, a, b);
    }

    /// Предмет ушёл с карты.
    public static void Gone(NetProp kind, int id, PropGone why, Vector2 pos, float radius)
    {
        if (Live) Match.SendPropGone(kind, id, why, pos, radius);
    }

    /// Показать уход предмета у клиента. Урон и воронку он получит от хоста
    /// отдельно — здесь только то, что видно и слышно.
    public static void PlayGone(PropGone why, Vector2 pos, float radius)
    {
        switch (why)
        {
            case PropGone.Taken:
                Sfx.Pickup();
                break;
            case PropGone.Blown:
                Combat.Boom(pos, radius);
                break;
            case PropGone.Sunk:
                Fx.Splash(new Vector2(pos.x, DestructibleTerrain.WaterLevel));
                Sfx.Splash();
                break;
        }
    }
}
