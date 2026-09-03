using UnityEngine;
using UnityEngine.UIElements;

/// Одна точка, где рождаются PanelSettings для HUD и для меню.
///
/// Панель обязана происходить из ассета, а не из `ScriptableObject.CreateInstance`:
/// продвинутому текстовому движку UI Toolkit нужны данные ICU, а Unity кладёт их
/// в PanelSettings только при импорте ассета. У созданной в рантайме панели их нет,
/// `UITKTextHandle.ShapeText` падает `NullReferenceException` на первом же замере
/// текста, раскладка обрывается — и в сборке остаётся серый экран. В редакторе всё
/// рисуется, потому что ICU там загружен всегда: баг виден только в плеере.
///
/// Ассеты создаёт `Editor/UiAssets.cs`, оба текстовые — «ноль бинарных ассетов»
/// в проекте по-прежнему верно.
public static class UiPanels
{
    public const int RefW = 1920, RefH = 1080;
    const string PanelAsset = "UiPanel";

    public static PanelSettings Create(string name, int sortingOrder)
    {
        var src = Resources.Load<PanelSettings>(PanelAsset);
        if (src == null)
            Debug.LogWarning($"UiPanels: нет Resources/{PanelAsset} — в сборке текст рисоваться не будет, " +
                             "почините меню «Worms → Сборка → Создать ассеты интерфейса»");

        // Копия, а не сам ассет: у HUD и меню разные sortingOrder, править ассет нельзя.
        var p = src != null ? Object.Instantiate(src) : ScriptableObject.CreateInstance<PanelSettings>();

        p.name = name;
        p.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        p.referenceResolution = new Vector2Int(RefW, RefH);
        p.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        p.match = 0.5f;
        p.clearColor = false;
        p.sortingOrder = sortingOrder;
        if (p.themeStyleSheet == null) p.themeStyleSheet = ResolveTheme();
        return p;
    }

    static ThemeStyleSheet ResolveTheme()
    {
        var t = Resources.Load<ThemeStyleSheet>("UnityDefaultRuntimeTheme")
             ?? Resources.Load<ThemeStyleSheet>("unity-runtime-theme");
        if (t == null) t = ScriptableObject.CreateInstance<ThemeStyleSheet>();
        return t;
    }
}
