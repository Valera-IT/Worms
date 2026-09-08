using System.Collections.Generic;
using UnityEngine;

/// Иконки оружия для панели HUD — как в оригинале, где вместо подписей
/// в ряд стоят картинки. Рисуются кистями Pix при первом обращении и
/// кешируются: в проекте по-прежнему нет ни одного файла-ассета.
public static class WeaponIcons
{
    const int S = 48;   // сторона иконки в пикселях

    static readonly Dictionary<WeaponKind, Texture2D> Cache = new Dictionary<WeaponKind, Texture2D>();
    static readonly Dictionary<WeaponKind, Sprite> SpriteCache = new Dictionary<WeaponKind, Sprite>();

    static readonly Color32 Ink = new Color32(18, 16, 20, 255);
    static readonly Color32 Steel = new Color32(150, 156, 166, 255);
    static readonly Color32 SteelLit = new Color32(206, 212, 220, 255);
    static readonly Color32 Wood = new Color32(126, 84, 44, 255);

    // Ракета: тёмный корпус со светлым бликом, светлый нос и белый дым.
    static readonly Color32 Hull = new Color32(38, 46, 84, 255);
    static readonly Color32 HullLit = new Color32(96, 120, 186, 255);
    static readonly Color32 Nose = new Color32(206, 232, 255, 255);
    static readonly Color32 Smoke = new Color32(250, 250, 252, 255);

    static Sprite _missile;
    static Sprite _cross;

    /// Ракета в полёте — отдельная картинка, а не иконка панели: смотрит вправо,
    /// и Projectile разворачивает её по вектору скорости.
    public static Sprite Missile
    {
        get
        {
            if (_missile != null) return _missile;

            // Рисуем прямоугольниками, а не кистью: круглые концы Line на
            // картинке в тридцать пикселей раздувают нос в шар.
            var p = new Pix(32, 14);
            p.Rect(7, 4, 15, 6, Hull);          // корпус
            p.Rect(8, 8, 13, 2, HullLit);       // блик по верхней кромке

            // Нос клином: колонка за колонкой, высота сходит к единице.
            for (int i = 0; i < 7; i++)
            {
                int h = Mathf.Max(1, 6 - i);
                p.Rect(22 + i, 4 + (6 - h) / 2, 1, h, Nose);
            }

            // Оперение двумя уступами и огонёк двигателя в хвосте.
            p.Rect(4, 2, 4, 3, Hull);
            p.Rect(4, 9, 4, 3, Hull);
            p.Disc(4f, 7f, 2.0f, new Color32(252, 196, 64, 255));

            p.Outline(Ink);
            _missile = p.ToSprite(40f);
            _missile.name = "MissileSprite";
            return _missile;
        }
    }

    static Sprite _powerBeam;

    /// Полоса силы: клин, расходящийся от червя вперёд, с переливом поперёк —
    /// светло-жёлтым по верхней кромке и красным по нижней, как в оригинале.
    /// Перелив идёт поперёк луча, а не вдоль, поэтому растягивать картинку по
    /// длине можно без вреда: тянутся ровные полосы одного цвета.
    ///
    /// Пивот — в узком конце: полоса растёт из ствола, а не в обе стороны от
    /// своей середины.
    public static Sprite PowerBeam
    {
        get
        {
            if (_powerBeam != null) return _powerBeam;

            const int w = 64, h = 22;
            var p = new Pix(w, h);

            var top = new Color(1f, 0.96f, 0.62f);
            var mid = new Color(1f, 0.6f, 0.12f);
            var low = new Color(0.85f, 0.13f, 0.11f);

            for (int x = 0; x < w; x++)
            {
                // Клин: у ствола почти остриё, к концу — во всю высоту.
                float half = Mathf.Lerp(1.6f, (h - 2) * 0.5f, x / (float)(w - 1));
                int y0 = Mathf.RoundToInt(h * 0.5f - half);
                int y1 = Mathf.RoundToInt(h * 0.5f + half);

                for (int y = y0; y <= y1; y++)
                {
                    float t = y1 > y0 ? (y - y0) / (float)(y1 - y0) : 0.5f;
                    // t растёт сверху вниз по клину, поэтому светлое — в начале.
                    var c = t < 0.5f ? Color.Lerp(top, mid, t * 2f)
                                     : Color.Lerp(mid, low, (t - 0.5f) * 2f);
                    p.Set(x, y, c);
                }
            }

            p.Outline(Ink);
            _powerBeam = p.ToSprite(32f, new Vector2(0f, 0.5f));
            _powerBeam.name = "PowerBeamSprite";
            return _powerBeam;
        }
    }

    /// Крестик наводки: та самая метка попадания, которую на Сеге ставили
    /// джойстиком до выстрела.
    public static Sprite CrossMark
    {
        get
        {
            if (_cross != null) return _cross;

            var p = new Pix(30, 30);
            var gold = new Color32(250, 176, 32, 255);
            var hot = new Color32(255, 226, 128, 255);
            p.Line(7f, 7f, 22f, 22f, 6.5f, gold);
            p.Line(22f, 7f, 7f, 22f, 6.5f, gold);
            p.Line(9f, 9f, 20f, 20f, 2f, hot);
            p.Line(20f, 9f, 9f, 20f, 2f, hot);

            p.Outline(Ink);
            // 30 пикселей на 32 — крестик выходит ростом почти в юнит, вровень
            // с червём: мельче его на карте попросту не разглядеть.
            _cross = p.ToSprite(32f);
            _cross.name = "CrossMarkSprite";
            return _cross;
        }
    }

