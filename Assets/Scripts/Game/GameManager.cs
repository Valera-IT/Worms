using System.Collections.Generic;
using UnityEngine;

/// Отход (Retreat) — окно между выстрелом и передачей хода: червь уже отстрелялся,
/// но ещё бежит прятаться. Стрелять в нём нельзя, ходить и прыгать — можно.
public enum GameState { Aim, Projectile, Retreat, Settle, GameOver }

public class GameManager : MonoBehaviour
{
    public static GameManager I;

    public DestructibleTerrain Terrain;
    public CameraRig Cam;
    public List<Team> Teams = new List<Team>();

    public int CurrentTeam;
    public GameState State = GameState.Aim;
    public float Wind;
    public float TurnTimeLeft;
    public string WinnerText;

    public Transform WorldRoot;
    public static Transform Root => I != null ? I.WorldRoot : null;
    public int Generation;

    /// Номер раунда: раунд — это круг, в котором сходили все живые команды.
    public int Round { get; private set; } = 1;

    /// Потоп начался: вода прибывает каждый ход.
    public bool Flooding { get; private set; }

    /// Сколько раундов осталось до потопа; -1 — режим выключен в конфигурации.
    public int RoundsToFlood =>
        Config == null || Config.FloodRound <= 0 ? -1
        : Mathf.Max(0, Config.FloodRound - Round + 1);

    public int SelectedWeapon;
    public Weapon CurrentWeapon => Weapon.All[SelectedWeapon];
    public Worm ActiveWorm { get; private set; }

    /// Конфигурация матча: число и состав команд, время хода, червей, боезапас.
    /// Раньше это были константы и захардкоженные списки прямо здесь.
    public MatchConfig Config { get; private set; }

    /// Сколько червю дают отбежать после того, как взрыв отгремел.
    const float RetreatTime = 2f;

    /// Мина и динамит взводятся под ногами: три секунды на взвод и ещё две на
    /// побег — отход тут начинается сразу, ждать полёта нечего.
    const float FuseRetreatTime = 5f;

    /// Сколько ждём развязки выстрела, пока в воздухе ничего нет: мгновенному
    /// оружию хватает мига, а летящий снаряд сам продлевает окно.
    const float ShotWatch = 0.6f;

    /// На сколько поднимается вода за ход после начала потопа и как быстро она
    /// доходит до новой отметки: рывком уровень не ставим, иначе червь на берегу
    /// оказывается под водой в один кадр и утонуть уже не успевает.
    const float FloodStep = 0.45f;
    const float FloodSpeed = 1.1f;

    readonly List<Projectile> _projectiles = new List<Projectile>();
    float _settleTimer;
    int _seed;
    bool _reported;
    int _turnsTaken;
    int _dying;         // сколько червей сейчас прощаются: ход их дожидается
    bool _shooterHurt;  // стрелявшего задело его же выстрелом — отход отменяется
    float _waterTarget = float.NaN;   // куда ползёт вода; NaN — стоит на месте
    Transform _water;   // квада воды: при потопе её приходится двигать

    void Awake()
    {
        I = this;
        Physics2D.gravity = new Vector2(0f, -24f);
        Application.targetFrameRate = 60;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        if (WorldRoot != null) Destroy(WorldRoot.gameObject);
    }

    /// Запуск матча по конфигурации. Зовётся из App сразу после AddComponent.
    public void Begin(MatchConfig cfg)
    {
        Config = cfg ?? MatchConfig.Hotseat();
        Sfx.Prewarm();
        BotMemory.Clear();
        _seed = Random.Range(0, 100000);
        BuildWorld(_seed);
    }

    public void Restart()
    {
        Generation++;
        if (WorldRoot != null) Destroy(WorldRoot.gameObject);
        Teams.Clear();
        _projectiles.Clear();
        Crate.Forget();
        // Промахи прошлого матча к новой карте отношения не имеют.
        BotMemory.Clear();
        Terrain = null;
        _water = null;
        ActiveWorm = null;
        WinnerText = null;
        _reported = false;
        _turnsTaken = 0;
        Round = 1;
        Flooding = false;
        _dying = 0;
        _waterTarget = float.NaN;
        State = GameState.Aim;
        _seed = Random.Range(0, 100000);
        BuildWorld(_seed);
    }

