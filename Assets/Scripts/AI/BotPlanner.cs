using System.Collections.Generic;
using UnityEngine;

/// Готовый выстрел: чем, под каким углом и с какой силой стрелять.
public struct BotShot
{
    public bool Found;
    public int Weapon;      // индекс в Weapon.All
    public float Angle;     // градусы к горизонту, -85..85
    public int Facing;      // 1 вправо, -1 влево
    public float Charge;    // доля набора силы, 0.18..1
    public float Score;     // ожидаемая польза: урон врагу минус свои потери
    public Vector2 Impact;  // где рванёт по расчёту — для отладки и тестов

    /// Точка для крестика: налёт и телепорт наводятся ею, а не углом ствола
    /// (Weapon.Targeted). BotInput отдаёт её червю через IGameInput.Mark.
    public bool HasMark;
    public Vector2 Mark;
}

/// Как бот выбирает выстрел: перебор пар «угол и сила» с аналитической
/// баллистикой (гравитация плюс ветер как постоянные ускорения), проверка
/// трассы по маске ландшафта и оценка воронки по той же формуле, что у Combat.
/// Отдельно от BotInput: тут чистый расчёт без состояния и без кадров,
/// поэтому его можно звать из тестов напрямую.
public static class BotPlanner
{
    const float Coarse = 0.05f;    // шаг интегрирования грубого прохода, с
    /// Шаг точного прохода — ровно шаг физики: Unity считает полуявным Эйлером,
    /// и модель обязана шагать так же, иначе за две секунды полёта расчёт уходит
    /// от снаряда на полметра только из-за разной дискретизации.
    static float Fine => Time.fixedDeltaTime;
    const float ProjRadius = 0.22f;// коллайдер снаряда: он рвётся, не доехав до маски
    /// Дольше не летает ничего: у самого долгого фитиля (банан) четыре секунды,
    /// а прыгучая граната иначе скачет весь лимит и одна съедает половину плана.
    const float MaxFlight = 4.3f;
    const float HitRadius = 0.72f; // червь 0.5 + снаряд 0.22
    const float CrateRadius = 0.67f;// ящик 0.9×0.9 + снаряд 0.22

    /// Минимальная скорость, которой хватит, чтобы добросить до самого удобного
    /// врага с этой стороны, или -1, если с этой стороны врагов нет.
    ///
    /// Раньше здесь было простое расстояние, а отсечка сравнивала его с v²/g —
    /// формулой дальности по ровному месту. Из-за этого бот, стоящий выше цели,
    /// отбрасывал все слабые заряды: до врага двадцатью юнитами ниже «по прямой»
    /// далеко, а бросить туда можно почти любым плевком, гравитация доделает.
    /// Правильная оценка — классический минимум для попадания в точку (dx, dy):
    /// v² = g·(dy + √(dx² + dy²)). Для цели ниже dy отрицательный, и порог сам
    /// опускается; для цели выше — растёт.
    /// Заодно и самый дальний: заряд, которым перебрасывает и через него,
    /// перебирать незачем. Раньше верхней границы не было вовсе — при прежних
    /// скоростях её роль играл сам предел броска, а после того как бросок
    /// удвоили, половина сетки зарядов стала уходить за край карты.
    static float MaxSpeedUseful;

    static float MinSpeedNeeded(Worm shooter, int facing)
    {
        float g = Mathf.Abs(Physics2D.gravity.y);
        float best = -1f;
        float far = 0f;
        Vector2 from = shooter.transform.position;

        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            Vector2 to = o.transform.position;
            float dx = to.x - from.x;
            if (dx * facing < -1f) continue;

            float dy = to.y - from.y;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float v = Mathf.Sqrt(Mathf.Max(0.01f, g * (dy + r)));
            if (best < 0f || v < best) best = v;
            if (v > far) far = v;
        }

