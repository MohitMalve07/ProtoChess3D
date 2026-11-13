using UnityEngine;

public class Pawn : ChessPiece
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
        int direction = (pieceColor == PieceColor.White) ? 1 : -1;

        // --- 1. Standard Forward Move ---
        int oneStepRow = currentRow + direction;
        if (IsOnBoard(oneStepRow, currentCol) && gameManager.GetPieceAt(oneStepRow, currentCol) == null)
        {
            moves[oneStepRow, currentCol] = true;

            // --- 2. Two-Step Forward Move (on first turn) ---
            int twoStepRow = currentRow + 2 * direction;
            if (!hasMoved && IsOnBoard(twoStepRow, currentCol) && gameManager.GetPieceAt(twoStepRow, currentCol) == null)
            {
                moves[twoStepRow, currentCol] = true;
            }
        }

        // --- 3. Diagonal Capture & En Passant Moves ---
        int[] captureCols = { currentCol - 1, currentCol + 1 };
        foreach (int colOffset in captureCols)
        {
            if (IsOnBoard(oneStepRow, colOffset))
            {
                ChessPiece piece = gameManager.GetPieceAt(oneStepRow, colOffset);
                if (piece != null && piece.pieceColor != this.pieceColor)
                {
                    moves[oneStepRow, colOffset] = true;
                }
                else if (piece == null &&
                         oneStepRow == gameManager.EnPassantTargetRow &&
                         colOffset == gameManager.EnPassantTargetCol)
                {
                    moves[oneStepRow, colOffset] = true;
                }
            }
        }

        return moves;
    }

    public override bool[,] GetAttackSquares()
    {
        bool[,] attacks = new bool[8, 8];
        int direction = (pieceColor == PieceColor.White) ? 1 : -1;
        int attackRow = currentRow + direction;
        int[] captureCols = { currentCol - 1, currentCol + 1 };
        foreach (int colOffset in captureCols)
        {
            if (IsOnBoard(attackRow, colOffset))
            {
                attacks[attackRow, colOffset] = true;
            }
        }
        return attacks;
    }

    private bool IsOnBoard(int row, int col)
    {
        return row >= 0 && row < 8 && col >= 0 && col < 8;
    }
}