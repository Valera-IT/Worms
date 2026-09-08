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
    /// Сколько секунд работает двигатель после его запуска. Дальше ракета —
    /// обычная болванка: доворачивать нечем, и гравитация возвращается к полной.
    /// Отсюда и способ уйти от ракеты — увести её за гребень и переждать.
    const float HomingFuel = 3.4f;
    /// След кладём по пройденному пути, а не по времени: на полной силе базука
    /// за кадр уходит дальше, чем на четверти, и клубки по таймеру рассыпались
    /// бы пунктиром тем реже, чем быстрее летит ракета.
    Vector2 _lastPuff;
    bool _puffed;
    const float PuffStep = 0.28f;      // расстояние между клубками, юнитов
    const float WalkSpeed = 3.6f;

    /// Супер-овца в полёте: ход вперёд, рывок вверх на отрыве и размах волны,
    /// которой она идёт дальше. Волна симметрична — овца не набирает высоту
    /// без конца, а держит ту, что взяла рывком, и на ней пересекает карту.
    public const float FlySpeed = 9f;
    public const float FlyClimb = 11f;
    public const float FlyClimbTime = 1.2f;
    public const float FlyWave = 4.5f;
    public const float FlyWaveRate = 4.5f;
    /// Сколько воронок осталось пробить бетонному ослу.
    int _punchesLeft;
    bool _aloft;

    public static Projectile Spawn(Weapon w, Vector2 pos, Vector2 velocity, Worm owner, float radiusScale = 1f, int clusterChildren = 0)
    {
        var go = new GameObject("Projectile_" + w.Name);
        GameManager.Attach(go);
        go.transform.position = pos;

        // Тому, что лежит и тикает (динамит, мина, овца), круглешок не идёт —
        // берём ту же картинку, что и в панели оружия.
        bool useIcon = w.Use == WeaponUse.Drop || w.Kind == WeaponKind.Banana
                    || w.Kind == WeaponKind.HolyGrenade || w.Kind == WeaponKind.Anvil
                    || w.Kind == WeaponKind.MineStrike || w.Kind == WeaponKind.Donkey;
        bool rocket = w.Homing || w.Rocket;
        var sr = rocket
            ? Sprites.Make("Body", WeaponIcons.Missile, Color.white, 5, go.transform)
            : useIcon
            ? Sprites.Make("Body", WeaponIcons.Sprite(w.Kind), Color.white, 5, go.transform)
            : Sprites.Make("Body", Sprites.Circle, w.Color, 5, go.transform);
        if (!useIcon && !rocket) sr.transform.localScale = Vector3.one * 0.55f;

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
        p._punchesLeft = w.Punches;
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

        if (Weapon.Homing) Thrust();
        if (Weapon.Walker) { if (Aloft) Fly(); else Walk(); }
    }

    /// Три фазы полёта ракеты: пуск по стволу, работа двигателя с доворотом,
    /// свободное падение на остатках скорости.
    void Thrust()
    {
        if (_life <= HomingDelay) return;

        if (_life < HomingDelay + HomingFuel) { Home(); return; }

        // Топливо кончилось: вес возвращается, дальше ракета просто летит.
        if (_rb.gravityScale < 1f) _rb.gravityScale = 1f;
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

    /// Супер-овца оторвалась от земли. Отрыв считаем по времени с рождения:
    /// столько же считает и бот (BotPlanner.SheepBoom), а любая другая мера —
    /// скажем, «после прыжка» — у него бы не сошлась.
    bool Aloft => Weapon.LiftAfter > 0f && _life > Weapon.LiftAfter;

    /// Полёт супер-овцы: ровный ход вперёд и волна по высоте. Гравитацию
    /// снимаем — иначе волна на спуске переходила бы в падение, и овца
    /// втыкалась бы в первый же бугор вместо того, чтобы идти над ним.
    void Fly()
    {
        if (!_aloft)
        {
            _aloft = true;
            _rb.gravityScale = 0f;
            Sfx.Shot();
        }
        _rb.linearVelocity = new Vector2(WalkDir * FlySpeed, FlyRise(_life - Weapon.LiftAfter));
    }

    /// Вертикальная скорость овцы через t секунд после отрыва. Вынесена в
    /// статику: тем же выражением бот считает её полёт (BotPlanner.SheepBoom),
    /// и разъехаться этим двум формулам нельзя.
    public static float FlyRise(float t)
        => Mathf.Max(0f, 1f - t / FlyClimbTime) * FlyClimb + Mathf.Sin(t * FlyWaveRate) * FlyWave;

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

        Trail();

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

    /// Дымный след. У самонаводящейся он ещё и показания приборов: дым идёт,
    /// пока работает двигатель, кончился дым — ракета больше не доворачивает.
    /// У базуки двигатель горит весь полёт, поэтому и след тянется до самого
    /// взрыва. Клубки кладём в хвост, а не в центр, — иначе они лезут ракете
    /// на нос.
    void Trail()
    {
        bool on = Weapon.Homing
                ? _life > HomingDelay && _life < HomingDelay + HomingFuel
                : Weapon.Rocket || _aloft;
        if (!on || _exploded) return;

        Vector2 pos = transform.position;
        if (!_puffed) { _puffed = true; _lastPuff = pos; }

        Vector2 v = _rb.linearVelocity;
        Vector2 back = v.sqrMagnitude > 0.01f ? -v.normalized : Vector2.zero;
        Vector2 side = new Vector2(-back.y, back.x);

        // Догоняем ракету шагами по PuffStep: за один кадр она проходит и по
        // полтора юнита, и один клубок на кадр оставил бы в следе дыры.
        int guard = 0;
        while ((pos - _lastPuff).sqrMagnitude >= PuffStep * PuffStep && guard++ < 12)
        {
            Vector2 step = (pos - _lastPuff).normalized * PuffStep;
            _lastPuff += step;
            Fx.Smoke(_lastPuff + back * 0.3f + side * Random.Range(-0.07f, 0.07f),
                     Random.Range(0.34f, 0.5f));
        }
    }

    void OnCollisionEnter2D(Collision2D c)
    {
        if (_exploded) return;

        // Овца рвётся, боднув червя, а от земли просто отталкивается. Та же
        // овца в воздухе рвётся обо всё подряд: она уже не идёт по карте,
        // а летит в цель, и отскакивать ей нечем.
        if (Weapon.Walker)
        {
            // Первую долю секунды после отрыва землю не считаем: овца уходит
            // в небо с той самой земли, на которой стояла, и её же касание,
            // пришедшее шагом физики следом за взлётом, рвало овцу на старте.
            bool justLifted = _aloft && _life < Weapon.LiftAfter + 0.2f;
            if ((_aloft && !justLifted) || c.collider.GetComponent<Worm>() != null) Explode();
            return;
        }

        // Бомба минного удара не рвётся, а ложится миной там, где упала.
        if (Weapon.Plants) { Plant(); return; }

        // Осёл идёт сквозь остров: каждое касание — воронка, и дальше вниз,
        // пока не кончатся пробои. Последний из них и есть взрыв.
        if (_punchesLeft > 0) { Punch(); return; }

        if (!Weapon.Contact)
        {
            Ricochet(c);
            if (c.relativeVelocity.sqrMagnitude > 9f) Sfx.Bounce();
            return;
        }

        if (Weapon.Bouncy)
        {
            Fx.Splash(transform.position, Weapon.Color, 4, 0.15f);
            Ricochet(c);
            // Тихие касания на излёте не озвучиваем — иначе граната тарахтит,
            // пока не докатится.
            if (c.relativeVelocity.sqrMagnitude > 4f) Sfx.Bounce();
            return;
        }

        // Ракета взрывается при любом контакте.
        Explode();
    }

    /// Искры рикошета: чиркнувший о породу снаряд высекает их вдоль отскока.
    /// Тихие касания на излёте пропускаем — иначе докатывающаяся граната
    /// сыпала бы искрами до самой остановки.
    void Ricochet(Collision2D c)
    {
        float hit = c.relativeVelocity.sqrMagnitude;
        if (hit < 9f || c.contactCount == 0) return;

        var contact = c.GetContact(0);
        Vector2 n = contact.normal;
        Vector2 inc = c.relativeVelocity.sqrMagnitude > 0.01f ? c.relativeVelocity.normalized : -n;
        // Отскок плюс доля нормали: как бы ни легло попадание, искры уходят
        // от поверхности, а не в неё.
        Vector2 dir = (Vector2.Reflect(inc, n) + n * 0.6f).normalized;
        Fx.Sparks(contact.point, dir, Mathf.Clamp(Mathf.RoundToInt(hit * 0.2f), 3, 8), 5.5f);
    }

    /// Заложить мину на месте падения. Мина рисуется своей иконкой и живёт
    /// своей жизнью — ход она не держит, поэтому снаряд после неё исчезает
    /// молча, без воронки.
    void Plant()
    {
        if (_exploded) return;
        _exploded = true;

        Vector2 pos = transform.position;
        Mine.Drop(MinePayload(Weapon), pos + Vector2.up * 0.1f);
        Fx.Splash(pos, Weapon.Color, 4, 0.15f);
        Sfx.Bounce();
        Vanish();
    }

    /// Мина минного удара: воронка и урон берутся у налёта, а вид и повадки —
    /// у обычной мины, потому что это она и есть.
    static Weapon MinePayload(Weapon strike) => new Weapon
    {
        Kind = WeaponKind.Mine,
        Name = "Мина",
        Use = WeaponUse.Drop,
        BlastRadius = strike.BlastRadius,
        Damage = strike.Damage,
        Contact = false,
        Color = strike.Color
    };

    /// Пробой: воронка вполовину меньше взрывной, скорость вниз восстанавливается,
    /// и осёл проваливается дальше. Порода перед ним уже вынута воронкой, так
    /// что следующего касания он ждёт, пока не дойдёт до нижней кромки полости.
    void Punch()
    {
        _punchesLeft--;
        Vector2 pos = transform.position;
        Combat.Detonate(pos, Weapon.BlastRadius * 0.55f, Weapon.Damage * 0.5f, true);
        Sfx.Explosion(Weapon.BlastRadius * 0.55f);
        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x * 0.2f, -16f);
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
        // Напалм рассыпается каплями: они скачут по склону и рвутся от касания
        // червя, а не по фитилю. Каждая почти безобидна — берёт их число.
        bool napalm = Weapon.Kind == WeaponKind.Napalm;

        var child = new Weapon
        {
            Kind = WeaponKind.Grenade,
            Name = napalm ? "Капля" : banana ? "Долька" : "Осколок",
            BlastRadius = napalm ? 1.1f : banana ? 2.4f : 1.8f,
            Damage = napalm ? 12f : banana ? 28f : mortar ? 18f : 22f,
            Fuse = napalm ? 2.2f : banana ? 1.6f : mortar ? 0f : 1.2f,
            Bouncy = !mortar,
            Contact = mortar,
            Color = napalm ? new Color(1f, 0.62f, 0.18f)
                  : banana ? new Color(0.98f, 0.85f, 0.25f) : new Color(1f, 0.85f, 0.35f)
        };

        for (int i = 0; i < ClusterChildren; i++)
        {
            float span = napalm ? 170f : banana ? 140f : mortar ? 40f : 60f;
            float mid = mortar ? -90f : 90f;
            float ang = mid - span * 0.5f + span / Mathf.Max(1, ClusterChildren - 1) * i + Random.Range(-8f, 8f);
            var dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
            float speed = mortar ? Random.Range(9f, 12f)
                        : napalm ? Random.Range(4f, 7f)
                        : Random.Range(7f, 11f);
            Spawn(child, pos + dir * 0.6f, dir * speed, null, 0.8f);
        }
    }

    void Vanish()
    {
        GameManager.I.UnregisterProjectile(this);
        Destroy(gameObject);
    }
}
