#if UNITY_IOS
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

/// Xcode 16+ включает по умолчанию две проверки, которые ломают сгенерированный
/// Unity Xcode-проект:
///   ENABLE_USER_SCRIPT_SANDBOXING — песочница запрещает il2cpp-скрипт-фазам
///     писать во время сборки: «Sandbox: mkdir deny(1) … /artifacts».
///   ENABLE_MODULE_VERIFIER — гоняет umbrella-заголовок UnityFramework.h через
///     синтетический Test.m; заголовки движка (порядок Undefine/RedefinePlatforms,
///     кавычки вместо <>) её не проходят: «could not build module 'Test'».
///
/// Unity перегенерирует project.pbxproj при каждой сборке, поэтому проставляем
/// обе в NO явно — на проекте и на каждой цели.
public static class IOSXcodePatch
{
    static readonly string[] Flags =
    {
        "ENABLE_USER_SCRIPT_SANDBOXING",
        "ENABLE_MODULE_VERIFIER",
    };

    [PostProcessBuild(2)] // после IOSPrivacyManifest (1)
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var pbx = new PBXProject();
        pbx.ReadFromFile(pbxPath);

        var guids = new List<string>
        {
            pbx.ProjectGuid(),
            pbx.GetUnityMainTargetGuid(),
            pbx.GetUnityFrameworkTargetGuid(),
        };
        // GameAssembly / тестовая цель — имена уникальны, deprecated-исключения нет.
        foreach (string name in new[] { "GameAssembly", "Unity-iPhone Tests" })
        {
            try
            {
                string g = pbx.TargetGuidByName(name);
                if (!string.IsNullOrEmpty(g)) guids.Add(g);
            }
            catch { /* цели может не быть */ }
        }

        foreach (string flag in Flags)
            foreach (string guid in guids)
                pbx.SetBuildProperty(guid, flag, "NO");

        pbx.WriteToFile(pbxPath);
        Debug.Log("BUILD iOS: sandboxing и module verifier выключены в Xcode-проекте");
    }
}
#endif
