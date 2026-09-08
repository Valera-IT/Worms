using UnityEngine;

/// Червь вместо шарика. Как и всё остальное в проекте, рисуется кодом:
/// два слоя на одном холсте Pix — тело в оттенках серого, которое красит
/// SpriteRenderer в цвет команды, и поверх него черты (контур, глаза, рот),
/// которым цвет команды не нужен и вреден.
///
/// Кадры поз (фаза 13b) — это не отдельные рисунки, а один и тот же червь,
/// собранный по разным скелетам: голова, туловище и хвост ездят, тело
/// сплющивается и вытягивается. Так шаг и прыжок нельзя нарисовать «мимо»
/// стойки — все кадры остаются одним и тем же червём.
public static class WormSprite
{
    const int S = 40;          // сторона холста в пикселях
    const float Ppu = 34f;     // 40 px / 34 ≈ 1,18 юнита в высоту

    /// Скелет кадра: всё, чем один кадр отличается от другого. Голова —
    /// диск, туловище — толстая линия от подошвы к голове, хвост — диск с
    /// перемычкой. Ширина туловища это и есть «сплющенность»: узкий червь
    /// вытянут вверх, широкий — придавлен к земле.
    struct Rig
    {
        public float HeadX, HeadY, HeadR;
        public float BodyX, BodyW, Foot;   // Foot — нижняя точка туловища
        public float TailX, TailY, TailR;
        public float Squint;               // 0 глаза открыты, 1 зажмурены
        public float Mouth;                // 0 черта, 1 разинут
        public float Look;                 // куда смотрит зрачок, пиксели вверх
    }

    /// Стойка в покое — та самая картинка, что была до анимации. Все прочие
    /// кадры считаются от неё сдвигами, поэтому червь не «перерисовывается»
    /// от позы к позе.
    static Rig Rest => new Rig
    {
        HeadX = 19f, HeadY = 25f, HeadR = 10.5f,
        BodyX = 19f, BodyW = 15f, Foot = 3.5f,
        TailX = 7.5f, TailY = 9f, TailR = 3.4f,
    };

    /// Тело: капсула с головой и хвостиком, светлая, чтобы умножение на цвет
    /// команды не превращало червя в силуэт.
    public static Sprite Body { get { Frames(WormPose.Idle, out var b, out _); return b[0]; } }

    /// Черты поверх тела: контур, глаза, зрачки, рот, блик.
    public static Sprite Face { get { Frames(WormPose.Idle, out _, out var f); return f[0]; } }

    /// Сколько кадров в позе. Числа объявлены здесь, а не у проигрывателя:
    /// длина клипа — это про рисунок, а не про тайминг.
    public static int FrameCount(WormPose pose)
    {
        switch (pose)
        {
            case WormPose.Idle: return 8;    // дыхание и моргание в конце круга
            case WormPose.Walk: return 6;    // круг шага: потянулся — хвост догнал
            case WormPose.Drown: return 4;
            case WormPose.Jump: return 3;
            case WormPose.Land: return 3;
            case WormPose.Hurt: return 2;
            case WormPose.Fall: return 2;
            case WormPose.Chute: return 2;
            case WormPose.Jet: return 2;
            case WormPose.Aim: return AimSteps;   // ступени корпуса по углу прицела
            case WormPose.Swing: return 3;
            case WormPose.Dig: return 3;
            case WormPose.Bye: return 4;
            case WormPose.Rope: return 2;
            default: return 1;
        }
    }

    /// Сколько ступеней у прицельной позы. Семь на 170° — это 28° на ступень:
    /// меньше на глаз не различить, больше — корпус начинает щёлкать.
    public const int AimSteps = 7;

    /// Ступень корпуса под углом прицела. Кадр здесь выбирает угол, а не
    /// секундомер: клип прицела не проигрывается, а показывается.
    public static int AimStep(float angleDeg)
    {
        float t = Mathf.InverseLerp(-85f, 85f, Mathf.Clamp(angleDeg, -85f, 85f));
        return Mathf.Clamp(Mathf.RoundToInt(t * (AimSteps - 1)), 0, AimSteps - 1);
    }

