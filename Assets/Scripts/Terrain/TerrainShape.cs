using UnityEngine;

/// Пять генераторов формы мира. Каждый пишет булеву маску породы; траву,
/// покраску и коллайдеры делает DestructibleTerrain — форма про них не знает.
/// Сид один на весь мир: он же задаёт тип, если в конфиге выбран «Случайный».
public static class TerrainShape
{
    public static void Fill(bool[] solid, int w, int h, TerrainStyle style, int seed)
    {
        var rnd = new System.Random(seed);
        float waterN = Mathf.Clamp01(style.WaterLevel / DestructibleTerrain.WorldHeight);

        switch (style.Kind)
        {
            case TerrainKind.Cave: Cave(solid, w, h, rnd); break;
            case TerrainKind.Archipelago: Archipelago(solid, w, h, rnd, waterN); break;
            case TerrainKind.Canyon: Canyon(solid, w, h, rnd, waterN); break;
            case TerrainKind.Snow: Snow(solid, w, h, rnd, waterN); break;
            default: Island(solid, w, h, rnd, waterN); break;
        }

        // Последнее слово: всё, что не доходит до воды (в пещере — до пола
        // или кровли), из мира выбрасывается. Плоский кусок земли, висящий
        // в небе ни на чём, читается как ошибка генератора, а не как рельеф,
        // и ни один ярус ниже такой опоры себе больше не позволяет.
        Cleanup(solid, w, h, waterN,
                style.Kind == TerrainKind.Island ? 2200 : 900,
                style.Kind == TerrainKind.Cave);
    }

    // ---------- общие кирпичи ----------

    static float Off(System.Random rnd) => (float)rnd.NextDouble() * 1000f;

