using UnityEngine;

public class ChessBoardGenerator : MonoBehaviour
{
    [Header("Tile Prefabs")]
    public GameObject whiteTilePrefab;
    public GameObject blackTilePrefab;

    [Header("Board Settings")]
    public int boardSize = 8;        // Standard chessboard is 8x8
    public float tileSize = 1f;      // Size of each tile

    private void Start()
    {
        GenerateBoard();
    }

    private void GenerateBoard()
    {
        for (int row = 0; row < boardSize; row++)
        {
            for (int col = 0; col < boardSize; col++)
            {
                // Decide if tile is black or white
                bool isWhite = (row + col) % 2 == 0;

                GameObject tilePrefab = isWhite ? whiteTilePrefab : blackTilePrefab;

                // Instantiate tile
                Vector3 position = new Vector3(col * tileSize, row * tileSize, 0);
                GameObject tile = Instantiate(tilePrefab, position, Quaternion.identity, transform);

                // Name the tile for clarity
                tile.name = $"Tile_{row}_{col}";
            }
        }
    }
}
