using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Проверка содержимого фазы 9: процедурный звук, ящики с припасами, верёвка,
/// телепорт и потоп с утоплением; сюда же добавлены толчок и выбор червя. Гоняется в пещере — там есть потолок для
/// гарпуна и нет воды, поэтому прибывающая вода видна особенно ясно.
/// Запуск: меню Worms → Тест содержимого или Unity -executeMethod ContentTest.Run
public static class ContentTest
{
    const string Flag = "ContentTest.Running";

    static double _start;
    static int _step;
    static int _errors;
    static float _waterAtStart;
    static Crate _bomb;
    static Worm _patient;   // червь, которому адресована аптечка: ход сменится раньше подбора
    static Worm _faller;    // червь, сброшенный с высоты: он обязан разбиться
    static Worm _hopper;    // червь, поднятый на высоту прыжка: ему падение бесплатно
    static float _fallFrom, _fallHealth, _hopHealth;
    static Worm _prodded;   // червь, которого толкнули: он обязан улететь с места
    static float _prodFrom, _prodHealth;
    static int _prodDir;
    static Worm _swimmer;   // червь, опущенный под воду: он обязан утонуть
    static bool _swimmerSet;
    static bool _swimmerGrounded;
    static int _gravesBefore;
    static float _swimmerHealth;
    static bool _restarted;

