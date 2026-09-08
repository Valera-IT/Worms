using UnityEngine;

/// Соперник-бот: ещё одна реализация IGameInput, как и клавиатура с касаниями.
/// Червь и GameManager про бота ничего не знают — команда просто приносит
/// свой Controller, а тот выдаёт те же ход, прицел и набор силы.
///
/// Ход бота разложен по фазам: подумать → (при нужде подойти) → навести →
/// набрать силу → выстрелить → убежать. Наведение не мгновенное: угол
/// доводится за полсекунды, чтобы человек за экраном видел, как бот целится.
///
/// Две фазы про свою шкуру. Climb — вода прибывает каждый ход, и червю у самой
/// кромки важнее вылезти на берег, чем выстрелить на пять урона. Flee — окно
/// отхода после выстрела: бот уходит от собственной воронки, и только поэтому
/// ему вообще можно давать в руки динамит.
///
/// Fetch — дорога к ящику с припасами. Подбор ходом не кончается, поэтому за
/// ящиком идут не вместо выстрела, а до него: если времени хватает и на дорогу,
/// и на прицел.
///
/// Rope и Swing — верёвка-ниндзя. Когда до врага не достать ни выстрелом, ни
/// ногами, бот цепляется гарпуном, качается на маятнике и отпускает верёвку
/// в нижней точке дуги: там скорость горизонтальна и наибольшая. Ход после
/// верёвки не кончается, так что с новой земли он ещё и стреляет.
///
/// Чего именно бот не умеет, решает не случай, а сложность: BotSkills
/// раскладывает её на умения — арсенал, самосохранение, память о промахах.
///
/// Дробовик бьёт двумя выстрелами по отдельному нажатию: после первого бот
/// уходит в Reposition — немного проходит в сторону, — затем целится заново
/// и стреляет второй раз.
public class BotInput : IGameInput
{
    enum Phase { Think, Approach, Climb, Fetch, Rope, Swing, Aim, Charge, Reposition, Flee, Done }

    readonly Team _team;
    readonly BotDifficulty _diff;
    readonly BotSkills _skills;
    readonly System.Random _rng;

    Worm _worm;
    Phase _phase;
    float _t;             // время в текущей фазе
    int _frame = -1;
    int _attempts;        // сколько раз уже пересчитывали выстрел за этот ход
    BotShot _shot;
    float _aimAngle;      // угол, доведённый до червя в этом кадре
    int _aimFacing = 1;
    int _wantWeapon = -1;
    float _stuck;         // сколько уже упираемся, идя к врагу
    int _bumps;           // сколько раз подряд упёрлись: пора обходить
    float _backoff;       // сколько ещё пятимся, чтобы выйти из ямы
    bool _teleported;     // телепорт за этот ход уже пробовали
    Vector2 _posBeforeJump;
    int _burstFired;      // выстрелов дробовика уже сделано за этот ход
    float _stepDir = 1f;  // куда шагаем между выстрелами дробовика
    bool _climbed;        // из воды за этот ход уже вылезали
    bool _fetched;        // за ящиком в этот ход уже ходили
    Crate _crate;         // ящик, к которому идём
    float _fetchBudget;   // сколько секунд отвели на дорогу к нему
    int _climbDir;        // куда берег выше: выбирается раз за лазанье
    int _fleeDir;         // куда бежим от своей воронки
    Vector2 _danger;      // где рванёт: от этой точки и убегаем
    bool _swung;          // верёвку за этот ход уже бросали
    bool _blinded;        // выстрел наугад за этот ход уже делали
    BotPlanner.BotSwing _swing;   // за что цепляемся и куда качаемся

    // Выходные поля: заполняются раз в кадр в Tick.
    float _move;
    bool _jump, _firePressed, _fireHeld, _fireReleased;

    const float ThinkTime = 0.7f;
    const float AimSpeed = 140f;    // градусов в секунду
    const float ApproachTime = 1.6f;