    static Sprite[][] _poseBody, _poseFace;

    /// Кадры позы: два ряда одной длины — тело и черты. Строятся один раз на
    /// запуск и отдаются теми же массивами: перерисовывать сорок на сорок
    /// пикселей в кадре у каждого червя нельзя.
    public static void Frames(WormPose pose, out Sprite[] body, out Sprite[] face)
    {
        int n = System.Enum.GetValues(typeof(WormPose)).Length;
        if (_poseBody == null) { _poseBody = new Sprite[n][]; _poseFace = new Sprite[n][]; }

        // Кадры проверяем на живость, а не только на наличие: спрайты,
        // построенные в редакторе, Unity сносит на входе в PlayMode, а
        // статический кэш остаётся с мёртвыми ссылками.
        int i = (int)pose;
        if (_poseBody[i] == null || _poseBody[i].Length == 0 || _poseBody[i][0] == null)
        {
            int frames = Mathf.Max(1, FrameCount(pose));
            var b = new Sprite[frames];
            var f = new Sprite[frames];
            for (int k = 0; k < frames; k++)
            {
                var rig = RigFor(pose, k, frames);
                var pb = BuildBody(rig);
                b[k] = pb.ToSprite(Ppu);
                f[k] = BuildFace(pb, rig).ToSprite(Ppu);
                b[k].name = $"Worm{pose}Body{k}";
                f[k].name = $"Worm{pose}Face{k}";
            }
            _poseBody[i] = b;
            _poseFace[i] = f;
        }

        body = _poseBody[i];
        face = _poseFace[i];
    }

