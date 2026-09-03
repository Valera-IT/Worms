using System.Collections.Generic;
using UnityEngine;

/// Сложность бота: чем ниже, тем крупнее разброс по углу и силе и тем
/// грубее перебор. BotPlanner читает это поле напрямую.
public enum BotDifficulty { Easy, Normal, Hard }

/// Тип мира. Форму строит TerrainShape, палитру и воду — TerrainStyle.
/// Random раскрывается из сида матча, поэтому «новая карта» меняет и тип.
public enum TerrainKind { Island, Cave, Archipelago, Canyon, Snow, Random }

/// Как укомплектован боезапас команды поверх базового набора Weapon.All.
public enum AmmoPlan { Standard, Generous, Unlimited }

/// Как часто с неба падают ящики с припасами.
public enum CratePlan { Off, Rare, Normal, Plenty }

/// Одна команда в конфигурации матча.
public class TeamSetup
{
    public string Name;
    public Color Color;
    public bool IsBot;   // командой правит BotInput, а не человек за устройством
}

/// Всё, что раньше было константами и захардкоженными списками в GameManager:
/// число и состав команд, время хода, червей в команде, тип мира, боезапас.
/// Меню собирает этот объект, GameManager строит по нему матч.
public class MatchConfig
{
    public int WormsPerTeam = 4;
    public float TurnTime = 15f;
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public TerrainKind Terrain = TerrainKind.Random;
    public AmmoPlan Ammo = AmmoPlan.Standard;
    public CratePlan Crates = CratePlan.Normal;

    /// С какого раунда начинается потоп: вода прибывает каждый ход и черви
    /// внизу тонут. 0 — выключено. Раньше здесь была внезапная смерть, которая
    /// срезала всем здоровье до единицы, — вода делает то же самое честнее.
    public int FloodRound = 1;

    public List<TeamSetup> Teams = new List<TeamSetup>();

    public int TeamCount => Teams.Count;

    public static readonly Color[] Palette =
    {
        new Color(0.35f, 0.65f, 1.0f),
        new Color(1.0f, 0.45f, 0.4f),
        new Color(0.6f, 0.9f, 0.4f),
        new Color(0.95f, 0.8f, 0.35f)
    };


    /// Боезапас для оружия с учётом плана. Бесконечный (-1) остаётся бесконечным.
    public int AmmoFor(Weapon w)
    {
        if (w.Ammo < 0) return -1;
        return Ammo switch
        {
            AmmoPlan.Unlimited => -1,
            AmmoPlan.Generous => w.Ammo * 2,
            _ => w.Ammo
        };
    }

    /// Сколько команд под ботом.
    public int BotCount
    {
        get
        {
            int n = 0;
            foreach (var t in Teams) if (t.IsBot) n++;
            return n;
        }
    }

    /// Ботами делаем последние n команд. Ботов может быть столько же, сколько
    /// команд: тогда за устройством не остаётся никого и матч играется сам.
    public void SetBotCount(int n)
    {
        n = Mathf.Clamp(n, 0, Teams.Count);
        for (int i = 0; i < Teams.Count; i++) Teams[i].IsBot = i >= Teams.Count - n;
    }

    /// Вероятность сбросить ящик в начале хода.
    public float CrateChance => Crates switch
    {
        CratePlan.Off => 0f,
        CratePlan.Rare => 0.18f,
        CratePlan.Plenty => 0.7f,
        _ => 0.38f
    };

    /// Приводит число команд к n (2..4), сохраняя уже настроенные и добавляя
    /// стандартные. Флаг бота у добавленных выставляет вызывающий.
    public void SetTeamCount(int n)
    {
        n = Mathf.Clamp(n, 2, 4);
        // Число ботов держим прежним, пока команд хватает: иначе добавленная
        // пятой строкой команда молча отдавала бы ход человеку.
        int bots = Mathf.Min(BotCount, n);
        while (Teams.Count > n) Teams.RemoveAt(Teams.Count - 1);
        // Названия команд случайные («Старики», «Гопники»), как в оригинале:
        // раньше это были цвета, и все матчи выглядели одинаково.
        while (Teams.Count < n)
        {
            int i = Teams.Count;
            Teams.Add(new TeamSetup { Name = FreshTeamName(), Color = Palette[i], IsBot = false });
        }
        SetBotCount(bots);
    }

    /// Название, которого ещё нет среди команд: с шестнадцатью вариантами на
    /// четыре команды десятка попыток хватает с запасом.
    string FreshTeamName()
    {
        for (int attempt = 0; attempt < 24; attempt++)
        {
            string candidate = Names.RandomTeam();
            bool taken = false;
            foreach (var t in Teams) if (t.Name == candidate) { taken = true; break; }
            if (!taken) return candidate;
        }
        return Names.RandomTeam();
    }

    public MatchConfig Clone()
    {
        var c = new MatchConfig
        {
            WormsPerTeam = WormsPerTeam,
            TurnTime = TurnTime,
            Difficulty = Difficulty,
            Terrain = Terrain,
            Ammo = Ammo,
            Crates = Crates,
            FloodRound = FloodRound,
            Teams = new List<TeamSetup>()
        };
        foreach (var t in Teams)
            c.Teams.Add(new TeamSetup { Name = t.Name, Color = t.Color, IsBot = t.IsBot });
        return c;
    }

    /// Хотсит: две живые команды за одним устройством. Играется с фазы 1.
    public static MatchConfig Hotseat()
    {
        var c = new MatchConfig();
        c.SetTeamCount(2);
        return c;
    }

    /// Пресет экрана «Настройка матча»: две команды, обе под ботом (человек
    /// убавляет число ботов сам), ход 15 с, случайная карта, потоп с 1 раунда —
    /// это уже даёт поля MatchConfig, здесь остаётся только выставить ботов.
    public static MatchConfig Setup()
    {
        var c = Hotseat();
        c.SetBotCount(2);
        return c;
    }

    /// Быстрый бой против бота: игрок против одной команды под BotInput,
    /// на случайной карте, ход 20 с, потоп с первого раунда.
    public static MatchConfig QuickBot()
    {
        var c = Hotseat();
        c.Teams[1].IsBot = true;
        c.TurnTime = 20f;
        c.Terrain = TerrainKind.Random;
        c.FloodRound = 1;
        return c;
    }

    /// «Начать бой» с титульного экрана: сразу 1 на 1 против бота на случайной
    /// карте, ход 30 секунд, потоп с первого раунда. Между кнопкой и боем
    /// никаких экранов — все прочие настройки живут в «Настройке матча».
    public static MatchConfig QuickFight()
    {
        var c = Hotseat();          // две команды, одна из них — бот ниже
        c.Teams[1].IsBot = true;    // ботов 1
        c.TurnTime = 30f;
        c.Terrain = TerrainKind.Random;
        c.FloodRound = 1;
        return c;
    }
}
