using UnityEngine;

public class Worm : MonoBehaviour
{
    public Team Team;
    public string WormName = "Worm";
    public float Health = 100f;
    public bool IsDead { get; private set; }

    public float AimAngle = 30f;   // градусы относительно горизонта
    public int Facing = 1;         // 1 вправо, -1 влево

    const float MoveSpeed = 4.2f;
    // Прыжок вдвое: высота и дальность выросли в два раза, а не скорость — при
    // гравитации высота идёт от квадрата скорости, так что удвоенный импульс дал
    // бы прыжок вчетверо и червь улетал бы за верх карты.
    const float JumpX = 7.1f;
    const float JumpY = 13.4f;

    /// На сколько червь способен взойти шагом, не прыгая. Чуть больше своего
    /// роста: ступенька в ландшафте и спина чужого червя проходят одинаково.
    const float StepHeight = 1.25f;

    // Урон от падения, как в оригинале: считается пройденная вниз высота, а не
    // скорость удара. Прыжок бесплатный по построению — при vy 13,4 и g 24 он
    // поднимает на 3,7 юнита, и до порога остаётся запас. Дальше по 5 очков за
    // юнит с потолком в 30: падение с любой высоты калечит, но не убивает целого
    // червя — добить его должно оружие, а не рельеф.
    const float FallFree = 6f;
    const float FallPerUnit = 5f;
    const float FallMax = 30f;

    // Толчок: досягаемость короче, чем у кулака (1,7) — толкают вплотную,
    // а не с полутора шагов. Импульс подобран так, чтобы сосед улетел примерно
    // на свой рост и слегка вверх: на ровном месте это ничего не решает, а
    // у кромки воды и над обрывом — решает всё.
    /// Парашют: предельная скорость снижения и снос по ветру.
    const float ChuteFall = 3.2f;
    const float ChuteDrift = 4.5f;

    /// Ранец: тяга вверх, потолок скорости и разгон вбок.
    const float JetLift = 34f;
    const float JetTop = 7.5f;
    const float JetSide = 5.5f;

    /// Балка: длина, толщина и на сколько она ставится от червя.
    const float GirderLength = 4.2f;
    const float GirderThickness = 0.55f;
    const float GirderReach = 3.2f;

    /// Сколько секунд полоса силы идёт от нуля до полного заряда.
    const float ChargeTime = 1.15f;

    /// Ниже этого заряда отпускание считается не выстрелом, а сорвавшимся
    /// касанием: снаряд с такой силой всё равно падает под ноги.
    const float MinCharge = 0.12f;

    const float ProdReach = 1.3f;
    const float ProdPush = 6.5f;
    const float ProdLift = 3.2f;

    Rigidbody2D _rb;
    CircleCollider2D _col;
    Transform _art;               // тело и черты вместе — их и разворачиваем по Facing
    SpriteRenderer _body;
    SpriteRenderer _face;
    Transform _crosshair;
    SpriteRenderer _crossSr;

    float _charge;
    bool _charging;
    bool _hasFiredThisTurn;
    int _burstLeft;               // остаток выстрелов дробовика на этот ход
    float _airTime;
    float _pushTime;              // сколько червь подряд толкается вбок, для захода на склон
    float _stepTimer;
    float _drownTimer;            // сколько червь уже под водой: тонет не мгновенно
    float _restTime;              // сколько червь стоит без дела — после чего примерзает
    float _fallPeak = float.NaN;  // высшая точка текущего полёта; NaN — падение не считаем
    bool _frozen;                 // покой: тело зажато связями, толкнуть его нельзя
    bool _chute;                  // парашют раскрыт
    bool _jet;                    // ранец включён
    float _fuel;                  // остаток тяги ранца в секундах
    Transform _canopy;            // купол парашюта
    Transform _flame;             // выхлоп ранца
    bool _acted;                  // червь уже походил: сменить его на другого нельзя
    float _bubbleTimer;
    int _lastWeapon = -1;
    Rope _rope;
    int _gen;

    public bool IsActive => GameManager.I != null && GameManager.I.ActiveWorm == this;

    /// Червь уже сделал в этом ходу что-то необратимое: сходил, прыгнул,
    /// выстрелил, толкнул соседа или бросил верёвку. До этого мига ход можно
    /// передать другому червю команды (GameManager.SelectNextWorm), после — нет.
    public bool HasActed => _acted || _hasFiredThisTurn;
    public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;
    public float Charge => _charge;
    public bool IsCharging => _charging;

    /// Верёвка червя, если она уже создана. Ищем компонент, а не полагаемся на
    /// поле: верёвку может завести и не сам червь (телепорт, тесты, потом бот),
    /// и тогда два источника правды разошлись бы.
    Rope RopeTool => _rope != null ? _rope : (_rope = GetComponent<Rope>());

    /// Червь висит на верёвке: ходьба и прыжок с земли на это время отключены.
    public bool Roped { get { var r = RopeTool; return r != null && r.Attached; } }

