namespace Pieces;
using System.Numerics;
using System.Timers;

public class GameEnvironment
{
    public ChessPiece?[,] GameBoard = new ChessPiece[8, 8];

    private ChessPiece _whiteKing;
    private ChessPiece _blackKing;

    private Player _whitePlayer;
    private Player _blackPlayer;

    private Dictionary<Team, List<ChessPiece>> _teamPieces;

    public ChessPiece this[int x, int y]
    {
        get => GameBoard[x, y]!;
        set => GameBoard[x, y] = value;
    }

    public GameEnvironment(Player playerOne, Player playerTwo)
    {
        _teamPieces = GenerateBoard();
        var playerList = new List<Player> { playerOne, playerTwo };
        var rf = new Random();

        int playerID = rf.Next(1);
        _whitePlayer = playerList[playerID];
        playerID = playerID == 0 ? 1 : 0;
        _blackPlayer = playerList[playerID];

        _whiteKing = AssignKing(Team.White);
        _blackKing = AssignKing(Team.Black);

    }
    /// <summary>
    /// Returns a ChessPiece object of type King. Call this during the initialization of the GameEnvironment class.
    /// </summary>
    /// <returns>A ChessPiece object of type king located on 1 of 2 starting lines.</returns>
    /// <param name="kingTeam">Enum from the ChessPiece.cs file. Can either be Team.White or .Black</param>
    private ChessPiece AssignKing(Team kingTeam)
    {
        int targetRow = kingTeam == Team.White ? 0 : this.GameBoard.GetUpperBound(0);
        return this[targetRow, 4];
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
        var pawnRows = new int[] { 1, GameBoard.GetUpperBound(0) - 1 };

        for (int columnIndex = 0; columnIndex < GameBoard.GetUpperBound(1) + 1; columnIndex++)
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
                // I've opted to start white at row 0.
                int mainRow = (currentTeam == Team.White) ? 0 : GameBoard.GetUpperBound(0);

                var pawnPiece = new ChessPiece(PieceType.Pawn, new Coords(pawnRow, columnIndex), currentTeam);
                var specialPiece = new ChessPiece(currentPiece, new Coords(mainRow, columnIndex), currentTeam);

                // Place piece on the board so it can be tracked
                GameBoard[pawnRow, columnIndex] = pawnPiece;
                GameBoard[mainRow, columnIndex] = specialPiece;

                piecesPerTeam[currentTeam].Add(pawnPiece);
                piecesPerTeam[currentTeam].Add(specialPiece);
            }
        }
        return piecesPerTeam;
    }

    /// <summary>
    /// Determine if a given space is empty or not.
    /// </summary>
    /// <returns>A boolean value that represents  if a null value exists at a certain location within the current game.</returns>
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
        //var allPossibleMoves = AllPossibleMovesPerTeam(GameBoard);

        /*foreach (ChessPiece king in new ChessPiece[] { _whiteKing, _blackKing })
        {
            if (IsKingChecked(king, allPossibleMoves))
            {
                if (IsKingCheckMated(king, allPossibleMoves))
                {

                }
            }
        }*/
    }
    /// <summary>
    /// Determines if a given king is checked based on king visibility.
    /// </summary>
    /// <returns>A boolean value for whether or not a king is checked.</returns>
    /// <param name="queriedKing">King that has its checked status tested.</param>
    /// <param name="board">2D board of ChessPiece Objects.</param>
    public static bool IsKingChecked(ChessPiece queriedKing, ChessPiece?[,] board)
    {
        int kingRow = queriedKing.ReturnLocation(0);

        for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
        {
            for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
            {   // Exclude the current space.
                if (verticalScalar == 0 && horizontalScalar == 0) continue;

                Vector2 vectorDirection = new Vector2(verticalScalar, horizontalScalar);
                // Propagate the vector at most 7 times to get from the current space to the opposite side of the board.
                for (int i = 1; i < 8; i++)
                {
                    var locationToCheck = Vector2.Add(queriedKing.currentLocation, Vector2.Multiply(vectorDirection, i));
                    int rowCoord = (int)locationToCheck.X;
                    int columnCoord = (int)locationToCheck.Y;

                    if ((rowCoord is >= 0 and <= 7) && (columnCoord is >= 0 and <= 7))
                    {
                        ChessPiece? piece = board[rowCoord, columnCoord];

                        if (piece != null)
                        {   // If a ChessPiece object is found determine its type and whether or not it is friendly.

                            if (piece.PieceTeam != queriedKing.PieceTeam)
                            {
                                PieceType pieceType = piece.ReturnPieceType();
                                // Certain captures are only available to specific combinations of vector scalars and piece types.
                                bool enemyRookFound = pieceType == PieceType.Rook && (horizontalScalar == 0 || verticalScalar == 0);
                                bool enemyBishopFound = pieceType == PieceType.Bishop && (horizontalScalar != 0 && verticalScalar != 0);
                                bool enemyPawnFound = pieceType == PieceType.Pawn && (horizontalScalar != 0 && verticalScalar != 0) && ((kingRow > rowCoord && piece.PieceTeam == Team.White) || (kingRow < rowCoord && piece.PieceTeam == Team.Black));
                                bool enemyQueenOrKingFound = pieceType == PieceType.Queen|| (pieceType == PieceType.King && i == 1);

                                if (enemyBishopFound || enemyQueenOrKingFound || enemyPawnFound || enemyRookFound)
                                {
                                    return true;
                                }
                            }
                            // A ChessPiece object was found. Stop propagation of vector.
                            break;
                        }
                    }
                    else
                    {
                        // Out of bounds. Stop propagation of vector.
                        break;
                    }
                }
            }
        }

        foreach (var knightVector in ChessPiece.KnightDirectionVectors())
        {
            var locationToCheck = Vector2.Add(queriedKing.currentLocation, knightVector);
            try
            {
                ChessPiece? piece = board[(int)locationToCheck.X, (int)locationToCheck.Y];

                if (piece != null && piece.PieceTeam != queriedKing.PieceTeam && piece.ReturnPieceType() == PieceType.Knight)
                {
                    return true;
                }
            }
            catch (IndexOutOfRangeException)
            {

            }
        }
        return false;
    }
    /// <summary>
    /// Determines if a movement will expose a friendly king.
    /// </summary>
    /// <param name="moveInfo">Readonly struct with details on the move to test.</param>
    /// <param name="board">2D ChessPiece board.</param>
    /// <returns>A boolean representation of whether or not a given move will result in an on side check.</returns>
    public static bool WillChangeResultInFriendlyCheck(MovementInformation moveInfo, ChessPiece?[,] board)
    {
        Team friendlyTeam = moveInfo.MainPiece.PieceTeam;

        var boardCopy = CopyBoard(board);

        ChangePieceLocation(boardCopy, moveInfo, movementHasBeenFinalized: false);
        // This is just to avoid the possibly null error.
        ChessPiece friendlyKing = moveInfo.MainPiece;

        foreach (ChessPiece? piece in boardCopy)
        {
            if (piece != null && piece.IsKing() && piece.PieceTeam == friendlyTeam)
            {
                friendlyKing = piece;
                break;
            }
        }
        return IsKingChecked(friendlyKing, boardCopy);
    }
    /// <summary>
    /// Determins if the king is checked and if so, determines if any move can undo the check.
    /// </summary>
    /// <returns>A boolean represntation for if a given king is check-mated.</returns>
    public bool IsKingCheckMated(ChessPiece kingToCheck, Dictionary<ChessPiece, List<MovementInformation>> opposingMoves)
    {
        // It isn't possible to be checkmated without being in check first.
        if (!IsKingChecked(kingToCheck, GameBoard)) return false;

        ChessPiece?[,] mainBoardCopy = CopyBoard(GameBoard);
        Team oppositeTeam = kingToCheck.PieceTeam == Team.White ? Team.Black : Team.White;

        // Determine if there are any moves that can be done to prevent the current check.
        foreach (var friendlyChessPiece in _teamPieces[kingToCheck.PieceTeam])
        {
            // ignoreFriendlyInducedChecks: true  to avoid recursive stack overflow. King is already in check. Generate a list of all available moves. 
            List<MovementInformation> movesAvailableToQueriedPiece = friendlyChessPiece.AvailableMoves(mainBoardCopy, ignoreFriendlyInducedChecks: true, disableCastling: true);
            // Move the piece within the board and check if the king is still checked.
            foreach (var movement in movesAvailableToQueriedPiece)
            {
                ChangePieceLocation(mainBoardCopy, movement, movementHasBeenFinalized: false);

                if (!IsKingChecked(kingToCheck, mainBoardCopy))
                {
                    return false;
                }

                mainBoardCopy[movement.MainPiece.ReturnLocation(0), movement.MainPiece.ReturnLocation(1)] = movement.MainPiece.Copy();

                mainBoardCopy[(int)movement.MainNewLocation.X, (int)movement.MainNewLocation.Y] = null;

                if (movement.SecondaryPiece != null)
                {
                    mainBoardCopy[movement.SecondaryPiece.ReturnLocation(0), movement.SecondaryPiece.ReturnLocation(1)] = movement.SecondaryPiece.Copy();
                    // If this is a castling vector.
                    if (!Vector2.Equals(movement.SecondaryNewLocation, ChessPiece.DefaultLocation))
                    {
                        mainBoardCopy[(int)movement.SecondaryNewLocation.X, (int)movement.SecondaryNewLocation.Y] = null;
                    }
                }
            }

        }
        // Previous checks have failed. Return true.
        return true;
    }
    /// <summary>
    /// When given a 2D ChessPiece array, copy any objects to a new array.
    /// </summary>
    /// <returns> A ChessPiece[,] array that contains a copy of every ChessPiece.</returns>
    public static ChessPiece[,] CopyBoard(ChessPiece?[,] boardToCopy)
    {
        int arrayRowCount = boardToCopy.GetUpperBound(0) + 1;
        int arrayColumnCount = boardToCopy.GetUpperBound(1) + 1;

        ChessPiece[,] copy2d = new ChessPiece[arrayRowCount, arrayColumnCount];

        for (int row = 0; row < arrayRowCount; row++)
        {
            for (int column = 0; column < arrayColumnCount; column++)
            {
                if (boardToCopy[row, column] != null)
                {
                    copy2d[row, column] = boardToCopy[row, column]!.Copy();
                }
            }
        }
        return copy2d;
    }

    /// <summary>
    /// Generates an array of Vectors represnting all moves for a given team.
    /// </summary>
    /// <returns>A dictionary of possible moves keyed to a team.</returns>
    /// <param name="ignoreChecks">If set to true then moves made by friendly pieces that would expose their king to check are ignored.</param>
    public static Dictionary<ChessPiece, List<MovementInformation>> AllPossibleMovesPerTeam(ChessPiece?[,] queriedBoard, Team queriedTeam, bool ignoreChecks, bool disableCastling)
    {
        var teamMoves = new Dictionary<ChessPiece, List<MovementInformation>>();

        foreach (var chessPiece in queriedBoard)
        {
            if (chessPiece != null && !chessPiece.Captured && chessPiece.PieceTeam == queriedTeam)
            {
                teamMoves.Add(chessPiece, chessPiece.AvailableMoves(queriedBoard!, ignoreChecks, disableCastling));
            }
        }

        return teamMoves;
    }

    /// <summary>
    /// Allows the GameEnvironment class to designate a ChessPiece object as captured.
    /// </summary>
    /// <param name="chessItem">ChessPiece object whose captured property will be edited.</param>
    private void DesignatePieceAsCaptured(ChessPiece chessItem)
    {
        chessItem.Captured = true;

        GameBoard[chessItem.ReturnLocation(0), chessItem.ReturnLocation(1)] = null;
    }
    /// <summary>
    /// Replaces or moves ChessPiece object within a given chess board.
    /// </summary>
    /// <param name ="queriedBoard">2D nullable ChessPiece array.</param>
    /// <param name ="movementDetails"> Struct that contains details for a given movement or capture.</param>
    /// <param name ="movementHasBeenFinalized">Boolean value that tells the code that this movemnet has been finalized and passes all checks.</param>
    public static void ChangePieceLocation(ChessPiece?[,] queriedBoard, MovementInformation movementDetails, bool movementHasBeenFinalized)
    {
        int oldRowCoord = movementDetails.MainPiece.ReturnLocation(0);
        int oldColumnCoord = movementDetails.MainPiece.ReturnLocation(1);

        int newRowCoord = (int)movementDetails.MainNewLocation.X;
        int newColumnCoord = (int)movementDetails.MainNewLocation.Y;

        if (movementDetails.SecondaryPiece != null)
        {
            ChessPiece secondaryInput = movementDetails.SecondaryPiece;
            // Since the board may be copy, deal with the object on the board rather than the input variables.
            ChessPiece secondaryOnBoard = queriedBoard[secondaryInput.ReturnLocation(0), secondaryInput.ReturnLocation(1)]!;

            queriedBoard[secondaryOnBoard.ReturnLocation(0), secondaryOnBoard.ReturnLocation(1)] = null;

            secondaryOnBoard.currentLocation = movementDetails.SecondaryNewLocation;

            if (!Vector2.Equals(movementDetails.SecondaryNewLocation, ChessPiece.DefaultLocation))
            {
                queriedBoard[(int)movementDetails.SecondaryNewLocation.X, (int)movementDetails.SecondaryNewLocation.Y] = secondaryOnBoard;
            }
            else
            {
                secondaryOnBoard.Captured = true;
            }
        }

        var pieceToChange = queriedBoard[oldRowCoord, oldColumnCoord];
        queriedBoard[oldRowCoord, oldColumnCoord] = null;

        if (pieceToChange != null)
        {
            queriedBoard[newRowCoord, newColumnCoord] = pieceToChange;

            pieceToChange.currentLocation = movementDetails.MainNewLocation;

            if (movementHasBeenFinalized) pieceToChange.IncreaseMovemntCount();
        }

    }
    private void PrintBoard()
    {

    }

}