    /// Всё, что создаётся под мир, вешаем на общий корень — так рестарт сводится к его удалению.
    public static void Attach(GameObject go)
    {
        if (I != null && I.WorldRoot != null) go.transform.SetParent(I.WorldRoot, true);
    }

    void BuildWorld(int seed)
    {
        var rootGo = new GameObject("World");
        WorldRoot = rootGo.transform;

        // Камера: берём ту, что уже поставил App под меню, и настраиваем под бой.
        if (Cam == null)
        {
            var camGo = Camera.main != null ? Camera.main.gameObject : null;
            if (camGo == null)
            {
                camGo = new GameObject("MainCamera") { tag = "MainCamera" };
                camGo.AddComponent<Camera>().orthographic = true;
            }
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 13f;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            camGo.transform.position = new Vector3(DestructibleTerrain.WorldWidth * 0.5f, 20f, -10f);
            Cam = camGo.GetComponent<CameraRig>() ?? camGo.AddComponent<CameraRig>();
        }

        Crate.Forget();

        // Ландшафт. Тип мира берём из конфига; «Случайный» раскрывается из сида.
        var tGo = new GameObject("Terrain");
        tGo.transform.SetParent(WorldRoot, true);
        tGo.AddComponent<SpriteRenderer>();
        Terrain = tGo.AddComponent<DestructibleTerrain>();
        Terrain.Build(seed, Config.Terrain);

        // Небо и вода — их цвета живут в стиле мира, поэтому уже после ландшафта.
        BuildBackdrop(seed);
        var camComp = Cam != null ? Cam.GetComponent<Camera>() : Camera.main;
        if (camComp != null) camComp.backgroundColor = Terrain.Style.Sky;

        // Команды
        int teamCount = Mathf.Clamp(Config.TeamCount, 2, 4);
        int wormsPerTeam = Mathf.Clamp(Config.WormsPerTeam, 1, 8);
        // Клички раздаёт мешок из Names: он не повторяется и не кончается —
        // раньше пула на двенадцать имён не хватало на четыре команды по восемь.
        var nameBag = Names.WormBag(seed);
        var spawnPoints = PickSpawnPoints(teamCount, wormsPerTeam, seed);

        // Памятники: у каждой команды свой вид из десяти, и они не повторяются —
        // тасуем список видов сидом матча и раздаём по порядку.
        var graves = new List<int>();
        for (int i = 0; i < Grave.Kinds; i++) graves.Add(i);
        var graveRnd = new System.Random(seed ^ 0x6A4E);
        for (int i = graves.Count - 1; i > 0; i--)
        {
            int j = graveRnd.Next(i + 1);
            (graves[i], graves[j]) = (graves[j], graves[i]);
        }

        int spawnIdx = 0;
        for (int t = 0; t < teamCount; t++)
        {
            var setup = Config.Teams[t];
            var team = new Team { Name = setup.Name, Color = setup.Color, GraveKind = graves[t % graves.Count] };
            foreach (var w in Weapon.All) team.Ammo[w.Kind] = Config.AmmoFor(w);
            // Бот-команда приносит свою реализацию IGameInput — червь и очередь
            // ходов не отличают её от человека за клавиатурой.
            if (setup.IsBot) team.Controller = new BotInput(team, Config.Difficulty, seed * 31 + t);

            for (int k = 0; k < wormsPerTeam; k++)
            {
                if (spawnIdx >= spawnPoints.Count) break;
                var pos = spawnPoints[spawnIdx++];

                var worm = Worm.Spawn(team, Names.NextWorm(nameBag), pos);
                // Черви раскиданы по всей карте, поэтому смотрят к центру,
                // а не всей командой в одну сторону.
                worm.Facing = pos.x < DestructibleTerrain.WorldWidth * 0.5f ? 1 : -1;
                team.Worms.Add(worm);
            }
            Teams.Add(team);
        }

        CurrentTeam = 0;
        SelectedWeapon = 0;
        StartTurn();
    }

