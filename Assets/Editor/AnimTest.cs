using System.Text;
using UnityEditor;
using UnityEngine;

/// Проверка проигрывателя поз (фаза 13a) и кромки воды (13e). Как и WorldTest, гоняется вне
/// PlayMode: клипы строятся из кода, а выбор позы — чистая функция от
/// состояния червя, так что ни сцены, ни физики тут не нужно.
/// Проверяем три вещи: каждому состоянию достаётся клип, у клипа непустые
/// ряды кадров одной длины, повторный запрос кадров не строит их заново;
/// и отдельно — что головка проигрывателя доходит до конца, зовёт событие
/// и по-разному ведёт себя у петли и у одиночного клипа.
/// Запуск: меню Worms → Тест анимации или Unity -executeMethod AnimTest.Run
public static class AnimTest
{
    static readonly StringBuilder Log = new StringBuilder();
    static int _fails;

    [MenuItem("Worms/Тест анимации")]
    public static void Run()
    {
        _fails = 0;
        Log.Clear();

        Clips();
        Cache();
        Distinct();
        States();
        Aiming();
        Playback();
        Blast();
        Waves();

        Debug.Log(Log.ToString());
        Debug.Log(_fails == 0 ? "ANIM: OK" : $"ANIM: ПРОВАЛОВ {_fails}");
        if (Application.isBatchMode) EditorApplication.Exit(_fails == 0 ? 0 : 1);
    }

    static void Fail(string what)
    {
        _fails++;
        Log.Append("  ПРОВАЛ: " + what + "\n");
    }

    static WormPose[] AllPoses => (WormPose[])System.Enum.GetValues(typeof(WormPose));

    /// Кромка воды (13е): профиль обеих волн сшит по краям — иначе на стыке
    /// плиток по ленте бежал бы вертикальный шов; полосы кэшируются, как кадры
    /// червя; ближняя и дальняя волна не совпадают, иначе бег двух лент
    /// читался бы как одна ползущая картинка.
    static void Waves()
    {
        for (int layer = 0; layer < 2; layer++)
        {
            float a = Water.Wave(layer, 0f), b = Water.Wave(layer, 1f);
            if (Mathf.Abs(a - b) > 1e-3f)
                Fail($"волна {layer}: край {a:F3} не сошёлся с краем {b:F3} — на стыке плиток будет шов");

            var s1 = Water.Strip(layer);
            var s2 = Water.Strip(layer);
            if (s1 == null) { Fail($"волна {layer}: полоса не построилась"); continue; }
            if (!ReferenceEquals(s1, s2)) Fail($"волна {layer}: полоса строится заново при каждом запросе");
        }

        if (!ReferenceEquals(Water.Foam, Water.Foam)) Fail("пена строится заново при каждом запросе");

        // Волны обязаны разойтись хотя бы где-то на четверть амплитуды.
        float far = 0f;
        for (int i = 0; i <= 64; i++)
        {
            float u = i / 64f;
            far = Mathf.Max(far, Mathf.Abs(Water.Wave(0, u) - Water.Wave(1, u)));
        }
        if (far < 0.25f) Fail($"ближняя и дальняя волна почти совпали: расхождение {far:F3}");

        // Полоса не может быть ни пустой, ни залитой целиком: и то и другое
        // означало бы прямую кромку — ровно то, что фаза 13e убирает.
        var tex = Water.Strip(0).texture;
        var px = tex.GetPixels32();
        int solid = 0;
        for (int i = 0; i < px.Length; i++) if (px[i].a > 0) solid++;
        float part = solid / (float)px.Length;
        if (part < 0.2f || part > 0.8f) Fail($"полоса волны залита на {part:P0} — кромки в ней нет");

        Log.Append($"  волны: полос 3, залито {part:P0}, расхождение {far:F2}\n");
    }

