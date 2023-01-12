namespace Pieces;
using System.Numerics;
public class GameEnvironment
{
    private ChessPiece?[,] GameBoard = new ChessPiece[8, 8];
    private ChessPiece _whiteKing;
    private ChessPiece _blackKing;

    private Dictionary<Team, List<ChessPiece>> _teamPieces;
    public ChessPiece this[int x, int y]
    {
        get => GameBoard[x, y]!;
        set => GameBoard[x, y] = value;
    }

    public GameEnvironment()
    {
        _teamPieces = GenerateBoard();

    }
    /// <summary>
    /// Creates chess pieces for both teams and places them within the GameBoard array.
    /// </summary>
    /// <returns>A dictionary of pieces keyed to their respective teams.</returns>
    Dictionary<Team, List<ChessPiece>> GenerateBoard()
    {
        PieceType currentPiece = PieceType.Pawn;
        var piecesPerTeam = new Dictionary<Team, List<ChessPiece>>();

        foreach (var currentTeam in new Team[] { Team.White, Team.Black })
        {
            piecesPerTeam.Add(currentTeam, new List<ChessPiece>());
        }
        // 2nd row from the top and bottom.
        var pawnRows = new int[] { 1, GameBoard.GetLength(1) - 2 };

        for (int columnIndex = 0; columnIndex < GameBoard.GetLength(1); columnIndex++)
        {
            foreach (int pawnRow in pawnRows)
            {
                var currentTeam = (pawnRow == 1) ? Team.White : Team.Black;

                switch (columnIndex)
                {
                    case 0:
                    case 7:
                        currentPiece = PieceType.Rook;
                        break;
                    case 1:
                    case 6:
                        currentPiece = PieceType.Knight;
                        break;
                    case 2:
                    case 5:
                        currentPiece = PieceType.Bishop;
                        break;
                    case 3:
                        currentPiece = PieceType.Queen;
                        break;
                    case 4:
                        currentPiece = PieceType.King;
                        break;
                }
                // C# arrays start with [0,0] in the top left corner.
                // I've opted to start white at the top.
                int mainRow = (currentTeam == Team.White) ? 0 : GameBoard.GetLength(1) - 1;

                var pawnPiece = new ChessPiece(PieceType.Pawn, new Coords(pawnRow, columnIndex), currentTeam);
                var specialPiece = new ChessPiece(currentPiece, new Coords(mainRow, columnIndex), currentTeam);

                // Place piece on the board so it can be tracked
                GameBoard[pawnRow, columnIndex] = pawnPiece;
                GameBoard[mainRow, columnIndex] = specialPiece;

                piecesPerTeam[currentTeam].Add(pawnPiece);
                piecesPerTeam[currentTeam].Add(specialPiece);

                if (currentPiece == PieceType.King)
                {
                    if (currentTeam == Team.White) _whiteKing = GameBoard[mainRow, columnIndex]!;
                    else _blackKing = GameBoard[mainRow, columnIndex]!;
                }
            }
        }
        return piecesPerTeam;
    }

    /// <summary>
    /// Determine if a given space is empty or not.
    /// </summary>
    /// <returns>A boolean value that represents  if a null value exists at a certain location within the current game.</returns >
    public bool IsSpaceEmpty(int row, int column)
    {
        return GameBoard[row, column] == null;
    }

    /// <summary>
    /// This method is called after every move and determines if a check or checkmate has been acquired for either team.
    /// </summary>
    /// <returns>A boolean value.</returns>
    private void KingInteractions()
    {
        var allPossibleMoves = AllPossibleMovesPerTeam(GameBoard);

        foreach (ChessPiece king in new ChessPiece[] { _whiteKing, _blackKing })
        {
            if (IsKingChecked(king, allPossibleMoves))
            {
                if (IsKingCheckMated(king, allPossibleMoves))
                {

                }
            }
        }
    }