    /// Скелет кадра позы. Червь смотрит вправо: положительный сдвиг головы —
    /// это «вперёд», разворот делает Worm масштабом по X.
    static Rig RigFor(WormPose pose, int frame, int frames)
    {
        var r = Rest;
        float ph = Mathf.PI * 2f * frame / Mathf.Max(1, frames);

        switch (pose)
        {
            // Стойка дышит: голова чуть поднимается, тело чуть полнеет,
            // а под конец круга червь моргает — один кадр из восьми.
            case WormPose.Idle:
                // Вдох и выдох сдвинуты по фазе: качайся всё в такт, кадры
                // на встречных склонах синуса совпали бы попиксельно и червь
                // дышал бы вчетверо быстрее, чем задумано.
                r.HeadY += 0.8f * Mathf.Sin(ph);
                r.HeadR += 0.3f * Mathf.Sin(ph);
                r.BodyW += 0.9f * Mathf.Sin(ph + 1.9f);
                r.TailY += 0.7f * Mathf.Sin(ph + 0.9f);
                if (frame == frames - 2) { r.Squint = 1f; r.Look = -0.3f; }
                break;

            // Шаг гусеницей, как в оригинале: сначала червь тянется головой
            // вперёд и худеет, потом подтягивает хвост и горбится. Подошва
            // на середине круга отрывается от земли — оттуда и покачивание.
            case WormPose.Walk:
            {
                float reach = Mathf.Sin(ph);            // тело тянется вперёд
                float pull = Mathf.Sin(ph - 1.1f);      // хвост догоняет с запозданием
                r.HeadX += 2.6f * reach;
                r.HeadY += 0.9f + 1.6f * Mathf.Cos(ph);
                r.BodyX += 1.2f * reach;
                r.BodyW -= 1.9f * reach;
                r.Foot += Mathf.Max(0f, 1.8f * Mathf.Sin(ph + 1.5f));
                r.TailX += 3f * pull;
                r.TailY += Mathf.Max(0f, 2.4f * pull);
                r.Look = -0.4f;
                r.Squint = 0.15f;
                break;
            }

            // Прыжок показывается уже в воздухе (позу даёт скорость вверх),
            // поэтому приседания в нём нет: рывок вверх — дуга — комок
            // к верхней точке.
            case WormPose.Jump:
                if (frame == 0)
                {
                    r.HeadY += 3.6f; r.HeadR -= 0.9f; r.BodyW -= 3.2f; r.Foot += 2.4f;
                    r.TailX -= 1.2f; r.TailY -= 1.6f; r.TailR -= 0.3f;
                    r.Mouth = 0.7f; r.Look = 1f;
                }
                else if (frame == 1)
                {
                    r.HeadX += 1.3f; r.HeadY += 2.6f; r.BodyW -= 1.6f; r.Foot += 3.6f;
                    r.TailX -= 0.6f; r.TailY += 2.4f;
                    r.Mouth = 0.5f; r.Look = 0.7f;
                }
                else
                {
                    r.HeadX += 1.8f; r.HeadY += 1.1f; r.HeadR += 0.4f; r.BodyW += 1.2f; r.Foot += 4.6f;
                    r.TailX += 3.4f; r.TailY += 5f;
                    r.Mouth = 0.3f; r.Squint = 0.2f;
                }
                break;

            // Падение: хвост мотает, рот открыт, глаза прижмурены.
            case WormPose.Fall:
                r.Foot += 3f; r.BodyW -= 1f;
                r.Mouth = frame == 0 ? 1f : 0.75f;
                r.Squint = frame == 0 ? 0.2f : 0.35f;
                r.Look = -0.6f;
                r.HeadX -= 0.8f;
                r.TailX += frame == 0 ? -1.2f : 1.6f;
                r.TailY += frame == 0 ? 4.2f : 2.2f;
                break;

            // Приземление: удар о землю сплющивает червя и он раскладывается
            // обратно в стойку. Клип одиночный, последний кадр — покой.
            case WormPose.Land:
                if (frame == 0)
                {
                    r.BodyW += 5f; r.HeadY -= 3.6f; r.HeadR += 0.9f; r.Foot -= 0.8f;
                    r.TailX += 1.6f; r.TailY -= 1.2f;
                    r.Squint = 1f; r.Mouth = 0.4f;
                }
                else if (frame == 1)
                {
                    r.BodyW += 2f; r.HeadY -= 1.4f; r.HeadR += 0.35f;
                    r.TailX += 0.8f;
                    r.Squint = 0.3f;
                }
                break;

            // Боль: отшатнулся назад, зажмурился, разинул рот.
            case WormPose.Hurt:
                r.HeadX -= frame == 0 ? 2.4f : 1.2f;
                r.HeadY -= frame == 0 ? 1.2f : 0.5f;
                r.BodyW += frame == 0 ? 1.6f : 0.6f;
                r.TailX -= frame == 0 ? 1.4f : 0.6f;
                r.Squint = 1f;
                r.Mouth = frame == 0 ? 1f : 0.6f;
                break;

            // Вода: червь бьётся и уходит вниз, хвост ходит из стороны в сторону.
            case WormPose.Drown:
                r.HeadY += 0.8f * Mathf.Sin(ph);
                r.HeadX += 0.9f * Mathf.Sin(ph * 2f);
                r.BodyW -= 1.2f;
                r.Foot += 2.5f;
                r.TailX += 2.8f * Mathf.Cos(ph);
                r.TailY += 1.5f + 1.5f * Mathf.Sin(ph);
                r.Mouth = 1f;
                r.Squint = 0.35f;
                break;

            // Под куполом червь висит вытянутым и качается вместе со стропами.
            case WormPose.Chute:
                r.Foot += 2.2f; r.BodyW -= 2f;
                r.HeadX += frame == 0 ? -0.9f : 0.9f;
                r.TailX += frame == 0 ? 1.2f : -1.2f;
                r.TailY -= 1.4f;
                r.Look = 0.4f;
                break;

            // Прицел: кадры идут не по времени, а по углу — от «носом в землю»
            // до «глядя в зенит». Корпус ведёт за стволом, взгляд идёт туда же,
            // хвост работает противовесом: на верхних ступенях он уходит назад,
            // на нижних подпирает сзади-сверху.
            case WormPose.Aim:
            {
                float t = frames > 1 ? frame / (float)(frames - 1) * 2f - 1f : 0f;  // −1 вниз, +1 вверх
                float up = Mathf.Max(0f, t), down = Mathf.Max(0f, -t);
                r.HeadY += 1.7f * t;
                r.HeadX += 1.3f * (1f - Mathf.Abs(t));   // в упор вперёд корпус тянется больше всего
                r.BodyW -= 1.1f * t;
                r.Foot += 0.5f * up;
                r.TailX -= 1.6f * up;
                r.TailY += 1.3f * down;
                r.Look = 1.3f * t;
                break;
            }

            // Удар вплотную: замах назад, выброс вперёд, проводка. Кулак, бита
            // и толчок делят один клип — разными их делает то, что происходит
            // с жертвой, а не то, как червь машет.
            case WormPose.Swing:
                if (frame == 0)
                {
                    r.HeadX -= 2.6f; r.HeadY -= 0.6f; r.BodyW += 1.4f;
                    r.TailX += 2.2f; r.TailY += 1.2f;
                    r.Squint = 0.4f;
                }
                else if (frame == 1)
                {
                    r.HeadX += 4.2f; r.HeadY += 1.2f; r.BodyW -= 2.2f; r.Foot += 0.6f;
                    r.TailX -= 2.4f; r.TailY -= 0.8f;
                    r.Mouth = 0.9f; r.Look = -0.3f;
                }
                else
                {
                    r.HeadX += 1.8f; r.BodyW -= 0.6f;
                    r.TailX -= 0.8f;
                    r.Mouth = 0.3f;
                }
                break;

            // Рытьё: червь уткнулся вперёд-вниз и трясётся вместе с буром.
            // Дрожь идёт через кадр и по обеим осям — ровное качание читалось
            // бы кивком, а не работой инструмента.
            case WormPose.Dig:
            {
                float shake = frame == 1 ? 1f : frame == 2 ? -1f : 0f;
                r.HeadX += 1.6f + 0.7f * shake;
                r.HeadY -= 2.2f + 0.5f * Mathf.Abs(shake);
                r.BodyW += 1.6f;
                r.BodyX += 0.6f;
                r.TailX -= 1.4f - 0.6f * shake;
                r.TailY += 1.6f;
                r.Squint = 0.8f;
                r.Mouth = 0.35f;
                r.Look = -0.8f;
                break;
            }

            // Прощание: червь качается, оседая, и в последнем кадре сползает
            // вниз. Раскачку раньше вела крутилка трансформа — теперь это
            // такие же кадры, как всё остальное.
            case WormPose.Bye:
            {
                float sway = frame == 0 ? -1f : frame == 1 ? 1f : frame == 2 ? -0.6f : 0.2f;
                float sag = frame * 0.9f;
                r.HeadX += 2f * sway;
                r.HeadY += 0.8f - sag;
                r.BodyW += 0.5f + sag * 0.6f;
                r.TailX -= 1.6f * sway;
                r.Squint = 1f;
                r.Mouth = 1f;
                break;
            }

            // На верёвке червь висит вытянутым и качается маятником: точка
            // подвеса выше головы, поэтому ведёт хвост, а не голова.
            case WormPose.Rope:
                r.Foot += 3.4f; r.BodyW -= 2.4f;
                r.HeadY += 1f;
                r.TailX += frame == 0 ? -2.2f : 2.2f;
                r.TailY -= 1.2f;
                r.Look = 0.5f;
                r.Squint = 0.2f;
                break;

            // Ранец: наклон вперёд, хвост задран выхлопом, дрожь через кадр.
            case WormPose.Jet:
                r.HeadX += 2f; r.Foot += 3.2f; r.BodyW -= 1.4f;
                r.TailX -= 1.6f; r.TailY += 3f;
                r.HeadY += frame == 0 ? 0.5f : -0.5f;
                r.Mouth = 0.4f; r.Squint = 0.25f;
                break;
        }

        return r;
    }

