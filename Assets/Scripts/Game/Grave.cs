using UnityEngine;

/// Памятник на месте гибели червя. У каждой команды свой вид из десяти —
/// он выбирается на старте матча и больше не меняется, поэтому по кладбищу
/// видно, кого и где выбили. Как и всё в проекте, рисуется кодом на Pix:
/// камень отдельным спрайтом, накладка — отдельным, её и красит цвет команды.
public static class Grave
{
    public const int Kinds = 10;

    const int S = 32;          // сторона холста в пикселях
    const float Ppu = 24f;     // 32 px / 24 ≈ 1,33 юнита в высоту

    static readonly Sprite[] _stone = new Sprite[Kinds];
    static readonly Sprite[] _mark = new Sprite[Kinds];

    static readonly Color32 Stone = new Color32(198, 198, 206, 255);
    static readonly Color32 Dark = new Color32(146, 146, 158, 255);
    static readonly Color32 Lit = new Color32(232, 232, 240, 255);
    static readonly Color32 Wood = new Color32(148, 104, 58, 255);
    static readonly Color32 Earth = new Color32(96, 68, 40, 255);
    static readonly Color32 Ink = new Color32(22, 18, 24, 255);
    static readonly Color32 White = new Color32(255, 255, 255, 255);

    public static Sprite Stones(int kind)
    {
        kind = ((kind % Kinds) + Kinds) % Kinds;
        return _stone[kind] != null ? _stone[kind]
             : (_stone[kind] = BuildStone(kind).ToSprite(Ppu, true));
    }

    public static Sprite Mark(int kind)
    {
        kind = ((kind % Kinds) + Kinds) % Kinds;
        return _mark[kind] != null ? _mark[kind]
             : (_mark[kind] = BuildMark(kind).ToSprite(Ppu, true));
    }

    /// Ставит памятник на месте гибели: спускает его до ближайшей поверхности
    /// под точкой и вешает на корень мира, чтобы рестарт унёс и кладбище.
    public static GameObject Place(Team team, Vector2 pos)
    {
        if (GameManager.Root == null) return null;
        int kind = team != null ? team.GraveKind : 0;
        var color = team != null ? team.Color : Color.white;

        var go = new GameObject("Grave");
        GameManager.Attach(go);
        go.transform.position = pos;

        var stone = Sprites.Make("Stone", Stones(kind), Color.white, 8, go.transform);
        stone.transform.localPosition = Vector3.zero;
        var mark = Sprites.Make("Mark", Mark(kind), color, 9, go.transform);
        mark.transform.localPosition = Vector3.zero;

        go.AddComponent<GraveMarker>().Init();
        return go;
    }

    // --- рисование ---------------------------------------------------------

    static Pix BuildStone(int kind)
    {
        var p = new Pix(S, S);
        switch (kind)
        {
            case 0: Cross(p); break;
            case 1: Slab(p); break;
            case 2: Obelisk(p); break;
            case 3: Coffin(p); break;
            case 4: SkullStake(p); break;
            case 5: Urn(p); break;
            case 6: Pyramid(p); break;
            case 7: Anchor(p); break;
            case 8: Cairn(p); break;
            default: Mound(p); break;
        }
        p.Outline(Ink);
        return p;
    }

    /// Накладка команды: табличка, лента или флажок — своя форма у каждого вида.
    /// Рисуется белым, цвет даёт SpriteRenderer, поэтому оттенок ровно командный.
    static Pix BuildMark(int kind)
    {
        var p = new Pix(S, S);
        switch (kind)
        {
            case 0: p.Rect(11, 18, 11, 4, White); break;                       // лента на перекрестье
            case 1: p.Rect(11, 9, 11, 6, White); break;                        // табличка на плите
            case 2: p.Rect(12, 9, 9, 4, White); break;                         // пояс обелиска
            case 3: p.Rect(14, 12, 5, 11, White); p.Rect(11, 16, 11, 4, White); break;  // крест на крышке
            case 4: p.Rect(10, 22, 13, 3, White); break;                       // повязка на черепе
            case 5: p.Rect(11, 12, 11, 3, White); break;                       // поясок урны
            case 6: p.Disc(16f, 17f, 3.2f, White); break;                      // звезда на вершине
            case 7: p.Rect(13, 11, 7, 3, White); break;                        // муфта на стволе якоря
            case 8: p.Rect(11, 5, 11, 3, White); break;                        // плитка у подножия
            default: p.Rect(17, 19, 9, 6, White); break;                       // флажок на палке
        }
        return p;
    }