    static Sprite _sight;

    /// Прицел направления: кольцо с перекрестием, как в оригинале, — стоит
    /// перед червём в ту сторону, куда он целится. Красное с тёмной обводкой:
    /// на снегу, песке и в чёрной пещере одинаково видно.
    public static Sprite Sight
    {
        get
        {
            if (_sight != null) return _sight;

            const int n = 34;
            float c = n * 0.5f - 0.5f;
            var p = new Pix(n, n);
            var red = new Color32(228, 54, 66, 255);
            var hot = new Color32(255, 146, 152, 255);

            // Кольцо: диск и вырезанная середина. Вырезаем прозрачным — так же
            // делается дырка в любом другом спрайте проекта.
            p.Disc(c, c, 11.5f, red);
            p.Disc(c, c, 8.5f, Pix.Clear);
            p.Disc(c, c, 11f, hot);
            p.Disc(c, c, 9f, Pix.Clear);

            // Четыре луча наружу, с прогалом в самом центре: перекрестие без
            // дырки закрывало бы собой ту точку, на которую наводят.
            for (int i = 0; i < 4; i++)
            {
                float dx = i == 0 ? 1 : i == 1 ? -1 : 0;
                float dy = i == 2 ? 1 : i == 3 ? -1 : 0;
                p.Line(c + dx * 3.5f, c + dy * 3.5f, c + dx * 16f, c + dy * 16f, 3.2f, red);
                p.Line(c + dx * 4f, c + dy * 4f, c + dx * 15f, c + dy * 15f, 1.4f, hot);
            }

            p.Outline(Ink);
            // 34 пикселя на 30 — кольцо чуть больше червя, как на скриншотах
            // оригинала: меньше теряется на пёстрой земле.
            _sight = p.ToSprite(30f);
            _sight.name = "SightSprite";
            return _sight;
        }
    }

