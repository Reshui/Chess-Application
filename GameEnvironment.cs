namespace Pieces;

using System.Numerics;
using System.Linq;
using System.Diagnostics.CodeAnalysis;

public class GameEnvironment
{
    ///<value>Array used to hold ChessPiece objects and their locations withn the current game.</value>
    public ChessPiece?[,] GameBoard = new ChessPiece[8, 8];

    private readonly ChessPiece _whiteKing;
    private readonly ChessPiece _blackKing;

    public Player? WhitePlayer;
    public Player? BlackPlayer;

    /// <value>Dictionary of <c>ChessPiece</c> objects keyed to a team color.</value>
    private Dictionary<Team, List<ChessPiece>> _teamPieces;

    ///<value>If over 50 then a Draw is determined.</value>
    private int _movesSinceLastCapture = 0;

    /// <value>List of submitted moves within the current <c>GameEnvironment</c> instance.</value>
    private List<MovementInformation> _gameMoves = new();

    public bool GameEnded =false;
    public ChessPiece this[int x, int y]
    {
        get => GameBoard[x, y]!;
        set => GameBoard[x, y] = value;
    }

    public GameEnvironment()
    {
        _teamPieces = GenerateBoard();
        _whiteKing = AssignKing(Team.White);
        _blackKing = AssignKing(Team.Black);
    }

    /// <summary>
    /// Constructor used for server - side code.
    /// </summary>
    public GameEnvironment(Player playerOne, Player playerTwo)
    {
        _teamPieces = GenerateBoard();

        var playerList = new List<Player> { playerOne, playerTwo };
        var rand = new Random();
        int playerID = rand.Next(1);
        WhitePlayer = playerList[playerID];
        playerID = playerID == 0 ? 1 : 0;
        BlackPlayer = playerList[playerID];

        WhitePlayer.AssignTeam(Team.White);
        BlackPlayer.AssignTeam(Team.Black);

        _whiteKing = AssignKing(Team.White);
        _blackKing = AssignKing(Team.Black);
    }

    /// <summary>
    /// Returns a <c>ChessPiece</c> object of type King. Call this during the initialization of the <c>GameEnvironment</c> class.
    /// </summary>
    /// <returns>A <c>ChessPiece</c> object of type king located on 1 of 2 starting lines.</returns>
    /// <param name="kingTeam">Enum from the ChessPiece.cs file. Can either be Team.White/Black</param>
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
        int[] pawnRows = { 1, GameBoard.GetUpperBound(0) - 1 };

