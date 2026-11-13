using UnityEngine;

public class Bishop : ChessPiece
{
    [SerializeField] private int startRow;
    [SerializeField] private int startCol;
    private GameManager gameManager;

    void Awake()
    {
        gameManager = FindAnyObjectByType<GameManager>();
        Initialize(startRow, startCol);
    }

    public override bool[,] GetLegalMoves()
    {
        bool[,] moves = new bool[8, 8];

        // Check moves diagonally in all four directions
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

            // Check if the new position is within the board boundaries
            if (newRow >= 0 && newRow < 8 && newCol >= 0 && newCol < 8)
            {
                ChessPiece piece = gameManager.GetPieceAt(newRow, newCol);

                // If the square is empty, it's a legal move.
                if (piece == null)
                {
                    moves[newRow, newCol] = true;
                }
                // If the square is occupied by another piece.
                else
                {
                    // Check if it's an opponent's piece.
                    if (piece.pieceColor != this.pieceColor)
                    {
                        // It's an enemy, so this is a legal capture move.
                        moves[newRow, newCol] = true;
                    }

                    // The path is blocked by either a friendly or enemy piece, so stop checking.
                    break;
                }
            }
            else
            {
                // We've gone off the board, so stop checking in this direction.
                break;
            }
        }
    }
}