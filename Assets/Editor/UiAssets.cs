using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// В сборке обязан лежать ассет `PanelSettings`: без него UI Toolkit остаётся без
/// данных ICU и валится `NullReferenceException` в `UITKTextHandle.ShapeText` на
/// первом замере текста — раскладка обрывается, в плеере серый экран. Подробнее —
/// в шапке `Scripts/UI/UiPanels.cs`.
///
/// Скрипт создаёт минимальную пару в `Assets/Resources`: тему и панель. Оба файла
/// текстовые (`.tss` — одна строка, `.asset` — YAML), так что «ноль бинарных
/// ассетов» в проекте остаётся правдой.
///
/// Запуск: меню **Worms → Сборка → Создать ассеты интерфейса** либо
/// `-executeMethod UiAssets.Create`. Идемпотентно; `BuildAll` зовёт его сам.
public static class UiAssets
{
    const string Dir = "Assets/Resources";
    const string ThemePath = Dir + "/UnityDefaultRuntimeTheme.tss";
    const string PanelPath = Dir + "/UiPanel.asset";

    /// Ровно то, что Unity кладёт в сгенерированную тему по умолчанию.
    const string ThemeSource = "@import url(\"unity-theme://default\");\n";

    [MenuItem("Worms/Сборка/Создать ассеты интерфейса")]
    public static void Create()
    {
        bool ok = Ensure();
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// true, если после вызова оба ассета на месте.
    public static bool Ensure()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Resources");

        if (!File.Exists(ThemePath))
        {
            File.WriteAllText(ThemePath, ThemeSource);
            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
        }
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (panel == null)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, PanelPath);
        }

        panel.themeStyleSheet = theme;
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(UiPanels.RefW, UiPanels.RefH);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;
        panel.clearColor = false;
        EditorUtility.SetDirty(panel);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        bool ok = theme != null && panel != null;

        Debug.Log($"UI ASSETS: тема={(theme != null ? "ok" : "НЕТ")} панель={(panel != null ? "ok" : "НЕТ")} icu={IcuState(panel)}");
        if (!ok) Debug.LogError("UI ASSETS: ассеты интерфейса не созданы — сборка даст серый экран");
        return ok;
    }

    /// Поле с данными ICU у PanelSettings приватное; читаем через SerializedObject,
    /// чтобы прогон было видно по логу, а не только по чёрному экрану на телефоне.
    static string IcuState(PanelSettings panel)
    {
        if (panel == null) return "?";
        var so = new SerializedObject(panel);
        var prop = so.FindProperty("m_ICUDataAsset");
        if (prop == null) return "поля нет";
        return prop.objectReferenceValue != null ? "ok" : "пусто";
    }
}
