#if UNITY_EDITOR_OSX
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// Worms/Сборка/iOS — собрать и на телефон:
///   1. BuildAll.BuildIOSProject() — генерит Xcode-проект (+ манифест приватности,
///      + IOSXcodePatch гасит sandboxing/module verifier), автоподпись с командой.
///   2. xcodebuild — компилирует и подписывает Wormfall.app.
///   3. devicectl — ставит и запускает на подключённом устройстве.
///
/// Xcode-проект руками править не нужно и в GUI открывать не обязательно —
/// поэтому подсказка «Validate Project Settings», которая возвращала
/// ENABLE_MODULE_VERIFIER = YES, больше не всплывает.
///
/// Требует: Xcode + залогиненный Apple ID (Xcode ▸ Settings ▸ Accounts),
/// разблокированный iPhone с включённым Режимом разработчика.
public static class IOSDeploy
{
    const string Scheme = "Unity-iPhone";
    const string Config = "Release";
    const string AppName = "Wormfall.app";
    const string BundleId = "com.valeragames.wormfall";

    [MenuItem("Worms/Сборка/iOS — собрать и на телефон")]
    public static void BuildAndDeploy()
    {
        if (!BuildAll.BuildIOSProject())
        {
            Debug.LogError("iOS deploy: генерация Xcode-проекта не удалась — стоп.");
            return;
        }

        string proj = Path.Combine(BuildAll.IosOutDir, "Unity-iPhone.xcodeproj");
        string dd = Path.Combine(BuildAll.IosOutDir, "DD");
        string app = Path.Combine(dd, "Build", "Products", Config + "-iphoneos", AppName);

        Debug.Log("iOS deploy: xcodebuild — компиляция и подпись…");
        int rc = Run("xcrun", new[]
        {
            "xcodebuild",
            "-project", proj,
            "-scheme", Scheme,
            "-configuration", Config,
            "-destination", "generic/platform=iOS",
            "-derivedDataPath", dd,
            "-allowProvisioningUpdates",
            "DEVELOPMENT_TEAM=" + BuildAll.IosTeamId,
            "CODE_SIGN_STYLE=Automatic",
            "build",
        }, out string xcodeOut);

        if (rc != 0 || !Directory.Exists(app))
        {
            Debug.LogError($"iOS deploy: xcodebuild упал (код {rc}). Хвост вывода:\n{Tail(xcodeOut, 40)}");
            return;
        }
        Debug.Log("iOS deploy: собрано → " + app);

        string device = FirstConnectedDevice();
        if (device == null)
        {
            Debug.LogWarning("iOS deploy: устройство не найдено. .app готов, поставь вручную:\n" +
                             $"xcrun devicectl device install app --device <id> \"{app}\"");
            return;
        }

        Debug.Log("iOS deploy: установка на " + device + "…");
        if (Run("xcrun", new[] { "devicectl", "device", "install", "app", "--device", device, app }, out string insOut) != 0)
        {
            Debug.LogError("iOS deploy: установка не удалась:\n" + Tail(insOut, 30));
            return;
        }

        Run("xcrun", new[] { "devicectl", "device", "process", "launch", "--device", device, BundleId }, out _);
        Debug.Log("iOS deploy: установлено и запущено (" + BundleId + "). " +
                  "Первый раз: на iPhone Настройки ▸ Основные ▸ VPN и управление устройством ▸ доверять разработчику.");
    }

    static string FirstConnectedDevice()
    {
        if (Run("xcrun", new[] { "devicectl", "list", "devices" }, out string outp) != 0) return null;
        foreach (string line in outp.Split('\n'))
        {
            bool usable = line.IndexOf("connected", StringComparison.OrdinalIgnoreCase) >= 0
                       || line.IndexOf("available", StringComparison.OrdinalIgnoreCase) >= 0
                       || line.IndexOf("paired", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!usable) continue;
            var m = Regex.Match(line, @"[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}");
            if (m.Success) return m.Value;
        }
        return null;
    }

    static int Run(string exe, string[] args, out string combinedOutput)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = BuildAll.IosOutDir,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        var sb = new StringBuilder();
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        combinedOutput = sb.ToString();
        return p.ExitCode;
    }

    static string Tail(string s, int lines)
    {
        var all = s.Split('\n');
        int from = Math.Max(0, all.Length - lines);
        return string.Join("\n", all, from, all.Length - from);
    }
}
#endif
