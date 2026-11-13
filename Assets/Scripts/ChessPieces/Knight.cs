using UnityEngine;

public class Knight : ChessPiece
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

        // All 8 possible L-shaped moves for a knight
        int[] rowOffsets = { 2, 2, -2, -2, 1, 1, -1, -1 };
        int[] colOffsets = { 1, -1, 1, -1, 2, -2, 2, -2 };

        for (int i = 0; i < 8; i++)
        {
            int newRow = currentRow + rowOffsets[i];
            int newCol = currentCol + colOffsets[i];

            if (IsOnBoard(newRow, newCol))
            {
                ChessPiece piece = gameManager.GetPieceAt(newRow, newCol);

                // The move is legal if the square is empty or occupied by an opponent.
                if (piece == null || piece.pieceColor != this.pieceColor)
                {
                    moves[newRow, newCol] = true;
                }
            }
        }
        return moves;
    }

    private bool IsOnBoard(int row, int col)
    {
        return row >= 0 && row < 8 && col >= 0 && col < 8;
    }
}