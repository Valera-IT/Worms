using System;
using UnityEngine;
using UnityEngine.UIElements;

/// Заготовки элементов для меню на UI Toolkit. Строятся из кода теми же приёмами,
/// что и боевой HUD: ни ассетов, ни USS-файлов. Размеры — в пикселях макета
/// 1920×1080, PanelSettings масштабирует их под экран.
static class MenuTheme
{
    // Палитра титульного экрана оригинала: мшистый зелёный задник, белые и
    // бирюзовые строки, кроваво-красный логотип.
    public static readonly Color Ink     = new Color(1f, 1f, 1f, 0.96f);
    public static readonly Color InkDim  = new Color(0.72f, 0.82f, 0.72f, 0.75f);
    public static readonly Color Scrim   = new Color(0.03f, 0.06f, 0.03f, 0.72f);
    public static readonly Color Card    = new Color(0.06f, 0.13f, 0.07f, 0.94f);
    public static readonly Color Btn     = new Color(0.09f, 0.18f, 0.10f, 0.92f);
    public static readonly Color BtnHot  = new Color(0.16f, 0.30f, 0.16f, 0.96f);
    public static readonly Color BtnOff  = new Color(0.06f, 0.10f, 0.06f, 0.9f);
    public static readonly Color Accent  = new Color(0.30f, 0.92f, 0.86f);   // бирюза строк меню
    public static readonly Color Hot     = new Color(1f, 0.86f, 0.25f);      // подсветка под курсором
    public static readonly Color Track   = new Color(0f, 0f, 0f, 0.55f);
    public static readonly Color Edge    = new Color(0.45f, 0.72f, 0.42f, 0.85f);
    public static readonly Color LogoRed = new Color(0.86f, 0.13f, 0.11f);
    public static readonly Color LogoDark= new Color(0.36f, 0.03f, 0.03f);

    static Texture2D _backdrop;