    static readonly Color32 Base = new Color32(226, 226, 226, 255);
    static readonly Color32 Lit = new Color32(255, 255, 255, 255);
    static readonly Color32 Dim = new Color32(178, 178, 178, 255);
    static readonly Color32 Ink = new Color32(20, 16, 22, 255);

    static Pix BuildBody(Rig r)
    {
        var p = new Pix(S, S);

        // Подошва туловища держится за Foot, а не за середину: как бы червя
        // ни сплющило, он не всплывает над землёй и не тонет в ней.
        float bodyBot = r.Foot + r.BodyW * 0.5f;

        // Хвост загибается назад-вниз, тело идёт вверх, сверху голова пошире.
        p.Disc(r.TailX, r.TailY, r.TailR, Base);
        p.Line(r.TailX + 0.5f, r.TailY - 0.5f, r.BodyX - 4f, bodyBot - 2.5f, 6.5f, Base);
        p.Line(r.BodyX, bodyBot, r.HeadX, r.HeadY - 5f, r.BodyW, Base);
        p.Disc(r.HeadX, r.HeadY, r.HeadR, Base);

        // Тень снизу и по спине — объём без единого градиента.
        p.Line(r.BodyX, bodyBot - 1f, r.BodyX, bodyBot + 3f, r.BodyW + 2f, Dim);
        int back = Mathf.RoundToInt(r.HeadX + 6f);
        for (int y = 0; y < S; y++)
        for (int x = back; x < S; x++)
            if (p.Get(x, y).a != 0) p.Set(x, y, Dim);

        // Блик на затылке слева.
        p.Soft(r.HeadX - 5f, r.HeadY + 4f, 5.5f, Lit, 0.9f);
        p.Soft(r.BodyX - 7f, r.HeadY - 7f, 3.5f, Lit, 1f);
        return p;
    }

