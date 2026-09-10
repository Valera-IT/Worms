using System.Collections.Generic;
using UnityEngine;

/// Что внутри ящика. Оружейный и утилитный разведены с шага 14b: в оригинале
/// это разные ящики и разная находка — ствол на ход или верёвка на побег.
public enum CrateKind { Ammo, Health, Utility }

/// Ящик с припасами: в начале хода падает с неба на парашюте, подбирается
/// касанием любого живого червя, а от близкого взрыва детонирует сам —
/// поэтому стрелять по ящику рядом с врагом выгодно, а рядом с собой нет.
public class Crate : MonoBehaviour
{
    /// Живые ящики: по ним ходит взрыв, поэтому список, а не поиск по сцене.
    public static readonly List<Crate> All = new List<Crate>();

    public const float BlastRadius = 2.6f;
    public const float BlastDamage = 35f;

    public CrateKind Kind { get; private set; }

    /// Номер ящика в сетевом бою: по нему хост объявляет, что этот самый ящик
    /// подобрали или взорвали. В одиночном бою номер не значит ничего.
    public int NetId { get; private set; }

    /// Что именно внутри: для Ammo — какое оружие, для Health — сколько здоровья.
    public WeaponKind AmmoKind { get; private set; }
    public int AmmoAmount { get; private set; } = 2;
    public float HealAmount { get; private set; } = 25f;

    /// Ящик уже на земле: в сверке это едет вместе с местом, иначе у
    /// вошедшего посреди боя лежащий ящик снова висел бы на парашюте.
    public bool Landed => _landed;

    Rigidbody2D _rb;
    Transform _canopy;
    bool _landed;
    bool _taken;
    int _gen;

    static readonly Color AmmoColor = new Color(0.80f, 0.62f, 0.28f);
    static readonly Color HealthColor = new Color(0.92f, 0.95f, 0.96f);
    static readonly Color UtilityColor = new Color(0.38f, 0.55f, 0.72f);

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// Забыть всё разом — при пересборке мира объекты умирают отложенно,
    /// а список нужен чистым уже сейчас.
    public static void Forget() => All.Clear();

