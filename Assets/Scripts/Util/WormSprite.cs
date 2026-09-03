using UnityEngine;

/// Червь вместо шарика. Как и всё остальное в проекте, рисуется кодом:
/// два слоя на одном холсте Pix — тело в оттенках серого, которое красит
/// SpriteRenderer в цвет команды, и поверх него черты (контур, глаза, рот),
/// которым цвет команды не нужен и вреден.
public static class WormSprite
{
    const int S = 40;          // сторона холста в пикселях
    const float Ppu = 34f;     // 40 px / 34 ≈ 1,18 юнита в высоту

    static Sprite _body, _face;

    /// Тело: капсула с головой и хвостиком, светлая, чтобы умножение на цвет
    /// команды не превращало червя в силуэт.
    public static Sprite Body => _body != null ? _body : (_body = BuildBody().ToSprite(Ppu));

    /// Черты поверх тела: контур, глаза, зрачки, рот, блик.
    public static Sprite Face => _face != null ? _face : (_face = BuildFace().ToSprite(Ppu));

    static readonly Color32 Base = new Color32(226, 226, 226, 255);
    static readonly Color32 Lit = new Color32(255, 255, 255, 255);
    static readonly Color32 Dim = new Color32(178, 178, 178, 255);
    static readonly Color32 Ink = new Color32(20, 16, 22, 255);

    static Pix BuildBody()
    {
        var p = new Pix(S, S);

        // Хвост загибается влево-вниз, тело идёт вверх, сверху голова пошире.
        p.Disc(7.5f, 9f, 3.4f, Base);
        p.Line(8f, 8.5f, 15f, 8.5f, 6.5f, Base);
        p.Line(19f, 11f, 19f, 20f, 15f, Base);
        p.Disc(19f, 25f, 10.5f, Base);

        // Тень снизу и по правому боку — объём без единого градиента.
        p.Line(19f, 10f, 19f, 14f, 17f, Dim);
        for (int y = 0; y < S; y++)
        for (int x = 25; x < S; x++)
            if (p.Get(x, y).a != 0) p.Set(x, y, Dim);

        // Блик на затылке слева.
        p.Soft(14f, 29f, 5.5f, Lit, 0.9f);
        p.Soft(12f, 18f, 3.5f, Lit, 1f);
        return p;
    }

    static Pix BuildFace()
    {
        var body = BuildBody();
        var p = new Pix(S, S);

        // Контур снимаем с тела: рисовать его в самом теле нельзя — цвет команды
        // выкрасил бы и его, и червь потерял бы обводку на тёмной палитре.
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            if (body.Get(x, y).a != 0) continue;
            if (body.Get(x - 1, y).a != 0 || body.Get(x + 1, y).a != 0 ||
                body.Get(x, y - 1).a != 0 || body.Get(x, y + 1).a != 0) p.Set(x, y, Ink);
        }

        var white = new Color32(250, 250, 252, 255);
        Eye(p, 15.5f, 26f, white);
        Eye(p, 22.5f, 26f, white);

        // Рот — короткий тонкий штрих под глазами, ближе к морде.
        p.Line(19.5f, 20.5f, 23.5f, 20f, 1.4f, Ink);
        return p;
    }

    /// Глаз: белок, зрачок смещён вперёд и вниз, сверху тяжёлое веко —
    /// от него взгляд получается не рыбий, а исподлобья, как в оригинале.
    static void Eye(Pix p, float cx, float cy, Color32 white)
    {
        p.Disc(cx, cy, 4f, white);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > 16f) continue;
            if (dy > 1.6f) p.Set(x, y, Ink);
        }
        p.Disc(cx + 1.2f, cy - 0.8f, 2f, Ink);
    }
}
