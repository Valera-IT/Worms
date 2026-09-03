#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

/// Фаза 10: кладёт PrivacyInfo.xcprivacy в сгенерированный Xcode-проект и
/// подключает его к главной цели, чтобы файл попал в бандл .app.
///
/// Apple требует манифест с мая 2024. Игра ничего не собирает и не трекает
/// (NSPrivacyTracking = false, пустые CollectedDataTypes), поэтому в манифесте
/// только «required reason» API, которые тянет сам движок: отметки времени
/// файлов и свободное место — загрузка ассетов и кэш, момент старта системы —
/// тайминги внутри Unity, UserDefaults — это PlayerPrefs (в игре — громкость и
/// настройки матча через GameSettings).
public static class IOSPrivacyManifest
{
    const string FileName = "PrivacyInfo.xcprivacy";

    [PostProcessBuild(1)]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        string manifestPath = Path.Combine(pathToBuiltProject, FileName);
        File.WriteAllText(manifestPath, Manifest);

        string pbxPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var pbx = new PBXProject();
        pbx.ReadFromFile(pbxPath);

        string mainTarget = pbx.GetUnityMainTargetGuid();
        string fileGuid = pbx.AddFile(FileName, FileName, PBXSourceTree.Source);
        pbx.AddFileToBuild(mainTarget, fileGuid);

        pbx.WriteToFile(pbxPath);
        Debug.Log("BUILD iOS: " + FileName + " добавлен в Xcode-проект");
    }

    const string Manifest =
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
	<key>NSPrivacyTracking</key>
	<false/>
	<key>NSPrivacyTrackingDomains</key>
	<array/>
	<key>NSPrivacyCollectedDataTypes</key>
	<array/>
	<key>NSPrivacyAccessedAPITypes</key>
	<array>
		<dict>
			<key>NSPrivacyAccessedAPIType</key>
			<string>NSPrivacyAccessedAPICategoryFileTimestamp</string>
			<key>NSPrivacyAccessedAPITypeReasons</key>
			<array>
				<string>C617.1</string>
			</array>
		</dict>
		<dict>
			<key>NSPrivacyAccessedAPIType</key>
			<string>NSPrivacyAccessedAPICategoryDiskSpace</string>
			<key>NSPrivacyAccessedAPITypeReasons</key>
			<array>
				<string>E174.1</string>
			</array>
		</dict>
		<dict>
			<key>NSPrivacyAccessedAPIType</key>
			<string>NSPrivacyAccessedAPICategorySystemBootTime</string>
			<key>NSPrivacyAccessedAPITypeReasons</key>
			<array>
				<string>35F9.1</string>
			</array>
		</dict>
		<dict>
			<key>NSPrivacyAccessedAPIType</key>
			<string>NSPrivacyAccessedAPICategoryUserDefaults</string>
			<key>NSPrivacyAccessedAPITypeReasons</key>
			<array>
				<string>CA92.1</string>
			</array>
		</dict>
	</array>
</dict>
</plist>
";
}
#endif
