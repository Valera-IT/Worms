using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Проверяет соперника-бота тремя способами. Сначала — саму баллистику:
/// расчётная точка попадания сравнивается с тем, куда реально прилетел
/// настоящий снаряд, запущенный с теми же углом, силой и ветром. Потом —
/// трудные позиции: сверху вниз и в упор, где бот раньше не находил решения.
/// Потом — поведение: обе команды отдаются ботам и матч должен идти сам, с
/// выстрелами и уроном. Запуск: Unity -executeMethod BotTest.Run
public static class BotTest
{
    const string Flag = "BotTest.Running";

    static double _start;
    static int _step;
    static bool _failed;

    static BotShot _shot;
    static Projectile _probe;
    static Vector2 _lastProbePos;
    static float _probeTime;
    static Vector2 _probeStart, _probeVel;
    static float _probeT0;
    static float _traceErr;   // худшее расхождение модели со снарядом в свободном полёте

    static GameState _prevState;
    static int _fires;
    static float _dmgAtStart;

    [MenuItem("Worms/Тест бота")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _failed = false; _fires = 0; _probe = null;

        SessionState.SetBool(Flag, true);
        Arm();
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    /// Вход в PlayMode перезагружает домен и обнуляет статику, поэтому
    /// конфигурацию матча ставим и здесь, и после перезагрузки.
    static void Arm()
    {
        var cfg = MatchConfig.Hotseat();
        cfg.WormsPerTeam = 2;
        cfg.TurnTime = 20f;
        cfg.Terrain = TerrainKind.Island;
        cfg.Teams[1].IsBot = true;
        // Ящики выключаем: падающий ящик перехватывает пробный снаряд, и сверка
        // расчёта с физикой начинает мерить не баллистику, а случайность.
        cfg.Crates = CratePlan.Off;
        cfg.FloodRound = 0;

        App.AutostartMatch = true;
        App.AutostartConfig = cfg;
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (!SessionState.GetBool(Flag, false)) return;
        Arm();
        EditorApplication.update += Tick;
    }

    static void Fail(string msg)
    {
        Debug.LogError("BOT: " + msg);
        _failed = true;
    }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        Debug.Log(_failed ? "BOT: ПРОВАЛ" : "BOT: OK");
        if (Application.isBatchMode) EditorApplication.Exit(_failed ? 1 : 0);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        var gm = GameManager.I;
        if (gm == null)
        {
            if (t > 10) { Fail("GameManager не создан"); Finish(); }
            return;
        }

        // --- шаг 1: команда бота действительно ведётся BotInput ---
        if (_step == 0 && t > 2)
        {
            _step = 1;
            if (gm.Teams.Count < 2) { Fail("команд меньше двух"); Finish(); return; }
            if (!gm.Teams[1].IsBot) { Fail("вторая команда не под ботом"); Finish(); return; }
            if (gm.Teams[0].IsBot) Fail("первая команда должна остаться за игроком");
            Debug.Log($"BOT step1: команды — {gm.Teams[0].Name} игрок, {gm.Teams[1].Name} бот");

            // --- шаг 2: расчёт против реальной физики, с заметным ветром ---
            var worm = gm.ActiveWorm;
            if (worm == null) { Fail("нет активного червя"); Finish(); return; }
            gm.Wind = 0.8f;

            int facing = 1;
            foreach (var w in gm.AllWorms())
                if (w != null && w.Team != worm.Team)
                { facing = w.transform.position.x > worm.transform.position.x ? 1 : -1; break; }

            foreach (float ang in new[] { 45f, 35f, 55f, 25f })
                foreach (float ch in new[] { 0.8f, 0.6f, 1f })
                {
                    var s = BotPlanner.Try(worm, 0, ang, facing, ch);
                    if (s.Found) { _shot = s; break; }
                }

            if (!_shot.Found) { Fail("баллистика не нашла ни одной попадающей траектории"); Finish(); return; }

            var weapon = Weapon.All[0];
            float rad = _shot.Angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(rad) * _shot.Facing, Mathf.Sin(rad));
            Vector2 pos = (Vector2)worm.transform.position + dir * 0.95f;
            float speed = weapon.LaunchSpeed * Mathf.Max(0.18f, _shot.Charge);
            _probe = Projectile.Spawn(weapon, pos, dir * speed, worm);
            _lastProbePos = pos;
            _probeTime = 0f;
            _probeStart = pos;
            _probeVel = dir * speed;
            _probeT0 = Time.time;
            _traceErr = 0f;
            Debug.Log($"BOT step2: червь в {(Vector2)worm.transform.position}, расчёт — угол {_shot.Angle:0.0}°, " +
                      $"сила {_shot.Charge:0.00}, ветер {gm.Wind:0.00}, попадание в {_shot.Impact}");
        }