        for (int columnIndex = 0; columnIndex < GameBoard.GetUpperBound(1) + 1; columnIndex++)
        {
            foreach (int pawnRow in pawnRows)
            {
                Team currentTeam = (pawnRow == 1) ? Team.White : Team.Black;
                int mainRow = (currentTeam == Team.White) ? 0 : GameBoard.GetUpperBound(0);

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
    /// Determines if a given king is checked based on king visibility.
    /// </summary>
    /// <param name="queriedKing">King that has its checked status tested.</param>
    /// <param name="board">2D board of <c>ChessPiece</c> Objects.</param>
    /// <returns>true if <paramref name="queriedKing"/> is checked; else false.</returns>
    public static bool IsKingChecked(ChessPiece queriedKing, ChessPiece?[,] board)
    {
        // This variable is needed to determine if a king can be attacked by an enemy pawn(pawns can only attack towards on side of the board.).
        int kingRow = queriedKing.ReturnLocation(0);

        for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
        {
            for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
            {   // Exclude the current space.
                if (verticalScalar == 0 && horizontalScalar == 0) continue;

                bool perpendicularVector = Math.Abs(verticalScalar) + Math.Abs(horizontalScalar) == 1;

                Vector2 vectorDirection = new Vector2(verticalScalar, horizontalScalar);
                // Propagate the vector at most 7 times to get from the current space to the opposite side of the board.
                for (int propagationCount = 1; propagationCount < 8; propagationCount++)
                {
                    var locationToCheck = Vector2.Add(queriedKing.CurrentLocation, Vector2.Multiply(vectorDirection, propagationCount));
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
                                bool enemyRookFound = pieceType == PieceType.Rook && perpendicularVector;
                                bool enemyBishopFound = pieceType == PieceType.Bishop && !perpendicularVector;
                                bool enemyPawnFound = pieceType == PieceType.Pawn && propagationCount == 1 && !perpendicularVector && ((kingRow > rowCoord && piece.PieceTeam == Team.White) || (kingRow < rowCoord && piece.PieceTeam == Team.Black));
                                bool enemyQueenOrKingFound = pieceType == PieceType.Queen || (pieceType == PieceType.King && propagationCount == 1);

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
            var locationToCheck = Vector2.Add(queriedKing.CurrentLocation, knightVector);

            int row = (int)locationToCheck.X, column = (int)locationToCheck.Y;

            if ((row is >= 0 and <= 7) && (column is >= 0 and <= 7))
            {
                ChessPiece? piece = board[row, column];

                if (piece != null && piece.PieceTeam != queriedKing.PieceTeam && piece.ReturnPieceType() == PieceType.Knight)
                {
                    return true;
                }
            }
        }
        return false;
    }
    /// <summary>
    /// Determines if movement details given by <paramref name="moveInfo"/> will expose a friendly king to check.
    /// </summary>
    /// <param name="moveInfo">Readonly struct with details on the movement to test.</param>
    /// <param name="board">2D <c>ChessPiece</c> board.</param>
    /// <returns>true if a friendly king will be checked; false otherwise.</returns>
    public static bool WillChangeResultInFriendlyCheck(MovementInformation moveInfo, ChessPiece?[,] board)
    {
        Team friendlyTeam = moveInfo.MainPiece.PieceTeam;

        var boardCopy = CopyBoard(board);

        ChangePieceLocation(boardCopy, moveInfo, movementHasBeenFinalized: false);
        // This is just to avoid the possibly null error.
        ChessPiece friendlyKing = moveInfo.MainPiece;

        foreach (ChessPiece piece in boardCopy)
        {
            if (piece != null && piece.PieceTeam == friendlyTeam & piece.IsKing())
            {
                friendlyKing = piece;
                break;
            }
        }
        return IsKingChecked(friendlyKing, boardCopy);
    }
    /// <summary>
    /// A friendly King from the current board.
    /// </summary>
    /// <param name="teamColor">Enum to determine which team to return a king for.</param>
    /// <returns>A friendly <c>ChessPiece</c> object with a <paramref name="_pieceType"/> property of type King.</returns>
    public ChessPiece ReturnKing(Team teamColor)
    {
        if (teamColor == Team.White) return _whiteKing;
        else return _blackKing;
    }
    /// <summary>
    /// Determins if the king is checked and if so, determines if any move can undo the check.
    /// </summary>
    /// <returns>A boolean represntation for if a given king is check-mated.</returns>
    /// <param name ="kingToCheck"><c>ChessPiece</c> object of type King for which this function is executed against.</param>
    public bool IsKingCheckMated(ChessPiece kingToCheck)
    {
        // It isn't possible to be checkmated without being in check first.
        if (!IsKingChecked(kingToCheck, GameBoard)) return false;

        ChessPiece?[,] mainBoardCopy = CopyBoard(GameBoard);

        // Determine if there are any moves that can be done to prevent the current check.
        foreach (var friendlyChessPiece in _teamPieces[kingToCheck.PieceTeam])
        {
            if (friendlyChessPiece.Captured == false)
            {
                // ignoreFriendlyInducedChecks: true  to avoid recursive stack overflow. King is already in check. Generate a list of all available moves. 
                List<MovementInformation> movesAvailableToQueriedPiece = friendlyChessPiece.AvailableMoves(mainBoardCopy, ignoreFriendlyInducedChecks: false, disableCastling: true);
                // Move the piece within the board and check if the king is still checked.
                foreach (var movement in movesAvailableToQueriedPiece)
                {
                    ChangePieceLocation(mainBoardCopy, movement, movementHasBeenFinalized: false);

                    if (IsKingChecked(kingToCheck, mainBoardCopy) == false) return false;

                    UndoChange(movement, mainBoardCopy);
                }
            }
        }
        // Previous checks have failed. Return true.
        return true;
    }
    /// <summary>
    /// Determines if a stalemate has been reached.
    /// </summary>
    /// <returns>true if a stalemate has been reached and false otherwise.</returns>
    public bool IsStalemate()
    {
        throw new NotImplementedException("Game Draw not implemented.");

        if (_movesSinceLastCapture >= 50) return true;

        #region Count Pieces on Board
        int piecesRemaining =(from pieces in _teamPieces.Values
                              from piece in pieces
                              where piece.Captured == false
                              select piece).Count();

        if (piecesRemaining == 2) return true;
        #endregion
    }

    /// <summary>
    /// When given a 2D <c>ChessPiece</c> array, copy any objects to a new array.
    /// </summary>
    /// <returns> A <c>ChessPiece</c>[,] array that contains a copy of every <c>ChessPiece</c>.</returns>
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
    /// <param name="ignoreChecks">If set to true then moves made by friendly pieces that would expose their king to check are ignored.</param>
    /// <param name="disableCastling">true if you want to disable a king's ability to castle.</param>
    /// <param name="queriedTeam">Team for which available moves are calculated.</param>
    /// <param name="queriedBoard">Array representation of a chess board used to calculate available moves.</param>
    /// <returns>A dictionary of possible moves keyed to a team.</returns>
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
    /// Replaces or moves <c>ChessPiece</c> object within a given chess board.
    /// </summary>
    /// <param name ="queriedBoard">2D nullable <c>ChessPiece</c> array.</param>
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

            secondaryOnBoard.CurrentLocation = movementDetails.SecondaryNewLocation;

            if (movementDetails.CastlingWithSecondary)
            {
                queriedBoard[(int)movementDetails.SecondaryNewLocation.X, (int)movementDetails.SecondaryNewLocation.Y] = secondaryOnBoard;
                if (movementHasBeenFinalized) secondaryInput.IncreaseMovementCount();
            }
            else if (movementDetails.CapturingSecondary)
            {
                secondaryOnBoard.Captured = true;
            }
        }

        var pieceToChange = queriedBoard[oldRowCoord, oldColumnCoord];
        queriedBoard[oldRowCoord, oldColumnCoord] = null;

        if (pieceToChange != null)
        {
            queriedBoard[newRowCoord, newColumnCoord] = pieceToChange;

            pieceToChange.CurrentLocation = movementDetails.MainNewLocation;

            if (movementHasBeenFinalized)
            {
                if (movementDetails.EnPassantCapturePossible) pieceToChange.EnableEnPassantCaptures();
                pieceToChange.IncreaseMovementCount();

            }
        }

    }
    /// <summary>
    /// Undoes a change to a <c>ChessPiece</c> Array
    /// </summary>
    /// <param name="move">Struct that contains information on the change to undo.</param>
    /// <param name="board">Array that changes will be undone within.</param>
    private static void UndoChange(MovementInformation move, ChessPiece?[,] board)
    {
        ChessPiece? pieceOne = board[(int)move.MainNewLocation.X, (int)move.MainNewLocation.Y];

        board[pieceOne!.ReturnLocation(0), pieceOne.ReturnLocation(1)] = null;

        board[move.MainPiece.ReturnLocation(0), move.MainPiece.ReturnLocation(1)] = pieceOne;

        pieceOne.CurrentLocation = move.MainPiece.CurrentLocation;

        if (move.CastlingWithSecondary)
        {
            ChessPiece? pieceTwo = board[(int)move.SecondaryNewLocation.X, (int)move.SecondaryNewLocation.Y];
            board[pieceTwo!.ReturnLocation(0), pieceTwo.ReturnLocation(1)] = null;

            board[move.SecondaryPiece!.ReturnLocation(0), move.SecondaryPiece.ReturnLocation(1)] = pieceTwo;
            pieceTwo!.CurrentLocation = move.SecondaryPiece.CurrentLocation;
        }
        else if (move.CapturingSecondary)
        {
            board[(int)move.MainNewLocation.X, (int)move.MainNewLocation.Y] = move.SecondaryPiece!.Copy();
        }
    }
    /// <summary>
    /// This method is used to submite finalized changes to the current game and return the next <c>Player</c> via <paramref name="currentlyActivePlayer"/>.
    /// </summary>
    /// <param name="newMove">Movement information to submit to the current instance of the <c>GameEnvironment</c> class.</param>
     public void SubmitFinalizedChange(MovementInformation newMove)
    {
        ChangePieceLocation(this.GameBoard, newMove, true);
        // Determine whose turn it is.
        var currentlyActivePlayerTeam = newMove.MainPiece.PieceTeam == Team.Black ? Team.White : Team.Black;

        if (newMove.CapturingSecondary) _movesSinceLastCapture = 0;
        else _movesSinceLastCapture++;

        DisablePlayerVulnerabilityToEnPassant(currentlyActivePlayerTeam);

        _gameMoves.Add(newMove);
    }

    /// <summary>
    /// Disables vulnerability to En Passant for the current <paramref name="activeTeam"/>.
    /// </summary>
    /// <param name="activeTeam">The currently active <c>Player</c>.</param>
    private void DisablePlayerVulnerabilityToEnPassant(Team activeTeam)
    {
        foreach (ChessPiece chessPiece in _teamPieces[activeTeam])
        {
            chessPiece.DisableEnPassantCaptures();
        }
    }

}