    /// Ящик по сетевому номеру. Ящиков на карте не больше четырёх, поэтому
    /// перебор дешевле словаря, который пришлось бы чистить.
    public static Crate Find(int id)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].NetId == id) return All[i];
        return null;
    }

    // --- появление ---------------------------------------------------------

    /// Ящик на случайной пригодной колонке. null, если садиться некуда.
    public static Crate DropRandom(DestructibleTerrain terrain)
    {
        if (terrain == null) return null;

        for (int tries = 0; tries < 40; tries++)
        {
            float x = Random.Range(6f, DestructibleTerrain.WorldWidth - 6f);
            if (!terrain.FindSpawnPoint(x, out _)) continue;
            return Drop(RollKind(), x);
        }
        return null;
    }

    /// Кубик содержимого: чаще всего оружие, треть — аптечка, пятая часть —
    /// снаряжение. Утилитный ящик реже аптечки нарочно: верёвка и телепорт
    /// решают позицию сильнее, чем двадцать пять очков здоровья.
    static CrateKind RollKind()
    {
        float r = Random.value;
        if (r < 0.30f) return CrateKind.Health;
        if (r < 0.50f) return CrateKind.Utility;
        return CrateKind.Ammo;
    }

    /// Набор, из которого ящик берёт содержимое. Бесконечное оружие в ящик
    /// не кладём — подарок должен что-то значить, — а утилитный ящик и
    /// оружейный делят арсенал по флагу Utility, а не по списку имён.
    static List<WeaponKind> Pool(bool utility)
    {
        var pool = new List<WeaponKind>();
        foreach (var w in Weapon.All)
            if (w.Ammo > 0 && w.Utility == utility) pool.Add(w.Kind);
        return pool;
    }

    public static Crate Drop(CrateKind kind, float x)
    {
        // У сетевого клиента ящики не заводятся сами: и место, и начинку
        // бросает кубик, а два кубика в разных процессах — это два разных
        // ящика. Свой придёт объявлением от хоста.
        if (NetProps.Mirror) return null;

        var ammo = default(WeaponKind);
        if (kind != CrateKind.Health)
        {
            var pool = Pool(kind == CrateKind.Utility);
            // Пустой набор возможен только при безлимитном боезапасе: тогда
            // ящик всё равно отдаст здоровьем, а метка нужна хоть какая-то.
            ammo = pool.Count > 0
                ? pool[Random.Range(0, pool.Count)]
                : (kind == CrateKind.Utility ? WeaponKind.Rope : WeaponKind.Cluster);
        }

        var c = Make(NetProps.NextId(), kind, ammo, new Vector2(x, DropHeight(x)));
        Sfx.CrateDrop();
        NetProps.Spawned(NetProp.Crate, c.NetId, c.transform.position, (int)kind, (int)ammo);
        return c;
    }

    /// Ящик, объявленный хостом: место, номер и начинку назначил он, кубик
    /// здесь не бросается вовсе. landed — ящик уже на земле: так приезжают
    /// ящики в сверке, а свежесброшенный ещё висит на парашюте.
    public static Crate Net(int id, CrateKind kind, WeaponKind ammo, Vector2 pos, bool landed)
    {
        var c = Make(id, kind, ammo, pos);
        NetProps.Seen(id);
        if (landed) c.Touchdown();
        else Sfx.CrateDrop();
        return c;
    }

    static Crate Make(int id, CrateKind kind, WeaponKind ammo, Vector2 pos)
    {
        var go = new GameObject("Crate");
        GameManager.Attach(go);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        var c = go.AddComponent<Crate>();
        c.NetId = id;
        c.Kind = kind;
        c.AmmoKind = ammo;
        c._gen = GameManager.I != null ? GameManager.I.Generation : 0;
        c.Build();
        return c;
    }

    /// Откуда падать. Просто «с верхней кромки карты» не годится: в пещере
    /// там потолок, и ящик родился бы внутри породы. Берём точку над
    /// поверхностью и опускаем её, пока над головой камень.
    static float DropHeight(float x)
    {
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        float top = DestructibleTerrain.WorldHeight - 2f;
        if (terrain == null) return top;

        float ground = terrain.SurfaceHeightWorld(x);
        if (ground < 0f) return top;

        float y = Mathf.Min(top, ground + 7f);
        while (y > ground + 1.2f &&
               (terrain.IsSolidWorld(new Vector2(x, y)) || terrain.IsSolidWorld(new Vector2(x, y + 0.6f))))
            y -= 0.4f;
        return y;
    }

    Color BodyColor() => Kind switch
    {
        CrateKind.Health => HealthColor,
        CrateKind.Utility => UtilityColor,
        _ => AmmoColor
    };

    void Build()
    {
        var body = Sprites.Make("Box", Sprites.Square, BodyColor(), 9, transform);
        body.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        // Метка содержимого: у аптечки крест, у снаряжения — косая скоба,
        // у боеприпаса — полоса цвета оружия.
        if (Kind == CrateKind.Health)
        {
            var v = Sprites.Make("CrossV", Sprites.Square, new Color(0.85f, 0.2f, 0.2f), 10, transform);
            v.transform.localScale = new Vector3(0.22f, 0.6f, 1f);
            var h = Sprites.Make("CrossH", Sprites.Square, new Color(0.85f, 0.2f, 0.2f), 10, transform);
            h.transform.localScale = new Vector3(0.6f, 0.22f, 1f);
        }
        else if (Kind == CrateKind.Utility)
        {
            // Скоба: косая планка с утолщением на конце — ключ, а не полоса,
            // чтобы утилитный ящик не путался с оружейным на пол-экрана.
            var bar = Sprites.Make("Hook", Sprites.Square, Weapon.All[Weapon.IndexOf(AmmoKind)].Color, 10, transform);
            bar.transform.localScale = new Vector3(0.6f, 0.16f, 1f);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);
            var head = Sprites.Make("Head", Sprites.Circle, Weapon.All[Weapon.IndexOf(AmmoKind)].Color, 10, transform);
            head.transform.localPosition = new Vector3(-0.2f, -0.16f, 0f);
            head.transform.localScale = Vector3.one * 0.28f;
        }
        else
        {
            var stripe = Sprites.Make("Stripe", Sprites.Square, Weapon.All[Weapon.IndexOf(AmmoKind)].Color, 10, transform);
            stripe.transform.localScale = new Vector3(0.62f, 0.24f, 1f);
        }

        var canopy = Sprites.Make("Canopy", Sprites.Square, new Color(0.95f, 0.95f, 0.98f, 0.9f), 8, transform);
        canopy.transform.localPosition = new Vector3(0f, 0.85f, 0f);
        canopy.transform.localScale = new Vector3(1.7f, 0.32f, 1f);
        _canopy = canopy.transform;

        var col = gameObject.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.9f, 0.9f);

        _rb = gameObject.AddComponent<Rigidbody2D>();
        _rb.freezeRotation = true;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        // Парашют: почти невесомое падение, пока ящик не коснётся земли.
        _rb.gravityScale = 0.3f;
        _rb.linearDamping = 1.4f;
    }

    // --- жизнь -------------------------------------------------------------

    void Update()
    {
        if (GameManager.I == null || GameManager.I.Generation != _gen) return;

        // Утонуть ящик может только у того, кто считает бой: у клиента он
        // ждёт объявления, иначе один ящик утонул бы дважды по разным часам.
        if (NetProps.Mirror) return;

        if (transform.position.y < DestructibleTerrain.WaterLevel)
        {
            Fx.Splash(new Vector2(transform.position.x, DestructibleTerrain.WaterLevel));
            Sfx.Splash();
            NetProps.Gone(NetProp.Crate, NetId, PropGone.Sunk, transform.position, 0f);
            All.Remove(this);
            Destroy(gameObject);
        }
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        var worm = c.collider.GetComponent<Worm>();
        if (worm != null) { Take(worm); return; }

        Touchdown();
    }

    /// Приземление: парашют отстёгивается, ящик становится обычным телом.
    void Touchdown()
    {
        if (_landed) return;
        _landed = true;
        if (_rb != null)
        {
            _rb.gravityScale = 1f;
            _rb.linearDamping = 0f;
        }
        if (_canopy != null) Destroy(_canopy.gameObject);
    }

    /// Подбор. Берёт любой живой червь, а не только тот, чей ход, — так ящик
    /// сам по себе становится поводом сходить в опасное место.
    void Take(Worm worm)
    {
        if (_taken || worm == null || worm.IsDead) return;
        // Подбор — это здоровье и патроны, то есть состояние боя. Клиент его
        // не решает: хост объявит подобранный ящик, и тогда он исчезнет.
        if (NetProps.Mirror) return;
        _taken = true;

        if (Kind == CrateKind.Health || worm.Team == null)
        {
            worm.Heal(HealAmount);
            Fx.FloatingText(transform.position + Vector3.up * 0.8f,
                "+" + Mathf.RoundToInt(HealAmount), new Color(0.5f, 1f, 0.6f));
        }
        else
        {
            var team = worm.Team;
            var weapon = Weapon.All[Weapon.IndexOf(AmmoKind)];
            // При безлимитном боезапасе патроны бессмысленны — отдаём здоровьем.
            if (team.Ammo.TryGetValue(AmmoKind, out int have) && have >= 0)
            {
                team.Ammo[AmmoKind] = have + AmmoAmount;
                Fx.FloatingText(transform.position + Vector3.up * 0.8f,
                    weapon.Name + " +" + AmmoAmount, weapon.Color);
            }
            else
            {
                worm.Heal(HealAmount);
                Fx.FloatingText(transform.position + Vector3.up * 0.8f,
                    "+" + Mathf.RoundToInt(HealAmount), new Color(0.5f, 1f, 0.6f));
            }
        }

        Sfx.Pickup();
        NetProps.Gone(NetProp.Crate, NetId, PropGone.Taken, transform.position, 0f);
        All.Remove(this);
        Destroy(gameObject);
    }

    /// Детонация от чужого взрыва. Отдельный флаг нужен, чтобы цепочка ящиков
    /// сошлась: каждый рвётся ровно один раз.
    public void Blow()
    {
        if (_taken) return;
        // Цепочка у клиента идёт своим счётом и разойдётся с хостовой: у него
        // ящик рвёт только объявление хоста.
        if (NetProps.Mirror) return;
        _taken = true;

        Vector2 pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);
        NetProps.Gone(NetProp.Crate, NetId, PropGone.Blown, pos, BlastRadius);
        Combat.Detonate(pos, BlastRadius, BlastDamage);
    }

    /// Убрать ящик по слову хоста. Урон и воронку клиент получит отдельно —
    /// здесь только сам ящик и то, что видно на его месте.
    public void NetRemove(PropGone why, float radius)
    {
        if (_taken) return;
        _taken = true;
        Vector2 pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);
        NetProps.PlayGone(why, pos, radius);
    }

    /// Тихо убрать по сверке: показывать нечего — либо всё уже показано
    /// объявлением, либо этого ящика не стало, пока нас не было.
    public void NetVanish()
    {
        _taken = true;
        All.Remove(this);
        Destroy(gameObject);
    }
}
