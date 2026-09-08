using UnityEngine;

/// Крохотный растровый холст: на нём рисуются все спрайты, которых в проекте
/// нет файлами — деревья, облака, горы, иконки оружия. Ноль бинарных ассетов
/// остаётся правилом проекта, поэтому кисти живут в коде.
public class Pix
{
    public readonly int W, H;
    readonly Color32[] _px;

    public Pix(int w, int h)
    {
        W = w; H = h;
        _px = new Color32[w * h];
    }

    public static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    public void Set(int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        _px[y * W + x] = c;
    }

    public Color32 Get(int x, int y)
        => (x < 0 || y < 0 || x >= W || y >= H) ? Clear : _px[y * W + x];

    /// Смешивание по альфе — им кладутся тени и блики поверх готового.
    public void Blend(int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        if (c.a == 255) { _px[y * W + x] = c; return; }
        var d = _px[y * W + x];
        float a = c.a / 255f;
        float da = d.a / 255f;
        float outA = a + da * (1f - a);
        if (outA <= 0f) { _px[y * W + x] = Clear; return; }
        byte Mix(byte s, byte t) => (byte)((s * a + t * da * (1f - a)) / outA);
        _px[y * W + x] = new Color32(Mix(c.r, d.r), Mix(c.g, d.g), Mix(c.b, d.b), (byte)(outA * 255f));
    }

    public void Fill(Color32 c) { for (int i = 0; i < _px.Length; i++) _px[i] = c; }

    public void Rect(int x0, int y0, int w, int h, Color32 c)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++) Set(x, y, c);
    }

    public void Disc(float cx, float cy, float r, Color32 c)
    {
        int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
        int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r2) Set(x, y, c);
        }
    }

    /// Круг с мягким краем: облака и свет от кристаллов.
    public void Soft(float cx, float cy, float r, Color32 c, float feather = 0.45f)
    {
        int x0 = Mathf.FloorToInt(cx - r), x1 = Mathf.CeilToInt(cx + r);
        int y0 = Mathf.FloorToInt(cy - r), y1 = Mathf.CeilToInt(cy + r);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
            if (d > 1f) continue;
            float a = Mathf.Clamp01((1f - d) / Mathf.Max(0.001f, feather));
            Blend(x, y, new Color32(c.r, c.g, c.b, (byte)(c.a * a)));
        }
    }

    public void Line(float x0, float y0, float x1, float y1, float thick, Color32 c)
    {
        int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2f) + 1;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Disc(Mathf.Lerp(x0, x1, t), Mathf.Lerp(y0, y1, t), thick * 0.5f, c);
        }
    }

    /// Обвести непрозрачные пиксели снаружи — тёмный контур, как в спрайтах Worms.
    public void Outline(Color32 c)
    {
        var edge = new bool[W * H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (Get(x, y).a != 0) continue;
            if (Get(x - 1, y).a != 0 || Get(x + 1, y).a != 0 ||
                Get(x, y - 1).a != 0 || Get(x, y + 1).a != 0) edge[y * W + x] = true;
        }
        for (int i = 0; i < edge.Length; i++) if (edge[i]) _px[i] = c;
    }

    public Texture2D ToTexture(FilterMode filter = FilterMode.Point)
    {
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp
        };
        tex.SetPixels32(_px);
        tex.Apply();
        return tex;
    }

    /// Спрайт с пивотом в основании (деревья и кактусы ставятся на землю)
    /// либо в центре (облака, горы).
    public Sprite ToSprite(float pixelsPerUnit, bool pivotAtFoot = false, FilterMode filter = FilterMode.Point)
        => ToSprite(pixelsPerUnit, pivotAtFoot ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f), filter);

    /// Пивот произвольной точкой — тому, что растёт из своего основания, а не
    /// стоит на земле: полосе силы, которая тянется от червя вперёд.
    public Sprite ToSprite(float pixelsPerUnit, Vector2 pivot, FilterMode filter = FilterMode.Point)
    {
        var tex = ToTexture(filter);
        return Sprite.Create(tex, new Rect(0, 0, W, H), pivot, pixelsPerUnit, 0, SpriteMeshType.FullRect);
    }
}
