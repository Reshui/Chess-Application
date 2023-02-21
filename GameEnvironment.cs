namespace Pieces;

using System.Numerics;
public enum GameState
{
    LocalWin, LocalLoss, GameDraw, Playing
}

public class GameEnvironment
{
    private readonly ChessPiece _whiteKing;
    private readonly ChessPiece _blackKing;
    private bool _gameEnded = false;

    /// <summary>Array used to hold <see cref="Pieces.ChessPiece"/> instances and their locations withn the current game.</summary>
    public readonly ChessPiece?[,] GameBoard = new ChessPiece[8, 8];

    /// <summary>Array used to store visual information about <see cref="GameBoard"/>.</summary>
    /// <remarks>Assigned a value when an instance of <see cref="Chess_GUi.BoardGUI"/> is generated.</remarks>
    public PictureBox[,]? Squares { get; set; }

    /// <summary>Player dictionary keyed to the <see cref="Pieces.Team"/> that they have been assigned to.</summary>
    public Dictionary<Team, Player> AssociatedPlayers = new();

    /// <summary>Dictionary of <see cref="Pieces.ChessPiece"/> objects keyed to a <see cref="Team"/> enum.</summary>
    private readonly Dictionary<Team, List<ChessPiece>> _teamPieces;

    /// <summary>Stores how many moves have been submitted since the last capture was made.</summary>
    /// <remarks>If over 50 then a Draw is determined.</remarks>
    private int _movesSinceLastCapture = 0;

    /// <summary>List of submitted moves within the current <see cref="GameEnvironment"/> instance.</summary>
    private readonly List<MovementInformation> _gameMoves = new();

    /// <summary>Gets a boolean that denotes whether or not a given instance has ended.</summary>
    /// <value><see langword="true"/> if the <see cref="GameEnvironment"/> instance has ended; otherwise, <see langword="false"/>.</value>
    public bool GameEnded { get => _gameEnded; }

    /// <summary>Server-Side integer used to identify the current <see cref="GameEnvironment"/> instance.</summary>
    /// <remarks>Value is auto-incremented in the relevant constructors.</remarks>
    private static int s_instanceNumber = 0;

    /// <summary>Gets or initializes an ID number used to track the current instance on the server.</summary>
    /// <value>The ID of the current <see cref="GameEnvironment"/> instance on the server.</value>
    public int GameID { get; init; }

    /// <summary>Gets or initializes a <see cref="Pieces.Team"/> enum that represents the assigned Team of the client.</summary>
    /// <remarks>Variable used by the client-side <see cref="BoardGUI"/> instance to limit interaction with the board until it is that team's turn.</remarks>
    /// <value><see cref="Pieces.Team"/> assigned to the local user.</value>
    public Team PlayerTeam { get; init; }

    /// <summary>Gets or sets a value representing which <see cref="Pieces.Team"/> is currently allowed to submit MovementInformation.</summary>
    /// <remarks>Alternated whenever <see cref="GameEnvironment.SubmitFinalizedChange(MovementInformation)"/> is called.</remarks>
    ///<value>The <see cref="Pieces.Team"/> that is currently allowed to submit <see cref="Pieces.MovementInformation"/> to the <see cref="GameEnvironment"/> instance.</value>
    public Team ActiveTeam { get; set; } = Team.White;

    /// <summary>Gets or sets the current game state.</summary>
    /// <value>The current game state.</value>
    public GameState MatchState { get; set; } = GameState.Playing;

    private readonly bool _initializedByServer = false;
    private readonly bool _initializedByClient = false;
    public ChessPiece this[int x, int y]
    {
        get => GameBoard[x, y]!;
        set => GameBoard[x, y] = value;
    }

    /// <summary>
    /// Constructor used for client - side code.
    /// <summary>
    public GameEnvironment(int serverSideID, Team playerTeam)
    {
        GameID = serverSideID;
        PlayerTeam = playerTeam;

        _teamPieces = GenerateBoard();
        _whiteKing = AssignKing(Team.White);
        _blackKing = AssignKing(Team.Black);

        _initializedByClient = true;
    }

