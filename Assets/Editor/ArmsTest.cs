using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Оружие-визитки шага 14a: святая граната, супер-овца, наковальня, напалм,
/// минный удар и бетонный осёл. Проверяем не цифры в таблице, а повадки:
/// овца обязана оторваться от земли и уйти за полкарты, осёл — пробить остров
/// насквозь, бомба минного удара — лечь миной, а не рвануть, бак напалма —
/// рассыпаться каплями, наковальня — достать червя с неба.
/// Гоняется на острове: осла надо ронять на толщу, а не на свод пещеры.
/// Запуск: меню Worms → Тест оружия-визиток.
public static class ArmsTest
{
    const string Flag = "ArmsTest.Running";

    static double _start;
    static int _step;
    static int _errors;

    static Vector2 _spot;          // площадка, над которой всё роняем
    static int _spotDir;           // куда от неё свободно бежать овце
    static Projectile _sheep;
    static Vector2 _sheepFrom;
    static float _sheepPeak;
    static int _columnBefore;      // сплошных точек в колонке под ослом
    static Worm _target;           // червь под наковальней
    static float _targetHealth;
    static int _napalmBefore;

    [MenuItem("Worms/Тест оружия-визиток")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _errors = 0;
        _sheep = null; _target = null; _sheepPeak = 0f;
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

    static void Autostart()
    {
        App.AutostartMatch = true;
        var cfg = MatchConfig.Hotseat();
        cfg.Terrain = TerrainKind.Island;
        cfg.TurnTime = 300f;    // ход не должен смениться посреди проверки
        cfg.FloodRound = 99;
        cfg.Crates = CratePlan.Off;
        App.AutostartConfig = cfg;
    }

    static void Fail(string message) { _errors++; Debug.LogError("ARMS: " + message); }

    static void Finish()
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        Debug.Log(_errors == 0 ? "ARMS: OK" : $"ARMS: ПРОВАЛ ({_errors})");
        if (Application.isBatchMode) EditorApplication.Exit(_errors == 0 ? 0 : 1);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static Weapon Of(WeaponKind k) => Weapon.All[Weapon.IndexOf(k)];

    static int Projectiles() => Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length;

    /// Ровное место с толстой землёй под ним и свободным небом над: сюда
    /// сажаем овцу, роняем осла и всё остальное. Стороне пробега овцы нужен
    /// вдобавок пустой коридор: овца рвётся о первого встречного червя, и
    /// тогда проверять полёт уже не на чем. Направление возвращаем вместе
    /// с точкой — на острове свободным чаще оказывается только один бок.
    static bool FlatSpot(DestructibleTerrain terrain, out Vector2 spot, out int dir)
    {
        spot = Vector2.zero;
        dir = 1;
        for (float x = 10f; x < DestructibleTerrain.WorldWidth - 10f; x += 1f)
        {
            float surf = terrain.SurfaceHeightWorld(x);
            if (surf <= DestructibleTerrain.WaterLevel + 6f) continue;

            // Толща под ногами: ослу нужно что пробивать.
            int solid = 0;
            for (float dy = 0.5f; dy <= 8f; dy += 0.5f)
                if (terrain.IsSolidWorld(new Vector2(x, surf - dy))) solid++;
            if (solid < 12) continue;

            // Чистое небо: снаряды роняем с высоты.
            bool clear = true;
            for (float dy = 1f; dy <= 12f && clear; dy += 0.5f)
                clear = !terrain.IsSolidWorld(new Vector2(x, surf + dy));
            if (!clear) continue;

            if (Corridor(terrain, x, surf, 1)) { spot = new Vector2(x, surf); dir = 1; return true; }
            if (Corridor(terrain, x, surf, -1)) { spot = new Vector2(x, surf); dir = -1; return true; }
        }
        return false;
    }

    /// Коридор для пробега овцы: четырнадцать юнитов суши без червей, без
    /// моря и без отвесной стены. Без проверки на сушу овца на острове
    /// добегала до кромки и тонула, ни разу не взлетев, — а по журналу это
    /// выглядело точно так же, как несработавший отрыв.
    static bool Corridor(DestructibleTerrain terrain, float x, float surf, int dir)
    {
        if (!WormFree(x, dir)) return false;
        for (float d = 1f; d <= 15f; d += 1f)
        {
            float h = terrain.SurfaceHeightWorld(x + dir * d);
            if (h < DestructibleTerrain.WaterLevel + 4f) return false;   // впереди море
            if (Mathf.Abs(h - surf) > 5f) return false;                  // впереди стена или обрыв
        }
        return true;
    }

    /// Ни одного червя на пробеге овцы: четырнадцать юнитов в сторону хода
    /// и четыре за спиной.
    static bool WormFree(float x, int dir)
    {
        var worms = GameManager.I.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead) continue;
            float dx = (w.transform.position.x - x) * dir;
            if (dx > -4f && dx < 14f) return false;
        }
        return true;
    }

    /// Червь под открытым небом: наковальня обязана долететь до макушки, а не
    /// разбиться о свод или карниз над ним.
    static Worm OpenSkyWorm(GameManager gm)
    {
        var worms = gm.AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead || w.Health < 40f) continue;
            Vector2 p = w.transform.position;
            bool clear = true;
            for (float dy = 1f; dy <= 8f && clear; dy += 0.5f)
                clear = !gm.Terrain.IsSolidWorld(p + Vector2.up * dy);
            if (clear) return w;
        }
        return null;
    }

    /// Сколько сплошной породы в колонке под площадкой — мера работы осла.
    static int Column(DestructibleTerrain terrain, float x, float top)
    {
        int n = 0;
        for (float y = top; y > DestructibleTerrain.WaterLevel; y -= 0.4f)
            if (terrain.IsSolidWorld(new Vector2(x, y))) n++;
        return n;
    }

    /// Самая длинная пустота в колонке, в юнитах: это и есть шахта осла.
    /// Считать одну убыль породы мало — четыре воронки вразброс дают ту же
    /// убыль, что и один сквозной ход, а играют совсем по-разному.
    static float Shaft(DestructibleTerrain terrain, float x, float top)
    {
        const float Step = 0.4f;
        int run = 0, best = 0;
        for (float y = top; y > DestructibleTerrain.WaterLevel; y -= Step)
        {
            if (terrain.IsSolidWorld(new Vector2(x, y))) run = 0;
            else if (++run > best) best = run;
        }
        return best * Step;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var gm = GameManager.I;
        if (gm == null) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        // Шаг 0: описания. Визитка обязана быть визиткой — иначе она просто
        // ещё одна граната с другим именем в панели.
        if (_step == 0 && t > 1.5)
        {
            var holy = Of(WeaponKind.HolyGrenade);
            var grenade = Of(WeaponKind.Grenade);
            if (holy.BlastRadius <= grenade.BlastRadius || holy.Damage <= grenade.Damage)
                Fail("святая граната не сильнее обычной");
            if (!holy.Bouncy || holy.Fuse <= 0f) Fail("святая граната не скачет и не тикает");

            var super = Of(WeaponKind.SuperSheep);
            if (!super.Walker || super.LiftAfter <= 0f) Fail("супер-овца не ходит и не взлетает");

            foreach (var k in new[] { WeaponKind.Anvil, WeaponKind.Napalm,
                                      WeaponKind.MineStrike, WeaponKind.Donkey })
                if (Of(k).Use != WeaponUse.Strike) Fail($"{Of(k).Name}: это должен быть налёт");

            if (!Of(WeaponKind.MineStrike).Plants) Fail("минный удар не закладывает мин");
            if (Of(WeaponKind.Donkey).Punches <= 1) Fail("осёл пробивает породу один раз");
            if (Of(WeaponKind.Napalm).Cluster <= 1) Fail("напалм не рассыпается");

            // Бот должен уметь всё, что ему дали в руки: иначе трудный держит
            // визитку до конца матча и не понимает, что с ней делать.
            foreach (var k in new[] { WeaponKind.HolyGrenade, WeaponKind.SuperSheep,
                                      WeaponKind.Anvil, WeaponKind.Napalm,
                                      WeaponKind.MineStrike, WeaponKind.Donkey })
                if (!Of(k).BotCanUse) Fail($"{Of(k).Name} вне арсенала бота");

            // Иконка рисуется кодом и обязана нарисоваться: пустой слот в
            // панели ничем не отличается от отсутствующего оружия.
            foreach (var k in new[] { WeaponKind.HolyGrenade, WeaponKind.SuperSheep,
                                      WeaponKind.Anvil, WeaponKind.Napalm,
                                      WeaponKind.MineStrike, WeaponKind.Donkey })
            {
                var tex = WeaponIcons.Get(k);
                int lit = 0;
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++) if (px[i].a > 40) lit++;
                if (lit < 200) Fail($"{Of(k).Name}: иконка почти пустая ({lit} точек)");
            }

            if (!FlatSpot(gm.Terrain, out _spot, out _spotDir))
            {
                Fail("не нашлось площадки");
                Finish();
                return;
            }
            Debug.Log($"ARMS шаг 0: площадка на x={_spot.x:0.0}, поверхность {_spot.y:0.0}, "
                    + $"овца бежит {(_spotDir > 0 ? "вправо" : "влево")}");
            _step = 1;
            return;
        }

        // Шаг 1: сажаем супер-овцу на площадку и отпускаем вправо.
        if (_step == 1 && t > 2.0)
        {
            var w = Of(WeaponKind.SuperSheep);
            _sheepFrom = _spot + Vector2.up * 0.6f;
            _sheep = Projectile.Spawn(w, _sheepFrom, new Vector2(_spotDir * 1.5f, 0f), null);
            _sheep.WalkDir = _spotDir;
            _sheepPeak = _sheepFrom.y;
            _step = 2;
            return;
        }

        // Шаг 2: полторы секунды — овца ещё бежит ногами. Взлети она сразу,
        // закладка ничем не отличалась бы от ракеты.
        if (_step == 2 && t > 3.6)
        {
            if (_sheep == null) { Fail("овца пропала, не начав бежать"); _step = 4; return; }
            float up = _sheep.transform.position.y - _sheepFrom.y;
            if (up > 4f) Fail($"овца поднялась на {up:0.0} до отрыва");
            _step = 3;
            return;
        }

        if (_step == 3)
        {
            if (_sheep != null) _sheepPeak = Mathf.Max(_sheepPeak, _sheep.transform.position.y);

            // Шаг 3: ещё через две секунды овца обязана быть в небе и далеко
            // вперёд. Взрыв о склон по дороге тоже считается пройденным
            // полётом — но только если она успела подняться.
            if (t > 6.0)
            {
                float up = _sheepPeak - _sheepFrom.y;
                float ahead = _sheep != null
                        ? (_sheep.transform.position.x - _sheepFrom.x) * _spotDir : 0f;
                if (up < 3f) Fail($"супер-овца не взлетела: подъём {up:0.0}"
                                + (_sheep == null ? " (и пропала с карты)" : ""));
                if (_sheep != null && ahead < 8f) Fail($"овца ушла всего на {ahead:0.0}");
                Debug.Log($"ARMS шаг 3: подъём {up:0.0}, вперёд {ahead:0.0}");
                if (_sheep != null) Object.Destroy(_sheep.gameObject);
                _step = 4;
            }
            return;
        }

        // Шаг 4: роняем осла на ту же площадку.
        if (_step == 4 && t > 6.4)
        {
            _columnBefore = Column(gm.Terrain, _spot.x, _spot.y);
            var w = Of(WeaponKind.Donkey);
            Projectile.Spawn(w, new Vector2(_spot.x, _spot.y + 10f), new Vector2(0f, -6f), null);
            _step = 5;
            return;
        }

        // Шаг 5: осёл ушёл сквозь остров — колонка под площадкой обязана
        // похудеть вдвое. Одной воронки на такое не хватает.
        if (_step == 5 && t > 9.5)
        {
            int after = Column(gm.Terrain, _spot.x, _spot.y);
            float shaft = Shaft(gm.Terrain, _spot.x, _spot.y);
            Debug.Log($"ARMS шаг 5: колонка {_columnBefore} → {after}, шахта {shaft:0.0} юнита");
            // Осёл прошёл насквозь, если под площадкой почти ничего не
            // осталось, — а на толстой земле сквозного хода и не будет, там
            // мерой служит глубина шахты.
            if (_columnBefore < 8) Fail($"под площадкой и до осла было пусто ({_columnBefore})");
            else if (after > _columnBefore * 0.25f && shaft < 12f)
                Fail($"осёл не разрезал породу: {_columnBefore} → {after}, шахта {shaft:0.0}");
            _step = 6;
            return;
        }

        // Шаг 6: бомба минного удара.
        if (_step == 6 && t > 9.8)
        {
            var w = Of(WeaponKind.MineStrike);
            Projectile.Spawn(w, new Vector2(_spot.x + 4f, _spot.y + 8f), new Vector2(0f, -4f), null);
            _step = 7;
            return;
        }

        // Шаг 7: она обязана лечь миной, а не рвануть.
        if (_step == 7 && t > 12.5)
        {
            int mines = Object.FindObjectsByType<Mine>(FindObjectsSortMode.None).Length;
            if (mines < 1) Fail("минный удар не оставил ни одной мины");
            else Debug.Log($"ARMS шаг 7: мин на карте {mines}");
            _step = 8;
            return;
        }

        // Шаг 8: бак напалма — с той же кассетой, что даёт ему налёт.
        if (_step == 8 && t > 12.8)
        {
            var w = Of(WeaponKind.Napalm);
            _napalmBefore = Projectiles();
            Projectile.Spawn(w, new Vector2(_spot.x - 4f, _spot.y + 8f), new Vector2(0f, -4f),
                             null, 1f, w.Cluster);
            _step = 9;
            return;
        }

        // Шаг 9: бак разбился о склон и оставил после себя каплю за каплей.
        if (_step == 9 && t > 15.0)
        {
            int now = Projectiles();
            Debug.Log($"ARMS шаг 9: снарядов было {_napalmBefore}, стало {now}");
            if (now <= _napalmBefore) Fail("напалм не рассыпался каплями");
            _step = 10;
            return;
        }

        // Шаг 10: наковальня падает точно на червя.
        if (_step == 10 && t > 15.3)
        {
            _target = OpenSkyWorm(gm);
            if (_target == null) { Fail("некому стоять под наковальней"); Finish(); return; }
            _targetHealth = _target.Health;
            Debug.Log($"ARMS шаг 10: наковальня на {_target.name}, здоровья {_targetHealth:0}");
            var w = Of(WeaponKind.Anvil);
            Projectile.Spawn(w, (Vector2)_target.transform.position + Vector2.up * 7f,
                             new Vector2(0f, -5f), null);
            _step = 11;
            return;
        }

        // Шаг 11: червю прилетело с неба.
        if (_step == 11 && t > 18.0)
        {
            float lost = _targetHealth - (_target != null ? _target.Health : 0f);
            Debug.Log($"ARMS шаг 11: наковальня сняла {lost:0}");
            if (lost < 20f) Fail($"наковальня почти не задела червя ({lost:0})");
            _step = 12;
            return;
        }

        if (_step == 12 && t > 18.4) Finish();
    }
}