    /// У каждой позы есть клип, в нём есть кадры, тело и лицо одной длины
    /// и без дырок — разъехавшийся номер дал бы червя с чужим лицом.
    static void Clips()
    {
        foreach (var pose in AllPoses)
        {
            var clip = WormAnimator.ClipFor(pose);
            if (clip == null) { Fail($"поза {pose} без клипа"); continue; }

            if (clip.Frames == 0) { Fail($"клип {pose} пуст"); continue; }
            if (clip.Body.Length != clip.Face.Length)
                Fail($"клип {pose}: тело {clip.Body.Length} кадров, лицо {clip.Face.Length}");
            if (clip.Fps <= 0f) Fail($"клип {pose}: частота {clip.Fps}");

            for (int i = 0; i < clip.Frames; i++)
            {
                if (clip.Body[i] == null) Fail($"клип {pose}: кадр тела {i} пуст");
                if (i < clip.Face.Length && clip.Face[i] == null) Fail($"клип {pose}: кадр лица {i} пуст");
            }

            // Одиночными идут ровно те позы, из которых червь обязан вернуться
            // сам: отрыв, приземление, боль, удар и прощание. Прицел — не петля
            // по другой причине: его кадр выбирает угол, а не время.
            bool once = pose == WormPose.Jump || pose == WormPose.Land || pose == WormPose.Hurt
                     || pose == WormPose.Swing || pose == WormPose.Bye || pose == WormPose.Aim;
            if (clip.Loop == once) Fail($"клип {pose}: петля {clip.Loop}, ожидалась {!once}");
        }

        Log.Append($"  клипов {AllPoses.Length}, кадров всего "
                   + Total() + "\n");
    }

    static int Total()
    {
        int n = 0;
        foreach (var pose in AllPoses) n += WormAnimator.ClipFor(pose).Frames;
        return n;
    }

    /// Кадры строятся один раз на запуск: второй запрос обязан вернуть те же
    /// массивы и те же спрайты, иначе Pix перерисовывал бы 40×40 у каждого
    /// червя в кадре.
    static void Cache()
    {
        foreach (var pose in AllPoses)
        {
            WormSprite.Frames(pose, out var b1, out var f1);
            WormSprite.Frames(pose, out var b2, out var f2);

            if (!ReferenceEquals(b1, b2) || !ReferenceEquals(f1, f2))
            { Fail($"кадры {pose} строятся заново при каждом запросе"); continue; }

            for (int i = 0; i < b1.Length; i++)
                if (!ReferenceEquals(b1[i], b2[i])) { Fail($"кадр {i} позы {pose} пересоздан"); break; }

            if (!ReferenceEquals(WormAnimator.ClipFor(pose), WormAnimator.ClipFor(pose)))
                Fail($"клип {pose} пересобирается");
        }
    }

    /// Кадры позы обязаны отличаться друг от друга: пока они повторяли одну
    /// картинку (фаза 13a), проигрыватель крутился вхолостую, и заметить это
    /// по логам было нельзя. Сравниваем пиксели тела — лицо снимает контур
    /// с него же, так что одинаковые тела означают одинаковые кадры.
    static void Distinct()
    {
        foreach (var pose in AllPoses)
        {
            WormSprite.Frames(pose, out var body, out _);
            if (body.Length < 2) continue;

            for (int i = 0; i < body.Length; i++)
            for (int j = i + 1; j < body.Length; j++)
                if (Same(body[i], body[j]))
                    Fail($"клип {pose}: кадры {i} и {j} совпадают пиксель в пиксель");
        }
    }

    static bool Same(Sprite a, Sprite b)
    {
        var pa = a.texture.GetPixels32();
        var pb = b.texture.GetPixels32();
        if (pa.Length != pb.Length) return false;
        for (int i = 0; i < pa.Length; i++)
            if (!pa[i].Equals(pb[i])) return false;
        return true;
    }

    static WormAnimState Ground(float speed = 0f)
        => new WormAnimState { Grounded = true, SpeedX = speed };

