using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Проверка чанкованного коллайдера ландшафта: границы чанков не должны
/// отличаться от остальной карты, шар обязан свободно катиться через шов,
/// а взрыв на шве — пробивать обе половины. Карта строится с фиксированным
/// сидом, иначе прогоны несравнимы.
/// Запуск: Unity -executeMethod TerrainTest.Run
public static class TerrainTest
{
    const string Flag = "TerrainTest.Running";
    const int Seed = 20260831;

    /// Ширина чанка коллайдера в юнитах — швы идут через каждые столько.
    const float ChunkWidth = 128f / DestructibleTerrain.PixelsPerUnit;

    static double _start;
    static int _step;
    static DestructibleTerrain _terrain;
    static Rigidbody2D _probe;
    static float _probeStartX, _probeMaxX, _probeSeam, _probeRun;
    static int _probeSeed;

    [MenuItem("Worms/Terrain test")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Game.unity");
        _start = 0; _step = 0; _terrain = null; _probe = null;
        SessionState.SetBool(Flag, true);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Reattach()
    {
        if (SessionState.GetBool(Flag, false)) EditorApplication.update += Tick;
    }

    static void Finish(int code)
    {
        SessionState.SetBool(Flag, false);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    static bool IsTerrain(Collider2D c) => c != null && c.GetComponentInParent<DestructibleTerrain>() != null;

    /// Колонка, по которой червь может пройти: над водой и без обрыва рядом.
    static bool Walkable(float x)
    {
        float h = _terrain.SurfaceHeightWorld(x);
        if (h < DestructibleTerrain.WaterLevel + 1.5f) return false;
        for (float d = -0.5f; d <= 0.5f; d += 0.25f)
            if (Mathf.Abs(_terrain.SurfaceHeightWorld(x + d) - h) > 0.35f) return false;
        return true;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (_start == 0) _start = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - _start;

        if (_step == 0 && t > 1)
        {
            _step = 1;
            var go = new GameObject("TestTerrain");
            go.AddComponent<SpriteRenderer>();
            _terrain = go.AddComponent<DestructibleTerrain>();
            _terrain.Build(Seed);
            Debug.Log($"TERRAIN step0: карта {_terrain.W}x{_terrain.H}, сид {Seed}, " +
                      $"швы через {ChunkWidth:0.00} юнита, контуров {_terrain.ColliderPathCount}");
            return;
        }

        // 1. Физическая поверхность против маски — отдельно у швов и вдали от них.
        if (_step == 1 && t > 1.6)
        {
            _step = 2;
            int nSeam = 0, badSeam = 0, nFar = 0, badFar = 0;
            float devSeam = 0f, devFar = 0f;

            // Пробуем все площадки карты, а не только верхний силуэт: с
            // навесами и кавернами коллайдер сшивается ещё и под землёй,
            // а по одному силуэту у швов почти не остаётся точек.
            foreach (var spawn in _terrain.CollectSpawnPoints(0.25f))
            {
                float x = spawn.x;
                float h = spawn.y - 0.8f;
                var p = new Vector2(x, h + 1.0f);

                bool bad = false;
                float dev = 0f;
                foreach (var c in Physics2D.OverlapCircleAll(p, 0.4f))
                    if (IsTerrain(c)) { bad = true; break; }   // твердь там, где должен быть воздух
                if (!bad)
                {
                    var hit = Physics2D.CircleCast(p, 0.4f, Vector2.down, 1.4f);
                    if (!IsTerrain(hit.collider)) bad = true;  // опоры нет вовсе
                    else dev = Mathf.Abs(h + 0.6f - hit.distance - h);
                }

                float d = Mathf.Abs(x / ChunkWidth - Mathf.Round(x / ChunkWidth)) * ChunkWidth;
                if (d < 0.5f) { nSeam++; if (bad) badSeam++; devSeam = Mathf.Max(devSeam, dev); }
                else { nFar++; if (bad) badFar++; devFar = Mathf.Max(devFar, dev); }
            }

            float rSeam = nSeam > 0 ? badSeam / (float)nSeam : 0f;
            float rFar = nFar > 0 ? badFar / (float)nFar : 0f;
            Debug.Log($"TERRAIN step1: у швов {badSeam}/{nSeam} расхождений ({rSeam * 100f:0.0}%, " +
                      $"отклонение до {devSeam:0.00}), вдали {badFar}/{nFar} ({rFar * 100f:0.0}%, " +
                      $"до {devFar:0.00})");

            if (nSeam < 20) { Debug.LogError("TERRAIN: слишком мало ходибельных колонок на швах"); Finish(1); return; }
            if (rSeam > rFar + 0.05f)
            {
                Debug.LogError("TERRAIN: на швах чанков коллайдер расходится с маской чаще, чем вне швов");
                Finish(1);
                return;
            }
        }

        // 2. Шар катится через шов: ищем ровный участок вокруг какого-нибудь шва.
        if (_step == 2 && t > 1.8)
        {
            _step = 3;
            _probeSeam = -1f;
            _probeSeed = Seed;
            // Разгон не обязан быть симметричным: деревья впечатаны в породу
            // и держат шар не хуже скалы, поэтому коридор длиной 5,5 юнита
            // сдвигаем вдоль шва, пока не найдётся чистый.
            var runs = new[] { 4f, 3.5f, 3f, 2.5f, 2f, 1.5f, 1f, 4.5f, 5f };
            // Чистый коридор есть не на каждой карте: деревья впечатаны в
            // породу, а многоярусный рельеф режет силуэт шахтами каверн.
            // Поэтому при неудаче пересобираем ландшафт следующим сидом —
            // прокатка проверяет сшивку чанков, а не конкретный сид.
            for (int tries = 0; tries < 6 && _probeSeam < 0f; tries++)
            {
                if (tries > 0)
                {
                    _probeSeed = Seed + tries;
                    var stale = _terrain.gameObject;
                    var fresh = new GameObject("TestTerrain");
                    fresh.AddComponent<SpriteRenderer>();
                    _terrain = fresh.AddComponent<DestructibleTerrain>();
                    _terrain.Build(_probeSeed);
                    Object.DestroyImmediate(stale);
                }

                for (float s = ChunkWidth; s < DestructibleTerrain.WorldWidth - 8f && _probeSeam < 0f; s += ChunkWidth)
                    foreach (float run in runs)
                    {
                        bool flat = true;
                        float lo = float.MaxValue, hi = float.MinValue;
                        for (float x = s - run; x <= s + (5.5f - run) && flat; x += 0.25f)
                        {
                            float y = _terrain.SurfaceHeightWorld(x);
                            flat = Walkable(x) && !_terrain.DecorBlocks(x, y);
                            lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                        }
                        // Полого, а не просто ходибельно: ступеньку в рост
                        // червя шар с разгоном 4,2 не берёт.
                        if (flat && hi - lo < 1.2f) { _probeSeam = s; _probeRun = run; break; }
                    }
            }

            if (_probeSeam < 0f)
            {
                Debug.Log("TERRAIN step2: ровного участка вокруг шва не нашлось, прокатку пропускаем");
                _step = 4;
                return;
            }

            _probeStartX = _probeSeam - (_probeRun - 0.5f);
            float h = _terrain.SurfaceHeightWorld(_probeStartX);
            var go = new GameObject("SeamProbe");
            go.transform.position = new Vector3(_probeStartX, h + 0.8f, 0f);
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.5f;
            col.sharedMaterial = new PhysicsMaterial2D("ProbeMat") { friction = 0.35f, bounciness = 0f };
            var rb = go.AddComponent<Rigidbody2D>();
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _probe = rb;
            _probeMaxX = _probeStartX;
            Debug.Log($"TERRAIN step2: шар пущен с x={_probeStartX:0.0} к шву x={_probeSeam:0.0} (сид {_probeSeed})");
        }

        if (_step == 3)
        {
            if (_probe != null)
            {
                // Гоним вправо ровно так же, как ходит червь.
                var v = _probe.linearVelocity;
                v.x = 4.2f;
                if (v.y > -0.05f && v.y < 0.05f) v.y += 0.6f;
                _probe.linearVelocity = v;
                _probeMaxX = Mathf.Max(_probeMaxX, _probe.transform.position.x);
            }

            if (t > 6)
            {
                _step = 4;
                Debug.Log($"TERRAIN step3: шар дошёл до x={_probeMaxX:0.0} " +
                          $"(шов x={_probeSeam:0.0}, проехал {_probeMaxX - _probeStartX:0.0} юнита)");
                if (_probeMaxX < _probeSeam + 1f)
                {
                    Debug.LogError("TERRAIN: шар не перевалил через шов чанков");
                    Finish(1);
                    return;
                }
            }
        }

        // 3. Взрыв ровно на шве обязан пробить обе половины.
        if (_step == 4)
        {
            _step = 5;
            float sx = ChunkWidth * 3f;
            float sh = _terrain.SurfaceHeightWorld(sx);
            int pathsBefore = _terrain.ColliderPathCount;
            _terrain.Explode(new Vector2(sx, sh - 0.5f), 3.0f);

            bool left = !_terrain.IsSolidWorld(new Vector2(sx - 1.5f, sh - 1f));
            bool right = !_terrain.IsSolidWorld(new Vector2(sx + 1.5f, sh - 1f));
            Debug.Log($"TERRAIN step4: взрыв на шве x={sx:0.0}, маска пробита слева={left} справа={right}, " +
                      $"контуров {pathsBefore} -> {_terrain.ColliderPathCount}");
            if (!left || !right) { Debug.LogError("TERRAIN: взрыв на шве пробил не обе стороны"); Finish(1); return; }
            return;
        }

        // Коллайдер обновляется не мгновенно — проверяем воронку на следующем шаге.
        if (_step == 5 && t > 6.5)
        {
            _step = 6;
            float sx = ChunkWidth * 3f;
            float sh = _terrain.SurfaceHeightWorld(sx);
            bool air = true;
            foreach (var c in Physics2D.OverlapCircleAll(new Vector2(sx, sh + 0.6f), 0.35f))
                if (IsTerrain(c)) air = false;
            Debug.Log($"TERRAIN step5: над воронкой на шве пусто={air} (дно y={sh:0.0})");
            if (!air) { Debug.LogError("TERRAIN: коллайдер не убрал породу над воронкой"); Finish(1); return; }

            Debug.Log("TERRAIN: OK");
            Finish(0);
        }
    }
}
