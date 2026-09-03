using UnityEngine;

public class Projectile : MonoBehaviour
{
    public Weapon Weapon;
    public Worm Owner;
    public int ClusterChildren;   // сколько осколков породит при взрыве

    /// Куда доворачивает самонаводящаяся ракета.
    public Vector2 HomeTarget;

    /// Куда идёт овца: 1 вправо, -1 влево.
    public int WalkDir = 1;

    Rigidbody2D _rb;
    Transform _sprite;      // крутим его, а не корень: у корня Rigidbody2D
    SpriteRenderer _sr;
    Color _baseColor;
    float _fuseLeft;
    bool _exploded;
    float _life;

    const float HomingDelay = 0.35f;   // столько ракета летит прямо, как из ствола
    const float HomingTurn = 260f;     // градусов в секунду
    const float WalkSpeed = 3.6f;

    public static Projectile Spawn(Weapon w, Vector2 pos, Vector2 velocity, Worm owner, float radiusScale = 1f, int clusterChildren = 0)
    {
        var go = new GameObject("Projectile_" + w.Name);
        GameManager.Attach(go);
        go.transform.position = pos;

        // Тому, что лежит и тикает (динамит, мина, овца), круглешок не идёт —
        // берём ту же картинку, что и в панели оружия.
        bool useIcon = w.Use == WeaponUse.Drop || w.Kind == WeaponKind.Banana;
        var sr = useIcon
            ? Sprites.Make("Body", WeaponIcons.Sprite(w.Kind), Color.white, 5, go.transform)
            : Sprites.Make("Body", Sprites.Circle, w.Color, 5, go.transform);
        if (!useIcon) sr.transform.localScale = Vector3.one * 0.55f;

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = useIcon ? 0.3f : 0.22f;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = w.Homing ? 0.25f : 1f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.linearVelocity = velocity;

        if (w.Bouncy)
        {
            var mat = new PhysicsMaterial2D("Bouncy") { bounciness = 0.45f, friction = 0.35f };
            col.sharedMaterial = mat;
            rb.angularVelocity = Random.Range(-360f, 360f);
        }
        else
        {
            // Динамит и овца должны лежать и стоять, а не скользить по склону.
            col.sharedMaterial = new PhysicsMaterial2D("Blunt") { bounciness = 0f, friction = w.Contact ? 0.4f : 0.9f };
            rb.freezeRotation = true;
        }

        var p = go.AddComponent<Projectile>();
        p.Weapon = w;
        p.Owner = owner;
        p._rb = rb;
        p._sprite = sr.transform;
        p._sr = sr;
        p._baseColor = sr.color;
        p._fuseLeft = w.Fuse;
        p.ClusterChildren = clusterChildren;
        p.transform.localScale = Vector3.one * radiusScale;

        // Не взрываемся об самого стрелка в момент выстрела.
        if (owner != null)
        {
            var oc = owner.GetComponent<Collider2D>();
            if (oc != null) Physics2D.IgnoreCollision(col, oc, true);
        }

        GameManager.I.RegisterProjectile(p);
        return p;
    }

    // Силы — только здесь: в Update они копились бы по числу кадров между шагами
    // физики, и ветер получался бы разным на 60 и 120 Гц.
    void FixedUpdate()
    {
        if (Weapon.AffectedByWind)
            _rb.AddForce(new Vector2(GameManager.I.Wind * 9f, 0f) * _rb.mass, ForceMode2D.Force);

        if (Weapon.Homing && _life > HomingDelay) Home();
        if (Weapon.Walker) Walk();
    }

    /// Доворот на цель с ограниченной угловой скоростью: ракета не разворачивается
    /// на месте, а закладывает дугу — иначе она била бы без промаха с любой точки.
    void Home()
    {
        Vector2 v = _rb.linearVelocity;
        float speed = Mathf.Max(v.magnitude, Weapon.LaunchSpeed * 0.55f);
        Vector2 want = HomeTarget - (Vector2)transform.position;
        if (want.sqrMagnitude < 0.04f) { Explode(); return; }

        float cur = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        float target = Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg;
        float a = Mathf.MoveTowardsAngle(cur, target, HomingTurn * Time.fixedDeltaTime) * Mathf.Deg2Rad;
        _rb.linearVelocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed;
    }

    static readonly RaycastHit2D[] _hits = new RaycastHit2D[8];