    public static Worm Spawn(Team team, string name, Vector2 pos)
    {
        var go = new GameObject("Worm_" + name);
        GameManager.Attach(go);
        go.transform.position = pos;

        var w = go.AddComponent<Worm>();
        w._gen = GameManager.I.Generation;
        w.Team = team;
        w.WormName = name;

        var art = new GameObject("Art").transform;
        art.SetParent(go.transform, false);
        w._art = art;

        // Тело красится в цвет команды, черты (контур, глаза, рот) — нет:
        // иначе обводка червя тонула бы в цвете команды.
        w._body = Sprites.Make("Body", WormSprite.Body, team.Color, 10, art);
        w._face = Sprites.Make("Face", WormSprite.Face, Color.white, 11, art);

        var col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.5f;
        col.sharedMaterial = new PhysicsMaterial2D("WormMat") { friction = 0.35f, bounciness = 0f };
        w._col = col;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        w._rb = rb;

        // Купол и выхлоп висят выключенными: включаются на время полёта.
        var canopy = Sprites.Make("Canopy", Sprites.Square, new Color(0.95f, 0.96f, 1f, 0.95f), 9, go.transform);
        canopy.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        canopy.transform.localScale = new Vector3(2.1f, 0.42f, 1f);
        canopy.gameObject.SetActive(false);
        w._canopy = canopy.transform;

        var flame = Sprites.Make("Jet", Sprites.Circle, new Color(1f, 0.72f, 0.25f, 0.95f), 9, go.transform);
        flame.transform.localPosition = new Vector3(0f, -0.55f, 0f);
        flame.transform.localScale = new Vector3(0.45f, 0.7f, 1f);
        flame.gameObject.SetActive(false);
        w._flame = flame.transform;

        var cross = Sprites.Make("Crosshair", Sprites.Circle, new Color(1f, 1f, 1f, 0.85f), 12, go.transform);
        cross.transform.localScale = Vector3.one * 0.3f;
        w._crosshair = cross.transform;
        w._crossSr = cross;

        return w;
    }

    public void BeginTurn()
    {
        Wake();
        CloseChute();
        StopJet();
        _hasFiredThisTurn = false;
        _acted = false;
        _burstLeft = 0;
        _charge = 0f;
        _charging = false;
        _lastWeapon = GameManager.I != null ? GameManager.I.SelectedWeapon : -1;
        ReleaseRope();
    }

    /// Отцепить верёвку. Зовётся на старте хода, при его завершении и при смерти —
    /// висеть на ней, пока ходит соперник, было бы странно.
    public void ReleaseRope()
    {
        var r = RopeTool;
        if (r != null) r.Release();
    }

    static readonly RaycastHit2D[] _groundHits = new RaycastHit2D[8];
    static readonly ContactFilter2D _groundFilter = MakeGroundFilter();

    static ContactFilter2D MakeGroundFilter()
    {
        var f = new ContactFilter2D();
        f = f.NoFilter();
        f.useTriggers = false;
        return f;
    }

    public bool Grounded
    {
        get
        {
            // queriesStartInColliders включён, поэтому луч, пущенный из центра червя,
            // первым же попадает в его собственный коллайдер на нулевой дистанции.
            // Одиночный CircleCast возвращал только это попадание — червь никогда не
            // считался стоящим на земле. Поэтому берём весь список и пропускаем себя.
            int n = Physics2D.CircleCast(transform.position, _col.radius * 0.92f, Vector2.down,
                                         _groundFilter, _groundHits, 0.18f);
            for (int i = 0; i < n; i++)
            {
                var c = _groundHits[i].collider;
                if (c != null && c.gameObject != gameObject) return true;
            }
            return false;
        }
    }

    static readonly Collider2D[] _overlap = new Collider2D[8];

    /// Занято ли место, если поставить червя центром в at (себя не считаем).
    bool Occupied(Vector2 at)
    {
        int n = Physics2D.OverlapCircle(at, _col.radius * 0.95f, _groundFilter, _overlap);
        for (int i = 0; i < n; i++)
        {
            var c = _overlap[i];
            if (c != null && c.gameObject != gameObject) return true;
        }
        return false;
    }

    /// Шаг наверх через препятствие: ищем самую низкую высоту, с которой червь
    /// пролезает вперёд, и переставляем его туда. Так он взбегает по ступеням
    /// и пробегает по спине чужого червя, как в оригинале, вместо того чтобы
    /// катить его перед собой.
    bool StepOver(float dir)
    {
        float d = Mathf.Sign(dir) * 0.5f;
        for (float dy = 0.25f; dy <= StepHeight; dy += 0.15f)
        {
            var target = _rb.position + new Vector2(d, dy);
            if (Occupied(target)) continue;
            // Над собой тоже должно быть свободно, иначе червь въедет в потолок.
            if (Occupied(_rb.position + new Vector2(0f, dy))) return false;

            _rb.position = target;
            transform.position = target;
            _rb.linearVelocity = new Vector2(Mathf.Sign(dir) * MoveSpeed, 0f);
            return true;
        }
        return false;
    }

    /// Червь в покое стоит намертво. В оригинале червь после приземления замирает
    /// там, где упал: его не сдвинуть боком, а сам он не съезжает по склону.
    /// У нас же круглый коллайдер с трением 0,35 скатывался с любого ската —
    /// команда уползала в море ещё до первого хода — и работал шаром, которым
    /// активный червь толкал соседа.
    void Settle()
    {
        if (IsDead || _rb == null || !_rb.simulated) return;

        bool steering = IsActive && GameManager.I != null
                        && (GameManager.I.State == GameState.Aim || GameManager.I.State == GameState.Retreat);
        bool wet = transform.position.y < DestructibleTerrain.WaterLevel - 0.1f;

        if (Roped || wet || !Grounded) { Wake(); return; }

        float move = steering && Controls != null ? Controls.Move : 0f;
        if (Mathf.Abs(move) > 0.01f) { Wake(); return; }

        // Трение покоя: остаток скорости гасим сами, а не ждём, пока круглый
        // коллайдер остановится о неровности.
        var v = _rb.linearVelocity;
        if (Mathf.Abs(v.x) > 0.01f && !_frozen)
        {
            v.x = Mathf.MoveTowards(v.x, 0f, 26f * Time.deltaTime);
            _rb.linearVelocity = v;
        }

        // Активного червя не примораживаем: ему ещё прыгать и получать отдачу.
        if (steering) { Wake(); return; }

        if (v.sqrMagnitude > 0.09f) { _restTime = 0f; return; }
        _restTime += Time.deltaTime;
        if (_restTime > 0.12f) Freeze();
    }

