using UnityEngine;

/// Подвесные мосты через проливы и пропасти. Ставятся на архипелаге и в
/// каньоне — там, где карта разрезана на куски, между которыми ногами не
/// пройти: черви весь матч сидели каждый на своём островке и перестреливались
/// с одного места, а бот, которому некуда шагнуть, и вовсе простаивал ход.
///
/// Мост — часть ландшафта, а не спрайт поверх него: настил впечатан в маску
/// той же кистью, что дерево и балка, поэтому по нему ходят, он ловит снаряд
/// и разлетается воронкой. Разница с деревом одна и важная: настил — пол
/// (DestructibleTerrain.StampDeck), а не крона, иначе ни червь, ни расчёт
/// дороги у бота его бы не увидели.
///
/// Канаты и стойки породой не становятся вовсе (StampRope): коллайдер их не
/// видит, сквозь перила проходят и прыгают. Взрыв их всё равно рвёт — иначе
/// над пропастью висели бы перила от снесённого моста.
///
/// Концы моста ищутся не на самой кромке обрыва, а вглубь берега: у островков
/// архипелага кромка едва торчит из воды, и мост, положенный от кромки к
/// кромке, залило бы первым же подъёмом потопа. Отойдя на несколько юнитов,
/// мост встаёт на твёрдое и высокое, а низкий язык пляжа пролетает над водой.
public static class Bridges
{
    /// Толщина настила в пикселях. Меньше восьми брать нельзя: коллайдер
    /// выбирает маску через каждые четыре пикселя, и тонкая доска для физики
    /// то есть, то нет — червь проваливается сквозь собственный мост.
    const int DeckThick = 8;

    /// Ширина доски и шаг подвесов в пикселях.
    const int PlankWidth = 7;
    const int HangerStep = 11;

    /// Высота перил над настилом и заход настила за концевую опору, в пикселях.
    const int RailUp = 19;
    const int Anchor = 5;

    /// Границы пролива в юнитах. Уже — это трещина, её перешагивают; шире —
    /// мост читается как вторая земля и съедает половину карты.
    public const float MinGap = 0.9f;
    public const float MaxGap = 30f;

    /// Предельная длина самого моста: концы уходят вглубь берега, и пролёт
    /// получается длиннее пролива.
    const float MaxSpan = 40f;

    /// Насколько берег должен возвышаться над водой, чтобы считаться берегом.
    const float LandOverWater = 1.0f;

    /// На сколько юнитов вглубь берега разрешено уходить в поисках опоры.
    const float MaxInland = 11f;

    /// Настил не должен идти над самой водой: потоп прибывает каждый ход, и
    /// мост, залитый на втором же круге, не стоит ни пикселя.
    const float DeckOverWater = 1.8f;

    /// Предельный уклон настила. Круче — это лестница, червь по ней съедет.
    const float MaxSlope = 0.5f;

    static readonly Color32 PlankA = new Color32(154, 110, 66, 255);
    static readonly Color32 PlankB = new Color32(126, 88, 52, 255);
    static readonly Color32 PlankSeam = new Color32(74, 50, 30, 255);
    static readonly Color32 Rope = new Color32(206, 182, 132, 255);
    static readonly Color32 RopeDark = new Color32(150, 126, 86, 255);

    /// Навести мосты. Возвращает, сколько встало, — этим числом пользуется
    /// аудит рельефа.
    public static int Build(DestructibleTerrain terrain, int seed)
    {
        if (terrain == null) return 0;
        var kind = terrain.Style != null ? terrain.Style.Kind : TerrainKind.Island;
        if (kind != TerrainKind.Archipelago && kind != TerrainKind.Canyon) return 0;

        int ppu = DestructibleTerrain.PixelsPerUnit;
        float water = DestructibleTerrain.WaterLevel;
        int w = terrain.W;

        // Профиль берега по колонкам: высота поверхности и есть ли она вообще.
        var height = new float[w];
        var land = new bool[w];
        for (int x = 0; x < w; x++)
        {
            height[x] = terrain.SurfaceHeightWorld(x / (float)ppu);
            land[x] = height[x] > water + LandOverWater;
        }

        int built = 0;
        int rim = -1;
        for (int x = 0; x < w; x++)
        {
            if (!land[x]) continue;

            float gap = (x - rim) / (float)ppu;
            if (rim >= 0 && gap >= MinGap && gap <= MaxGap && Span(terrain, height, land, rim, x, water))
                built++;
            rim = x;
        }
        return built;
    }

