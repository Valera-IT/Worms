using System.Collections.Generic;
using UnityEngine;

/// Итог сыгранного матча: победитель и построчная статистика по командам.
/// GameManager собирает это в момент конца игры, экран итогов показывает.
public class MatchResults
{
    public int WinnerTeam = -1;   // -1 — ничья
    public string Title;
    public readonly List<Row> Teams = new List<Row>();

    public struct Row
    {
        public string Name;
        public Color Color;
        public int WormsAlive;
        public int WormsTotal;
        public float DamageDealt;
        public bool IsWinner;
    }
}
