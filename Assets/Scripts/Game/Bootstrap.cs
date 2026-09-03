using UnityEngine;

/// Единственный компонент в сцене: поднимает ввод и корень приложения.
/// Дальше всем распоряжается App — меню, матч, итоги.
public class Bootstrap : MonoBehaviour
{
    void Awake()
    {
        if (FindAnyObjectByType<GameInput>() == null)
            new GameObject("Input").AddComponent<GameInput>();

        if (FindAnyObjectByType<App>() == null)
            new GameObject("App").AddComponent<App>();
    }
}