    /// Таблица «состояние червя → поза». Здесь же порядок приоритетов:
    /// вода важнее купола, воздух важнее боли, замороженный только стоит.
    static void States()
    {
        Expect(Ground(), WormPose.Idle, "стоит");
        Expect(Ground(2.5f), WormPose.Walk, "идёт");
        Expect(Ground(-2.5f), WormPose.Walk, "идёт влево");
        Expect(Ground(0.3f), WormPose.Idle, "сползает на волосок");

        var frozen = Ground(2.5f); frozen.Frozen = true;
        Expect(frozen, WormPose.Idle, "примёрзший не шагает");

        Expect(new WormAnimState { VelY = 6f }, WormPose.Jump, "оторвался");
        Expect(new WormAnimState { VelY = -7f }, WormPose.Fall, "падает");

        var land = Ground(); land.Landing = true;
        Expect(land, WormPose.Land, "приземлился");

        var hurt = Ground(2.5f); hurt.Hurt = true;
        Expect(hurt, WormPose.Hurt, "задело на ходу");

        var hurtAir = new WormAnimState { VelY = -7f, Hurt = true };
        Expect(hurtAir, WormPose.Fall, "задело в воздухе — всё равно падает");

        var chute = new WormAnimState { Chute = true, VelY = -3f };
        Expect(chute, WormPose.Chute, "под куполом");

        var jet = new WormAnimState { Jet = true, VelY = 4f };
        Expect(jet, WormPose.Jet, "на ранце");

        var wet = new WormAnimState { Underwater = true, Chute = true, Grounded = true };
        Expect(wet, WormPose.Drown, "вода важнее всего");

        // Позы, которых не было в 13b.
        var aim = Ground(); aim.Aiming = true;
        Expect(aim, WormPose.Aim, "целится стоя");

        var aimWalk = Ground(2.5f); aimWalk.Aiming = true;
        Expect(aimWalk, WormPose.Walk, "с оружием в руках всё равно сначала идёт");

        var roped = new WormAnimState { Roped = true, VelY = -7f };
        Expect(roped, WormPose.Rope, "на верёвке висит, а не падает");

        var dig = Ground(); dig.Digging = true;
        Expect(dig, WormPose.Dig, "роет");

        var swing = Ground(); swing.Swinging = true;
        Expect(swing, WormPose.Swing, "бьёт вплотную");

        var swingHurt = Ground(); swingHurt.Swinging = true; swingHurt.Hurt = true;
        Expect(swingHurt, WormPose.Hurt, "чужой удар важнее своего замаха");

        var bye = new WormAnimState { Saying = true, Underwater = true, Roped = true };
        Expect(bye, WormPose.Bye, "прощание перекрывает всё");
    }

    /// Прицел — единственная поза, где кадр выбирает угол, а не секундомер.
    /// Крайние углы обязаны дать крайние кадры, ноль — середину, а всё, что
    /// за пределами −85…85, прижаться к краю, а не выпасть за массив.
    static void Aiming()
    {
        int last = WormSprite.AimSteps - 1;
        Check(-85f, 0, "нижний угол");
        Check(85f, last, "верхний угол");
        Check(0f, last / 2, "горизонт");
        Check(-400f, 0, "угол ниже допустимого прижат к краю");
        Check(400f, last, "угол выше допустимого прижат к краю");

        // Ступени обязаны идти по порядку: перевёрнутая шкала дала бы червя,
        // который целится вверх, а корпусом клонится вниз.
        int prev = -1;
        for (float a = -85f; a <= 85f; a += 5f)
        {
            int step = WormSprite.AimStep(a);
            if (step < prev) { Fail($"ступени прицела идут не по порядку: {a}° дало {step} после {prev}"); break; }
            prev = step;
        }

        var clip = WormAnimator.ClipFor(WormPose.Aim);
        if (clip.Frames != WormSprite.AimSteps)
            Fail($"клип прицела: кадров {clip.Frames}, ступеней {WormSprite.AimSteps}");

        // Головка обязана уметь показать кадр, а не только проиграть его.
        var p = new SpriteAnimPlayer();
        p.Show(clip, last);
        if (p.Frame != last) Fail($"Show поставил кадр {p.Frame}, а не {last}");
        p.Show(clip, 99);
        if (p.Frame != last) Fail("Show не прижал номер кадра к последнему");
    }

