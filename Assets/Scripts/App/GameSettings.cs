using System;
using System.IO;
using UnityEngine;

/// Предпочтение схемы управления. Auto — роутер выбирает сам (как было);
/// остальное принудительно фиксирует схему, пока устройство доступно.
public enum InputPref { Auto, Keyboard, Gamepad, Touch }

public enum LanguagePref { Russian, English }

/// Настройки игрока. Лежат одним JSON в Application.persistentDataPath и переживают
/// перезапуск. Применяются сразу: громкость эффектов и музыки, качество, разрешение,
/// схема ввода, вибрация; только хранится пока язык — под будущие фазы.
[Serializable]
public class GameSettings
{
    public float SoundVolume = 0.9f;
    public float MusicVolume = 0.6f;
    public InputPref Input = InputPref.Auto;
    public bool Vibration = true;
    public LanguagePref Language = LanguagePref.Russian;
    public int Quality = -1;          // -1 = не трогать текущий уровень
    public bool Fullscreen = true;
    public int ResolutionIndex = -1;  // индекс в Screen.resolutions, -1 = текущее

    static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

    public static GameSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath));
                if (s != null) return s;
            }
        }
        catch (Exception e) { Debug.LogWarning("Настройки не прочитаны: " + e.Message); }
        return new GameSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); }
        catch (Exception e) { Debug.LogWarning("Настройки не сохранены: " + e.Message); }
    }

    /// Проталкивает значения в движок. Вызывается на старте и после каждой правки.
    public void Apply()
    {
        // Общий AudioListener держим на единице: у эффектов и музыки свои ползунки,
        // и приглушать их вместе было бы ровно одним ползунком меньше.
        AudioListener.volume = 1f;
        Sfx.Volume = Mathf.Clamp01(SoundVolume);
        Music.Volume = Mathf.Clamp01(MusicVolume);

        if (Quality >= 0 && Quality < QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(Quality, true);
        else
            Quality = QualitySettings.GetQualityLevel();

        // Разрешение и окно — только там, где это осмысленно (PC и Mac).
        if (CanChangeWindow)
        {
            var res = Screen.resolutions;
            if (ResolutionIndex >= 0 && ResolutionIndex < res.Length)
            {
                var r = res[ResolutionIndex];
                var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                Screen.SetResolution(r.width, r.height, mode, r.refreshRateRatio);
            }
            else
            {
                Screen.fullScreen = Fullscreen;
            }
        }

        GameInput.Forced = Input switch
        {
            InputPref.Keyboard => (InputScheme?)InputScheme.Keyboard,
            InputPref.Gamepad => (InputScheme?)InputScheme.Gamepad,
            InputPref.Touch => (InputScheme?)InputScheme.Touch,
            _ => null
        };
    }

    public static bool CanChangeWindow =>
        Application.platform == RuntimePlatform.WindowsPlayer ||
        Application.platform == RuntimePlatform.OSXPlayer ||
        Application.platform == RuntimePlatform.LinuxPlayer ||
        Application.isEditor;

    public static bool CanVibrate => Application.isMobilePlatform;
}