    /// Один мост через пролив между колонками rim и next. false — не встал:
    /// берега слишком низкие, слишком крутые или слишком далеко друг от друга.
    static bool Span(DestructibleTerrain terrain, float[] height, bool[] land,
                     int rim, int next, float water)
    {
        int ppu = DestructibleTerrain.PixelsPerUnit;

        // Опора должна стоять выше воды с запасом на провис и на потоп.
        float need = water + DeckOverWater + 0.4f;
        int ax = Foot(height, land, rim, -1, need);
        int bx = Foot(height, land, next, +1, need);
        if (ax < 0 || bx < 0) return false;

        // Уклон выравниваем, оттягивая низкий конец дальше вглубь берега:
        // пролёт становится длиннее, а подъём — тот же, и мост из лестницы
        // превращается в мост.
        while (true)
        {
            float span = (bx - ax) / (float)ppu;
            if (span > MaxSpan) return false;
            if (Mathf.Abs(height[bx] - height[ax]) <= span * MaxSlope) break;

            bool pullLeft = height[ax] < height[bx];
            int at = pullLeft ? ax - 1 : bx + 1;
            if (at < 0 || at >= land.Length || !land[at]) return false;
            if (pullLeft) ax = at; else bx = at;
        }

        // Опоры проверяем уже подтянутыми: пока уклон выравнивался, конец мог
        // выехать со дна пропасти на плато, и наоборот.
        if (!Exposed(height, ax) || !Exposed(height, bx)) return false;

        float ay = height[ax], by = height[bx];
        float length = (bx - ax) / (float)ppu;

        // Провис — двадцатая часть пролёта, но не больше юнита с четвертью:
        // с ним мост читается верёвочным, а не доской, положенной поперёк.
        float sag = Mathf.Clamp(length * 0.05f, 0.15f, 1.25f);
        if (Mathf.Min(ay, by) - sag < water + DeckOverWater) return false;

        int y0 = terrain.WorldToPixelY(ay);
        int y1 = terrain.WorldToPixelY(by);
        float sagPx = sag * ppu;
        int spanPx = bx - ax;

        // Мост стоит под открытым небом. Без этой проверки он вставал и в
        // глубине каньона: дно пропасти тоже «берег», и между двумя такими
        // днищами вырастал настил, замурованный в породу этажом ниже карты.
        if (!OpenSky(terrain, ax, bx, y0, y1, sagPx)) return false;

        for (int x = ax - Anchor; x <= bx + Anchor; x++)
        {
            float t = Mathf.Clamp01((x - ax) / (float)spanPx);
            int deck = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t) - sagPx * 4f * t * (1f - t));

            // Под холмом настила не видно, а доски внутри породы читались бы
            // как ошибка: там, где берег и так выше моста, доску не кладём.
            if (x >= 0 && x < height.Length && height[x] > deck / (float)ppu + 0.8f) continue;

            // Настил: доски вперемешку, между ними тёмный шов, снизу прогон.
            int step = x - ax;
            bool light = (Mathf.FloorToInt(step / (float)PlankWidth) & 1) == 0;
            var plank = light ? PlankA : PlankB;
            bool seam = ((step % PlankWidth) + PlankWidth) % PlankWidth == 0;

            for (int d = 0; d < DeckThick; d++)
                terrain.StampDeck(x, deck - d, seam || d == DeckThick - 1 ? PlankSeam : plank);

            // Перила и подвесы — только над пролётом: над берегом они бы
            // висели в воздухе рядом с землёй.
            if (x < ax || x > bx) continue;

            int rail = deck + RailUp;
            terrain.StampRope(x, rail, Rope);
            terrain.StampRope(x, rail - 1, RopeDark);

            bool post = x <= ax + 1 || x >= bx - 1;
            if (post)
                for (int y = deck + 1; y <= rail + 3; y++) terrain.StampRope(x, y, RopeDark);
            else if (((step % HangerStep) + HangerStep) % HangerStep == 0)
                for (int y = deck + 2; y < rail - 1; y++) terrain.StampRope(x, y, RopeDark);
        }

        return true;
    }

    /// Стоит ли опора на виду, а не на дне щели. Дно пропасти в каньоне —
    /// такая же «земля» выше воды, как и плато, и по нему тоже находились
    /// проливы: мост вырастал двадцатью юнитами ниже карты, между двумя
    /// днищами, и был виден только как доска, торчащая из породы.
    static bool Exposed(float[] height, int x) => Top(height, x) - height[x] <= 9f;

    static float Top(float[] height, int x)
    {
        int reach = Mathf.RoundToInt(3f * DestructibleTerrain.PixelsPerUnit);
        float top = height[x];
        for (int i = Mathf.Max(0, x - reach); i <= Mathf.Min(height.Length - 1, x + reach); i++)
            if (height[i] > top) top = height[i];
        return top;
    }

    /// Свободно ли над пролётом: над настилом должно быть небо — не свод
    /// пещеры и не толща плато. Пробуем каждую четвёртую колонку и терпим
    /// четверть перекрытых: концы моста уходят вглубь берега и там честно
    /// оказываются под землёй.
    static bool OpenSky(DestructibleTerrain terrain, int ax, int bx, int y0, int y1, float sagPx)
    {
        int spanPx = bx - ax;
        int checks = 0, blocked = 0;

        for (int x = ax; x <= bx; x += 4)
        {
            float t = Mathf.Clamp01((x - ax) / (float)spanPx);
            int deck = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t) - sagPx * 4f * t * (1f - t));

            checks++;
            for (int y = deck + 2; y < terrain.H; y++)
                if (terrain.IsSolidPixel(x, y)) { blocked++; break; }
        }

        return checks > 0 && blocked * 4 <= checks;
    }

    /// Опора моста: идём от кромки вглубь берега, пока земля не поднимется до
    /// нужной высоты. −1 — берег так и остался низким, мост здесь не нужен:
    /// такой островок всё равно уйдёт под воду.
    static int Foot(float[] height, bool[] land, int from, int dir, float need)
    {
        int limit = Mathf.RoundToInt(MaxInland * DestructibleTerrain.PixelsPerUnit);
        for (int i = 0; i <= limit; i++)
        {
            int x = from + dir * i;
            if (x < 0 || x >= land.Length || !land[x]) return -1;
            if (height[x] >= need) return x;
        }
        return -1;
    }
}