    /// Кадры взрыва (13d). Проверяем то же, что и у поз: кадры есть, они
    /// непустые, отличаются друг от друга и строятся один раз на запуск.
    /// Отдельно — что вспышка растёт: кадр разгорания обязан быть уже кадра
    /// пика, иначе взрыв читался бы вспышкой одного размера.
    static void Blast()
    {
        var flash = BlastSprite.Flash;
        if (flash.Length < 2 || flash.Length > 3) Fail($"вспышка в {flash.Length} кадра, ожидалось два-три");

        int[] wide = new int[flash.Length];
        for (int i = 0; i < flash.Length; i++)
        {
            if (flash[i] == null) { Fail($"кадр вспышки {i} пуст"); continue; }
            wide[i] = Filled(flash[i]);
            if (wide[i] == 0) Fail($"кадр вспышки {i} прозрачен целиком");
        }
        if (wide[0] >= wide[1]) Fail($"разгорание не меньше пика: {wide[0]} против {wide[1]} пикселей");

        for (int i = 0; i < flash.Length; i++)
        for (int j = i + 1; j < flash.Length; j++)
            if (Same(flash[i], flash[j])) Fail($"кадры вспышки {i} и {j} совпадают");

        var puff = BlastSprite.Puff;
        if (puff.Length < 2) Fail("дыму нужен не один комок, иначе из точки идёт столбик одинаковых шариков");
        for (int i = 0; i < puff.Length; i++)
            if (puff[i] == null || Filled(puff[i]) == 0) Fail($"кадр дыма {i} пуст");
        for (int i = 0; i < puff.Length; i++)
        for (int j = i + 1; j < puff.Length; j++)
            if (Same(puff[i], puff[j])) Fail($"комки дыма {i} и {j} совпадают");

        if (BlastSprite.Spark == null || Filled(BlastSprite.Spark) == 0) Fail("искра пуста");

        // Кэш: кадры взрыва рождаются пачками по десятку на попадание, и
        // перерисовывать их каждый раз значило бы класть Pix в кадр боя.
        if (!ReferenceEquals(BlastSprite.Flash, BlastSprite.Flash)
            || !ReferenceEquals(BlastSprite.Puff, BlastSprite.Puff)
            || !ReferenceEquals(BlastSprite.Spark, BlastSprite.Spark))
            Fail("кадры взрыва строятся заново при каждом запросе");

        Log.Append($"  взрыв: вспышка {flash.Length} кадра, дым {puff.Length}, искра 1\n");
    }

    /// Сколько непрозрачных пикселей в кадре — им и меряем рост вспышки.
    static int Filled(Sprite s)
    {
        var px = s.texture.GetPixels32();
        int n = 0;
        for (int i = 0; i < px.Length; i++) if (px[i].a > 8) n++;
        return n;
    }

    static void Check(float angle, int want, string what)
    {
        int got = WormSprite.AimStep(angle);
        if (got != want) Fail($"{what}: ступень {got}, ожидалась {want}");
    }

    static void Expect(WormAnimState s, WormPose want, string what)
    {
        var got = WormAnimator.PoseFor(s);
        if (got != want) Fail($"{what}: поза {got}, ожидалась {want}");
    }

    /// Головка: одиночный клип доходит до последнего кадра, встаёт на нём и
    /// шлёт событие ровно раз; петля возвращается к началу и шлёт событие
    /// на каждом обороте.
    static void Playback()
    {
        var once = WormAnimator.ClipFor(WormPose.Land);
        var p = new SpriteAnimPlayer();
        int ends = 0;
        p.Ended += _ => ends++;
        p.Play(once);

        if (p.Frame != 0) Fail("одиночный клип начался не с первого кадра");

        float step = 1f / (once.Fps * 4f);
        for (float t = 0f; t < once.Length * 2f; t += step) p.Tick(step);

        if (!p.Done) Fail("одиночный клип не доигрался");
        if (p.Frame != once.Frames - 1) Fail($"одиночный клип встал на кадре {p.Frame}, а не последнем");
        if (ends != 1) Fail($"событие конца пришло {ends} раз, ожидался один");
        if (p.BodyFrame == null || p.FaceFrame == null) Fail("головка не отдаёт кадры");

        var loop = WormAnimator.ClipFor(WormPose.Walk);
        var q = new SpriteAnimPlayer();
        int laps = 0;
        q.Ended += _ => laps++;
        q.Play(loop);

        int seen = 0;
        step = 1f / (loop.Fps * 4f);
        for (float t = 0f; t < loop.Length * 2f + step; t += step)
        {
            q.Tick(step);
            seen = Mathf.Max(seen, q.Frame);
        }

        if (q.Done) Fail("петля объявила себя доигранной");
        if (seen != loop.Frames - 1) Fail($"петля дошла только до кадра {seen} из {loop.Frames - 1}");
        if (laps < 2) Fail($"петля сделала {laps} оборота, ожидалось два");
        if (q.Frame > 1) Fail($"петля не вернулась к началу: кадр {q.Frame}");

        // Тот же клип не перезапускается: иначе ходьба вечно сидела бы на
        // первом кадре — состояние червя пересчитывается каждый кадр.
        int before = q.Frame;
        q.Play(loop);
        if (q.Frame != before) Fail("повторный Play сбросил кадр");
        q.Play(loop, true);
        if (q.Frame != 0) Fail("Play с перезапуском не сбросил кадр");
    }
}