    static void Cross(Pix p)
    {
        p.Rect(8, 0, 17, 3, Earth);
        p.Rect(14, 2, 5, 26, Stone);
        p.Rect(8, 17, 17, 5, Stone);
        p.Rect(19, 2, 2, 26, Dark);
        p.Rect(14, 24, 3, 4, Lit);
    }

    static void Slab(Pix p)
    {
        p.Rect(7, 0, 19, 3, Earth);
        p.Rect(9, 2, 15, 20, Stone);
        p.Disc(16.5f, 22f, 7.5f, Stone);
        p.Rect(21, 2, 3, 22, Dark);
        p.Rect(11, 18, 4, 6, Lit);
    }

    static void Obelisk(Pix p)
    {
        p.Rect(8, 0, 17, 4, Earth);
        p.Rect(10, 3, 13, 3, Stone);
        for (int y = 6; y < 24; y++)
        {
            int half = Mathf.RoundToInt(Mathf.Lerp(5f, 3f, (y - 6) / 18f));
            p.Rect(16 - half, y, half * 2, 1, Stone);
        }
        for (int y = 24; y < 30; y++)
        {
            int half = Mathf.RoundToInt(Mathf.Lerp(3f, 0f, (y - 24) / 6f));
            p.Rect(16 - half, y, half * 2 + 1, 1, Stone);
        }
        p.Rect(19, 6, 2, 20, Dark);
    }

    static void Coffin(Pix p)
    {
        p.Rect(7, 0, 19, 3, Earth);
        // Шестиугольник: плечи на трети высоты, дальше сужение к ногам и к голове.
        for (int y = 2; y < 28; y++)
        {
            float k = (y - 2) / 26f;
            float half = k < 0.72f ? Mathf.Lerp(4.5f, 7.5f, k / 0.72f)
                                   : Mathf.Lerp(7.5f, 5f, (k - 0.72f) / 0.28f);
            int h = Mathf.RoundToInt(half);
            p.Rect(16 - h, y, h * 2, 1, Wood);
        }
        for (int y = 2; y < 28; y++) { p.Set(22, y, Earth); p.Set(23, y, Earth); }
    }

    static void SkullStake(Pix p)
    {
        p.Rect(9, 0, 15, 3, Earth);
        p.Rect(15, 2, 3, 16, Wood);
        p.Disc(16f, 22f, 6f, Stone);
        p.Rect(13, 15, 7, 3, Stone);
        p.Disc(13.8f, 23f, 1.9f, Ink);
        p.Disc(18.2f, 23f, 1.9f, Ink);
        p.Rect(15, 17, 1, 2, Ink);
        p.Rect(17, 17, 1, 2, Ink);
    }

    static void Urn(Pix p)
    {
        p.Rect(8, 0, 17, 4, Earth);
        p.Rect(11, 3, 11, 3, Stone);
        p.Disc(16f, 13f, 6.5f, Stone);
        p.Rect(13, 16, 7, 4, Stone);
        p.Rect(11, 19, 11, 3, Stone);
        p.Rect(19, 8, 3, 10, Dark);
        p.Disc(13f, 15f, 2f, Lit);
    }

    static void Pyramid(Pix p)
    {
        p.Rect(6, 0, 21, 3, Earth);
        for (int y = 2; y < 22; y++)
        {
            int half = Mathf.RoundToInt(Mathf.Lerp(10f, 1f, (y - 2) / 20f));
            p.Rect(16 - half, y, half * 2, 1, Stone);
            p.Rect(16 + half - 3, y, 3, 1, Dark);
        }
        p.Rect(15, 21, 3, 3, Stone);
    }

