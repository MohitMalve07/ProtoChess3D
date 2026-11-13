using UnityEngine;
using System.Collections.Generic;

public class MinimaxAI : MonoBehaviour
{
    // --- Data Structures ---
    public struct AIMove
    {
        public int fromRow, fromCol, toRow, toCol;
    }

    public struct InternalPiece
    {
        public PieceType type;
        public PieceColor color;

        public int GetMaterialValue()
        {
            switch (type)
            {
                case PieceType.Pawn: return 100; // Increased values for better granularity
                case PieceType.Knight: return 320;
                case PieceType.Bishop: return 330;
                case PieceType.Rook: return 500;
                case PieceType.Queen: return 900;
                case PieceType.King: return 20000;
                default: return 0;
            }
        }
    }

    // --- Piece-Square Tables (Positional Evaluation) ---
    // These tables define where pieces "like" to be. (Defined from White's perspective)
    // Positive values = Good position.

    private static readonly int[,] pawnTable = new int[,]
    {
        { 0,  0,  0,  0,  0,  0,  0,  0 },
        { 50, 50, 50, 50, 50, 50, 50, 50 },
        { 10, 10, 20, 30, 30, 20, 10, 10 },
        { 5,  5, 10, 25, 25, 10,  5,  5 },
        { 0,  0,  0, 20, 20,  0,  0,  0 },
        { 5, -5,-10,  0,  0,-10, -5,  5 },
        { 5, 10, 10,-20,-20, 10, 10,  5 },
        { 0,  0,  0,  0,  0,  0,  0,  0 }
    };

    private static readonly int[,] knightTable = new int[,]
    {
        {-50,-40,-30,-30,-30,-30,-40,-50 },
        {-40,-20,  0,  0,  0,  0,-20,-40 },
        {-30,  0, 10, 15, 15, 10,  0,-30 },
        {-30,  5, 15, 20, 20, 15,  5,-30 },
        {-30,  0, 15, 20, 20, 15,  0,-30 },
        {-30,  5, 10, 15, 15, 10,  5,-30 },
        {-40,-20,  0,  5,  5,  0,-20,-40 },
        {-50,-40,-30,-30,-30,-30,-40,-50 }
    };

    private static readonly int[,] bishopTable = new int[,]
    {
        {-20,-10,-10,-10,-10,-10,-10,-20 },
        {-10,  0,  0,  0,  0,  0,  0,-10 },
        {-10,  0,  5, 10, 10,  5,  0,-10 },
        {-10,  5,  5, 10, 10,  5,  5,-10 },
        {-10,  0, 10, 10, 10, 10,  0,-10 },
        {-10, 10, 10, 10, 10, 10, 10,-10 },
        {-10,  5,  0,  0,  0,  0,  5,-10 },
        {-20,-10,-10,-10,-10,-10,-10,-20 }
    };

    private static readonly int[,] rookTable = new int[,]
    {
        { 0,  0,  0,  0,  0,  0,  0,  0 },
        { 5, 10, 10, 10, 10, 10, 10,  5 },
        {-5,  0,  0,  0,  0,  0,  0, -5 },
        {-5,  0,  0,  0,  0,  0,  0, -5 },
        {-5,  0,  0,  0,  0,  0,  0, -5 },
        {-5,  0,  0,  0,  0,  0,  0, -5 },
        {-5,  0,  0,  0,  0,  0,  0, -5 },
        { 0,  0,  0,  5,  5,  0,  0,  0 }
    };

    private static readonly int[,] queenTable = new int[,]
    {
        {-20,-10,-10, -5, -5,-10,-10,-20 },
        {-10,  0,  0,  0,  0,  0,  0,-10 },
        {-10,  0,  5,  5,  5,  5,  0,-10 },
        { -5,  0,  5,  5,  5,  5,  0, -5 },
        {  0,  0,  5,  5,  5,  5,  0, -5 },
        {-10,  5,  5,  5,  5,  5,  0,-10 },
        {-10,  0,  5,  0,  0,  0,  0,-10 },
        {-20,-10,-10, -5, -5,-10,-10,-20 }
    };

    // Encourages King safety in corners
    private static readonly int[,] kingTable = new int[,]
    {
        {-30,-40,-40,-50,-50,-40,-40,-30 },
        {-30,-40,-40,-50,-50,-40,-40,-30 },
        {-30,-40,-40,-50,-50,-40,-40,-30 },
        {-30,-40,-40,-50,-50,-40,-40,-30 },
        {-20,-30,-30,-40,-40,-30,-30,-20 },
        {-10,-20,-20,-20,-20,-20,-20,-10 },
        { 20, 20,  0,  0,  0,  0, 20, 20 },
        { 20, 30, 10,  0,  0, 10, 30, 20 }
    };


