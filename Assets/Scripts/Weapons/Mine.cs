using System.Collections.Generic;
using UnityEngine;

/// Мина: лежит на карте между ходами и рвётся, когда рядом проходит червь.
/// В отличие от снаряда, она не держит ход — иначе матч встал бы навсегда.
/// Мины бывают двух родов: заложенные червём (или минным ударом) и «дикие» —
/// разбросанные по карте перед началом боя, шагом 14b.
public class Mine : MonoBehaviour
{
    /// Живые мины: по ним ходит взрыв, поэтому список, а не поиск по сцене.
    public static readonly List<Mine> All = new List<Mine>();

    /// Номер мины в сетевом бою: по нему хост объявляет, что рванула именно
    /// эта. В одиночном бою номер не значит ничего.
    public int NetId { get; private set; }

    Weapon _weapon;
    SpriteRenderer _sr;
    float _arm = 3f;          // три секунды взвода: столько свой червь бежит прочь
    float _countdown = -1f;   // пошёл отсчёт после того, как её задели
    bool _spent;              // рвётся ровно один раз, даже в цепочке

    const float Trigger = 1.6f;
    const float Delay = 0.9f;
    /// Задетая взрывом мина рвётся почти сразу: это цепь, а не собственный отсчёт.
    const float ChainDelay = 0.25f;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// Забыть всё разом — при пересборке мира объекты умирают отложенно,
    /// а список нужен чистым уже сейчас.
    public static void Forget() => All.Clear();

    /// Мина по сетевому номеру.
    public static Mine Find(int id)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].NetId == id) return All[i];
        return null;
    }

    public static Mine Drop(Weapon w, Vector2 pos)
    {
        // У сетевого клиента мина не закладывается: его снаряд — картинка,
        // а мина остаётся на карте и решает урон. Свою он получит объявлением.
        if (NetProps.Mirror) return null;

        var m = Make(NetProps.NextId(), w, pos);
        NetProps.Spawned(NetProp.Mine, m.NetId, pos);
        return m;
    }

    /// Мина, объявленная хостом: своей жизни у неё нет — ни взвода, ни
    /// отсчёта, — она ждёт слова хоста и до тех пор просто лежит.
    public static Mine Net(int id, Vector2 pos)
    {
        var m = Make(id, WildPayload(), pos);
        NetProps.Seen(id);
        return m;
    }

    static Mine Make(int id, Weapon w, Vector2 pos)
    {
        var go = new GameObject("Mine");
        GameManager.Attach(go);
        go.transform.position = pos;

        var sr = Sprites.Make("Body", WeaponIcons.Sprite(w.Kind), Color.white, 6, go.transform);
        sr.transform.localScale = Vector3.one * 0.7f;

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.22f;
        col.sharedMaterial = new PhysicsMaterial2D("MineMat") { bounciness = 0f, friction = 0.9f };

        var rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var m = go.AddComponent<Mine>();
        m.NetId = id;
        m._weapon = w;
        m._sr = sr;
        return m;
    }

    /// Дикая мина: та же мина, но ничья и взведённая почти сразу — она лежала
    /// здесь до боя, а не была заложена в этом ходу. Ставится только там, где
    /// на неё не наступят на первом же шаге, — расстановкой ведает GameManager.
    public static Mine Scatter(Vector2 pos)
    {
        var m = Drop(WildPayload(), pos);
        if (m == null) return null;
        m._arm = 0.6f;
        m.name = "WildMine";
        return m;
    }

    /// Начинка дикой мины: слабее заложенной — она достаётся даром и лежит
    /// там, куда червя загоняет карта, а не расчёт противника.
    static Weapon WildPayload() => new Weapon
    {
        Kind = WeaponKind.Mine,
        Name = "Мина",
        Use = WeaponUse.Drop,
        BlastRadius = 2.0f,
        Damage = 28f,
        Contact = false,
        Color = new Color(0.5f, 0.5f, 0.55f),
        Ammo = 0
    };

    void Update()
    {
        if (GameManager.I == null) return;
        // У клиента мина не живёт своей жизнью: ни взвода, ни отсчёта, ни
        // воды. Всё, что с ней случится, объявит хост.
        if (NetProps.Mirror) return;

        // Мина в воде бесполезна и невидима — топим её, как ящик.
        if (transform.position.y < DestructibleTerrain.WaterLevel)
        {
            Fx.Splash(new Vector2(transform.position.x, DestructibleTerrain.WaterLevel));
            NetProps.Gone(NetProp.Mine, NetId, PropGone.Sunk, transform.position, 0f);
            All.Remove(this);
            Destroy(gameObject);
            return;
        }

        if (_arm > 0f) { _arm -= Time.deltaTime; return; }

        if (_countdown < 0f)
        {
            if (!SomeoneClose()) return;
            _countdown = Delay;
            Sfx.Bounce();
        }

        _countdown -= Time.deltaTime;
        _sr.color = Mathf.Repeat(_countdown, 0.16f) < 0.08f ? Color.red : Color.white;
        if (_countdown <= 0f) Blow();
    }

    bool SomeoneClose()
    {
        var worms = GameManager.I.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead) continue;
            if (Vector2.Distance(w.transform.position, transform.position) <= Trigger) return true;
        }
        return false;
    }

    /// Мину задело чужим взрывом. Своего Detonate отсюда не зовём: цепь из
    /// десятка мин ушла бы в рекурсию на десять взрывов в одном кадре.
    /// Вместо этого запускаем короткий отсчёт — цепочка идёт волной.
    public void Chain()
    {
        if (_spent || NetProps.Mirror) return;
        _arm = 0f;
        if (_countdown < 0f || _countdown > ChainDelay) _countdown = ChainDelay;
    }

    void Blow()
    {
        if (_spent) return;
        _spent = true;

        var pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);
        NetProps.Gone(NetProp.Mine, NetId, PropGone.Blown, pos, _weapon.BlastRadius);
        Combat.Detonate(pos, _weapon.BlastRadius, _weapon.Damage);
    }

    /// Убрать мину по слову хоста: воронку и урон клиент получит отдельно.
    public void NetRemove(PropGone why, float radius)
    {
        if (_spent) return;
        _spent = true;
        Vector2 pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);
        NetProps.PlayGone(why, pos, radius);
    }

    /// Тихо убрать по сверке.
    public void NetVanish()
    {
        _spent = true;
        All.Remove(this);
        Destroy(gameObject);
    }
}
