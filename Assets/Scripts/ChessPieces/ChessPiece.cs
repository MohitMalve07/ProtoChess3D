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

    public abstract bool[,] GetLegalMoves();

    public void Initialize(int row, int col)
    {
        currentRow = row;
        currentCol = col;

        // --- THIS IS THE FIX ---
        // We set the position directly here instead of calling the virtual MoveToSquare() method.
        // This prevents the Pawn's hasMoved flag from being set during initial setup.
        transform.position = new Vector3(col, yAxisHeight, row);
    }

    public virtual void MoveToSquare(int row, int col)
    {
        currentRow = row;
        currentCol = col;
        Vector3 newPosition = new Vector3(col, yAxisHeight, row);
        transform.position = newPosition;
    }
}