    /// <summary>
    /// Constructor used by the server to track changes of active games and assign teams to Players.
    /// </summary>
    /// <param name="playerOne">First player to be tracked.</param>
    /// <param name="playerTwo">Second player to track.</param>
    public GameEnvironment(Player playerOne, Player playerTwo)
    {
        _teamPieces = GenerateBoard();
        GameID = ++s_instanceNumber;

        var playerList = new List<Player> { playerOne, playerTwo };

        var rand = new Random();
        int playerID = rand.Next(playerList.Count);

        AssociatedPlayers.Add(Team.White, playerList[playerID]);
        AssociatedPlayers.Add(Team.Black, playerList[playerID == 0 ? 1 : 0]);

        _whiteKing = AssignKing(Team.White);
        _blackKing = AssignKing(Team.Black);

        _initializedByServer = true;
    }

    /// <summary>
    /// Returns a <see cref="ChessPiece"/> object of type King. Call this during the initialization of the <see cref="GameEnvironment"/> class.
    /// </summary>
    /// <returns>A <see cref="ChessPiece"/> object of type king located on 1 of 2 starting lines.</returns>
    /// <param name="kingTeam">Variable used to find a given king.</param>
    private ChessPiece AssignKing(Team kingTeam)
    {
        int targetRow = kingTeam == Team.White ? 0 : this.GameBoard.GetUpperBound(0);
        return this[targetRow, 4];
    }

