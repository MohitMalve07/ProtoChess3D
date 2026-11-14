using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro; // --- NEW: Required for UI Text ---

public class GameManager : MonoBehaviour
{
    private class MoveSnapshot
    {
        public ChessPiece movedPiece;
        public int fromRow;
        public int fromCol;
        public bool fromHasMoved;
        public int toRow;
        public int toCol;

        public ChessPiece capturedPiece;
        public int capturedRow;
        public int capturedCol;

        public ChessPiece castlingRook;
        public int rookFromCol;
        public int rookToCol;
        public bool rookFromHasMoved;

        public ChessPiece promotedQueen;
        public bool promotedPrefabUsed;

        public int prevEnPassantRow;
        public int prevEnPassantCol;
        public PieceColor prevTurn;
    }

    // --- Public Fields & Properties ---
    public LayerMask chessPieceLayer;
    public GameObject moveHighlightPrefab;
    public GameObject gameModeSelectorCanvas;
    public TextMeshProUGUI gameStatusText; // --- NEW: Drag your UI Text here ---
    public GameObject whiteQueenPrefab;
    public GameObject blackQueenPrefab;
    [SerializeField] private int aiSearchDepth = 5;
    [SerializeField] private int aiThinkTimeMs = 1500;

    [Header("HUD & Timer")]
    public GameObject hudCanvas;
    public TextMeshProUGUI playerTimerText;
    public TextMeshProUGUI opponentTimerText;
    public Button undoButton;
    public Button resignButton;
    public Button drawButton;
    [SerializeField] private float initialTimeSeconds = 900f; // 15 minutes per side

    public int EnPassantTargetRow { get; private set; } = -1;
    public int EnPassantTargetCol { get; private set; } = -1;

    // --- Game Mode ---
    public enum GameMode { PvP, PvAI }
    private GameMode currentMode;
    private bool isGameActive = false;
    private int gameStartFrame = -1;

    // --- Private Game State ---
    private Dictionary<Vector2, ChessPiece> occupiedSquares = new Dictionary<Vector2, ChessPiece>();
    private readonly Stack<MoveSnapshot> moveHistory = new Stack<MoveSnapshot>();
    private ChessPiece selectedPiece = null;
    private List<GameObject> activeHighlights = new List<GameObject>();
    private bool[,] legalMovesGrid;
    private PieceColor currentPlayerTurn = PieceColor.White;
    private bool isGameOver = false;
    private float whiteTimeRemaining;
    private float blackTimeRemaining;
    private Coroutine aiCoroutine;
    private CancellationTokenSource aiCts;
    private Task<MinimaxAI.AIMove> activeAiTask;
    private bool isAIThinking = false;

    // --- Component References ---
    private CameraToggle cameraToggle;
    private MinimaxAI ai;
    private BoardLayout boardLayout;

    #region Unity Methods

    void Start()
    {
        cameraToggle = FindAnyObjectByType<CameraToggle>();
        boardLayout = FindAnyObjectByType<BoardLayout>();
        ai = GetComponent<MinimaxAI>() ?? FindAnyObjectByType<MinimaxAI>();

        ChessPiece[] allPieces = FindObjectsByType<ChessPiece>();
        foreach (ChessPiece piece in allPieces)
        {
            occupiedSquares.Add(new Vector2(piece.currentCol, piece.currentRow), piece);
        }

        ResolveHudReferences();

        if (hudCanvas != null && gameModeSelectorCanvas != null && gameModeSelectorCanvas.activeInHierarchy)
        {
            hudCanvas.SetActive(false);
        }

        whiteTimeRemaining = initialTimeSeconds;
        blackTimeRemaining = initialTimeSeconds;
        UpdateTimerUI();

        // Ensure UI starts clean
        if (gameStatusText != null) gameStatusText.text = "";
        UpdateActionButtons();
    }