    void BuildBackdrop(int seed)
    {
        var style = Terrain.Style;

        // Небо, солнце, хребты с параллаксом, облака и деревья на поверхности.
        // Раньше здесь была одна плоская заливка цветом Sky.
        Scenery.Build(WorldRoot, Terrain, seed);

        // Квада воды есть всегда, даже в пещере: потоп зальёт и её,
        // а пока уровень ниже нуля — квада просто выключена.
        var water = Sprites.Make("Water", Sprites.Square, style.Water, 15, WorldRoot);
        _water = water.transform;
        LayoutWater();
    }

    /// Ставит кваду так, чтобы её верхняя кромка совпала с уровнем воды.
    void LayoutWater()
    {
        if (_water == null) return;
        float level = DestructibleTerrain.WaterLevel;
        _water.gameObject.SetActive(level > 0f);
        _water.position = new Vector3(DestructibleTerrain.WorldWidth * 0.5f, level * 0.5f - 20f, 0f);
        _water.localScale = new Vector3(DestructibleTerrain.WorldWidth * 3f, level + 40f, 1f);
    }

    /// Точки старта, блоками по командам: сначала все черви первой, потом второй.
    /// Берём все площадки карты — включая полки под навесами, полы каверн и
    /// плиты в воздухе — и разводим червей жадным «дальше всех от уже занятых».
    /// Так команды перемешаны по карте и по этажам, а не сидят двумя кучами
    /// вдоль силуэта, как раньше.
    List<Vector2> PickSpawnPoints(int teams, int perTeam, int seed)
        => SpawnLayout(Terrain, teams * perTeam, seed);

    /// Та же раскладка отдельно от матча — ею пользуются тесты и снимки миров.
    public static List<Vector2> SpawnLayout(DestructibleTerrain terrain, int need, int seed)
    {
        var candidates = terrain.CollectSpawnPoints(1f);
        var result = new List<Vector2>();

        if (candidates.Count == 0)
        {
            for (int i = 0; i < need; i++)
                result.Add(new Vector2(10f + i * 6f, DestructibleTerrain.WorldHeight * 0.7f));
            return result;
        }

        var rnd = new System.Random(seed ^ 0x5EED);
        var picked = new List<Vector2>();
        var best = new float[candidates.Count];
        for (int i = 0; i < best.Length; i++) best[i] = float.MaxValue;

        // Первую точку берём случайную, дальше каждый раз ту, что дальше всех
        // от уже занятых. По горизонтали карта в два раза шире, чем по
        // вертикали, поэтому этажи в метрике весят вдвое — иначе весь разброс
        // уходит вширь и разные уровни не задействуются.
        int idx = rnd.Next(candidates.Count);
        while (picked.Count < need && picked.Count < candidates.Count)
        {
            var p = candidates[idx];
            picked.Add(p);

            int next = 0;
            float far = -1f;
            for (int i = 0; i < candidates.Count; i++)
            {
                var d = candidates[i] - p;
                float dist = d.x * d.x + (2f * d.y) * (2f * d.y);
                if (dist < best[i]) best[i] = dist;
                if (best[i] > far) { far = best[i]; next = i; }
            }
            idx = next;
        }

        // Площадок меньше, чем червей, — досаживаем по кругу.
        for (int i = picked.Count; i < need; i++) picked.Add(candidates[i % candidates.Count]);

        // Перемешиваем, чтобы соседние точки не достались одной команде.
        for (int i = picked.Count - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (picked[i], picked[j]) = (picked[j], picked[i]);
        }

        result.AddRange(picked);
        return result;
    }

    public List<Worm> AllWorms()
    {
        var list = new List<Worm>();
        for (int t = 0; t < Teams.Count; t++)
            for (int i = 0; i < Teams[t].Worms.Count; i++)
                if (Teams[t].Worms[i] != null) list.Add(Teams[t].Worms[i]);
        return list;
    }

