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
    bool _conceding;    // идут смерти сдавшейся команды: их урон ничей
    bool _shooterHurt;  // стрелявшего задело его же выстрелом — отход отменяется
    Worm _shooter;      // кто стрелял в этом ходу: ему и отзываться на итог
    float _shotDamage;  // сколько он с выстрела снял с чужих червей
    float _waterTarget = float.NaN;   // куда ползёт вода; NaN — стоит на месте
    Water _water;       // тело, кромка и блики: при потопе всё это едет вверх

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
        _seed = Config.Seed != 0 ? Config.Seed : Random.Range(0, 100000);
        BuildWorld(_seed);
    }

    public void Restart()
    {
        Generation++;
        if (WorldRoot != null) Destroy(WorldRoot.gameObject);
        Teams.Clear();
        _projectiles.Clear();
        Crate.Forget();
        Mine.Forget();
        Barrel.Forget();
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
        _conceding = false;
        _shooter = null;
        _shotDamage = 0f;
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
        Mine.Forget();
        Barrel.Forget();

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
        // Мины и бочки берут точки из той же раскладки, что и черви: она уже
        // разводит всё, что ставит, «дальше всех от занятого», поэтому просить
        // у неё больше точек дешевле, чем считать вторую.
        int wormCount = teamCount * wormsPerTeam;
        int scatterCount = Config.MineCount + Config.BarrelCount;
        var spawnPoints = PickSpawnPoints(teamCount, wormsPerTeam, seed, scatterCount);

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
            var team = new Team
            {
                Name = setup.Name,
                Color = setup.Color,
                GraveKind = graves[t % graves.Count],
                VoiceBank = setup.VoiceBank
            };
            // Банк голоса греем сразу: первая же фраза считалась бы в тот кадр,
            // когда червь выстрелил, а это заметная задержка на телефоне.
            Voice.Prewarm(team.VoiceBank);
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

        ScatterWorld(spawnPoints, wormCount, seed);

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

        // Вода есть всегда, даже в пещере: потоп зальёт и её, а пока уровень
        // ниже нуля — она просто выключена.
        _water = Water.Build(WorldRoot, style, seed);
    }

    /// Ставит воду на текущий уровень. Кромка едет и сама, каждый кадр, но
    /// после шага потопа её двигаем сразу — иначе волна отстанет на кадр.
    void LayoutWater()
    {
        if (_water != null) _water.Layout();
    }

    /// Точки старта, блоками по командам: сначала все черви первой, потом второй.
    /// Берём все площадки карты — включая полки под навесами, полы каверн и
    /// плиты в воздухе — и разводим червей жадным «дальше всех от уже занятых».
    /// Так команды перемешаны по карте и по этажам, а не сидят двумя кучами
    /// вдоль силуэта, как раньше.
    List<Vector2> PickSpawnPoints(int teams, int perTeam, int seed, int extra = 0)
        => SpawnLayout(Terrain, teams * perTeam + extra, seed);

    /// Та же раскладка отдельно от матча — ею пользуются тесты и снимки миров.
    public static List<Vector2> SpawnLayout(DestructibleTerrain terrain, int need, int seed)
    {
        // Сначала ищем только ровные площадки. Если карта скалистая и их
        // меньше, чем нужно с запасом, отпускаем допуск по наклону — лучше
        // посадить червя на склон, чем свалить всю команду в одну кучу.
        var candidates = terrain.CollectSpawnPoints(1f, 0.4f);
        if (candidates.Count < need * 2) candidates = terrain.CollectSpawnPoints(0.5f, 0.6f);
        if (candidates.Count < need) candidates = terrain.CollectSpawnPoints(0.5f, 1.2f);
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

    /// Мины и бочки на карте: хвост общей раскладки, начиная с точки после
    /// последнего червя. Точку берём не любую — мина не должна лечь под ногами
    /// у червя на старте (иначе первый же шаг стоил бы тридцати очков ни за
    /// что) и не должна лечь вплотную к соседней, иначе весь запас уйдёт
    /// в одну кучу там, где раскладка развернулась по кругу.
    void ScatterWorld(List<Vector2> points, int wormCount, int seed)
    {
        int mines = Config != null ? Config.MineCount : 0;
        int barrels = Config != null ? Config.BarrelCount : 0;
        if (mines + barrels <= 0) return;

        // Черви уже стоят на своих точках, но брать их из Teams надёжнее, чем
        // из списка: часть точек могла не достаться никому.
        var worms = AllWorms();
        var taken = new List<Vector2>();

        int placedMines = 0, placedBarrels = 0;
        for (int i = wormCount; i < points.Count; i++)
        {
            var p = points[i];
            if (p.y < DestructibleTerrain.WaterLevel + 1f) continue;

            bool tooClose = false;
            for (int k = 0; k < worms.Count && !tooClose; k++)
                if (worms[k] != null && Vector2.Distance(worms[k].transform.position, p) < WildSafeRange)
                    tooClose = true;
            for (int k = 0; k < taken.Count && !tooClose; k++)
                if (Vector2.Distance(taken[k], p) < 2.5f) tooClose = true;
            if (tooClose) continue;

            // Бочки ставим первыми: их меньше, и лучше отдать им точки
            // получше, чем оставить пять бочек в одном углу.
            if (placedBarrels < barrels) { Barrel.Place(p); placedBarrels++; }
            else if (placedMines < mines) { Mine.Scatter(p + Vector2.up * 0.3f); placedMines++; }
            else break;

            taken.Add(p);
        }
    }

    /// Ближе этого к червю дикая мина не ложится: радиус срабатывания 1,6,
    /// и запас нужен на то, что червь ещё и сползёт по склону, пока встаёт.
    const float WildSafeRange = 4.5f;

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

        // Реплика прошлого хода не должна доиграться в этом: сказанное чужим
        // червём звучит так, будто говорит тот, кто только что вышел.
        Voice.Hush();

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
        var crate = Crate.DropRandom(Terrain);
        // Сброс — бросок случайных чисел, повторить его у клиента нечем:
        // хост объявляет упавший ящик отдельным сообщением.
        if (crate != null && NetGame.I != null && NetGame.I.Match != null)
            NetGame.I.Match.SendCrateDrop(crate.Kind, crate.transform.position.x);
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

    /// Можно ли прямо сейчас передать ход другому червю своей команды. Окно
    /// открыто в начале хода и закрывается первым же действием: походил,
    /// прыгнул или выстрелил — играешь тем, кем начал.
    public bool CanSelectWorm =>
        State == GameState.Aim && ActiveWorm != null && !ActiveWorm.IsDead
        && !ActiveWorm.HasActed && Teams[CurrentTeam].AliveCount > 1;

    /// Выбор червя, как в оригинале: ход начинается не обязательно с того, кем
    /// хочется играть. Меняем ровно одно — активного червя; таймер хода, ветер,
    /// выбранное оружие и очередь команд остаются как были. Team.ActiveIndex
    /// сдвигается вместе с выбором, поэтому следующий круг пойдёт от нового
    /// червя — перебор по кругу от этого не сбивается.
    /// Возвращает true, если червь действительно сменился.
    public bool SelectNextWorm()
    {
        if (!CanSelectWorm) return false;

        var next = Teams[CurrentTeam].NextAliveWorm();
        if (next == null || next == ActiveWorm) return false;

        // Прежний червь остаётся стоять там же: он ничего не успел сделать,
        // а Settle через мгновение снова его приморозит.
        ActiveWorm.ReleaseRope();
        ActiveWorm = next;
        next.BeginTurn();
        Cam.Follow(next.transform);
        Sfx.TurnStart();
        return true;
    }

    /// Можно ли прямо сейчас пропустить ход. Окно шире, чем у выбора червя:
    /// пропустить не грех и после того, как походил, — в оригинале «пропустить»
    /// как раз и означает «мне тут делать нечего».
    public bool CanSkipTurn =>
        (State == GameState.Aim || State == GameState.Retreat)
        && ActiveWorm != null && !ActiveWorm.IsDead && !NetSim.Mirror;

    /// Пропустить ход. Отдельного состояния не заводим: это ровно то же, что
    /// истёкший таймер, — червь замирает, мир успокаивается, ход уходит дальше.
    public void SkipTurn()
    {
        if (!CanSkipTurn) return;
        TurnTimeLeft = 0f;
        EnterSettle(0.4f);
    }

    /// Можно ли сдаться: в сетевом матче решение о смерти принимает хост,
    /// а бот за себя не сдаётся — сдаётся человек за устройством.
    public bool CanSurrender =>
        State != GameState.GameOver && !NetSim.Mirror
        && CurrentTeam >= 0 && CurrentTeam < Teams.Count && !Teams[CurrentTeam].IsBot;

    /// Команда уходит с карты. Черви гибнут тем же путём, что и последний червь
    /// команды в бою, — с прощанием, взрывом и памятником, — поэтому итоги
    /// считает всё та же CheckGameOver и никакой отдельной ветки «сдался» в них
    /// нет. Разница ровно одна: урон сдачи никому не записывается (Worm.Concede).
    public void Surrender(int team)
    {
        if (team < 0 || team >= Teams.Count) return;
        if (State == GameState.GameOver) return;

        var t = Teams[team];
        _conceding = true;
        for (int i = 0; i < t.Worms.Count; i++)
        {
            var w = t.Worms[i];
            if (w != null && !w.IsDead) w.Concede();
        }

        // Сдаваться было некому — снимаем метку сразу, иначе она залипнет.
        if (_dying == 0) _conceding = false;

        // Сдалась команда, чей сейчас ход, — ход обрывается прямо здесь.
        // Сдалась чужая — свой ход текущая команда доигрывает, а матч сойдётся
        // на ближайшей передаче хода: CheckGameOver считает живые команды.
        if (team == CurrentTeam && State != GameState.Settle) EnterSettle(0.6f);
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

        // Червь отзывается на свой выстрел, а итог — попал или промазал —
        // объявит уже в конце хода, когда всё отгремело.
        _shooter = ActiveWorm;
        _shotDamage = 0f;
        Voice.Say(ActiveWorm, Voice.Line.Fire);

        // Мину и динамит червь кладёт себе под ноги — смотреть на полёт нечего,
        // и бежать надо прямо сейчас, пока горит фитиль. Овца и супер-овца —
        // другое дело: они уходят с места сами, и ход идёт за ними.
        var fired = CurrentWeapon;
        if (fired.Use == WeaponUse.Drop && !fired.Walker)
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

        // Из хвоста списка выбрасываем уже уничтоженное. Взрыв рвёт соседние
        // снаряды цепью — кассету, капли напалма, пачку бомб налёта, — и они
        // уходят одним кадром: последним в списке вполне может лежать тот, чей
        // GameObject уже разрушен, а камера потянулась бы к его transform.
        while (_projectiles.Count > 0 && _projectiles[_projectiles.Count - 1] == null)
            _projectiles.RemoveAt(_projectiles.Count - 1);

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
    public void EndDeathAnim()
    {
        _dying = Mathf.Max(0, _dying - 1);
        // Последний сдавшийся червь догорел — дальше урон снова боевой.
        if (_dying == 0) _conceding = false;
    }

    /// Урон записываем на команду, чей сейчас ход, — для статистики на экране итогов.
    public void RegisterDamage(Worm victim, float dmg)
    {
        if (dmg <= 0f) return;
        // Своим же взрывом (или водой) стрелявшего задело — прятаться он не пойдёт.
        if (victim != null && victim == ActiveWorm) _shooterHurt = true;
        // Взрывы червей, ушедших с карты по сдаче, никому в заслугу не идут:
        // Worm.Concede обходит TakeDamage, но осколки его смерти — нет.
        if (_conceding) return;
        if (CurrentTeam >= 0 && CurrentTeam < Teams.Count)
            Teams[CurrentTeam].DamageDealt += dmg;

        // Попаданием считаем только чужого червя: подорвать себя или соседа
        // по команде — не то, чем хвастаются.
        if (_shooter != null && victim != null && victim.Team != Teams[CurrentTeam])
            _shotDamage += dmg;
    }

    void Update()
    {
        FloodTick();

        // Сетевой клиент ход не ведёт: чей ход, сколько осталось и что
        // случилось — всё это ему присылает хост. Здесь остаётся только
        // отсчёт времени, чтобы плашка хода не стояла между сообщениями.
        if (NetSim.Mirror)
        {
            if (State == GameState.Aim || State == GameState.Projectile || State == GameState.Retreat)
                TurnTimeLeft = Mathf.Max(0f, TurnTimeLeft - Time.deltaTime);
            return;
        }

        switch (State)
        {
            case GameState.Aim:
                TurnTimeLeft -= Time.deltaTime;
                if (ActiveWorm == null || ActiveWorm.IsDead) { EnterSettle(1.0f); break; }
                // Выбор червя читаем здесь, а не в Worm.HandleInput: он меняет
                // самого активного червя, и делать это изнутри его же обновления
                // значило бы менять землю под ногами у идущего кода.
                if (ActiveWorm.Controls != null && ActiveWorm.Controls.SelectWormPressed) SelectNextWorm();
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

    /// Принять ход, назначенный хостом. Клиент сам ходы не переключает:
    /// иначе два устройства решали бы это независимо и рано или поздно
    /// разошлись бы в том, чей сейчас червь, — а это худший вид расхождения,
    /// потому что виден он не сразу.
    public void ApplyRemoteTurn(int team, int wormIndex, GameState state, int round,
                                float wind, float timeLeft, int weapon, float water)
    {
        if (Teams.Count == 0) return;

        DestructibleTerrain.SetWaterLevel(water);
        LayoutWater();

        Wind = wind;
        Round = Mathf.Max(1, round);
        TurnTimeLeft = timeLeft;
        if (weapon >= 0 && weapon < Weapon.All.Length) SelectedWeapon = weapon;

        CurrentTeam = Mathf.Clamp(team, 0, Teams.Count - 1);
        var current = Teams[CurrentTeam];
        Worm next = wormIndex >= 0 && wormIndex < current.Worms.Count ? current.Worms[wormIndex] : null;

        if (next != ActiveWorm)
        {
            if (wormIndex >= 0) current.ActiveIndex = wormIndex;
            ActiveWorm = next;
            if (ActiveWorm != null && !ActiveWorm.IsDead)
            {
                ActiveWorm.BeginTurn();
                Cam.Follow(ActiveWorm.transform);
                Sfx.TurnStart();
            }
        }

        State = state;
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
        SayShotVerdict();
    }

    /// Итог выстрела голосом стрелявшего. Место выбрано в конце хода, а не
    /// сразу после взрыва: динамит и мина рвутся уже в отходе, и объявленный
    /// раньше промах через секунду оказался бы попаданием. Ход дожидается
    /// реплики — иначе Hush на старте следующего срежет её на полуслове.
    void SayShotVerdict()
    {
        if (_shooter == null) return;
        var shooter = _shooter;
        float damage = _shotDamage;
        _shooter = null;
        _shotDamage = 0f;

        if (shooter.IsDead) return;   // мёртвый уже попрощался, добавить ему нечего
        float wait = Voice.Say(shooter, damage > 0f ? Voice.Line.Hit : Voice.Line.Miss);
        _settleTimer = Mathf.Max(_settleTimer, wait);
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