    public bool IsKingChecked(ChessPiece kingToCheck, Dictionary<Team, Vector2[]> possibleMoves)
    {
        List<Vector2> opposingChecks =
                (from oppositeMoves in possibleMoves[kingToCheck.PieceTeam == Team.White ? Team.Black : Team.White]
                 where oppositeMoves.Equals(kingToCheck.currentLocation)
                 select oppositeMoves).ToList();

        if (opposingChecks.Count > 0) return true;
        return false;
    }
    /// <summary>
    /// Determins if the king is checked and if so determines if any move can undo the check.
    /// </summary>
    /// <returns>A boolean represntation for if a given king is check-mated.</returns>
    public bool IsKingCheckMated(ChessPiece kingToCheck, Dictionary<Team, Vector2[]> possibleMoves)
    {
        // It isn't possible to be checkmated without being in check.
        if (!IsKingChecked(kingToCheck, possibleMoves)) return false;

        ChessPiece?[,] mainBoardCopy = CopyBoard();
        // Determine if there are any moves that can be done to prevent the current check.
        foreach (var friendlyChessPiece in _teamPieces[kingToCheck.PieceTeam])
        {
            List<Vector2> movesAvailableToQueriedPiece = friendlyChessPiece.AvailableMoves(mainBoardCopy);

            var originalPosition = new float[2];

            friendlyChessPiece.currentLocation.CopyTo(originalPosition);

            foreach (Vector2 movement in movesAvailableToQueriedPiece)
            {
                ChangePieceLocation(mainBoardCopy, movement, friendlyChessPiece);

                if (!IsKingChecked(kingToCheck, AllPossibleMovesPerTeam(mainBoardCopy)))
                {
                    return false;
                }
            }
            ChangePieceLocation(mainBoardCopy, new Vector2(originalPosition[0], originalPosition[1]), friendlyChessPiece);
        }

        return true;
    }
    private ChessPiece[,] CopyBoard()
    {
        ChessPiece[,] copy2d = new ChessPiece[8, 8];

        for (int row = 0; row < GameBoard.GetLength(0); row++)
        {
            for (int column = 0; row < GameBoard.GetLength(1); column++)
            {
                copy2d[row, column] = GameBoard[row, column]!.Copy();
            }
        }
        return copy2d;
    }
    /// <summary>
    /// Generates an array of Vectors represnting all moves for a given team.
    /// </summary>
    /// <returns>A dictionary of possible moves keyed to a team.</returns>
    public Dictionary<Team, Vector2[]> AllPossibleMovesPerTeam(ChessPiece?[,] queriedBoard)
    {
        var availableTeams = new Team[] { Team.Black, Team.White };

        var currentlyAvailableMoves = new Dictionary<Team, Vector2[]>();

        foreach (Team queriedTeam in availableTeams)
        {
            // Will a hold a list of possible moves for each chess piece on the current team.
            var teamMoves = new List<List<Vector2>>();

            foreach (var chessPiece in queriedBoard)
            {
                if (chessPiece != null && !chessPiece.Captured && chessPiece.PieceTeam == queriedTeam)
                {
                    teamMoves.Add(chessPiece.AvailableMoves(GameBoard));
                }
            }

            var allMoves = (from pieceMovements in teamMoves
                            from pieceMovement in pieceMovements
                            select pieceMovement).ToArray();

            currentlyAvailableMoves.Add(queriedTeam, allMoves);
        }
        return currentlyAvailableMoves;
    }
    private void DesignatePieceAsCaptured(ChessPiece chessItem)
    {
        chessItem.Captured = true;

        GameBoard[(int)chessItem.currentLocation.X, (int)chessItem.currentLocation.Y] = null;
    }
    /// <summary>
    /// Removes chess piece from the board and replaces or moves it.
    /// </summary>
    private static void ChangePieceLocation(ChessPiece?[,] queriedBoard, Vector2 newLocation, ChessPiece pieceToChange)
    {

        int oldRowCoord = (int)pieceToChange.currentLocation.X;
        int oldColumnCoord = (int)pieceToChange.currentLocation.Y;

        if (!(newLocation == null))
        {
            int newRowCoord = (int)newLocation.X;
            int newColumnCoord = (int)newLocation.Y;

            queriedBoard[oldRowCoord, oldColumnCoord] = null;

            queriedBoard[newRowCoord, newColumnCoord] = pieceToChange;

            pieceToChange.currentLocation = newLocation;

        }
        else
        {
            queriedBoard[oldRowCoord, oldColumnCoord] = null;
            //pieceToChange.currentLocation = null;
        }

    }

}
