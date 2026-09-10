using System.Collections.Generic;
using UnityEngine;

public class Team
{
    public string Name;
    public Color Color;
    public List<Worm> Worms = new List<Worm>();
    public int ActiveIndex;
    public Dictionary<WeaponKind, int> Ammo = new Dictionary<WeaponKind, int>();

    /// Банк голоса команды: им говорят все её черви. Приходит из TeamSetup.
    public int VoiceBank;

    /// Вид памятника, который остаётся от погибших червей этой команды:
    /// один из десяти, у каждой команды свой. Раздаёт GameManager при сборке мира.
    public int GraveKind;

    /// Суммарный урон, нанесённый этой командой за матч — для экрана итогов.
    public float DamageDealt;

    /// Кто ведёт команду. null — живой игрок за этим устройством,
    /// иначе своя реализация ввода: бот с фазы 8, запись повтора и так далее.
    public IGameInput Controller;

    /// Командой правит бот, а не человек за устройством.
    public bool IsBot => Controller is BotInput;

    public bool Alive
    {
        get
        {
            for (int i = 0; i < Worms.Count; i++)
                if (Worms[i] != null && !Worms[i].IsDead) return true;
            return false;
        }
    }

    /// Сколько червей команды ещё живы. Нужно выбору червя: переключать
    /// некого, пока в команде остался один.
    public int AliveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Worms.Count; i++)
                if (Worms[i] != null && !Worms[i].IsDead) n++;
            return n;
        }
    }

    public float TotalHealth
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < Worms.Count; i++)
                if (Worms[i] != null && !Worms[i].IsDead) sum += Worms[i].Health;
            return sum;
        }
    }

    public Worm NextAliveWorm()
    {
        for (int step = 1; step <= Worms.Count; step++)
        {
            int idx = (ActiveIndex + step) % Worms.Count;
            if (Worms[idx] != null && !Worms[idx].IsDead)
            {
                ActiveIndex = idx;
                return Worms[idx];
            }
        }
        return null;
    }
}