    static Pix BuildFace(Pix body, Rig r)
    {
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
        Eye(p, r.HeadX - 3.5f, r.HeadY + 1f, white, r);
        Eye(p, r.HeadX + 3.5f, r.HeadY + 1f, white, r);

        // Рот — короткий штрих под глазами; от испуга и боли он раскрывается
        // в тёмное пятно. Пятно сидит ниже штриха и не растёт выше: доросши
        // до глаз, оно слилось бы с ними в одну чёрную кляксу.
        if (r.Mouth > 0.05f) p.Disc(r.HeadX + 2.2f, r.HeadY - 6.4f, 1f + 1.5f * r.Mouth, Ink);
        else p.Line(r.HeadX + 0.5f, r.HeadY - 4.5f, r.HeadX + 4.5f, r.HeadY - 5f, 1.4f, Ink);
        return p;
    }

    /// Глаз: белок, зрачок смещён вперёд и вниз, сверху тяжёлое веко —
    /// от него взгляд получается не рыбий, а исподлобья, как в оригинале.
    /// Веко же и закрывает глаз: Squint опускает его до самого низа белка.
    static void Eye(Pix p, float cx, float cy, Color32 white, Rig r)
    {
        // Закрытый глаз — не залитый чернотой белок, а одна ресница на голом
        // теле: залитый превращал моргание и боль в чёрную маску во всю морду.
        if (r.Squint > 0.85f)
        {
            p.Line(cx - 3.2f, cy + 0.3f, cx + 3.2f, cy - 0.3f, 1.6f, Ink);
            return;
        }

        p.Disc(cx, cy, 4f, white);
        p.Disc(cx + 1.2f, cy - 0.8f + r.Look, 2f, Ink);

        // Прищур опускает веко только до середины белка: ниже глаз читался
        // бы чёрной точкой, и прищур было бы не отличить от зажмуренного.
        float lid = Mathf.Lerp(1.6f, 0f, Mathf.Clamp01(r.Squint));
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy > 16f) continue;
            if (dy > lid) p.Set(x, y, Ink);
        }
    }
}
