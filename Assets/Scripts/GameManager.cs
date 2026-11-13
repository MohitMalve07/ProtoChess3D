using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using TMPro; // --- NEW: Required for UI Text ---

public class GameManager : MonoBehaviour
{
    // --- Public Fields & Properties ---
    public LayerMask chessPieceLayer;
    public GameObject moveHighlightPrefab;
    public GameObject gameModeSelectorCanvas;
    public TextMeshProUGUI gameStatusText; // --- NEW: Drag your UI Text here ---

    // --- Game Mode ---
    public enum GameMode { PvP, PvAI }
    private GameMode currentMode;
    private bool isGameActive = false;

    // --- Private Game State ---
    private Dictionary<Vector2, ChessPiece> occupiedSquares = new Dictionary<Vector2, ChessPiece>();
    private ChessPiece selectedPiece = null;
    private List<GameObject> activeHighlights = new List<GameObject>();
    private bool[,] legalMovesGrid;
    private PieceColor currentPlayerTurn = PieceColor.White;
    private bool isGameOver = false;

    // --- Component References ---
    private CameraToggle cameraToggle;
    private MinimaxAI ai;

    #region Unity Methods

    void Start()
    {
        cameraToggle = FindObjectOfType<CameraToggle>();
        ai = GetComponent<MinimaxAI>();

        ChessPiece[] allPieces = FindObjectsOfType<ChessPiece>();
        foreach (ChessPiece piece in allPieces)
        {
            occupiedSquares.Add(new Vector2(piece.currentCol, piece.currentRow), piece);
        }

        // Ensure text is clear at start
        if (gameStatusText != null) gameStatusText.text = "";
    }

