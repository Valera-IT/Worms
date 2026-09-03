using UnityEditor;
using UnityEngine;

/// Запуск замера ландшафта из редактора: Worms → Terrain bench,
/// или `Unity -executeMethod TerrainBenchMenu.Run` в батче.
public static class TerrainBenchMenu
{
    [MenuItem("Worms/Terrain bench")]
    public static void Run()
    {
        // С фазы 6 миров пять — меряем каждый: у пещеры и каньона профиль
        // маски другой, а значит и стоимость взрыва может отличаться.
        foreach (TerrainKind kind in new[]
                 { TerrainKind.Island, TerrainKind.Cave, TerrainKind.Archipelago,
                   TerrainKind.Canyon, TerrainKind.Snow })
            Debug.Log(TerrainBench.Run(60, 12345, null, kind));

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
