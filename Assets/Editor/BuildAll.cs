using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Фаза 10: одна кнопка — четыре платформы. Каждая цель сама выставляет свой
/// бэкенд, архитектуру и формат пакета, потом зовёт общий Build.
///
/// Батчмод (редактор закрыт):
///   Unity -batchmode -nographics -projectPath . -executeMethod BuildAll.All -logFile /tmp/build.log
///   -executeMethod BuildAll.IOS  (Android / AndroidApk / Mac / Windows) — по одной цели, код возврата 0/1.
/// Меню: Worms/Сборка/…
///
/// Куда кладёт: Build/<platform>/ в корне проекта. Переопределяется переменной
/// окружения WORMS_BUILD_DIR (абсолютный путь).
public static class BuildAll
{
    const string Product = "Wormfall";
    const string AppId = "com.valeragames.wormfall";

    /// Apple Developer Team для подписи iOS. Личная команда пользователя;
    /// переопределяется переменной окружения WORMS_IOS_TEAM.
    public static string IosTeamId =>
        Environment.GetEnvironmentVariable("WORMS_IOS_TEAM") ?? "FRUVCH4G48";

    public static string IosOutDir => Path.Combine(OutRoot, "iOS");

    static string OutRoot
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("WORMS_BUILD_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Build");
        }
    }

    static string[] Scenes()
    {
        var list = new List<string>();
        foreach (var s in EditorBuildSettings.scenes)
            if (s.enabled) list.Add(s.path);
        if (list.Count == 0) list.Add("Assets/Scenes/Game.unity");
        return list.ToArray();
    }

    // ————————————————————————————————————————————————— цели

    [MenuItem("Worms/Сборка/iOS — Xcode-проект")]
    public static void IOS() => Finish(BuildIOS());

    /// Публичная обёртка для IOSDeploy: собрать Xcode-проект, вернуть успех.
    public static bool BuildIOSProject() => BuildIOS();

    [MenuItem("Worms/Сборка/Android — AAB")]
    public static void Android() => Finish(BuildAndroid(appBundle: true));

    [MenuItem("Worms/Сборка/Android — APK")]
    public static void AndroidApk() => Finish(BuildAndroid(appBundle: false));

    [MenuItem("Worms/Сборка/Mac — arm64")]
    public static void Mac() => Finish(BuildMac());

    [MenuItem("Worms/Сборка/Windows — x64")]
    public static void Windows() => Finish(BuildWindows());

    [MenuItem("Worms/Сборка/Все четыре")]
    public static void All()
    {
        // Одна упавшая цель не должна ронять остальные — собираем всё, отчёт в конце.
        var results = new List<string>();
        bool ok = true;
        foreach (var (name, fn) in new (string, Func<bool>)[]
                 {
                     ("Windows", BuildWindows),
                     ("Mac", BuildMac),
                     ("Android", () => BuildAndroid(appBundle: true)),
                     ("iOS", BuildIOS),
                 })
        {
            bool r;
            try { r = fn(); }
            catch (Exception e) { r = false; Debug.LogError($"BUILD {name}: исключение — {e.Message}"); }
            results.Add($"{name}: {(r ? "OK" : "FAIL")}");
            ok &= r;
        }
        Debug.Log("BUILD ALL: " + string.Join("  |  ", results));
        Finish(ok);
    }

    // ————————————————————————————————————————————————— настройка платформ

    static bool BuildIOS()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, AppId);
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
        // Автоподпись с зашитой командой — готовый проект открывается в Xcode и
        // запускается ⌘R без ручной настройки Signing.
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        PlayerSettings.iOS.appleDeveloperTeamID = IosTeamId;
        // PrivacyInfo.xcprivacy кладёт IOSPrivacyManifest.OnPostprocessBuild;
        // sandboxing / module verifier гасит IOSXcodePatch.OnPostprocessBuild.
        return Build(BuildTarget.iOS, BuildTargetGroup.iOS, IosOutDir);
    }

    /// appBundle=true → .aab для Google Play; false → .apk, который ставится
    /// напрямую (adb install / боковая загрузка). APK подписывается debug-ключом,
    /// пока androidUseCustomKeystore=0 — для релиза в магазин нужен .aab.
    static bool BuildAndroid(bool appBundle)
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppId);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64; // 64-битный ELF под 16 КБ страницы
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
        EditorUserBuildSettings.buildAppBundle = appBundle;
        string ext = appBundle ? ".aab" : ".apk";
        string path = Path.Combine(OutRoot, "Android", Product + ext);
        return Build(BuildTarget.Android, BuildTargetGroup.Android, path);
    }

    static bool BuildMac()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, AppId);
        // Только arm64. В universal обе нативные библиотеки лежат в двух
        // архитектурах, и .app весит 120 МБ вместо 60 при нулевом контенте.
        // Плата — Intel-маки: на них сборка не запустится.
        UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
        string path = Path.Combine(OutRoot, "Mac", Product + ".app");
        return Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, path);
    }

    static bool BuildWindows()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, AppId);
        string path = Path.Combine(OutRoot, "Windows", Product + ".exe");
        return Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, path);
    }

    /// Сколько весит результат сборки: файл — сам файл, папка (.app,
    /// Xcode-проект) — сумма по всему дереву.
    static long OutputSize(string path)
    {
        if (File.Exists(path)) return new FileInfo(path).Length;
        if (!Directory.Exists(path)) return 0;

        long total = 0;
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(f).Length; }
            catch (IOException) { }        // симлинк в никуда размера не имеет
        }
        return total;
    }

    // ————————————————————————————————————————————————— общий прогон

    static bool Build(BuildTarget target, BuildTargetGroup group, string locationPath)
    {
        if (!BuildPipeline.IsBuildTargetSupported(group, target))
        {
            Debug.LogError($"BUILD {target}: модуль поддержки не установлен — пропуск");
            return false;
        }

        // Без ассета PanelSettings плеер стартует с серым экраном — см. UiAssets.
        if (!UiAssets.Ensure()) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(locationPath));

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes(),
            locationPathName = locationPath,
            target = target,
            targetGroup = group,
            options = BuildOptions.None,
        };

        var t0 = DateTime.UtcNow;
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var sum = report.summary;
        double sec = (DateTime.UtcNow - t0).TotalSeconds;

        if (sum.result == BuildResult.Succeeded)
        {
            // Размер того, что поедет пользователю, а не всей папки сборки:
            // рядом с .app и Xcode-проектом Unity кладёт символы отладки и
            // сгенерированный C++ — по ним outTotalSize насчитывал 995 МБ
            // там, где приложение весит 120.
            Debug.Log($"BUILD {target}: OK за {sec:0}s, {OutputSize(locationPath) / (1024 * 1024)} МБ → {locationPath}");
            return true;
        }

        Debug.LogError($"BUILD {target}: {sum.result}, ошибок {sum.totalErrors} за {sec:0}s");
        return false;
    }

    static void Finish(bool ok)
    {
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
