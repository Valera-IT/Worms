using System.Collections.Generic;
using UnityEngine;

public enum CrateKind { Ammo, Health }

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

    /// Что именно внутри: для Ammo — какое оружие, для Health — сколько здоровья.
    public WeaponKind AmmoKind { get; private set; }
    public int AmmoAmount { get; private set; } = 2;
    public float HealAmount { get; private set; } = 25f;

    Rigidbody2D _rb;
    Transform _canopy;
    bool _landed;
    bool _taken;
    int _gen;

    static readonly Color AmmoColor = new Color(0.80f, 0.62f, 0.28f);
    static readonly Color HealthColor = new Color(0.92f, 0.95f, 0.96f);

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// Забыть всё разом — при пересборке мира объекты умирают отложенно,
    /// а список нужен чистым уже сейчас.
    public static void Forget() => All.Clear();

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

    static CrateKind RollKind() => Random.value < 0.35f ? CrateKind.Health : CrateKind.Ammo;

    public static Crate Drop(CrateKind kind, float x)
    {
        var go = new GameObject("Crate");
        GameManager.Attach(go);
        go.transform.position = new Vector3(x, DropHeight(x), 0f);

        var c = go.AddComponent<Crate>();
        c.Kind = kind;
        c._gen = GameManager.I != null ? GameManager.I.Generation : 0;

        if (kind == CrateKind.Ammo)
        {
            // Бесконечное оружие в ящик не кладём — подарок должен что-то значить.
            var pool = new List<WeaponKind>();
            foreach (var w in Weapon.All)
                if (w.Ammo > 0) pool.Add(w.Kind);
            c.AmmoKind = pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : WeaponKind.Cluster;
        }

        c.Build();
        Sfx.CrateDrop();
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

    void Build()
    {
        var body = Sprites.Make("Box", Sprites.Square, Kind == CrateKind.Ammo ? AmmoColor : HealthColor, 9, transform);
        body.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        // Метка содержимого: у аптечки крест, у боеприпаса — полоса цвета оружия.
        if (Kind == CrateKind.Health)
        {
            var v = Sprites.Make("CrossV", Sprites.Square, new Color(0.85f, 0.2f, 0.2f), 10, transform);
            v.transform.localScale = new Vector3(0.22f, 0.6f, 1f);
            var h = Sprites.Make("CrossH", Sprites.Square, new Color(0.85f, 0.2f, 0.2f), 10, transform);
            h.transform.localScale = new Vector3(0.6f, 0.22f, 1f);
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

        if (transform.position.y < DestructibleTerrain.WaterLevel)
        {
            Fx.Splash(new Vector2(transform.position.x, DestructibleTerrain.WaterLevel));
            Sfx.Splash();
            Destroy(gameObject);
        }
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        var worm = c.collider.GetComponent<Worm>();
        if (worm != null) { Take(worm); return; }

        if (_landed) return;
        _landed = true;
        _rb.gravityScale = 1f;
        _rb.linearDamping = 0f;
        if (_canopy != null) Destroy(_canopy.gameObject);
    }

    /// Подбор. Берёт любой живой червь, а не только тот, чей ход, — так ящик
    /// сам по себе становится поводом сходить в опасное место.
    void Take(Worm worm)
    {
        if (_taken || worm == null || worm.IsDead) return;
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
        Destroy(gameObject);
    }

    /// Детонация от чужого взрыва. Отдельный флаг нужен, чтобы цепочка ящиков
    /// сошлась: каждый рвётся ровно один раз.
    public void Blow()
    {
        if (_taken) return;
        _taken = true;

        Vector2 pos = transform.position;
        All.Remove(this);
        Destroy(gameObject);
        Combat.Detonate(pos, BlastRadius, BlastDamage);
    }
}
