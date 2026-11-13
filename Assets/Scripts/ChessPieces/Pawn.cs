using UnityEngine;

public class Pawn : ChessPiece
{
    [SerializeField] private int startRow;
    [SerializeField] private int startCol;
    private bool hasMoved = false;
    private GameManager gameManager;

    void Awake()
    {
        gameManager = FindObjectOfType<GameManager>();
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

        // --- 3. Diagonal Capture Moves ---
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
            }
        }

        return moves;
    }

    // Override the base MoveToSquare to set the hasMoved flag
    public override void MoveToSquare(int row, int col)
    {
        base.MoveToSquare(row, col); // Call the original method to move the piece
        hasMoved = true;             // Set the flag after the first move
    }

    private bool IsOnBoard(int row, int col)
    {
        return row >= 0 && row < 8 && col >= 0 && col < 8;
    }
}