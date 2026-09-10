using System.Collections.Generic;
using UnityEngine;

/// Что именно едет по проводу внутри NetMsg.Event.
public enum NetEvent : byte
{
    Blast = 1,    // воронка от взрыва
    Dig,          // прогрызенный коридор бура
    Beam,         // поставленная балка
    CrateDrop,    // ящик с припасами упал в точке
    CrateGone,    // ящик подобрали или он взорвался
    GameOver      // бой окончен, вот победитель
}

/// Сетевая часть самого боя: поток ввода, точки синхронизации, разрушения
/// ландшафта и сверка состояния в конце хода.
///
/// Правило одно и простое: хост считает, клиент повторяет. Клиент крутит ту
/// же физику ради живой картинки — червь идёт под пальцем без задержки, — но
/// воронки, урон и смена хода приходят к нему готовыми. Поэтому расхождение
/// не может копиться: всё, что не сходится, стирается ближайшей сверкой, а
/// сверка случается каждый ход.
public class NetMatch
{
    readonly NetGame _net;

    /// Ввод по командам. У хоста заполняется пакетами от хозяев команд, у
    /// клиента — пересылкой хода активного игрока.
    readonly Dictionary<int, NetInput> _inputs = new Dictionary<int, NetInput>();

    /// Журнал разрушений с начала матча: хост складывает сюда каждую
    /// операцию, которую разослал, и целиком пересылает вернувшемуся.
    /// Ландшафт из сида строится одинаково у всех, значит карту довольно
    /// догнать теми же операциями, что её и меняли, — гонять по проводу
    /// маску в мегапиксель не нужно.
    readonly List<TerrainOp> _ops = new List<TerrainOp>();

    ushort _frame;
    bool _bound;

    // последнее разосланное состояние хода — чтобы не слать одно и то же
    int _sentTeam = -1;
    int _sentWorm = -1;
    GameState _sentState = GameState.GameOver;

    public NetMatch(NetGame net)
    {
        _net = net;
    }

    GameManager Gm => GameManager.I;

    public void OnMatchBegan()
    {
        _bound = false;
        _frame = 0;
        _sentTeam = -1;
        _sentWorm = -1;
        _ops.Clear();
        NetSim.Authority = _net.IsHost;

        if (_net.IsHost)
        {
            // Только хост записывает разрушения: у клиента ландшафт меняется
            // исключительно тем, что пришло по проводу.
            DestructibleTerrain.OpSink = SendTerrainOp;
        }
        else
        {
            DestructibleTerrain.OpSink = null;
        }
    }

    /// Ввод команды: у хоста — то, что прислал её хозяин, у клиента — то, что
    /// переслал хост. Своя команда у клиента идёт мимо: её он предсказывает
    /// сам, локальным вводом, иначе прицел ходил бы с задержкой пинга.
    NetInput InputFor(int team)
    {
        if (!_inputs.TryGetValue(team, out var input))
        {
            input = new NetInput();
            _inputs[team] = input;
        }
        return input;
    }

    /// Раздать командам источники ввода. Делается один раз, когда мир уже
    /// построен: до этого списка команд ещё нет.
    void TryBind()
    {
        var gm = Gm;
        if (gm == null || gm.Teams.Count == 0) return;

        for (int t = 0; t < gm.Teams.Count; t++)
        {
            var team = gm.Teams[t];
            if (team.IsBot) continue;              // бота ведёт хост, и он остаётся ботом

            bool mine = t == _net.LocalTeam;
            if (mine) team.Controller = null;      // за своей командой — живой ввод устройства
            else team.Controller = InputFor(t);    // чужой ход показываем по присланному вводу
        }
        _bound = true;
    }

    /// Хозяин команды отвалился: до конца боя её ведёт бот, чтобы матч не
    /// встал на месте у ушедшего игрока.
    public void HandOverToBot(int team)
    {
        var gm = Gm;
        if (gm == null || team < 0 || team >= gm.Teams.Count) return;
        if (!_net.IsHost) return;

        gm.Teams[team].Controller = new BotInput(gm.Teams[team], gm.Config.Difficulty, team * 977 + 13);
        _inputs.Remove(team);
    }

    // --- цикл -------------------------------------------------------------