    void Update()
    {
        if (isGameOver || !isGameActive) return;

        // --- Prevent player input during AI's turn ---
        if (currentMode == GameMode.PvAI && currentPlayerTurn == PieceColor.Black) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 100f, chessPieceLayer))
            {
                ChessPiece piece = hit.collider.GetComponent<ChessPiece>();
                if (piece != null && piece.pieceColor == currentPlayerTurn)
                {
                    if (selectedPiece == piece) DeselectPiece();
                    else SelectPiece(piece);
                }
            }
            else if (selectedPiece != null)
            {
                if (Physics.Raycast(ray, out hit) && hit.collider.gameObject.name.StartsWith("X:"))
                {
                    Vector3 hitPosition = hit.collider.transform.position;
                    int newCol = Mathf.RoundToInt(hitPosition.x);
                    int newRow = Mathf.RoundToInt(hitPosition.z);

                    if (legalMovesGrid[newRow, newCol])
                    {
                        HandleMove(selectedPiece, newRow, newCol);
                        DeselectPiece();
                        SwitchTurn();
                    }
                }
            }
        }
    }

    #endregion

    public void StartGame(int mode)
    {
        currentMode = (GameMode)mode;
        isGameActive = true;

        if (cameraToggle != null)
        {
            cameraToggle.ConfigureForSinglePlayer(currentMode == GameMode.PvAI);
        }

        if (gameModeSelectorCanvas != null)
        {
            gameModeSelectorCanvas.SetActive(false);
        }
    }

    #region Core Game Logic

    private void HandleMove(ChessPiece pieceToMove, int newRow, int newCol)
    {
        ChessPiece targetPiece = GetPieceAt(newRow, newCol);
        if (targetPiece != null)
        {
            occupiedSquares.Remove(new Vector2(newCol, newRow));
            Destroy(targetPiece.gameObject);
        }

        occupiedSquares.Remove(new Vector2(pieceToMove.currentCol, pieceToMove.currentRow));
        occupiedSquares.Add(new Vector2(newCol, newRow), pieceToMove);

        pieceToMove.MoveToSquare(newRow, newCol);
    }

    private void SwitchTurn()
    {
        currentPlayerTurn = (currentPlayerTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        if (cameraToggle != null) cameraToggle.ToggleView();

        CheckForGameOver();

        if (currentMode == GameMode.PvAI && currentPlayerTurn == PieceColor.Black && !isGameOver)
        {
            StartCoroutine(TriggerAIMove());
        }
    }

    private IEnumerator TriggerAIMove()
    {
        MinimaxAI.InternalPiece[,] currentBoard = new MinimaxAI.InternalPiece[8, 8];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                ChessPiece piece = GetPieceAt(r, c);
                if (piece != null)
                {
                    currentBoard[r, c] = new MinimaxAI.InternalPiece { type = piece.pieceType, color = piece.pieceColor };
                }
                else
                {
                    currentBoard[r, c] = new MinimaxAI.InternalPiece { type = PieceType.None, color = PieceColor.White };
                }
            }
        }

        yield return new WaitForSeconds(0.1f);

        MinimaxAI.AIMove bestMove = ai.GetBestMove(currentBoard, 3);

        if (bestMove.fromRow != bestMove.toRow || bestMove.fromCol != bestMove.toCol)
        {
            ChessPiece pieceToMove = GetPieceAt(bestMove.fromRow, bestMove.fromCol);
            if (pieceToMove != null)
            {
                HandleMove(pieceToMove, bestMove.toRow, bestMove.toCol);
                SwitchTurn();
            }
        }
        else
        {
            Debug.Log("AI could not find a valid move.");
        }
    }

    // --- MODIFIED: Updates UI for Check and Checkmate ---
    private void CheckForGameOver()
    {
        List<ChessPiece> currentPlayerPieces = new List<ChessPiece>();
        foreach (ChessPiece piece in occupiedSquares.Values)
        {
            if (piece.pieceColor == currentPlayerTurn) currentPlayerPieces.Add(piece);
        }

        bool hasValidMoves = false;
        foreach (ChessPiece piece in currentPlayerPieces)
        {
            bool[,] validMoves = GetValidMovesForPiece(piece);
            foreach (bool move in validMoves)
            {
                if (move)
                {
                    hasValidMoves = true;
                    break;
                }
            }
            if (hasValidMoves) break;
        }

        // Identify the King and Opponent Color
        PieceColor opponentColor = (currentPlayerTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        ChessPiece king = null;
        foreach (ChessPiece p in occupiedSquares.Values)
        {
            if (p.pieceType == PieceType.King && p.pieceColor == currentPlayerTurn)
            {
                king = p;
                break;
            }
        }

        if (!hasValidMoves)
        {
            isGameOver = true;

            if (IsSquareAttacked(king.currentRow, king.currentCol, opponentColor))
            {
                // CHECKMATE
                string winner = (opponentColor == PieceColor.White) ? "White" : "Black";
                Debug.Log("CHECKMATE! " + winner + " wins!");
                if (gameStatusText != null) gameStatusText.text = $"Checkmate!\n{winner} Wins!";
            }
            else
            {
                // STALEMATE
                Debug.Log("STALEMATE! It's a draw!");
                if (gameStatusText != null) gameStatusText.text = "Stalemate!\nIt's a draw.";
            }
        }
        else
        {
            // Game continues, but check if King is currently under attack (Check)
            if (king != null && IsSquareAttacked(king.currentRow, king.currentCol, opponentColor))
            {
                if (gameStatusText != null) gameStatusText.text = "Check!";
            }
            else
            {
                if (gameStatusText != null) gameStatusText.text = ""; // Clear text if safe
            }
        }
    }

    #endregion

    #region Move Generation & Validation

    private bool[,] GetValidMovesForPiece(ChessPiece piece)
    {
        bool[,] pseudoLegalMoves = piece.GetLegalMoves();
        bool[,] validMoves = new bool[8, 8];

        ChessPiece myKing = null;
        foreach (ChessPiece p in occupiedSquares.Values)
        {
            if (p.pieceType == PieceType.King && p.pieceColor == piece.pieceColor)
            {
                myKing = p;
                break;
            }
        }

        int originalRow = piece.currentRow;
        int originalCol = piece.currentCol;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (pseudoLegalMoves[row, col])
                {
                    ChessPiece pieceAtDestination = GetPieceAt(row, col);

                    occupiedSquares.Remove(new Vector2(originalCol, originalRow));
                    if (pieceAtDestination != null) occupiedSquares.Remove(new Vector2(col, row));
                    occupiedSquares.Add(new Vector2(col, row), piece);
                    piece.currentRow = row;
                    piece.currentCol = col;

                    PieceColor opponentColor = (piece.pieceColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
                    bool isKingInCheck = IsSquareAttacked(myKing.currentRow, myKing.currentCol, opponentColor);

                    if (!isKingInCheck)
                    {
                        validMoves[row, col] = true;
                    }

                    occupiedSquares.Remove(new Vector2(col, row));
                    occupiedSquares.Add(new Vector2(originalCol, originalRow), piece);
                    if (pieceAtDestination != null) occupiedSquares.Add(new Vector2(col, row), pieceAtDestination);
                    piece.currentRow = originalRow;
                    piece.currentCol = originalCol;
                }
            }
        }
        return validMoves;
    }

    public bool IsSquareAttacked(int row, int col, PieceColor attackingColor)
    {
        foreach (ChessPiece piece in occupiedSquares.Values)
        {
            if (piece.pieceColor == attackingColor)
            {
                bool[,] threatenedSquares = piece.GetLegalMoves();
                if (threatenedSquares[row, col])
                {
                    return true;
                }
            }
        }
        return false;
    }

    #endregion

    #region Piece Selection & UI

    private void SelectPiece(ChessPiece piece)
    {
        DeselectPiece();
        selectedPiece = piece;
        legalMovesGrid = GetValidMovesForPiece(selectedPiece);
        ShowLegalMoves();
    }

    private void DeselectPiece()
    {
        if (selectedPiece != null)
        {
            selectedPiece = null;
            ClearLegalMoves();
        }
    }

    private void ShowLegalMoves()
    {
        if (moveHighlightPrefab == null) return;
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (legalMovesGrid[row, col])
                {
                    Vector3 highlightPosition = new Vector3(col, 0.75f, row);
                    GameObject highlight = Instantiate(moveHighlightPrefab, highlightPosition, Quaternion.identity);
                    activeHighlights.Add(highlight);
                }
            }
        }
    }

    private void ClearLegalMoves()
    {
        foreach (var highlight in activeHighlights)
        {
            Destroy(highlight);
        }
        activeHighlights.Clear();
        legalMovesGrid = null;
    }

    public ChessPiece GetPieceAt(int row, int col)
    {
        Vector2 position = new Vector2(col, row);
        return occupiedSquares.ContainsKey(position) ? occupiedSquares[position] : null;
    }

    #endregion
}