        // Полторы минимальные скорости — это и навесная траектория над гребнем,
        // и перелёт с запасом; всё, что выше, летит в соседнее море.
        MaxSpeedUseful = far * 1.7f;
        return best;
    }

    /// Умения бота, с которыми считается текущий план: чем он умеет стрелять,
    /// насколько тонко перебирает и насколько внимательно выбирает цель.
    /// Живёт полем, а не параметром, ровно по той же причине, что и снимок
    /// червей: расчёт зовётся из десятка мест по паре тысяч раз за план.
    static BotSkills _skills = BotSkills.For(BotDifficulty.Hard);

    /// Чей план считаем: нужен памяти о промахах — она хранится по командам.
    static Worm _shooter;

    /// Ближайшие враги: буфер для налёта, чтобы не аллоцировать на каждый план.
    static readonly List<Worm> _targets = new List<Worm>();

    /// Черви на карте: список снимается один раз на планирование, иначе
    /// AllWorms() аллоцировал бы на каждую из пары тысяч проверенных траекторий.
    static readonly List<Worm> _worms = new List<Worm>();

    /// Ящики с припасами: снаряд рвётся об них, а сам ящик детонирует,
    /// поэтому для расчёта они и препятствие, и второй взрыв.
    static readonly List<Crate> _crates = new List<Crate>();

    static void Snapshot()
    {
        // Метка живёт ровно один перебор: чужой расчёт не должен получить
        // ракету, привязанную к чьей-то прошлой цели.
        _homeForced = false;

        _worms.Clear();
        var all = GameManager.I.AllWorms();
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && !all[i].IsDead) _worms.Add(all[i]);

        _crates.Clear();
        for (int i = 0; i < Crate.All.Count; i++)
            if (Crate.All[i] != null) _crates.Add(Crate.All[i]);
    }

    /// Ускорение снаряда: гравитация из Physics2D и ветер, который Projectile
    /// прикладывает силой, пропорциональной массе, — то есть тоже ускорением.
    /// У ракеты gravityScale 0.25 (Projectile.Spawn): она почти не проседает,
    /// иначе доворот не успевал бы вытянуть её обратно на цель.
    static Vector2 Accel(Weapon w) =>
        new Vector2(w.AffectedByWind ? GameManager.I.Wind * 9f : 0f,
                    Physics2D.gravity.y * (w.Homing ? 0.25f : 1f));

    /// Лучший выстрел для червя. rng задаёт разброс: он же — сложность.
    ///
    /// Перебор идёт в два прохода. Грубый прогоняет всю сетку пар с крупным
    /// шагом времени — так дёшево отбрасывается всё, что упирается в склон или
    /// тонет. Точный пересчитывает окрестность найденного мелким шагом и с
    /// поправкой на радиус снаряда: одним лишь грубым проходом расчёт расходился
    /// с физикой на полтора юнита, а это промах.
    public static BotShot Plan(Worm shooter, BotDifficulty diff, System.Random rng)
        => Plan(shooter, BotSkills.For(diff), rng);

    /// Тот же перебор, но умения заданы прямо: так его зовёт BotInput, который
    /// разворачивает сложность в набор умений один раз на матч.
    public static BotShot Plan(Worm shooter, BotSkills skills, System.Random rng)
    {
        var best = new BotShot { Score = float.NegativeInfinity };
        var gm = GameManager.I;
        if (gm == null || shooter == null || shooter.IsDead) return default;

        Snapshot();
        _strikeFall.Clear();
        _skills = skills;
        _shooter = shooter;
        // Грубый шаг по углу можно держать крупным: точность добирает второй
        // проход по окрестности, а каждый лишний угол — это целая траектория.
        float angleStep = skills.AngleStep;
        float chargeStep = skills.ChargeStep;

        // Арсенал перебираем в два захода: сперва бесконечное (базука и
        // граната), потом штучное. Если бесконечным уже нашлось хорошее
        // попадание, штучное можно не считать вовсе — всё равно не возьмём,
        // а половина стоимости плана именно там.
        for (int pass = 0; pass < 2; pass++)
        {
        if (pass == 1 && best.Found && best.Score >= 30f) break;

        for (int wi = 0; wi < Weapon.All.Length; wi++)
        {
            if (!gm.HasAmmo(wi)) continue;
            var w = Weapon.All[wi];
            if ((w.Ammo < 0) != (pass == 0)) continue;
            // Снаряжение без урона (верёвка, телепорт) перебором ничего не решает.
            if (!w.BotCanUse) continue;
            // Остальное отсекает сложность: чем она ниже, тем короче арсенал.
            if (w.Homing && !skills.Homing) continue;
            if (w.Use == WeaponUse.Drop && !skills.Drops) continue;
            if (w.Use == WeaponUse.Strike && !skills.Strikes) continue;

            // Не всё летит по баллистике: луч считается своим проходом, налёт
            // сыплется с неба мимо всякого рельефа, а удар вплотную вообще не
            // летит. Раньше бот знал только первые два и в упор или из-за
            // гребня оставался без единого варианта.
            // Ракета целится меткой, а не стволом, поэтому и перебор у неё
            // свой: он идёт от точки на карте, а не от угла ствола.
            if (w.Homing) { Consider(ref best, Homing(shooter, wi)); continue; }

            switch (w.Use)
            {
                case WeaponUse.Hitscan: Consider(ref best, Hitscan(shooter, wi)); continue;
                case WeaponUse.Melee: Consider(ref best, Melee(shooter, wi)); continue;
                case WeaponUse.Strike: Consider(ref best, Strike(shooter, wi)); continue;
                case WeaponUse.Drop: Consider(ref best, Drop(shooter, wi)); continue;
            }

            for (int facing = -1; facing <= 1; facing += 2)
            {
                // В сторону, где нет ни одного врага, стрелять незачем.
                float need = MinSpeedNeeded(shooter, facing);
                if (need < 0f) continue;

                // Запас на пятую часть: слабее этого до врага не долетает даже
                // с горы, но подрыв укрытия рядом и скачущая граната ещё в игре.
                float floorSpeed = need * 0.8f;

                float ceilSpeed = MaxSpeedUseful;

                for (float c = 0.2f; c <= 1.0001f; c += chargeStep)
                {
                    float speed = w.LaunchSpeed * Mathf.Min(c, 1f);
                    if (speed < floorSpeed || speed > ceilSpeed) continue;

                    for (float a = -85f; a <= 85f; a += angleStep)
                        Consider(ref best, TryCore(shooter, wi, a, facing, Mathf.Min(c, 1f), Coarse, false));
                }
            }
        }
        }

        if (!best.Found) return default;

        // Точный проход по окрестности найденной пары — уже с тем же оружием.
        if (Weapon.All[best.Weapon].Use == WeaponUse.Charged)
        {
            var seed = best;
            float da = skills.TuneStep;
            int an = Mathf.CeilToInt(angleStep / da);
            var tuned = new BotShot { Score = float.NegativeInfinity };

            // Ракету точный проход обязан вести в ту же метку, что и грубый:
            // без этого она на последнем шаге переучивалась на ближайшего врага
            // и уточнялась совсем другая траектория.
            _homeForced = seed.HasMark;
            _homeMark = seed.Mark;

            for (int i = -an; i <= an; i++)
                for (int j = -2; j <= 2; j++)
                {
                    float c = Mathf.Clamp(seed.Charge + j * chargeStep * 0.4f, 0.18f, 1f);
                    Consider(ref tuned, TryCore(shooter, seed.Weapon, seed.Angle + i * da,
                                               seed.Facing, c, Fine, true));
                }
            if (tuned.Found)
            {
                tuned.HasMark = seed.HasMark;
                tuned.Mark = seed.Mark;
                best = tuned;
            }
            _homeForced = false;
        }

        // Дрожание рук — последняя из разниц между сложностями, а не главная.
        best.Angle = Mathf.Clamp(best.Angle + Gauss(rng) * skills.AngleJitter, -85f, 85f);
        best.Charge = Mathf.Clamp(best.Charge + Gauss(rng) * skills.ChargeJitter, 0.18f, 1f);
        return best;
    }

    /// Выстрел наугад: последнее средство, когда перебор не нашёл ни одной
    /// траектории, а ногами до врага не дойти. Угол берётся из школьной
    /// баллистики (без ветра и без проверки препятствий), сила — полная.
    ///
    /// Нужен затем, что раньше в этом месте бот уходил «подойти поближе»,
    /// упирался в воду у кромки острова, возвращался думать — и так до конца
    /// хода: со стороны червь просто стоял столбом весь ход. Промах в море
    /// стоит меньше, чем сгоревший ход, а из двух решений баллистики берётся
    /// настильное — оно короче по времени и меньше зависит от ветра.
    public static BotShot Blind(Worm shooter, Vector2 target)
    {
        var gm = GameManager.I;
        if (gm == null || shooter == null) return default;

        int idx = -1;
        for (int i = 0; i < Weapon.All.Length && idx < 0; i++)
        {
            var w = Weapon.All[i];
            // Бесконечное и по баллистике: тратить последнюю ракету на выстрел
            // вслепую незачем.
            if (w.Use == WeaponUse.Charged && w.Ammo < 0 && !w.Homing && gm.HasAmmo(i)) idx = i;
        }
        for (int i = 0; i < Weapon.All.Length && idx < 0; i++)
        {
            var w = Weapon.All[i];
            if (w.Use == WeaponUse.Charged && w.BotCanUse && gm.HasAmmo(i)) idx = i;
        }
        if (idx < 0) return default;

        var gun = Weapon.All[idx];
        Vector2 from = shooter.transform.position;
        float dx = target.x - from.x, dy = target.y - from.y;
        int facing = dx >= 0f ? 1 : -1;
        float x = Mathf.Abs(dx);
        float g = Mathf.Abs(Physics2D.gravity.y);
        float v = gun.LaunchSpeed;

        // v⁴ − g(g·x² + 2·dy·v²): меньше нуля — до цели не добросить ни под
        // каким углом, тогда бьём под сорок пять, дальше всего.
        float angle = 45f;
        float disc = v * v * v * v - g * (g * x * x + 2f * dy * v * v);
        if (x > 0.5f && disc >= 0f)
            angle = Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * x)) * Mathf.Rad2Deg;

        return new BotShot
        {
            Found = true, Weapon = idx, Charge = 1f, Facing = facing,
            Angle = Mathf.Clamp(angle, -85f, 85f), Impact = target, Score = 0f
        };
    }

    /// Чем платим за выстрел. Базука и граната бесконечны — их не жалко;
    /// за остальное берём вперёд, и чем меньше в запасе, тем дороже: иначе бот
    /// спускает единственный налёт на премию за близость и остаётся ни с чем.
    static float Adjusted(BotShot s)
    {
        float score = s.Score - Repeat(s);
        int ammo = Weapon.All[s.Weapon].Ammo;
        if (ammo < 0) return score + 1.5f;
        return score - (ammo <= 2 ? 14f : 6f);
    }

    /// Штраф за повтор: этим оружием с этой точки под этим углом бот уже мазал.
    /// Промах помнит BotMemory, а лёгкий бот не помнит ничего.
    static float Repeat(BotShot s)
    {
        if (!_skills.Memory || _shooter == null) return 0f;
        return BotMemory.Penalty(_shooter.Team, s.Weapon, s.Angle, s.Charge,
                                 _shooter.transform.position);
    }

    static void Consider(ref BotShot best, BotShot candidate)
    {
        if (!candidate.Found) return;
        if (!best.Found || Adjusted(candidate) > Adjusted(best)) best = candidate;
    }

    /// Один прогон траектории для пары «угол и сила». Точка входа для тестов:
    /// сама снимает список червей, тогда как перебор внутри Plan идёт по снятому.
    public static BotShot Try(Worm shooter, int weaponIndex, float angleDeg, int facing, float charge)
    {
        Snapshot();
        _skills = BotSkills.For(BotDifficulty.Hard);
        _shooter = shooter;
        return TryCore(shooter, weaponIndex, angleDeg, facing, charge, Fine, true);
    }

    /// Один расчёт закладки. Точка входа для тестов — как Try для баллистики.
    public static BotShot TryDrop(Worm shooter, int weaponIndex)
    {
        Snapshot();
        _skills = BotSkills.For(BotDifficulty.Hard);
        _shooter = shooter;
        return Drop(shooter, weaponIndex);
    }

    /// Один расчёт ракеты. Точка входа для тестов — как TryDrop для закладки.
    public static BotShot TryHoming(Worm shooter, int weaponIndex)
    {
        Snapshot();
        _skills = BotSkills.For(BotDifficulty.Hard);
        _shooter = shooter;
        return Homing(shooter, weaponIndex);
    }

    /// Один расчёт налёта. Точка входа для тестов — как TryDrop для закладки.
    public static BotShot TryStrike(Worm shooter, int weaponIndex)
    {
        Snapshot();
        _skills = BotSkills.For(BotDifficulty.Hard);
        _shooter = shooter;
        return Strike(shooter, weaponIndex);
    }

    static BotShot TryCore(Worm shooter, int weaponIndex, float angleDeg, int facing, float charge,
                           float dt, bool fine)
    {
        var w = Weapon.All[weaponIndex];
        angleDeg = Mathf.Clamp(angleDeg, -85f, 85f);
        float rad = angleDeg * Mathf.Deg2Rad;
        var dir = new Vector2(Mathf.Cos(rad) * facing, Mathf.Sin(rad));

        Vector2 p = (Vector2)shooter.transform.position + dir * 0.95f;
        Vector2 v = dir * (w.LaunchSpeed * Mathf.Max(0.18f, charge));

        // Куда доворачивать. Бот метит цель крестиком, и тогда точка задана
        // снаружи — ровно та, которую червь поставит меткой. Метки нет
        // (выстрел наугад, сверка из теста) — ракета выберет цель сама, по
        // стороне прицела, ровно как Worm.HomingTarget.
        if (w.Homing) _homeTarget = _homeForced ? _homeMark : HomeTargetFor(shooter, dir);

        if (!Fly(w, shooter, ref p, ref v, dt, fine)) return default;

        return new BotShot
        {
            Found = true, Weapon = weaponIndex, Angle = angleDeg, Facing = facing,
            Charge = charge, Impact = p, Score = Score(p, w, shooter)
        };
    }

    const float HomingDelay = 0.35f;   // Projectile.HomingDelay: столько ракета летит прямо
    const float HomingTurn = 260f;     // Projectile.HomingTurn: градусов в секунду
    const float HomingFuel = 3.4f;     // Projectile.HomingFuel: столько работает двигатель

    /// Куда доворачивает ракета в считаемой сейчас траектории.
    static Vector2 _homeTarget;

    /// Метка, поставленная перебором: пока она задана, ракета в модели идёт
    /// именно в неё, а не в того, кого выбрал бы ствол.
    static bool _homeForced;
    static Vector2 _homeMark;

    /// Кого выберет ракета, пущенная в эту сторону. Точка входа для тестов:
    /// сама снимает список червей, как Try и TryDrop.
    public static Vector2 HomingTargetOf(Worm shooter, Vector2 dir)
    {
        Snapshot();
        return HomeTargetFor(shooter, dir);
    }

    /// Та же выборка цели, что в Worm.HomingTarget: ближайший живой враг,
    /// причём стоящие позади прицела втрое дальше — но не отброшены совсем.
    static Vector2 HomeTargetFor(Worm shooter, Vector2 dir)
    {
        Worm best = null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o == shooter || o.Team == shooter.Team) continue;
            Vector2 d = (Vector2)o.transform.position - (Vector2)shooter.transform.position;
            float score = d.magnitude * (Vector2.Dot(d.normalized, dir) > 0f ? 1f : 3f);
            if (score < bestScore) { bestScore = score; best = o; }
        }
        return best != null ? (Vector2)best.transform.position
                            : (Vector2)shooter.transform.position + dir * 30f;
    }

    /// Положение снаряда через steps шагов физики, без всяких столкновений —
    /// та самая модель, по которой идёт перебор. Нужна тестам, чтобы сверить
    /// её с настоящим снарядом в свободном полёте.
    public static Vector2 Trace(Weapon w, Vector2 p0, Vector2 v0, int steps)
    {
        var a = Accel(w);
        float dt = Time.fixedDeltaTime;
        Vector2 p = p0, v = v0;
        for (int i = 0; i < steps; i++) { v += a * dt; p += v * dt; }
        return p;
    }

    /// Полёт до взрыва. true — рвануло в p, false — утонуло, улетело за край
    /// или не успело за отведённое время. dt задаёт точность: грубый проход
    /// шагает крупно, точный — мелко и с поправкой на радиус снаряда.
    /// Проверка породы кругом радиуса снаряда: центр плюс четыре стороны.
    static bool SolidAround(DestructibleTerrain terrain, Vector2 p, float r)
        => terrain.IsSolidWorld(p)
        || terrain.IsSolidWorld(p + Vector2.up * r)
        || terrain.IsSolidWorld(p + Vector2.down * r)
        || terrain.IsSolidWorld(p + Vector2.left * r)
        || terrain.IsSolidWorld(p + Vector2.right * r);

    static bool Fly(Weapon w, Worm shooter, ref Vector2 p, ref Vector2 v, float dt, bool fine)
    {
        var terrain = GameManager.I.Terrain;
        Vector2 a = Accel(w);
        float fuse = w.Fuse > 0f ? w.Fuse : float.MaxValue;
        float t = 0f;

        // Точку касания уточняем половинением шага, но в грубом проходе — не до
        // миллиметра: там важно, куда легло примерно, а каждое половинение это
        // ещё один шаг на каждый из сотен перебираемых углов.
        float touch = fine ? 0.005f : 0.02f;

        // Сколько раз гранате позволено отскочить. В грубом проходе три: дальше
        // она всё равно уезжает от прицела, а стоит каждый отскок дорого.
        // Точный проход досчитывает выбранную траекторию целиком.
        int bounces = fine ? 12 : 3;

        while (t < MaxFlight)
        {
            // Доворот ракеты — до интегрирования, как и в FixedUpdate снаряда:
            // Home() переписывает скорость, и уже её физика превращает в путь.
            // Кончилось топливо — ракета перестаёт доворачивать и вновь весит
            // полную гравитацию, как в Projectile.Thrust.
            if (w.Homing && t >= HomingDelay + HomingFuel)
                a = new Vector2(a.x, Physics2D.gravity.y);

            if (w.Homing && t >= HomingDelay && t < HomingDelay + HomingFuel)
            {
                Vector2 want = _homeTarget - p;
                // Ракета рвётся, дойдя до самой точки цели, а не только от касания.
                if (want.sqrMagnitude < 0.04f) return true;

                float cur = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                float aim = Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg;
                float turned = Mathf.MoveTowardsAngle(cur, aim, HomingTurn * dt) * Mathf.Deg2Rad;
                float speed = Mathf.Max(v.magnitude, w.LaunchSpeed * 0.55f);
                v = new Vector2(Mathf.Cos(turned), Mathf.Sin(turned)) * speed;
            }

            // Тот же порядок, что в физике Unity: сначала силы к скорости,
            // потом скорость к позиции.
            Vector2 vNext = v + a * dt;
            Vector2 next = p + vNext * dt;

            if (next.y < DestructibleTerrain.WaterLevel) return false;
            if (next.x < 0f || next.x > DestructibleTerrain.WorldWidth) return false;

            if (t + dt >= fuse) { p = next; return true; }

            // Шаг летит длинным: при полном заряде базука проходит за кадр
            // физики больше юнита, и проверка одного лишь конца шага
            // прошивала насквозь и гребень, и червя. Идём по отрезку
            // выборками не реже чем через треть юнита — тем же способом,
            // каким физика Unity ведёт непрерывную проверку (Continuous).
            Vector2 seg = next - p;
            float spacing = fine ? 0.3f : 0.7f;
            int samples = Mathf.Clamp(Mathf.CeilToInt(seg.magnitude / spacing), 1, 32);
            Vector2 dirN = v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.zero;

            bool hitWorm = false, hitGround = false;
            Vector2 at = next;

            for (int sIdx = 1; sIdx <= samples && !hitWorm && !hitGround; sIdx++)
            {
                Vector2 q = p + seg * (sIdx / (float)samples);

                // Снаряд рвётся коллайдером, а не центром: точный проход щупает
                // на радиус вперёд по курсу.
                Vector2 probe = fine ? q + dirN * ProjRadius : q;

                // Прямое попадание в червя: рвётся всё, кроме прыгучего — граната
                // от червя просто отскакивает, как и в Projectile.OnCollisionEnter2D.
                if (!w.Bouncy)
                {
                    for (int i = 0; i < _worms.Count; i++)
                    {
                        var o = _worms[i];
                        if (o == null || o.IsDead || o == shooter) continue;
                        if (((Vector2)o.transform.position - probe).sqrMagnitude < HitRadius * HitRadius)
                        { hitWorm = true; break; }
                    }

                    if (!hitWorm)
                        for (int i = 0; i < _crates.Count; i++)
                        {
                            var cr = _crates[i];
                            if (cr == null) continue;
                            if (((Vector2)cr.transform.position - probe).sqrMagnitude < CrateRadius * CrateRadius)
                            { hitWorm = true; break; }
                        }
                }

                // Круг, а не точка: снаряд с радиусом задевает крону дерева или
                // гребень, сквозь которые точечная выборка проскакивала — и расчёт
                // расходился с физикой на десяток юнитов.
                if (!hitWorm)
                    hitGround = fine ? SolidAround(terrain, probe, ProjRadius) : terrain.IsSolidWorld(probe);

                if (hitWorm || hitGround) at = q;
            }

            if (hitWorm || hitGround)
            {
                next = at;
                // Шаг крупный — половиним его и подходим к точке касания ближе.
                if (dt > touch) { dt *= 0.5f; continue; }

                if (hitWorm || !w.Bouncy) { p = next; return true; }

                // Отскоки кончились — считаем, что легла тут.
                if (--bounces <= 0) { p = next; return w.Fuse > 0f; }

                // Граната живёт до фитиля: отскок с той же упругостью 0.45.
                var n = SurfaceNormal(terrain, next);
                v = Vector2.Reflect(vNext, n) * 0.45f;
                p = next + n * 0.12f;
                t += dt;
                dt = fine ? Fine : Coarse;
                if (v.sqrMagnitude < 2.2f) return w.Fuse > 0f;   // легла и лежит
                continue;
            }

            p = next;
            v = vNext;
            t += dt;
        }
        return false;
    }

    /// Нормаль поверхности по маске: считаем, в какую сторону от точки больше пустоты.
    static Vector2 SurfaceNormal(DestructibleTerrain t, Vector2 p)
    {
        Vector2 n = Vector2.zero;
        const float s = 2f / DestructibleTerrain.PixelsPerUnit;
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var o = new Vector2(dx * s, dy * s);
                if (!t.IsSolidWorld(p + o)) n += o;
            }
        return n.sqrMagnitude < 1e-6f ? Vector2.up : n.normalized;
    }

    /// Дробовик: мгновенный луч на 45 юнитов, без набора силы.
    static BotShot Hitscan(Worm shooter, int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        var best = new BotShot { Score = float.NegativeInfinity };
        var terrain = GameManager.I.Terrain;

        for (int facing = -1; facing <= 1; facing += 2)
            for (float ang = -80f; ang <= 85f; ang += 4f)
            {
                float rad = ang * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad) * facing, Mathf.Sin(rad));
                Vector2 origin = (Vector2)shooter.transform.position + dir * 0.95f;
                Vector2 hit = origin + dir * 45f;

                for (float d = 0f; d < 45f; d += 0.2f)
                {
                    Vector2 p = origin + dir * d;
                    if (p.y < DestructibleTerrain.WaterLevel ||
                        p.x < 0f || p.x > DestructibleTerrain.WorldWidth) { hit = p; break; }
                    if (terrain.IsSolidWorld(p)) { hit = p; break; }

                    bool stop = false;
                    for (int i = 0; i < _worms.Count; i++)
                    {
                        var o = _worms[i];
                        if (o == null || o.IsDead || o == shooter) continue;
                        if (((Vector2)o.transform.position - p).sqrMagnitude < HitRadius * HitRadius)
                        { hit = p; stop = true; break; }
                    }
                    if (stop) break;
                }

                var cand = new BotShot
                {
                    Found = true, Weapon = weaponIndex, Angle = ang, Facing = facing,
                    Charge = 1f, Impact = hit, Score = Score(hit, w, shooter)
                };
                if (cand.Score > best.Score) best = cand;
            }
        return best;
    }

    /// Самонаводящаяся ракета. Целится она меткой на карте, а не стволом,
    /// поэтому и перебор идёт от метки: берём ближайших врагов, ставим крестик
    /// в корпус и чуть выше головы — из-под свода и с обратного склона ракета
    /// заходит сверху, — и для каждой метки ищем пару «угол и сила», с которой
    /// она доходит. Ствол при этом почти свободен: доворот вытянет ракету на
    /// метку с любого разумного угла, и перебор нужен затем, чтобы найти тот,
    /// с которого она не воткнётся в породу на первых метрах.
    ///
    /// Раньше ракета шла общим перебором, а цель ей выбирал HomeTargetFor —
    /// ближайший враг в сторону ствола. Отсюда и брались её главные глупости:
    /// пущенная в кучу, она сворачивала к тому, кто ближе, а не к тому, по кому
    /// считался выстрел.
    static BotShot Homing(Worm shooter, int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        var best = new BotShot { Score = float.NegativeInfinity };
        Vector2 from = shooter.transform.position;

        _targets.Clear();
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            _targets.Add(o);
        }
        _targets.Sort((x, y) =>
            Vector2.SqrMagnitude((Vector2)x.transform.position - from)
            .CompareTo(Vector2.SqrMagnitude((Vector2)y.transform.position - from)));

        for (int i = 0; i < _targets.Count && i < 3; i++)
        {
            Vector2 t = _targets[i].transform.position;
            foreach (float lift in _homingLifts)
            {
                Vector2 mark = t + Vector2.up * lift;

                // Червь разворачивается к метке, как и у игрока: крестик сам
                // поворачивает червя (Worm.MarkInput), а стрелять ракетой в
                // противоположную сторону смысла нет — доворот съест топливо
                // на разворот и до цели уже не дотянет.
                int facing = mark.x >= from.x ? 1 : -1;

                _homeForced = true;
                _homeMark = mark;

                // Силу перебираем от трети: слабее ракета не успевает уйти
                // от собственных ног, а доворот всё равно уравнивает разницу.
                for (float c = 0.3f; c <= 1.0001f; c += _skills.ChargeStep)
                    for (float a = -85f; a <= 85f; a += _skills.AngleStep)
                    {
                        var cand = TryCore(shooter, weaponIndex, a, facing,
                                           Mathf.Min(c, 1f), Coarse, false);
                        if (!cand.Found) continue;
                        cand.HasMark = true;
                        cand.Mark = mark;
                        Consider(ref best, cand);
                    }
            }
        }

        _homeForced = false;
        return best;
    }

    /// Насколько выше червя ставится метка. Ноль — прямо в корпус; полтора
    /// юнита — над головой, откуда ракета сваливается сверху и не цепляет
    /// козырёк, под которым враг сидит.
    static readonly float[] _homingLifts = { 0f, 1.5f };

    /// Налёт: бомбы падают с неба, поэтому он достаёт туда, куда снаряд не
    /// летит, — за гребень, на дно каньона, на соседний остров. Точку задаёт
    /// крестик (Worm.CallAirStrike берёт её из метки), а значит перебирать
    /// углы больше незачем: пробуем сами точки вокруг ближайших врагов и
    /// считаем ту самую пятёрку бомб, которая на них посыплется.
    ///
    /// Раньше точка искалась лучом прицела, и налёт наследовал все его беды:
    /// за гребнем бомбы ложились на гребень, а из ямы — в двух шагах от ног.
    static BotShot Strike(Worm shooter, int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        var best = new BotShot { Score = float.NegativeInfinity };

        // Только ближайшая тройка: пятёрка бомб на каждую точку — это уже
        // сотня полётов, а по дальнему краю карты налёт всё равно не лучший
        // выбор.
        _targets.Clear();
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            _targets.Add(o);
        }
        _targets.Sort((x, y) =>
            Vector2.SqrMagnitude((Vector2)x.transform.position - (Vector2)shooter.transform.position)
            .CompareTo(Vector2.SqrMagnitude((Vector2)y.transform.position - (Vector2)shooter.transform.position)));

        for (int i = 0; i < _targets.Count && i < 3; i++)
        {
            Vector2 t = _targets[i].transform.position;

            // Смещения вокруг врага: строй бомб идёт с шагом 2,2 юнита и
            // сносится в сторону, куда смотрит червь, поэтому середина строя
            // редко оказывается лучшей точкой.
            foreach (float off in _strikeOffsets)
            {
                float x = Mathf.Clamp(t.x + off, 0f, DestructibleTerrain.WorldWidth);

                // Червь разворачивается к метке — как игрок, которому крестик
                // сам поворачивает червя (Worm.MarkInput). От этого зависит
                // снос бомб, так что и считаем с тем же разворотом.
                int facing = x >= shooter.transform.position.x ? 1 : -1;
                Vector2 d = new Vector2(x, t.y) - (Vector2)shooter.transform.position;
                float ang = d.sqrMagnitude > 0.04f
                          ? Mathf.Clamp(Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg, -85f, 85f)
                          : 0f;

                var cand = new BotShot
                {
                    Found = true, Weapon = weaponIndex, Angle = ang, Facing = facing, Charge = 1f,
                    Impact = new Vector2(x, t.y), HasMark = true, Mark = new Vector2(x, t.y),
                    // Минный удар не рвётся, а ложится: урон тот же, но случится
                    // ли он — вопрос чужого хода. Берём его вполовину, той же
                    // мерой, что и мину под ногами в Drop.
                    Score = StrikeScore(shooter, w, x, facing) * (w.Plants ? 0.5f : 1f)
                };
                if (cand.Score > best.Score) best = cand;
            }
        }
        return best;
    }

    static readonly float[] _strikeOffsets = { 0f, -1.1f, 1.1f, -2.2f, 2.2f, -4.4f, 4.4f };

    /// Куда падает бомба налёта, по точке рождения. Ветер их не сносит, они не
    /// скачут и не тикают, поэтому у всех пяти налётов — от обычного до осла —
    /// полёт один и тот же, и различаются они только начинкой. Пока это была
    /// одна пятёрка бомб, считать её заново было дёшево; на пяти налётах
    /// перебор дорос до сотни лишних полётов на каждую точку прицела, и план
    /// стоил сто миллисекунд вместо двадцати.
    static readonly Dictionary<long, Vector2> _strikeFall = new Dictionary<long, Vector2>();

    /// Пятёрка бомб от верхней кромки карты: те же точки рождения и та же
    /// начальная скорость, что в Worm.CallAirStrike, и тот же полёт.
    static float StrikeScore(Worm shooter, Weapon w, float x, int facing)
    {
        float top = DestructibleTerrain.WorldHeight + 6f;
        int n = Mathf.Max(1, w.Burst);
        float sum = 0f;

        for (int i = 0; i < n; i++)
        {
            float dx = (i - (n - 1) * 0.5f) * 2.2f;
            Vector2 p = new Vector2(x + dx - facing * 3f, top + i * 0.6f);
            Vector2 v = new Vector2(facing * 2.5f, -4f);

            // Ключ — сама точка рождения с точностью до сантиметра и сторона
            // сноса. Кэш живёт один расчёт хода: карта и черви за это время
            // не меняются.
            long key = ((long)Mathf.RoundToInt(p.x * 100f) << 24)
                     ^ ((long)Mathf.RoundToInt(p.y * 100f) << 2) ^ (facing > 0 ? 1L : 0L);
            if (!_strikeFall.TryGetValue(key, out var hit))
            {
                hit = Fly(w, shooter, ref p, ref v, Coarse, false) ? p : NoFall;
                _strikeFall[key] = hit;
            }
            if (hit != NoFall) sum += Score(hit, w, shooter);
        }
        return sum;
    }

    /// «Бомба никуда не долетела»: за край карты и заведомо мимо любой оценки.
    static readonly Vector2 NoFall = new Vector2(-9999f, -9999f);

    /// Закладка под ноги: динамит и мина ложатся на месте, овца уходит вперёд
    /// сама. Ход на этом кончается, но не жизнь — окно отхода даёт боту
    /// несколько секунд ногами, и урон себе считается уже с той точки, куда он
    /// успеет уйти. Без этой поправки динамит, самое сильное оружие в игре,
    /// всегда выглядел самоубийством, и бот не брал его ни разу.
    static BotShot Drop(Worm shooter, int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        var best = new BotShot { Score = float.NegativeInfinity };
        bool mine = w.Kind == WeaponKind.Mine;

        // Сколько у бота времени на побег: динамит рвётся по фитилю, мина ждёт
        // чужого хода, а окно отхода всё равно не длиннее пяти секунд.
        float run = mine ? 4.5f : Mathf.Min(w.Fuse > 0f ? w.Fuse : 3f, 4.5f);

        for (int facing = -1; facing <= 1; facing += 2)
        {
            Vector2 pos = (Vector2)shooter.transform.position + new Vector2(facing * 0.6f, 0f);
            Vector2 boom = pos;
            if (w.Walker && !SheepBoom(shooter, w, pos, facing, out boom)) continue;

            // Воронка обязана накрыть врага: одной премии за близость мало —
            // закладок на матч пара штук, и тратить их на перекопанный склон
            // бот не должен.
            if (!EnemyNear(shooter, boom, w.BlastRadius + 1f)) continue;

            Vector2 escape = EscapeTo(shooter, boom, run);
            var cand = new BotShot
            {
                Found = true, Weapon = weaponIndex, Angle = 0f, Facing = facing, Charge = 1f,
                Impact = boom,
                // Мина ждёт, пока на неё наступят: урон тот же, а случится ли он —
                // вопрос чужого хода, поэтому берём его вполовину.
                Score = Score(boom, w, shooter, escape) * (mine ? 0.5f : 1f)
            };
            if (cand.Score > best.Score) best = cand;
        }
        return best;
    }

    /// Есть ли поблизости хоть один живой враг.
    static bool EnemyNear(Worm shooter, Vector2 pos, float reach)
    {
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            if (((Vector2)o.transform.position - pos).sqrMagnitude <= reach * reach) return true;
        }
        return false;
    }

    /// Куда бот успеет уйти от закладки, пока горит фитиль: шаг за шагом по
    /// поверхности в обе стороны, пока есть куда ставить ногу. Берём ту сторону,
    /// где к взрыву он окажется дальше от воронки, — ровно то же правило, по
    /// которому он побежит в BotInput.
    static Vector2 EscapeTo(Worm shooter, Vector2 boom, float seconds)
    {
        var terrain = GameManager.I.Terrain;
        Vector2 from = shooter.transform.position;
        // Ходит червь на 4,2, но по склонам, с разгоном и разворотом выходит меньше.
        float limit = 3.4f * seconds;
        Vector2 best = from;

        for (int dir = -1; dir <= 1; dir += 2)
        {
            Vector2 p = from;
            for (float d = 0.5f; d <= limit; d += 0.5f)
            {
                float x = from.x + dir * d;
                if (x < 1f || x > DestructibleTerrain.WorldWidth - 1f) break;

                float h = terrain.SurfaceHeightWorld(x);
                if (h < DestructibleTerrain.WaterLevel + 0.4f) break;  // дальше море
                if (h > p.y + 1.2f) break;                             // отвесная стена
                p = new Vector2(x, h + 0.5f);
            }
            if (Vector2.Distance(p, boom) > Vector2.Distance(best, boom)) best = p;
        }
        return best;
    }

    /// Пробег овцы: идёт по поверхности со своей скоростью, рвётся от червя под
    /// носом и догорает за пять секунд. Модель грубая, по высоте поверхности,
    /// зато той же длины, что настоящий пробег. false — овца ушла в воду:
    /// взрыва не будет, и плана тоже. Супер-овца этим пробегом только
    /// начинается: добежав до отрыва, она уходит в небо, и дальше её считает
    /// SheepFlight.
    static bool SheepBoom(Worm shooter, Weapon w, Vector2 from, int dir, out Vector2 boom)
    {
        var terrain = GameManager.I.Terrain;
        const float Speed = 3.6f;      // Projectile.WalkSpeed
        float walkTime = w.LiftAfter > 0f ? w.LiftAfter : (w.Fuse > 0f ? w.Fuse : 5f);
        float limit = Speed * walkTime;
        Vector2 p = from;
        boom = from;

        for (float d = 0.3f; d <= limit; d += 0.3f)
        {
            float x = from.x + dir * d;
            if (x < 1f || x > DestructibleTerrain.WorldWidth - 1f) break;

            float h = terrain.SurfaceHeightWorld(x);
            if (h < DestructibleTerrain.WaterLevel + 0.2f) return false;
            // Через что овца перепрыгивает, она берёт: подскок 6,5 — это метра
            // два с половиной. Выше — стена, там она и догорит.
            if (h > p.y + 2.5f) break;

            p = new Vector2(x, h + 0.3f);
            boom = p;

            for (int i = 0; i < _worms.Count; i++)
            {
                var o = _worms[i];
                if (o == null || o.IsDead || o == shooter) continue;
                if (((Vector2)o.transform.position - p).sqrMagnitude < 0.81f) return true;
            }
        }

        if (w.LiftAfter > 0f) boom = SheepFlight(shooter, w, boom, dir, walkTime);
        return true;
    }

    /// Полёт супер-овцы после отрыва: те же скорости, что в Projectile.Fly, и
    /// тот же профиль подъёма (Projectile.FlyRise). Идём шагом по времени и
    /// смотрим не высоту поверхности, а саму маску: овца летит над рельефом,
    /// и «поверхность под точкой» о своде пещеры ничего не скажет.
    static Vector2 SheepFlight(Worm shooter, Weapon w, Vector2 from, int dir, float walked)
    {
        var terrain = GameManager.I.Terrain;
        const float Dt = 0.05f;
        float left = Mathf.Max(0f, (w.Fuse > 0f ? w.Fuse : 9f) - walked);
        Vector2 p = from;

        for (float t = 0f; t < left; t += Dt)
        {
            p += new Vector2(dir * Projectile.FlySpeed, Projectile.FlyRise(t)) * Dt;

            if (p.x < 0.5f || p.x > DestructibleTerrain.WorldWidth - 0.5f) break;
            if (p.y < DestructibleTerrain.WaterLevel) break;
            if (p.y > DestructibleTerrain.WorldHeight + 4f) continue;   // над картой лететь можно
            if (terrain.IsSolidWorld(p)) break;

            for (int i = 0; i < _worms.Count; i++)
            {
                var o = _worms[i];
                if (o == null || o.IsDead || o == shooter) continue;
                if (((Vector2)o.transform.position - p).sqrMagnitude < 0.81f) return p;
            }
        }
        return p;
    }

    /// Удар вплотную. Модель полёта тут ни при чём: важно, кто стоит рядом и
    /// куда его унесёт — бита у воды стоит дороже любого попадания.
    static BotShot Melee(Worm shooter, int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        bool punch = w.Kind == WeaponKind.FirePunch;
        var best = new BotShot { Score = float.NegativeInfinity };

        for (int facing = -1; facing <= 1; facing += 2)
        {
            Vector2 origin = (Vector2)shooter.transform.position + new Vector2(facing * 0.9f, 0.1f);
            float score = 0f;
            bool any = false;

            for (int i = 0; i < _worms.Count; i++)
            {
                var o = _worms[i];
                if (o == null || o.IsDead || o == shooter) continue;
                Vector2 d = (Vector2)o.transform.position - origin;
                if (d.magnitude > 1.7f || d.x * facing < -0.4f) continue;

                any = true;
                var push = punch ? new Vector2(facing * 4f, 16f) : new Vector2(facing * 20f, 7f);
                float eff = Mathf.Min(w.Damage, o.Health);
                bool kill = w.Damage >= o.Health || KnockedIntoWater(o, push);
                float value = kill ? o.Health + 30f : eff;

                if (o.Team != shooter.Team) score += value;
                else score -= value * 2.2f + (kill ? 80f : 0f);
            }

            if (!any) continue;
            var cand = new BotShot
            {
                Found = true, Weapon = weaponIndex, Angle = 0f, Facing = facing, Charge = 1f,
                Impact = origin, Score = score
            };
            if (cand.Score > best.Score) best = cand;
        }
        return best;
    }

    /// Улетит ли червь от удара в воду: та же баллистика, только вместо снаряда
    /// само тело. Порода останавливает полёт, вода — засчитывает утопление.
    static bool KnockedIntoWater(Worm victim, Vector2 push)
    {
        var terrain = GameManager.I.Terrain;
        Vector2 p = victim.transform.position;
        Vector2 v = push;
        const float dt = 0.04f;   // точность полёта тела тут не нужна: важен только исход
        var g = Physics2D.gravity;

        for (float t = 0f; t < 3f; t += dt)
        {
            v += g * dt;
            Vector2 next = p + v * dt;
            if (next.y < DestructibleTerrain.WaterLevel) return true;
            if (next.x < 0.5f || next.x > DestructibleTerrain.WorldWidth - 0.5f) return false;
            if (SolidAround(terrain, next, 0.45f)) return false;
            p = next;
        }
        return false;
    }

    /// Оценка выстрела по формуле Combat.Detonate, вместе с цепочкой по ящикам.
    ///
    /// shooterAt задан для закладок: динамит рвётся через три секунды, и к этому
    /// мигу стрелок стоит уже не там, где положил его. Считать урон себе по
    /// месту закладки значило бы вычеркнуть самое сильное оружие в игре.
    static float Score(Vector2 pos, Weapon w, Worm shooter, Vector2? shooterAt = null)
    {
        // Кассета рассыпается осколками — считаем её как одну воронку пошире.
        // Осколочные снаряды бьют шире и больнее, чем говорит их собственная воронка.
        float radius = w.Cluster > 0 ? w.BlastRadius * 1.45f : w.BlastRadius;
        float damage = w.Cluster > 0 ? w.Damage * 1.6f : w.Damage;

        float score = 0f;

        // Задетый ящик рвётся сам: считаем его воронку тем же порядком,
        // иначе бот не увидит, что удачное попадание бьёт вдвое.
        for (int i = 0; i < _crates.Count; i++)
        {
            var cr = _crates[i];
            if (cr == null) continue;
            float d = Vector2.Distance(cr.transform.position, pos);
            if (d <= radius + 0.6f) score += Blast(cr.transform.position, Crate.BlastRadius, Crate.BlastDamage, shooter, shooterAt);
        }

        return score + Blast(pos, radius, damage, shooter, shooterAt) + Near(pos, radius, shooter);
    }

    /// Маленькая премия за то, что воронка легла рядом с врагом. Настоящий урон
    /// она не перевешивает — зато отличает «подорвал укрытие в двух шагах от
    /// врага» от «выстрелил в море». Без неё все недолёты стоили ровно ноль,
    /// бот не видел между ними разницы и предпочитал не стрелять вовсе.
    static float Near(Vector2 pos, float radius, Worm shooter)
    {
        float best = 0f;
        float reach = radius * 3f + 6f;

        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            float d = Vector2.Distance(o.transform.position, pos);
            if (d > reach) continue;
            // Раненый враг притягивает сильнее целого: если уж копать склон,
            // то у того, кого следующим выстрелом получится добить.
            float weight = 5f + 4f * _skills.Focus * (1f - o.Health / 100f);
            best = Mathf.Max(best, weight * (1f - d / reach));
        }
        return best;
    }

    /// Одна воронка: урон врагу в плюс, свои и сам стрелок — в минус,
    /// причём дороже, чем стоит попадание.
    static float Blast(Vector2 pos, float radius, float damage, Worm shooter, Vector2? shooterAt = null)
    {
        float score = 0f;
        int enemies = 0;

        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead) continue;

            // Стрелок в расчёте закладки стоит не там, где стоит сейчас, а там,
            // куда успеет убежать. Это уже догадка — и строить на ней вторую
            // догадку (что его оттуда сдует в море) не стоит.
            bool guessed = o == shooter && shooterAt.HasValue;
            Vector2 at = guessed ? shooterAt.Value : (Vector2)o.transform.position;
            float dist = Vector2.Distance(at, pos);
            float range = radius + 0.6f;
            if (dist > range) continue;

            float dmg = damage * (1f - Mathf.Clamp01(dist / range));
            float eff = Mathf.Min(dmg, o.Health);
            bool kill = dmg >= o.Health;

            // Цена червя, кем бы он ни был. Урон — только основа; сверху идёт
            // всё, ради чего человек и выбирает, куда бить.
            float value = eff + (kill ? 30f : 0f);

            // Выбить в море: взрыв не только ранит, но и отбрасывает — тем же
            // импульсом, что в Combat.Detonate. Улетевший в воду червь потерян
            // целиком, сколько бы здоровья ему ни осталось.
            if (!kill && !guessed && _skills.Focus > 0f && Tossed(o, at, pos, dmg))
                value += (o.Health - eff + 25f) * _skills.Focus;

            // Добить: последний червь команды дороже прочих — с ним кончается
            // не червь, а соперник.
            if (kill && LastOfTeam(o)) value += 40f * _skills.Focus;

            if (o.Team != shooter.Team) { score += value; enemies++; }
            else if (o == shooter) score -= value * 3f + (kill ? 90f : 0f);
            else score -= value * 2.2f + (kill ? 50f : 0f);
        }

        // Куча: накрыть двоих одной воронкой лучше, чем каждого по разу, —
        // за второй ход соперник успевает и отойти, и ответить.
        if (enemies > 1) score += 10f * _skills.Focus * (enemies - 1);
        return score;
    }

    /// Последний живой червь своей команды: его смерть выносит команду целиком.
    static bool LastOfTeam(Worm victim)
    {
        int alive = 0;
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o != null && !o.IsDead && o.Team == victim.Team && ++alive > 1) return false;
        }
        return true;
    }

    /// Улетит ли червь от взрыва в воду. Импульс — тот самый, что раздаёт
    /// Combat.Detonate, масса тела единичная, поэтому импульс сразу скорость.
    ///
    /// Полёт считаем грубо: шаг 0,12 с и одна проба маски вместо круга.
    /// Точность тут ни к чему — важен только исход, — а зовётся эта проверка
    /// из перебора, где счёт траекторий идёт на тысячи.
    static bool Tossed(Worm victim, Vector2 at, Vector2 blast, float dmg)
    {
        // Червю на плато посреди карты море не грозит — и считать нечего.
        if (at.y - DestructibleTerrain.WaterLevel > 10f) return false;

        Vector2 dir = Vector2.Distance(at, blast) < 0.05f ? Vector2.up : (at - blast).normalized;
        dir = (dir + Vector2.up * 0.35f).normalized;
        Vector2 v = dir * (6f + dmg * 0.32f);

        var terrain = GameManager.I.Terrain;
        var g = Physics2D.gravity;
        Vector2 p = at;
        const float dt = 0.12f;

        for (float t = 0f; t < 2.5f; t += dt)
        {
            v += g * dt;
            Vector2 next = p + v * dt;
            if (next.y < DestructibleTerrain.WaterLevel) return true;
            if (next.x < 0.5f || next.x > DestructibleTerrain.WorldWidth - 0.5f) return false;
            if (terrain.IsSolidWorld(next)) return false;
            p = next;
        }
        return false;
    }

    // --- верёвка -----------------------------------------------------------

    /// Куда бот собрался качнуться: за что цепляться, в какую сторону
    /// раскачиваться и где он окажется.
    public struct BotSwing
    {
        public bool Found;
        public float Angle;     // угол броска гарпуна, градусы к горизонту
        public int Facing;      // в какую сторону бросаем
        public Vector2 Anchor;  // куда воткнётся гарпун
        public Vector2 Release; // где по расчёту отцепимся: низшая свободная точка дуги
        public Vector2 Landing; // где по расчёту приземлимся
        public int SwingDir;    // куда раскачиваемся: 1 вправо, -1 влево
        public float Gain;      // насколько ближе к врагу станет, юнитов
    }

    /// Меньше этого выигрыш не стоит патрона верёвки: пройти столько же можно
    /// и ногами.
    const float SwingGain = 7f;

    /// Верёвка-ниндзя для бота. Ходить бот умеет только по земле, и враг за
    /// проливом или этажом выше для него был недосягаем: перебор пар «угол и
    /// сила» с той стороны ничего не находил, подойти не выходило, а телепорт
    /// один на матч. Верёвка — то же перемещение, только своими руками.
    ///
    /// Модель качелей простая и честная: гарпун цепляется за первую породу по
    /// лучу, червь висит на маятнике и разгоняется, падая по дуге. Отцепляется
    /// он в низшей точке, до которой дуга свободна: под якорем, зацепленным за
    /// свод, это её низ, а на склоне — то место, где дуга упирается в землю.
    /// Скорость там — по касательной к дуге и равна √(2g·h), где h — высота
    /// падения; дальше это обычный полёт телом, тот же шаг, что и у снаряда.
    /// Ровно так же поступит и BotInput: отпустит верёвку, когда червь пройдёт
    /// расчётную точку отцепа.
    public static BotSwing PlanSwing(Worm shooter)
    {
        var gm = GameManager.I;
        var best = new BotSwing();
        if (gm == null || shooter == null || shooter.IsDead) return best;

        Snapshot();
        var terrain = gm.Terrain;
        Vector2 from = shooter.transform.position;

        // Куда, собственно, хотим: к ближайшему врагу.
        Worm enemy = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            float d = Vector2.Distance(o.transform.position, from);
            if (d < bestD) { bestD = d; enemy = o; }
        }
        if (enemy == null) return best;

        Vector2 goal = enemy.transform.position;
        int want = goal.x >= from.x ? 1 : -1;
        float now = Mathf.Abs(goal.x - from.x);

        for (int facing = -1; facing <= 1; facing += 2)
            // От двадцати градусов: гарпун цепляют и за стену впереди, не только
            // за свод над головой. Совсем полого нельзя — качели с якорем на
            // уровне глаз никуда не несут.
            for (float ang = 20f; ang <= 88f; ang += 6f)
            {
                float rad = ang * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad) * facing, Mathf.Sin(rad));
                if (!Harpoon(terrain, from, dir, out Vector2 anchor)) continue;

                float len = Vector2.Distance(from, anchor);
                if (len < 2f) continue;
                // Качели несут в сторону якоря и дальше: цепляться в другую
                // сторону от цели бессмысленно.
                if ((anchor.x - from.x) * want < -0.5f) continue;
                if (!ReleasePoint(terrain, anchor, from, out Vector2 rel)) continue;

                // Вся высота падения по дуге ушла в разгон; скорость — по
                // касательной, а ReleaseKick добавляет подскок при отцепе.
                float drop = from.y - rel.y;
                if (drop < 1.5f) continue;
                float speed = Mathf.Sqrt(2f * Mathf.Abs(Physics2D.gravity.y) * drop);

                Vector2 r = rel - anchor;
                var tangent = new Vector2(-r.y, r.x).normalized;
                if (tangent.x * want < 0f) tangent = -tangent;

                Vector2 land = FlightOf(terrain, rel, tangent * speed + new Vector2(0f, 3.5f));
                if (land.y < DestructibleTerrain.WaterLevel + 0.5f) continue;

                float gain = now - Mathf.Abs(goal.x - land.x);
                if (gain < SwingGain || gain <= best.Gain) continue;

                best = new BotSwing
                {
                    Found = true, Angle = ang, Facing = facing, Anchor = anchor,
                    Release = rel, Landing = land, SwingDir = want, Gain = gain
                };
            }

        return best;
    }

    /// Куда воткнётся гарпун: первая порода по лучу, не ближе 0,7 юнита —
    /// тот же порог, что у Rope.Throw.
    static bool Harpoon(DestructibleTerrain terrain, Vector2 from, Vector2 dir, out Vector2 hit)
    {
        hit = default;
        for (float d = 0.7f; d <= Rope.MaxLength; d += 0.25f)
        {
            Vector2 p = from + dir * d;
            if (p.x < 0.5f || p.x > DestructibleTerrain.WorldWidth - 0.5f) return false;
            if (p.y > DestructibleTerrain.WorldHeight) return false;
            if (terrain.IsSolidWorld(p)) { hit = p; return true; }
        }
        return false;
    }

    /// Где отцепляться: идём по дуге от червя вниз, пока свободно, и берём
    /// последнюю свободную точку. Под сводом это низ дуги, на склоне — место,
    /// где дуга упирается в землю: волочиться по склону на верёвке это не
    /// полёт, а застревание. Меньше трёх свободных шагов — не качели.
    static bool ReleasePoint(DestructibleTerrain terrain, Vector2 anchor, Vector2 from,
                             out Vector2 release)
    {
        const int Steps = 12;
        float len = Vector2.Distance(anchor, from);
        float a0 = Mathf.Atan2(from.y - anchor.y, from.x - anchor.x);
        float a1 = -Mathf.PI * 0.5f;   // низ дуги

        release = from;
        int free = 0;
        for (int i = 1; i <= Steps; i++)
        {
            float a = Mathf.Lerp(a0, a1, (float)i / Steps);
            Vector2 p = anchor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * len;
            if (p.y < DestructibleTerrain.WaterLevel + 0.3f) break;
            if (terrain.IsSolidWorld(p)) break;
            release = p;
            free++;
        }
        return free >= 3;
    }

    /// Полёт тела после отцепа: та же баллистика, только вместо снаряда червь.
    /// Возвращает точку, где он встретит породу, воду или край карты.
    static Vector2 FlightOf(DestructibleTerrain terrain, Vector2 p, Vector2 v)
    {
        var g = Physics2D.gravity;
        const float dt = 0.05f;

        for (float t = 0f; t < 4f; t += dt)
        {
            v += g * dt;
            Vector2 next = p + v * dt;
            if (next.y < DestructibleTerrain.WaterLevel) return next;
            if (next.x < 1f || next.x > DestructibleTerrain.WorldWidth - 1f) return p;
            if (SolidAround(terrain, next, 0.45f)) return p;
            p = next;
        }
        return p;
    }

    /// Дальше этого за ящиком не ходим: дорога съест ход целиком.
    const float FetchRange = 16f;
    /// С какой скоростью бот реально доезжает до цели: MoveSpeed равен 4,2,
    /// но склоны, прыжки через уступы и разгон съедают треть.
    const float FetchSpeed = 2.8f;
    /// Сколько секунд оставляем себе на выстрел после подбора: думать,
    /// наводиться и копить силу. Меньше — и ящик достаётся ценой хода.
    const float ShotReserve = 8f;
    /// Куда прыгнуть телепортом. Точку выбираем как игрок крестиком: смотрим
    /// на всю карту, а не на луч прицела, и берём место, где стоять лучше, чем
    /// сейчас. «Лучше» — это сухо (вода прибывает каждый ход), это на удобной
    /// дистанции до ближайшего врага, это со свободной линией до него и по
    /// возможности выше него: сверху вниз стреляется легче всего.
    ///
    /// Возвращает не оценку места, а прибавку к тому, где червь стоит: прыжок
    /// стоит целого хода, и менять шило на мыло незачем. Тонущему червю та же
    /// формула сама даёт огромную прибавку — сухая земля весит больше всего
    /// остального.
    public static bool PlanJump(Worm shooter, out Vector2 point, out float gain)
    {
        point = shooter != null ? (Vector2)shooter.transform.position : Vector2.zero;
        gain = 0f;

        var gm = GameManager.I;
        if (gm == null || shooter == null || shooter.IsDead) return false;
        var terrain = gm.Terrain;
        if (terrain == null) return false;

        Snapshot();

        Worm enemy = null;
        float bestD = float.MaxValue;
        Vector2 from = shooter.transform.position;
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            float d = Vector2.Distance(o.transform.position, from);
            if (d < bestD) { bestD = d; enemy = o; }
        }
        if (enemy == null) return false;

        float here = SpotScore(terrain, from, shooter, enemy);
        float bestScore = float.NegativeInfinity;
        Vector2 bestPoint = from;

        // Шаг в полтора юнита: червю в поперечнике один, и место, найденное с
        // таким шагом, не «почти подходит», а подходит целиком.
        for (float x = 2f; x <= DestructibleTerrain.WorldWidth - 2f; x += 1.5f)
        {
            // Сверху вниз, чтобы верхний ярус многоярусной карты нашёлся
            // раньше нижнего: он и лучше — с него видно дальше.
            for (float y = DestructibleTerrain.WorldHeight - 1.5f;
                 y > DestructibleTerrain.WaterLevel + 2f; y -= 0.5f)
            {
                var p = new Vector2(x, y);
                if (!Standing(terrain, p)) continue;

                float sc = SpotScore(terrain, p, shooter, enemy);
                if (sc > bestScore) { bestScore = sc; bestPoint = p; }

                // Нашли площадку — следующие полтора юнита вниз это она же.
                y -= 1.5f;
            }
        }

        if (bestScore == float.NegativeInfinity) return false;

        point = bestPoint;
        gain = bestScore - here;
        return true;
    }

    /// Место, где червю есть куда встать: свободный столбик в его рост (те же
    /// пробы, что в Teleport.FindRoom — иначе прыжок сорвётся) и порода под
    /// ногами. Без опоры это не площадка, а воздух над пропастью.
    static bool Standing(DestructibleTerrain terrain, Vector2 p)
    {
        if (terrain.IsSolidWorld(p)) return false;
        if (terrain.IsSolidWorld(p + Vector2.up * 0.9f)) return false;
        if (terrain.IsSolidWorld(p + new Vector2(0.45f, 0.45f))) return false;
        if (terrain.IsSolidWorld(p + new Vector2(-0.45f, 0.45f))) return false;
        return terrain.IsSolidWorld(p + Vector2.down * 0.7f)
            || terrain.IsSolidWorld(p + Vector2.down * 1.1f);
    }

    /// Чего стоит место под ногами. Считается и для точки прыжка, и для той,
    /// где червь стоит сейчас, — разница между ними и есть польза телепорта.
    static float SpotScore(DestructibleTerrain terrain, Vector2 p, Worm shooter, Worm enemy)
    {
        // Сухо. Вода прибывает каждый ход, и низкая площадка — это место,
        // с которого следующий ход придётся начинать по грудь в море.
        float score = Mathf.Min(p.y - DestructibleTerrain.WaterLevel, 14f) * 1.6f;

        Vector2 e = enemy.transform.position;
        float d = Vector2.Distance(p, e);

        // Девять юнитов — дистанция уверенного выстрела: и базука долетает,
        // и своей воронкой себя не задевает.
        score -= Mathf.Abs(d - 9f) * 1.1f;
        if (d < 5f) score -= 30f;

        // Свободная линия до врага — это выстрел уже со следующего хода.
        if (Visible(terrain, p, e)) score += 25f;

        // Сверху вниз стреляется легче: и видно дальше, и снаряд не упирается
        // в собственный склон.
        score += Mathf.Clamp(p.y - e.y, -6f, 6f) * 1.2f;

        // В обнимку с соседом не приземляются: столкнувшиеся черви расталкивают
        // друг друга, и прыжок кончается падением с чужой головы.
        for (int i = 0; i < _worms.Count; i++)
        {
            var o = _worms[i];
            if (o == null || o.IsDead || o == shooter) continue;
            if (((Vector2)o.transform.position - p).sqrMagnitude < 4f) score -= 30f;
        }
        return score;
    }

    /// Видно ли отсюда врага: прямая линия без породы. Шаг в полюйнита —
    /// стенка тоньше него всё равно не укрытие.
    static bool Visible(DestructibleTerrain terrain, Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        float len = d.magnitude;
        if (len < 0.5f) return true;
        d /= len;
        for (float t = 0.5f; t < len - 0.5f; t += 0.5f)
            if (terrain.IsSolidWorld(from + d * t)) return false;
        return true;
    }


    /// Ближайший ящик, до которого есть дорога по земле и хватает времени.
    /// Аптечка тем дороже, чем сильнее побит червь; ящик с боезапасом стоит
    /// ровно столько, сколько стоит лишнее штучное оружие в руках.
    public static Crate BestCrate(Worm shooter, float turnTimeLeft, out float budget)
    {
        budget = 0f;
        Crate best = null;
        float bestScore = 0f;

        for (int i = 0; i < Crate.All.Count; i++)
        {
            var cr = Crate.All[i];
            if (cr == null) continue;

            Vector2 to = cr.transform.position;
            float dist = Mathf.Abs(to.x - shooter.transform.position.x);
            if (dist > FetchRange) continue;
            // Ящик этажом выше или ниже: по земле туда не дойти, а лезть бот
            // не умеет — этим займётся верёвка, когда до неё дойдут руки.
            if (Mathf.Abs(to.y - shooter.transform.position.y) > 6f) continue;
            if (!WalkableTo(shooter, to.x)) continue;

            // Времени должно хватить и на дорогу, и на выстрел после неё.
            float walk = dist / FetchSpeed + 1.2f;
            if (turnTimeLeft < walk + ShotReserve) continue;

            // Ящик у врага под ногами — это его ящик: пока дойдём, он и подберёт,
            // а мы окажемся в упор к чужому червю с пустыми руками.
            if (NearerToEnemy(shooter, to)) continue;

            float value = cr.Kind == CrateKind.Health
                ? Mathf.Max(0f, 100f - shooter.Health)
                : 30f;
            // Полному здоровью аптечка не нужна — за такой ящик ход не тратим.
            if (value < 12f) continue;

            float score = value - dist * 2f;
            if (score <= 0f || score <= bestScore) continue;

            bestScore = score;
            best = cr;
            budget = walk + 1.5f;
        }
        return best;
    }

    /// Есть ли враг, которому до этой точки ближе, чем нам.
    static bool NearerToEnemy(Worm shooter, Vector2 point)
    {
        float mine = Vector2.Distance(shooter.transform.position, point);
        var worms = GameManager.I.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var o = worms[i];
            if (o == null || o.IsDead || o.Team == shooter.Team) continue;
            if (Vector2.Distance(o.transform.position, point) < mine) return true;
        }
        return false;
    }

    /// Можно ли дойти по земле до этой отметки: поверхность на всём пути должна
    /// быть выше воды, а уступы — берущимися прыжком. Тот же приём, что у Reach,
    /// только путь известной длины и важен каждый шаг, а не конец.
    static bool WalkableTo(Worm shooter, float x)
    {
        var terrain = GameManager.I != null ? GameManager.I.Terrain : null;
        if (terrain == null || shooter == null) return false;

        float x0 = shooter.transform.position.x;
        int dir = x >= x0 ? 1 : -1;
        float prev = terrain.SurfaceHeightWorld(x0);

        for (float d = 1.2f; d <= Mathf.Abs(x - x0); d += 1.2f)
        {
            float px = x0 + dir * d;
            if (px < 1f || px > DestructibleTerrain.WorldWidth - 1f) return false;

            float h = terrain.SurfaceHeightWorld(px);
            if (h < DestructibleTerrain.WaterLevel + 0.4f) return false;
            // Стена выше прыжка или обрыв глубже, чем стоит падать.
            if (h - prev > 3.5f || prev - h > 9f) return false;
            prev = h;
        }
        return true;
    }

    /// Приближение нормального распределения: сумма двух равномерных, -1..1 по краям.
    static float Gauss(System.Random rng) =>
        (float)(rng.NextDouble() + rng.NextDouble() - 1.0);
}
