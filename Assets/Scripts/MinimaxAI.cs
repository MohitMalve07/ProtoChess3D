using UnityEngine;
using System.Collections.Generic;

public class MinimaxAI : MonoBehaviour
{
    // --- Data Structures ---
    public struct AIMove
    {
        public int fromRow, fromCol, toRow, toCol;
    }

    public struct SearchRights
    {
        public bool whiteCanCastleKingSide;
        public bool whiteCanCastleQueenSide;
        public bool blackCanCastleKingSide;
        public bool blackCanCastleQueenSide;
        public int enPassantRow;
        public int enPassantCol;

        public static SearchRights None => new SearchRights
        {
            enPassantRow = -1,
            enPassantCol = -1
        };
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
                    // Tables are oriented from White's perspective with row 0 = 8th rank, row 7 = 1st rank.
                    // In scene coordinates, White starts at row 0..1 and Black at row 7..6.
                    int positionValue = 0;
                    int tableRow = (piece.color == PieceColor.White) ? 7 - r : r;

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

    private const int MaxQuiescenceDepth = 4;

    public AIMove GetBestMove(InternalPiece[,] board, int depth)
    {
        return GetBestMove(board, depth, SearchRights.None);
    }

    public AIMove GetBestMove(InternalPiece[,] board, int depth, SearchRights rights)
    {
        List<AIMove> possibleMoves = GenerateLegalMoves(board, PieceColor.Black, rights);
        OrderMoves(board, possibleMoves);

        // --- IMPROVEMENT: Track ALL best moves, not just the first one found ---
        List<AIMove> bestMoves = new List<AIMove>();
        int bestValue = -9999999;

        foreach (AIMove move in possibleMoves)
        {
            InternalPiece[,] nextBoardState = SimulateMove(board, move, rights, out SearchRights nextRights);
            int boardValue = Minimax(nextBoardState, depth - 1, -1000000, 1000000, false, nextRights);

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

    private int Minimax(InternalPiece[,] board, int depth, int alpha, int beta, bool isMaximizing, SearchRights rights)
    {
        if (depth <= 0)
        {
            return Quiesce(board, alpha, beta, isMaximizing, rights, MaxQuiescenceDepth);
        }

        PieceColor sideToMove = isMaximizing ? PieceColor.Black : PieceColor.White;
        List<AIMove> moves = GenerateLegalMoves(board, sideToMove, rights);

        if (moves.Count == 0)
        {
            if (IsKingInCheck(board, sideToMove))
            {
                // Prefer faster checkmate for White-mated (+depth) and slower defeat for Black-mated (-depth)
                return isMaximizing ? (-100000 - depth) : (100000 + depth);
            }
            return 0; // Stalemate
        }

        OrderMoves(board, moves);

        if (isMaximizing) // Black
        {
            int maxEval = -9999999;
            foreach (AIMove move in moves)
            {
                InternalPiece[,] nextBoardState = SimulateMove(board, move, rights, out SearchRights nextRights);
                int eval = Minimax(nextBoardState, depth - 1, alpha, beta, false, nextRights);
                maxEval = Mathf.Max(maxEval, eval);
                alpha = Mathf.Max(alpha, eval);
                if (beta <= alpha) break;
            }
            return maxEval;
        }
        else // White
        {
            int minEval = 9999999;
            foreach (AIMove move in moves)
            {
                InternalPiece[,] nextBoardState = SimulateMove(board, move, rights, out SearchRights nextRights);
                int eval = Minimax(nextBoardState, depth - 1, alpha, beta, true, nextRights);
                minEval = Mathf.Min(minEval, eval);
                beta = Mathf.Min(beta, eval);
                if (beta <= alpha) break;
            }
            return minEval;
        }
    }

    private int Quiesce(InternalPiece[,] board, int alpha, int beta, bool isMaximizing, SearchRights rights, int qDepth)
    {
        PieceColor sideToMove = isMaximizing ? PieceColor.Black : PieceColor.White;
        bool inCheck = IsKingInCheck(board, sideToMove);

        if (!inCheck)
        {
            int standPat = Evaluate(board);
            if (qDepth <= 0) return standPat;

            if (isMaximizing)
            {
                if (standPat >= beta) return beta;
                if (standPat > alpha) alpha = standPat;
            }
            else
            {
                if (standPat <= alpha) return alpha;
                if (standPat < beta) beta = standPat;
            }
        }
        else if (qDepth <= 0)
        {
            return Evaluate(board);
        }

        List<AIMove> legalMoves = GenerateLegalMoves(board, sideToMove, rights);
        if (legalMoves.Count == 0)
        {
            return inCheck ? (isMaximizing ? -100000 : 100000) : 0;
        }

        List<AIMove> tacticalMoves;
        if (inCheck)
        {
            // When in check, consider all legal evasions
            tacticalMoves = legalMoves;
        }
        else
        {
            tacticalMoves = new List<AIMove>(legalMoves.Count);
            foreach (AIMove move in legalMoves)
            {
                if (IsTacticalMove(board, move))
                {
                    tacticalMoves.Add(move);
                }
            }
            if (tacticalMoves.Count == 0) return Evaluate(board);
        }

        OrderMoves(board, tacticalMoves);

        if (isMaximizing)
        {
            int maxEval = inCheck ? -9999999 : alpha;
            foreach (AIMove move in tacticalMoves)
            {
                InternalPiece[,] nextBoardState = SimulateMove(board, move, rights, out SearchRights nextRights);
                int eval = Quiesce(nextBoardState, alpha, beta, false, nextRights, qDepth - 1);
                if (eval > maxEval) maxEval = eval;
                if (eval > alpha) alpha = eval;
                if (beta <= alpha) break;
            }
            return maxEval;
        }
        else
        {
            int minEval = inCheck ? 9999999 : beta;
            foreach (AIMove move in tacticalMoves)
            {
                InternalPiece[,] nextBoardState = SimulateMove(board, move, rights, out SearchRights nextRights);
                int eval = Quiesce(nextBoardState, alpha, beta, true, nextRights, qDepth - 1);
                if (eval < minEval) minEval = eval;
                if (eval < beta) beta = eval;
                if (beta <= alpha) break;
            }
            return minEval;
        }
    }

    private bool IsTacticalMove(InternalPiece[,] board, AIMove move)
    {
        if (board[move.toRow, move.toCol].type != PieceType.None) return true;

        InternalPiece mover = board[move.fromRow, move.fromCol];
        if (mover.type == PieceType.Pawn)
        {
            if (move.fromCol != move.toCol) return true; // En passant capture
            if ((mover.color == PieceColor.White && move.toRow == 7) ||
                (mover.color == PieceColor.Black && move.toRow == 0))
            {
                return true; // Promotion
            }
        }
        return false;
    }

    private void OrderMoves(InternalPiece[,] board, List<AIMove> moves)
    {
        int count = moves.Count;
        if (count <= 1) return;

        int[] scores = new int[count];
        for (int i = 0; i < count; i++)
        {
            scores[i] = ScoreMoveForOrdering(board, moves[i]);
        }

        // Insertion sort descending on small move lists (zero allocations)
        for (int i = 1; i < count; i++)
        {
            int keyScore = scores[i];
            AIMove keyMove = moves[i];
            int j = i - 1;
            while (j >= 0 && scores[j] < keyScore)
            {
                scores[j + 1] = scores[j];
                moves[j + 1] = moves[j];
                j--;
            }
            scores[j + 1] = keyScore;
            moves[j + 1] = keyMove;
        }
    }

    private int ScoreMoveForOrdering(InternalPiece[,] board, AIMove move)
    {
        InternalPiece attacker = board[move.fromRow, move.fromCol];
        InternalPiece victim = board[move.toRow, move.toCol];

        int score = 0;
        if (victim.type != PieceType.None)
        {
            score += 10000 + victim.GetMaterialValue() * 10 - attacker.GetMaterialValue();
        }
        else if (attacker.type == PieceType.Pawn && move.fromCol != move.toCol)
        {
            // En passant capture
            score += 10000 + 1000 - 100;
        }

        if (attacker.type == PieceType.Pawn &&
            ((attacker.color == PieceColor.White && move.toRow == 7) ||
             (attacker.color == PieceColor.Black && move.toRow == 0)))
        {
            score += 9000;
        }
        else if (attacker.type == PieceType.King && Mathf.Abs(move.toCol - move.fromCol) == 2)
        {
            score += 500;
        }

        return score;
    }

    /// <summary>
    /// Generates all strictly legal moves for <paramref name="color"/>, filtering out moves that leave its King in check.
    /// </summary>
    public List<AIMove> GenerateLegalMoves(InternalPiece[,] board, PieceColor color)
    {
        return GenerateLegalMoves(board, color, SearchRights.None);
    }

    public List<AIMove> GenerateLegalMoves(InternalPiece[,] board, PieceColor color, SearchRights rights)
    {
        List<AIMove> pseudoMoves = GenerateMoves(board, color, rights);
        List<AIMove> legalMoves = new List<AIMove>(pseudoMoves.Count);

        foreach (AIMove move in pseudoMoves)
        {
            InternalPiece movedPiece = board[move.fromRow, move.fromCol];
            InternalPiece capturedPiece = board[move.toRow, move.toCol];

            bool isEnPassant = movedPiece.type == PieceType.Pawn && move.fromCol != move.toCol && capturedPiece.type == PieceType.None;
            InternalPiece enPassantPawn = default;
            if (isEnPassant)
            {
                enPassantPawn = board[move.fromRow, move.toCol];
                board[move.fromRow, move.toCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
            }

            board[move.fromRow, move.fromCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
            board[move.toRow, move.toCol] = movedPiece;

            if (!IsKingInCheck(board, color))
            {
                legalMoves.Add(move);
            }

            board[move.fromRow, move.fromCol] = movedPiece;
            board[move.toRow, move.toCol] = capturedPiece;
            if (isEnPassant)
            {
                board[move.fromRow, move.toCol] = enPassantPawn;
            }
        }

        return legalMoves;
    }

    public bool IsKingInCheck(InternalPiece[,] board, PieceColor kingColor)
    {
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                if (board[r, c].type == PieceType.King && board[r, c].color == kingColor)
                {
                    PieceColor attackerColor = (kingColor == PieceColor.White) ? PieceColor.Black : PieceColor.White;
                    return IsSquareAttacked(board, r, c, attackerColor);
                }
            }
        }
        return false;
    }

    private bool IsSquareAttacked(InternalPiece[,] board, int targetRow, int targetCol, PieceColor attackerColor)
    {
        // 1. Pawn attacks (relative to attacker forward direction)
        int attackerDir = (attackerColor == PieceColor.White) ? 1 : -1;
        int pawnRow = targetRow - attackerDir;
        if (IsOnBoard(pawnRow, targetCol - 1) && board[pawnRow, targetCol - 1].type == PieceType.Pawn && board[pawnRow, targetCol - 1].color == attackerColor)
            return true;
        if (IsOnBoard(pawnRow, targetCol + 1) && board[pawnRow, targetCol + 1].type == PieceType.Pawn && board[pawnRow, targetCol + 1].color == attackerColor)
            return true;

        // 2. Knight attacks
        int[] knightRowOffsets = { 2, 2, -2, -2, 1, 1, -1, -1 };
        int[] knightColOffsets = { 1, -1, 1, -1, 2, -2, 2, -2 };
        for (int i = 0; i < 8; i++)
        {
            int nr = targetRow + knightRowOffsets[i];
            int nc = targetCol + knightColOffsets[i];
            if (IsOnBoard(nr, nc) && board[nr, nc].type == PieceType.Knight && board[nr, nc].color == attackerColor)
                return true;
        }

        // 3. King attacks
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int kr = targetRow + dr;
                int kc = targetCol + dc;
                if (IsOnBoard(kr, kc) && board[kr, kc].type == PieceType.King && board[kr, kc].color == attackerColor)
                    return true;
            }
        }

        // 4. Orthogonal sliding attacks (Rook / Queen)
        int[] orthoDr = { 1, -1, 0, 0 };
        int[] orthoDc = { 0, 0, 1, -1 };
        for (int d = 0; d < 4; d++)
        {
            if (IsAttackedAlongRay(board, targetRow, targetCol, orthoDr[d], orthoDc[d], attackerColor, PieceType.Rook))
                return true;
        }

        // 5. Diagonal sliding attacks (Bishop / Queen)
        int[] diagDr = { 1, 1, -1, -1 };
        int[] diagDc = { 1, -1, 1, -1 };
        for (int d = 0; d < 4; d++)
        {
            if (IsAttackedAlongRay(board, targetRow, targetCol, diagDr[d], diagDc[d], attackerColor, PieceType.Bishop))
                return true;
        }

        return false;
    }

