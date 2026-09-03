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
            _ => TeleportIcon()
        };

        pix.Outline(Ink);
        tex = pix.ToTexture();
        Cache[kind] = tex;
        return tex;
    }

    /// Ракета носом вверх-вправо, с оперением и огоньком в сопле.
    static Pix Bazooka()
    {
        var p = new Pix(S, S);
        var red = new Color32(198, 58, 46, 255);
        var redLit = new Color32(238, 112, 92, 255);

        // Корпус по диагонали и конус носа.
        p.Line(12f, 12f, 34f, 34f, 11f, Steel);
        p.Line(11f, 14f, 31f, 34f, 3f, SteelLit);
        for (int i = 0; i < 12; i++)
            p.Disc(34f + i * 0.6f, 34f + i * 0.6f, 5.5f - i * 0.42f, i < 4 ? red : redLit);

        // Оперение и выхлоп.
        p.Line(14f, 10f, 8f, 16f, 4f, Steel);
        p.Line(10f, 14f, 16f, 8f, 4f, Steel);
        p.Disc(9f, 9f, 4f, new Color32(252, 196, 64, 255));
        p.Disc(6f, 6f, 2.6f, new Color32(248, 128, 40, 255));
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


    /// Самонаводящаяся: ракета с носовым конусом и кольцом захвата.
    static Pix Homing()
    {
        var p = new Pix(S, S);
        var body = new Color32(232, 128, 44, 255);
        var lit = new Color32(252, 186, 112, 255);

        p.Line(10f, 18f, 32f, 30f, 10f, body);
        p.Line(10f, 20f, 30f, 31f, 3f, lit);
        for (int i = 0; i < 10; i++)
            p.Disc(32f + i * 0.7f, 30f + i * 0.38f, 5f - i * 0.42f, new Color32(226, 66, 52, 255));

        p.Line(12f, 15f, 7f, 20f, 4f, Steel);
        p.Disc(8f, 15f, 3.6f, new Color32(252, 196, 64, 255));

        // Кольцо захвата у носа.
        var ring = new Color32(120, 230, 140, 255);
        for (int a = 0; a < 26; a++)
        {
            float t = a / 26f * Mathf.PI * 2f;
            p.Disc(40f + Mathf.Cos(t) * 6f, 36f + Mathf.Sin(t) * 6f, 1.2f, ring);
        }
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