    void Update()
    {
        if (isGameOver || !isGameActive) return;

        TickTimers();
        if (isGameOver || Time.frameCount == gameStartFrame) return;

        // --- Prevent player input during AI's turn ---
        if (isAIThinking || (currentMode == GameMode.PvAI && currentPlayerTurn == PieceColor.Black)) return;

        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 100f, chessPieceLayer))
            {
                ChessPiece piece = hit.collider.GetComponent<ChessPiece>();
                if (piece != null)
                {
                    if (piece.pieceColor == currentPlayerTurn)
                    {
                        if (selectedPiece == piece) DeselectPiece();
                        else SelectPiece(piece);
                    }
                    else if (selectedPiece != null)
                    {
                        TryMoveSelectedPiece(piece.currentRow, piece.currentCol);
                    }
                }
            }
            else if (selectedPiece != null)
            {
                if (Physics.Raycast(ray, out hit) && hit.collider.gameObject.name.StartsWith("X:"))
                {
                    Vector3 hitPosition = hit.collider.transform.position;
                    int newCol, newRow;
                    if (boardLayout != null && boardLayout.TryGetBoardCoordinates(hitPosition, out newRow, out newCol))
                    {
                        TryMoveSelectedPiece(newRow, newCol);
                    }
                    else
                    {
                        newCol = Mathf.RoundToInt(hitPosition.x);
                        newRow = Mathf.RoundToInt(hitPosition.z);
                        TryMoveSelectedPiece(newRow, newCol);
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
        isGameOver = false;
        currentPlayerTurn = PieceColor.White;
        gameStartFrame = Time.frameCount;

        CancelAISearch();
        moveHistory.Clear();

        whiteTimeRemaining = initialTimeSeconds;
        blackTimeRemaining = initialTimeSeconds;
        UpdateTimerUI();

        if (hudCanvas != null)
        {
            hudCanvas.SetActive(true);
        }

        if (gameStatusText != null)
        {
            gameStatusText.text = "";
        }

        if (cameraToggle != null)
        {
            cameraToggle.ExitMainMenu();
            cameraToggle.ConfigureForSinglePlayer(currentMode == GameMode.PvAI);
        }

        if (gameModeSelectorCanvas != null)
        {
            gameModeSelectorCanvas.SetActive(false);
        }

        UpdateActionButtons();
    }

    public void UndoLastMove()
    {
        if (!isGameActive || isGameOver || moveHistory.Count == 0) return;

        if (currentMode == GameMode.PvAI)
        {
            if (isAIThinking || aiCoroutine != null)
            {
                CancelAISearch();
                UndoSinglePly();
            }
            else
            {
                int pliesToUndo = Mathf.Min(2, moveHistory.Count);
                for (int i = 0; i < pliesToUndo; i++)
                {
                    UndoSinglePly();
                    if (currentPlayerTurn == PieceColor.White) break;
                }
            }
        }
        else
        {
            UndoSinglePly();
        }

        if (cameraToggle != null)
        {
            cameraToggle.SetBlackSide(currentPlayerTurn == PieceColor.Black);
        }

        CheckForGameOver();
        UpdateActionButtons();
    }

    public void ResignGame()
    {
        if (!isGameActive || isGameOver) return;

        string result = currentMode == GameMode.PvAI
            ? "White Resigned!\nBlack Wins!"
            : (currentPlayerTurn == PieceColor.White ? "White Resigned!\nBlack Wins!" : "Black Resigned!\nWhite Wins!");
        EndGameWithResult(result);
    }

    public void DrawGame()
    {
        if (!isGameActive || isGameOver) return;
        EndGameWithResult("Draw!\nGame Ended in a Draw.");
    }

    private void UndoSinglePly()
    {
        if (moveHistory.Count == 0) return;

        DeselectPiece();
        MoveSnapshot snap = moveHistory.Pop();

        occupiedSquares.Remove(new Vector2(snap.toCol, snap.toRow));

        if (snap.promotedQueen != null)
        {
            if (snap.promotedPrefabUsed)
            {
                Destroy(snap.promotedQueen.gameObject);
                snap.movedPiece.gameObject.SetActive(true);
            }
            else
            {
                Destroy(snap.promotedQueen);
                snap.movedPiece.enabled = true;
            }
        }

        snap.movedPiece.RestoreState(snap.fromRow, snap.fromCol, snap.fromHasMoved);
        occupiedSquares[new Vector2(snap.fromCol, snap.fromRow)] = snap.movedPiece;

        if (snap.castlingRook != null)
        {
            occupiedSquares.Remove(new Vector2(snap.rookToCol, snap.fromRow));
            snap.castlingRook.RestoreState(snap.fromRow, snap.rookFromCol, snap.rookFromHasMoved);
            occupiedSquares[new Vector2(snap.rookFromCol, snap.fromRow)] = snap.castlingRook;
        }

        if (snap.capturedPiece != null)
        {
            snap.capturedPiece.gameObject.SetActive(true);
            occupiedSquares[new Vector2(snap.capturedCol, snap.capturedRow)] = snap.capturedPiece;
        }

        EnPassantTargetRow = snap.prevEnPassantRow;
        EnPassantTargetCol = snap.prevEnPassantCol;
        currentPlayerTurn = snap.prevTurn;
    }

    private void ResolveHudReferences()
    {
        if (hudCanvas == null)
        {
            GameObject foundHud = GameObject.Find("HUDCanvas");
            if (foundHud != null) hudCanvas = foundHud;
        }

        if (hudCanvas == null) return;

        if (playerTimerText == null)
            playerTimerText = hudCanvas.transform.Find("Player Timer/Remaining Time")?.GetComponent<TextMeshProUGUI>();
        if (opponentTimerText == null)
            opponentTimerText = hudCanvas.transform.Find("Opponent Timer/Remaining Time")?.GetComponent<TextMeshProUGUI>();
        if (undoButton == null)
            undoButton = hudCanvas.transform.Find("Buttons/Undo")?.GetComponent<Button>();
        if (resignButton == null)
            resignButton = hudCanvas.transform.Find("Buttons/Resign")?.GetComponent<Button>();
        if (drawButton == null)
            drawButton = hudCanvas.transform.Find("Buttons/Draw")?.GetComponent<Button>();
    }

    private void TickTimers()
    {
        if (currentPlayerTurn == PieceColor.White)
        {
            whiteTimeRemaining -= Time.deltaTime;
            if (whiteTimeRemaining <= 0f)
            {
                whiteTimeRemaining = 0f;
                UpdateTimerUI();
                EndGameWithResult("Time's Up!\nBlack Wins!");
                return;
            }
        }
        else
        {
            blackTimeRemaining -= Time.deltaTime;
            if (blackTimeRemaining <= 0f)
            {
                blackTimeRemaining = 0f;
                UpdateTimerUI();
                EndGameWithResult("Time's Up!\nWhite Wins!");
                return;
            }
        }

        UpdateTimerUI();
    }

    private void UpdateTimerUI()
    {
        if (playerTimerText != null)
        {
            playerTimerText.text = FormatTime(whiteTimeRemaining);
        }

        if (opponentTimerText != null)
        {
            opponentTimerText.text = FormatTime(blackTimeRemaining);
        }
    }

    private static string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        return $"{minutes:00}:{secs:00}";
    }

    private void UpdateActionButtons()
    {
        bool activeMatch = isGameActive && !isGameOver;
        if (undoButton != null) undoButton.interactable = activeMatch && moveHistory.Count > 0;
        if (resignButton != null) resignButton.interactable = activeMatch;
        if (drawButton != null) drawButton.interactable = activeMatch;
    }

    private void EndGameWithResult(string message)
    {
        isGameOver = true;
        CancelAISearch();
        DeselectPiece();
        if (gameStatusText != null) gameStatusText.text = message;
        UpdateActionButtons();
    }

    private void CancelAISearch()
    {
        isAIThinking = false;
        if (aiCoroutine != null)
        {
            StopCoroutine(aiCoroutine);
            aiCoroutine = null;
        }
        if (aiCts != null)
        {
            aiCts.Cancel();
        }
    }

    private void OnDestroy()
    {
        CancelAISearch();
    }

    #region Core Game Logic

    /// <summary>
    /// Moves the currently selected piece to the target square if it is a legal move, then ends the turn.
    /// </summary>
    private void TryMoveSelectedPiece(int targetRow, int targetCol)
    {
        if (selectedPiece == null || legalMovesGrid == null) return;
        if (targetRow < 0 || targetRow >= 8 || targetCol < 0 || targetCol >= 8) return;

        if (legalMovesGrid[targetRow, targetCol])
        {
            HandleMove(selectedPiece, targetRow, targetCol);
            DeselectPiece();
            SwitchTurn();
        }
    }

    private void HandleMove(ChessPiece pieceToMove, int newRow, int newCol)
    {
        int oldRow = pieceToMove.currentRow;
        int oldCol = pieceToMove.currentCol;

        MoveSnapshot snapshot = new MoveSnapshot
        {
            movedPiece = pieceToMove,
            fromRow = oldRow,
            fromCol = oldCol,
            fromHasMoved = pieceToMove.hasMoved,
            toRow = newRow,
            toCol = newCol,
            prevEnPassantRow = EnPassantTargetRow,
            prevEnPassantCol = EnPassantTargetCol,
            prevTurn = currentPlayerTurn
        };

        ChessPiece targetPiece = GetPieceAt(newRow, newCol);
        bool isEnPassantCapture = pieceToMove.pieceType == PieceType.Pawn &&
                                  targetPiece == null &&
                                  newRow == EnPassantTargetRow &&
                                  newCol == EnPassantTargetCol &&
                                  newCol != oldCol;

        if (isEnPassantCapture)
        {
            ChessPiece capturedPawn = GetPieceAt(oldRow, newCol);
            if (capturedPawn != null)
            {
                snapshot.capturedPiece = capturedPawn;
                snapshot.capturedRow = oldRow;
                snapshot.capturedCol = newCol;
                occupiedSquares.Remove(new Vector2(newCol, oldRow));
                capturedPawn.gameObject.SetActive(false);
            }
        }
        else if (targetPiece != null)
        {
            snapshot.capturedPiece = targetPiece;
            snapshot.capturedRow = newRow;
            snapshot.capturedCol = newCol;
            occupiedSquares.Remove(new Vector2(newCol, newRow));
            targetPiece.gameObject.SetActive(false);
        }

        occupiedSquares.Remove(new Vector2(oldCol, oldRow));
        occupiedSquares.Add(new Vector2(newCol, newRow), pieceToMove);
        pieceToMove.MoveToSquare(newRow, newCol);

        // Castling: move the corresponding rook
        if (pieceToMove.pieceType == PieceType.King && Mathf.Abs(newCol - oldCol) == 2)
        {
            int rookFromCol = (newCol == 6) ? 7 : 0;
            int rookToCol = (newCol == 6) ? 5 : 3;
            ChessPiece rook = GetPieceAt(oldRow, rookFromCol);
            if (rook != null && rook.pieceType == PieceType.Rook)
            {
                snapshot.castlingRook = rook;
                snapshot.rookFromCol = rookFromCol;
                snapshot.rookToCol = rookToCol;
                snapshot.rookFromHasMoved = rook.hasMoved;
                occupiedSquares.Remove(new Vector2(rookFromCol, oldRow));
                occupiedSquares.Add(new Vector2(rookToCol, oldRow), rook);
                rook.MoveToSquare(oldRow, rookToCol);
            }
        }

        // Track en passant vulnerability for the immediately following turn
        if (pieceToMove.pieceType == PieceType.Pawn && Mathf.Abs(newRow - oldRow) == 2)
        {
            EnPassantTargetRow = (oldRow + newRow) / 2;
            EnPassantTargetCol = newCol;
        }
        else
        {
            EnPassantTargetRow = -1;
            EnPassantTargetCol = -1;
        }

        // Auto-promote pawns reaching the final rank to Queen
        if (pieceToMove.pieceType == PieceType.Pawn &&
            ((pieceToMove.pieceColor == PieceColor.White && newRow == 7) ||
             (pieceToMove.pieceColor == PieceColor.Black && newRow == 0)))
        {
            PromotePawnToQueen(pieceToMove, newRow, newCol, snapshot);
        }

        moveHistory.Push(snapshot);
    }

    private void PromotePawnToQueen(ChessPiece pawn, int row, int col, MoveSnapshot snapshot)
    {
        PieceColor color = pawn.pieceColor;
        GameObject prefab = (color == PieceColor.White) ? whiteQueenPrefab : blackQueenPrefab;

        if (prefab != null)
        {
            GameObject queenObj = Instantiate(prefab, pawn.transform.parent);
            queenObj.layer = pawn.gameObject.layer;

            BoxCollider colComp = queenObj.GetComponent<BoxCollider>();
            if (colComp == null)
            {
                colComp = queenObj.AddComponent<BoxCollider>();
                colComp.center = new Vector3(0f, 0.7f, 0f);
                colComp.size = new Vector3(0.5f, 1f, 0.5f);
            }

            Queen queen = queenObj.GetComponent<Queen>();
            if (queen == null) queen = queenObj.AddComponent<Queen>();
            queen.ConfigureRuntime(color, PieceType.Queen, row, col, true);

            occupiedSquares[new Vector2(col, row)] = queen;
            pawn.gameObject.SetActive(false);

            snapshot.promotedQueen = queen;
            snapshot.promotedPrefabUsed = true;
        }
        else
        {
            Queen queen = pawn.gameObject.AddComponent<Queen>();
            queen.ConfigureRuntime(color, PieceType.Queen, row, col, true);
            occupiedSquares[new Vector2(col, row)] = queen;
            pawn.enabled = false;

            snapshot.promotedQueen = queen;
            snapshot.promotedPrefabUsed = false;
        }
    }

    private void SwitchTurn()
    {
        currentPlayerTurn = (currentPlayerTurn == PieceColor.White) ? PieceColor.Black : PieceColor.White;
        if (cameraToggle != null) cameraToggle.ToggleView();

        CheckForGameOver();
        UpdateActionButtons();

        if (currentMode == GameMode.PvAI && currentPlayerTurn == PieceColor.Black && !isGameOver)
        {
            aiCoroutine = StartCoroutine(TriggerAIMove());
        }
    }

    private IEnumerator TriggerAIMove()
    {
        isAIThinking = true;

        // Ensure any previously cancelled search finishes before reusing MinimaxAI's internal TT buffer
        while (activeAiTask != null && !activeAiTask.IsCompleted)
        {
            yield return null;
        }

        if (aiCts != null)
        {
            aiCts.Dispose();
            aiCts = null;
        }

        yield return new WaitForSeconds(0.1f);
        if (isGameOver || !isGameActive || currentMode != GameMode.PvAI || currentPlayerTurn != PieceColor.Black)
        {
            isAIThinking = false;
            aiCoroutine = null;
            yield break;
        }

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

        MinimaxAI.SearchRights rights = BuildSearchRights();
        int depth = Mathf.Max(1, aiSearchDepth);
        int thinkMs = Mathf.Max(50, aiThinkTimeMs);

        CancellationTokenSource localCts = new CancellationTokenSource();
        aiCts = localCts;
        CancellationToken token = localCts.Token;

        Task<MinimaxAI.AIMove> searchTask = Task.Run(
            () => ai.GetBestMove(currentBoard, depth, rights, thinkMs, token),
            token);
        activeAiTask = searchTask;

        while (!searchTask.IsCompleted)
        {
            if (isGameOver || token.IsCancellationRequested)
            {
                localCts.Cancel();
                isAIThinking = false;
                aiCoroutine = null;
                yield break;
            }
            yield return null;
        }

        isAIThinking = false;
        aiCoroutine = null;

        if (isGameOver || token.IsCancellationRequested || searchTask.IsCanceled)
        {
            yield break;
        }

        if (searchTask.IsFaulted)
        {
            Debug.LogException(searchTask.Exception);
            yield break;
        }

        if (aiCts == localCts)
        {
            aiCts.Dispose();
            aiCts = null;
        }

        MinimaxAI.AIMove bestMove = searchTask.Result;
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

    private MinimaxAI.SearchRights BuildSearchRights()
    {
        bool CanCastleWith(int row, int kingCol, int rookCol, PieceColor color)
        {
            ChessPiece king = GetPieceAt(row, kingCol);
            ChessPiece rook = GetPieceAt(row, rookCol);
            return king != null && king.pieceType == PieceType.King && king.pieceColor == color && !king.hasMoved &&
                   rook != null && rook.pieceType == PieceType.Rook && rook.pieceColor == color && !rook.hasMoved;
        }

        return new MinimaxAI.SearchRights
        {
            whiteCanCastleKingSide = CanCastleWith(0, 4, 7, PieceColor.White),
            whiteCanCastleQueenSide = CanCastleWith(0, 4, 0, PieceColor.White),
            blackCanCastleKingSide = CanCastleWith(7, 4, 7, PieceColor.Black),
            blackCanCastleQueenSide = CanCastleWith(7, 4, 0, PieceColor.Black),
            enPassantRow = EnPassantTargetRow,
            enPassantCol = EnPassantTargetCol
        };
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
            if (king != null && IsSquareAttacked(king.currentRow, king.currentCol, opponentColor))
            {
                // CHECKMATE
                string winner = (opponentColor == PieceColor.White) ? "White" : "Black";
                Debug.Log("CHECKMATE! " + winner + " wins!");
                EndGameWithResult($"Checkmate!\n{winner} Wins!");
            }
            else
            {
                // STALEMATE
                Debug.Log("STALEMATE! It's a draw!");
                EndGameWithResult("Stalemate!\nIt's a draw.");
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
                    bool isEnPassant = piece.pieceType == PieceType.Pawn &&
                                       pieceAtDestination == null &&
                                       row == EnPassantTargetRow &&
                                       col == EnPassantTargetCol &&
                                       col != originalCol;
                    ChessPiece enPassantPawn = isEnPassant ? GetPieceAt(originalRow, col) : null;

                    occupiedSquares.Remove(new Vector2(originalCol, originalRow));
                    if (pieceAtDestination != null) occupiedSquares.Remove(new Vector2(col, row));
                    if (enPassantPawn != null) occupiedSquares.Remove(new Vector2(col, originalRow));
                    occupiedSquares.Add(new Vector2(col, row), piece);
                    piece.currentRow = row;
                    piece.currentCol = col;

                    PieceColor opponentColor = (piece.pieceColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
                    bool isKingInCheck = myKing != null && IsSquareAttacked(myKing.currentRow, myKing.currentCol, opponentColor);

                    if (!isKingInCheck)
                    {
                        validMoves[row, col] = true;
                    }

                    occupiedSquares.Remove(new Vector2(col, row));
                    occupiedSquares.Add(new Vector2(originalCol, originalRow), piece);
                    if (pieceAtDestination != null) occupiedSquares.Add(new Vector2(col, row), pieceAtDestination);
                    if (enPassantPawn != null) occupiedSquares.Add(new Vector2(col, originalRow), enPassantPawn);
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
                bool[,] threatenedSquares = piece.GetAttackSquares();
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
                    Vector3 highlightPosition = boardLayout != null
                        ? boardLayout.GetSquareWorldPosition(row, col, 0.75f)
                        : new Vector3(col, 0.75f, row);
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