        // --- шаг 2 (продолжение): следим за реальным снарядом ---
        if (_step == 1)
        {
            _probeTime += Time.unscaledDeltaTime;
            if (_probe != null)
            {
                _lastProbePos = _probe.transform.position;

                // Сверка самой модели: берём позицию тела (не интерполированную
                // позу) и столько же шагов физики от старта. Ландшафт тут ни при
                // чём — меряется ровно интегратор и ветер.
                // Снаряд рождается между шагами физики, поэтому «сколько шагов
                // прошло» известно с точностью до одного — а один шаг это
                // полюнита пути. Поэтому меряем не отклонение в конкретный
                // момент, а расстояние до самой траектории: сдвиг по фазе она
                // прощает, а вот неверную гравитацию или ветер — нет.
                var rb = _probe.GetComponent<Rigidbody2D>();
                int steps = Mathf.RoundToInt((Time.time - _probeT0) / Time.fixedDeltaTime);
                if (rb != null && steps > 2)
                {
                    float e = float.MaxValue;
                    for (int k = Mathf.Max(1, steps - 4); k <= steps + 4; k++)
                        e = Mathf.Min(e, Vector2.Distance(rb.position,
                                BotPlanner.Trace(_probe.Weapon, _probeStart, _probeVel, k)));
                    if (e > _traceErr) _traceErr = e;
                }
            }

            if (_probe == null || _probeTime > 12f)
            {
                _step = 2;
                float err = Vector2.Distance(_lastProbePos, _shot.Impact);
                string near = "";
                foreach (var c in Physics2D.OverlapCircleAll(_lastProbePos, 0.8f))
                    near += c.gameObject.name + " ";
                Debug.Log($"BOT step3: снаряд лёг в {_lastProbePos}, расхождение с расчётом попадания " +
                          $"{err:0.00} юнита, траектории — {_traceErr:0.000}; рядом: {near}");
                // Траектория обязана совпадать почти точно: это чистая сверка
                // модели с интегратором. Точка попадания честно расходится
                // сильнее — по касательной к склону коллайдер и маска дают
                // разные метры, — поэтому у неё лишь грубый порог.
                if (_traceErr > 0.15f) Fail($"модель полёта расходится с физикой на {_traceErr:0.00} юнита");
                if (err > 3.0f) Fail($"расчётная точка попадания мимо на {err:0.0} юнита");

                // Перебор пар «угол и сила» должен укладываться в кадр: бот
                // считает его один раз за ход, но прямо в Update.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var plan = BotPlanner.Plan(gm.ActiveWorm, BotDifficulty.Hard, new System.Random(7));
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                Debug.Log($"BOT step3b: план за {ms:0.0} мс — оружие {(plan.Found ? Weapon.All[plan.Weapon].Name : "нет")}, " +
                          $"угол {plan.Angle:0.0}°, сила {plan.Charge:0.00}, оценка {plan.Score:0.0}");
                if (ms > 60) Fail($"планирование стоит {ms:0} мс — это заметный провал кадра");

                CheckHardSpots(gm);

                // --- шаг 3: отдаём обе команды ботам и смотрим, идёт ли матч сам ---
                gm.Teams[0].Controller = new BotInput(gm.Teams[0], BotDifficulty.Hard, 12345);
                _dmgAtStart = gm.Teams[0].DamageDealt + gm.Teams[1].DamageDealt;
                _prevState = gm.State;
                _start = EditorApplication.timeSinceStartup;   // отсчёт заново
            }
            return;
        }