    // --- Improved Evaluation Function ---
    private int Evaluate(InternalPiece[,] board)
    {
        int score = 0;
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                InternalPiece piece = board[r, c];
                if (piece.type != PieceType.None)
                {
                    // 1. Material Score
                    int materialValue = piece.GetMaterialValue();

                    // 2. Positional Score (Piece-Square Tables)
                    // If White, use table[r, c]. If Black, mirror it to table[7-r, c].
                    int positionValue = 0;
                    int tableRow = (piece.color == PieceColor.White) ? r : 7 - r;

                    switch (piece.type)
                    {
                        case PieceType.Pawn: positionValue = pawnTable[tableRow, c]; break;
                        case PieceType.Knight: positionValue = knightTable[tableRow, c]; break;
                        case PieceType.Bishop: positionValue = bishopTable[tableRow, c]; break;
                        case PieceType.Rook: positionValue = rookTable[tableRow, c]; break;
                        case PieceType.Queen: positionValue = queenTable[tableRow, c]; break;
                        case PieceType.King: positionValue = kingTable[tableRow, c]; break;
                    }

                    int totalValue = materialValue + positionValue;

                    if (piece.color == PieceColor.Black) score += totalValue;
                    else score -= totalValue;
                }
            }
        }
        return score;
    }

    // --- Main Move Logic ---

    public AIMove GetBestMove(InternalPiece[,] board, int depth)
    {
        List<AIMove> possibleMoves = GenerateMoves(board, PieceColor.Black);

        // --- IMPROVEMENT: Track ALL best moves, not just the first one found ---
        List<AIMove> bestMoves = new List<AIMove>();
        int bestValue = -999999;

        foreach (AIMove move in possibleMoves)
        {
            InternalPiece[,] nextBoardState = SimulateMove(board, move);
            int boardValue = Minimax(nextBoardState, depth - 1, -100000, 100000, false);

            if (boardValue > bestValue)
            {
                bestValue = boardValue;
                bestMoves.Clear(); // Found a strictly better move, clear the old list
                bestMoves.Add(move);
            }
            else if (boardValue == bestValue)
            {
                bestMoves.Add(move); // Found another equally good move, add it
            }
        }

        // --- IMPROVEMENT: Randomly select one of the best moves ---
        if (bestMoves.Count > 0)
        {
            return bestMoves[Random.Range(0, bestMoves.Count)];
        }

        return new AIMove(); // No moves available (Checkmate/Stalemate)
    }

    // --- Standard Minimax & Helpers (Unchanged Logic, just ensuring structure is correct) ---

    private int Minimax(InternalPiece[,] board, int depth, int alpha, int beta, bool isMaximizing)
    {
        if (depth == 0) return Evaluate(board);

        if (isMaximizing) // Black
        {
            int maxEval = -999999;
            List<AIMove> moves = GenerateMoves(board, PieceColor.Black);
            foreach (AIMove move in moves)
            {
                InternalPiece[,] nextBoardState = SimulateMove(board, move);
                int eval = Minimax(nextBoardState, depth - 1, alpha, beta, false);
                maxEval = Mathf.Max(maxEval, eval);
                alpha = Mathf.Max(alpha, eval);
                if (beta <= alpha) break;
            }
            return maxEval;
        }
        else // White
        {
            int minEval = 999999;
            List<AIMove> moves = GenerateMoves(board, PieceColor.White);
            foreach (AIMove move in moves)
            {
                InternalPiece[,] nextBoardState = SimulateMove(board, move);
                int eval = Minimax(nextBoardState, depth - 1, alpha, beta, true);
                minEval = Mathf.Min(minEval, eval);
                beta = Mathf.Min(beta, eval);
                if (beta <= alpha) break;
            }
            return minEval;
        }
    }

    // --- Move Generation (Must match your existing Phase 3 logic) ---
    // Make sure to include all your GetPawnMoves, GetRookMoves, etc. functions here.
    // I am including the structure, please ensure the body of these methods matches what you had before.

    public List<AIMove> GenerateMoves(InternalPiece[,] board, PieceColor color)
    {
        List<AIMove> moves = new List<AIMove>();
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                InternalPiece piece = board[r, c];
                if (piece.type != PieceType.None && piece.color == color)
                {
                    switch (piece.type)
                    {
                        case PieceType.Pawn: GetPawnMoves(board, r, c, color, moves); break;
                        case PieceType.Rook: GetRookMoves(board, r, c, color, moves); break;
                        case PieceType.Knight: GetKnightMoves(board, r, c, color, moves); break;
                        case PieceType.Bishop: GetBishopMoves(board, r, c, color, moves); break;
                        case PieceType.Queen: GetQueenMoves(board, r, c, color, moves); break;
                        case PieceType.King: GetKingMoves(board, r, c, color, moves); break;
                    }
                }
            }
        }
        return moves;
    }

    // --- Insert your private GetPawnMoves, GetRookMoves, etc. here from the previous step ---
    // (For brevity, assuming you copy them from the previous working version)

    private void GetPawnMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves)
    {
        int direction = (color == PieceColor.White) ? 1 : -1;
        int oneStepRow = r + direction;
        if (IsOnBoard(oneStepRow, c) && board[oneStepRow, c].type == PieceType.None)
        {
            AddMove(r, c, oneStepRow, c, moves);
            bool onStartRank = (color == PieceColor.White && r == 1) || (color == PieceColor.Black && r == 6);
            int twoStepRow = r + 2 * direction;
            if (onStartRank && IsOnBoard(twoStepRow, c) && board[twoStepRow, c].type == PieceType.None)
                AddMove(r, c, twoStepRow, c, moves);
        }
        int[] captureCols = { c - 1, c + 1 };
        foreach (int colOffset in captureCols)
        {
            if (IsOnBoard(oneStepRow, colOffset))
            {
                InternalPiece piece = board[oneStepRow, colOffset];
                if (piece.type != PieceType.None && piece.color != color)
                    AddMove(r, c, oneStepRow, colOffset, moves);
            }
        }
    }
    private void GetRookMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves)
    {
        CheckSlidingDirection(board, r, c, color, moves, 1, 0);
        CheckSlidingDirection(board, r, c, color, moves, -1, 0);
        CheckSlidingDirection(board, r, c, color, moves, 0, 1);
        CheckSlidingDirection(board, r, c, color, moves, 0, -1);
    }
    private void GetKnightMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves)
    {
        int[] rowOffsets = { 2, 2, -2, -2, 1, 1, -1, -1 };
        int[] colOffsets = { 1, -1, 1, -1, 2, -2, 2, -2 };
        for (int i = 0; i < 8; i++)
        {
            int newRow = r + rowOffsets[i];
            int newCol = c + colOffsets[i];
            if (IsOnBoard(newRow, newCol))
            {
                InternalPiece piece = board[newRow, newCol];
                if (piece.type == PieceType.None || piece.color != color)
                    AddMove(r, c, newRow, newCol, moves);
            }
        }
    }
    private void GetBishopMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves)
    {
        CheckSlidingDirection(board, r, c, color, moves, 1, 1);
        CheckSlidingDirection(board, r, c, color, moves, 1, -1);
        CheckSlidingDirection(board, r, c, color, moves, -1, 1);
        CheckSlidingDirection(board, r, c, color, moves, -1, -1);
    }
    private void GetQueenMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves)
    {
        GetRookMoves(board, r, c, color, moves);
        GetBishopMoves(board, r, c, color, moves);
    }
    private void GetKingMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves)
    {
        int[] rowOffsets = { 0, 0, 1, 1, 1, -1, -1, -1 };
        int[] colOffsets = { 1, -1, 0, 1, -1, 0, 1, -1 };
        for (int i = 0; i < 8; i++)
        {
            int newRow = r + rowOffsets[i];
            int newCol = c + colOffsets[i];
            if (IsOnBoard(newRow, newCol))
            {
                InternalPiece piece = board[newRow, newCol];
                if (piece.type == PieceType.None || piece.color != color)
                    AddMove(r, c, newRow, newCol, moves);
            }
        }
    }

    private void CheckSlidingDirection(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves, int rowDir, int colDir)
    {
        for (int i = 1; i < 8; i++)
        {
            int newRow = r + i * rowDir;
            int newCol = c + i * colDir;
            if (!IsOnBoard(newRow, newCol)) break;
            InternalPiece piece = board[newRow, newCol];
            if (piece.type == PieceType.None) AddMove(r, c, newRow, newCol, moves);
            else
            {
                if (piece.color != color) AddMove(r, c, newRow, newCol, moves);
                break;
            }
        }
    }

    private bool IsOnBoard(int row, int col) => row >= 0 && row < 8 && col >= 0 && col < 8;
    private void AddMove(int fromRow, int fromCol, int toRow, int toCol, List<AIMove> moves) => moves.Add(new AIMove { fromRow = fromRow, fromCol = fromCol, toRow = toRow, toCol = toCol });

    private InternalPiece[,] SimulateMove(InternalPiece[,] originalBoard, AIMove move)
    {
        InternalPiece[,] newBoard = (InternalPiece[,])originalBoard.Clone();
        InternalPiece pieceToMove = newBoard[move.fromRow, move.fromCol];
        newBoard[move.fromRow, move.fromCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
        newBoard[move.toRow, move.toCol] = pieceToMove;
        return newBoard;
    }
}