    /// Гребневый шум: складка вместо купола. Даёт острые вершины и крутые
    /// бока — силуэт карт оригинала держится на них, а не на холмах.
    static float Ridge(float x, float y) => 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x, y) - 1f);
    static float Rng(System.Random rnd, float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    /// Мягкий заход краёв карты в воду — общий для острова, каньона и холмов.
    static float EdgeFade(float nx, float margin)
        => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nx / margin))
         * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - nx) / margin));

    static void FillColumns(bool[] solid, int w, int h, int[] surface)
    {
        for (int x = 0; x < w; x++)
        {
            int top = Mathf.Clamp(surface[x], -1, h - 1);
            for (int y = 0; y <= top; y++) solid[y * w + x] = true;
        }
    }

    // ---------- остров ----------

    /// Базовый мир: поверхность из трёх октав шума, края уходят в воду,
    /// в глубине — редкие полости.
    static void Island(bool[] solid, int w, int h, System.Random rnd, float waterN)
    {
        Blobs(solid, w, h, rnd, waterN);
        Warp(solid, w, h, rnd, 7f);
        Layers(solid, w, h, rnd, waterN, Rng(rnd, 0.85f, 1.15f));
    }

    /// Массивы суши как слипшиеся кляксы, а не как высотное поле. Поле
    /// «сколько я внутри ближайшей кляксы» плюс шум по обеим осям: граница
    /// получается волнистой в обе стороны, поэтому сами собой выходят бухты,
    /// козырьки и перемычки — то, чем карта оригинала и держится. Высотным
    /// полем такого не построить: у него на столбец ровно одна поверхность.
    static void Blobs(bool[] solid, int w, int h, System.Random rnd, float waterN)
    {
        int n = rnd.Next(12, 18);
        var cx = new float[n];
        var cy = new float[n];
        var rx = new float[n];
        var ry = new float[n];

        float waterY = waterN * h;
        float slot = 0.94f / n;

        for (int i = 0; i < n; i++)
        {
            // Кляксы идут цепочкой слева направо с разбросом: сплошной стены
            // не выходит, но и одинокими островками они не рассыпаются.
            cx[i] = (0.03f + slot * (i + 0.5f) + Rng(rnd, -0.55f, 0.55f) * slot) * w;

            // Каждая пятая клякса — горб: выше и уже соседей. Оригинал —
            // это одна сплошная гряда с горбами и провалами, а не частокол
            // столбов, поэтому башен мало и они тонут в общей массе.
            if (rnd.Next(5) == 0)
            {
                cy[i] = Rng(rnd, h * 0.16f, h * 0.26f);
                rx[i] = Rng(rnd, 0.055f, 0.090f) * w;
                ry[i] = Rng(rnd, 0.26f, 0.38f) * h;
            }
            else
            {
                cy[i] = Rng(rnd, waterY - h * 0.02f, h * 0.22f);
                rx[i] = Rng(rnd, 0.080f, 0.140f) * w;
                ry[i] = Rng(rnd, 0.17f, 0.28f) * h;
            }
        }

        float o1 = Off(rnd), o2 = Off(rnd), o3 = Off(rnd);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float best = 0f;
                for (int i = 0; i < n; i++)
                {
                    float dx = (x - cx[i]) / rx[i];
                    float dy = (y - cy[i]) / ry[i];
                    float v = 1f - (dx * dx + dy * dy);
                    if (v > best) best = v;
                }

                // Шум по обеим осям ведёт границу; крупная октава лепит бухты,
                // мелкая — рваную кромку.
                float noise = 0.50f * (Mathf.PerlinNoise(x * 0.0060f + o1, y * 0.0060f + o1) - 0.5f)
                            + 0.20f * (Mathf.PerlinNoise(x * 0.021f + o2, y * 0.021f + o2) - 0.5f)
                            + 0.08f * (Mathf.PerlinNoise(x * 0.06f + o3, y * 0.06f + o3) - 0.5f);

                // Ниже воды масса добирается — берег уходит в воду, а не висит.
                float sink = y < waterY ? (waterY - y) / (h * 0.10f) * 0.35f : 0f;

                // Порог ниже прежнего: кляксы слипаются в одну гряду, и по
                // ней можно пройти, а не перепрыгивать с колонны на колонну.
                if (best + noise * 2f + sink > 0.14f) solid[y * w + x] = true;
            }
        }
    }

    // ---------- пещера ----------

    /// Замкнутая коробка: пол, потолок со сталактитами и стены по периметру.
    /// Утонуть негде, зато снаряд бьётся о потолок — вся баллистика другая.
    static void Cave(bool[] solid, int w, int h, System.Random rnd)
    {
        float o1 = Off(rnd), o2 = Off(rnd), o3 = Off(rnd), o4 = Off(rnd);
        int wall = Mathf.RoundToInt(w * 0.045f);

        var floor = new int[w];
        var ceil = new int[w];

        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)w;

            float f = 0.15f
                    + 0.07f * Mathf.PerlinNoise(nx * 3.1f + o1, o1 * 0.11f)
                    + 0.03f * Mathf.PerlinNoise(nx * 9.5f + o2, o2 * 0.23f);
            floor[x] = Mathf.RoundToInt(f * h);

            // Потолок: ровная кровля минус сталактиты. Четвёртая степень шума
            // высокой частоты делает их редкими, узкими и острыми.
            float c = 0.82f - 0.04f * Mathf.PerlinNoise(nx * 2.7f + o3, o3 * 0.19f);
            float spike = Mathf.PerlinNoise(nx * 30f + o4, o4 * 0.29f);
            spike *= spike; spike *= spike;
            ceil[x] = Mathf.RoundToInt((c - spike * 0.42f) * h);
        }

        for (int x = 0; x < w; x++)
        {
            bool isWall = x < wall || x >= w - wall;
            int top = isWall ? h - 1 : floor[x];
            int bottom = isWall ? 0 : ceil[x];

            for (int y = 0; y <= top; y++) solid[y * w + x] = true;
            if (!isWall)
                for (int y = bottom; y < h; y++) solid[y * w + x] = true;
        }

        CaveLedges(solid, w, h, rnd, floor, ceil, wall);
    }

    /// Этажи пещеры: плиты между полом и кровлей, каждая — на колонне от
    /// пола или на ножке от кровли. Общий пост-проход тут не годится — в
    /// коробке над каждым столбцом уже лежит камень, и он не находит места.
    static void CaveLedges(bool[] solid, int w, int h, System.Random rnd, int[] floor, int[] ceil, int wall)
    {
        int clear = 28;   // столько же, сколько требует высадка
        int n = rnd.Next(5, 8);

        for (int i = 0; i < n; i++)
        {
            int half = Mathf.RoundToInt(w * Rng(rnd, 0.045f, 0.08f));
            int thick = Mathf.RoundToInt(h * Rng(rnd, 0.018f, 0.030f));
            int cx = Mathf.RoundToInt(Rng(rnd, 0.12f, 0.88f) * w);
            int lo = Mathf.Max(wall, cx - half), hi = Mathf.Min(w - wall - 1, cx + half);
            if (hi - lo < 8) continue;

            int under = 0, over = h;
            for (int x = lo; x <= hi; x++) { under = Mathf.Max(under, floor[x]); over = Mathf.Min(over, ceil[x]); }
            int y0 = under + clear, y1 = over - clear - thick;
            if (y1 <= y0) continue;

            int y = rnd.Next(y0, y1 + 1);
            float wobble = Off(rnd);
            for (int x = lo; x <= hi; x++)
            {
                float t = Mathf.Abs(x - cx) / (float)half;
                float taper = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, t));
                int th = Mathf.RoundToInt(thick * taper
                       + 2f * (Mathf.PerlinNoise(x * 0.06f + wobble, wobble) - 0.5f));
                for (int k = 0; k < th && y + k < h; k++) solid[(y + k) * w + x] = true;
            }

            // Плита без опоры — тот самый парящий кусок, поэтому опора есть
            // у каждой: либо колонна от пола, либо ножка от кровли. Заодно
            // пещера перестаёт быть пустой коробкой, а низ получает укрытия.
            int pw = Mathf.Max(2, Mathf.RoundToInt(w * 0.008f));
            bool fromFloor = rnd.Next(2) == 0;
            int px0 = cx + Mathf.RoundToInt(Rng(rnd, -0.45f, 0.45f) * half);
            for (int x = px0 - pw; x <= px0 + pw; x++)
            {
                if (x < 0 || x >= w) continue;
                int c = Mathf.Clamp(x, 0, w - 1);
                if (fromFloor) for (int py = floor[c]; py <= y; py++) solid[py * w + x] = true;
                else           for (int py = y; py <= ceil[c] && py < h; py++) solid[py * w + x] = true;
            }
        }
    }

    // ---------- архипелаг ----------

    /// Три-пять отдельных островков: между ними вода, и червь заперт на своём
    /// до верёвки и телепорта.
    static void Archipelago(bool[] solid, int w, int h, System.Random rnd, float waterN)
    {
        int n = rnd.Next(3, 6);
        float o1 = Off(rnd), o2 = Off(rnd);

        var cx = new float[n];
        var hw = new float[n];
        var amp = new float[n];
        float slot = 0.88f / n;
        for (int i = 0; i < n; i++)
        {
            cx[i] = 0.06f + slot * (i + 0.5f) + Rng(rnd, -0.02f, 0.02f);
            hw[i] = slot * Rng(rnd, 0.42f, 0.50f);
            amp[i] = Rng(rnd, 0.13f, 0.20f);
        }

        var surface = new int[w];
        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)w;
            float best = waterN - 0.07f;   // дно между островками — под водой

            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Abs(nx - cx[i]) / hw[i];
                if (t >= 1f) continue;
                // Плато с обрывистыми берегами: середина островка ровная,
                // иначе черви оказываются на макушке конуса.
                float bell = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, t));
                float f = waterN - 0.05f + bell * (amp[i]
                        + 0.06f * Mathf.PerlinNoise(nx * 9f + o1, o1 * 0.17f)
                        + 0.03f * Mathf.PerlinNoise(nx * 21f + o2, o2 * 0.37f));
                if (f > best) best = f;
            }

            surface[x] = Mathf.Clamp(Mathf.RoundToInt(best * h), 0, h - 1);
        }

        FillColumns(solid, w, h, surface);
        // Островки невысокие: полостей в них меньше, иначе островок
        // превращается в решето и стоять на нём негде.
        Layers(solid, w, h, rnd, waterN, Rng(rnd, 0.35f, 0.6f));
    }

    // ---------- каньон ----------

    /// Высокое плато, прорезанное 4–7 узкими пропастями до воды:
    /// стреляют сквозь щели, а врага легко сбросить вниз.
    static void Canyon(bool[] solid, int w, int h, System.Random rnd, float waterN)
    {
        float o1 = Off(rnd), o2 = Off(rnd), o3 = Off(rnd);
        var surface = new int[w];

        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)w;
            float f = 0.54f
                    + 0.10f * Mathf.PerlinNoise(nx * 2.2f + o1, o1 * 0.13f)
                    + 0.03f * Mathf.PerlinNoise(nx * 8f + o2, o2 * 0.29f);
            f = Mathf.Lerp(waterN - 0.06f, f, EdgeFade(nx, 0.08f));
            surface[x] = Mathf.Clamp(Mathf.RoundToInt(f * h), 0, h - 1);
        }

        FillColumns(solid, w, h, surface);

        int chasms = rnd.Next(4, 8);
        int floorY = Mathf.RoundToInt((waterN - 0.04f) * h);
        float span = 0.76f / chasms;

        for (int i = 0; i < chasms; i++)
        {
            float ncx = 0.12f + span * (i + 0.5f) + Rng(rnd, -0.25f, 0.25f) * span;
            int cxp = Mathf.RoundToInt(ncx * w);
            float half = Rng(rnd, 9f, 17f);            // пропасть узкая: 1,5–2,8 юнита
            float wobble = Off(rnd);
            int pad = Mathf.CeilToInt(half) + 3;

            for (int x = Mathf.Max(0, cxp - pad); x <= Mathf.Min(w - 1, cxp + pad); x++)
            {
                for (int y = Mathf.Max(0, floorY); y < h; y++)
                {
                    // Книзу щель сужается, стены слегка виляют.
                    float depth = Mathf.InverseLerp(floorY, surface[cxp], y);
                    float hw = half * (0.55f + 0.45f * depth)
                             + 3f * (Mathf.PerlinNoise(y * 0.03f + wobble, wobble * 0.7f) - 0.5f);
                    if (Mathf.Abs(x - cxp) <= hw) solid[y * w + x] = false;
                }
            }
        }

        // Плато толстое — полостей в нём больше, чем где-либо: есть где
        // им поместиться, а пропасти от этого только выигрывают, обзаводясь
        // выходами на середине стены.
        Warp(solid, w, h, rnd, 8f);
        Layers(solid, w, h, rnd, waterN, Rng(rnd, 1.0f, 1.3f));
    }

    // ---------- снежные холмы ----------

    /// Широкие плавные холмы и одна-две арки: укрытий мало, дуэли дальние.
    static void Snow(bool[] solid, int w, int h, System.Random rnd, float waterN)
    {
        float o1 = Off(rnd), o2 = Off(rnd), o3 = Off(rnd);
        var surface = new int[w];

        for (int x = 0; x < w; x++)
        {
            float nx = x / (float)w;
            float f = 0.36f
                    + 0.15f * Mathf.PerlinNoise(nx * 1.5f + o1, o1 * 0.11f)
                    + 0.06f * Mathf.PerlinNoise(nx * 3.3f + o2, o2 * 0.21f)
                    + 0.02f * Mathf.PerlinNoise(nx * 8f + o3, o3 * 0.31f);
            f = Mathf.Lerp(waterN - 0.05f, f, EdgeFade(nx, 0.12f));
            surface[x] = Mathf.Clamp(Mathf.RoundToInt(f * h), 0, h - 1);
        }

        int arches = rnd.Next(1, 3);
        var sites = new float[arches];
        for (int i = 0; i < arches; i++)
            sites[i] = arches == 1 ? Rng(rnd, 0.28f, 0.72f) : 0.22f + 0.5f * i + Rng(rnd, -0.05f, 0.05f);

        // Под аркой выкапываем ложбину: иначе это не проход, а пещера в холме.
        var span = new int[arches];
        var floorY = new int[arches];
        for (int i = 0; i < arches; i++)
        {
            int cxp = Mathf.RoundToInt(sites[i] * w);
            span[i] = Mathf.RoundToInt(w * Rng(rnd, 0.055f, 0.075f));
            floorY[i] = Mathf.RoundToInt((waterN + 0.06f) * h);

            // Ложбина шириной в пролёт, но с плавным подъёмом вдвое дальше:
            // иначе по бокам арки встают отвесные стены.
            int fade = span[i] * 2;
            for (int x = Mathf.Max(0, cxp - fade); x <= Mathf.Min(w - 1, cxp + fade); x++)
            {
                float t = Mathf.Abs(x - cxp) / (float)fade;
                float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, t));
                surface[x] = Mathf.RoundToInt(Mathf.Lerp(surface[x], floorY[i], k));
            }
        }

        FillColumns(solid, w, h, surface);

        // Сама арка: полуэллиптическая перемычка, концами уходящая в склоны.
        for (int i = 0; i < arches; i++)
        {
            int cxp = Mathf.RoundToInt(sites[i] * w);
            int rise = Mathf.RoundToInt(h * 0.20f);
            int band = Mathf.RoundToInt(h * 0.045f);

            for (int x = Mathf.Max(0, cxp - span[i]); x <= Mathf.Min(w - 1, cxp + span[i]); x++)
            {
                float t = (x - cxp) / (float)span[i];
                int yc = floorY[i] + Mathf.RoundToInt(rise * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)));
                for (int y = yc; y < Mathf.Min(h, yc + band); y++) solid[y * w + x] = true;
            }
        }

        Warp(solid, w, h, rnd, 7f);
        Layers(solid, w, h, rnd, waterN, Rng(rnd, 0.5f, 0.8f));
    }

    // ---------- многоуровневость ----------


    /// Горизонтальный перекос маски: каждая строка сдвигается по шуму от
    /// высоты. Отвесная стена превращается в крючок с навесом — то, чего
    /// высотное поле не умеет в принципе, а на скриншоте оригинала таких
    /// нависающих козырьков больше, чем ровных склонов.
    static void Warp(bool[] solid, int w, int h, System.Random rnd, float amp)
    {
        float o = Off(rnd);
        var row = new bool[w];
        var shift = new int[h];

        for (int y = 0; y < h; y++)
        {
            // Низкая частота по высоте: сдвиг меняется плавно, иначе вместо
            // навесов выходит рябь и по такой земле не пройти.
            float n = Mathf.PerlinNoise(y * 0.013f + o, o * 0.31f)
                    + 0.5f * Mathf.PerlinNoise(y * 0.041f + o * 0.7f, o * 0.11f);
            shift[y] = Mathf.RoundToInt((n / 1.5f - 0.5f) * 2f * amp);
        }

        for (int y = 0; y < h; y++)
        {
            int d = shift[y];
            if (d == 0) continue;
            System.Array.Copy(solid, y * w, row, 0, w);
            for (int x = 0; x < w; x++)
            {
                int src = x + d;
                solid[y * w + x] = src >= 0 && src < w && row[src];
            }
        }
    }

    /// Провалы до воды: массив суши разваливается на несколько кусков с
    /// отвесными боками и пустым небом между ними. На карте оригинала это
    /// главный приём — сплошного берега от края до края там нет.
    static void SkyGaps(bool[] solid, int w, int h, System.Random rnd, float waterN, int gaps)
    {
        int floorY = Mathf.RoundToInt((waterN - 0.02f) * h);
        float span = 0.72f / Mathf.Max(1, gaps);

        for (int i = 0; i < gaps; i++)
        {
            float ncx = 0.16f + span * (i + 0.5f) + Rng(rnd, -0.3f, 0.3f) * span;
            int cx = Mathf.RoundToInt(ncx * w);
            int half = Mathf.RoundToInt(w * Rng(rnd, 0.015f, 0.032f));
            float wobble = Off(rnd);

            for (int y = Mathf.Max(0, floorY); y < h; y++)
            {
                // Бока провала виляют и книзу расходятся: ровная щель читается
                // как прорезь, а не как обрыв.
                float k = Mathf.InverseLerp(floorY, h - 1, y);
                float hw = half * (1f + 0.35f * k)
                         + 5f * (Mathf.PerlinNoise(y * 0.02f + wobble, wobble * 0.5f) - 0.5f);
                for (int x = Mathf.CeilToInt(cx - hw); x <= cx + hw; x++)
                    if (x >= 0 && x < w) solid[y * w + x] = false;
            }
        }
    }


    /// Уборка: выбросить крошку и всё, что ни на чём не держится. Крошку
    /// сыплют и перекос, и шум по краю; парящий кусок оставляют кляксы и
    /// вырезы — раньше крупные такие куски щадились, теперь нет ни одного.
    /// В пещере опорой считается и кровля: сталактит растёт сверху вниз.
    static void Cleanup(bool[] solid, int w, int h, float waterN, int minPixels, bool ceilingRoots = false)
    {
        var seen = new bool[w * h];
        var stack = new System.Collections.Generic.List<int>();
        var part = new System.Collections.Generic.List<int>();

        for (int start = 0; start < solid.Length; start++)
        {
            if (!solid[start] || seen[start]) continue;

            stack.Clear(); part.Clear();
            stack.Add(start); seen[start] = true;

            while (stack.Count > 0)
            {
                int i = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                part.Add(i);

                int x = i % w, y = i / w;
                if (x > 0 && solid[i - 1] && !seen[i - 1]) { seen[i - 1] = true; stack.Add(i - 1); }
                if (x < w - 1 && solid[i + 1] && !seen[i + 1]) { seen[i + 1] = true; stack.Add(i + 1); }
                if (y > 0 && solid[i - w] && !seen[i - w]) { seen[i - w] = true; stack.Add(i - w); }
                if (y < h - 1 && solid[i + w] && !seen[i + w]) { seen[i + w] = true; stack.Add(i + w); }
            }

            // Кусок «на земле», если он дотягивается до полосы у воды
            // (в пещере — ещё и до кровли).
            int rooted = Mathf.RoundToInt((waterN * h) + 3f);
            bool grounded = false;
            for (int k = 0; k < part.Count && !grounded; k++)
            {
                int y = part[k] / w;
                grounded = y <= rooted || (ceilingRoots && y >= h - 2);
            }

            if (part.Count < minPixels || !grounded)
                for (int k = 0; k < part.Count; k++) solid[part[k]] = false;
        }
    }

    /// Верхняя твёрдая колонка каждого столбца (или -1). Нужна пост-проходам:
    /// они лепят полки над землёй и выгрызают каверны под ней.
    static int[] TopSolid(bool[] solid, int w, int h)
    {
        var top = new int[w];
        for (int x = 0; x < w; x++)
        {
            top[x] = -1;
            for (int y = h - 1; y >= 0; y--)
                if (solid[y * w + x]) { top[x] = y; break; }
        }
        return top;
    }

    /// Пост-проход, ради которого всё и затевалось: высотное поле даёт один
    /// силуэт, и вся команда садится вдоль него в линию. Этажи здесь не
    /// пристраиваются к массиву снаружи, а выгрызаются внутри: полости в
    /// толще, куда можно спуститься и откуда стреляют снизу вверх. Так
    /// сделан оригинал — там нет ни одной надстройки над землёй, вся
    /// многоярусность живёт в породе. Дальше PickSpawnPoints разводит
    /// червей по этим этажам.
    static void Layers(bool[] solid, int w, int h, System.Random rnd, float waterN, float amount)
        => Caves(solid, w, h, rnd, waterN, amount);

    /// Полости — срез гладкого шума, а не набор куполов с лазами. Порог по
    /// шуму даёт кляксы разной формы и размера с рваной кромкой: то, чем
    /// изрыт массив в оригинале. Купол же, как его ни виляй, всегда читался
    /// фигурой, нарисованной циркулем, а лаз из него — прямой щелью.
    static void Caves(bool[] solid, int w, int h, System.Random rnd, float waterN, float amount)
    {
        var top = TopSolid(solid, w, h);
        float o1 = Off(rnd), o2 = Off(rnd);
        int floorMin = Mathf.RoundToInt((waterN + 0.02f) * h);

        // Порог: чем ниже, тем больше и чаще полости. Высокий взят нарочно —
        // редкие крупные пузыри читаются как пещеры, частая мелочь читалась
        // как трещины по всему склону.
        float thr = 0.76f - 0.06f * amount;

        for (int x = 0; x < w; x++)
        {
            int t = top[x];
            if (t < floorMin + 30) continue;
            for (int y = floorMin; y <= t; y++)
            {
                // Частоты по осям почти равны: вытянутый по горизонтали шум
                // давал плоские линзы — те самые щели, от которых уходим.
                float n = Mathf.PerlinNoise(x * 0.0090f + o1, y * 0.0105f + o1)
                        + 0.30f * (Mathf.PerlinNoise(x * 0.024f + o2, y * 0.028f + o2) - 0.5f);

                // Под самой поверхностью грызём неохотно, у воды — тоже:
                // тонкая корка над полостью обваливается и читается трещиной,
                // а полость у самой воды просто затопило бы.
                float crust = Mathf.InverseLerp(8f, 42f, t - y);
                float deep = Mathf.InverseLerp(0f, 24f, y - floorMin);
                float need = thr + 0.22f * (1f - crust) + 0.30f * (1f - deep);

                if (n > need) solid[y * w + x] = false;
            }
        }
    }
}