    void StartTurn()
    {
        if (CheckGameOver()) return;

        Wind = Random.Range(-1f, 1f);
        TurnTimeLeft = Config.TurnTime;

        // Ищем следующую живую команду.
        for (int i = 0; i < Teams.Count; i++)
        {
            var team = Teams[CurrentTeam];
            if (team.Alive)
            {
                ActiveWorm = team.NextAliveWorm();
                break;
            }
            CurrentTeam = (CurrentTeam + 1) % Teams.Count;
        }

        if (ActiveWorm == null) { CheckGameOver(); return; }

        // Оружие с закончившимися патронами не оставляем выбранным.
        if (!HasAmmo(SelectedWeapon)) SelectWeapon(0);

        ActiveWorm.BeginTurn();
        Cam.Follow(ActiveWorm.transform);
        State = GameState.Aim;
        Sfx.TurnStart();

        MaybeDropCrate();
    }

    /// Ящик с припасами падает в начале хода — с вероятностью из конфигурации.
    void MaybeDropCrate()
    {
        float chance = Config != null ? Config.CrateChance : 0f;
        if (chance <= 0f || Random.value > chance) return;
        // Больше четырёх ящиков на карте — это уже свалка, а не подарок.
        if (Crate.All.Count >= 4) return;
        Crate.DropRandom(Terrain);
    }

    public bool HasAmmo(int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        var team = Teams[CurrentTeam];
        return !team.Ammo.TryGetValue(w.Kind, out int a) || a != 0;
    }

    public int AmmoOf(int weaponIndex)
    {
        var w = Weapon.All[weaponIndex];
        return Teams[CurrentTeam].Ammo.TryGetValue(w.Kind, out int a) ? a : -1;
    }

    /// Списать патрон, не заканчивая ход: так тратятся верёвка и телепорт.
    public void ConsumeAmmo(WeaponKind kind)
    {
        var team = Teams[CurrentTeam];
        if (team.Ammo.TryGetValue(kind, out int a) && a > 0) team.Ammo[kind] = a - 1;
        if (!HasAmmo(SelectedWeapon)) SelectWeapon(0);
    }

    /// Снаряжение сработало и ход на этом кончается (телепорт). Окно отхода тут
    /// не нужно: прятаться уже некуда — червь только что переместился сам.
    public void EndTurnAfterUtility()
    {
        State = GameState.Projectile;
        TurnTimeLeft = 0.5f;
    }

    public void SelectWeapon(int index)
    {
        if (index < 0 || index >= Weapon.All.Length) return;
        if (!HasAmmo(index)) return;
        SelectedWeapon = index;
    }

    /// Перебор оружия вперёд-назад, мимо пустых — так удобнее бамперам геймпада.
    public void CycleWeapon(int step)
    {
        if (step == 0) return;
        int n = Weapon.All.Length;
        for (int i = 1; i <= n; i++)
        {
            int idx = ((SelectedWeapon + step * i) % n + n) % n;
            if (HasAmmo(idx)) { SelectedWeapon = idx; return; }
        }
    }

    public void OnWeaponFired()
    {
        var team = Teams[CurrentTeam];
        if (team.Ammo.TryGetValue(CurrentWeapon.Kind, out int a) && a > 0)
            team.Ammo[CurrentWeapon.Kind] = a - 1;

        // Отход считаем от выстрела: всё, что прилетит червю после этого мига,
        // отменит ему побег.
        _shooterHurt = false;

        // Мину и динамит червь кладёт себе под ноги — смотреть на полёт нечего,
        // и бежать надо прямо сейчас, пока горит фитиль.
        var fired = CurrentWeapon;
        if (fired.Use == WeaponUse.Drop && fired.Kind != WeaponKind.Sheep)
        {
            EnterRetreat(FuseRetreatTime);
            return;
        }

        State = GameState.Projectile;
        TurnTimeLeft = ShotWatch;

        // Снаряд рождается раньше, чем ход переходит в состояние Projectile,
        // поэтому цепляем камеру здесь, а не в RegisterProjectile.
        if (_projectiles.Count > 0)
            Cam.Follow(_projectiles[_projectiles.Count - 1].transform, true);
    }

    public void RegisterProjectile(Projectile p)
    {
        _projectiles.Add(p);
        // Осколки кассетного снаряда появляются уже в полёте — за ними следим сразу,
        // перебивая задержку на воронке родительского взрыва.
        if (State == GameState.Projectile) Cam.Follow(p.transform, true);
    }