    public void Tick()
    {
        var gm = Gm;
        if (gm == null) return;
        if (!_bound) TryBind();

        if (_net.IsHost) HostTick(gm);
        else ClientTick(gm);

        // Разовые нажатия живут один кадр — как у живого ввода.
        foreach (var kv in _inputs) kv.Value.ConsumeEdges();
    }

    void HostTick(GameManager gm)
    {
        RelayActiveInput(gm);
        MaybeSendTurn(gm);
    }

    void ClientTick(GameManager gm)
    {
        // Свой ход — шлём ввод хосту. Не свой — молчим: чужого червя мы не
        // двигаем, а хост всё равно не примет чужие команды.
        if (gm.CurrentTeam == _net.LocalTeam && GameInput.Player != null)
        {
            var w = new NetWriter(24);
            w.U8((byte)NetMsg.Input);
            w.U8((byte)gm.CurrentTeam);
            NetInput.Write(ref w, GameInput.Player, _frame++);
            _net.SendTo(0, ref w, NetChannel.Unreliable);
        }
    }

    /// Хост пересылает ввод того, чей сейчас ход, — кем бы тот ни был: живым
    /// игроком за этим устройством, ботом или удалённым игроком. Одно правило
    /// на все три случая, поэтому у клиентов чужой ход выглядит одинаково
    /// живым независимо от того, кто его играет.
    void RelayActiveInput(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        if (worm == null) return;
        var src = worm.Controls;
        if (src == null) return;

        int team = gm.CurrentTeam;
        var w = new NetWriter(24);
        w.U8((byte)NetMsg.Input);
        w.U8((byte)team);
        NetInput.Write(ref w, src, _frame++);

        // Хозяину команды его же ввод не возвращаем: он у себя уже всё
        // показал, а эхо только дёргало бы картинку.
        _net.SendAll(ref w, NetChannel.Unreliable, _net.OwnerOf(team));
    }

    void MaybeSendTurn(GameManager gm)
    {
        int worm = ActiveWormIndex(gm);
        if (gm.CurrentTeam == _sentTeam && worm == _sentWorm && gm.State == _sentState) return;

        bool newTurn = gm.CurrentTeam != _sentTeam || worm != _sentWorm;
        _sentTeam = gm.CurrentTeam;
        _sentWorm = worm;
        _sentState = gm.State;

        // Начало хода — та самая точка, где состояние обязано сойтись:
        // сверка идёт первой, чтобы клиент вошёл в новый ход уже с
        // хостовыми позициями и здоровьем.
        if (newTurn) SendSnapshot(-1);
        SendTurn(-1);
    }

    static int ActiveWormIndex(GameManager gm)
    {
        var worm = gm.ActiveWorm;
        if (worm == null || worm.Team == null) return -1;
        return worm.Team.Worms.IndexOf(worm);
    }

    // --- смена хода --------------------------------------------------------

    void SendTurn(int peer)
    {
        var gm = Gm;
        if (gm == null) return;

        var w = new NetWriter(24);
        w.U8((byte)NetMsg.Turn);
        w.I8((sbyte)gm.CurrentTeam);
        w.I8((sbyte)ActiveWormIndex(gm));
        w.U8((byte)gm.State);
        w.U8((byte)Mathf.Clamp(gm.Round, 0, 255));
        w.Unit(gm.Wind);
        w.U16((ushort)Mathf.Clamp(Mathf.RoundToInt(gm.TurnTimeLeft * 100f), 0, 65535));
        w.U8((byte)gm.SelectedWeapon);
        w.Coord(DestructibleTerrain.WaterLevel);

        if (peer < 0) _net.SendAll(ref w, NetChannel.Reliable);
        else _net.SendTo(peer, ref w, NetChannel.Reliable);
    }

    void ApplyTurn(ref NetReader r)
    {
        var gm = Gm;
        int team = r.I8();
        int worm = r.I8();
        var state = (GameState)r.U8();
        int round = r.U8();
        float wind = r.Unit();
        float timeLeft = r.U16() / 100f;
        int weapon = r.U8();
        float water = r.Coord();
        if (!r.Ok || gm == null) return;

        NetSim.Applying = true;
        gm.ApplyRemoteTurn(team, worm, state, round, wind, timeLeft, weapon, water);
        NetSim.Applying = false;
    }

    // --- сверка состояния --------------------------------------------------

