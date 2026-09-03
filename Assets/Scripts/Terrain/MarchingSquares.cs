using System.Collections.Generic;
using UnityEngine;

/// Построение контуров (замкнутых петель) по булевой сетке методом marching squares.
/// Используется для пересборки PolygonCollider2D разрушаемого ландшафта.
public static class MarchingSquares
{
    struct Seg { public long A, B; }

    static long Key(int x2, int y2) => ((long)(x2 + 4) << 24) | (uint)(y2 + 4);

    public static List<List<Vector2>> Build(bool[] grid, int gw, int gh, Vector2 origin, float cell, float simplifyEps)
    {
        var segs = new List<Seg>(1024);

        bool At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= gw || y >= gh) return false;
            return grid[y * gw + x];
        }

        // Идём по клеткам с запасом в одну клетку, чтобы контуры на краю сетки замыкались.
        for (int y = -1; y < gh; y++)
        for (int x = -1; x < gw; x++)
        {
            bool bl = At(x, y), br = At(x + 1, y), tr = At(x + 1, y + 1), tl = At(x, y + 1);
            int idx = (tl ? 8 : 0) | (tr ? 4 : 0) | (br ? 2 : 0) | (bl ? 1 : 0);
            if (idx == 0 || idx == 15) continue;

            int x2 = x * 2, y2 = y * 2;
            long L = Key(x2, y2 + 1);
            long R = Key(x2 + 2, y2 + 1);
            long T = Key(x2 + 1, y2 + 2);
            long B = Key(x2 + 1, y2);

            switch (idx)
            {
                case 1:  segs.Add(new Seg { A = L, B = B }); break;
                case 2:  segs.Add(new Seg { A = B, B = R }); break;
                case 3:  segs.Add(new Seg { A = L, B = R }); break;
                case 4:  segs.Add(new Seg { A = R, B = T }); break;
                case 5:  segs.Add(new Seg { A = L, B = T }); segs.Add(new Seg { A = R, B = B }); break;
                case 6:  segs.Add(new Seg { A = B, B = T }); break;
                case 7:  segs.Add(new Seg { A = L, B = T }); break;
                case 8:  segs.Add(new Seg { A = T, B = L }); break;
                case 9:  segs.Add(new Seg { A = T, B = B }); break;
                case 10: segs.Add(new Seg { A = T, B = R }); segs.Add(new Seg { A = B, B = L }); break;
                case 11: segs.Add(new Seg { A = T, B = R }); break;
                case 12: segs.Add(new Seg { A = R, B = L }); break;
                case 13: segs.Add(new Seg { A = R, B = B }); break;
                case 14: segs.Add(new Seg { A = B, B = L }); break;
            }
        }

        // Индекс: точка старта -> индексы сегментов.
        var byStart = new Dictionary<long, List<int>>(segs.Count);
        for (int i = 0; i < segs.Count; i++)
        {
            if (!byStart.TryGetValue(segs[i].A, out var list))
            {
                list = new List<int>(2);
                byStart[segs[i].A] = list;
            }
            list.Add(i);
        }

        var used = new bool[segs.Count];
        var loops = new List<List<Vector2>>();

        for (int i = 0; i < segs.Count; i++)
        {
            if (used[i]) continue;

            var keys = new List<long>(64);
            int cur = i;
            long startKey = segs[i].A;
            int guard = 0;

            while (cur >= 0 && !used[cur] && guard++ < segs.Count + 4)
            {
                used[cur] = true;
                keys.Add(segs[cur].A);
                long next = segs[cur].B;
                if (next == startKey) break;

                int nextSeg = -1;
                if (byStart.TryGetValue(next, out var cand))
                {
                    for (int c = 0; c < cand.Count; c++)
                        if (!used[cand[c]]) { nextSeg = cand[c]; break; }
                }
                cur = nextSeg;
            }

            if (keys.Count < 3) continue;

            var pts = new List<Vector2>(keys.Count);
            for (int k = 0; k < keys.Count; k++)
            {
                long key = keys[k];
                int kx = (int)(key >> 24) - 4;
                int ky = (int)(key & 0xFFFFFF) - 4;
                pts.Add(origin + new Vector2(kx * 0.5f * cell, ky * 0.5f * cell));
            }

            var simplified = Simplify(pts, simplifyEps);
            if (simplified.Count >= 3) loops.Add(simplified);
        }

        return loops;
    }

    /// Выкидываем точки, лежащие почти на одной прямой с соседями.
    static List<Vector2> Simplify(List<Vector2> pts, float eps)
    {
        if (pts.Count < 4 || eps <= 0f) return pts;
        var outPts = new List<Vector2>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 prev = outPts.Count > 0 ? outPts[outPts.Count - 1] : pts[(i - 1 + pts.Count) % pts.Count];
            Vector2 cur = pts[i];
            Vector2 next = pts[(i + 1) % pts.Count];
            Vector2 a = cur - prev, b = next - cur;
            float cross = Mathf.Abs(a.x * b.y - a.y * b.x);
            if (cross > eps || a.sqrMagnitude < 1e-8f) outPts.Add(cur);
        }
        return outPts.Count >= 3 ? outPts : pts;
    }
}