    /// Выше этого выстрел считается настоящим попаданием и делается сразу.
    /// Ниже — это подрыв укрытия рядом с врагом: полезно, но сперва стоит
    /// поискать позицию получше.
    const float GoodShot = 8f;
    const float RepositionTime = 0.7f;   // короткий проход между выстрелами дробовика
    const float ChargeRate = 1f / 1.15f;   // как в Worm.HandleInput

    /// Ниже этого над водой червь считается тонущим: следующий подъём воды
    /// накроет его с головой, а до своего хода он больше не доживёт.
    const float DrownMargin = 2.6f;
    const float ClimbTime = 2.4f;

    public BotInput(Team team, BotDifficulty diff, int seed)
    {
        _team = team;
        _diff = diff;
        _skills = BotSkills.For(diff);
        _rng = new System.Random(seed);
    }

    /// Что бот собирается сделать — для HUD, тестов и отладки.
    public BotShot Shot => _shot;
    public BotDifficulty Difficulty => _diff;

    public InputScheme Scheme => InputScheme.Keyboard;

    public float Move { get { Tick(); return _move; } }
    public float AimAxis { get { Tick(); return 0f; } }
    /// Прицел бот задаёт точкой, но только когда целится: на ходу — и когда
    /// идёт к врагу, и когда лезет из воды, и когда убегает — червь должен
    /// смотреть туда, куда шагает.
    public bool HasAimTarget { get { Tick(); return _worm != null && (_phase == Phase.Aim || _phase == Phase.Charge || _phase == Phase.Rope || _phase == Phase.Done); } }
    public Vector2 AimTarget
    {
        get
        {
            Tick();
            if (_worm == null) return Vector2.zero;
            float r = _aimAngle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(r) * _aimFacing, Mathf.Sin(r));
            return (Vector2)_worm.transform.position + dir * 4f;
        }
    }
    /// Метка крестика: налёт и телепорт бот наводит ею, а не углом ствола.
    /// Точку считает BotPlanner вместе с самим планом, поэтому отдельной фазы
    /// «поставить крестик» у бота нет — червь ставит метку тем же кадром,
    /// каким жмёт огонь.
    public bool HasMark { get { Tick(); return _worm != null && _shot.Found && _shot.HasMark; } }
    public Vector2 Mark { get { Tick(); return _shot.Mark; } }

    public bool JumpPressed { get { Tick(); return _jump; } }
    public bool FirePressed { get { Tick(); return _firePressed; } }
    public bool FireHeld { get { Tick(); return _fireHeld; } }
    public bool FireReleased { get { Tick(); return _fireReleased; } }
    public int WeaponRequest { get { Tick(); return _wantWeapon; } }
    public int WeaponCycle { get { Tick(); return 0; } }
    // Бот играет тем червём, до которого дошла очередь: выбор своего червя —
    // это про то, чем игроку удобнее ходить, и в его расчёт не входит.
    public bool SelectWormPressed => false;

    /// Раз в кадр, кто бы ни спросил первым, — тот же приём, что у роутера GameInput.
    void Tick()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;

        _move = 0f;
        _jump = _firePressed = _fireHeld = _fireReleased = false;
        _wantWeapon = -1;

        var gm = GameManager.I;
        if (gm == null) { _worm = null; return; }

        var worm = gm.ActiveWorm;
        bool playable = gm.State == GameState.Aim || gm.State == GameState.Retreat;
        if (worm == null || worm.IsDead || worm.Team != _team || !playable)
        {
            _worm = null;
            return;
        }

        if (worm != _worm) BeginTurn(worm);
        _t += Time.deltaTime;

        // Окно отхода: стрелять уже нечем, всё дело — унести ноги от фитиля.
        if (gm.State == GameState.Retreat)
        {
            // Прятаться — тоже умение: лёгкий бот стоит и смотрит на фитиль.
            if (!_skills.Flees) return;
            if (_phase != Phase.Flee) { _phase = Phase.Flee; _t = 0f; _stuck = 0f; }
            TickFlee();
            return;
        }

        // Оружие переключаем, пока GameManager не согласится: у выбранного мог
        // кончиться боезапас, и тогда план придётся считать заново.
        if (_shot.Found && gm.SelectedWeapon != _shot.Weapon)
        {
            if (!gm.HasAmmo(_shot.Weapon)) { Replan(); return; }
            _wantWeapon = _shot.Weapon;
        }

        switch (_phase)
        {
            case Phase.Think: TickThink(gm); break;
            case Phase.Approach: TickApproach(gm); break;
            case Phase.Climb: TickClimb(); break;
            case Phase.Fetch: TickFetch(); break;
            case Phase.Rope: TickRope(gm); break;
            case Phase.Swing: TickSwing(); break;
            case Phase.Aim: TickAim(); break;
            case Phase.Charge: TickCharge(gm); break;
            case Phase.Reposition: TickReposition(gm); break;
            case Phase.Done: TickDone(); break;
        }
    }

    void BeginTurn(Worm worm)
    {
        _worm = worm;
        _phase = Phase.Think;
        _t = 0f;
        _attempts = 0;
        _stuck = 0f;
        _bumps = 0;
        _backoff = 0f;
        _teleported = false;
        _burstFired = 0;
        _climbed = false;
        _climbDir = 0;
        _fetched = false;
        _crate = null;
        _fleeDir = 0;
        _swung = false;
        _blinded = false;
        _swing = default;
        _shot = default;
        _aimAngle = worm.AimAngle;
        _aimFacing = worm.Facing;
    }

    void TickThink(GameManager gm)
    {
        if (_t < ThinkTime) return;
        // Пока червя мотает после взрыва, считать траекторию бессмысленно.
        if (_worm.Velocity.sqrMagnitude > 0.6f) { _t = ThinkTime * 0.5f; return; }

        _shot = BotPlanner.Plan(_worm, _skills, _rng);
        _attempts++;

        bool timePressed = gm.TurnTimeLeft < 8f;

        // Вода прибывает каждый ход. Червю по колено в море следующего хода
        // может не достаться, и лезть наверх важнее, чем ковырять склон рядом
        // с врагом. Верное убийство — исключение: за него не жалко и утонуть.
        if (_skills.Climbs && Drowning() && !_climbed && !timePressed &&
            (!_shot.Found || _shot.Score < 40f))
        {
            _climbed = true;
            _climbDir = 0;
            _phase = Phase.Climb;
            _t = 0f;
            return;
        }

        // Ногами из воды не вышло — уходим телепортом. Сухая земля весит в
        // оценке места больше всего остального, поэтому тонущему червю PlanJump
        // почти всегда что-нибудь да находит.
        if (_climbed && Drowning() && TryTeleport(gm)) return;

        // Ящик с припасами. Подбор ходом не считается: сходить и выстрелить
        // можно за один ход, поэтому вопрос не «вместо чего», а «успеем ли» —
        // время на дорогу и на выстрел после неё считает BotPlanner.BestCrate.
        if (TryFetch(gm)) return;

        // Есть настоящее попадание — стреляем не раздумывая.
        if (_shot.Found && _shot.Score > GoodShot) { GoAim(); return; }

        // Время выходит: берём что угодно, лишь бы не по своим.
        if (timePressed && _shot.Found && _shot.Score > -5f) { GoAim(); return; }
        if (timePressed && !_shot.Found && TryBlind(gm)) return;

        // Попадания нет, но воронка ложится у врага — со второй попытки этого
        // достаточно: подрытое укрытие уже работа, а ход не резиновый.
        if (_attempts >= 2 && _shot.Found && _shot.Score > 0.2f) { GoAim(); return; }

        // Есть ли вообще куда шагнуть. На островке шириной в три прыжка идти
        // некуда: по обе стороны вода, и «подойти поближе» превращается в
        // хождение по кругу «думаю — упёрся — думаю».
        bool canWalk = SafeStep(1f) || SafeStep(-1f);

        // Совсем ничего. Подходим и считаем заново — с новой точки открывается
        // и угол, и дистанция.
        if (_attempts <= 3 && !timePressed && canWalk) { _phase = Phase.Approach; _t = 0f; return; }

        // Подойти не вышло: враг за гребнем, этажом ниже или на соседнем
        // острове. Сперва верёвка — её на матч три, и ход после неё продолжается;
        // телепорт держим на случай, когда качнуться некуда.
        if (TryRope(gm)) return;
        if (TryTeleport(gm)) return;

        if (_shot.Found && _shot.Score > -1f) { GoAim(); return; }

        // Ни выстрела, ни дороги, ни верёвки. Бьём наугад в сторону врага:
        // мимо так мимо, зато ход сделан. Раньше бот в этом месте уходил в
        // Approach, тот сразу упирался в воду и возвращал его сюда — так весь
        // ход и сгорал, а червь на соседнем острове стоял не шелохнувшись.
        if (TryBlind(gm)) return;

        // Идти дальше всё равно лучше, чем стоять столбом.
        _shot = default;
        _phase = Phase.Approach;
        _t = 0f;
    }

    /// Выстрел вслепую в ближайшего врага — раз за ход. Считает его BotPlanner:
    /// там же, где и остальная баллистика.
    bool TryBlind(GameManager gm)
    {
        if (_blinded) return false;

        var enemy = NearestEnemy(gm);
        if (enemy == null) return false;

        var shot = BotPlanner.Blind(_worm, enemy.transform.position);
        if (!shot.Found) return false;

        _blinded = true;
        _shot = shot;
        GoAim();
        return true;
    }

    void GoAim()
    {
        _aimFacing = _shot.Facing;
        _phase = Phase.Aim;
        _t = 0f;
    }

    /// Насколько место должно стать лучше, чтобы прыжок себя оправдал.
    /// Телепорт стоит целого хода и одного из двух патронов на матч, поэтому
    /// переставлять червя на соседний бугор незачем. Тонущему червю столько
    /// набегает от одной только сухой земли.
    const float JumpGain = 14f;

    /// Врага не достать ни выстрелом, ни ногами — или вода уже по пояс.
    /// Точку выбирает BotPlanner.PlanJump: он смотрит на всю карту, как игрок
    /// крестиком, и берёт место, где стоять заметно лучше, чем сейчас.
    bool TryTeleport(GameManager gm)
    {
        if (_teleported || !_skills.Teleport) return false;
        int idx = Weapon.IndexOf(WeaponKind.Teleport);
        if (!gm.HasAmmo(idx)) return false;

        if (!BotPlanner.PlanJump(_worm, out Vector2 point, out float gain)) return false;
        if (gain < JumpGain) return false;

        Vector2 d = point - (Vector2)_worm.transform.position;
        _teleported = true;
        _posBeforeJump = _worm.transform.position;
        _shot = new BotShot
        {
            Found = true,
            Weapon = idx,
            // Угол и сторона нужны только на вид: червь разворачивается туда,
            // куда сейчас исчезнет. Переносит его сама метка.
            Angle = Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg, -85f, 85f),
            Facing = d.x >= 0f ? 1 : -1,
            Charge = 1f,
            HasMark = true,
            Mark = point,
            Impact = point
        };
        GoAim();
        return true;
    }

    /// Верёвка-ниндзя: качнуться туда, куда не дойти ногами. Патрон тратится
    /// только на зацепившийся гарпун, а ход после верёвки продолжается —
    /// поэтому пробовать её дёшево, и пробуем мы до телепорта.
    bool TryRope(GameManager gm)
    {
        if (_swung || !_skills.Rope) return false;
        int idx = Weapon.IndexOf(WeaponKind.Rope);
        if (!gm.HasAmmo(idx)) return false;

        var swing = BotPlanner.PlanSwing(_worm);
        if (!swing.Found) return false;

        _swung = true;
        _swing = swing;
        _shot = new BotShot
        {
            Found = true, Weapon = idx, Angle = swing.Angle, Facing = swing.Facing, Charge = 1f
        };
        _phase = Phase.Rope;
        _t = 0f;
        return true;
    }

    /// Бросок гарпуна: доводим прицел, жмём один раз и ждём. Зацепился —
    /// качаемся; ушёл в пустоту — патрон цел, считаем выстрел заново.
    void TickRope(GameManager gm)
    {
        if (_worm.Roped) { _phase = Phase.Swing; _t = 0f; _stuck = 0f; return; }
        if (_t > 2f) { Replan(); return; }

        // Пока в руках не верёвка, бросать нечего: смену оружия просит Tick.
        if (gm.SelectedWeapon != _shot.Weapon) return;

        _aimFacing = _shot.Facing;
        _aimAngle = Mathf.MoveTowards(_aimAngle, _shot.Angle, AimSpeed * Time.deltaTime);
        if (Mathf.Abs(_aimAngle - _shot.Angle) > 0.05f) return;

        // Нажимаем не каждый кадр: повторное нажатие на висящей верёвке её же
        // и отцепляет, а промах впустую щёлкает гарпуном.
        if (_t > 0.6f) { _firePressed = _fireHeld = true; _t = 1.6f; }
    }

    /// Качели. Раскачиваемся в выбранную сторону и отпускаем верёвку в той
    /// самой точке, по которой BotPlanner считал приземление, — низшей, до
    /// которой дуга свободна. Отцепились — ждём земли и считаем выстрел
    /// с новой позиции.
    void TickSwing()
    {
        if (!_worm.Roped)
        {
            bool landed = _worm.Velocity.sqrMagnitude < 0.6f;
            if (_t > 0.4f && (landed || _t > 4f)) Replan();
            return;
        }

        _move = _swing.SwingDir;
        _aimFacing = _swing.SwingDir;

        float dx = _worm.transform.position.x - _swing.Release.x;
        bool passed = dx * _swing.SwingDir >= 0f && Mathf.Abs(_worm.Velocity.x) > 1.5f;

        // Пять секунд на дуге — значит, маятник встал: отпускаем как есть,
        // ход всё равно дороже.
        if ((passed && _t > 0.3f) || _t > 5f) { _jump = true; _t = 0f; }
    }

    /// Телепорт мог не найти просвета — тогда патрон цел, ход продолжается, а
    /// бот уже считает себя отработавшим. Замечаем это и думаем заново.
    void TickDone()
    {
        if (!_shot.Found || !Weapon.All[_shot.Weapon].Utility) return;
        if (_t < 0.8f) return;
        if (((Vector2)_worm.transform.position - _posBeforeJump).sqrMagnitude > 4f) return;
        Replan();
    }

    /// Стоит ли сходить за ящиком — и если да, уходим в Phase.Fetch.
    ///
    /// Ящик подбирается касанием, ход этим не кончается, поэтому дорога к нему
    /// стоит только времени. Берём, если оно есть и с запасом на выстрел.
    bool TryFetch(GameManager gm)
    {
        if (_fetched || !_skills.Crates) return false;

        var crate = BotPlanner.BestCrate(_worm, gm.TurnTimeLeft, out float budget);
        if (crate == null) return false;

        _fetched = true;
        _crate = crate;
        _fetchBudget = budget;
        _phase = Phase.Fetch;
        _t = 0f;
        _stuck = 0f;
        _bumps = 0;
        return true;
    }

    /// Дорога к ящику. Подобрали — ящик исчезает сам, и это же служит сигналом
    /// вернуться к выстрелу; не дошли за отведённое время — тоже.
    void TickFetch()
    {
        if (_crate == null || _t > _fetchBudget || Drowning()) { Replan(); return; }

        float dx = _crate.transform.position.x - _worm.transform.position.x;
        float dir = dx >= 0f ? 1f : -1f;

        if (!SafeStep(dir)) { Replan(); return; }

        _move = dir;
        _aimFacing = dir > 0f ? 1 : -1;

        // Уткнулись в уступ — подпрыгнем; трижды подряд — ящик стоит не там,
        // куда есть дорога, и ход дороже него.
        if (Mathf.Abs(_worm.Velocity.x) < 0.6f) _stuck += Time.deltaTime; else _stuck = 0f;
        if (_stuck > 0.35f)
        {
            _jump = true;
            _stuck = 0f;
            if (++_bumps >= 3) Replan();
        }
    }

    void TickApproach(GameManager gm)
    {
        var enemy = NearestEnemy(gm);
        if (enemy == null || _t > ApproachTime) { _phase = Phase.Think; _t = 0f; return; }

        float dx = enemy.transform.position.x - _worm.transform.position.x;
        float dir = dx >= 0f ? 1f : -1f;

        // Три раза подряд уткнулись — полсекунды пятимся: из ямы или из-под
        // навеса дорога к врагу может идти в обход, а раньше бот всё это время
        // просто толкал стену.
        if (_backoff > 0f)
        {
            _backoff -= Time.deltaTime;
            dir = -dir;
        }
        else if (!SafeStep(dir))
        {
            // Впереди море или обрыв: туда не идём — с потопом это верная смерть.
            _phase = Phase.Think;
            _t = 0f;
            return;
        }

        _move = dir;
        _aimFacing = dir > 0f ? 1 : -1;

        // Упёрлись в стену или в край — подпрыгнем.
        if (Mathf.Abs(_worm.Velocity.x) < 0.6f) _stuck += Time.deltaTime; else _stuck = 0f;
        if (_stuck > 0.35f)
        {
            _jump = true;
            _stuck = 0f;
            if (++_bumps >= 3) { _bumps = 0; _backoff = 0.6f; }
        }
    }

    /// Есть ли куда шагнуть: земля в полутора юнитах впереди должна быть выше
    /// воды и не на дне пропасти. Вода теперь прибывает каждый ход, и берег,
    /// по которому бот шёл в прошлый раз, мог стать морем.
    bool SafeStep(float dir)
    {
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        if (terrain == null || _worm == null) return true;

        float x = _worm.transform.position.x + dir * 1.6f;
        if (x < 1f || x > DestructibleTerrain.WorldWidth - 1f) return false;

        float h = terrain.SurfaceHeightWorld(x);
        if (h < DestructibleTerrain.WaterLevel + 0.4f) return false;
        return h > _worm.transform.position.y - 9f;
    }

    /// По колено в воде: следующая волна накроет.
    bool Drowning() =>
        _worm != null && _worm.transform.position.y - DestructibleTerrain.WaterLevel < DrownMargin;

    /// Уходим от воды наверх. Ход на этом не кончается: вылез — и с новой точки
    /// ещё успеешь выстрелить, поэтому времени на подъём даём меньше трёх секунд.
    void TickClimb()
    {
        if (_climbDir == 0) _climbDir = Uphill();
        // Наверх дороги нет — ни в одну сторону. Тогда хоть выстрелить.
        if (_climbDir == 0 || _t > ClimbTime || !Drowning()) { Replan(); return; }

        _move = _climbDir;
        _aimFacing = _climbDir;

        if (Mathf.Abs(_worm.Velocity.x) < 0.6f) _stuck += Time.deltaTime; else _stuck = 0f;
        if (_stuck > 0.35f)
        {
            _jump = true;
            _stuck = 0f;
            // Дважды уткнулись — берег с этой стороны не берётся, пробуем другую.
            if (++_bumps >= 2) { _bumps = 0; _climbDir = -_climbDir; }
        }
    }

    /// В какую сторону берег выше. Смотрим поверхность на семь юнитов вперёд:
    /// важен не только конец пути, но и то, что дорога туда не идёт под воду —
    /// высокий утёс за проливом не спасает.
    int Uphill()
    {
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        if (terrain == null || _worm == null) return 0;

        float x0 = _worm.transform.position.x;
        float left = Reach(terrain, x0, -1), right = Reach(terrain, x0, 1);
        if (float.IsNegativeInfinity(left) && float.IsNegativeInfinity(right)) return 0;
        return right >= left ? 1 : -1;
    }

    /// Высота берега в конце пути или −∞, если пути нет.
    static float Reach(DestructibleTerrain terrain, float x0, int dir)
    {
        float end = float.NegativeInfinity;
        for (float d = 1.5f; d <= 7f; d += 1f)
        {
            float x = x0 + dir * d;
            if (x < 1f || x > DestructibleTerrain.WorldWidth - 1f) return float.NegativeInfinity;

            float h = terrain.SurfaceHeightWorld(x);
            if (h < DestructibleTerrain.WaterLevel + 0.4f) return float.NegativeInfinity;
            end = h;
        }
        return end;
    }

    /// Окно отхода: уходим от своей воронки. Сторону выбираем один раз — иначе
    /// червь топчется на месте, когда закладка ровно под ногами, — и только
    /// туда, где есть земля выше воды.
    void TickFlee()
    {
        if (_worm == null) return;

        if (_fleeDir == 0)
        {
            float dx = _worm.transform.position.x - _danger.x;
            int away = dx >= 0f ? 1 : -1;
            if (!SafeStep(away) && SafeStep(-away)) away = -away;
            _fleeDir = away;
        }

        // Впереди обрыв или море: от фитиля это не спасение. Лучше остаться.
        if (!SafeStep(_fleeDir)) return;

        _move = _fleeDir;
        _aimFacing = _fleeDir;

        if (Mathf.Abs(_worm.Velocity.x) < 0.6f) _stuck += Time.deltaTime; else _stuck = 0f;
        if (_stuck > 0.3f) { _jump = true; _stuck = 0f; }
    }

    /// Между двумя выстрелами дробовика: короткий проход в выбранную сторону,
    /// затем пересчёт выстрела под новую позицию.
    void TickReposition(GameManager gm)
    {
        _move = _stepDir;
        _aimFacing = _stepDir > 0f ? 1 : -1;

        // Упёрлись в стену или в край — подпрыгнем.
        if (Mathf.Abs(_worm.Velocity.x) < 0.6f) _stuck += Time.deltaTime; else _stuck = 0f;
        if (_stuck > 0.3f) { _jump = true; _stuck = 0f; }

        if (_t <= RepositionTime) return;
        _stuck = 0f;

        // Оружие менять нельзя, пока очередь не отстреляна, — второй выстрел
        // тоже из дробовика. Берём план планировщика, только если он выбрал тот
        // же ствол; иначе бьём в ближайшего врага навскидку.
        int gun = _shot.Weapon;
        var plan = BotPlanner.Plan(_worm, _skills, _rng);
        if (plan.Found && plan.Weapon == gun)
        {
            _shot = plan;
        }
        else
        {
            var e = NearestEnemy(gm);
            if (e == null) { _phase = Phase.Done; return; }
            Vector2 d = (Vector2)e.transform.position - (Vector2)_worm.transform.position;
            int facing = d.x >= 0f ? 1 : -1;
            float ang = Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg, -80f, 85f);
            _shot = new BotShot { Found = true, Weapon = gun, Angle = ang, Facing = facing, Charge = 1f };
        }

        _aimFacing = _shot.Facing;
        _phase = Phase.Aim;
        _t = 0f;
    }

    void TickAim()
    {
        _aimFacing = _shot.Facing;
        _aimAngle = Mathf.MoveTowards(_aimAngle, _shot.Angle, AimSpeed * Time.deltaTime);
        if (Mathf.Abs(_aimAngle - _shot.Angle) < 0.05f && _t > 0.35f)
        {
            _aimAngle = _shot.Angle;
            _phase = Phase.Charge;
            _t = 0f;
        }
    }

    void TickCharge(GameManager gm)
    {
        _aimAngle = _shot.Angle;
        _aimFacing = _shot.Facing;

        var hs = Weapon.All[_shot.Weapon];

        // Удар вплотную, налёт, закладка и телепорт набора силы не требуют:
        // одно нажатие — и ход окончен. У налёта и телепорта всё наведение —
        // это метка, а полосу силы червь всё равно отпустит следующим кадром,
        // как только бот перестанет держать огонь. Закладка при этом кладётся
        // под ноги, поэтому убегать придётся от себя самого — точку взрыва
        // запоминаем здесь.
        if (hs.Use == WeaponUse.Melee || hs.Use == WeaponUse.Strike
         || hs.Use == WeaponUse.Drop  || hs.Use == WeaponUse.Teleport)
        {
            _firePressed = _fireHeld = true;
            MarkDanger();
            _phase = Phase.Done;
            _t = 0f;
            return;
        }

        if (hs.Hitscan)
        {
            // Набор силы не нужен — жмём огонь.
            _firePressed = _fireHeld = true;
            MarkDanger();

            // Узи: одна очередь за нажатие, дальше червь отстреляет её сам.
            if (hs.AutoBurst) return;

            // Дробовик: выстрел за нажатие. Сделали не последний — уходим
            // немного пройтись и прицелиться заново; после последнего ход окончен.
            _burstFired++;
            if (_burstFired < Mathf.Max(1, hs.Burst))
            {
                _stepDir = _rng.Next(2) == 0 ? -1f : 1f;
                _phase = Phase.Reposition;
                _t = 0f;
            }
            else
            {
                _phase = Phase.Done;
            }
            return;
        }

        // Держим нажатие, пока червь не начал копить силу.
        if (!_worm.IsCharging) { _firePressed = true; _fireHeld = true; return; }

        _fireHeld = true;
        // Отпускаем с упреждением в полкадра: заряд растёт дискретно,
        // и без поправки бот стабильно перебирал силу.
        if (_worm.Charge + ChargeRate * Time.deltaTime * 0.5f >= _shot.Charge)
        {
            _fireHeld = false;
            _fireReleased = true;
            MarkDanger();
            _phase = Phase.Done;
        }
    }

    /// От чего бежать в окне отхода. Расчётная воронка знает это точнее всего;
    /// если её нет (навскидку в упор, промазавший план), считаем опасным
    /// место, где стоим: чужой ответ прилетит именно сюда.
    void MarkDanger()
    {
        _danger = _shot.Found && _shot.Impact != Vector2.zero
                ? _shot.Impact
                : (Vector2)_worm.transform.position;
        _fleeDir = 0;
        // Итог этого выстрела принесёт первый же взрыв — и ляжет в память
        // команды: повторять промах со следующего хода будет дороже.
        BotMemory.NoteShot(_team, _shot, _worm.transform.position);
    }

    void Replan()
    {
        _shot = default;
        _phase = Phase.Think;
        _t = 0f;
    }

    Worm NearestEnemy(GameManager gm)
    {
        Worm best = null;
        float bestD = float.MaxValue;
        var worms = gm.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var o = worms[i];
            if (o == null || o.IsDead || o.Team == _team) continue;
            float d = Vector2.Distance(o.transform.position, _worm.transform.position);
            if (d < bestD) { bestD = d; best = o; }
        }
        return best;
    }
}