    private bool IsAttackedAlongRay(InternalPiece[,] board, int startRow, int startCol, int rowDir, int colDir, PieceColor attackerColor, PieceType sliderType)
    {
        for (int i = 1; i < 8; i++)
        {
            int r = startRow + i * rowDir;
            int c = startCol + i * colDir;
            if (!IsOnBoard(r, c)) return false;

            InternalPiece piece = board[r, c];
            if (piece.type == PieceType.None) continue;

            return piece.color == attackerColor && (piece.type == sliderType || piece.type == PieceType.Queen);
        }
        return false;
    }

    // --- Move Generation (Must match your existing Phase 3 logic) ---
    // Make sure to include all your GetPawnMoves, GetRookMoves, etc. functions here.
    // I am including the structure, please ensure the body of these methods matches what you had before.

    public List<AIMove> GenerateMoves(InternalPiece[,] board, PieceColor color)
    {
        return GenerateMoves(board, color, SearchRights.None);
    }

    public List<AIMove> GenerateMoves(InternalPiece[,] board, PieceColor color, SearchRights rights)
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
                        case PieceType.Pawn: GetPawnMoves(board, r, c, color, moves, rights); break;
                        case PieceType.Rook: GetRookMoves(board, r, c, color, moves); break;
                        case PieceType.Knight: GetKnightMoves(board, r, c, color, moves); break;
                        case PieceType.Bishop: GetBishopMoves(board, r, c, color, moves); break;
                        case PieceType.Queen: GetQueenMoves(board, r, c, color, moves); break;
                        case PieceType.King: GetKingMoves(board, r, c, color, moves, rights); break;
                    }
                }
            }
        }
        return moves;
    }

    private void GetPawnMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves, SearchRights rights)
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
                {
                    AddMove(r, c, oneStepRow, colOffset, moves);
                }
                else if (piece.type == PieceType.None &&
                         oneStepRow == rights.enPassantRow &&
                         colOffset == rights.enPassantCol)
                {
                    AddMove(r, c, oneStepRow, colOffset, moves);
                }
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
    private void GetKingMoves(InternalPiece[,] board, int r, int c, PieceColor color, List<AIMove> moves, SearchRights rights)
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

        int homeRow = (color == PieceColor.White) ? 0 : 7;
        if (r == homeRow && c == 4)
        {
            PieceColor attackerColor = (color == PieceColor.White) ? PieceColor.Black : PieceColor.White;
            bool canKingSide = (color == PieceColor.White) ? rights.whiteCanCastleKingSide : rights.blackCanCastleKingSide;
            bool canQueenSide = (color == PieceColor.White) ? rights.whiteCanCastleQueenSide : rights.blackCanCastleQueenSide;

            if ((canKingSide || canQueenSide) && !IsSquareAttacked(board, homeRow, 4, attackerColor))
            {
                if (canKingSide &&
                    board[homeRow, 7].type == PieceType.Rook && board[homeRow, 7].color == color &&
                    board[homeRow, 5].type == PieceType.None && board[homeRow, 6].type == PieceType.None &&
                    !IsSquareAttacked(board, homeRow, 5, attackerColor))
                {
                    AddMove(homeRow, 4, homeRow, 6, moves);
                }

                if (canQueenSide &&
                    board[homeRow, 0].type == PieceType.Rook && board[homeRow, 0].color == color &&
                    board[homeRow, 1].type == PieceType.None && board[homeRow, 2].type == PieceType.None && board[homeRow, 3].type == PieceType.None &&
                    !IsSquareAttacked(board, homeRow, 3, attackerColor))
                {
                    AddMove(homeRow, 4, homeRow, 2, moves);
                }
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

    private InternalPiece[,] SimulateMove(InternalPiece[,] originalBoard, AIMove move, SearchRights rights, out SearchRights nextRights)
    {
        InternalPiece[,] newBoard = (InternalPiece[,])originalBoard.Clone();
        InternalPiece pieceToMove = newBoard[move.fromRow, move.fromCol];
        InternalPiece targetBeforeMove = newBoard[move.toRow, move.toCol];

        // En passant capture removes the passed pawn on fromRow, toCol
        if (pieceToMove.type == PieceType.Pawn && move.fromCol != move.toCol && targetBeforeMove.type == PieceType.None)
        {
            newBoard[move.fromRow, move.toCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
        }

        // Castling moves the corresponding rook
        if (pieceToMove.type == PieceType.King && Mathf.Abs(move.toCol - move.fromCol) == 2)
        {
            if (move.toCol == 6)
            {
                newBoard[move.fromRow, 5] = newBoard[move.fromRow, 7];
                newBoard[move.fromRow, 7] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
            }
            else if (move.toCol == 2)
            {
                newBoard[move.fromRow, 3] = newBoard[move.fromRow, 0];
                newBoard[move.fromRow, 0] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
            }
        }

        // Auto-promote pawns to Queen on the final rank
        if (pieceToMove.type == PieceType.Pawn &&
            ((pieceToMove.color == PieceColor.White && move.toRow == 7) ||
             (pieceToMove.color == PieceColor.Black && move.toRow == 0)))
        {
            pieceToMove.type = PieceType.Queen;
        }

        newBoard[move.fromRow, move.fromCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
        newBoard[move.toRow, move.toCol] = pieceToMove;

        nextRights = rights;
        if (pieceToMove.type == PieceType.Pawn && Mathf.Abs(move.toRow - move.fromRow) == 2)
        {
            nextRights.enPassantRow = (move.fromRow + move.toRow) / 2;
            nextRights.enPassantCol = move.toCol;
        }
        else
        {
            nextRights.enPassantRow = -1;
            nextRights.enPassantCol = -1;
        }

        if (pieceToMove.type == PieceType.King)
        {
            if (pieceToMove.color == PieceColor.White)
            {
                nextRights.whiteCanCastleKingSide = false;
                nextRights.whiteCanCastleQueenSide = false;
            }
            else
            {
                nextRights.blackCanCastleKingSide = false;
                nextRights.blackCanCastleQueenSide = false;
            }
        }

        if ((move.fromRow == 0 && move.fromCol == 0) || (move.toRow == 0 && move.toCol == 0)) nextRights.whiteCanCastleQueenSide = false;
        if ((move.fromRow == 0 && move.fromCol == 7) || (move.toRow == 0 && move.toCol == 7)) nextRights.whiteCanCastleKingSide = false;
        if ((move.fromRow == 7 && move.fromCol == 0) || (move.toRow == 7 && move.toCol == 0)) nextRights.blackCanCastleQueenSide = false;
        if ((move.fromRow == 7 && move.fromCol == 7) || (move.toRow == 7 && move.toCol == 7)) nextRights.blackCanCastleKingSide = false;

        return newBoard;
    }
}