    /// Падение: пока червь в воздухе, помним высшую точку; коснулся земли —
    /// платит за пройденную вниз высоту сверх порога. Верёвка и вода падение
    /// отменяют: на верёвке червь спускается сам, а в воду он не падает, а тонет.
    void Falling(bool onGround)
    {
        if (IsDead) return;

        if (Roped || _chute || _jet || transform.position.y < DestructibleTerrain.WaterLevel)
        {
            // Парашют и ранец гасят падение целиком: за приземление под куполом
            // в оригинале не платят, ради этого его и держат в наборе.
            _fallPeak = float.NaN;
            return;
        }

        // Приземлением считаем только остановку. Луч «стою на земле» щупает на
        // 0,18 юнита вниз, и червь, летящий мимо уступа, на кадр оказывается
        // «на земле» — если верить этому, длинное падение дробится на короткие
        // и не стоит ничего.
        if (!onGround || _rb.linearVelocity.y < -0.5f)
        {
            float y = transform.position.y;
            _fallPeak = float.IsNaN(_fallPeak) ? y : Mathf.Max(_fallPeak, y);
            return;
        }

        if (float.IsNaN(_fallPeak)) return;

        float drop = _fallPeak - transform.position.y;
        _fallPeak = float.NaN;
        if (drop <= FallFree) return;

        Sfx.Thud();
        TakeDamage(Mathf.Min(FallMax, (drop - FallFree) * FallPerUnit));
    }

    void Freeze()
    {
        if (_frozen) return;
        _frozen = true;
        _rb.linearVelocity = Vector2.zero;
        _rb.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    /// Расковать тело: ход, отбрасывание, телепорт, ушедшая из-под ног земля.
    public void Wake()
    {
        _restTime = 0f;
        if (!_frozen) return;
        _frozen = false;
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    void Update()
    {
        if (GameManager.I == null || GameManager.I.Generation != _gen) return;

        // В отходе червь тоже слушает ввод: ходит, прыгает и бежит прятаться.
        // Стрелять он там уже не может — этому мешает _hasFiredThisTurn ниже.
        var state = GameManager.I.State;
        bool active = IsActive && !IsDead && (state == GameState.Aim || state == GameState.Retreat);
        // Прицел в отходе убираем: он обещал бы выстрел, которого не будет.
        _crossSr.enabled = active && !_hasFiredThisTurn;

        if (active) HandleInput();

        // Прицел рисуем всегда для активного червя.
        var dir = AimDirection;
        _crosshair.localPosition = dir * 2.2f;
        // Червь смотрит туда же, куда целится: зеркалим весь спрайт целиком.
        _art.localScale = new Vector3(Facing, 1f, 1f);

        Drowning();
        Settle();

        // Купол и ранец тикают у каждого червя, а не только у того, чей ход:
        // ход длится две секунды отхода, а спуск под куполом — дольше, и
        // передача хода не должна складывать парашют в воздухе. Тяга при этом
        // остаётся привилегией активного червя — ею управляют кнопкой.
        ChuteTick();
        var flyInput = active ? Controls : null;
        JetTick(flyInput, flyInput != null ? Mathf.Clamp(flyInput.Move, -1f, 1f) : 0f);

        bool onGround = Grounded;
        if (!onGround) _airTime += Time.deltaTime; else _airTime = 0f;
        Falling(onGround);

        Steps(active);
    }

    /// Шаги слышны только у того червя, кем ходят: шорох чужого тела,
    /// съезжающего по склону после взрыва, звучал бы как чьи-то шаги.
    void Steps(bool active)
    {
        if (!active || Roped || _airTime > 0.05f || Mathf.Abs(Velocity.x) < 0.8f)
        {
            _stepTimer = 0f;
            return;
        }

        _stepTimer -= Time.deltaTime;
        if (_stepTimer > 0f) return;
        _stepTimer = 0.26f;
        Sfx.Step();
    }

    /// Под водой червь тонет: здоровье уходит за пару секунд, вокруг идут
    /// пузыри. Раньше вода убивала мгновенно — при потопе, который поднимается
    /// каждый ход, это лишало шанса вылезти на берег в свой ход.
    void Drowning()
    {
        if (IsDead) return;

        float level = DestructibleTerrain.WaterLevel;
        if (transform.position.y >= level - 0.1f) { _drownTimer = 0f; return; }

        if (_drownTimer <= 0f)
        {
            Fx.Splash(new Vector2(transform.position.x, level));
            Sfx.Splash();
        }
        _drownTimer += Time.deltaTime;

        // Вода вязкая: червь идёт ко дну ровно, а не летит по инерции.
        if (_rb != null && _rb.simulated)
            _rb.linearVelocity = Vector2.Lerp(_rb.linearVelocity, new Vector2(0f, -2.2f), 6f * Time.deltaTime);

        _bubbleTimer -= Time.deltaTime;
        if (_bubbleTimer <= 0f)
        {
            _bubbleTimer = 0.28f;
            Fx.Bubbles(transform.position, 3);
        }

        // Урон от воды никому не записываем: это не чей-то выстрел.
        Health -= DrownRate * Time.deltaTime;
        if (Health <= 0f)
        {
            Health = 0f;
            Die(true);
        }
    }

    public Vector2 AimDirection
    {
        get
        {
            float a = AimAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a) * Facing, Mathf.Sin(a));
        }
    }

    /// Кто ведёт этого червя: команда бота приносит свою реализацию,
    /// у живого игрока это роутер клавиатуры, геймпада и касаний.
    public IGameInput Controls => Team != null && Team.Controller != null ? Team.Controller : GameInput.Player;