    public void UnregisterProjectile(Projectile p)
    {
        _projectiles.Remove(p);
        if (_projectiles.Count > 0)
            Cam.Follow(_projectiles[_projectiles.Count - 1].transform, true);
        // Взорвался — камеру не уводим: Combat.Detonate задержал её на воронке.
        // А если снаряд просто утонул или улетел за край, смотреть не на что.
        else if (!Cam.IsHolding && ActiveWorm != null)
            Cam.Follow(ActiveWorm.transform, true);
    }

    public void OnWormDied(Worm w)
    {
        if (w == ActiveWorm) ActiveWorm = null;
    }

    /// Червь прощается перед взрывом. Пока счётчик не нулевой, мир не считается
    /// успокоившимся и ход не передаётся — иначе прощание доигрывалось бы уже
    /// в чужом ходу, а памятник вставал под ногами следующего червя.
    public void BeginDeathAnim() => _dying++;
    public void EndDeathAnim() => _dying = Mathf.Max(0, _dying - 1);

    /// Урон записываем на команду, чей сейчас ход, — для статистики на экране итогов.
    public void RegisterDamage(Worm victim, float dmg)
    {
        if (dmg <= 0f) return;
        // Своим же взрывом (или водой) стрелявшего задело — прятаться он не пойдёт.
        if (victim != null && victim == ActiveWorm) _shooterHurt = true;
        if (CurrentTeam >= 0 && CurrentTeam < Teams.Count)
            Teams[CurrentTeam].DamageDealt += dmg;
    }

    void Update()
    {
        FloodTick();

        switch (State)
        {
            case GameState.Aim:
                TurnTimeLeft -= Time.deltaTime;
                if (ActiveWorm == null || ActiveWorm.IsDead) { EnterSettle(1.0f); break; }
                if (TurnTimeLeft <= 0f) EnterSettle(0.6f);
                break;

            case GameState.Projectile:
                TurnTimeLeft -= Time.deltaTime;
                _projectiles.RemoveAll(p => p == null);
                // Отход начинается, когда взрыв уже показан: пока камера держит
                // воронку, бежать некуда — игрок всё равно смотрит не на червя.
                if (_projectiles.Count == 0 && TurnTimeLeft <= 0f && !Cam.IsHolding)
                    EnterRetreat(RetreatTime);
                // Снаряд ещё летит — продлеваем окно ожидания.
                if (_projectiles.Count > 0) TurnTimeLeft = Mathf.Max(TurnTimeLeft, 0.5f);
                break;

            case GameState.Retreat:
                TurnTimeLeft -= Time.deltaTime;
                // Задело своим же — прятаться уже поздно, ход кончается сразу.
                if (ActiveWorm == null || ActiveWorm.IsDead || _shooterHurt) { EnterSettle(0.8f); break; }
                if (TurnTimeLeft <= 0f) EnterSettle(0.8f);
                break;

            case GameState.Settle:
                _settleTimer -= Time.deltaTime;
                _projectiles.RemoveAll(p => p == null);
                // Ход не передаём, пока камера досматривает взрыв.
                if (_settleTimer <= 0f && _projectiles.Count == 0 && WorldIsCalm() && !Cam.IsHolding)
                    NextTurn();
                break;
        }
    }

    /// Вода доходит до новой отметки не мгновенно, а за доли секунды: так видно,
    /// как её кромка накрывает червя, и он успевает начать тонуть.
    void FloodTick()
    {
        if (float.IsNaN(_waterTarget)) return;
        float level = DestructibleTerrain.WaterLevel;
        if (Mathf.Approximately(level, _waterTarget)) { _waterTarget = float.NaN; return; }

        DestructibleTerrain.SetWaterLevel(Mathf.MoveTowards(level, _waterTarget, FloodSpeed * Time.deltaTime));
        LayoutWater();
    }

