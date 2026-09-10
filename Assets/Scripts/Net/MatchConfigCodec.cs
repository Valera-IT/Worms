using UnityEngine;

/// Настройки матча по проводу. Отдельным файлом, а не методами в MatchConfig:
/// конфигурация — это про игру, а её укладка в байты — про сеть, и версия
/// протокола меняется по своим причинам.
///
/// Цвет команды едет тремя байтами, а не тремя float: палитра всё равно
/// подобрана вручную и полутонов в ней нет.
public static class MatchConfigCodec
{
    public static void Write(ref NetWriter w, MatchConfig c)
    {
        w.U8((byte)c.WormsPerTeam);
        w.U8((byte)Mathf.Clamp(Mathf.RoundToInt(c.TurnTime), 0, 255));
        w.U8((byte)c.Difficulty);
        w.U8((byte)c.Terrain);
        w.U8((byte)c.Ammo);
        w.U8((byte)c.Crates);
        w.U8((byte)c.Scatter);
        w.U8((byte)Mathf.Clamp(c.FloodRound, 0, 255));

        w.U8((byte)c.Teams.Count);
        for (int i = 0; i < c.Teams.Count; i++)
        {
            var t = c.Teams[i];
            w.Str(t.Name);
            w.U8((byte)(t.Color.r * 255f));
            w.U8((byte)(t.Color.g * 255f));
            w.U8((byte)(t.Color.b * 255f));
            w.Bool(t.IsBot);
            w.U8((byte)t.VoiceBank);
        }
    }

    public static MatchConfig Read(ref NetReader r)
    {
        var c = new MatchConfig
        {
            WormsPerTeam = r.U8(),
            TurnTime = r.U8(),
            Difficulty = (BotDifficulty)r.U8(),
            Terrain = (TerrainKind)r.U8(),
            Ammo = (AmmoPlan)r.U8(),
            Crates = (CratePlan)r.U8(),
            Scatter = (ScatterPlan)r.U8(),
            FloodRound = r.U8()
        };

        c.Teams.Clear();
        int n = r.U8();
        for (int i = 0; i < n; i++)
        {
            string name = r.Str();
            float cr = r.U8() / 255f;
            float cg = r.U8() / 255f;
            float cb = r.U8() / 255f;
            bool bot = r.Bool();
            int voice = r.U8();
            c.Teams.Add(new TeamSetup { Name = name, Color = new Color(cr, cg, cb), IsBot = bot, VoiceBank = voice });
        }
        return c;
    }
}
