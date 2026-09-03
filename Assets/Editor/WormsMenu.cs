using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Запасной путь: пересоздать игровую сцену, если файл сцены потерялся.
public static class WormsMenu
{
    [MenuItem("Worms/Создать игровую сцену")]
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Bootstrap");
        go.AddComponent<Bootstrap>();

        const string dir = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, dir + "/Game.unity");
        Debug.Log("Сцена создана: " + dir + "/Game.unity — нажмите Play.");
    }
}
