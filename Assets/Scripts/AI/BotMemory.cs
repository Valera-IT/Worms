using System.Collections.Generic;
using UnityEngine;

/// Память бота между ходами. Планировщик чистый: он считает баллистику с нуля
/// и не помнит ничего. Из-за этого бот, промазавший из-за ветра или дерева,
/// которого нет в маске у самой кромки, на следующем ходу с того же места
/// повторял тот же выстрел — и мазал точно так же. Человек так не играет.
///
/// Здесь лежит короткий журнал выстрелов команды: откуда, чем, под каким углом
/// и с какой силой стреляли, и чем это кончилось. Промахом считается воронка,
/// легшая дальше MissRange от ближайшего врага. Повтор промаха планировщик
/// штрафует — и берёт следующий по счёту вариант.
///
/// Журнал живёт ровно один матч: GameManager чистит его на сборке мира.
public static class BotMemory
{
    /// Дальше этого от ближайшего врага воронка считается промахом.
    const float MissRange = 3.5f;
    /// Насколько похожими должны быть два выстрела, чтобы считаться одним.
    const float SameSpot = 2.0f;    // юнитов между точками стрельбы
    const float SameAngle = 4.0f;   // градусов
    const float SameCharge = 0.06f; // доля набора силы
    /// Штраф за повтор промаха: он должен перевесить премию за близость
    /// воронки, но не запрещать выстрел, когда лучше и правда ничего нет.
    const float MissPenalty = 22f;
    /// Больше этого не храним: старые выстрелы делались по другой карте —
    /// ландшафт с тех пор перекопан, а черви разошлись.
    const int Capacity = 24;

    struct Record
    {
        public Team Team;
        public int Weapon;
        public float Angle, Charge;
        public Vector2 From;
        public float Miss;      // сколько юнитов от ближайшего врага легло
        public bool Resolved;   // взрыв уже случился и записан
    }

    static readonly List<Record> _log = new List<Record>();
    /// Индекс выстрела, ждущего своего взрыва: следующий взрыв — его.
    static int _pending = -1;

    public static void Clear()
    {
        _log.Clear();
        _pending = -1;
    }

    /// Бот нажал на спуск. Итог ещё неизвестен — его принесёт первый же взрыв.
    public static void NoteShot(Team team, BotShot shot, Vector2 from)
    {
        if (team == null || !shot.Found) return;

        if (_log.Count >= Capacity) _log.RemoveAt(0);
        _log.Add(new Record
        {
            Team = team, Weapon = shot.Weapon, Angle = shot.Angle, Charge = shot.Charge,
            From = from, Miss = 0f, Resolved = false
        });
        _pending = _log.Count - 1;
    }

    /// Взрыв на карте. Первый после выстрела считается его: воронку от чужого
    /// хода мы всё равно не увидим — ход к тому времени уже наш соседский.
    public static void NoteBlast(Vector2 pos)
    {
        if (_pending < 0 || _pending >= _log.Count) return;

        int idx = _pending;
        _pending = -1;
        var r = _log[idx];
        if (r.Resolved) return;

        float miss = float.MaxValue;
        var worms = GameManager.I != null ? GameManager.I.AllWorms() : null;
        if (worms != null)
            for (int i = 0; i < worms.Count; i++)
            {
                var o = worms[i];
                if (o == null || o.IsDead || o.Team == r.Team) continue;
                miss = Mathf.Min(miss, Vector2.Distance(o.transform.position, pos));
            }

        r.Miss = miss == float.MaxValue ? 0f : miss;
        r.Resolved = true;
        _log[idx] = r;
    }

    /// Сколько снять с оценки за то, что этим уже мазали отсюда же.
    /// Ноль — так ещё не стреляли или стреляли удачно.
    public static float Penalty(Team team, int weapon, float angle, float charge, Vector2 from)
    {
        for (int i = _log.Count - 1; i >= 0; i--)
        {
            var r = _log[i];
            if (!r.Resolved || r.Team != team || r.Weapon != weapon) continue;
            if (r.Miss < MissRange) continue;
            if (Mathf.Abs(Mathf.DeltaAngle(r.Angle, angle)) > SameAngle) continue;
            if (Mathf.Abs(r.Charge - charge) > SameCharge) continue;
            if (Vector2.Distance(r.From, from) > SameSpot) continue;
            return MissPenalty;
        }
        return 0f;
    }

    /// Сколько промахов запомнено — для тестов и отладки.
    public static int Misses
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _log.Count; i++)
                if (_log[i].Resolved && _log[i].Miss >= MissRange) n++;
            return n;
        }
    }
}
