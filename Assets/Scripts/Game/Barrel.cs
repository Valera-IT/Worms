using System.Collections.Generic;
using UnityEngine;

/// Бочка с нефтью: стоит на карте с начала боя и рвётся от попадания.
/// Смысл её не в уроне, а в том, что карта перестаёт быть нейтральной:
/// позиция у бочки выгодна ровно до первого выстрела в её сторону.
///
/// В отличие от ящика бочка не детонирует мгновенно — сначала занимается
/// огнём и полсекунды горит. Этой паузы хватает, чтобы отскочить, и она же
/// разводит цепочку бочек во времени: они рвутся волной, а не одним хлопком.
public class Barrel : MonoBehaviour
{
    /// Живые бочки: по ним ходит взрыв, поэтому список, а не поиск по сцене.
    public static readonly List<Barrel> All = new List<Barrel>();

    public const float BlastRadius = 4.2f;
    public const float BlastDamage = 55f;

    /// Сколько урона держит железо. Пуля узи (5) бочку не вскрывает, очередь
    /// и любой взрыв — вскрывают.
    const float Armor = 18f;
    const float Fuse = 0.55f;

    static readonly Color Iron = new Color(0.55f, 0.32f, 0.16f);
    static readonly Color Band = new Color(0.30f, 0.18f, 0.09f);

    /// Номер бочки в сетевом бою: по нему хост объявляет, что рванула именно
    /// эта. В одиночном бою номер не значит ничего.
    public int NetId { get; private set; }

    float _armor = Armor;
    bool _burning;
    bool _spent;
    float _fuse;
    int _gen;
    SpriteRenderer _body;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public static void Forget() => All.Clear();

    /// Бочка по сетевому номеру.
    public static Barrel Find(int id)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].NetId == id) return All[i];
        return null;
    }

    public static Barrel Place(Vector2 pos)
    {
        // У сетевого клиента бочки не расставляются: их место и число решает
        // хост, он же их и объявляет.
        if (NetProps.Mirror) return null;

        // Чуть приподнимаем: точка раскладки лежит на поверхности, и бочка
        // родилась бы наполовину в породе.
        var b = Make(NetProps.NextId(), new Vector2(pos.x, pos.y + 0.35f));
        NetProps.Spawned(NetProp.Barrel, b.NetId, b.transform.position);
        return b;
    }

    /// Бочка, объявленная хостом: место и номер назначил он, а гореть и
    /// рваться она сама не станет.
    public static Barrel Net(int id, Vector2 pos)
    {
        var b = Make(id, pos);
        NetProps.Seen(id);
        return b;
    }

    static Barrel Make(int id, Vector2 pos)
    {
        var go = new GameObject("Barrel");
        GameManager.Attach(go);
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        var b = go.AddComponent<Barrel>();
        b.NetId = id;
        b._gen = GameManager.I != null ? GameManager.I.Generation : 0;
        b.Build();
        return b;
    }

    void Build()
    {
        _body = Sprites.Make("Iron", Sprites.Square, Iron, 9, transform);
        _body.transform.localScale = new Vector3(0.72f, 1.05f, 1f);

        // Два обруча поперёк — по ним бочка узнаётся с любого зума.
        for (int i = 0; i < 2; i++)
        {
            var band = Sprites.Make("Band", Sprites.Square, Band, 10, transform);
            band.transform.localPosition = new Vector3(0f, i == 0 ? 0.28f : -0.28f, 0f);
            band.transform.localScale = new Vector3(0.76f, 0.12f, 1f);
        }

        var col = gameObject.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.72f, 1.05f);

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Update()
    {
        if (GameManager.I == null || GameManager.I.Generation != _gen) return;
        // У клиента бочка не горит и не тонет сама: фитиль зажигает хост,
        // он же объявляет и взрыв.
        if (NetProps.Mirror) return;

        if (transform.position.y < DestructibleTerrain.WaterLevel)
        {
            Fx.Splash(new Vector2(transform.position.x, DestructibleTerrain.WaterLevel));
            Sfx.Splash();
            NetProps.Gone(NetProp.Barrel, NetId, PropGone.Sunk, transform.position, 0f);
            All.Remove(this);
            Destroy(gameObject);
            return;
        }

        if (!_burning) return;

        _fuse -= Time.deltaTime;
        // Пока горит — мигает жёлтым и дымит: видно и того, кто рядом, и того,
        // кто целился в соседнюю бочку.
        _body.color = Mathf.Repeat(_fuse, 0.14f) < 0.07f ? new Color(1f, 0.75f, 0.25f) : Iron;
        if (_fuse <= 0f) Blow();
    }

    /// Попадание. Взрыв рядом отдаёт бочке столько же, сколько отдал бы червю,
    /// поэтому железо считается тем же уроном, а не отдельным «задело или нет».
    public void Hit(float damage)
    {
        if (_spent || _burning || NetProps.Mirror) return;
        _armor -= damage;
        if (_armor > 0f)
        {
            Fx.Sparks(transform.position, Vector2.up, 4, 5f);
            return;
        }
        Ignite();
    }

    void Ignite()
    {
        if (_spent || _burning) return;
        _burning = true;
        _fuse = Fuse;
        Fx.Smoke(transform.position + Vector3.up * 0.6f, 0.5f);
    }

    void Blow()
    {
        if (_spent) return;
        _spent = true;

        Vector2 pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);

        // Нефть: к обычному взрыву добавляем брызги пламени — по ним видно,
        // что рвануло не железо, а то, что внутри.
        NetProps.Gone(NetProp.Barrel, NetId, PropGone.Blown, pos, BlastRadius);
        Combat.Detonate(pos, BlastRadius, BlastDamage);
        Oil(pos);
    }

    /// Нефть на месте взрыва: брызги пламени и дым. У клиента взрыв ставит
    /// объявление хоста, но нефть в нём та же самая.
    static void Oil(Vector2 pos)
    {
        Fx.Splash(pos, new Color(1f, 0.6f, 0.15f), 16, 0.35f);
        Fx.Smoke(pos, 1.6f);
    }

    /// Убрать бочку по слову хоста: воронку и урон клиент получит отдельно.
    public void NetRemove(PropGone why, float radius)
    {
        if (_spent) return;
        _spent = true;
        Vector2 pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);
        NetProps.PlayGone(why, pos, radius);
        if (why == PropGone.Blown) Oil(pos);
    }

    /// Тихо убрать по сверке.
    public void NetVanish()
    {
        _spent = true;
        All.Remove(this);
        Destroy(gameObject);
    }
}