    /// Мшистый задник: тот же приём с полосами шума, что и у породы в
    /// ландшафте, только в зелёных тонах. Рисуется один раз за запуск.
    public static Texture2D Backdrop
    {
        get
        {
            if (_backdrop != null) return _backdrop;

            const int N = 384;
            var pix = new Pix(N, N);
            var ramp = new Color32[]
            {
                new Color32(8, 20, 10, 255),
                new Color32(16, 40, 18, 255),
                new Color32(26, 62, 26, 255),
                new Color32(40, 88, 34, 255),
                new Color32(58, 116, 44, 255)
            };

            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // Частота с запасом: текстура растягивается на весь экран,
                // и крупные пятна выглядели бы размазанной кляксой.
                float n = 0.62f * Mathf.PerlinNoise(x * 0.055f, y * 0.055f)
                        + 0.38f * Mathf.PerlinNoise(x * 0.150f + 19f, y * 0.150f + 7f);
                float t = Mathf.Clamp01(Mathf.InverseLerp(0.30f, 0.72f, n)) * (ramp.Length - 0.001f);
                int band = (int)t;
                bool seam = (t - band) < 0.12f && band > 0;
                pix.Set(x, y, seam ? ramp[band - 1] : ramp[band]);
            }

            _backdrop = pix.ToTexture();
            return _backdrop;
        }
    }

    /// Слой на весь экран с мшистым задником — под ним ничего не видно.
    public static VisualElement Screen()
    {
        var e = new VisualElement();
        var st = e.style;
        st.position = Position.Absolute;
        st.left = 0; st.top = 0; st.right = 0; st.bottom = 0;
        st.backgroundImage = new StyleBackground(Backdrop);
        st.backgroundColor = new Color(0.05f, 0.11f, 0.06f);
        st.alignItems = Align.Center;
        st.justifyContent = Justify.Center;
        e.pickingMode = PickingMode.Position;
        return e;
    }

    /// Логотип: три копии надписи со сдвигом дают объёмные буквы с чёрной
    /// обводкой — так же, как красное WORMS на титульном экране 1995 года.
    public static VisualElement Logo(string text, int size = 128)
    {
        var wrap = new VisualElement();
        wrap.style.height = size * 1.25f;
        wrap.style.width = Length.Percent(100);
        wrap.pickingMode = PickingMode.Ignore;

        void Layer(Color color, float dx, float dy)
        {
            var l = Text(text, size, color, FontStyle.Bold);
            l.style.position = Position.Absolute;
            l.style.left = 0; l.style.right = 0; l.style.top = dy;
            l.style.translate = new Translate(dx, 0, 0);
            l.style.letterSpacing = size * 0.10f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.textShadow = new TextShadow { offset = Vector2.zero, blurRadius = 0f, color = Color.clear };
            wrap.Add(l);
        }

        Layer(new Color(0f, 0f, 0f, 0.9f), 8f, 10f);   // тень
        Layer(LogoDark, 4f, 5f);                        // боковина букв
        Layer(LogoRed, 0f, 0f);                         // лицевая грань
        return wrap;
    }

    /// Строка меню: без плашки, как в оригинале — только текст, который
    /// желтеет под курсором.
    public static Button Item(string label, Action onClick, Color? color = null)
    {
        var c = color ?? Ink;
        var b = new Button(() => onClick?.Invoke()) { text = label };
        var st = b.style;
        st.backgroundColor = Color.clear;
        st.color = c;
        st.fontSize = 34;
        st.height = 58;
        st.marginTop = 3; st.marginBottom = 3;
        st.unityFontStyleAndWeight = FontStyle.Bold;
        st.unityTextAlign = TextAnchor.MiddleCenter;
        st.letterSpacing = 6;
        st.borderTopWidth = st.borderBottomWidth = st.borderLeftWidth = st.borderRightWidth = 0;
        st.textShadow = new TextShadow { offset = new Vector2(3f, 3f), blurRadius = 0f, color = new Color(0, 0, 0, 0.85f) };
        b.RegisterCallback<PointerEnterEvent>(_ => b.style.color = Hot);
        b.RegisterCallback<PointerLeaveEvent>(_ => b.style.color = c);
        return b;
    }

    public static VisualElement Row(FlexDirection dir = FlexDirection.Row)
    {
        var e = new VisualElement();
        e.style.flexDirection = dir;
        e.style.alignItems = Align.Center;
        e.pickingMode = PickingMode.Ignore;
        return e;
    }

    public static Label Text(string s, int size, Color color, FontStyle style = FontStyle.Normal)
    {
        var l = new Label(s);
        l.pickingMode = PickingMode.Ignore;
        var st = l.style;
        st.fontSize = size;
        st.color = color;
        st.unityFontStyleAndWeight = style;
        st.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 0f, color = new Color(0, 0, 0, 0.55f) };
        return l;
    }

    static void Round(VisualElement e, float r)
    {
        var st = e.style;
        st.borderTopLeftRadius = st.borderTopRightRadius = r;
        st.borderBottomLeftRadius = st.borderBottomRightRadius = r;
    }

    /// Полупрозрачный слой на весь экран с карточкой-колонкой по центру.
    /// Возвращает и слой (добавлять в root), и тело карточки (складывать в него ряды).
    public static (VisualElement layer, VisualElement body) Sheet(string title, float width = 720f)
    {
        var layer = new VisualElement();
        layer.style.position = Position.Absolute;
        layer.style.left = 0; layer.style.top = 0; layer.style.right = 0; layer.style.bottom = 0;
        layer.style.backgroundColor = Scrim;
        layer.style.justifyContent = Justify.Center;
        layer.style.alignItems = Align.Center;
        layer.pickingMode = PickingMode.Position;   // ловит клики мимо кнопок

        // Экран боя виден сквозь пелену только в паузе; в меню под пеленой
        // лежит мшистый задник, так что все экраны выглядят одинаково.
        var card = new VisualElement();
        card.style.backgroundColor = Card;
        card.style.width = width;
        card.style.maxWidth = Length.Percent(94);
        card.style.paddingLeft = 40; card.style.paddingRight = 40;
        card.style.paddingTop = 28; card.style.paddingBottom = 28;
        card.style.borderTopWidth = card.style.borderBottomWidth = 3;
        card.style.borderLeftWidth = card.style.borderRightWidth = 3;
        card.style.borderTopColor = card.style.borderBottomColor = Edge;
        card.style.borderLeftColor = card.style.borderRightColor = Edge;
        Round(card, 4);
        card.pickingMode = PickingMode.Position;

        if (!string.IsNullOrEmpty(title))
        {
            var t = Text(title.ToUpperInvariant(), 40, Hot, FontStyle.Bold);
            t.style.marginBottom = 20;
            t.style.letterSpacing = 6;
            t.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(t);
        }

        layer.Add(card);
        return (layer, card);
    }

    public static Button Button(string label, Action onClick, bool enabled = true)
    {
        var b = new Button(() => { if (enabled) onClick?.Invoke(); }) { text = label };
        var st = b.style;
        st.height = 60;
        st.marginTop = 6; st.marginBottom = 6;
        st.marginLeft = 0; st.marginRight = 0;
        st.paddingLeft = 18; st.paddingRight = 18;
        st.fontSize = 22;
        st.color = enabled ? Accent : InkDim;
        st.backgroundColor = enabled ? Btn : BtnOff;
        st.unityFontStyleAndWeight = FontStyle.Bold;
        st.letterSpacing = 2;
        st.borderTopWidth = st.borderBottomWidth = st.borderLeftWidth = st.borderRightWidth = 2;
        st.borderTopColor = st.borderBottomColor = st.borderLeftColor = st.borderRightColor =
            enabled ? Edge : new Color(1f, 1f, 1f, 0.08f);
        Round(b, 4);
        st.unityTextAlign = TextAnchor.MiddleCenter;
        if (enabled)
        {
            b.RegisterCallback<PointerEnterEvent>(_ => { b.style.backgroundColor = BtnHot; b.style.color = Hot; });
            b.RegisterCallback<PointerLeaveEvent>(_ => { b.style.backgroundColor = Btn; b.style.color = Accent; });
        }
        return b;
    }

    /// Ряд «‹ label: value ›». next/prev крутят значение, текст перечитывается через getText.
    public static VisualElement Stepper(string label, Func<string> getText, Action prev, Action next)
    {
        var row = Row();
        row.style.height = 56;
        row.style.marginTop = 4; row.style.marginBottom = 4;
        row.style.justifyContent = Justify.SpaceBetween;
        row.pickingMode = PickingMode.Position;

        var name = Text(label, 20, Ink);
        row.Add(name);

        var right = Row();
        var minus = SmallButton("‹", prev);
        var val = Text("", 20, Accent, FontStyle.Bold);
        val.style.minWidth = 200;
        val.style.unityTextAlign = TextAnchor.MiddleCenter;
        var plus = SmallButton("›", next);

        void Refresh() => val.text = getText();
        minus.clicked += Refresh;
        plus.clicked += Refresh;
        Refresh();

        right.Add(minus); right.Add(val); right.Add(plus);
        row.Add(right);
        return row;
    }

    static Button SmallButton(string glyph, Action onClick)
    {
        var b = new Button(() => onClick?.Invoke()) { text = glyph };
        var st = b.style;
        st.width = 48; st.height = 48;
        st.fontSize = 26;
        st.color = Ink;
        st.backgroundColor = Btn;
        st.unityFontStyleAndWeight = FontStyle.Bold;
        st.unityTextAlign = TextAnchor.MiddleCenter;
        st.borderTopWidth = st.borderBottomWidth = st.borderLeftWidth = st.borderRightWidth = 0;
        st.marginLeft = 4; st.marginRight = 4;
        Round(b, 8);
        return b;
    }

    public static VisualElement Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var row = Row();
        row.style.height = 56;
        row.style.marginTop = 4; row.style.marginBottom = 4;
        row.style.justifyContent = Justify.SpaceBetween;
        row.pickingMode = PickingMode.Position;
        row.Add(Text(label, 20, Ink));

        var knobTrack = new VisualElement();
        knobTrack.style.width = 72; knobTrack.style.height = 36;
        knobTrack.style.backgroundColor = Track;
        Round(knobTrack, 18);
        knobTrack.pickingMode = PickingMode.Position;

        var knob = new VisualElement();
        knob.style.position = Position.Absolute;
        knob.style.top = 3; knob.style.width = 30; knob.style.height = 30;
        Round(knob, 15);
        knobTrack.Add(knob);

        void Paint()
        {
            bool on = get();
            knob.style.left = on ? 39 : 3;
            knob.style.backgroundColor = on ? Accent : new Color(1f, 1f, 1f, 0.5f);
        }
        knobTrack.RegisterCallback<PointerDownEvent>(evt => { set(!get()); Paint(); evt.StopPropagation(); });
        Paint();

        row.Add(knobTrack);
        return row;
    }

    /// Ползунок 0..1, собранный из дорожки и заливки — без зависимости от USS-темы.
    public static VisualElement Slider(string label, Func<float> get, Action<float> set)
    {
        var wrap = new VisualElement();
        wrap.style.marginTop = 6; wrap.style.marginBottom = 6;
        wrap.pickingMode = PickingMode.Ignore;

        var head = Row();
        head.style.justifyContent = Justify.SpaceBetween;
        var name = Text(label, 20, Ink);
        var pct = Text("", 18, InkDim);
        head.Add(name); head.Add(pct);
        wrap.Add(head);

        var track = new VisualElement();
        track.style.height = 20;
        track.style.marginTop = 8;
        track.style.backgroundColor = Track;
        Round(track, 10);
        track.pickingMode = PickingMode.Position;

        var fill = new VisualElement();
        fill.style.position = Position.Absolute;
        fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
        fill.style.backgroundColor = Accent;
        Round(fill, 10);
        track.Add(fill);

        void Paint()
        {
            float v = Mathf.Clamp01(get());
            fill.style.width = Length.Percent(v * 100f);
            pct.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        void SetFromPointer(float localX)
        {
            float w = track.resolvedStyle.width;
            if (w <= 1f) return;
            set(Mathf.Clamp01(localX / w));
            Paint();
        }

        bool drag = false;
        track.RegisterCallback<PointerDownEvent>(evt =>
        {
            drag = true;
            SetFromPointer(evt.localPosition.x);
            evt.StopPropagation();
        });
        track.RegisterCallback<PointerMoveEvent>(evt => { if (drag) SetFromPointer(evt.localPosition.x); });
        track.RegisterCallback<PointerUpEvent>(_ => drag = false);
        track.RegisterCallback<PointerLeaveEvent>(_ => drag = false);
        track.RegisterCallback<GeometryChangedEvent>(_ => Paint());
        Paint();

        wrap.Add(track);
        return wrap;
    }

    /// Строка ввода: подпись слева, поле справа. Пока нужна одному экрану —
    /// адресу хоста в сетевой игре, — но заводится тут, рядом с остальными
    /// рядами, чтобы поле не выбивалось из вида меню.
    public static TextField Field(string label, string value, Action<string> onChange, int maxLength = 40)
    {
        var f = new TextField(label) { value = value, maxLength = maxLength };
        f.style.marginTop = 6; f.style.marginBottom = 6;
        f.style.fontSize = 20;

        var lab = f.labelElement;
        lab.style.color = Ink;
        lab.style.fontSize = 20;
        lab.style.minWidth = 180;
        lab.style.unityFontStyleAndWeight = FontStyle.Bold;

        var input = f.Q(TextField.textInputUssName);
        if (input != null)
        {
            input.style.backgroundColor = Track;
            input.style.color = Ink;
            input.style.height = 52;
            input.style.paddingLeft = 12; input.style.paddingRight = 12;
            input.style.borderTopWidth = input.style.borderBottomWidth =
                input.style.borderLeftWidth = input.style.borderRightWidth = 2;
            input.style.borderTopColor = input.style.borderBottomColor =
                input.style.borderLeftColor = input.style.borderRightColor = Edge;
            Round(input, 4);
        }

        f.RegisterValueChangedCallback(e => onChange?.Invoke(e.newValue));
        return f;
    }

    public static Label Note(string s)
    {
        var l = Text(s, 15, InkDim);
        l.style.whiteSpace = WhiteSpace.Normal;
        l.style.marginTop = 8;
        return l;
    }
}