    /// Всё состояние целиком. Здесь его немного: черви, ветер, вода, ход и
    /// боезапас. Ландшафт в сверку не входит намеренно — он у клиента и так
    /// хостовый, потому что все воронки к нему приходят по проводу, а не
    /// возникают из его собственного счёта.
    void SendSnapshot(int peer)
    {
        var gm = Gm;
        if (gm == null || gm.Teams.Count == 0) return;

        var w = new NetWriter(512);
        w.U8((byte)NetMsg.Snapshot);
        w.Coord(DestructibleTerrain.WaterLevel);
        w.Unit(gm.Wind);
        w.U8((byte)gm.Teams.Count);

        for (int t = 0; t < gm.Teams.Count; t++)
        {
            var team = gm.Teams[t];
            w.U8((byte)team.Worms.Count);
            for (int i = 0; i < team.Worms.Count; i++)
            {
                var worm = team.Worms[i];
                bool alive = worm != null && !worm.IsDead;
                w.Bool(alive);
                if (!alive) continue;
                w.Vec(worm.transform.position);
                w.U8((byte)Mathf.Clamp(Mathf.RoundToInt(worm.Health), 0, 255));
                w.Angle(worm.AimAngle);
                w.I8((sbyte)worm.Facing);
            }

            // Боезапас: сколько чего осталось у команды.
            w.U8((byte)team.Ammo.Count);
            foreach (var kv in team.Ammo)
            {
                w.U8((byte)kv.Key);
                w.I16((short)Mathf.Clamp(kv.Value, -1, short.MaxValue));
            }
        }

        if (peer < 0) _net.SendAll(ref w, NetChannel.Reliable);
        else _net.SendTo(peer, ref w, NetChannel.Reliable);
    }

    void ApplySnapshot(ref NetReader r)
    {
        var gm = Gm;
        if (gm == null) return;

        float water = r.Coord();
        float wind = r.Unit();
        int teams = r.U8();

        NetSim.Applying = true;
        DestructibleTerrain.SetWaterLevel(water);
        gm.Wind = wind;

        for (int t = 0; t < teams; t++)
        {
            int count = r.U8();
            for (int i = 0; i < count; i++)
            {
                bool alive = r.Bool();
                Worm worm = t < gm.Teams.Count && i < gm.Teams[t].Worms.Count ? gm.Teams[t].Worms[i] : null;

                if (!alive)
                {
                    // Хост считает червя мёртвым, а у нас он ещё жив: добиваем
                    // ровно тем же путём, что и обычная смерть, — с прощанием,
                    // памятником и звуком.
                    if (worm != null && !worm.IsDead) worm.TakeDamage(worm.Health + 1f);
                    continue;
                }

                var pos = r.Vec();
                float health = r.U8();
                float aim = r.Angle();
                int facing = r.I8();
                if (worm == null || worm.IsDead) continue;

                // Тянем к хостовой точке, а не телепортируем: за ход
                // расхождение набегает на сантиметры, и рывок картинки был бы
                // заметнее самой ошибки. Далеко разъехались — ставим на место.
                float gap = Vector2.Distance(worm.transform.position, pos);
                if (gap > 1.5f) worm.PlaceAt(pos);
                else if (gap > 0.02f)
                    worm.transform.position = Vector2.Lerp(worm.transform.position, pos, 0.5f);

                worm.Health = health;
                worm.AimAngle = aim;
                worm.Facing = facing >= 0 ? 1 : -1;
            }

            int ammoCount = r.U8();
            for (int a = 0; a < ammoCount; a++)
            {
                var kind = (WeaponKind)r.U8();
                int left = r.I16();
                if (t < gm.Teams.Count) gm.Teams[t].Ammo[kind] = left;
            }
        }
        NetSim.Applying = false;
    }

    /// Всё, что нужно вошедшему посреди боя: ландшафт, состояние и ход.
    /// Порядок именно такой: сначала карта, потом черви на ней, иначе
    /// снапшот поставил бы червя на землю, которой у него ещё нет.
    public void SendFullState(int peer)
    {
        SendTerrainLog(peer);
        SendSnapshot(peer);
        SendTurn(peer);
    }