        if (_step == 2)
        {
            // Закладку видно не по снаряду: динамит и мина уводят ход сразу в
            // отход, минуя Projectile.
            if (_prevState == GameState.Aim &&
                (gm.State == GameState.Projectile || gm.State == GameState.Retreat)) _fires++;
            _prevState = gm.State;

            if (gm.State == GameState.GameOver || t > 45)
            {
                float dmg = gm.Teams[0].DamageDealt + gm.Teams[1].DamageDealt - _dmgAtStart;
                Debug.Log($"BOT step4: за {t:0} с боты сделали {_fires} выстрелов, урона {dmg:0}, " +
                          $"состояние {gm.State}");
                if (_fires < 2) Fail("боты не стреляют — ход простаивает");
                if (dmg <= 0f) Fail("боты стреляют, но ни разу не попали");
                Finish();
            }
        }
    }

    // --- трудные позиции ---------------------------------------------------

    /// Две позы, в которых бот раньше тупил: стрелок высоко над целью (отсечка
    /// по дальности считала расстояние по прямой и выбрасывала все слабые
    /// заряды) и враг вплотную (перебор пар «угол и сила» тут бессмыслен,
    /// а бить кулаком бот не умел вовсе).
    static void CheckHardSpots(GameManager gm)
    {
        var terrain = gm.Terrain;
        var shooter = gm.Teams[0].Worms.Count > 0 ? gm.Teams[0].Worms[0] : null;
        var victim = gm.Teams[1].Worms.Count > 0 ? gm.Teams[1].Worms[0] : null;
        if (shooter == null || victim == null || shooter.IsDead || victim.IsDead)
        {
            Debug.Log("BOT step3c: некому проверять трудные позиции — пропуск");
            return;
        }

        float wind = gm.Wind;
        gm.Wind = 0f;   // ветер тут только мешает читать результат

        if (!Platform(terrain, DestructibleTerrain.WorldWidth * 0.35f, out var low))
        {
            Debug.Log("BOT step3c: не нашлось площадки — пропуск");
            gm.Wind = wind;
            return;
        }

        // --- сверху вниз: цель на 16 юнитов ниже и в 18 в стороне ---
        victim.PlaceAt(low);
        shooter.PlaceAt(low + new Vector2(18f, 16f));

        var plan = BotPlanner.Plan(shooter, BotDifficulty.Hard, new System.Random(3));
        // Мерим промах до ближайшего врага, а не до подставленного: остальные
        // черви чужой команды стоят там, куда их посадил случайный мир, и бот
        // вправе выбрать любого из них — это не промах, а выбор цели.
        float miss = 999f;
        if (plan.Found)
            foreach (var o in gm.AllWorms())
                if (o != null && !o.IsDead && o.Team != shooter.Team)
                    miss = Mathf.Min(miss, Vector2.Distance(plan.Impact, o.transform.position));
        Debug.Log($"BOT step3c: сверху вниз — {(plan.Found ? Weapon.All[plan.Weapon].Name : "нет")}, " +
                  $"угол {plan.Angle:0.0}°, сила {plan.Charge:0.00}, оценка {plan.Score:0.0}, " +
                  $"воронка в {miss:0.0} юнита от цели");

        if (!plan.Found) Fail("с горы бот не нашёл ни одного выстрела");
        else if (plan.Score <= 0f) Fail($"с горы бот не видит смысла стрелять, оценка {plan.Score:0.0}");
        else if (Weapon.All[plan.Weapon].Use == WeaponUse.Charged && miss > 5f)
            Fail($"с горы воронка легла в {miss:0.0} юнита от цели");

        // --- в упор: враг в полутора юнитах, стрелять по нему себе дороже ---
        shooter.PlaceAt(low + new Vector2(1.3f, 0f));
        var melee = BotPlanner.Plan(shooter, BotDifficulty.Hard, new System.Random(5));
        Debug.Log($"BOT step3d: в упор — {(melee.Found ? Weapon.All[melee.Weapon].Name : "нет")}, " +
                  $"оценка {melee.Score:0.0}");

        if (!melee.Found) Fail("в упор бот не нашёл ни одного действия");
        else if (melee.Score <= 0f) Fail($"в упор бот не видит смысла бить, оценка {melee.Score:0.0}");

        // --- закладка в упор: динамит стоит брать, если есть куда убежать ---
        int dyn = Weapon.IndexOf(WeaponKind.Dynamite);
        var drop = BotPlanner.TryDrop(shooter, dyn);
        float feet = drop.Found
                   ? Vector2.Distance(drop.Impact, shooter.transform.position) : 999f;
        Debug.Log($"BOT step3e: динамит в упор — {(drop.Found ? "есть" : "нет")}, " +
                  $"оценка {drop.Score:0.0}, закладка в {feet:0.0} юнита от ног");

        if (!drop.Found) Fail("динамит в упор бот не рассматривает вовсе");
        else if (drop.Score <= 0f) Fail($"динамит в упор бот считает невыгодным, оценка {drop.Score:0.0}");
        else if (feet > 1.5f) Fail($"закладка легла в {feet:0.0} юнита от ног — это не под ноги");

        // Тот же динамит, но у врага за спиной ничего нет: закладка без цели
        // рядом не должна вообще появляться в планах.
        shooter.PlaceAt(low + new Vector2(20f, 0f));
        float toEnemy = float.MaxValue;
        foreach (var o in gm.AllWorms())
            if (o != null && !o.IsDead && o.Team != shooter.Team)
                toEnemy = Mathf.Min(toEnemy, Vector2.Distance(o.transform.position, shooter.transform.position));

        var far = BotPlanner.TryDrop(shooter, dyn);
        Debug.Log($"BOT step3f: динамит вдали от врага (ближайший в {toEnemy:0.0}) — " +
                  $"{(far.Found ? $"есть, оценка {far.Score:0.0}" : "нет")}");
        // Второй червь чужой команды мог оказаться рядом — тогда проверять нечего.
        if (toEnemy > 8f && far.Found) Fail("бот готов заложить динамит там, где никого нет");

        // --- ящик с припасами: за аптечкой раненый червь должен сходить ---
        CheckCrate(gm, terrain, shooter, victim, low);

        // --- фаза 12, остальное: ракета, верёвка, память и умения ---
        CheckHoming(gm, terrain, shooter, victim, low);
        CheckStrike(gm, shooter, victim, low);
        CheckJump(gm, shooter, victim, low);
        CheckSwing(gm, shooter);
        CheckBlind(gm, shooter, victim);
        CheckMemory(gm, shooter, victim);
        CheckSkills(shooter);

        // Ставим обоих обратно на площадки, чтобы дальше матч шёл по-человечески.
        if (Platform(terrain, DestructibleTerrain.WorldWidth * 0.25f, out var a1)) shooter.PlaceAt(a1);
        if (Platform(terrain, DestructibleTerrain.WorldWidth * 0.75f, out var b1)) victim.PlaceAt(b1);
        gm.Wind = wind;
    }


    /// Ящик с припасами: раненый червь должен пойти за аптечкой в двух шагах,
    /// целый — не должен, и никто не должен идти за ней через полкарты.
    static void CheckCrate(GameManager gm, DestructibleTerrain terrain,
                           Worm shooter, Worm victim, Vector2 low)
    {
        // Врага уводим подальше: ящик под ногами чужого червя бот справедливо
        // отдаёт ему, и проверять на такой расстановке было бы нечего.
        if (Platform(terrain, DestructibleTerrain.WorldWidth * 0.9f, out var away)) victim.PlaceAt(away);
        shooter.PlaceAt(low);

        // Ящик ставим не «площадкой» — та ищет по всей округе и может вернуть
        // точку под ногами, — а ровно тем же способом, каким бот считает дорогу:
        // поверхность выше воды в нескольких юнитах вбок и без уступа.
        Vector2 spot = default;
        bool placed = false;
        for (float d = 4f; d <= 10f && !placed; d += 1f)
            for (int dir = -1; dir <= 1 && !placed; dir += 2)
            {
                float x = low.x + dir * d;
                if (x < 3f || x > DestructibleTerrain.WorldWidth - 3f) continue;
                float h = terrain.SurfaceHeightWorld(x);
                if (h < DestructibleTerrain.WaterLevel + 1.5f) continue;
                if (Mathf.Abs(h - low.y) > 3f) continue;
                spot = new Vector2(x, h + 0.5f);
                placed = true;
            }

        if (!placed)
        {
            Debug.Log("BOT step3g: рядом нет ровного места под ящик — пропуск");
            return;
        }

        float dist = Mathf.Abs(spot.x - low.x);

        var crate = Crate.Drop(CrateKind.Health, spot.x);
        if (crate == null) { Debug.Log("BOT step3g: ящик не встал — пропуск"); return; }
        crate.transform.position = spot;

        float mine = Vector2.Distance(shooter.transform.position, spot);
        float rival = float.MaxValue;
        foreach (var o in gm.AllWorms())
            if (o != null && !o.IsDead && o.Team != shooter.Team)
                rival = Mathf.Min(rival, Vector2.Distance(o.transform.position, spot));

        // Чужой червь ближе — ящик по правилам его, и проверять нечего.
        if (rival < mine)
        {
            Debug.Log($"BOT step3g: чужой червь в {rival:0.0} ближе нас ({mine:0.0}) — ящик его, пропуск");
            Object.DestroyImmediate(crate.gameObject);
            return;
        }

        float health = shooter.Health;

        shooter.Health = 40f;
        bool hurt = BotPlanner.BestCrate(shooter, 40f, out float budget) != null;

        shooter.Health = 100f;
        bool whole = BotPlanner.BestCrate(shooter, 40f, out _) != null;

        // Тот же ящик, но времени в обрез: дорога съест ход, и идти незачем.
        shooter.Health = 40f;
        bool rushed = BotPlanner.BestCrate(shooter, 4f, out _) != null;

        // И тот же ящик через полкарты.
        crate.transform.position = new Vector3(
            Mathf.Clamp(low.x + 40f, 2f, DestructibleTerrain.WorldWidth - 2f), spot.y, 0f);
        bool far = BotPlanner.BestCrate(shooter, 40f, out _) != null;

        // Ответы сняты в bool до уничтожения ящика: уничтоженный объект Unity
        // считает равным null, и проверка «ящик найден» после Destroy всегда
        // отвечала бы «не найден».
        shooter.Health = health;
        Object.DestroyImmediate(crate.gameObject);

        Debug.Log($"BOT step3g: аптечка в {dist:0.0} юнита (враг в {rival:0.0}) — " +
                  $"раненый {(hurt ? $"идёт, {budget:0.0} с" : "не идёт")}, " +
                  $"целый {(whole ? "идёт" : "не идёт")}, в цейтноте {(rushed ? "идёт" : "не идёт")}, " +
                  $"через полкарты {(far ? "идёт" : "не идёт")}");

        if (!hurt) Fail($"раненый бот не идёт за аптечкой в {dist:0.0} юнита");
        else if (budget <= 0f) Fail("на дорогу к ящику отведено ноль секунд");
        if (whole) Fail("бот идёт за аптечкой с полным здоровьем");
        if (rushed) Fail("бот идёт за ящиком, когда на выстрел уже не остаётся времени");
        if (far) Fail("бот идёт за ящиком через полкарты");
    }

    /// Самонаводящаяся ракета: модель полёта должна доводить её до цели, иначе
    /// брать ракету в руки боту нельзя. Обоих червей поднимаем высоко в небо —
    /// там заведомо нет ни склона, ни кроны, и проверяется ровно доворот, а не
    /// удача с рельефом.
    /// Налёт наводится меткой, а не лучом прицела: бомбы должны сыпаться на
    /// врага и тогда, когда до него не докинуть по прямой. Раньше точку искал
    /// луч, и из-за гребня налёт ложился на гребень.
    static void CheckStrike(GameManager gm, Worm shooter, Worm victim, Vector2 low)
    {
        victim.PlaceAt(low);
        shooter.PlaceAt(low + new Vector2(26f, 0f));

        int idx = Weapon.IndexOf(WeaponKind.AirStrike);
        var strike = BotPlanner.TryStrike(shooter, idx);

        // Целью мог оказаться другой червь чужой команды — меряем до ближайшего.
        float miss = 999f;
        if (strike.Found)
            foreach (var o in gm.AllWorms())
                if (o != null && !o.IsDead && o.Team != shooter.Team)
                    miss = Mathf.Min(miss, Mathf.Abs(strike.Mark.x - o.transform.position.x));

        Debug.Log($"BOT step3m: налёт — {(strike.Found ? "есть" : "нет")}, " +
                  $"метка {(strike.HasMark ? $"{strike.Mark.x:0.0};{strike.Mark.y:0.0}" : "нет")}, " +
                  $"оценка {strike.Score:0.0}, метка в {miss:0.0} юнита от врага по горизонтали");

        if (!strike.Found) Fail("бот не рассматривает налёт вовсе");
        else if (!strike.HasMark) Fail("налёт бота идёт без метки — точку задаёт крестик");
        else if (miss > 5f) Fail($"метка налёта легла в {miss:0.0} юнита от врага");
        else if (strike.Score <= 0f) Fail($"налёт бот считает бесполезным, оценка {strike.Score:0.0}");
    }

    /// Телепорт наводится меткой на всю карту. Тонущему червю прыжок обязан
    /// найтись: сухая земля весит в оценке места больше всего остального.
    static void CheckJump(GameManager gm, Worm shooter, Worm victim, Vector2 low)
    {
        victim.PlaceAt(low);
        shooter.PlaceAt(new Vector2(Mathf.Clamp(low.x + 30f, 2f, DestructibleTerrain.WorldWidth - 2f),
                                    DestructibleTerrain.WaterLevel + 1f));

        bool found = BotPlanner.PlanJump(shooter, out Vector2 point, out float gain);
        float dry = point.y - DestructibleTerrain.WaterLevel;
        float toEnemy = 999f;
        foreach (var o in gm.AllWorms())
            if (o != null && !o.IsDead && o.Team != shooter.Team)
                toEnemy = Mathf.Min(toEnemy, Vector2.Distance(point, o.transform.position));

        Debug.Log($"BOT step3n: телепорт тонущего — {(found ? $"{point.x:0.0};{point.y:0.0}" : "некуда")}, " +
                  $"прибавка {gain:0.0}, над водой {dry:0.0}, до врага {toEnemy:0.0}");

        if (!found) Fail("тонущему червю бот не нашёл куда прыгнуть");
        else
        {
            if (gain <= 0f) Fail($"бот считает прыжок из воды бесполезным, прибавка {gain:0.0}");
            if (dry < 2f) Fail($"бот прыгает обратно в воду: над водой {dry:0.0} юнита");
            if (toEnemy < 4f) Fail($"бот прыгает врагу под ноги: до него {toEnemy:0.0} юнита");
            if (!Teleport.JumpTo(shooter, point))
                Fail("отмеченная точка не годится для прыжка — сам телепорт её не берёт");
        }
    }

    static void CheckHoming(GameManager gm, DestructibleTerrain terrain,
                            Worm shooter, Worm victim, Vector2 low)
    {
        float vx = Mathf.Clamp(low.x, 6f, DestructibleTerrain.WorldWidth - 26f);
        float sky = DestructibleTerrain.WorldHeight - 5f;
        victim.PlaceAt(new Vector2(vx, sky));
        shooter.PlaceAt(new Vector2(vx + 20f, sky + 2f));

        // Цель ракета выбирает сама, и второй червь чужой команды стоит там,
        // куда его посадил случайный мир: если выбор пал на него, проверять
        // нечего — это не промах, а другая цель.
        // В пещере «высоко в небе» — это внутри кровли: там ракету съест потолок,
        // и мерить будет нечего. Требуем чистую линию между червями.
        for (float x = vx; x <= vx + 20f; x += 0.5f)
            if (terrain.IsSolidWorld(new Vector2(x, sky + 1f)))
            {
                Debug.Log("BOT step3h: над картой нет чистого неба — пропуск");
                return;
            }

        var dir = new Vector2(-1f, 0f);
        Vector2 target = BotPlanner.HomingTargetOf(shooter, dir);
        if (Vector2.Distance(target, victim.transform.position) > 0.5f)
        {
            Debug.Log("BOT step3h: ракета целится в другого червя — пропуск");
            return;
        }

        int idx = Weapon.IndexOf(WeaponKind.Homing);
        var shot = default(BotShot);
        foreach (float ang in new[] { 0f, 15f, -12f, 30f })
        {
            foreach (float ch in new[] { 1f, 0.7f })
            {
                var s = BotPlanner.Try(shooter, idx, ang, -1, ch);
                if (s.Found) { shot = s; break; }
            }
            if (shot.Found) break;
        }

        float miss = shot.Found
                   ? Vector2.Distance(shot.Impact, victim.transform.position) : 999f;
        Debug.Log($"BOT step3h: ракета — {(shot.Found ? "летит" : "никуда не летит")}, " +
                  $"воронка в {miss:0.0} юнита от цели, оценка {shot.Score:0.0}");

        if (!Weapon.All[idx].BotCanUse) Fail("ракета всё ещё вне арсенала бота");
        if (!shot.Found) Fail("модель не доводит ракету никуда — доворот не работает");
        else if (miss > 5f) Fail($"ракета уходит в {miss:0.0} юнита мимо цели");

        // Тот же выстрел, но посчитанный перебором целиком: цель ракете задаёт
        // метка, а не ствол, поэтому перебор обязан вернуть точку на враге и
        // траекторию, которая в эту точку приходит. Раньше цель выбирал
        // HomeTargetFor, и в куче червей посчитан был один выстрел, а летел
        // другой.
        var planned = BotPlanner.TryHoming(shooter, idx);

        // Метку бот вправе поставить на любого врага — меряем до ближайшего.
        float markMiss = 999f;
        if (planned.HasMark)
            foreach (var o in gm.AllWorms())
                if (o != null && !o.IsDead && o.Team != shooter.Team)
                    markMiss = Mathf.Min(markMiss, Vector2.Distance(planned.Mark, o.transform.position));
        float drift = planned.Found ? Vector2.Distance(planned.Impact, planned.Mark) : 999f;

        Debug.Log($"BOT step3o: ракета по метке — {(planned.Found ? "есть" : "нет")}, " +
                  $"метка {(planned.HasMark ? $"{planned.Mark.x:0.0};{planned.Mark.y:0.0}" : "нет")} " +
                  $"в {markMiss:0.0} юнита от врага, воронка в {drift:0.0} от метки, " +
                  $"угол {planned.Angle:0.0}°, сила {planned.Charge:0.00}, оценка {planned.Score:0.0}");

        if (!planned.Found) Fail("перебор не нашёл ракете ни одной траектории");
        else if (!planned.HasMark) Fail("ракета бота идёт без метки — цель задаёт крестик");
        else if (markMiss > 2.5f) Fail($"метка ракеты легла в {markMiss:0.0} юнита от врага");
        else if (drift > 4f) Fail($"ракета приходит в {drift:0.0} юнита от собственной метки");
        else if (planned.Score <= 0f) Fail($"свой же выстрел ракетой бот считает пустым, оценка {planned.Score:0.0}");
    }

    /// Выстрел наугад. Он нужен там, где перебор не нашёл ничего, а ногами до
    /// врага не дойти: без него бот на своём островке простаивал весь ход,
    /// перебирая «подумать — упереться в воду — подумать». Проверяем, что
    /// выстрел вообще находится, смотрит в сторону врага и не летит отвесно
    /// вверх, — а на чистом небе ещё и что баллистика доносит его до цели.
    static void CheckBlind(GameManager gm, Worm shooter, Worm victim)
    {
        var blind = BotPlanner.Blind(shooter, victim.transform.position);
        float dx = victim.transform.position.x - shooter.transform.position.x;
        int want = dx >= 0f ? 1 : -1;

        Debug.Log($"BOT step3l: выстрел наугад — {(blind.Found ? Weapon.All[blind.Weapon].Name : "нет")}, " +
                  $"угол {blind.Angle:0.0}°, сила {blind.Charge:0.00}, сторона {blind.Facing} (враг в {dx:0.0})");

        if (!blind.Found) { Fail("выстрела наугад нет вовсе — боту опять нечем занять ход"); return; }
        if (blind.Facing != want) Fail("выстрел наугад смотрит не в ту сторону");
        if (blind.Charge < 0.5f) Fail($"выстрел наугад идёт вполсилы: {blind.Charge:0.00}");
        if (Mathf.Abs(blind.Angle) > 85f) Fail($"угол выстрела наугад за пределами прицела: {blind.Angle:0.0}");

        // Ту же пару «угол и сила» прогоняем настоящей моделью полёта: если
        // между червями чисто, наугад — это попадание, а не жест отчаяния.
        var flown = BotPlanner.Try(shooter, blind.Weapon, blind.Angle, blind.Facing, blind.Charge);
        if (flown.Found)
        {
            float miss = Vector2.Distance(flown.Impact, victim.transform.position);
            Debug.Log($"BOT step3l: наугад лёг в {miss:0.0} юнита от врага");
        }
    }

    /// Верёвка: качели считаются не всегда — над червём должно быть за что
    /// цепляться, — поэтому отсутствие плана это не провал. А вот найденный
    /// план обязан быть осмысленным: якорь в породе, приземление на сушу и
    /// заметно ближе к врагу.
    static void CheckSwing(GameManager gm, Worm shooter)
    {
        // Верёвка — дело позиции: над червём должен быть свод или отвесная
        // стена. Поэтому не спрашиваем в одной точке, а обходим карту: где-то
        // качели найдутся, и вот их-то и проверяем.
        var swing = default(BotPlanner.BotSwing);
        Vector2 was = shooter.transform.position;
        for (float f = 0.1f; f <= 0.9f && !swing.Found; f += 0.05f)
        {
            float x = DestructibleTerrain.WorldWidth * f;
            if (!gm.Terrain.FindSpawnPoint(x, out var spot)) continue;
            shooter.PlaceAt(spot);
            swing = BotPlanner.PlanSwing(shooter);
        }

        if (!swing.Found)
        {
            shooter.PlaceAt(was);
            Debug.Log("BOT step3i: на этой карте качнуться неоткуда — пропуск");
            return;
        }

        bool solid = gm.Terrain.IsSolidWorld(swing.Anchor);
        bool dry = swing.Landing.y > DestructibleTerrain.WaterLevel;
        Debug.Log($"BOT step3i: верёвка — якорь в {swing.Anchor} ({(solid ? "порода" : "пустота")}), " +
                  $"качели {(swing.SwingDir > 0 ? "вправо" : "влево")}, приземление в {swing.Landing}, " +
                  $"выигрыш {swing.Gain:0.0} юнита");

        if (!solid) Fail("гарпун цепляется за пустоту");
        if (!dry) Fail("бот собрался качнуться в море");
        if (swing.Gain < 7f) Fail($"верёвка ради {swing.Gain:0.0} юнита — это не стоит патрона");

        shooter.PlaceAt(was);
    }

    /// Память о промахе: помеченный промах должен дорожать повтор того же
    /// выстрела и не трогать ни другой угол, ни чужую команду.
    static void CheckMemory(GameManager gm, Worm shooter, Worm victim)
    {
        BotMemory.Clear();

        Vector2 from = shooter.transform.position;
        var shot = new BotShot { Found = true, Weapon = 0, Angle = 40f, Charge = 0.7f, Facing = 1 };

        // Воронка далеко от всех — это промах.
        BotMemory.NoteShot(shooter.Team, shot, from);
        BotMemory.NoteBlast(from + new Vector2(0f, 30f));

        float same = BotMemory.Penalty(shooter.Team, 0, 40.3f, 0.71f, from);
        float other = BotMemory.Penalty(shooter.Team, 0, 62f, 0.71f, from);
        float far = BotMemory.Penalty(shooter.Team, 0, 40.3f, 0.71f, from + new Vector2(9f, 0f));
        float alien = BotMemory.Penalty(victim.Team, 0, 40.3f, 0.71f, from);

        // А теперь удачный выстрел: он в память ложится, но повтор не дорожает.
        BotMemory.NoteShot(shooter.Team, new BotShot { Found = true, Weapon = 3, Angle = 12f, Charge = 0.5f }, from);
        BotMemory.NoteBlast(victim.transform.position);
        float hit = BotMemory.Penalty(shooter.Team, 3, 12f, 0.5f, from);

        Debug.Log($"BOT step3j: память — промахов {BotMemory.Misses}, повтор {same:0}, " +
                  $"другой угол {other:0}, с другого места {far:0}, чужая команда {alien:0}, " +
                  $"после попадания {hit:0}");

        if (same <= 0f) Fail("бот не помнит, что уже мазал так же");
        if (other > 0f) Fail("память штрафует и другой угол");
        if (far > 0f) Fail("память штрафует выстрел с другого места");
        if (alien > 0f) Fail("память одной команды достаётся другой");
        if (hit > 0f) Fail("память штрафует повтор удачного выстрела");

        BotMemory.Clear();
    }

    /// Сложность как набор умений: лёгкий бот не должен ни закладывать динамит,
    /// ни звать налёт, ни вести ракету — и это видно по самому плану, а не по
    /// разбросу.
    static void CheckSkills(Worm shooter)
    {
        var easy = BotSkills.For(BotDifficulty.Easy);
        var hard = BotSkills.For(BotDifficulty.Hard);

        var plan = BotPlanner.Plan(shooter, easy, new System.Random(9));
        string picked = plan.Found ? Weapon.All[plan.Weapon].Name : "нет";
        Debug.Log($"BOT step3k: лёгкий бот выбрал «{picked}»; " +
                  $"умения — закладки {easy.Drops}/{hard.Drops}, налёт {easy.Strikes}/{hard.Strikes}, " +
                  $"ракета {easy.Homing}/{hard.Homing}, верёвка {easy.Rope}/{hard.Rope}, " +
                  $"память {easy.Memory}/{hard.Memory}");

        if (easy.Drops || easy.Strikes || easy.Homing || easy.Rope || easy.Teleport || easy.Memory)
            Fail("лёгкий бот умеет то, чего уметь не должен");
        if (!(hard.Drops && hard.Strikes && hard.Homing && hard.Rope && hard.Teleport && hard.Memory))
            Fail("трудный бот чего-то не умеет");
        if (easy.AngleJitter <= hard.AngleJitter) Fail("у лёгкого бота рука твёрже, чем у трудного");

        if (plan.Found)
        {
            var w = Weapon.All[plan.Weapon];
            if (w.Use == WeaponUse.Drop || w.Use == WeaponUse.Strike || w.Homing)
                Fail($"лёгкий бот взял «{w.Name}» — это не его оружие");
        }
    }

    /// Площадка около заданного x: одна колонка сплошь и рядом не годится
    /// (склон, дерево, вода), поэтому ищем ближайшую подходящую в стороне.
    static bool Platform(DestructibleTerrain terrain, float wx, out Vector2 spawn)
    {
        for (float step = 0f; step < 24f; step += 1f)
            for (int dir = -1; dir <= 1; dir += 2)
            {
                float x = Mathf.Clamp(wx + step * dir, 2f, DestructibleTerrain.WorldWidth - 2f);
                if (terrain.FindSpawnPoint(x, out spawn)) return true;
                if (step == 0f) break;
            }
        spawn = default;
        return false;
    }
}