    void HandleInput()
    {
        var input = Controls;
        if (input == null) return;

        float h = Mathf.Clamp(input.Move, -1f, 1f);
        if (Mathf.Abs(h) > 0.01f) Facing = h > 0 ? 1 : -1;

        // Первое же осмысленное действие закрывает окно выбора червя: шаг,
        // прыжок и выстрел уже нельзя отыграть назад, а прицел — можно.
        if (Mathf.Abs(h) > 0.01f || input.JumpPressed) _acted = true;

        bool roped = Roped;
        bool grounded = !roped && Grounded;

        if (roped)
        {
            // На весу ввод уходит верёвке целиком: вбок — раскачка, вверх-вниз —
            // длина, прыжок — отцеп. Прицел там же не покрутить, и это честно:
            // одни и те же оси не могут значить два разных дела сразу.
            RopeTool.Control(input);
        }
        else
        {
            if (grounded && !_charging)
            {
                var v = _rb.linearVelocity;
                bool pushing = Mathf.Abs(h) > 0.01f;
                _pushTime = pushing ? _pushTime + Time.deltaTime : 0f;

                // Подъём на склон даём только когда червь действительно упёрся:
                // раньше он подпрыгивал на каждом шаге и по ровному месту шёл
                // в полёте, где ввод уже не действует — ходьба от этого залипала.
                bool blocked = _pushTime > 0.08f && Mathf.Abs(v.x) < MoveSpeed * 0.35f;
                v.x = h * MoveSpeed;
                _rb.linearVelocity = v;
                // Упёрся — заходим сверху: и на уступ, и на чужого червя.
                // Прежний толчок вверх на 0,6 м/с поднимал червя на пару
                // сантиметров, поэтому в соседа он утыкался и толкал его.
                if (blocked && Mathf.Abs(v.y) < 0.6f && StepOver(h)) _pushTime = 0f;
            }
            else _pushTime = 0f;

            // Прицеливание: клавиши и стик крутят угол, палец и бот задают точку.
            if (input.HasAimTarget)
            {
                Vector2 d = input.AimTarget - (Vector2)transform.position;
                if (d.sqrMagnitude > 0.09f)
                {
                    Facing = d.x >= 0f ? 1 : -1;
                    AimAngle = Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg, -85f, 85f);
                }
            }
            else if (Mathf.Abs(input.AimAxis) > 0.01f)
            {
                AimAngle = Mathf.Clamp(AimAngle + input.AimAxis * 75f * Time.deltaTime, -85f, 85f);
            }

            if (grounded && input.JumpPressed)
            {
                _rb.linearVelocity = new Vector2(Facing * JumpX, JumpY);
                Sfx.Jump();
            }
        }


        // Пока дробовик не отстрелял очередь, оружие не переключить: оба выстрела
        // уходят из одного ствола.
        if (_burstLeft == 0)
        {
            if (input.WeaponRequest >= 0) GameManager.I.SelectWeapon(input.WeaponRequest);
            if (input.WeaponCycle != 0) GameManager.I.CycleWeapon(input.WeaponCycle);
        }

        // Смена оружия посреди набора силы сбрасывает набор: иначе полоса силы
        // оставалась висеть, а следующий выстрел уходил с чужим зарядом.
        if (GameManager.I.SelectedWeapon != _lastWeapon)
        {
            _lastWeapon = GameManager.I.SelectedWeapon;
            _charging = false;
            _charge = 0f;
        }

        if (_hasFiredThisTurn) return;

        var weapon = GameManager.I.CurrentWeapon;

        switch (weapon.Use)
        {
            // Верёвка бросается мгновенно и ход не заканчивает.
            case WeaponUse.Rope:
                if (input.FirePressed) ThrowRope(weapon);
                return;

            case WeaponUse.Hitscan:
                if (input.FirePressed) FireHitscan(weapon);
                return;

            case WeaponUse.Melee:
                if (input.FirePressed)
                {
                    if (weapon.Kind == WeaponKind.Prod) Prod(weapon);
                    else Strike(weapon);
                }
                return;

            case WeaponUse.Drop:
                if (input.FirePressed) DropItem(weapon);
                return;

            case WeaponUse.Strike:
                if (input.FirePressed) CallAirStrike(weapon);
                return;

            // Бур и паяльная лампа: прорезать коридор в породе.
            case WeaponUse.Dig:
                if (input.FirePressed) Excavate(weapon);
                return;

            // Балка ход не заканчивает — как верёвка: поставил мост и стреляй.
            case WeaponUse.Build:
                if (input.FirePressed) PlaceGirder(weapon);
                return;

            case WeaponUse.Chute:
                if (input.FirePressed) OpenChute(weapon);
                return;

            case WeaponUse.Jet:
                if (input.FirePressed) StartJet(weapon);
                return;
        }

        // Набор силы начинает только нажатие, пришедшее на пустую полосу.
        // Повторное нажатие посреди набора — палец перехватил прицел, кнопка
        // «Огонь» прислала второе срабатывание — раньше обнуляло полосу: со
        // стороны это выглядело как «сила пошла, сбросилась и пошла заново»,
        // а выстрел в этот миг уходил с нулевой силой и падал под ноги.
        if (input.FirePressed && !_charging) { _charging = true; _charge = 0f; }

