using UnityEngine;

public class King : ChessPiece
{
    [SerializeField] private int startRow;
    [SerializeField] private int startCol;
    private GameManager gameManager;

    void Awake()
    {
        gameManager = FindAnyObjectByType<GameManager>();
        Initialize(startRow, startCol);
    }

    public override bool[,] GetAttackSquares()
    {
        bool[,] attacks = new bool[8, 8];

        // The King attacks one square in any of the 8 directions (excludes castling to avoid recursion)
        int[] rowOffsets = { 0, 0, 1, 1, 1, -1, -1, -1 };
        int[] colOffsets = { 1, -1, 0, 1, -1, 0, 1, -1 };

        for (int i = 0; i < 8; i++)
        {
            int newRow = currentRow + rowOffsets[i];
            int newCol = currentCol + colOffsets[i];

            if (IsOnBoard(newRow, newCol))
            {
                ChessPiece piece = gameManager.GetPieceAt(newRow, newCol);
                if (piece == null || piece.pieceColor != this.pieceColor)
                {
                    attacks[newRow, newCol] = true;
                }
            }
        }
        return attacks;
    }

    public override bool[,] GetLegalMoves()
    {
        bool[,] moves = GetAttackSquares();

        int homeRow = (pieceColor == PieceColor.White) ? 0 : 7;
        if (!hasMoved && currentRow == homeRow && currentCol == 4 && gameManager != null)
        {
            PieceColor opponentColor = (pieceColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            if (!gameManager.IsSquareAttacked(homeRow, 4, opponentColor))
            {
                // Kingside castling (e -> g, rook h -> f)
                if (CanCastle(homeRow, 7, new[] { 5, 6 }, 5, opponentColor))
                {
                    moves[homeRow, 6] = true;
                }

                // Queenside castling (e -> c, rook a -> d)
                if (CanCastle(homeRow, 0, new[] { 1, 2, 3 }, 3, opponentColor))
                {
                    moves[homeRow, 2] = true;
                }
            }
        }

        return moves;
    }

    private bool CanCastle(int homeRow, int rookCol, int[] emptyCols, int passThroughCol, PieceColor opponentColor)
    {
        ChessPiece rook = gameManager.GetPieceAt(homeRow, rookCol);
        if (rook == null || rook.pieceType != PieceType.Rook || rook.pieceColor != pieceColor || rook.hasMoved)
        {
            return false;
        }

        foreach (int col in emptyCols)
        {
            if (gameManager.GetPieceAt(homeRow, col) != null)
            {
                return false;
            }
        }

        return !gameManager.IsSquareAttacked(homeRow, passThroughCol, opponentColor);
    }

    private bool IsOnBoard(int row, int col)
    {
        return row >= 0 && row < 8 && col >= 0 && col < 8;
    }
}