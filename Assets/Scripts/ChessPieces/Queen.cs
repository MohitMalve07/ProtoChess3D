using UnityEngine;

public class Queen : ChessPiece
{
    [SerializeField] private int startRow;
    [SerializeField] private int startCol;
    private GameManager gameManager;

    void Awake()
    {
        gameManager = FindObjectOfType<GameManager>();
        Initialize(startRow, startCol);
    }

    public override bool[,] GetLegalMoves()
    {
        bool[,] moves = new bool[8, 8];

        // --- Rook's Directions (Horizontal & Vertical) ---
        CheckDirection(moves, 1, 0);   // Up
        CheckDirection(moves, -1, 0);  // Down
        CheckDirection(moves, 0, 1);   // Right
        CheckDirection(moves, 0, -1);  // Left

        // --- Bishop's Directions (Diagonals) ---
        CheckDirection(moves, 1, 1);   // Top-Right
        CheckDirection(moves, 1, -1);  // Top-Left
        CheckDirection(moves, -1, 1);  // Bottom-Right
        CheckDirection(moves, -1, -1); // Bottom-Left

        return moves;
    }

    private void CheckDirection(bool[,] moves, int rowDirection, int colDirection)
    {
        for (int i = 1; i < 8; i++)
        {
            int newRow = currentRow + i * rowDirection;
            int newCol = currentCol + i * colDirection;

            if (newRow >= 0 && newRow < 8 && newCol >= 0 && newCol < 8)
            {
                ChessPiece piece = gameManager.GetPieceAt(newRow, newCol);

                if (piece == null)
                {
                    moves[newRow, newCol] = true;
                }
                else
                {
                    if (piece.pieceColor != this.pieceColor)
                    {
                        moves[newRow, newCol] = true;
                    }
                    break;
                }
            }
            else
            {
                break;
            }
        }
    }
}