using UnityEngine;

public enum PieceColor { White, Black }
public enum PieceType { None, Pawn, Rook, Knight, Bishop, Queen, King }

public abstract class ChessPiece : MonoBehaviour
{
    public PieceColor pieceColor;
    public PieceType pieceType;
    private const float yAxisHeight = 0.75f;
    public int currentRow;
    public int currentCol;
    public bool hasMoved { get; protected set; }

    public abstract bool[,] GetLegalMoves();

    /// <summary>
    /// Squares threatened/attacked by this piece (excludes non-attacking moves like castling or pawn pushes).
    /// </summary>
    public virtual bool[,] GetAttackSquares() => GetLegalMoves();

    public void Initialize(int row, int col)
    {
        currentRow = row;
        currentCol = col;

        // --- THIS IS THE FIX ---
        // We set the position directly here instead of calling the virtual MoveToSquare() method.
        // This prevents the Pawn's hasMoved flag from being set during initial setup.
        transform.position = new Vector3(col, yAxisHeight, row);
    }

    public void ConfigureRuntime(PieceColor color, PieceType type, int row, int col, bool moved = true)
    {
        pieceColor = color;
        pieceType = type;
        hasMoved = moved;
        Initialize(row, col);
    }

    public virtual void MoveToSquare(int row, int col)
    {
        currentRow = row;
        currentCol = col;
        hasMoved = true;
        Vector3 newPosition = new Vector3(col, yAxisHeight, row);
        transform.position = newPosition;
    }
}