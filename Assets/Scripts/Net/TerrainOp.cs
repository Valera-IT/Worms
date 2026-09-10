using UnityEngine;

/// Одно изменение ландшафта, пригодное к пересылке. Форма нарочно общая на
/// все три случая — воронка, коридор бура, балка, — чтобы у сети был один
/// формат вместо трёх и чтобы новый вид разрушения не заводил новый пакет.
public struct TerrainOp
{
    public NetEvent Kind;
    public Vector2 A;     // воронка: центр; бур: откуда; балка: центр
    public Vector2 B;     // бур: куда; балка: длина и толщина
    public float R;       // радиус
    public float Angle;   // балка: наклон
    public Color32 C;     // балка: цвет

    public static TerrainOp Blast(Vector2 center, float radius) =>
        new TerrainOp { Kind = NetEvent.Blast, A = center, R = radius };

    public static TerrainOp Dug(Vector2 from, Vector2 to, float radius) =>
        new TerrainOp { Kind = NetEvent.Dig, A = from, B = to, R = radius };

    public static TerrainOp Beam(Vector2 center, float angle, float length, float thickness, Color32 color) =>
        new TerrainOp { Kind = NetEvent.Beam, A = center, B = new Vector2(length, thickness), Angle = angle, C = color };
}