    /// Овца идёт вперёд и подпрыгивает, упёршись в склон.
    void Walk()
    {
        // queriesStartInColliders включён, поэтому свой же коллайдер попадает
        // в выдачу первым — как и у червя, перебираем список и пропускаем себя.
        int n = Physics2D.CircleCast(transform.position, 0.25f, Vector2.down,
                                     ContactFilter2D.noFilter, _hits, 0.22f);
        bool grounded = false;
        for (int i = 0; i < n && !grounded; i++)
            grounded = _hits[i].collider != null && _hits[i].collider.gameObject != gameObject;
        if (!grounded) return;

        var v = _rb.linearVelocity;

        if (Mathf.Abs(v.x) < WalkSpeed * 0.4f && _life > 0.3f) v.y = Mathf.Max(v.y, 6.5f);
        v.x = WalkDir * WalkSpeed;
        _rb.linearVelocity = v;
    }

    void Update()
    {
        _life += Time.deltaTime;

        // Поворот вешаем на дочерний спрайт. Запись в transform корня протолкнула бы
        // позу в тело и сбросила интерполяцию — снаряд начал бы идти шагами по 50 Гц.
        if (!Weapon.Bouncy && !Weapon.Walker && Weapon.Use != WeaponUse.Drop
            && _rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            float a = Mathf.Atan2(_rb.linearVelocity.y, _rb.linearVelocity.x) * Mathf.Rad2Deg;
            _sprite.rotation = Quaternion.Euler(0, 0, a);
        }

        if (Weapon.Walker) _sprite.localScale = new Vector3(WalkDir, 1f, 1f);

        if (Weapon.Fuse > 0f)
        {
            _fuseLeft -= Time.deltaTime;
            // Мигание перед взрывом.
            if (_fuseLeft < 1f && _sr != null)
                _sr.color = Mathf.Repeat(_fuseLeft, 0.2f) < 0.1f ? Color.white : _baseColor;
            if (_fuseLeft <= 0f) Explode();
        }

        // Улетел за пределы мира / упал в воду.
        var p = transform.position;
        if (p.y < DestructibleTerrain.WaterLevel - 1f || p.x < -20f || p.x > DestructibleTerrain.WorldWidth + 20f || _life > 20f)
        {
            if (p.y < DestructibleTerrain.WaterLevel && p.y > DestructibleTerrain.WaterLevel - 1.5f)
            {
                Fx.Splash(new Vector2(p.x, DestructibleTerrain.WaterLevel));
                Sfx.Splash();
            }
            Vanish();
        }
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (_exploded) return;

        // Овца рвётся, боднув червя, а от земли просто отталкивается.
        if (Weapon.Walker)
        {
            if (c.collider.GetComponent<Worm>() != null) Explode();
            return;
        }

        if (!Weapon.Contact)
        {
            if (c.relativeVelocity.sqrMagnitude > 9f) Sfx.Bounce();
            return;
        }

        if (Weapon.Bouncy)
        {
            Fx.Splash(transform.position, Weapon.Color, 4, 0.15f);
            // Тихие касания на излёте не озвучиваем — иначе граната тарахтит,
            // пока не докатится.
            if (c.relativeVelocity.sqrMagnitude > 4f) Sfx.Bounce();
            return;
        }

        // Ракета взрывается при любом контакте.
        Explode();
    }

    public void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        Vector2 pos = transform.position;
        Combat.Detonate(pos, Weapon.BlastRadius * transform.localScale.x, Weapon.Damage);

        if (ClusterChildren > 0) SpawnCluster(pos);

        Vanish();
    }

    /// Осколки: у кассеты они сыплются веером вверх, у миномёта бьют почти
    /// отвесно вниз, у банана разлетаются широко и подпрыгивают.
    void SpawnCluster(Vector2 pos)
    {
        bool banana = Weapon.Kind == WeaponKind.Banana;
        bool mortar = Weapon.Kind == WeaponKind.Mortar;

        var child = new Weapon
        {
            Kind = WeaponKind.Grenade,
            Name = banana ? "Долька" : "Осколок",
            BlastRadius = banana ? 2.4f : mortar ? 1.8f : 1.8f,
            Damage = banana ? 28f : mortar ? 18f : 22f,
            Fuse = banana ? 1.6f : mortar ? 0f : 1.2f,
            Bouncy = !mortar,
            Contact = mortar,
            Color = banana ? new Color(0.98f, 0.85f, 0.25f) : new Color(1f, 0.85f, 0.35f)
        };

        for (int i = 0; i < ClusterChildren; i++)
        {
            float span = banana ? 140f : mortar ? 40f : 60f;
            float mid = mortar ? -90f : 90f;
            float ang = mid - span * 0.5f + span / Mathf.Max(1, ClusterChildren - 1) * i + Random.Range(-8f, 8f);
            var dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            float speed = mortar ? Random.Range(9f, 12f) : Random.Range(7f, 11f);
            Spawn(child, pos + dir * 0.6f, dir * speed, null, 0.8f);
        }
    }

    void Vanish()
    {
        GameManager.I.UnregisterProjectile(this);
        Destroy(gameObject);
    }
}