    static void Anchor(Pix p)
    {
        p.Rect(9, 0, 15, 3, Earth);
        p.Rect(15, 2, 3, 24, Stone);
        p.Rect(11, 20, 11, 3, Stone);
        p.Disc(16.5f, 27f, 3.6f, Stone);
        p.Disc(16.5f, 27f, 1.8f, Pix.Clear);
        // Лапы: две дуги от основания вверх-вбок.
        p.Line(16f, 4f, 9f, 9f, 2.4f, Stone);
        p.Line(16f, 4f, 23f, 9f, 2.4f, Stone);
        p.Rect(18, 4, 2, 20, Dark);
    }

    static void Cairn(Pix p)
    {
        p.Rect(6, 0, 21, 3, Earth);
        p.Disc(16f, 6f, 7.5f, Stone);
        p.Disc(13f, 14f, 5.5f, Stone);
        p.Disc(18f, 20f, 4.5f, Stone);
        p.Disc(16f, 25f, 3f, Stone);
        p.Rect(21, 3, 3, 8, Dark);
        p.Disc(11f, 15f, 1.6f, Lit);
    }

    static void Mound(Pix p)
    {
        p.Rect(3, 0, 27, 4, Earth);
        p.Disc(14f, 4f, 9f, Earth);
        p.Rect(5, 4, 18, 3, Earth);
        // Лопата воткнута в холм: черенок и полотно.
        p.Line(17f, 5f, 22f, 24f, 2f, Wood);
        p.Rect(19, 22, 6, 6, Stone);
        p.Disc(10f, 9f, 2.2f, Stone);
    }
}

/// Памятник не физический — он не должен мешать червям ходить и стрелять.
/// Поэтому раз в полсекунды он сам проверяет, есть ли под ним земля: воронка
/// съела опору — опускается следом, вода дошла — тонет со всплеском.
public class GraveMarker : MonoBehaviour
{
    const float FallSpeed = 9f;

    /// На сколько вода должна перекрыть подножие, чтобы памятник утонул.
    /// Полтора юнита — это ровно рост червя: памятник тому, кого вода достала
    /// на берегу, встал бы уже утопленником. Даём ему постоять в мелководье
    /// несколько ходов, пока потоп не накроет его целиком.
    const float Depth = 2.5f;

    float _check;
    int _gen;

    public void Init()
    {
        _gen = GameManager.I != null ? GameManager.I.Generation : 0;
        Snap(60f);
    }

    void Update()
    {
        if (GameManager.I == null || GameManager.I.Generation != _gen) { Destroy(gameObject); return; }

        _check -= Time.deltaTime;
        if (_check > 0f) return;
        _check = 0.5f;

        Snap(FallSpeed * 0.5f);

        // Памятник у самой кромки не топим: червь, которого догнала вода на
        // берегу, хоронится там же, и крест ещё торчит из мелководья. Уходит он
        // под воду, только когда потоп накроет его с головой.
        if (transform.position.y < DestructibleTerrain.WaterLevel - Depth)
        {
            Fx.Splash(new Vector2(transform.position.x, DestructibleTerrain.WaterLevel));
            Sfx.Splash();
            Destroy(gameObject);
        }
    }

    /// Опускает памятник до ближайшей поверхности, но не дальше чем на maxDrop:
    /// иначе после первой же воронки он телепортировался бы на дно карты.
    void Snap(float maxDrop)
    {
        Vector2 from = (Vector2)transform.position + Vector2.up * 0.4f;
        var hits = Physics2D.RaycastAll(from, Vector2.down, maxDrop + 0.4f);
        float best = float.MaxValue;
        float y = transform.position.y;
        foreach (var hit in hits)
        {
            // Черви, ящики и мины опорой не считаются — иначе памятник поедет
            // на чужой голове.
            var c = hit.collider;
            if (c == null || c.isTrigger) continue;
            if (c.GetComponent<Worm>() != null || c.GetComponent<Crate>() != null) continue;
            if (c.GetComponentInParent<Rigidbody2D>() != null) continue;
            if (hit.distance < best) { best = hit.distance; y = hit.point.y; }
        }

        if (best == float.MaxValue) y = transform.position.y - maxDrop;
        transform.position = new Vector3(transform.position.x, Mathf.Min(transform.position.y, y), 0f);
    }
}
