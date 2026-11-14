using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

public class MinimaxAI : MonoBehaviour
{
    // --- Data Structures ---
    public struct AIMove
    {
        public int fromRow, fromCol, toRow, toCol;

        public bool Equals(AIMove other) =>
            fromRow == other.fromRow && fromCol == other.fromCol &&
            toRow == other.toRow && toCol == other.toCol;
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
                case PieceType.Pawn: return 100;
                case PieceType.Knight: return 320;
                case PieceType.Bishop: return 330;
                case PieceType.Rook: return 500;
                case PieceType.Queen: return 900;
                case PieceType.King: return 20000;
                default: return 0;
            }
        }
    }

    private struct UndoInfo
    {
        public InternalPiece movedPiece;
        public InternalPiece capturedPiece;
        public InternalPiece enPassantPiece;
        public bool isEnPassant;
        public bool isCastle;
        public SearchRights prevRights;
        public ulong prevHash;
    }

    private enum TTFlag : byte
    {
        None = 0,
        Exact = 1,
        LowerBound = 2,
        UpperBound = 3
    }

    private struct TTEntry
    {
        public ulong key;
        public int score;
        public AIMove bestMove;
        public sbyte depth;
        public TTFlag flag;
        public byte generation;
    }

    private sealed class SearchContext
    {
        public readonly InternalPiece[,] board = new InternalPiece[8, 8];
        public readonly AIMove[][] moveBuffers;
        public readonly int[][] scoreBuffers;
        public readonly AIMove[,] killerMoves = new AIMove[MaxSearchPly, 2];
        public readonly int[,,] history = new int[2, 64, 64];
        public readonly Stopwatch stopwatch = new Stopwatch();
        public readonly System.Random rng = new System.Random();

        public int timeLimitMs;
        public CancellationToken cancellationToken;
        public int nodes;
        public byte ttGeneration;

        public SearchContext()
        {
            moveBuffers = new AIMove[MaxSearchPly][];
            scoreBuffers = new int[MaxSearchPly][];
            for (int i = 0; i < MaxSearchPly; i++)
            {
                moveBuffers[i] = new AIMove[256];
                scoreBuffers[i] = new int[256];
            }
        }

        public void ClearHeuristics()
        {
            Array.Clear(killerMoves, 0, killerMoves.Length);
            Array.Clear(history, 0, history.Length);
        }
    }

    // --- Constants ---
    private const int Infinity = 9999999;
    private const int MateScore = 100000;
    private const int MateThreshold = 90000;
    private const int MaxQuiescenceDepth = 6;
    private const int MaxSearchPly = 48;
    private const int TTSize = 1 << 17; // 131,072 entries
    private const int TTMask = TTSize - 1;

    // --- Static Attack Offsets (Zero-Allocation) ---
    private static readonly int[] KnightRowOffsets = { 2, 2, -2, -2, 1, 1, -1, -1 };
    private static readonly int[] KnightColOffsets = { 1, -1, 1, -1, 2, -2, 2, -2 };
    private static readonly int[] KingRowOffsets = { 0, 0, 1, 1, 1, -1, -1, -1 };
    private static readonly int[] KingColOffsets = { 1, -1, 0, 1, -1, 0, 1, -1 };
    private static readonly int[] OrthoDr = { 1, -1, 0, 0 };
    private static readonly int[] OrthoDc = { 0, 0, 1, -1 };
    private static readonly int[] DiagDr = { 1, 1, -1, -1 };
    private static readonly int[] DiagDc = { 1, -1, 1, -1 };

    // --- Zobrist Keys ---
    private static readonly ulong[,,] ZobristPiece = new ulong[2, 7, 64];
    private static readonly ulong ZobristBlackToMove;
    private static readonly ulong[] ZobristCastle = new ulong[16];
    private static readonly ulong[] ZobristEnPassantFile = new ulong[8];

    // --- Piece-Square Tables (White perspective: row 0 = 8th rank, row 7 = 1st rank) ---
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

    private static readonly int[,] kingMiddlegameTable = new int[,]
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

    private static readonly int[,] kingEndgameTable = new int[,]
    {
        {-50,-40,-30,-20,-20,-30,-40,-50 },
        {-30,-20,-10,  0,  0,-10,-20,-30 },
        {-30,-10, 20, 30, 30, 20,-10,-30 },
        {-30,-10, 30, 40, 40, 30,-10,-30 },
        {-30,-10, 30, 40, 40, 30,-10,-30 },
        {-30,-10, 20, 30, 30, 20,-10,-30 },
        {-30,-30,  0,  0,  0,  0,-30,-30 },
        {-50,-30,-30,-30,-30,-30,-30,-50 }
    };

    private static readonly int[] PassedPawnBonusByRank = { 0, 10, 15, 25, 40, 65, 95, 0 };

    private readonly TTEntry[] transpositionTable = new TTEntry[TTSize];
    private readonly SearchContext reusableContext = new SearchContext();
    private readonly object searchLock = new object();
    private byte searchGeneration;

    static MinimaxAI()
    {
        System.Random zRng = new System.Random(133742);
        ulong NextU64()
        {
            byte[] buf = new byte[8];
            zRng.NextBytes(buf);
            return BitConverter.ToUInt64(buf, 0);
        }

        for (int color = 0; color < 2; color++)
        {
            for (int type = 1; type <= 6; type++)
            {
                for (int sq = 0; sq < 64; sq++)
                {
                    ZobristPiece[color, type, sq] = NextU64();
                }
            }
        }

        ZobristBlackToMove = NextU64();
        for (int i = 0; i < 16; i++) ZobristCastle[i] = NextU64();
        for (int i = 0; i < 8; i++) ZobristEnPassantFile[i] = NextU64();
    }

    // --- Public Search Entry Points ---

    public AIMove GetBestMove(InternalPiece[,] board, int depth)
    {
        return GetBestMove(board, depth, SearchRights.None, 0, CancellationToken.None);
    }

    public AIMove GetBestMove(InternalPiece[,] board, int depth, SearchRights rights)
    {
        return GetBestMove(board, depth, rights, 0, CancellationToken.None);
    }

    /// <summary>
    /// Runs iterative deepening alpha-beta search with transposition table and quiescence search.
    /// Safe to invoke from a background thread when <paramref name="board"/> is an isolated snapshot.
    /// </summary>
    public AIMove GetBestMove(InternalPiece[,] board, int maxDepth, SearchRights rights, int timeLimitMs, CancellationToken cancellationToken = default)
    {
        if (board == null || maxDepth <= 0) return new AIMove();

        lock (searchLock)
        {
            SearchContext ctx = reusableContext;
            Array.Copy(board, ctx.board, 64);
            ctx.ClearHeuristics();
            ctx.timeLimitMs = timeLimitMs;
            ctx.cancellationToken = cancellationToken;
            ctx.nodes = 0;
            searchGeneration++;
            if (searchGeneration == 0) searchGeneration = 1;
            ctx.ttGeneration = searchGeneration;
            ctx.stopwatch.Restart();

            int rootCount = GenerateLegalMovesInto(ctx.board, PieceColor.Black, rights, ctx.moveBuffers[0]);
            if (rootCount == 0)
            {
                ctx.stopwatch.Stop();
                return new AIMove();
            }

            AIMove[] rootMoves = new AIMove[rootCount];
            Array.Copy(ctx.moveBuffers[0], rootMoves, rootCount);
            OrderMoves(ctx.board, rootMoves, rootCount, default, ctx, 0, PieceColor.Black);

            ulong rootHash = ComputeHash(ctx.board, PieceColor.Black, rights);
            AIMove overallBest = rootMoves[0];
            bool hasCompletedDepth = false;

            for (int depth = 1; depth <= maxDepth; depth++)
            {
                if (depth > 1 && (cancellationToken.IsCancellationRequested || (timeLimitMs > 0 && ctx.stopwatch.ElapsedMilliseconds >= timeLimitMs)))
                {
                    break;
                }

                // Move previous iteration's best move to the front of rootMoves
                if (hasCompletedDepth)
                {
                    for (int i = 1; i < rootCount; i++)
                    {
                        if (rootMoves[i].Equals(overallBest))
                        {
                            rootMoves[i] = rootMoves[0];
                            rootMoves[0] = overallBest;
                            break;
                        }
                    }
                }

                try
                {
                    AIMove iterBest = SearchRoot(ctx, rootMoves, rootCount, depth, rights, rootHash, out _);
                    overallBest = iterBest;
                    hasCompletedDepth = true;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            ctx.stopwatch.Stop();
            return hasCompletedDepth ? overallBest : new AIMove();
        }
    }

    private AIMove SearchRoot(SearchContext ctx, AIMove[] rootMoves, int rootCount, int depth, SearchRights rights, ulong rootHash, out int bestScore)
    {
        bestScore = -Infinity;
        int alpha = -Infinity;
        int beta = Infinity;
        AIMove bestMove = rootMoves[0];

        for (int i = 0; i < rootCount; i++)
        {
            AIMove move = rootMoves[i];
            MakeMove(ctx.board, move, PieceColor.Black, ref rights, ref rootHash, out UndoInfo undo);
            int eval = NegamaxMinimax(ctx, depth - 1, 1, alpha, beta, false, rights, rootHash);
            UnmakeMove(ctx.board, move, ref rights, ref rootHash, in undo);

            if (eval > bestScore)
            {
                bestScore = eval;
                bestMove = move;
            }

            if (eval > alpha) alpha = eval;
        }

        return bestMove;
    }

    private int NegamaxMinimax(SearchContext ctx, int depth, int ply, int alpha, int beta, bool isMaximizing, SearchRights rights, ulong hash)
    {
        CheckTimeAndCancel(ctx);

        if (ply >= MaxSearchPly - 1)
        {
            return Evaluate(ctx.board);
        }

        if (depth <= 0)
        {
            return Quiesce(ctx, ply, alpha, beta, isMaximizing, rights, hash, MaxQuiescenceDepth);
        }

        AIMove ttMove = default;
        ref TTEntry entry = ref transpositionTable[hash & TTMask];
        if (entry.key == hash && entry.flag != TTFlag.None)
        {
            ttMove = entry.bestMove;
            if (entry.depth >= depth)
            {
                int ttScore = ScoreFromTT(entry.score, ply);
                if (entry.flag == TTFlag.Exact) return ttScore;
                if (entry.flag == TTFlag.LowerBound && ttScore > alpha) alpha = ttScore;
                else if (entry.flag == TTFlag.UpperBound && ttScore < beta) beta = ttScore;
                if (alpha >= beta) return ttScore;
            }
        }

        int searchAlpha = alpha;
        int searchBeta = beta;

        PieceColor sideToMove = isMaximizing ? PieceColor.Black : PieceColor.White;
        AIMove[] moves = ctx.moveBuffers[ply];
        int moveCount = GenerateLegalMovesInto(ctx.board, sideToMove, rights, moves);

        if (moveCount == 0)
        {
            if (IsKingInCheck(ctx.board, sideToMove))
            {
                return isMaximizing ? (-MateScore + ply) : (MateScore - ply);
            }
            return 0;
        }

        OrderMoves(ctx.board, moves, moveCount, ttMove, ctx, ply, sideToMove);

        AIMove bestMove = moves[0];
        int bestEval = isMaximizing ? -Infinity : Infinity;

        for (int i = 0; i < moveCount; i++)
        {
            AIMove move = moves[i];
            MakeMove(ctx.board, move, sideToMove, ref rights, ref hash, out UndoInfo undo);
            int eval = NegamaxMinimax(ctx, depth - 1, ply + 1, alpha, beta, !isMaximizing, rights, hash);
            UnmakeMove(ctx.board, move, ref rights, ref hash, in undo);

            if (isMaximizing)
            {
                if (eval > bestEval)
                {
                    bestEval = eval;
                    bestMove = move;
                }
                if (eval > alpha) alpha = eval;
            }
            else
            {
                if (eval < bestEval)
                {
                    bestEval = eval;
                    bestMove = move;
                }
                if (eval < beta) beta = eval;
            }

            if (beta <= alpha)
            {
                if (ctx.board[move.toRow, move.toCol].type == PieceType.None)
                {
                    RecordCutoffHeuristics(ctx, ply, depth, sideToMove, move);
                }
                break;
            }
        }

        TTFlag storeFlag;
        if (bestEval <= searchAlpha) storeFlag = TTFlag.UpperBound;
        else if (bestEval >= searchBeta) storeFlag = TTFlag.LowerBound;
        else storeFlag = TTFlag.Exact;

        if (entry.key != hash || entry.generation != ctx.ttGeneration || depth >= entry.depth)
        {
            entry.key = hash;
            entry.score = ScoreToTT(bestEval, ply);
            entry.bestMove = bestMove;
            entry.depth = (sbyte)depth;
            entry.flag = storeFlag;
            entry.generation = ctx.ttGeneration;
        }

        return bestEval;
    }

    private int Quiesce(SearchContext ctx, int ply, int alpha, int beta, bool isMaximizing, SearchRights rights, ulong hash, int qDepth)
    {
        CheckTimeAndCancel(ctx);

        PieceColor sideToMove = isMaximizing ? PieceColor.Black : PieceColor.White;
        bool inCheck = IsKingInCheck(ctx.board, sideToMove);

        int standPat = 0;
        if (!inCheck)
        {
            standPat = Evaluate(ctx.board);
            if (qDepth <= 0 || ply >= MaxSearchPly - 1) return standPat;

            if (isMaximizing)
            {
                if (standPat >= beta) return standPat;
                if (standPat > alpha) alpha = standPat;
            }
            else
            {
                if (standPat <= alpha) return standPat;
                if (standPat < beta) beta = standPat;
            }
        }
        else if (qDepth <= 0 || ply >= MaxSearchPly - 1)
        {
            return Evaluate(ctx.board);
        }

        AIMove[] buffer = ctx.moveBuffers[ply];
        int legalCount = GenerateLegalMovesInto(ctx.board, sideToMove, rights, buffer);
        if (legalCount == 0)
        {
            return inCheck ? (isMaximizing ? (-MateScore + ply) : (MateScore - ply)) : 0;
        }

        int searchCount;
        if (inCheck)
        {
            searchCount = legalCount;
        }
        else
        {
            searchCount = 0;
            for (int i = 0; i < legalCount; i++)
            {
                if (IsTacticalMove(ctx.board, buffer[i]))
                {
                    buffer[searchCount++] = buffer[i];
                }
            }
            if (searchCount == 0) return standPat;
        }

        OrderMoves(ctx.board, buffer, searchCount, default, ctx, ply, sideToMove);

        int bestEval = inCheck ? (isMaximizing ? -Infinity : Infinity) : standPat;

        for (int i = 0; i < searchCount; i++)
        {
            AIMove move = buffer[i];
            MakeMove(ctx.board, move, sideToMove, ref rights, ref hash, out UndoInfo undo);
            int eval = Quiesce(ctx, ply + 1, alpha, beta, !isMaximizing, rights, hash, qDepth - 1);
            UnmakeMove(ctx.board, move, ref rights, ref hash, in undo);

            if (isMaximizing)
            {
                if (eval > bestEval) bestEval = eval;
                if (eval > alpha) alpha = eval;
            }
            else
            {
                if (eval < bestEval) bestEval = eval;
                if (eval < beta) beta = eval;
            }

            if (beta <= alpha) break;
        }

        return bestEval;
    }

    private static void CheckTimeAndCancel(SearchContext ctx)
    {
        ctx.nodes++;
        if ((ctx.nodes & 2047) != 0) return;

        ctx.cancellationToken.ThrowIfCancellationRequested();
        if (ctx.timeLimitMs > 0 && ctx.stopwatch.ElapsedMilliseconds >= ctx.timeLimitMs)
        {
            throw new OperationCanceledException();
        }
    }

    private static int ScoreToTT(int score, int ply)
    {
        if (score > MateThreshold) return score + ply;
        if (score < -MateThreshold) return score - ply;
        return score;
    }

    private static int ScoreFromTT(int score, int ply)
    {
        if (score > MateThreshold) return score - ply;
        if (score < -MateThreshold) return score + ply;
        return score;
    }

    private static void RecordCutoffHeuristics(SearchContext ctx, int ply, int depth, PieceColor sideToMove, AIMove move)
    {
        if (!move.Equals(ctx.killerMoves[ply, 0]))
        {
            ctx.killerMoves[ply, 1] = ctx.killerMoves[ply, 0];
            ctx.killerMoves[ply, 0] = move;
        }

        int side = sideToMove == PieceColor.Black ? 1 : 0;
        int fromSq = move.fromRow * 8 + move.fromCol;
        int toSq = move.toRow * 8 + move.toCol;
        ctx.history[side, fromSq, toSq] += depth * depth;
    }

    // --- Evaluation Function (Material + Tapered PSTs + Pawn Structure + Piece Activity) ---

    private int Evaluate(InternalPiece[,] board)
    {
        int score = 0;
        int whiteBishops = 0;
        int blackBishops = 0;
        int phase = 0;

        int whiteKingRow = -1, whiteKingCol = -1;
        int blackKingRow = -1, blackKingCol = -1;

        int whitePawns0 = 0, whitePawns1 = 0, whitePawns2 = 0, whitePawns3 = 0, whitePawns4 = 0, whitePawns5 = 0, whitePawns6 = 0, whitePawns7 = 0;
        int blackPawns0 = 0, blackPawns1 = 0, blackPawns2 = 0, blackPawns3 = 0, blackPawns4 = 0, blackPawns5 = 0, blackPawns6 = 0, blackPawns7 = 0;

        int wMin0 = 8, wMin1 = 8, wMin2 = 8, wMin3 = 8, wMin4 = 8, wMin5 = 8, wMin6 = 8, wMin7 = 8;
        int bMax0 = -1, bMax1 = -1, bMax2 = -1, bMax3 = -1, bMax4 = -1, bMax5 = -1, bMax6 = -1, bMax7 = -1;

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                InternalPiece piece = board[r, c];
                if (piece.type == PieceType.None) continue;

                int tableRow = (piece.color == PieceColor.White) ? 7 - r : r;
                int material = piece.GetMaterialValue();
                int pos = 0;

                switch (piece.type)
                {
                    case PieceType.Pawn:
                        pos = pawnTable[tableRow, c];
                        if (piece.color == PieceColor.White)
                        {
                            switch (c)
                            {
                                case 0: whitePawns0++; if (r < wMin0) wMin0 = r; break;
                                case 1: whitePawns1++; if (r < wMin1) wMin1 = r; break;
                                case 2: whitePawns2++; if (r < wMin2) wMin2 = r; break;
                                case 3: whitePawns3++; if (r < wMin3) wMin3 = r; break;
                                case 4: whitePawns4++; if (r < wMin4) wMin4 = r; break;
                                case 5: whitePawns5++; if (r < wMin5) wMin5 = r; break;
                                case 6: whitePawns6++; if (r < wMin6) wMin6 = r; break;
                                case 7: whitePawns7++; if (r < wMin7) wMin7 = r; break;
                            }
                        }
                        else
                        {
                            switch (c)
                            {
                                case 0: blackPawns0++; if (r > bMax0) bMax0 = r; break;
                                case 1: blackPawns1++; if (r > bMax1) bMax1 = r; break;
                                case 2: blackPawns2++; if (r > bMax2) bMax2 = r; break;
                                case 3: blackPawns3++; if (r > bMax3) bMax3 = r; break;
                                case 4: blackPawns4++; if (r > bMax4) bMax4 = r; break;
                                case 5: blackPawns5++; if (r > bMax5) bMax5 = r; break;
                                case 6: blackPawns6++; if (r > bMax6) bMax6 = r; break;
                                case 7: blackPawns7++; if (r > bMax7) bMax7 = r; break;
                            }
                        }
                        break;

                    case PieceType.Knight:
                        pos = knightTable[tableRow, c];
                        phase += 1;
                        break;

                    case PieceType.Bishop:
                        pos = bishopTable[tableRow, c];
                        phase += 1;
                        if (piece.color == PieceColor.White) whiteBishops++;
                        else blackBishops++;
                        break;

                    case PieceType.Rook:
                        pos = rookTable[tableRow, c];
                        phase += 2;
                        if (piece.color == PieceColor.White && r == 6) pos += 20;
                        else if (piece.color == PieceColor.Black && r == 1) pos += 20;
                        break;

                    case PieceType.Queen:
                        pos = queenTable[tableRow, c];
                        phase += 4;
                        break;

                    case PieceType.King:
                        if (piece.color == PieceColor.White) { whiteKingRow = r; whiteKingCol = c; }
                        else { blackKingRow = r; blackKingCol = c; }
                        break;
                }

                if (piece.type != PieceType.King)
                {
                    int total = material + pos;
                    score += (piece.color == PieceColor.Black) ? total : -total;
                }
            }
        }

        // Tapered King PST + material
        int clampedPhase = Math.Min(phase, 24);
        int egWeight = 24 - clampedPhase;

        if (whiteKingRow >= 0)
        {
            int tRow = 7 - whiteKingRow;
            int kingPos = (kingMiddlegameTable[tRow, whiteKingCol] * clampedPhase + kingEndgameTable[tRow, whiteKingCol] * egWeight) / 24;
            score -= 20000 + kingPos;
        }
        if (blackKingRow >= 0)
        {
            int kingPos = (kingMiddlegameTable[blackKingRow, blackKingCol] * clampedPhase + kingEndgameTable[blackKingRow, blackKingCol] * egWeight) / 24;
            score += 20000 + kingPos;
        }

        // Bishop pair bonus
        if (blackBishops >= 2) score += 35;
        if (whiteBishops >= 2) score -= 35;

        // Pawn structure & Rook file activity
        int GetWCount(int c) => c == 0 ? whitePawns0 : c == 1 ? whitePawns1 : c == 2 ? whitePawns2 : c == 3 ? whitePawns3 : c == 4 ? whitePawns4 : c == 5 ? whitePawns5 : c == 6 ? whitePawns6 : whitePawns7;
        int GetBCount(int c) => c == 0 ? blackPawns0 : c == 1 ? blackPawns1 : c == 2 ? blackPawns2 : c == 3 ? blackPawns3 : c == 4 ? blackPawns4 : c == 5 ? blackPawns5 : c == 6 ? blackPawns6 : blackPawns7;
        int GetWMin(int c) => c == 0 ? wMin0 : c == 1 ? wMin1 : c == 2 ? wMin2 : c == 3 ? wMin3 : c == 4 ? wMin4 : c == 5 ? wMin5 : c == 6 ? wMin6 : wMin7;
        int GetBMax(int c) => c == 0 ? bMax0 : c == 1 ? bMax1 : c == 2 ? bMax2 : c == 3 ? bMax3 : c == 4 ? bMax4 : c == 5 ? bMax5 : c == 6 ? bMax6 : bMax7;

        for (int c = 0; c < 8; c++)
        {
            int wc = GetWCount(c);
            int bc = GetBCount(c);

            if (wc > 1) score += (wc - 1) * 15; // White doubled penalty -> good for Black
            if (bc > 1) score -= (bc - 1) * 15; // Black doubled penalty

            if (wc > 0)
            {
                bool wIsolated = (c == 0 || GetWCount(c - 1) == 0) && (c == 7 || GetWCount(c + 1) == 0);
                if (wIsolated) score += 15;
            }
            if (bc > 0)
            {
                bool bIsolated = (c == 0 || GetBCount(c - 1) == 0) && (c == 7 || GetBCount(c + 1) == 0);
                if (bIsolated) score -= 15;
            }
        }

        // Passed pawns + Rook open/semi-open files
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                InternalPiece piece = board[r, c];
                if (piece.type == PieceType.Pawn)
                {
                    if (piece.color == PieceColor.White)
                    {
                        bool passed = GetBMax(c) <= r &&
                                      (c == 0 || GetBMax(c - 1) <= r) &&
                                      (c == 7 || GetBMax(c + 1) <= r);
                        if (passed)
                        {
                            int bonus = PassedPawnBonusByRank[r] + (egWeight * r * 3) / 24;
                            score -= bonus;
                        }
                    }
                    else
                    {
                        bool passed = GetWMin(c) >= r &&
                                      (c == 0 || GetWMin(c - 1) >= r) &&
                                      (c == 7 || GetWMin(c + 1) >= r);
                        if (passed)
                        {
                            int adv = 7 - r;
                            int bonus = PassedPawnBonusByRank[adv] + (egWeight * adv * 3) / 24;
                            score += bonus;
                        }
                    }
                }
                else if (piece.type == PieceType.Rook)
                {
                    if (piece.color == PieceColor.White)
                    {
                        if (GetWCount(c) == 0)
                        {
                            score -= (GetBCount(c) == 0) ? 20 : 10;
                        }
                    }
                    else
                    {
                        if (GetBCount(c) == 0)
                        {
                            score += (GetWCount(c) == 0) ? 20 : 10;
                        }
                    }
                }
            }
        }

        return score;
    }

    // --- Move Ordering & Tactical Filter ---

    private static bool IsTacticalMove(InternalPiece[,] board, AIMove move)
    {
        if (board[move.toRow, move.toCol].type != PieceType.None) return true;

        InternalPiece mover = board[move.fromRow, move.fromCol];
        if (mover.type == PieceType.Pawn)
        {
            if (move.fromCol != move.toCol) return true;
            if ((mover.color == PieceColor.White && move.toRow == 7) ||
                (mover.color == PieceColor.Black && move.toRow == 0))
            {
                return true;
            }
        }
        return false;
    }

    private void OrderMoves(InternalPiece[,] board, AIMove[] moves, int count, AIMove ttMove, SearchContext ctx, int ply, PieceColor sideToMove)
    {
        if (count <= 1) return;

        int[] scores = ctx.scoreBuffers[ply];
        int side = sideToMove == PieceColor.Black ? 1 : 0;

        for (int i = 0; i < count; i++)
        {
            AIMove move = moves[i];
            if (move.Equals(ttMove))
            {
                scores[i] = 1000000;
            }
            else
            {
                scores[i] = ScoreMoveForOrdering(board, move, ctx, ply, side);
            }
        }

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

    private static int ScoreMoveForOrdering(InternalPiece[,] board, AIMove move, SearchContext ctx, int ply, int side)
    {
        InternalPiece attacker = board[move.fromRow, move.fromCol];
        InternalPiece victim = board[move.toRow, move.toCol];

        int score = 0;
        if (victim.type != PieceType.None)
        {
            score += 100000 + victim.GetMaterialValue() * 10 - attacker.GetMaterialValue();
        }
        else if (attacker.type == PieceType.Pawn && move.fromCol != move.toCol)
        {
            score += 100000 + 1000 - 100;
        }

        if (attacker.type == PieceType.Pawn &&
            ((attacker.color == PieceColor.White && move.toRow == 7) ||
             (attacker.color == PieceColor.Black && move.toRow == 0)))
        {
            score += 90000;
        }

        if (score == 0)
        {
            if (move.Equals(ctx.killerMoves[ply, 0])) score += 8000;
            else if (move.Equals(ctx.killerMoves[ply, 1])) score += 7000;
            else if (attacker.type == PieceType.King && Math.Abs(move.toCol - move.fromCol) == 2) score += 500;
            else score += Math.Min(ctx.history[side, move.fromRow * 8 + move.fromCol, move.toRow * 8 + move.toCol], 6000);
        }

        return score;
    }

    // --- Zobrist & Make/Unmake ---

    private static int GetRightsMask(SearchRights rights)
    {
        int mask = 0;
        if (rights.whiteCanCastleKingSide) mask |= 1;
        if (rights.whiteCanCastleQueenSide) mask |= 2;
        if (rights.blackCanCastleKingSide) mask |= 4;
        if (rights.blackCanCastleQueenSide) mask |= 8;
        return mask;
    }

    private static ulong ComputeHash(InternalPiece[,] board, PieceColor sideToMove, SearchRights rights)
    {
        ulong h = 0;
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                InternalPiece p = board[r, c];
                if (p.type != PieceType.None)
                {
                    int colorIdx = p.color == PieceColor.Black ? 1 : 0;
                    h ^= ZobristPiece[colorIdx, (int)p.type, r * 8 + c];
                }
            }
        }

        if (sideToMove == PieceColor.Black) h ^= ZobristBlackToMove;
        h ^= ZobristCastle[GetRightsMask(rights)];
        if (rights.enPassantRow >= 0 && rights.enPassantCol >= 0 && rights.enPassantCol < 8)
        {
            h ^= ZobristEnPassantFile[rights.enPassantCol];
        }
        return h;
    }

    private static void MakeMove(InternalPiece[,] board, AIMove move, PieceColor sideToMove, ref SearchRights rights, ref ulong hash, out UndoInfo undo)
    {
        InternalPiece moved = board[move.fromRow, move.fromCol];
        InternalPiece captured = board[move.toRow, move.toCol];

        undo = new UndoInfo
        {
            movedPiece = moved,
            capturedPiece = captured,
            enPassantPiece = default,
            isEnPassant = false,
            isCastle = false,
            prevRights = rights,
            prevHash = hash
        };

        int fromSq = move.fromRow * 8 + move.fromCol;
        int toSq = move.toRow * 8 + move.toCol;
        int colorIdx = moved.color == PieceColor.Black ? 1 : 0;
        int oppIdx = 1 - colorIdx;

        // XOR out old EP and castling rights, toggle side to move
        if (rights.enPassantRow >= 0 && rights.enPassantCol >= 0 && rights.enPassantCol < 8)
        {
            hash ^= ZobristEnPassantFile[rights.enPassantCol];
        }
        hash ^= ZobristCastle[GetRightsMask(rights)];
        hash ^= ZobristBlackToMove;

        // Remove moved piece from origin
        hash ^= ZobristPiece[colorIdx, (int)moved.type, fromSq];
        board[move.fromRow, move.fromCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };

        // Capture (regular or en passant)
        if (captured.type != PieceType.None)
        {
            hash ^= ZobristPiece[oppIdx, (int)captured.type, toSq];
        }
        else if (moved.type == PieceType.Pawn && move.fromCol != move.toCol)
        {
            undo.isEnPassant = true;
            undo.enPassantPiece = board[move.fromRow, move.toCol];
            int epSq = move.fromRow * 8 + move.toCol;
            if (undo.enPassantPiece.type != PieceType.None)
            {
                hash ^= ZobristPiece[oppIdx, (int)undo.enPassantPiece.type, epSq];
            }
            board[move.fromRow, move.toCol] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
        }

        // Castling rook move
        if (moved.type == PieceType.King && Math.Abs(move.toCol - move.fromCol) == 2)
        {
            undo.isCastle = true;
            if (move.toCol == 6)
            {
                InternalPiece rook = board[move.fromRow, 7];
                board[move.fromRow, 5] = rook;
                board[move.fromRow, 7] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
                hash ^= ZobristPiece[colorIdx, (int)PieceType.Rook, move.fromRow * 8 + 7];
                hash ^= ZobristPiece[colorIdx, (int)PieceType.Rook, move.fromRow * 8 + 5];
            }
            else if (move.toCol == 2)
            {
                InternalPiece rook = board[move.fromRow, 0];
                board[move.fromRow, 3] = rook;
                board[move.fromRow, 0] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
                hash ^= ZobristPiece[colorIdx, (int)PieceType.Rook, move.fromRow * 8 + 0];
                hash ^= ZobristPiece[colorIdx, (int)PieceType.Rook, move.fromRow * 8 + 3];
            }
        }

        // Auto-promote Pawn to Queen
        InternalPiece placedPiece = moved;
        if (moved.type == PieceType.Pawn &&
            ((moved.color == PieceColor.White && move.toRow == 7) ||
             (moved.color == PieceColor.Black && move.toRow == 0)))
        {
            placedPiece.type = PieceType.Queen;
        }

        board[move.toRow, move.toCol] = placedPiece;
        hash ^= ZobristPiece[colorIdx, (int)placedPiece.type, toSq];

        // Update search rights
        if (moved.type == PieceType.Pawn && Math.Abs(move.toRow - move.fromRow) == 2)
        {
            rights.enPassantRow = (move.fromRow + move.toRow) / 2;
            rights.enPassantCol = move.toCol;
            hash ^= ZobristEnPassantFile[rights.enPassantCol];
        }
        else
        {
            rights.enPassantRow = -1;
            rights.enPassantCol = -1;
        }

        if (moved.type == PieceType.King)
        {
            if (sideToMove == PieceColor.White)
            {
                rights.whiteCanCastleKingSide = false;
                rights.whiteCanCastleQueenSide = false;
            }
            else
            {
                rights.blackCanCastleKingSide = false;
                rights.blackCanCastleQueenSide = false;
            }
        }

        if ((move.fromRow == 0 && move.fromCol == 0) || (move.toRow == 0 && move.toCol == 0)) rights.whiteCanCastleQueenSide = false;
        if ((move.fromRow == 0 && move.fromCol == 7) || (move.toRow == 0 && move.toCol == 7)) rights.whiteCanCastleKingSide = false;
        if ((move.fromRow == 7 && move.fromCol == 0) || (move.toRow == 7 && move.toCol == 0)) rights.blackCanCastleQueenSide = false;
        if ((move.fromRow == 7 && move.fromCol == 7) || (move.toRow == 7 && move.toCol == 7)) rights.blackCanCastleKingSide = false;

        hash ^= ZobristCastle[GetRightsMask(rights)];
    }

    private static void UnmakeMove(InternalPiece[,] board, AIMove move, ref SearchRights rights, ref ulong hash, in UndoInfo undo)
    {
        board[move.fromRow, move.fromCol] = undo.movedPiece;
        board[move.toRow, move.toCol] = undo.capturedPiece;

        if (undo.isEnPassant)
        {
            board[move.fromRow, move.toCol] = undo.enPassantPiece;
        }
        else if (undo.isCastle)
        {
            if (move.toCol == 6)
            {
                board[move.fromRow, 7] = board[move.fromRow, 5];
                board[move.fromRow, 5] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
            }
            else if (move.toCol == 2)
            {
                board[move.fromRow, 0] = board[move.fromRow, 3];
                board[move.fromRow, 3] = new InternalPiece { type = PieceType.None, color = PieceColor.White };
            }
        }

        rights = undo.prevRights;
        hash = undo.prevHash;
    }

    // --- Move Generation & Legality ---

    /// <summary>
    /// Generates all strictly legal moves for <paramref name="color"/>, filtering out moves that leave its King in check.
    /// </summary>
    public List<AIMove> GenerateLegalMoves(InternalPiece[,] board, PieceColor color)
    {
        return GenerateLegalMoves(board, color, SearchRights.None);
    }

    public List<AIMove> GenerateLegalMoves(InternalPiece[,] board, PieceColor color, SearchRights rights)
    {
        AIMove[] buffer = new AIMove[256];
        int count = GenerateLegalMovesInto(board, color, rights, buffer);
        List<AIMove> legalMoves = new List<AIMove>(count);
        for (int i = 0; i < count; i++) legalMoves.Add(buffer[i]);
        return legalMoves;
    }

    private int GenerateLegalMovesInto(InternalPiece[,] board, PieceColor color, SearchRights rights, AIMove[] destination)
    {
        int pseudoCount = GeneratePseudoMovesInto(board, color, rights, destination);
        int legalCount = 0;

        for (int i = 0; i < pseudoCount; i++)
        {
            AIMove move = destination[i];
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
                destination[legalCount++] = move;
            }

            board[move.fromRow, move.fromCol] = movedPiece;
            board[move.toRow, move.toCol] = capturedPiece;
            if (isEnPassant)
            {
                board[move.fromRow, move.toCol] = enPassantPawn;
            }
        }

        return legalCount;
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

    private static bool IsSquareAttacked(InternalPiece[,] board, int targetRow, int targetCol, PieceColor attackerColor)
    {
        // 1. Pawn attacks
        int attackerDir = (attackerColor == PieceColor.White) ? 1 : -1;
        int pawnRow = targetRow - attackerDir;
        if (IsOnBoard(pawnRow, targetCol - 1) && board[pawnRow, targetCol - 1].type == PieceType.Pawn && board[pawnRow, targetCol - 1].color == attackerColor)
            return true;
        if (IsOnBoard(pawnRow, targetCol + 1) && board[pawnRow, targetCol + 1].type == PieceType.Pawn && board[pawnRow, targetCol + 1].color == attackerColor)
            return true;

        // 2. Knight attacks
        for (int i = 0; i < 8; i++)
        {
            int nr = targetRow + KnightRowOffsets[i];
            int nc = targetCol + KnightColOffsets[i];
            if (IsOnBoard(nr, nc) && board[nr, nc].type == PieceType.Knight && board[nr, nc].color == attackerColor)
                return true;
        }

        // 3. King attacks
        for (int i = 0; i < 8; i++)
        {
            int kr = targetRow + KingRowOffsets[i];
            int kc = targetCol + KingColOffsets[i];
            if (IsOnBoard(kr, kc) && board[kr, kc].type == PieceType.King && board[kr, kc].color == attackerColor)
                return true;
        }

        // 4. Orthogonal sliding attacks (Rook / Queen)
        for (int d = 0; d < 4; d++)
        {
            if (IsAttackedAlongRay(board, targetRow, targetCol, OrthoDr[d], OrthoDc[d], attackerColor, PieceType.Rook))
                return true;
        }

        // 5. Diagonal sliding attacks (Bishop / Queen)
        for (int d = 0; d < 4; d++)
        {
            if (IsAttackedAlongRay(board, targetRow, targetCol, DiagDr[d], DiagDc[d], attackerColor, PieceType.Bishop))
                return true;
        }

        return false;
    }

    private static bool IsAttackedAlongRay(InternalPiece[,] board, int startRow, int startCol, int rowDir, int colDir, PieceColor attackerColor, PieceType sliderType)
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

    public List<AIMove> GenerateMoves(InternalPiece[,] board, PieceColor color)
    {
        return GenerateMoves(board, color, SearchRights.None);
    }

    public List<AIMove> GenerateMoves(InternalPiece[,] board, PieceColor color, SearchRights rights)
    {
        AIMove[] buffer = new AIMove[256];
        int count = GeneratePseudoMovesInto(board, color, rights, buffer);
        List<AIMove> moves = new List<AIMove>(count);
        for (int i = 0; i < count; i++) moves.Add(buffer[i]);
        return moves;
    }

    private static int GeneratePseudoMovesInto(InternalPiece[,] board, PieceColor color, SearchRights rights, AIMove[] moves)
    {
        int count = 0;
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                InternalPiece piece = board[r, c];
                if (piece.type != PieceType.None && piece.color == color)
                {
                    switch (piece.type)
                    {
                        case PieceType.Pawn: AddPawnMoves(board, r, c, color, rights, moves, ref count); break;
                        case PieceType.Rook: AddRookMoves(board, r, c, color, moves, ref count); break;
                        case PieceType.Knight: AddKnightMoves(board, r, c, color, moves, ref count); break;
                        case PieceType.Bishop: AddBishopMoves(board, r, c, color, moves, ref count); break;
                        case PieceType.Queen:
                            AddRookMoves(board, r, c, color, moves, ref count);
                            AddBishopMoves(board, r, c, color, moves, ref count);
                            break;
                        case PieceType.King: AddKingMoves(board, r, c, color, rights, moves, ref count); break;
                    }
                }
            }
        }
        return count;
    }

    private static void AddPawnMoves(InternalPiece[,] board, int r, int c, PieceColor color, SearchRights rights, AIMove[] moves, ref int count)
    {
        int direction = (color == PieceColor.White) ? 1 : -1;
        int oneStepRow = r + direction;
        if (IsOnBoard(oneStepRow, c) && board[oneStepRow, c].type == PieceType.None)
        {
            AddMove(r, c, oneStepRow, c, moves, ref count);
            bool onStartRank = (color == PieceColor.White && r == 1) || (color == PieceColor.Black && r == 6);
            int twoStepRow = r + 2 * direction;
            if (onStartRank && IsOnBoard(twoStepRow, c) && board[twoStepRow, c].type == PieceType.None)
                AddMove(r, c, twoStepRow, c, moves, ref count);
        }

        for (int dc = -1; dc <= 1; dc += 2)
        {
            int capCol = c + dc;
            if (!IsOnBoard(oneStepRow, capCol)) continue;
            InternalPiece piece = board[oneStepRow, capCol];
            if (piece.type != PieceType.None && piece.color != color)
            {
                AddMove(r, c, oneStepRow, capCol, moves, ref count);
            }
            else if (piece.type == PieceType.None &&
                     oneStepRow == rights.enPassantRow &&
                     capCol == rights.enPassantCol)
            {
                AddMove(r, c, oneStepRow, capCol, moves, ref count);
            }
        }
    }

    private static void AddRookMoves(InternalPiece[,] board, int r, int c, PieceColor color, AIMove[] moves, ref int count)
    {
        for (int d = 0; d < 4; d++) AddSlidingDirection(board, r, c, color, OrthoDr[d], OrthoDc[d], moves, ref count);
    }

    private static void AddBishopMoves(InternalPiece[,] board, int r, int c, PieceColor color, AIMove[] moves, ref int count)
    {
        for (int d = 0; d < 4; d++) AddSlidingDirection(board, r, c, color, DiagDr[d], DiagDc[d], moves, ref count);
    }

    private static void AddKnightMoves(InternalPiece[,] board, int r, int c, PieceColor color, AIMove[] moves, ref int count)
    {
        for (int i = 0; i < 8; i++)
        {
            int newRow = r + KnightRowOffsets[i];
            int newCol = c + KnightColOffsets[i];
            if (IsOnBoard(newRow, newCol))
            {
                InternalPiece piece = board[newRow, newCol];
                if (piece.type == PieceType.None || piece.color != color)
                    AddMove(r, c, newRow, newCol, moves, ref count);
            }
        }
    }

    private static void AddKingMoves(InternalPiece[,] board, int r, int c, PieceColor color, SearchRights rights, AIMove[] moves, ref int count)
    {
        for (int i = 0; i < 8; i++)
        {
            int newRow = r + KingRowOffsets[i];
            int newCol = c + KingColOffsets[i];
            if (IsOnBoard(newRow, newCol))
            {
                InternalPiece piece = board[newRow, newCol];
                if (piece.type == PieceType.None || piece.color != color)
                    AddMove(r, c, newRow, newCol, moves, ref count);
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
                    AddMove(homeRow, 4, homeRow, 6, moves, ref count);
                }

                if (canQueenSide &&
                    board[homeRow, 0].type == PieceType.Rook && board[homeRow, 0].color == color &&
                    board[homeRow, 1].type == PieceType.None && board[homeRow, 2].type == PieceType.None && board[homeRow, 3].type == PieceType.None &&
                    !IsSquareAttacked(board, homeRow, 3, attackerColor))
                {
                    AddMove(homeRow, 4, homeRow, 2, moves, ref count);
                }
            }
        }
    }

    private static void AddSlidingDirection(InternalPiece[,] board, int r, int c, PieceColor color, int rowDir, int colDir, AIMove[] moves, ref int count)
    {
        for (int i = 1; i < 8; i++)
        {
            int newRow = r + i * rowDir;
            int newCol = c + i * colDir;
            if (!IsOnBoard(newRow, newCol)) break;
            InternalPiece piece = board[newRow, newCol];
            if (piece.type == PieceType.None) AddMove(r, c, newRow, newCol, moves, ref count);
            else
            {
                if (piece.color != color) AddMove(r, c, newRow, newCol, moves, ref count);
                break;
            }
        }
    }

    private static bool IsOnBoard(int row, int col) => row >= 0 && row < 8 && col >= 0 && col < 8;

    private static void AddMove(int fromRow, int fromCol, int toRow, int toCol, AIMove[] moves, ref int count)
    {
        moves[count++] = new AIMove { fromRow = fromRow, fromCol = fromCol, toRow = toRow, toCol = toCol };
    }
}
