using System.IO;
using UnityEditor;
using UnityEngine;

/// Контактный лист поз: строка — поза, столбец — кадр. Нужен затем же, зачем
/// WorldShots для миров: анимацию нельзя проверить логикой, её надо увидеть.
/// Кадры строятся из кода, PlayMode и графический контекст не нужны.
/// Запуск: меню Worms → Лист анимации или Unity -executeMethod AnimShots.Run
public static class AnimShots
{
    static string Dir => System.Environment.GetEnvironmentVariable("WORMS_SHOT_DIR") ?? "/tmp";

    const int Cell = 44;      // клетка чуть больше кадра 40×40 — чтобы был зазор
    const int Zoom = 3;       // червь высотой 40 px на глаз не читается

    [MenuItem("Worms/Лист анимации")]
    public static void Run()
    {
        var poses = (WormPose[])System.Enum.GetValues(typeof(WormPose));
        int cols = 1;
        foreach (var p in poses) cols = Mathf.Max(cols, WormSprite.FrameCount(p));

        int w = cols * Cell, h = poses.Length * Cell;
        var sheet = new Color32[w * h];

        var bg = new Color32(28, 30, 38, 255);
        var alt = new Color32(38, 41, 51, 255);
        var team = new Color(0.42f, 0.78f, 0.45f);   // цвет команды, как в бою

        for (int i = 0; i < sheet.Length; i++) sheet[i] = bg;

        for (int row = 0; row < poses.Length; row++)
        {
            WormSprite.Frames(poses[row], out var body, out var face);

            // Через строку — подложка посветлее: иначе на листе не видно,
            // где кончается одна поза и начинается другая.
            int band = (poses.Length - 1 - row) * Cell;
            if (row % 2 == 1)
                for (int y = 0; y < Cell; y++)
                for (int x = 0; x < w; x++)
                    sheet[(band + y) * w + x] = alt;

            for (int col = 0; col < body.Length; col++)
            {
                var bp = body[col].texture.GetPixels32();
                var fp = face[col].texture.GetPixels32();
                int ox = col * Cell + 2, oy = band + 2;

                for (int y = 0; y < 40; y++)
                for (int x = 0; x < 40; x++)
                {
                    var b = bp[y * 40 + x];
                    var f = fp[y * 40 + x];
                    int idx = (oy + y) * w + ox + x;
                    if (idx < 0 || idx >= sheet.Length) continue;

                    var dst = (Color)(Color32)sheet[idx];
                    if (b.a > 0) dst = Color.Lerp(dst, (Color)b * team, b.a / 255f);
                    if (f.a > 0) dst = Color.Lerp(dst, (Color)f, f.a / 255f);
                    sheet[idx] = dst;
                }
            }
        }

        // Увеличение целыми пикселями: сглаживание тут врало бы про рисунок.
        int zw = w * Zoom, zh = h * Zoom;
        var big = new Color32[zw * zh];
        for (int y = 0; y < zh; y++)
        for (int x = 0; x < zw; x++)
            big[y * zw + x] = sheet[(y / Zoom) * w + x / Zoom];

        var tex = new Texture2D(zw, zh, TextureFormat.RGBA32, false);
        tex.SetPixels32(big);
        tex.Apply();

        string path = Path.Combine(Dir, "worm-anim.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        Debug.Log($"ANIMSHOT: {path} ({zw}×{zh}, поз {poses.Length}, кадров до {cols})");
        Aim();
        Blast();
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    /// Лестница прицела отдельным листом: ступень корпуса и ствол в руках.
    /// Ствол ставится той же WormAnimator.GunOffset, что и в бою, — иначе лист
    /// показывал бы не то, что видит игрок.
    const int AimCell = 76;      // ствол выносится за габарит червя, нужен запас
    const float WormPpu = 34f;   // как в WormSprite
    const float IconPpu = 60f;   // как в WeaponIcons

    static void Aim()
    {
        int steps = WormSprite.AimSteps;
        int w = steps * AimCell, h = AimCell;
        var sheet = new Color32[w * h];
        var bg = new Color32(28, 30, 38, 255);
        var team = new Color(0.42f, 0.78f, 0.45f);
        for (int i = 0; i < sheet.Length; i++) sheet[i] = bg;

        WormSprite.Frames(WormPose.Aim, out var body, out var face);
        var icon = WeaponIcons.Get(WeaponKind.Bazooka);

        for (int step = 0; step < steps; step++)
        {
            float angle = Mathf.Lerp(-85f, 85f, step / (float)(steps - 1));
            int ox = step * AimCell + AimCell / 2, oy = AimCell / 2;

            Blit(sheet, w, body[step].texture, face[step].texture, ox, oy, team);

            // Та же точка, что и в игре, только в пикселях листа.
            var off = WormAnimator.GunOffset(angle) * WormPpu;
            // Тот же масштаб, что и в руках у червя (WormAnimator.HeldScale).
            Rotated(sheet, w, icon, ox + off.x, oy + off.y, angle - WeaponIcons.Lean(WeaponKind.Bazooka), WormPpu / IconPpu * WormAnimator.HeldScale);
        }

        const int zoom = 3;
        var tex = Grow(sheet, w, h, zoom, out int zw, out int zh);
        string path = Path.Combine(Dir, "worm-aim.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"ANIMSHOT: {path} ({zw}×{zh}, ступеней {steps})");
    }

    /// Лист взрыва: строка вспышки, строка дыма и искра. Смотреть на них
    /// поодиночке бесполезно — важно, что вспышка растёт, а комки дыма разные.
    const int BlastCell = 72;

    static void Blast()
    {
        var flash = BlastSprite.Flash;
        var puff = BlastSprite.Puff;

        int cols = Mathf.Max(flash.Length, puff.Length);
        int w = cols * BlastCell, h = 3 * BlastCell;
        var sheet = new Color32[w * h];
        var bg = new Color32(28, 30, 38, 255);
        for (int i = 0; i < sheet.Length; i++) sheet[i] = bg;

        // Строки сверху вниз: вспышка, дым, искра. Начало координат текстуры
        // внизу, поэтому верхняя строка — последняя по y.
        for (int i = 0; i < flash.Length; i++)
            Stamp(sheet, w, h, flash[i].texture, i * BlastCell + BlastCell / 2, BlastCell * 2 + BlastCell / 2);
        for (int i = 0; i < puff.Length; i++)
            Stamp(sheet, w, h, puff[i].texture, i * BlastCell + BlastCell / 2, BlastCell + BlastCell / 2);
        Stamp(sheet, w, h, BlastSprite.Spark.texture, BlastCell / 2, BlastCell / 2);

        var tex = Grow(sheet, w, h, 3, out int zw, out int zh);
        string path = Path.Combine(Dir, "blast.png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        Debug.Log($"ANIMSHOT: {path} ({zw}×{zh}, вспышка {flash.Length}, дым {puff.Length})");
    }

    /// Кадр как есть, по центру клетки: цвет команды тут ни при чём.
    static void Stamp(Color32[] sheet, int w, int h, Texture2D tex, int cx, int cy)
    {
        var px = tex.GetPixels32();
        for (int y = 0; y < tex.height; y++)
        for (int x = 0; x < tex.width; x++)
        {
            int tx = cx - tex.width / 2 + x, ty = cy - tex.height / 2 + y;
            if (tx < 0 || ty < 0 || tx >= w || ty >= h) continue;

            var c = px[y * tex.width + x];
            if (c.a == 0) continue;
            int idx = ty * w + tx;
            sheet[idx] = Color.Lerp((Color)(Color32)sheet[idx], (Color)c, c.a / 255f);
        }
    }

    /// Червь в клетку: тело в цвете команды, черты поверх.
    static void Blit(Color32[] sheet, int w, Texture2D body, Texture2D face, int cx, int cy, Color team)
    {
        var bp = body.GetPixels32();
        var fp = face.GetPixels32();
        int n = body.width;
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            int px = cx - n / 2 + x, py = cy - n / 2 + y;
            int idx = py * w + px;
            if (px < 0 || py < 0 || idx < 0 || idx >= sheet.Length) continue;

            var b = bp[y * n + x];
            var f = fp[y * n + x];
            var dst = (Color)(Color32)sheet[idx];
            if (b.a > 0) dst = Color.Lerp(dst, (Color)b * team, b.a / 255f);
            if (f.a > 0) dst = Color.Lerp(dst, (Color)f, f.a / 255f);
            sheet[idx] = dst;
        }
    }

    /// Иконка оружия, повёрнутая и уменьшенная под масштаб червя. Идём от
    /// пикселя листа к пикселю иконки: так не остаётся дырок от округления.
    static void Rotated(Color32[] sheet, int w, Texture2D icon, float cx, float cy, float angle, float scale)
    {
        var px = icon.GetPixels32();
        int n = icon.width, m = icon.height;
        float rad = -angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
        int reach = Mathf.CeilToInt(Mathf.Max(n, m) * scale * 0.75f);

        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            float u = (dx * cos - dy * sin) / scale + n * 0.5f;
            float v = (dx * sin + dy * cos) / scale + m * 0.5f;
            int sx = Mathf.FloorToInt(u), sy = Mathf.FloorToInt(v);
            if (sx < 0 || sy < 0 || sx >= n || sy >= m) continue;

            var c = px[sy * n + sx];
            if (c.a == 0) continue;

            int tx = Mathf.RoundToInt(cx) + dx, ty = Mathf.RoundToInt(cy) + dy;
            int idx = ty * w + tx;
            if (tx < 0 || ty < 0 || tx >= w || idx < 0 || idx >= sheet.Length) continue;
            sheet[idx] = Color.Lerp((Color)(Color32)sheet[idx], (Color)c, c.a / 255f);
        }
    }

    static Texture2D Grow(Color32[] sheet, int w, int h, int zoom, out int zw, out int zh)
    {
        zw = w * zoom; zh = h * zoom;
        var big = new Color32[zw * zh];
        for (int y = 0; y < zh; y++)
        for (int x = 0; x < zw; x++)
            big[y * zw + x] = sheet[(y / zoom) * w + x / zoom];

        var tex = new Texture2D(zw, zh, TextureFormat.RGBA32, false);
        tex.SetPixels32(big);
        tex.Apply();
        return tex;
    }
}
