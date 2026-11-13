using UnityEngine;

public class Rook : ChessPiece
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

        // Check moves in all four orthogonal directions
        CheckDirection(moves, 1, 0);  // Up
        CheckDirection(moves, -1, 0); // Down
        CheckDirection(moves, 0, 1);  // Right
        CheckDirection(moves, 0, -1); // Left

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
                // Stop if we hit the edge of the board.
                break;
            }
        }
    }
}