        if (_charging)
        {
            _charge = Mathf.Min(1f, _charge + Time.deltaTime / ChargeTime);
            if (input.FireReleased || !input.FireHeld || _charge >= 1f)
            {
                // Полоса едва тронулась — это не выстрел, а сорвавшийся палец.
                // Прежде такой промах уходил снарядом под ноги и стоил хода.
                if (_charge < MinCharge) { _charging = false; _charge = 0f; return; }

                // Телепорт использует ту же полосу силы, только не как скорость снаряда,
                // а как дальность прыжка — отдельного режима выбора точки не нужно.
                if (weapon.Use == WeaponUse.Teleport) DoTeleport(weapon);
                else FireProjectile(weapon);
            }
        }
    }

    /// Бросок верёвки. Промах патрон не тратит: гарпун просто ушёл в небо.
    void ThrowRope(Weapon w)
    {
        if (Roped) { ReleaseRope(); return; }

        _rope = Rope.Of(this);
        if (!_rope.Throw(AimDirection)) return;

        _acted = true;
        GameManager.I.ConsumeAmmo(w.Kind);
    }

    void DoTeleport(Weapon w)
    {
        _charging = false;

        if (!Teleport.Jump(this, AimDirection, _charge))
        {
            // Точки не нашлось — патрон цел, ход продолжается.
            Fx.FloatingText(transform.position + Vector3.up * 0.8f, "некуда", new Color(1f, 0.8f, 0.4f));
            _charge = 0f;
            return;
        }

        _hasFiredThisTurn = true;
        _charge = 0f;
        GameManager.I.ConsumeAmmo(w.Kind);
        GameManager.I.EndTurnAfterUtility();
    }

    /// Перенос без физики: телепорт и отладка. Скорость гасим, иначе червь
    /// прилетает в новую точку с прежним разгоном.
    public void PlaceAt(Vector2 pos)
    {
        ReleaseRope();
        Wake();
        _fallPeak = float.NaN;
        _rb.linearVelocity = Vector2.zero;
        _rb.position = pos;
        transform.position = pos;
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Health = Mathf.Min(100f, Health + amount);
    }

    void FireProjectile(Weapon w)
    {
        _charging = false;
        _hasFiredThisTurn = true;

        Vector2 dir = AimDirection;
        Vector2 pos = (Vector2)transform.position + dir * 0.95f;
        float speed = w.LaunchSpeed * Mathf.Max(0.18f, _charge);

        var shot = Projectile.Spawn(w, pos, dir * speed, this, 1f, w.Cluster);
        if (w.Homing) shot.HomeTarget = HomingTarget(dir);
        Sfx.Shot();

        _rb.AddForce(-dir * 1.2f, ForceMode2D.Impulse);
        GameManager.I.OnWeaponFired();
        _charge = 0f;
    }

    /// Ближайший живой враг в стороне прицела: туда пойдёт самонаводящаяся ракета.
    /// Цель выбирается на выстреле, а не каждый кадр — иначе ракета переключалась
    /// бы между червями и вертелась на месте.
    Vector2 HomingTarget(Vector2 dir)
    {
        var worms = GameManager.I.AllWorms();
        Worm best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead || w == this || w.Team == Team) continue;
            Vector2 d = (Vector2)w.transform.position - (Vector2)transform.position;
            // Врагов позади прицела штрафуем, но не отбрасываем: иначе ракета
            // без единой цели впереди просто улетала бы в небо.
            float score = d.magnitude * (Vector2.Dot(d.normalized, dir) > 0f ? 1f : 3f);
            if (score < bestScore) { bestScore = score; best = w; }
        }
        return best != null ? (Vector2)best.transform.position : (Vector2)transform.position + dir * 30f;
    }

    /// Мгновенное оружие двух повадок. Узи (AutoBurst) выпускает всю очередь одним
    /// нажатием, но пулями подряд во времени — иначе пять лучей ушли бы в один кадр
    /// по одной прямой. Дробовик бьёт по выстрелу за нажатие: между двумя выстрелами
    /// червь стоит на месте с тем же стволом и может перецелиться, а ход кончается
    /// только после последнего. Каждая пуля делает свою маленькую воронку.
    void FireHitscan(Weapon w)
    {
        if (w.AutoBurst)
        {
            _hasFiredThisTurn = true;
            StartCoroutine(AutoBurst(w));
            return;
        }

        if (_burstLeft == 0) _burstLeft = Mathf.Max(1, w.Burst);

        float spread = w.Spread > 0f ? Random.Range(-w.Spread, w.Spread) : 0f;
        float a = (AimAngle + spread) * Mathf.Deg2Rad;
        ShootRay(w, new Vector2(Mathf.Cos(a) * Facing, Mathf.Sin(a)), false);
        Sfx.Shotgun();

        if (--_burstLeft > 0) return;

        _hasFiredThisTurn = true;
        GameManager.I.OnWeaponFired();
    }

    /// Очередь узи: пули уходят одна за другой с короткой паузой, каждая — со своим
    /// разбросом. Ход завершаем только когда отстреляны все.
    System.Collections.IEnumerator AutoBurst(Weapon w)
    {
        int n = Mathf.Max(1, w.Burst);
        for (int i = 0; i < n; i++)
        {
            if (GameManager.I == null || GameManager.I.Generation != _gen || IsDead) yield break;

            float spread = w.Spread > 0f ? Random.Range(-w.Spread, w.Spread) : 0f;
            float a = (AimAngle + spread) * Mathf.Deg2Rad;
            ShootRay(w, new Vector2(Mathf.Cos(a) * Facing, Mathf.Sin(a)), true);
            Sfx.Shotgun();
            GameManager.I.Cam.Shake(0.12f);
            yield return new WaitForSeconds(0.07f);
        }

        if (GameManager.I != null && GameManager.I.Generation == _gen)
            GameManager.I.OnWeaponFired();
    }

    void ShootRay(Weapon w, Vector2 dir, bool soft)
    {
        Vector2 origin = (Vector2)transform.position + dir * 0.95f;

        var hits = Physics2D.RaycastAll(origin, dir, 45f);
        Vector2 point = origin + dir * 45f;
        float best = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider.gameObject == gameObject) continue;
            if (hit.distance < best) { best = hit.distance; point = hit.point; }
        }

        // Трассер
        var tracer = Sprites.Make("Tracer", Sprites.Square, new Color(1f, 1f, 0.7f, 0.9f), 18, GameManager.Root);
        float len = Vector2.Distance(origin, point);
        tracer.transform.position = origin + dir * (len * 0.5f);
        tracer.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        tracer.transform.localScale = new Vector3(len, 0.08f, 1f);
        Object.Destroy(tracer.gameObject, 0.12f);

        Combat.Detonate(point, w.BlastRadius, w.Damage, soft);
    }

    /// Удар вплотную: кулак подбрасывает жертву вверх, бита отправляет её в полёт
    /// вбок. Ландшафт трогает только кулак, и то едва.
    void Strike(Weapon w)
    {
        _hasFiredThisTurn = true;
        bool punch = w.Kind == WeaponKind.FirePunch;

        Vector2 origin = (Vector2)transform.position + new Vector2(Facing * 0.9f, 0.1f);
        var worms = GameManager.I.AllWorms();
        int hit = 0;
        for (int i = 0; i < worms.Count; i++)
        {
            var v = worms[i];
            if (v == null || v.IsDead || v == this) continue;
            Vector2 d = (Vector2)v.transform.position - origin;
            if (d.magnitude > 1.7f || d.x * Facing < -0.4f) continue;

            v.Knockback(punch ? new Vector2(Facing * 4f, 16f) : new Vector2(Facing * 20f, 7f));
            v.TakeDamage(w.Damage);
            hit++;
        }

        if (punch) Combat.Detonate(origin + new Vector2(Facing * 0.6f, 0f), w.BlastRadius, 0f);
        Fx.Splash(origin, w.Color, 8, 0.25f);
        Sfx.Shotgun();
        if (hit == 0) Fx.FloatingText(transform.position + Vector3.up * 0.8f, "мимо", new Color(1f, 0.85f, 0.4f));

        GameManager.I.OnWeaponFired();
    }

    /// Толчок: сосед улетает с места, здоровье не трогаем. Патрон не тратится и
    /// ход не заканчивается — как в оригинале, где толчок был способом решить
    /// дело, не стреляя: у воды и над обрывом он стоит целого червя, а на ровном
    /// месте не стоит ничего. Толкаем ближайшего, кто стоит вплотную и спереди,
    /// поэтому в куче червей улетает один, а не все сразу.
    /// Возвращает того, кого толкнули, или null — промах.
    public Worm Prod(Weapon w)
    {
        _acted = true;

        Vector2 origin = (Vector2)transform.position + new Vector2(Facing * 0.6f, 0f);
        var worms = GameManager.I.AllWorms();
        Worm victim = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < worms.Count; i++)
        {
            var v = worms[i];
            if (v == null || v.IsDead || v == this) continue;
            Vector2 d = (Vector2)v.transform.position - origin;
            // Толкают вперёд: тот, кто за спиной, не считается.
            if (d.x * Facing < -0.1f) continue;
            float dist = d.magnitude;
            if (dist > ProdReach || dist >= nearest) continue;
            nearest = dist;
            victim = v;
        }

        Fx.Splash(origin + new Vector2(Facing * 0.35f, 0f), w.Color, 6, 0.18f);
        if (victim == null)
        {
            Fx.FloatingText(transform.position + Vector3.up * 0.8f, "мимо", new Color(1f, 0.85f, 0.4f));
            Sfx.Step();
            return null;
        }

        // Knockback первым делом зовёт Wake: сосед в покое зажат связями
        // (Settle → Freeze), и без расковки импульс не сдвинул бы его ни на
        // пиксель — толчок выглядел бы сломанным.
        victim.Knockback(new Vector2(Facing * ProdPush, ProdLift));
        Sfx.Thud();
        return victim;
    }

    /// Раскрыт ли купол и включён ли ранец — для интерфейса и тестов.
    public bool Chuting => _chute;
    public bool Jetting => _jet;

    /// Парашют раскрывается только в падении: на земле и на подъёме куполу
    /// не за что зацепиться, а патрон тратился бы впустую. Ход не заканчивает.
    public void OpenChute(Weapon w)
    {
        if (_chute) { CloseChute(); return; }
        if (Grounded || _rb.linearVelocity.y > -1f)
        {
            Fx.FloatingText(transform.position + Vector3.up * 0.8f, "не в падении", new Color(1f, 0.85f, 0.4f));
            return;
        }

        _acted = true;
        _chute = true;
        _fallPeak = float.NaN;
        if (_canopy != null) _canopy.gameObject.SetActive(true);
        Sfx.CrateDrop();
        GameManager.I.ConsumeAmmo(w.Kind);
    }

    /// Под куполом червь падает медленно и его сносит ветром — тем же самым,
    /// что уводит снаряды. Складывается купол сам: о землю, о воду и о верёвку.
    void ChuteTick()
    {
        if (!_chute) return;

        if (IsDead || Roped || transform.position.y < DestructibleTerrain.WaterLevel
            || (Grounded && _rb.linearVelocity.y > -0.5f))
        {
            CloseChute();
            return;
        }

        var v = _rb.linearVelocity;
        v.y = Mathf.Max(v.y, -ChuteFall);
        float wind = GameManager.I != null ? GameManager.I.Wind : 0f;
        v.x = Mathf.MoveTowards(v.x, wind * ChuteDrift, 6f * Time.deltaTime);
        _rb.linearVelocity = v;
        _fallPeak = float.NaN;
    }

    public void CloseChute()
    {
        _chute = false;
        if (_canopy != null) _canopy.gameObject.SetActive(false);
    }

    /// Ранец: топливо тратится, только пока держат кнопку, поэтому им можно
    /// подпрыгнуть трижды по секунде, а не один раз на всё. Ход не заканчивает.
    public void StartJet(Weapon w)
    {
        if (_jet) return;

        _acted = true;
        _jet = true;
        _fuel = Mathf.Max(0.5f, w.Fuel);
        Wake();
        GameManager.I.ConsumeAmmo(w.Kind);
        Fx.FloatingText(transform.position + Vector3.up * 0.8f, "ранец", w.Color);
    }

    void JetTick(IGameInput input, float move)
    {
        if (!_jet) return;

        if (IsDead || Roped || transform.position.y < DestructibleTerrain.WaterLevel || _fuel <= 0f)
        {
            StopJet();
            return;
        }

        bool thrust = input != null && input.FireHeld;
        if (_flame != null) _flame.gameObject.SetActive(thrust);
        if (!thrust) return;

        _fuel -= Time.deltaTime;
        var v = _rb.linearVelocity;
        v.y = Mathf.Min(v.y + JetLift * Time.deltaTime, JetTop);
        v.x = Mathf.MoveTowards(v.x, move * JetSide, 14f * Time.deltaTime);
        _rb.linearVelocity = v;
        _fallPeak = float.NaN;

        if (_bubbleTimer <= 0f) Fx.Splash((Vector2)transform.position + Vector2.down * 0.6f,
                                          new Color(1f, 0.75f, 0.35f), 2, 0.12f);
    }

    public void StopJet()
    {
        _jet = false;
        _fuel = 0f;
        if (_flame != null) _flame.gameObject.SetActive(false);
    }

    /// Бур и паяльная лампа. Разница между ними одна — куда режут: бур строго
    /// вниз, лампа по прицелу. Рез идёт капсулой, а не чередой воронок: воронка
    /// оставляет копоть, круглые лунки и пересобирает коллайдер на каждом шаге.
    ///
    /// Ход инструмент заканчивает. Без этого бур был бы бесплатным способом
    /// уехать на другой конец карты: прокопался, вылез, выстрелил.
    void Excavate(Weapon w)
    {
        _hasFiredThisTurn = true;

        var terrain = GameManager.I.Terrain;
        Vector2 origin = transform.position;
        Vector2 dir = w.DigDown ? Vector2.down : AimDirection;
        // Начинаем от края червя, иначе первый же пиксель реза — под ним самим.
        Vector2 from = origin + dir * (_col.radius * 0.8f);
        Vector2 to = from + dir * w.DigLength;

        bool cut = terrain.Dig(from, to, w.DigRadius);

        // Червя сдвигаем следом за резом: вниз он свалится сам, а по горизонтали
        // остался бы стоять перед готовым тоннелем и лез бы в него ногами.
        if (cut && !w.DigDown)
        {
            Vector2 step = from + dir * Mathf.Min(w.DigLength * 0.6f, 3.2f);
            if (!terrain.IsSolidWorld(step)) PlaceAt(step);
        }

        Fx.Splash(from, w.Color, 10, 0.3f);
        Sfx.Drill();
        if (!cut) Fx.FloatingText(transform.position + Vector3.up * 0.8f, "не по чему копать", new Color(1f, 0.85f, 0.4f));

        GameManager.I.ConsumeAmmo(w.Kind);
        GameManager.I.OnWeaponFired();
    }

    /// Балка: мост под прицелом. Угол берётся из прицела и округляется до
    /// сорока пяти градусов, как в оригинале, — иначе балка встаёт под случайным
    /// наклоном и мостом уже не выглядит. Ставить внутрь червя нельзя: балка —
    /// это порода, и червь оказался бы замурован.
    void PlaceGirder(Weapon w)
    {
        _acted = true;

        var terrain = GameManager.I.Terrain;
        Vector2 center = (Vector2)transform.position + AimDirection * GirderReach;

        // Угол прицела к ближайшим 45°, знак — по направлению взгляда.
        float angle = Mathf.Round(AimAngle / 45f) * 45f * Facing;

        var worms = GameManager.I.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var v = worms[i];
            if (v == null || v.IsDead) continue;
            // Прямоугольник балки грубо накрываем окружностью: точности хватает,
            // а замуровать червя на полпикселя всё равно нельзя.
            if (Vector2.Distance(v.transform.position, center) < GirderLength * 0.5f + 0.7f)
            {
                Fx.FloatingText(transform.position + Vector3.up * 0.8f, "мешает червь", new Color(1f, 0.85f, 0.4f));
                return;
            }
        }

        var metal = new Color32(150, 158, 170, 255);
        if (!terrain.StampBeam(center, angle, GirderLength, GirderThickness, metal))
        {
            Fx.FloatingText(transform.position + Vector3.up * 0.8f, "некуда", new Color(1f, 0.85f, 0.4f));
            return;
        }

        // Балка могла лечь под ногами у соседей — их надо расковать, иначе
        // примороженный червь останется висеть, а не встанет на неё.
        for (int i = 0; i < worms.Count; i++)
            if (worms[i] != null) worms[i].Wake();

        Fx.Splash(center, w.Color, 8, 0.25f);
        Sfx.Thud();
        GameManager.I.ConsumeAmmo(w.Kind);
    }

    /// Динамит, мина и овца кладутся под ноги. Мина остаётся на карте и после
    /// хода, поэтому ходом её не ждут — остальное тикает как обычный снаряд.
    void DropItem(Weapon w)
    {
        _hasFiredThisTurn = true;

        Vector2 pos = (Vector2)transform.position + new Vector2(Facing * 0.6f, 0f);
        if (w.Kind == WeaponKind.Mine)
        {
            Mine.Drop(w, pos);
        }
        else
        {
            var p = Projectile.Spawn(w, pos, new Vector2(Facing * 1.5f, 1f), this);
            p.WalkDir = Facing;
        }

        Sfx.Shot();
        GameManager.I.OnWeaponFired();
    }

    /// Налёт: пятёрка бомб сыплется на точку, куда смотрит прицел. Точка ищется
    /// лучом до породы — так она одинаково задаётся и мышью, и стрелками.
    void CallAirStrike(Weapon w)
    {
        _hasFiredThisTurn = true;

        Vector2 dir = AimDirection;
        Vector2 origin = (Vector2)transform.position + dir * 0.95f;
        float x = origin.x + dir.x * 18f;

        var hits = Physics2D.RaycastAll(origin, dir, 60f);
        float best = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider.gameObject == gameObject) continue;
            if (hit.distance < best) { best = hit.distance; x = hit.point.x; }
        }

        float top = DestructibleTerrain.WorldHeight + 6f;
        for (int i = 0; i < Mathf.Max(1, w.Burst); i++)
        {
            float dx = (i - (w.Burst - 1) * 0.5f) * 2.2f;
            var pos = new Vector2(x + dx - Facing * 3f, top + i * 0.6f);
            Projectile.Spawn(w, pos, new Vector2(Facing * 2.5f, -4f), null);
        }

        Sfx.Shot();
        GameManager.I.OnWeaponFired();
    }

    public void Knockback(Vector2 impulse)
    {
        if (IsDead) return;
        _charging = false;
        Wake();
        _rb.AddForce(impulse, ForceMode2D.Impulse);
    }

    public void TakeDamage(float dmg)
    {
        if (IsDead || dmg <= 0.5f) return;
        Health -= dmg;
        if (GameManager.I != null) GameManager.I.RegisterDamage(this, dmg);
        Fx.FloatingText(transform.position + Vector3.up * 0.8f, "-" + Mathf.RoundToInt(dmg), new Color(1f, 0.5f, 0.4f));
        if (Health <= 0f) Die();
    }

    /// Сколько здоровья съедает вода за секунду: около двух с половиной секунд
    /// с полного здоровья — хватает, чтобы в свой ход выбраться на сушу.
    const float DrownRate = 42f;

    /// Что червь успевает сказать напоследок. Слова короткие: метка живёт
    /// меньше секунды и над головой должна читаться целиком.
    static readonly string[] Farewells =
    {
        "Прощай!", "Ой-ой…", "Ну всё…", "Мама!", "Я пошёл…",
        "Пока-пока!", "Не поминайте лихом", "Эх…"
    };

    void Die(bool drowned = false)
    {
        if (IsDead) return;
        IsDead = true;
        Health = 0f;

        // Утонуть можно двумя способами, и хоронят их по-разному. Если вода
        // просто дошла до червя, стоящего на земле, — это обычная смерть с
        // прощанием, взрывом и памятником. А если он ушёл на дно, ставить
        // памятник некуда: там остаются только пузыри. Опору проверяем до
        // того, как выключим коллайдер, иначе Grounded уже ничего не найдёт.
        bool sank = drowned && !Grounded;

        ReleaseRope();
        _crossSr.enabled = false;
        _col.enabled = false;
        _rb.simulated = false;
        _charging = false;
        GameManager.I.OnWormDied(this);
        StartCoroutine(DeathSequence(drowned, sank));
    }

    /// Смерть с прощанием: червь коротко прощается, качается на месте и только
    /// потом взрывается, оставляя памятник команды. Ход в это время не уходит —
    /// GameManager ждёт, пока счётчик умирающих не обнулится.
    System.Collections.IEnumerator DeathSequence(bool drowned, bool sank)
    {
        // Ссылку на менеджер каждый раз берём заново: матч может кончиться
        // прямо во время прощания, и старый объект к концу корутины уже мёртв.
        if (GameManager.I != null) GameManager.I.BeginDeathAnim();

        Vector2 pos = transform.position;
        if (drowned)
        {
            Fx.FloatingText(pos + Vector2.up * 0.8f, "буль-буль…", new Color(0.6f, 0.85f, 1f));
            Fx.Bubbles(pos, 8);
        }
        else
        {
            Fx.FloatingText(pos + Vector2.up * 0.9f, Farewells[Random.Range(0, Farewells.Length)],
                            new Color(1f, 0.92f, 0.6f));
            Sfx.Bye();
        }

        // Прощание короткое: полсекунды покачивания, иначе ход тянется.
        const float wave = 0.55f;
        for (float t = 0f; t < wave; t += Time.deltaTime)
        {
            if (_art != null)
            {
                float k = Mathf.Sin(t * 26f) * 9f;
                _art.localRotation = Quaternion.Euler(0f, 0f, k);
                _art.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(t * 13f)) * 0.12f, 0f);
            }
            yield return null;
        }

        if (_body != null) _body.enabled = false;
        if (_face != null) _face.enabled = false;

        var gm = GameManager.I;
        if (gm != null && gm.Generation == _gen)
        {
            if (sank)
            {
                // На дне ни воронки, ни памятника: ставить его туда некуда,
                // а взрыв в глубине выглядел бы фокусом.
                Fx.Bubbles(pos, 6);
            }
            else
            {
                // Взрывается только тот, кого убили. Утонувший на берегу тихо
                // ложится под памятник: воронка под ним съела бы ему же опору,
                // и памятник тут же ушёл бы под воду.
                if (!drowned) Combat.Detonate(pos, 2.2f, 20f);
                Grave.Place(Team, pos);
            }
        }

        if (GameManager.I != null) GameManager.I.EndDeathAnim();
        Destroy(gameObject);
    }
}
