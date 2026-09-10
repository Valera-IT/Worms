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

    public static Mine Drop(Weapon w, Vector2 pos)
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

        // Мина в воде бесполезна и невидима — топим её, как ящик.
        if (transform.position.y < DestructibleTerrain.WaterLevel)
        {
            Fx.Splash(new Vector2(transform.position.x, DestructibleTerrain.WaterLevel));
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
        if (_spent) return;
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
        Combat.Detonate(pos, _weapon.BlastRadius, _weapon.Damage);
    }
}