    /// <summary>
    /// Creates chess pieces for both teams and places them within the <see cref="GameBoard"/> array.
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
    /// Determines if <paramref name="queriedKing"/> is checked on <paramref name="board"/>.
    /// </summary>
    /// <param name="queriedKing">King that has its checked status tested.</param>
    /// <param name="board">2D board of <see cref="ChessPiece"/> Objects.</param>
    /// <returns><see langword="true"/> if <paramref name="queriedKing"/> is checked; else <see langword="false"/>.</returns>
    public static bool IsKingChecked(ChessPiece queriedKing, ChessPiece?[,] board)
    {
        // This variable is needed to determine if a king can be attacked by an enemy pawn(pawns can only attack towards on side of the board.).
        int kingRow = queriedKing.CurrentRow;

        for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
        {
            for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
            {   // Exclude the current space.
                if (verticalScalar == 0 && horizontalScalar == 0) continue;

                bool perpendicularVector = Math.Abs(verticalScalar) + Math.Abs(horizontalScalar) == 1;

                var vectorDirection = new Vector2(verticalScalar, horizontalScalar);
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
                                PieceType pieceType = piece.AssignedType;
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
                    {   // Out of bounds. Stop propagation of vector.
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

                if (piece != null && piece.PieceTeam != queriedKing.PieceTeam && piece.AssignedType == PieceType.Knight)
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
    /// <param name="board">2D <see cref="ChessPiece[,]"/> board.</param>
    /// <returns><see langword="true"/> if a friendly king will be checked; otherwise, <see langword="false"/>.</returns>
    public static bool WillChangeResultInFriendlyCheck(MovementInformation moveInfo, ChessPiece?[,] board)
    {
        Team friendlyTeam = moveInfo.SubmittingTeam;

        var boardCopy = CopyBoard(board);

        ChangePieceLocation(boardCopy, moveInfo, movementHasBeenFinalized: false);
        // This is just to avoid the possibly null error.
        ChessPiece friendlyKing = moveInfo.MainPiece;

        foreach (ChessPiece piece in boardCopy)
        {
            if (piece != null && piece.PieceTeam == friendlyTeam && piece.IsKing())
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
    /// <returns>A friendly <see cref="ChessPiece"/> object with a <see cref="ChessPiece._pieceType"/> property of <see cref="PieceType.King"/>.</returns>
    public ChessPiece ReturnKing(Team teamColor)
    {
        if (teamColor == Team.White) return _whiteKing;
        else return _blackKing;
    }
    /// <summary>
    /// Determins if the king is checked and if so, determines if any move can undo the check.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="kingToCheck"/> is check-mated; otherwise, <see langword="false"/>.</returns>
    /// <param name ="kingToCheck"><see cref="ChessPiece"/> object of type King for which this function is executed against.</param>
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
    /// <returns><see langword="true"/> if a stalemate has been reached; otherwise, <see langword="false"/>.</returns>
    public bool IsStalemate()
    {
        if (_movesSinceLastCapture >= 50) return true;

        #region Count Pieces on Board
        int piecesRemaining = (from pieces in _teamPieces.Values
                               from piece in pieces
                               where piece.Captured == false
                               select piece).Count();

        if (piecesRemaining == 2) return true;
        #endregion

        return false;
    }

    /// <summary>
    /// When given a 2D <see cref="ChessPiece"/> array, copy any objects to a new array.
    /// </summary>
    /// <returns> A <see cref="ChessPiece"/>[,] array that contains a copy of every <see cref="ChessPiece"/>.</returns>
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
    /// Generates an array of Vectors represnting all moves for a given <paramref name ="queriedTeam"/>.
    /// </summary>
    /// <param name="ignoreChecks">If set to true then moves made by friendly pieces that would expose their king to check are ignored.</param>
    /// <param name="disableCastling"><see langword="true"/> if you want to disable a king's ability to castle.</param>
    /// <param name="queriedTeam">Team for which available moves are calculated.</param>
    /// <param name="queriedBoard">Array representation of a chess board used to calculate available moves.</param>
    /// <returns>A dictionary of possible moves for chess pieces that are on <paramref name="queriedTeam"/>.</returns>
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
    /// Replaces or moves <see cref="ChessPiece"/> object within <paramref name ="queriedBoard"/>.
    /// </summary>
    /// <param name ="queriedBoard">2D nullable <see cref="ChessPiece"/> array.</param>
    /// <param name ="movementDetails">Struct that contains details for a given movement or capture.</param>
    /// <param name ="movementHasBeenFinalized">Boolean value that tells the code that this movemnet has been finalized and passes all checks.</param>
    public static void ChangePieceLocation(ChessPiece?[,] queriedBoard, MovementInformation movementDetails, bool movementHasBeenFinalized)
    {
        int oldRowCoord = movementDetails.MainPiece.CurrentRow;
        int oldColumnCoord = movementDetails.MainPiece.CurrentColumn;

        int newRowCoord = (int)movementDetails.MainNewLocation.X;
        int newColumnCoord = (int)movementDetails.MainNewLocation.Y;

        if (movementDetails.SecondaryPiece is not null)
        {
            ChessPiece secondaryInput = movementDetails.SecondaryPiece;
            // Since the board may be copy, deal with the object on the board rather than the input variables.
            ChessPiece secondaryOnBoard = queriedBoard[secondaryInput.CurrentRow, secondaryInput.CurrentColumn]!;

            queriedBoard[secondaryOnBoard.CurrentRow, secondaryOnBoard.CurrentColumn] = null;

            secondaryOnBoard.AssignLocation(movementDetails.SecondaryNewLocation);

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

            pieceToChange.AssignLocation(movementDetails.MainNewLocation);

            if (movementHasBeenFinalized)
            {
                if (movementDetails.EnPassantCapturePossible) pieceToChange.EnableEnPassantCaptures();
                pieceToChange.IncreaseMovementCount();

            }
        }

    }
    /// <summary>
    /// Undoes a change to a <see cref="ChessPiece"/> Array
    /// </summary>
    /// <param name="move">Struct that contains information on the change to undo.</param>
    /// <param name="board">Array that changes will be undone within.</param>
    private static void UndoChange(MovementInformation move, ChessPiece?[,] board)
    {
        ChessPiece? pieceOne = board[(int)move.MainNewLocation.X, (int)move.MainNewLocation.Y];

        board[pieceOne!.CurrentRow, pieceOne.CurrentColumn] = null;

        board[move.MainPiece.CurrentRow, move.MainPiece.CurrentColumn] = pieceOne;

        pieceOne.AssignLocation(move.MainPiece.CurrentLocation);

        if (move.CastlingWithSecondary)
        {
            ChessPiece? pieceTwo = board[(int)move.SecondaryNewLocation.X, (int)move.SecondaryNewLocation.Y];
            board[pieceTwo!.CurrentRow, pieceTwo.CurrentColumn] = null;

            board[move.SecondaryPiece!.CurrentRow, move.SecondaryPiece.CurrentColumn] = pieceTwo;
            pieceTwo!.AssignLocation(move.SecondaryPiece.CurrentLocation);
        }
        else if (move.CapturingSecondary)
        {
            board[(int)move.MainNewLocation.X, (int)move.MainNewLocation.Y] = move.SecondaryPiece!.Copy();
        }
    }
    /// <summary>
    /// This method is used to submit finalized changes to <see cref="GameBoard"/> and exchanges <see cref="ActiveTeam"/> with the opposite <see cref="Team"/>.
    /// </summary>
    /// <param name="newMove"><see cref="MovementInformation"/> to submit to <see cref="GameBoard"/>.</param>
    public void SubmitFinalizedChange(MovementInformation newMove)
    {
        if (newMove.SubmittingTeam == ActiveTeam)
        {
            ChangePieceLocation(this.GameBoard, newMove, true);
            // Determine whose turn it is.
            ActiveTeam = (ActiveTeam == Team.White ? Team.Black : Team.White);

            if (newMove.CapturingSecondary) _movesSinceLastCapture = 0;
            else _movesSinceLastCapture++;

            DisablePlayerVulnerabilityToEnPassant(ActiveTeam);

            _gameMoves.Add(newMove);
        }
        else throw new Exception("The wrong team has submitted a move.");
    }

    /// <summary>
    /// Disables vulnerability to En Passant for the current <paramref name="activeTeam"/>.
    /// </summary>
    /// <param name="activeTeam">The currently active <see cref="Player"/>.</param>
    private void DisablePlayerVulnerabilityToEnPassant(Team activeTeam)
    {
        foreach (ChessPiece chessPiece in _teamPieces[activeTeam])
        {
            if (chessPiece.AssignedType == PieceType.Pawn) chessPiece.DisableEnPassantCaptures();
        }
    }

    /// <summary>
    /// Updates a client-side <see cref="GameEnvironment"/> instance and corresponding visuals using <paramref name="newMove"/>.
    /// </summary>
    /// <param name="newMove">Board movement used to update a <see cref="GameEnvironment"/> instance.</param>
    public void ChangeGameBoardAndGUI(MovementInformation newMove, bool piecesAlreadyMovedOnGUI)
    {
        if (GameBoard != null && Squares != null && ActiveTeam == newMove.SubmittingTeam)
        {
            SubmitFinalizedChange(newMove);

            if (IsKingCheckMated(ReturnKing(PlayerTeam)))
            {
                ChangeGameState(GameState.LocalLoss);
            }
            else if (IsKingCheckMated(ReturnKing(PlayerTeam == Team.White ? Team.Black : Team.White)))
            {
                ChangeGameState(GameState.LocalWin);
            }
            else if (IsStalemate())
            {
                ChangeGameState(GameState.GameDraw);
            }

            #region Update Graphics
            if (!piecesAlreadyMovedOnGUI)
            {                
                if (newMove.SecondaryPiece is not null)
                {
                    ChessPiece secPiece = newMove.SecondaryPiece;
                    PictureBox? secBox = Squares[secPiece.CurrentRow, secPiece.CurrentColumn];

                    if (secBox is not null)
                    {
                        if (newMove.CapturingSecondary)
                        {
                            secBox.Image = null;
                        }
                        else if (newMove.CastlingWithSecondary)
                        {
                            Squares[newMove.SecondaryCoords.RowIndex, newMove.SecondaryCoords.ColumnIndex]!.Image = secBox.Image;
                            secBox.Image = null;
                        }
                    }
                }

                ChessPiece mainPiece = newMove.MainPiece;
                PictureBox? mainBox = Squares[mainPiece.CurrentRow, mainPiece.CurrentColumn];

                if (mainBox is not null)
                {
                    Squares[newMove.MainCoords.RowIndex, newMove.MainCoords.ColumnIndex]!.Image = mainBox.Image;
                    mainBox.Image = null;
                }
            }
            #endregion

            if (MatchState != GameState.Playing)
            {
                throw new NotImplementedException("Game ended UI changes/updates haven't been implemented.");
            }
        }
    }
    /// <summary>Changes the <see cref="MatchState"/> property to <paramref name="newState"/> and ends the game.</summary>
    /// <param name="newState">New game state for the current instance.</param>
    public void ChangeGameState(GameState newState)
    {
        MatchState = newState;
        _gameEnded = newState != GameState.Playing;
    }
}