    /// Догнать карту вернувшегося: весь журнал с начала матча, ему одному.
    /// Шлём с начала, а не с момента ухода, — какой она была, когда он
    /// отвалился, хост не знает, а повтор безвреден: воронка, коридор и
    /// балка ложатся на то же место тем же результатом, сколько их ни
    /// применяй.
    void SendTerrainLog(int peer)
    {
        for (int i = 0; i < _ops.Count; i++)
        {
            var w = new NetWriter(24);
            WriteTerrainOp(ref w, _ops[i]);
            _net.SendTo(peer, ref w, NetChannel.Reliable);
        }
    }

    // --- разрушения --------------------------------------------------------

    /// Хост записывает каждое изменение ландшафта и рассылает его надёжным
    /// каналом. У клиента своих изменений нет вовсе, поэтому карта у него не
    /// «почти такая же», а ровно такая же — до пикселя.
    public void SendTerrainOp(TerrainOp op)
    {
        _ops.Add(op);
        var w = new NetWriter(24);
        WriteTerrainOp(ref w, op);
        _net.SendAll(ref w, NetChannel.Reliable);
    }

    static void WriteTerrainOp(ref NetWriter w, TerrainOp op)
    {
        w.U8((byte)NetMsg.Event);
        w.U8((byte)op.Kind);
        w.Vec(op.A);
        w.Vec(op.B);
        w.Coord(op.R);
        w.Angle(op.Angle);
        w.U8(op.C.r); w.U8(op.C.g); w.U8(op.C.b);
    }

    void ApplyEvent(ref NetReader r)
    {
        var kind = (NetEvent)r.U8();
        var a = r.Vec();
        var b = r.Vec();
        float radius = r.Coord();
        float angle = r.Angle();
        var color = new Color32(r.U8(), r.U8(), r.U8(), 255);
        if (!r.Ok) return;

        var terrain = Gm != null ? Gm.Terrain : null;

        NetSim.Applying = true;
        switch (kind)
        {
            case NetEvent.Blast:
                if (terrain != null) terrain.Explode(a, radius);
                break;
            case NetEvent.Dig:
                if (terrain != null) terrain.Dig(a, b, radius);
                break;
            case NetEvent.Beam:
                if (terrain != null) terrain.StampBeam(a, angle, b.x, b.y, color);
                break;
            case NetEvent.CrateDrop:
                Crate.Drop((CrateKind)Mathf.RoundToInt(b.x), a.x);
                break;
        }
        NetSim.Applying = false;
    }

    /// Хост объявляет о сброшенном ящике: сам сброс — бросок случайных чисел,
    /// и повторить его у клиента нечем.
    public void SendCrateDrop(CrateKind kind, float x)
    {
        if (!_net.IsHost) return;
        var w = new NetWriter(24);
        w.U8((byte)NetMsg.Event);
        w.U8((byte)NetEvent.CrateDrop);
        w.Vec(new Vector2(x, 0f));
        w.Vec(new Vector2((int)kind, 0f));
        w.Coord(0f);
        w.Angle(0f);
        w.U8(0); w.U8(0); w.U8(0);
        _net.SendAll(ref w, NetChannel.Reliable);
    }

    // --- приём -------------------------------------------------------------

    public void Receive(int peer, NetMsg msg, ref NetReader r)
    {
        switch (msg)
        {
            case NetMsg.Input: OnInput(peer, ref r); break;
            case NetMsg.Turn: if (!_net.IsHost) ApplyTurn(ref r); break;
            case NetMsg.Snapshot: if (!_net.IsHost) ApplySnapshot(ref r); break;
            case NetMsg.Event: if (!_net.IsHost) ApplyEvent(ref r); break;
        }
    }

    void OnInput(int peer, ref NetReader r)
    {
        int team = r.U8();
        var gm = Gm;
        if (gm == null) return;

        if (_net.IsHost)
        {
            // Чужой ход присылать нельзя: команда должна быть за этим
            // участником, и ход должен быть её. Иначе пакет — либо опоздавший
            // хвост прошлого хода, либо чья-то самодеятельность.
            var owner = _net.Find(peer);
            if (owner == null || owner.Team != team) return;
            if (gm.CurrentTeam != team) return;
        }
        else
        {
            // Свой ввод к нам возвращаться не должен, но если вернулся —
            // не даём ему перебить то, что мы уже показали.
            if (team == _net.LocalTeam) return;
        }

        InputFor(team).Read(ref r);
    }
}