    /// Та же иконка мировым спрайтом: ею летит банан и лежат динамит, мина и
    /// овца. 60 пикселей на юнит — картинка 48×48 выходит ростом в 0,8 юнита,
    /// чуть меньше червя.
    public static Sprite Sprite(WeaponKind kind)
    {
        if (SpriteCache.TryGetValue(kind, out var s) && s != null) return s;

        var tex = Get(kind);
        s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                      new Vector2(0.5f, 0.5f), 60f, 0, SpriteMeshType.FullRect);
        s.name = "Weapon_" + kind;
        SpriteCache[kind] = s;
        return s;
    }

    /// Под каким углом иконка нарисована на холсте. Ствол в руках червя
    /// доворачивается на этот угол назад — иначе базука, нарисованная для
    /// панели по диагонали, целилась бы на сорок пять градусов выше прицела.
    /// NaN — предмет не наводят: граната, динамит и овца висят в руке ровно,
    /// как их нарисовали, и вертеть их за прицелом незачем.
    public static float Lean(WeaponKind kind)
    {
        switch (kind)
        {
            case WeaponKind.Bazooka: return 0f;    // труба нарисована горизонтально
            case WeaponKind.Homing: return 45f;
            case WeaponKind.Mortar: return 54f;
            case WeaponKind.Shotgun: return 25f;
            case WeaponKind.Uzi: return 0f;
            case WeaponKind.Bat: return 48f;
            case WeaponKind.Girder: return 28f;
            case WeaponKind.Blowtorch: return 0f;
            case WeaponKind.Prod: return 0f;
            default: return float.NaN;
        }
    }

    public static Texture2D Get(WeaponKind kind)
    {
        if (Cache.TryGetValue(kind, out var tex) && tex != null) return tex;

        var pix = kind switch
        {
            WeaponKind.Bazooka => Bazooka(),
            WeaponKind.Homing => Homing(),
            WeaponKind.Mortar => Mortar(),
            WeaponKind.Grenade => Grenade(),
            WeaponKind.Cluster => Cluster(),
            WeaponKind.Banana => Banana(),
            WeaponKind.Shotgun => Shotgun(),
            WeaponKind.Uzi => Uzi(),
            WeaponKind.FirePunch => FirePunch(),
            WeaponKind.Bat => Bat(),
            WeaponKind.Dynamite => Dynamite(),
            WeaponKind.Mine => MineIcon(),
            WeaponKind.Sheep => SheepIcon(),
            WeaponKind.AirStrike => AirStrike(),
            WeaponKind.Rope => RopeIcon(),
            WeaponKind.Prod => ProdIcon(),
            WeaponKind.Drill => DrillIcon(),
            WeaponKind.Blowtorch => TorchIcon(),
            WeaponKind.Girder => GirderIcon(),
            WeaponKind.Parachute => ChuteIcon(),
            WeaponKind.Jetpack => JetIcon(),
            WeaponKind.HolyGrenade => HolyIcon(),
            WeaponKind.SuperSheep => SuperSheepIcon(),
            WeaponKind.Anvil => AnvilIcon(),
            WeaponKind.Napalm => NapalmIcon(),
            WeaponKind.MineStrike => MineStrikeIcon(),
            WeaponKind.Donkey => DonkeyIcon(),
            _ => TeleportIcon()
        };

        pix.Outline(Ink);
        tex = pix.ToTexture();
        Cache[kind] = tex;
        return tex;
    }

    /// Святая граната: та же граната, но золотая, с крестом наверху и нимбом.
    /// Крест и нимб — единственное, чем она отличается от обычной в панели,
    /// поэтому оба рисуем крупно: в слоте семьдесят шесть пикселей мелочь
    /// сливается в пятно.
    static Pix HolyIcon()
    {
        var p = new Pix(S, S);
        var gold = new Color32(214, 168, 46, 255);
        var goldLit = new Color32(250, 224, 128, 255);
        var goldDim = new Color32(150, 112, 22, 255);

        p.Disc(23f, 16f, 12f, gold);
        p.Disc(18f, 20f, 5f, goldLit);
        p.Disc(29f, 10f, 4.5f, goldDim);

        // Нимб — кольцо над крестом: диск светлым, середина обратно в пустоту,
        // нижняя половина срезана. Рисуем его до креста: срез идёт по всей
        // ширине холста и снёс бы крестовину, попадись она раньше.
        p.Disc(23f, 43f, 5.5f, goldLit);
        p.Disc(23f, 43f, 3.4f, Pix.Clear);
        p.Rect(11, 34, 26, 9, Pix.Clear);

        // Крест: стойка от макушки корпуса до нимба и широкая крестовина.
        p.Rect(21, 26, 5, 14, goldLit);
        p.Rect(15, 32, 17, 5, goldLit);
        return p;
    }

    /// Супер-овца: та же овца, но в очках и с огнём из-под хвоста.
    static Pix SuperSheepIcon()
    {
        var p = new Pix(S, S);
        var wool = new Color32(236, 242, 250, 255);
        var woolDim = new Color32(190, 202, 220, 255);
        var dark = new Color32(46, 42, 48, 255);
        var glass = new Color32(94, 196, 232, 255);

        p.Rect(18, 8, 3, 6, dark);              // ноги поджаты: овца летит
        p.Rect(24, 8, 3, 6, dark);

        p.Disc(22f, 24f, 11f, wool);            // руно
        p.Disc(31f, 22f, 8f, wool);
        p.Disc(15f, 21f, 7f, woolDim);

        p.Disc(36f, 25f, 6f, dark);             // морда
        p.Disc(33f, 32f, 3f, dark);             // ухо
        p.Rect(33, 26, 11, 4, glass);           // очки
        p.Rect(34, 27, 3, 2, new Color32(198, 240, 255, 255));   // блик в стекле

        // Факел из-под хвоста: три языка, уходящие назад и вниз. Держим их
        // ниже руна: на одной с ним высоте огонь читался мордой, и овца
        // получалась двухголовой.
        p.Disc(9f, 13f, 3.8f, new Color32(252, 196, 64, 255));
        p.Disc(5f, 11f, 2.6f, new Color32(240, 128, 40, 255));
        p.Disc(2f, 10f, 1.6f, new Color32(214, 74, 32, 255));
        return p;
    }

    /// Наковальня: подошва, талия и рог. Рисуется прямоугольниками — у железа
    /// на такой стороне не должно быть ни одного скруглённого края.
    static Pix AnvilIcon()
    {
        var p = new Pix(S, S);
        var iron = new Color32(78, 84, 96, 255);
        var ironLit = new Color32(138, 146, 162, 255);
        var ironDim = new Color32(46, 50, 60, 255);

        p.Rect(12, 6, 24, 6, iron);          // подошва
        p.Rect(18, 12, 12, 10, ironDim);     // талия
        p.Rect(8, 22, 32, 10, iron);         // наковальня
        p.Rect(9, 29, 30, 3, ironLit);       // блик по рабочей плоскости

        // Рог: колонка за колонкой сходит на нет вправо.
        for (int i = 0; i < 8; i++)
        {
            int h = Mathf.Max(2, 9 - i);
            p.Rect(40 + i, 22 + (9 - h) / 2, 1, h, iron);
        }
        return p;
    }

    /// Напалм: бак с горючим, из горловины бьёт пламя, вниз капает огонь.
    static Pix NapalmIcon()
    {
        var p = new Pix(S, S);
        var can = new Color32(96, 88, 62, 255);
        var canLit = new Color32(146, 136, 96, 255);

        p.Rect(13, 20, 22, 20, can);         // бак
        p.Rect(15, 24, 3, 14, canLit);       // блик
        p.Rect(19, 40, 10, 4, can);          // горловина

        p.Disc(24f, 46f, 4f, new Color32(252, 208, 96, 255));    // факел
        p.Disc(24f, 44f, 2f, new Color32(255, 244, 200, 255));

        // Капли: три языка огня, стекающие из-под бака.
        p.Disc(15f, 15f, 4f, new Color32(240, 128, 36, 255));
        p.Disc(24f, 11f, 4.6f, new Color32(252, 172, 48, 255));
        p.Disc(33f, 15f, 4f, new Color32(240, 128, 36, 255));
        p.Disc(24f, 9f, 2.2f, new Color32(255, 232, 160, 255));
        return p;
    }

    /// Минный удар: тот же самолёт, что у налёта, но сыплет минами — рогатыми
    /// шариками, а не бомбами. Разница в панели должна читаться с одного
    /// взгляда: два соседних слота отличаются только грузом.
    static Pix MineStrikeIcon()
    {
        var p = new Pix(S, S);
        var plane = new Color32(120, 140, 168, 255);
        var planeLit = new Color32(186, 204, 226, 255);

        p.Line(8f, 34f, 40f, 40f, 6f, plane);          // фюзеляж
        p.Line(10f, 36f, 36f, 41f, 2f, planeLit);
        p.Line(20f, 40f, 26f, 30f, 4f, plane);         // крыло вниз
        p.Line(20f, 38f, 16f, 45f, 4f, plane);         // киль
        p.Disc(38f, 40f, 3.4f, planeLit);              // нос

        var shell = new Color32(96, 100, 108, 255);
        var spark = new Color32(226, 62, 48, 255);
        for (int i = 0; i < 3; i++)
        {
            float cx = 14f + i * 8f, cy = 22f - i * 7f;
            p.Disc(cx, cy, 4f, shell);
            p.Line(cx - 3f, cy + 3f, cx - 5f, cy + 6f, 1.6f, shell);   // усики
            p.Line(cx + 3f, cy + 3f, cx + 5f, cy + 6f, 1.6f, shell);
            p.Disc(cx, cy + 5f, 1.4f, spark);
        }
        return p;
    }

    /// Бетонный осёл: серый истукан анфас. Цвет — бетон, без единого блика
    /// на металле: он не отлит, а вылит.
    static Pix DonkeyIcon()
    {
        var p = new Pix(S, S);
        var stone = new Color32(146, 142, 134, 255);
        var stoneLit = new Color32(188, 184, 176, 255);
        var stoneDim = new Color32(96, 94, 90, 255);

        p.Rect(10, 6, 5, 12, stone);         // ноги
        p.Rect(19, 6, 5, 12, stone);
        p.Rect(28, 6, 5, 12, stone);

        p.Rect(8, 17, 27, 14, stone);        // туловище
        p.Rect(9, 27, 25, 3, stoneLit);      // спина посветлее
        p.Rect(8, 17, 27, 3, stoneDim);      // тень под брюхом

        p.Rect(30, 28, 10, 12, stone);       // голова
        p.Rect(36, 24, 8, 6, stone);         // морда
        p.Rect(31, 40, 3, 7, stone);         // уши
        p.Rect(36, 40, 3, 7, stone);
        p.Disc(38f, 34f, 1.6f, stoneDim);    // глаз

        p.Rect(4, 24, 5, 3, stone);          // хвост
        return p;
    }

    /// Ракета носом вверх-вправо, с оперением и огоньком в сопле.
    /// Базука — жёлтая труба одной толщины с чёрным хватом, как в оригинале.
    /// Раструба у дула нет: труба не расширяется, а спереди в ней видно
    /// круглое жерло в её же диаметре — тёмный кружок в жёлтом ободке.
    /// Смотрит вправо и лежит горизонтально: в руках у червя её доворачивает
    /// прицел (см. Lean), и наклон в самом рисунке этому мешал бы.
    static Pix Bazooka()
    {
        var p = new Pix(S, S);
        var tube = new Color32(232, 186, 36, 255);
        var tubeLit = new Color32(255, 236, 140, 255);
        var tubeDim = new Color32(168, 126, 18, 255);
        var grip = new Color32(38, 36, 42, 255);
        var bore = new Color32(26, 24, 28, 255);

        const float axis = 26f;
        const float thick = 11.5f;

        // Труба одной толщины от казённика до дула.
        p.Line(9f, axis, 39f, axis, thick, tube);

        // Низ в тень, верх — два светлых штриха вдоль, как блик на цилиндре.
        p.Line(10f, axis - 3.4f, 38f, axis - 3.4f, 2.4f, tubeDim);
        p.Line(11f, axis + 2.8f, 27f, axis + 2.8f, 2f, tubeLit);
        p.Line(34f, axis + 2.8f, 36f, axis + 2.8f, 2f, tubeLit);

        // Жерло: тёмный кружок в самом торце, ободок трубы вокруг него.
        // Жерло почти во всю трубу: узкий ободок и тёмная дыра — иначе
        // в руках у червя, где иконка втрое мельче, дуло не разглядеть.
        p.Disc(39f, axis, 4.8f, bore);
        p.Disc(39.4f, axis, 3.4f, new Color32(14, 12, 16, 255));

        // Казённик прикрыт: тень на скруглении, а не дырка.
        p.Disc(9.5f, axis, 3f, tubeDim);

        // Чёрное кольцо на трубе — и всё. Рукояти вниз у базуки в оригинале
        // нет: на рисунке снизу не деталь оружия, а рука червя. Кольцо лежит
        // ровно в диаметре трубы: прямоугольником, а не линией с круглыми
        // концами, — та выпирала за бока на радиус кисти. Стоит на трети
        // длины от дульного среза, а не посередине.
        p.Rect(28, Mathf.CeilToInt(axis - thick * 0.5f), 5,
               Mathf.FloorToInt(axis + thick * 0.5f) - Mathf.CeilToInt(axis - thick * 0.5f) + 1, grip);

        return p;
    }

    /// Граната-лимонка: тело в клетку, скоба и колечко.
    static Pix Grenade()
    {
        var p = new Pix(S, S);
        var body = new Color32(62, 108, 52, 255);
        var lit = new Color32(104, 158, 78, 255);
        var seam = new Color32(40, 74, 36, 255);

        p.Rect(19, 30, 8, 7, new Color32(96, 92, 84, 255));        // горловина
        p.Disc(23f, 19f, 13f, body);
        p.Disc(18f, 23f, 5.5f, lit);

        // Насечки рисуем прямо по кругу — так они не вылезают за корпус.
        for (int y = 6; y <= 32; y++)
        for (int x = 10; x <= 36; x++)
        {
            float dx = x - 23f, dy = y - 19f;
            if (dx * dx + dy * dy > 13f * 13f) continue;
            if (x % 5 == 0 || y % 5 == 0) p.Set(x, y, seam);
        }

        p.Line(27f, 34f, 34f, 26f, 3f, Steel);                     // скоба
        p.Disc(35f, 24f, 4f, Steel);                               // колечко
        p.Disc(35f, 24f, 2f, Pix.Clear);
        return p;
    }

    /// Кассета: бомба с оперением и три отделяемых шарика.
    static Pix Cluster()
    {
        var p = new Pix(S, S);
        var shell = new Color32(226, 176, 52, 255);
        var shellLit = new Color32(250, 222, 120, 255);

        p.Disc(24f, 30f, 10f, shell);
        p.Rect(18, 30, 12, 8, shell);
        p.Disc(20f, 33f, 4f, shellLit);
        p.Line(18f, 38f, 12f, 44f, 3f, Steel);
        p.Line(30f, 38f, 36f, 44f, 3f, Steel);

        p.Disc(11f, 11f, 5f, shell);
        p.Disc(24f, 7f, 5f, shell);
        p.Disc(37f, 11f, 5f, shell);
        return p;
    }

    /// Дробовик: приклад, ствол и облачко дроби.
    static Pix Shotgun()
    {
        var p = new Pix(S, S);
        p.Line(6f, 12f, 16f, 18f, 9f, Wood);                       // приклад
        p.Line(14f, 17f, 42f, 30f, 6f, Steel);                     // ствол
        p.Line(14f, 19f, 40f, 31f, 2f, SteelLit);
        p.Line(16f, 14f, 26f, 19f, 4f, new Color32(96, 66, 36, 255)); // цевьё
        p.Line(15f, 12f, 18f, 9f, 3f, Wood);                       // рукоять

        var shot = new Color32(250, 232, 150, 255);
        p.Disc(45f, 33f, 3f, shot);
        p.Disc(42f, 39f, 2f, shot);
        p.Disc(46f, 26f, 2f, shot);
        return p;
    }


    /// Самонаводящаяся: тёмная ракета с белым дымным следом — ровно та, что
    /// летит по карте, и та же, что была на Сеге. Носовой конус светлый,
    /// хвост уходит в клубки дыма.
    static Pix Homing()
    {
        var p = new Pix(S, S);

        // След: клубки от угла к хвосту, чем дальше — тем крупнее и бледнее.
        p.Disc(6f, 8f, 5.0f, new Color32(232, 235, 242, 255));
        p.Disc(13f, 15f, 3.9f, new Color32(242, 244, 249, 255));
        p.Disc(18f, 20f, 2.9f, Smoke);

        // Корпус по диагонали вверх-вправо, как у базуки рядом.
        p.Line(21f, 23f, 35f, 37f, 9f, Hull);
        p.Line(20f, 25f, 32f, 37f, 3f, HullLit);

        // Оперение поперёк хвоста.
        p.Line(17f, 27f, 25f, 19f, 3f, Hull);

        // Носовой конус.
        for (int i = 0; i < 8; i++)
            p.Disc(35f + i * 0.85f, 37f + i * 0.85f, 4.6f - i * 0.5f, Nose);

        return p;
    }

    /// Миномёт: короткая труба на сошках и мина над стволом.
    static Pix Mortar()
    {
        var p = new Pix(S, S);
        p.Line(14f, 10f, 30f, 32f, 10f, Steel);
        p.Line(13f, 12f, 27f, 32f, 3f, SteelLit);
        p.Line(10f, 6f, 20f, 20f, 3f, new Color32(96, 100, 108, 255));   // сошка
        p.Line(22f, 6f, 20f, 20f, 3f, new Color32(96, 100, 108, 255));
        p.Rect(8, 4, 16, 4, new Color32(96, 100, 108, 255));             // плита

        p.Disc(34f, 38f, 4.5f, new Color32(74, 82, 62, 255));            // мина в воздухе
        p.Line(34f, 42f, 34f, 45f, 2f, new Color32(74, 82, 62, 255));
        return p;
    }

    /// Банан: изогнутая долька с хвостиком.
    static Pix Banana()
    {
        var p = new Pix(S, S);
        var peel = new Color32(246, 214, 62, 255);
        var lit = new Color32(255, 240, 150, 255);
        var tip = new Color32(120, 92, 30, 255);

        for (int a = 0; a <= 42; a++)
        {
            float t = a / 42f;
            float ang = Mathf.Lerp(200f, 340f, t) * Mathf.Deg2Rad;
            float x = 24f + Mathf.Cos(ang) * 17f;
            float y = 34f + Mathf.Sin(ang) * 17f;
            p.Disc(x, y, 5.5f - Mathf.Abs(t - 0.5f) * 4f, peel);
        }
        for (int a = 0; a <= 30; a++)
        {
            float t = a / 30f;
            float ang = Mathf.Lerp(210f, 330f, t) * Mathf.Deg2Rad;
            p.Disc(24f + Mathf.Cos(ang) * 14.5f, 34f + Mathf.Sin(ang) * 14.5f, 1.6f, lit);
        }
        p.Disc(8f, 30f, 2.6f, tip);
        p.Disc(40f, 30f, 2.4f, tip);
        return p;
    }

    /// Узи: коробчатый автомат с магазином и вспышкой у среза.
    static Pix Uzi()
    {
        var p = new Pix(S, S);
        var gun = new Color32(84, 88, 96, 255);
        var gunLit = new Color32(150, 156, 166, 255);

        p.Rect(10, 22, 22, 9, gun);            // ствольная коробка
        p.Rect(12, 24, 18, 2, gunLit);
        p.Rect(30, 25, 12, 4, gun);            // ствол
        p.Rect(14, 12, 7, 11, gun);            // магазин
        p.Line(12f, 22f, 8f, 15f, 4f, new Color32(60, 62, 68, 255));  // рукоять

        var flash = new Color32(252, 226, 120, 255);
        p.Disc(44f, 27f, 4f, flash);
        p.Disc(46f, 32f, 2f, flash);
        p.Disc(46f, 22f, 2f, flash);
        return p;
    }

    /// Огненный кулак: перчатка снизу вверх в языках пламени.
    static Pix FirePunch()
    {
        var p = new Pix(S, S);
        var fist = new Color32(226, 176, 128, 255);
        var fistLit = new Color32(250, 214, 176, 255);
        var cuff = new Color32(72, 108, 176, 255);

        p.Disc(22f, 22f, 10f, fist);           // кулак
        p.Disc(18f, 25f, 4.5f, fistLit);
        p.Rect(14, 6, 16, 9, cuff);            // манжета
        p.Rect(16, 15, 12, 3, new Color32(52, 84, 148, 255));

        // Пламя над кулаком.
        var fire = new Color32(252, 168, 48, 255);
        var fireLit = new Color32(252, 232, 120, 255);
        p.Disc(22f, 34f, 6f, fire);
        p.Disc(16f, 38f, 3.6f, fire);
        p.Disc(29f, 37f, 3.2f, fire);
        p.Disc(22f, 40f, 3.4f, fireLit);
        p.Disc(22f, 45f, 1.8f, fireLit);
        return p;
    }

    /// Толчок: рука с выставленным указательным пальцем — тем самым, которым
    /// в оригинале сталкивают соседа с обрыва. Смотрит вправо, как и червь.
    static Pix ProdIcon()
    {
        var p = new Pix(S, S);
        var skin = new Color32(226, 176, 128, 255);
        var skinLit = new Color32(250, 214, 176, 255);
        var cuff = new Color32(72, 108, 176, 255);

        p.Rect(6, 15, 10, 16, cuff);              // манжета рукава
        p.Rect(14, 17, 4, 12, new Color32(52, 84, 148, 255));

        p.Disc(23f, 23f, 9.5f, skin);             // кулак
        p.Disc(20f, 26f, 4.2f, skinLit);

        p.Rect(30, 21, 13, 6, skin);              // указательный палец
        p.Disc(43f, 24f, 3f, skin);
        p.Rect(31, 25, 10, 2, skinLit);

        // Толчок: три чёрточки перед пальцем — движение вправо.
        var air = new Color32(236, 236, 240, 255);
        p.Rect(38, 33, 8, 3, air);
        p.Rect(36, 12, 8, 3, air);
        return p;
    }

    /// Парашют: купол дольками, стропы и груз под ними.
    static Pix ChuteIcon()
    {
        var p = new Pix(S, S);
        var cloth = new Color32(228, 234, 244, 255);
        var clothDark = new Color32(176, 188, 208, 255);
        var cord = new Color32(96, 90, 82, 255);
        var load = new Color32(140, 96, 60, 255);

        p.Disc(24f, 26f, 16f, cloth);           // купол
        p.Rect(0, 4, S, 22, Pix.Clear);         // низ купола срезаем — остаётся арка
        p.Rect(16, 24, 6, 14, clothDark);       // дольки
        p.Rect(28, 24, 6, 14, clothDark);

        p.Line(9f, 26f, 20f, 13f, 1.5f, cord);  // стропы
        p.Line(39f, 26f, 28f, 13f, 1.5f, cord);
        p.Line(24f, 26f, 24f, 13f, 1.5f, cord);

        p.Rect(19, 5, 10, 8, load);             // груз
        p.Rect(21, 8, 6, 3, new Color32(178, 130, 84, 255));
        return p;
    }

    /// Ранец: два баллона с лямкой и струя пламени снизу.
    static Pix JetIcon()
    {
        var p = new Pix(S, S);
        var tank = new Color32(126, 134, 148, 255);
        var tankLit = new Color32(184, 192, 206, 255);
        var strap = new Color32(70, 62, 56, 255);
        var flame = new Color32(250, 168, 48, 255);
        var flameHot = new Color32(255, 236, 160, 255);

        p.Rect(11, 18, 11, 24, tank);           // левый баллон
        p.Rect(26, 18, 11, 24, tank);           // правый
        p.Rect(13, 30, 4, 9, tankLit);
        p.Rect(28, 30, 4, 9, tankLit);
        p.Disc(16.5f, 42f, 5.5f, tank);         // скруглённые крышки
        p.Disc(31.5f, 42f, 5.5f, tank);
        p.Rect(22, 26, 4, 12, strap);           // перемычка

        p.Rect(13, 12, 7, 6, flame);            // сопла и пламя
        p.Rect(28, 12, 7, 6, flame);
        p.Disc(16.5f, 8f, 4f, flame);
        p.Disc(31.5f, 8f, 4f, flame);
        p.Disc(16.5f, 10f, 2f, flameHot);
        p.Disc(31.5f, 10f, 2f, flameHot);
        return p;
    }

    /// Бур: корпус с рукоятью, снизу — сужающееся сверло с витком.
    static Pix DrillIcon()
    {
        var p = new Pix(S, S);
        var body = new Color32(118, 126, 140, 255);
        var bodyLit = new Color32(176, 184, 198, 255);
        var steel = new Color32(198, 202, 210, 255);
        var grip = new Color32(74, 60, 48, 255);

        p.Rect(14, 30, 20, 16, body);          // корпус мотора
        p.Rect(16, 38, 8, 6, bodyLit);
        p.Rect(30, 34, 12, 7, grip);           // рукоять вбок

        p.Rect(20, 20, 8, 11, steel);          // хвостовик
        // Сверло книзу сужается — рисуем ступеньками, чтобы остался пиксель-арт.
        p.Rect(21, 14, 6, 6, steel);
        p.Rect(22, 9, 4, 5, steel);
        p.Rect(23, 5, 2, 4, steel);

        // Виток спирали: две косые чёрточки потемнее.
        p.Line(21f, 18f, 27f, 15f, 1.6f, body);
        p.Line(22f, 12f, 26f, 10f, 1.4f, body);
        return p;
    }

    /// Паяльная лампа: баллон, сопло и язык пламени вправо.
    static Pix TorchIcon()
    {
        var p = new Pix(S, S);
        var tank = new Color32(96, 104, 118, 255);
        var tankLit = new Color32(150, 158, 172, 255);
        var nozzle = new Color32(190, 194, 202, 255);
        var flame = new Color32(250, 176, 52, 255);
        var flameHot = new Color32(255, 232, 150, 255);

        p.Rect(6, 16, 18, 16, tank);           // баллон
        p.Rect(8, 24, 6, 6, tankLit);
        p.Rect(24, 21, 8, 6, nozzle);          // сопло

        p.Disc(36f, 24f, 7f, flame);           // пламя
        p.Rect(31, 21, 8, 6, flame);
        p.Disc(35f, 24f, 3.4f, flameHot);
        p.Rect(42, 22, 4, 4, flame);
        return p;
    }

    /// Балка: стальная плита наискось, с заклёпками по краям.
    static Pix GirderIcon()
    {
        var p = new Pix(S, S);
        var steel = new Color32(150, 158, 170, 255);
        var steelLit = new Color32(198, 206, 216, 255);
        var rivet = new Color32(86, 92, 104, 255);

        p.Line(7f, 15f, 41f, 33f, 9f, steel);      // сама балка
        p.Line(8f, 17f, 40f, 34f, 2.4f, steelLit); // блик по верхней кромке

        p.Disc(12f, 18f, 1.8f, rivet);             // заклёпки
        p.Disc(24f, 24f, 1.8f, rivet);
        p.Disc(36f, 30f, 1.8f, rivet);
        return p;
    }

    /// Бита: рукоять снизу слева, набалдашник сверху справа.
    static Pix Bat()
    {
        var p = new Pix(S, S);
        var wood = new Color32(196, 148, 88, 255);
        var woodLit = new Color32(232, 194, 138, 255);

        p.Line(9f, 9f, 16f, 16f, 5f, new Color32(126, 84, 44, 255));   // рукоять
        for (int i = 0; i < 22; i++)
            p.Disc(16f + i * 1.1f, 16f + i * 1.1f, 3.5f + i * 0.22f, wood);
        p.Line(16f, 19f, 34f, 37f, 2.4f, woodLit);
        p.Disc(11f, 11f, 3.4f, new Color32(96, 62, 32, 255));          // навершие
        return p;
    }

    /// Динамит: связка шашек с горящим фитилём.
    static Pix Dynamite()
    {
        var p = new Pix(S, S);
        var stick = new Color32(198, 54, 44, 255);
        var stickLit = new Color32(238, 106, 92, 255);

        p.Rect(12, 8, 8, 26, stick);
        p.Rect(20, 8, 8, 26, stick);
        p.Rect(28, 8, 8, 26, stick);
        p.Rect(13, 10, 2, 22, stickLit);
        p.Rect(21, 10, 2, 22, stickLit);
        p.Rect(10, 16, 28, 4, new Color32(120, 30, 24, 255));   // обмотка

        p.Line(24f, 34f, 30f, 42f, 2f, new Color32(210, 200, 180, 255));  // фитиль
        p.Disc(31f, 43f, 3.4f, new Color32(252, 196, 64, 255));
        p.Disc(32f, 45f, 1.8f, new Color32(252, 240, 180, 255));
        return p;
    }

    /// Мина: полусфера с усиками и красным глазком.
    static Pix MineIcon()
    {
        var p = new Pix(S, S);
        var shell = new Color32(96, 100, 108, 255);
        var shellLit = new Color32(152, 158, 168, 255);

        p.Disc(24f, 18f, 13f, shell);
        p.Rect(9, 8, 30, 8, shell);
        p.Disc(18f, 22f, 5f, shellLit);

        p.Line(14f, 26f, 10f, 34f, 2.4f, shell);    // усики
        p.Line(24f, 31f, 24f, 40f, 2.4f, shell);
        p.Line(34f, 26f, 38f, 34f, 2.4f, shell);
        p.Disc(24f, 41f, 2.6f, new Color32(226, 62, 48, 255));
        p.Disc(28f, 14f, 3f, new Color32(226, 62, 48, 255));
        return p;
    }

    /// Овца: облако шерсти, чёрная морда и четыре ножки.
    static Pix SheepIcon()
    {
        var p = new Pix(S, S);
        var wool = new Color32(238, 238, 232, 255);
        var woolDim = new Color32(198, 198, 194, 255);
        var dark = new Color32(46, 42, 48, 255);

        p.Rect(14, 6, 3, 8, dark);              // ноги
        p.Rect(20, 6, 3, 8, dark);
        p.Rect(26, 6, 3, 8, dark);
        p.Rect(32, 6, 3, 8, dark);

        p.Disc(20f, 22f, 11f, wool);            // руно
        p.Disc(30f, 20f, 9f, wool);
        p.Disc(13f, 20f, 7f, woolDim);
        p.Disc(24f, 30f, 8f, wool);

        p.Disc(36f, 27f, 6.5f, dark);           // морда
        p.Disc(38f, 29f, 1.6f, wool);           // глаз
        p.Disc(34f, 33f, 2.6f, dark);           // ухо
        return p;
    }

    /// Налёт: самолётик и три падающие бомбы.
    static Pix AirStrike()
    {
        var p = new Pix(S, S);
        var plane = new Color32(120, 140, 168, 255);
        var planeLit = new Color32(186, 204, 226, 255);

        p.Line(8f, 34f, 40f, 40f, 6f, plane);          // фюзеляж
        p.Line(10f, 36f, 36f, 41f, 2f, planeLit);
        p.Line(20f, 40f, 26f, 30f, 4f, plane);         // крыло вниз
        p.Line(20f, 38f, 16f, 45f, 4f, plane);         // киль
        p.Disc(38f, 40f, 3.4f, planeLit);              // нос

        var bomb = new Color32(74, 82, 62, 255);
        p.Disc(14f, 22f, 3.4f, bomb);
        p.Disc(22f, 15f, 3.4f, bomb);
        p.Disc(30f, 8f, 3.4f, bomb);
        p.Line(14f, 26f, 14f, 28f, 1.6f, bomb);
        p.Line(22f, 19f, 22f, 21f, 1.6f, bomb);
        p.Line(30f, 12f, 30f, 14f, 1.6f, bomb);
        return p;
    }

    /// Верёвка: моток с крюком-кошкой.
    static Pix RopeIcon()
    {
        var p = new Pix(S, S);
        var rope = new Color32(198, 174, 122, 255);
        var ropeDark = new Color32(146, 122, 78, 255);

        // Виток за витком по спирали — моток читается лучше, чем кольцо.
        for (int i = 0; i < 3; i++)
        {
            float r = 14f - i * 4.5f;
            for (int a = 0; a < 40; a++)
            {
                float t = a / 40f * Mathf.PI * 2f;
                p.Disc(20f + Mathf.Cos(t) * r, 26f + Mathf.Sin(t) * r * 0.85f, 2.2f,
                       a % 8 < 4 ? rope : ropeDark);
            }
        }

        p.Line(30f, 16f, 38f, 10f, 2.6f, rope);                    // хвост
        p.Line(38f, 10f, 44f, 14f, 3f, Steel);                     // крюк
        p.Line(44f, 14f, 40f, 18f, 3f, Steel);
        return p;
    }

    /// Телепорт: воронка из витков и стрелка внутрь.
    static Pix TeleportIcon()
    {
        var p = new Pix(S, S);
        var far = new Color32(96, 58, 168, 255);
        var near = new Color32(178, 130, 250, 255);

        for (int a = 0; a < 130; a++)
        {
            float t = a / 130f;
            float ang = t * Mathf.PI * 5f;
            float r = 20f * (1f - t);
            p.Disc(24f + Mathf.Cos(ang) * r, 24f + Mathf.Sin(ang) * r, 2.6f,
                   Color32.Lerp(far, near, t));
        }

        p.Line(24f, 44f, 24f, 30f, 3f, new Color32(240, 232, 255, 255));
        p.Line(24f, 30f, 19f, 35f, 3f, new Color32(240, 232, 255, 255));
        p.Line(24f, 30f, 29f, 35f, 3f, new Color32(240, 232, 255, 255));
        return p;
    }
}