    /// Окно отхода: червь уже отстрелялся, но ещё может убежать. Не даём его
    /// тому, кого задело своим же выстрелом: прятаться уже поздно. Бот отходом
    /// пользуется наравне с человеком — на этом и держится его динамит.
    void EnterRetreat(float seconds)
    {
        if (ActiveWorm == null || ActiveWorm.IsDead || _shooterHurt)
        {
            EnterSettle(0.8f);
            return;
        }

        State = GameState.Retreat;
        TurnTimeLeft = seconds;
        Cam.Follow(ActiveWorm.transform, true);
    }

    void EnterSettle(float delay)
    {
        State = GameState.Settle;
        _settleTimer = delay;
        // Досматривать чужой ход, вися на верёвке, нельзя: она держит только своего.
        if (ActiveWorm != null) ActiveWorm.ReleaseRope();
    }

    bool WorldIsCalm()
    {
        if (_dying > 0) return false;

        var worms = AllWorms();
        for (int i = 0; i < worms.Count; i++)
        {
            var w = worms[i];
            if (w == null || w.IsDead) continue;
            if (w.Velocity.sqrMagnitude > 0.6f) return false;
        }
        return true;
    }

    void NextTurn()
    {
        if (CheckGameOver()) return;

        // Раунд — круг по всем командам. Считаем ходы, а не обороты индекса:
        // выбывшая команда сбивала бы счёт.
        _turnsTaken++;
        int round = _turnsTaken / Mathf.Max(1, Teams.Count) + 1;
        if (round != Round) Round = round;

        // Потоп прибывает каждый ход, а не раз в круг: с четырьмя командами
        // раз в круг вода стояла бы почти весь матч.
        TickFlood();

        CurrentTeam = (CurrentTeam + 1) % Teams.Count;
        int guard = 0;
        while (!Teams[CurrentTeam].Alive && guard++ < Teams.Count)
            CurrentTeam = (CurrentTeam + 1) % Teams.Count;
        StartTurn();
    }

    /// Потоп: с назначенного раунда вода прибывает каждый ход. Здоровье никому
    /// не срезаем — карта сжимается сама, черви внизу тонут, и матч всё равно
    /// не может длиться вечно.
    void TickFlood()
    {
        if (Config == null || Config.FloodRound <= 0) return;
        if (Round < Config.FloodRound) return;

        float from = float.IsNaN(_waterTarget) ? DestructibleTerrain.WaterLevel : _waterTarget;
        _waterTarget = from + FloodStep;
        LayoutWater();

        if (!Flooding)
        {
            // Первый ход потопа объявляем сиреной, дальше — плеском прибывающей
            // воды: сирена каждый ход быстро надоела бы.
            Flooding = true;
            Sfx.Flood();
            Cam.Shake(0.35f);
        }
        else
        {
            Sfx.Splash();
            Cam.Shake(0.12f);
        }
    }

    bool CheckGameOver()
    {
        int alive = 0;
        Team last = null;
        int lastIdx = -1;
        for (int i = 0; i < Teams.Count; i++)
            if (Teams[i].Alive) { alive++; last = Teams[i]; lastIdx = i; }

        if (alive <= 1)
        {
            State = GameState.GameOver;
            WinnerText = last != null ? "Победила команда «" + last.Name + "»" : "Ничья";
            ActiveWorm = null;
            if (!_reported)
            {
                _reported = true;
                if (App.I != null) App.I.OnMatchOver(BuildResults(lastIdx));
            }
            return true;
        }
        return false;
    }

    MatchResults BuildResults(int winnerIdx)
    {
        var r = new MatchResults
        {
            WinnerTeam = winnerIdx,
            Title = WinnerText
        };
        for (int i = 0; i < Teams.Count; i++)
        {
            var team = Teams[i];
            int aliveCount = 0;
            for (int k = 0; k < team.Worms.Count; k++)
                if (team.Worms[k] != null && !team.Worms[k].IsDead) aliveCount++;

            r.Teams.Add(new MatchResults.Row
            {
                Name = team.Name,
                Color = team.Color,
                WormsAlive = aliveCount,
                WormsTotal = team.Worms.Count,
                DamageDealt = team.DamageDealt,
                IsWinner = i == winnerIdx
            });
        }
        return r;
    }
}