    [MenuItem("Worms/Тест содержимого")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0;
        _step = 0;
        _errors = 0;
        _restarted = false;
        _swimmer = null;
        _swimmerSet = false;
        _faller = null;
        _hopper = null;
        _prodded = null;
        SessionState.SetBool(Flag, true);
        Autostart();
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false))
        {
            Autostart();
            EditorApplication.update += Tick;
        }
    }

    /// Короткие ходы, потоп с четвёртого раунда и никаких случайных ящиков:
    /// их мы роняем сами, чтобы проверка не зависела от кубика. Пещера — потому
    /// что там есть потолок для гарпуна и нет воды: прибывающая вода в таком
    /// мире видна особенно ясно.
    static MatchConfig Config()
    {
        var cfg = MatchConfig.Hotseat();
        cfg.Terrain = TerrainKind.Cave;
        cfg.TurnTime = 1.2f;
        cfg.FloodRound = 4;
        cfg.Crates = CratePlan.Off;
        return cfg;
    }

    static void Autostart()
    {
        App.AutostartMatch = true;
        App.AutostartConfig = Config();
    }

    static void Fail(string message)
    {
        _errors++;
        Debug.LogError("CONTENT: " + message);
    }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        Debug.Log(_errors == 0 ? "CONTENT: OK" : $"CONTENT: ошибок {_errors}");
        if (Application.isBatchMode) EditorApplication.Exit(_errors == 0 ? 0 : 1);
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
            if (t > 8) { Fail("GameManager не создан"); Finish(); }
            return;
        }

        // Матч перезапускаем своей конфигурацией: полагаться на автостарт нельзя,
        // его мог выставить другой тест, запущенный в этом же редакторе.
        if (!_restarted)
        {
            if (t < 0.8) return;
            _restarted = true;
            App.I.StartMatch(Config());
            _start = EditorApplication.timeSinceStartup;
            return;
        }

        if (_step == 0 && t > 1.5)
        {
            _step = 1;
            CheckAudio();
            _waterAtStart = DestructibleTerrain.WaterLevel;
            Debug.Log($"CONTENT step1: мир «{gm.Terrain.Style.Name}», ход {gm.Config.TurnTime:0.0} с, " +
                      $"потоп с раунда {gm.Config.FloodRound}, вода {_waterAtStart:0.0}");
        }
        // Выбор червя и толчок ждут состояния Aim: вне своего хода менять червя
        // и некому, и нечем. Ходы здесь по 1,2 с, поэтому ловим ближайший.
        if (_step == 1 && t > 2.0 && (gm.State == GameState.Aim || t > 5.0))
        {
            _step = 2;
            CheckWormSwap(gm);
            DoProd(gm);
        }
        if (_step == 2 && t > 2.8) { _step = 3; CheckProd(gm); }
        if (_step == 3 && t > 3.5) { _step = 4; CheckRope(gm); }
        if (_step == 4 && t > 5.0) { _step = 5; CheckTeleport(gm); }
        if (_step == 5 && t > 6.0) { _step = 6; DropWorms(gm); }
        if (_step == 6 && t > 9.0) { _step = 7; CheckFall(gm); }
        if (_step == 7 && t > 9.5) { _step = 8; DropCrates(gm); }
        if (_step == 8 && t > 14.0) { _step = 9; CheckCrates(gm); }
        if (_step == 9 && t > 21.5) { _step = 10; CheckFlood(gm); }
        if (_step == 10 && t > 24.5) { _step = 11; CheckDrowned(gm); }
        if (_step == 11 && t > 25.0) { _step = 12; CheckTools(gm); }
        if (_step == 12 && t > 25.5) { _step = 13; ChuteDrop(gm); }
        if (_step == 13 && t > 25.9) { _step = 14; OpenChute(gm); }
        if (_step == 14 && t > 27.2) { _step = 15; CheckChute(gm); Finish(); }
    }

    // --- парашют и ранец ---------------------------------------------------

    static Worm _flier;
    static float _flierHealth;

    /// Роняем червя с высоты, где падение без купола стоило бы здоровья.
    static void ChuteDrop(GameManager gm)
    {
        var worms = gm.AllWorms();
        _flier = null;
        for (int i = 0; i < worms.Count && _flier == null; i++)
            if (worms[i] != null && !worms[i].IsDead) _flier = worms[i];

        if (_flier == null) { Debug.Log("CONTENT step13: живых червей не осталось — парашют пропускаем"); return; }

        var terrain = gm.Terrain;
        Vector2 spot = Vector2.zero;
        bool found = false;
        for (float x = 8f; x < DestructibleTerrain.WorldWidth - 8f && !found; x += 2f)
        {
            float surf = terrain.SurfaceHeightWorld(x);
            if (surf <= DestructibleTerrain.WaterLevel + 2f) continue;

            bool clear = true;
            for (float dy = 1f; dy <= 14f && clear; dy += 0.5f)
                clear = !terrain.IsSolidWorld(new Vector2(x, surf + dy));
            if (!clear) continue;

            spot = new Vector2(x, surf + 14f);
            found = true;
        }

        if (!found) { _flier = null; Debug.Log("CONTENT step13: нет колонки с высотой под падение — парашют пропускаем"); return; }

        _flier.Heal(100f);
        _flierHealth = _flier.Health;
        _flier.PlaceAt(spot);
        Debug.Log($"CONTENT step13: роняем {_flier.WormName} с {spot.y:0.0} (здоровье {_flierHealth:0})");
    }

    /// Купол раскрываем уже в падении: на месте он и не должен раскрываться.
    static void OpenChute(GameManager gm)
    {
        if (_flier == null || _flier.IsDead) return;

        var chute = Weapon.All[Weapon.IndexOf(WeaponKind.Parachute)];
        _flier.OpenChute(chute);
        if (!_flier.Chuting) Fail("парашют не раскрылся в падении");
    }

    /// Под куполом снижение медленное, а урона за приземление нет вовсе.
    /// Ранец проверяем состоянием: тяга живёт на удержании кнопки, которого
    /// в headless-прогоне нет, — её смотрят руками.
    static void CheckChute(GameManager gm)
    {
        if (_flier == null || _flier.IsDead)
        {
            Debug.Log("CONTENT step15: подопытный не дожил — парашют не мерим");
            return;
        }

        float vy = _flier.Velocity.y;
        if (vy < -4.2f) Fail($"под куполом снижение {vy:0.0} — купол не держит");
        if (_flier.Health < _flierHealth - 0.5f)
            Fail($"под куполом сняли здоровье: {_flierHealth:0} → {_flier.Health:0}");

        var jet = Weapon.All[Weapon.IndexOf(WeaponKind.Jetpack)];
        _flier.StartJet(jet);
        if (!_flier.Jetting) Fail("ранец не включился");
        _flier.StopJet();

        Debug.Log($"CONTENT step15: снижение {vy:0.0} юнита/с, здоровье {_flier.Health:0}, ранец включается");
    }

    // --- инструменты земли -------------------------------------------------

    /// Бур, паяльная лампа и балка правят не червей, а породу, поэтому меряем
    /// саму маску: рез обязан оставить сквозной коридор, балка — сплошную плиту
    /// в пустоте. Заодно сверяем, что бот их в руки не берёт: урона у них нет,
    /// а перебор «угол и сила» для лопаты бессмысленен.
    static void CheckTools(GameManager gm)
    {
        var terrain = gm.Terrain;

        // Ищем толщу породы: колонка, где под поверхностью подряд лежит камень.
        Vector2 deep = Vector2.zero;
        bool found = false;
        for (float x = 8f; x < DestructibleTerrain.WorldWidth - 8f && !found; x += 2f)
        {
            float surf = terrain.SurfaceHeightWorld(x);
            if (surf <= DestructibleTerrain.WaterLevel + 2f) continue;
            var probe = new Vector2(x, surf - 1.2f);
            if (!terrain.IsSolidWorld(probe)) continue;
            deep = probe;
            found = true;
        }

        if (!found) { Fail("не нашлось толщи породы под рез"); return; }

        // Рез вбок на четыре юнита: и начало, и конец обязаны стать пустыми.
        var from = deep;
        var to = deep + new Vector2(4f, 0f);
        bool cut = terrain.Dig(from, to, 0.6f);
        if (!cut) { Fail("рез не тронул породу"); return; }

        int stillSolid = 0;
        for (float k = 0f; k <= 1.001f; k += 0.1f)
            if (terrain.IsSolidWorld(Vector2.Lerp(from, to, k))) stillSolid++;
        if (stillSolid > 0) Fail($"после реза коридор не сквозной: {stillSolid} точек из 11 в породе");

        // Балка в пустоте. Место ищем по всей карте, а не над одной колонкой:
        // в пещере над полом кровля, и «просто повыше» пустоты не гарантирует.
        Vector2 air = Vector2.zero;
        bool space = false;
        for (float x = 8f; x < DestructibleTerrain.WorldWidth - 8f && !space; x += 2f)
            for (float y = DestructibleTerrain.WaterLevel + 3f;
                 y < DestructibleTerrain.WorldHeight - 3f && !space; y += 1f)
            {
                var probe = new Vector2(x, y);
                bool clear = true;
                for (float dx = -2.4f; dx <= 2.4f && clear; dx += 0.4f)
                    clear = !terrain.IsSolidWorld(probe + new Vector2(dx, 0f));
                if (!clear) continue;
                air = probe;
                space = true;
            }

        if (!space) { Fail("не нашлось пустоты под балку"); return; }

        int pathsBefore = terrain.ColliderPathCount;
        if (!terrain.StampBeam(air, 0f, 4.2f, 0.55f, new Color32(150, 158, 170, 255)))
        { Fail("балка не встала"); return; }

        // Пробы считаем целым счётчиком: `for (float dx = -1.8f; dx <= 1.8f; dx += 0.6f)`
        // на седьмом шаге даёт 1.8000001 и молча его пропускает.
        int onBeam = 0;
        for (int k = 0; k <= 6; k++)
            if (terrain.IsSolidWorld(air + new Vector2(-1.8f + 0.6f * k, 0f))) onBeam++;
        if (onBeam < 7) Fail($"балка дырявая: сплошных точек {onBeam} из 7");
        if (terrain.ColliderPathCount <= pathsBefore)
            Fail("балка не попала в коллайдер: контуров не прибавилось");

        // Инструменты — снаряжение без урона, бот их не берёт.
        foreach (var kind in new[] { WeaponKind.Drill, WeaponKind.Blowtorch, WeaponKind.Girder })
        {
            var w = Weapon.All[Weapon.IndexOf(kind)];
            if (w.Damage != 0f) Fail($"{w.Name}: урон {w.Damage}, инструмент не должен бить");
            if (w.BotCanUse) Fail($"{w.Name}: бот берёт инструмент в руки");
        }

        Debug.Log($"CONTENT step12: рез {stillSolid} точек в породе из 11, балка {onBeam} из 7, " +
                  $"контуров {pathsBefore} → {terrain.ColliderPathCount}");
    }

    // --- звук --------------------------------------------------------------

    /// Клип обязан существовать, иметь длину и не быть тишиной: опечатка в
    /// огибающей даёт ровно ноль, и на слух это замечаешь последним.
    static void CheckAudio()
    {
        int silent = 0;
        foreach (Sfx.Sound s in System.Enum.GetValues(typeof(Sfx.Sound)))
        {
            var clip = Sfx.Clip(s);
            if (clip == null || clip.samples < 100) { Fail($"клип {s} не собран"); continue; }
            float rms = Rms(clip);
            if (rms < 0.02f) { Fail($"клип {s} звучит тишиной (rms {rms:0.0000})"); silent++; }
        }

        var pad = Music.Build();
        float padRms = Rms(pad);
        if (pad.length < 8f) Fail("петля музыки короче восьми секунд");
        if (padRms < 0.02f) Fail($"музыка звучит тишиной (rms {padRms:0.0000})");

        Debug.Log($"CONTENT step1: звук — клипов {System.Enum.GetValues(typeof(Sfx.Sound)).Length}, " +
                  $"немых {silent}, музыка {pad.length:0.0} с rms {padRms:0.000}");
    }

    static float Rms(AudioClip clip)
    {
        var data = new float[clip.samples * clip.channels];
        clip.GetData(data, 0);
        double sum = 0;
        for (int i = 0; i < data.Length; i++) sum += (double)data[i] * data[i];
        return data.Length > 0 ? Mathf.Sqrt((float)(sum / data.Length)) : 0f;
    }

    // --- выбор червя -------------------------------------------------------

    /// Ход начинается не обязательно с того червя, кем хочется играть. Смена
    /// обязана поменять ровно одно — активного червя, не тронув таймер хода:
    /// иначе перебором своих можно было бы ходить бесконечно.
    static void CheckWormSwap(GameManager gm)
    {
        var before = gm.ActiveWorm;
        if (before == null) { Fail("нет активного червя для смены"); return; }

        var team = gm.Teams[gm.CurrentTeam];
        if (team.AliveCount < 2)
        {
            Debug.Log("CONTENT step2: в команде один червь — менять некого");
            return;
        }
        if (!gm.CanSelectWorm) { Fail("выбор червя закрыт в начале хода"); return; }

        float time = gm.TurnTimeLeft;
        int weapon = gm.SelectedWeapon;
        float wind = gm.Wind;

        if (!gm.SelectNextWorm()) { Fail("смена червя не сработала"); return; }
        var after = gm.ActiveWorm;

        if (after == before) Fail("активный червь не сменился");
        if (after == null || after.Team != team) Fail("смена увела ход в чужую команду");
        if (gm.TurnTimeLeft > time + 0.001f) Fail($"смена червя накинула времени: {time:0.00} → {gm.TurnTimeLeft:0.00}");
        if (gm.SelectedWeapon != weapon) Fail("смена червя сбросила оружие");
        if (!Mathf.Approximately(gm.Wind, wind)) Fail("смена червя перекрутила ветер");
        if (gm.State != GameState.Aim) Fail("смена червя увела ход из прицеливания");

        Debug.Log($"CONTENT step2: червь {before.WormName} → {after.WormName}, " +
                  $"время хода {time:0.00} → {gm.TurnTimeLeft:0.00}");
    }

    // --- толчок ------------------------------------------------------------

    /// Толчок не наносит урона, не тратит патрон и не заканчивает ход, но
    /// сдвигает соседа с места — ради этого он и нужен у воды и над обрывом.
    static void DoProd(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        if (worm == null) { Fail("нет активного червя для толчка"); return; }

        foreach (var v in gm.AllWorms())
        {
            if (v == null || v.IsDead || v == worm || v == _patient) continue;
            _prodded = v;
            break;
        }
        if (_prodded == null) { Debug.Log("CONTENT step2: некого толкать"); return; }

        // Ставим соседа вплотную перед носом: досягаемость толчка — 1,3 юнита
        // от точки в 0,6 перед червём, и 1,05 в неё попадает наверняка.
        // Сторону выбираем по камню: в пещере стена в полушаге справа обычное
        // дело, и толкать в неё — проверять не толчок, а породу.
        Vector2 p = worm.transform.position;
        int dir = 0;
        foreach (int d in new[] { 1, -1 })
        {
            if (gm.Terrain.IsSolidWorld(p + new Vector2(d * 1.05f, 0f))) continue;
            if (gm.Terrain.IsSolidWorld(p + new Vector2(d * 2.6f, 0.6f))) continue;
            dir = d;
            break;
        }
        if (dir == 0)
        {
            Debug.Log("CONTENT step2: вокруг червя камень — толкать некуда");
            _prodded = null;
            return;
        }

        worm.Facing = dir;
        _prodded.PlaceAt(new Vector2(p.x + dir * 1.05f, p.y));
        _prodFrom = _prodded.transform.position.x;
        _prodHealth = _prodded.Health;
        _prodDir = dir;

        int idx = Weapon.IndexOf(WeaponKind.Prod);
        var prod = Weapon.All[idx];
        if (prod.Kind != WeaponKind.Prod) { Fail("толчка нет в арсенале"); return; }
        if (prod.Damage > 0f) Fail($"у толчка есть урон: {prod.Damage:0}");
        if (prod.Ammo >= 0) Fail("толчок не бесконечный");

        var state = gm.State;
        var hit = worm.Prod(prod);

        if (hit != _prodded) Fail("толчок не нашёл соседа вплотную");
        if (_prodded.Health < _prodHealth - 0.001f) Fail("толчок снял здоровье");
        if (gm.State != state) Fail("толчок завершил ход, а не должен");
        if (gm.AmmoOf(idx) != -1) Fail($"толчок потратил патрон: осталось {gm.AmmoOf(idx)}");
        if (gm.CanSelectWorm) Fail("после толчка червя всё ещё можно сменить");

        Debug.Log($"CONTENT step2: {worm.WormName} толкает {_prodded.WormName} " +
                  $"с x {_prodFrom:0.0} в сторону {_prodDir}");
    }

    static void CheckProd(GameManager gm)
    {
        if (_prodded == null) return;
        if (_prodded.IsDead)
        {
            Debug.Log("CONTENT step3: толкнутый червь погиб — улетел так улетел");
            return;
        }

        float moved = (_prodded.transform.position.x - _prodFrom) * _prodDir;
        Debug.Log($"CONTENT step3: толкнутый уехал на {moved:0.00} юнита");
        if (moved < 0.6f) Fail($"толчок сдвинул червя всего на {moved:0.00} юнита");
    }

    // --- верёвка -----------------------------------------------------------

    static void CheckRope(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        if (worm == null) { Fail("нет активного червя для верёвки"); return; }

        var rope = Rope.Of(worm);

        // Ставим червя на четыре юнита выше пола и бьём гарпуном вниз: так проверка
        // не зависит от того, насколько высок потолок именно над этим местом.
        Vector2 home = worm.transform.position;
        worm.PlaceAt(home + Vector2.up * 4f);

        if (!rope.Throw(Vector2.down))
        {
            Fail("гарпун не зацепился за пол под червём");
            worm.PlaceAt(home);
            return;
        }
        if (!worm.Roped) Fail("верёвка не считается натянутой после зацепа");
        if (!gm.Terrain.IsSolidWorld(rope.Anchor)) Fail("верёвка зацепилась не за породу");

        float len = rope.Length;
        if (len <= 0.5f || len > Rope.MaxLength) Fail($"длина верёвки вне диапазона: {len:0.00}");

        Debug.Log($"CONTENT step2: верёвка — якорь {rope.Anchor.x:0.0};{rope.Anchor.y:0.0}, длина {len:0.0}");

        rope.Release();
        if (worm.Roped) Fail("верёвка не отцепилась");
        worm.PlaceAt(home);

        // Заодно смотрим, дотягивается ли верёвка до потолка — это уже про баланс,
        // а не про механику, поэтому просто в лог.
        bool ceiling = rope.Throw(new Vector2(0.15f, 1f));
        Debug.Log($"CONTENT step2: до потолка {(ceiling ? rope.Length.ToString("0.0") + " юнита" : "не дотянуться")}");
        rope.Release();

        // Верёвка тратит патрон, но ход не заканчивает — этим она и отличается от оружия.
        var state = gm.State;
        int ammo = gm.AmmoOf(Weapon.IndexOf(WeaponKind.Rope));
        gm.ConsumeAmmo(WeaponKind.Rope);
        int left = gm.AmmoOf(Weapon.IndexOf(WeaponKind.Rope));
        if (left != ammo - 1) Fail($"боезапас верёвки {ammo} → {left}, ожидалось на единицу меньше");
        if (gm.State != state) Fail("верёвка завершила ход, а не должна");
    }

    // --- телепорт ----------------------------------------------------------

    static void CheckTeleport(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        if (worm == null) { Fail("нет активного червя для телепорта"); return; }

        Vector2 before = worm.transform.position;
        bool moved = false;

        // Куда-то из четырёх сторон место найтись обязано.
        Vector2[] dirs = { Vector2.right, Vector2.left, new Vector2(0.6f, 0.8f), new Vector2(-0.6f, 0.8f) };
        foreach (var d in dirs)
        {
            if (!Teleport.Jump(worm, d, 0.25f)) continue;
            moved = true;
            break;
        }

        if (!moved) { Fail("телепорт не нашёл ни одной точки из четырёх направлений"); return; }

        Vector2 after = worm.transform.position;
        float dist = Vector2.Distance(before, after);
        if (dist < 2f) Fail($"телепорт сдвинул червя всего на {dist:0.0}");
        if (gm.Terrain.IsSolidWorld(after)) Fail("телепорт высадил червя внутрь породы");

        Debug.Log($"CONTENT step3: телепорт — {before.x:0.0};{before.y:0.0} → {after.x:0.0};{after.y:0.0}, {dist:0.0} юнита");
    }

    // --- ящики -------------------------------------------------------------

    static void DropCrates(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        if (worm == null) { Fail("нет активного червя для ящика"); return; }

        _patient = worm;
        worm.TakeDamage(40f);

        // Аптечка прямо над головой: падает на червя и подбирается сама.
        Crate.Drop(CrateKind.Health, worm.transform.position.x);

        // Второй ящик — подопытный для детонации: подальше от всех червей.
        float x = Mathf.Clamp(worm.transform.position.x + 14f, 6f, DestructibleTerrain.WorldWidth - 6f);
        _bomb = Crate.Drop(CrateKind.Ammo, x);

        var natural = Crate.DropRandom(gm.Terrain);
        if (natural == null) Fail("случайный сброс не нашёл места на карте");

        Debug.Log($"CONTENT step6: сброшено ящиков {Crate.All.Count}, здоровье червя {worm.Health:0}");
    }

    static void CheckCrates(GameManager gm)
    {
        var worm = _patient;
        if (worm == null || worm.IsDead)
            Debug.Log("CONTENT step7: подопечный червь не дожил — проверку аптечки пропускаем");
        else if (worm.Health > 60.5f)
            Debug.Log($"CONTENT step7: аптечка подобрана, здоровье {worm.Health:0}");
        else
            Fail($"аптечка не подобрана: здоровье {worm.Health:0} (ожидалось больше 60)");

        // Ящик мог достаться червю или утонуть — тогда роняем свежий: проверяем
        // детонацию, а не то, доживёт ли подопытный до конца хода.
        if (_bomb == null)
            _bomb = Crate.Drop(CrateKind.Ammo,
                Mathf.Clamp(DestructibleTerrain.WorldWidth * 0.5f, 6f, DestructibleTerrain.WorldWidth - 6f));
        if (_bomb == null) { Fail("не удалось сбросить ящик под детонацию"); return; }

        Vector2 pos = _bomb.transform.position;
        int before = Crate.All.Count;
        int paths = gm.Terrain.ColliderPathCount;

        Combat.Detonate(pos + Vector2.up * 1.5f, 2.5f, 30f);

        // Destroy отложен до конца кадра, поэтому ссылка ещё жива — судим по списку.
        if (Crate.All.Contains(_bomb)) Fail("ящик под взрывом не сдетонировал");
        if (Crate.All.Count >= before) Fail("список ящиков не уменьшился после детонации");

        Debug.Log($"CONTENT step7: детонация ящика — ящиков {before} → {Crate.All.Count}, " +
                  $"контуров {paths} → {gm.Terrain.ColliderPathCount}");
    }

    // --- урон от падения ---------------------------------------------------

    /// Роняем двух червей: одного с высоты, где падение обязано покалечить,
    /// второго — ровно на высоту прыжка, где оно обязано остаться бесплатным.
    /// Иначе порог легко сдвинуть так, что игра начнёт бить за каждый прыжок.
    static void DropWorms(GameManager gm)
    {
        var worms = gm.AllWorms();
        foreach (var w in worms)
        {
            if (w == null || w.IsDead || w == gm.ActiveWorm || w == _patient) continue;
            if (_faller == null) { _faller = w; continue; }
            if (_hopper == null) { _hopper = w; break; }
        }
        if (_faller == null || _hopper == null) { Debug.Log("CONTENT step4: некого ронять"); return; }

        Vector2 p = _faller.transform.position;
        // В пещере потолок близко: выше него поднимать нельзя, иначе червь
        // окажется в камне. Луч пускаем из-за своего коллайдера.
        var up = Physics2D.Raycast(p + Vector2.up * 0.7f, Vector2.up, 24f);
        float ceiling = up.collider != null ? up.point.y - 1.2f : p.y + 16f;
        _fallFrom = Mathf.Min(p.y + 16f, ceiling);
        _fallHealth = _faller.Health;
        _faller.PlaceAt(new Vector2(p.x, _fallFrom));

        Vector2 h = _hopper.transform.position;
        _hopHealth = _hopper.Health;
        _hopper.PlaceAt(new Vector2(h.x, h.y + 3.4f));   // прыжок поднимает на 3,7

        Debug.Log($"CONTENT step4: роняем с {_fallFrom - p.y:0.0} юнита " +
                  $"(здоровье {_fallHealth:0}) и подбрасываем на 3,4 (здоровье {_hopHealth:0})");
    }

    static void CheckFall(GameManager gm)
    {
        if (_faller == null || _hopper == null) return;

        if (_faller.IsDead)
        {
            Debug.Log("CONTENT step5: сброшенный червь погиб — падение засчитано");
        }
        else
        {
            float drop = _fallFrom - _faller.transform.position.y;
            float dmg = _fallHealth - _faller.Health;
            Debug.Log($"CONTENT step5: падение {drop:0.0} юнита, урон {dmg:0}");
            if (drop < 7f) Debug.Log("CONTENT step5: лететь было некуда — проверку пропускаем");
            else if (dmg <= 0.5f) Fail($"падение с {drop:0.0} юнита не нанесло урона");
            else if (dmg > 30.5f) Fail($"урон от падения {dmg:0} больше потолка в 30");
        }

        if (!_hopper.IsDead && _hopHealth - _hopper.Health > 0.5f)
            Fail($"падение с высоты прыжка стоило {_hopHealth - _hopper.Health:0} очков здоровья");
    }

    // --- потоп и утопление -------------------------------------------------

    static void CheckFlood(GameManager gm)
    {
        float water = DestructibleTerrain.WaterLevel;
        Debug.Log($"CONTENT step8: раунд {gm.Round}, потоп {gm.Flooding}, " +
                  $"вода {_waterAtStart:0.0} → {water:0.0}");

        if (gm.State == GameState.GameOver)
        {
            Debug.Log("CONTENT step8: матч кончился раньше потопа — проверку пропускаем");
            return;
        }

        if (!gm.Flooding) { Fail($"потоп не начался к раунду {gm.Round}"); return; }
        if (water <= _waterAtStart) Fail("вода не поднялась");

        // Утопление проверяем прямым погружением: ждать, пока вода сама дойдёт
        // до червя, — это десятки раундов, тест столько не живёт. Топим того,
        // кто стоит на земле: это тот самый случай, ради которого утонувшего
        // на берегу хоронят как обычного покойника — с воронкой и памятником.
        var worms = gm.AllWorms();
        foreach (var w in worms)
            if (w != null && !w.IsDead && w != gm.ActiveWorm && w.Grounded) { _swimmer = w; break; }
        if (_swimmer == null)
            foreach (var w in worms)
                if (w != null && !w.IsDead && w != gm.ActiveWorm) { _swimmer = w; break; }

        if (_swimmer == null) { Debug.Log("CONTENT step8: некого топить"); return; }
        _swimmerSet = true;
        _swimmerGrounded = _swimmer.Grounded;
        _swimmerHealth = _swimmer.Health;
        _gravesBefore = Object.FindObjectsByType<GraveMarker>(FindObjectsSortMode.None).Length;

        // Стоящего на земле накрываем водой с головой, не сдвигая с места;
        // висящего в воздухе просто опускаем под воду.
        if (_swimmerGrounded)
            DestructibleTerrain.SetWaterLevel(_swimmer.transform.position.y + 1f);
        else
            _swimmer.PlaceAt(new Vector2(_swimmer.transform.position.x, water - 3f));
    }

    /// Через три секунды под водой червь обязан быть мёртв, а если вода
    /// догнала его на земле — ещё и оставить памятник: хоронят его как
    /// обычного покойника. Ушедший на дно памятника не оставляет.
    static void CheckDrowned(GameManager gm)
    {
        if (!_swimmerSet) return;

        // Утонувший червь к этому моменту уничтожен, и обращаться к его полям
        // нельзя: сравнение с null у Unity как раз это и ловит.
        bool dead = _swimmer == null || _swimmer.IsDead;
        int graves = Object.FindObjectsByType<GraveMarker>(FindObjectsSortMode.None).Length;
        Debug.Log($"CONTENT step9: утонул {dead} (на земле {_swimmerGrounded}), " +
                  $"здоровье было {_swimmerHealth:0.0}, памятников {_gravesBefore} → {graves}");
        if (!dead) Fail($"червь под водой не утонул за три секунды, здоровье {_swimmer.Health:0.0}");
        if (_swimmerGrounded && graves <= _gravesBefore)
            Fail("утонувший на земле червь не оставил памятника");

        // Виды памятников у команд не совпадают: по кладбищу должно быть видно,
        // чья это могила.
        if (gm.Teams.Count >= 2 && gm.Teams[0].GraveKind == gm.Teams[1].GraveKind)
            Fail("у двух команд один вид памятника");
        for (int i = 0; i < Grave.Kinds; i++)
            if (Grave.Stones(i) == null || Grave.Mark(i) == null) Fail($"памятник вида {i} не собрался");
    }
}
