using UnityEngine;
using UnityEngine.UIElements;

/// Палитра и заготовки элементов для боевого интерфейса на UI Toolkit.
/// Все размеры — в пикселях макета 1920×1080; PanelSettings масштабирует их под экран.
static class HudTheme
{
    public static readonly Color Ink      = new Color(1f, 1f, 1f, 1f);
    public static readonly Color InkDim   = new Color(1f, 1f, 1f, 0.75f);
    public static readonly Color Shadow   = new Color(0f, 0f, 0f, 0.6f);
    public static readonly Color Panel    = new Color(0f, 0f, 0f, 0.35f);
    public static readonly Color Track    = new Color(0f, 0f, 0f, 0.5f);
    public static readonly Color Slot     = new Color(0f, 0f, 0f, 0.42f);
    public static readonly Color SlotSel  = new Color(1f, 1f, 1f, 0.28f);
    public static readonly Color SlotEdge = new Color(1f, 1f, 1f, 0.22f);
    public static readonly Color Spent    = new Color(0.55f, 0.55f, 0.55f, 1f);
    public static readonly Color NoAmmo   = new Color(0.4f, 0.4f, 0.4f, 1f);
    public static readonly Color WindPos  = new Color(0.5f, 0.9f, 1f);
    public static readonly Color WindNeg  = new Color(1f, 0.7f, 0.45f);
    public static readonly Color Curtain  = new Color(0f, 0f, 0f, 0.6f);

    /// Прямоугольник заданного цвета, прозрачный для кликов и жестов.
    public static VisualElement Box(Color bg)
    {
        var e = new VisualElement();
        e.style.backgroundColor = bg;
        e.pickingMode = PickingMode.Ignore;
        return e;
    }

    /// Подпись с тенью — тень заменяет двойной GUI.Label старого HUD и не мусорит в GC.
    public static Label Text(string s, int size, Color color, FontStyle style = FontStyle.Normal)
    {
        var l = new Label(s);
        l.pickingMode = PickingMode.Ignore;
        var st = l.style;
        st.fontSize = size;
        st.color = color;
        st.unityFontStyleAndWeight = style;
        st.whiteSpace = WhiteSpace.NoWrap;
        st.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 0f, color = Shadow };
        return l;
    }

    public static void Round(VisualElement e, float r)
    {
        var st = e.style;
        st.borderTopLeftRadius = r;
        st.borderTopRightRadius = r;
        st.borderBottomLeftRadius = r;
        st.borderBottomRightRadius = r;
    }

    /// Рамка одной толщины со всех сторон — ею подсвечивается выбранное оружие.
    public static void Border(VisualElement e, float width, Color color)
    {
        var st = e.style;
        st.borderTopWidth = width; st.borderBottomWidth = width;
        st.borderLeftWidth = width; st.borderRightWidth = width;
        st.borderTopColor = color; st.borderBottomColor = color;
        st.borderLeftColor = color; st.borderRightColor = color;
    }

    public static void Pad(VisualElement e, float v, float h)
    {
        var st = e.style;
        st.paddingTop = v; st.paddingBottom = v;
        st.paddingLeft = h; st.paddingRight = h;
    }

    /// Растянуть на весь родительский слой.
    public static void Fill(VisualElement e)
    {
        var st = e.style;
        st.position = Position.Absolute;
        st.left = 0; st.top = 0; st.right = 0; st.bottom = 0;
    }
}
