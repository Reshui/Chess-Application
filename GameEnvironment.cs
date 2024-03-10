namespace Pieces;

using System.Numerics;

public enum GameState
{
    LocalWin, LocalLoss, GameDraw, Playing, OpponentDisconnected, ServerUnavailable
}

public class GameEnvironment
{
    private readonly ChessPiece _whiteKing;
    private readonly ChessPiece _blackKing;

    /// <summary>Stores how many moves have been submitted since the last capture was made.</summary>
    /// <remarks>If over 50 then a Draw is determined.</remarks>
    private int _movesSinceLastCapture = 0;

    /// <summary>Array used to hold <see cref="ChessPiece"/> instances and their locations withn the current game.</summary>
    public readonly ChessPiece?[,] GameBoard = new ChessPiece[8, 8];

    /// <summary>Gets or sets an array used to store visual information about the current state of <see cref="GameBoard"/>.</summary>
    /// <remarks>Assigned a value when an instance of <see cref="Chess_GUi.BoardGUI"/> is generated.</remarks>
    public PictureBox[,]? Squares { get; set; }

    /// <summary>Player dictionary keyed to the <see cref="Team"/> that they have been assigned to.</summary>
    public Dictionary<Team, Player> AssociatedPlayers = new();

    /// <summary>Dictionary of <see cref="ChessPiece"/> objects keyed to a <see cref="Team"/> enum.</summary>
    private readonly Dictionary<Team, Dictionary<string, ChessPiece>> _chessPiecesByTeam;

    /// <summary>List of submitted moves within the current <see cref="GameEnvironment"/> instance.</summary>
    private readonly List<MovementInformation> _gameMoves = new();

    /// <summary>Gets or initializes an ID number used to track the current instance on the server.</summary>
    /// <value>The ID of the current <see cref="GameEnvironment"/> instance on the server.</value>
    public int GameID { get; init; }

    /// <summary>Gets or initializes a <see cref="Team"/> enum that represents the assigned Team of the client.</summary>
    /// <remarks>Variable used by the client-side <see cref="BoardGUI"/> instance to limit interaction with the board until it is that team's turn.</remarks>
    /// <value><see cref="Team"/> assigned to the local user.</value>
    public Team PlayerTeam { get; init; }

    /// <summary>Gets or sets a value representing which <see cref="Team"/> is currently allowed to submit MovementInformation.</summary>
    /// <remarks>Alternated whenever <see cref="SubmitFinalizedChange(MovementInformation)"/> is called.</remarks>
    ///<value>The <see cref="Team"/> that is currently allowed to submit <see cref="MovementInformation"/> to the <see cref="GameEnvironment"/> instance.</value>
    public Team ActiveTeam { get; set; } = Team.White;

    /// <summary>Gets or sets the current game state.</summary>
    /// <value>The current game state.</value>
    public GameState MatchState { get; private set; } = GameState.Playing;

    /// <summary>Gets a boolean that denotes whether or not a given instance has ended.</summary>
    /// <value><see langword="true"/> if the <see cref="GameEnvironment"/> instance has ended; otherwise, <see langword="false"/>.</value>
    public bool GameEnded { get => MatchState != GameState.Playing; }

    public ChessPiece this[int x, int y]
    {
        get => GameBoard[x, y]!;
        set => GameBoard[x, y] = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameEnvironment"/> instance.
    /// </summary>
    /// <param name="playerTeam"><see cref="Team"/> assigned to client-side <see cref="GameEnvironment"/> instances.</param>
    /// <param name="serverSideID">Sever generated id  assigned by server to identify this instance.</param>
    public GameEnvironment(int serverSideID, Team playerTeam)
    {
        GameID = serverSideID;
        PlayerTeam = playerTeam;

        _chessPiecesByTeam = GenerateBoard();
        AssignKings(out _whiteKing, out _blackKing);
    }

    private void AssignKings(out ChessPiece whiteKing, out ChessPiece blackKing)
    {
        whiteKing = GameBoard[GameBoard.GetLowerBound(0), 4]!;
        blackKing = GameBoard[GameBoard.GetUpperBound(0), 4]!;
    }

    /// <summary>
    /// Creates chess pieces for both teams and places them within the <see cref="GameBoard"/> array.
    /// </summary>
    /// <returns>A dictionary of pieces keyed to their Ids keyed to the team they are on.</returns>
    Dictionary<Team, Dictionary<string, ChessPiece>> GenerateBoard()
    {
        var piecesPerTeam = new Dictionary<Team, Dictionary<string, ChessPiece>>
        {
            { Team.White, new Dictionary<string, ChessPiece>() },
            { Team.Black, new Dictionary<string, ChessPiece>() }
        };

        // 2nd row from the top and bottom.
        int[] pawnRows = { 1, GameBoard.GetUpperBound(0) - 1 };

        for (int columnIndex = 0; columnIndex <= GameBoard.GetUpperBound(1); ++columnIndex)
        {
            PieceType specialPieceType = columnIndex switch
            {
                0 or 7 => PieceType.Rook,
                1 or 6 => PieceType.Knight,
                2 or 5 => PieceType.Bishop,
                3 => PieceType.Queen,
                4 => PieceType.King,
                _ => throw new ArgumentOutOfRangeException($"{nameof(columnIndex)} is greater than 7 or less than 0.")
            };

            foreach (int pawnRow in pawnRows)
            {
                Team currentTeam = (pawnRow == 1) ? Team.White : Team.Black;
                int specialRow = (currentTeam == Team.White) ? 0 : GameBoard.GetUpperBound(0);

                var pawnPiece = new ChessPiece(PieceType.Pawn, new Coords(pawnRow, columnIndex), currentTeam, columnIndex);
                var specialPiece = new ChessPiece(specialPieceType, new Coords(specialRow, columnIndex), currentTeam, columnIndex);

                GameBoard[pawnRow, columnIndex] = pawnPiece;
                GameBoard[specialRow, columnIndex] = specialPiece;

                piecesPerTeam[currentTeam].TryAdd(pawnPiece.ID, pawnPiece);
                piecesPerTeam[currentTeam].TryAdd(specialPiece.ID, specialPiece);
            }
        }
        return piecesPerTeam;
    }

    /// <summary>
    /// Determines if king associated with <paramref name="teamToCheck"/> is checked on <paramref name="GameBoard"/>.
    /// </summary>
    /// <param name="teamToCheck">The team you want the checked status for.</param>
    /// <returns><see langword="true"/> if the King associated with <paramref name="teamToCheck"/> is checked; else <see langword="false"/>.</returns>
    public bool IsKingChecked(Team teamToCheck)
    {
        // This variable is needed to determine if a king can be attacked by an enemy pawn(pawns can only attack towards on side of the board.).
        ChessPiece queriedKing = ReturnKing(teamToCheck);
        int kingRow = queriedKing.CurrentRow;

        for (int verticalScalar = -1; verticalScalar < 2; ++verticalScalar)
        {
            for (int horizontalScalar = -1; horizontalScalar < 2; ++horizontalScalar)
            {   // Exclude the current space.
                if (verticalScalar == 0 && horizontalScalar == 0) continue;

                bool vectorIsPerpendicularOrParallel = Math.Abs(verticalScalar) + Math.Abs(horizontalScalar) == 1;
                var vectorDirection = new Vector2(horizontalScalar, verticalScalar);
                // Propagate the vector at most 7 times to get from the current space to the opposite side of the board.
                for (int propagationCount = 1; propagationCount < 8; ++propagationCount)
                {
                    var locationToCheck = Vector2.Add(queriedKing.CurrentLocation, Vector2.Multiply(vectorDirection, propagationCount));
                    (int row, int column) = ((int)locationToCheck.Y, (int)locationToCheck.X);

                    if ((row is >= 0 and <= 7) && (column is >= 0 and <= 7))
                    {
                        ChessPiece? piece = GameBoard[row, column];

                        if (piece is not null)
                        {   // If a ChessPiece object is found determine its type and whether or not it is friendly.
                            if (!queriedKing.OnSameTeam(piece))
                            {
                                PieceType pieceType = piece.AssignedType;
                                // Certain captures are only available to specific combinations of vector scalars and piece types.
                                bool enemyRookFound = pieceType == PieceType.Rook && vectorIsPerpendicularOrParallel;
                                bool enemyBishopFound = pieceType == PieceType.Bishop && !vectorIsPerpendicularOrParallel;
                                bool enemyPawnFound = pieceType == PieceType.Pawn && propagationCount == 1 && !vectorIsPerpendicularOrParallel
                                    && ((kingRow > row && piece.AssignedTeam == Team.White) || (kingRow < row && piece.AssignedTeam == Team.Black));
                                bool enemyQueenOrKingFound = (pieceType == PieceType.Queen) || (pieceType == PieceType.King && propagationCount == 1);

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

        foreach (Vector2 knightVector in ChessPiece.KnightDirectionVectors())
        {
            var locationToCheck = Vector2.Add(queriedKing.CurrentLocation, knightVector);
            (int row, int column) = ((int)locationToCheck.Y, (int)locationToCheck.X);

            if ((row is >= 0 and <= 7) && (column is >= 0 and <= 7))
            {
                ChessPiece? piece = GameBoard[row, column];

                if (piece is not null && piece.AssignedType == PieceType.Knight && !queriedKing.OnSameTeam(piece))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Determines if movement details provided by <paramref name="moveInfo"/> will expose the king associated with <paramref name ="teamToCheck"/> to check.
    /// </summary>
    /// <param name="moveInfo">Readonly struct with details on the movement to test.</param>
    /// <param name="teamToCheck">Used to determines which Team should be queried for a checked state.</param>
    /// <returns><see langword="true"/> if the king associated with <paramref name="teamToCheck"/> will be checked; otherwise, <see langword="false"/>.</returns>
    public bool WillChangeResultInCheck(MovementInformation moveInfo, Team teamToCheck)
    {
        EditGameBoard(moveInfo);
        bool returnValue = IsKingChecked(teamToCheck);
        UndoGameBoardEdit(moveInfo);

        return returnValue;
    }

    /// <summary>Determins if the king is checked and if so, determines if any move can undo the check.</summary>
    /// <returns><see langword="true"/> if the team associated with <paramref name="teamToCheck"/> is check-mated; otherwise, <see langword="false"/>.</returns>
    /// <param name ="teamToCheck">Used to determine which king should be queried for a check-mate state.</param>
    public bool IsKingCheckMated(Team teamToCheck)
    {
        // It isn't possible to be checkmated without being in check first.
        if (!IsKingChecked(teamToCheck)) return false;

        // Determine if there are any moves that can be done to prevent the current check.
        foreach (ChessPiece friendlyChessPiece in _chessPiecesByTeam[teamToCheck].Values.Where(x => !x.Captured))
        {
            foreach (var movement in AvailableMoves(friendlyChessPiece))
            {
                if (WillChangeResultInCheck(movement, teamToCheck) == false) return false;
            }
        }
        // Previous checks have failed. Return true.
        return true;
    }

    /// <summary>Determines if a stalemate has been reached for the current instance.</summary>
    /// <returns><see langword="true"/> if a stalemate has been reached; otherwise, <see langword="false"/>.</returns>
    public bool IsStalemate()
    {
        if (_movesSinceLastCapture >= 50) return true;
        // Count Pieces on Board
        int piecesRemaining = (from teams in _chessPiecesByTeam.Values
                               from piece in teams.Values
                               where piece.Captured == false
                               select piece).Count();

        if (piecesRemaining == 2) return true;

        var moveAvailableForActiveTeam = (from piece in _chessPiecesByTeam[ActiveTeam].Values
                                          where AvailableMoves(piece).Any()
                                          select true).Any();
        return !moveAvailableForActiveTeam;
    }

    /// <summary>
    /// Generates an array of Vectors represnting all moves for a given <paramref name ="queriedTeam"/>.
    /// </summary>
    /// <param name="queriedTeam"><see cref="Team"/> that has their available moves queried.</param>
    /// <returns>A dictionary of possible moves for chess pieces that are on <paramref name="queriedTeam"/>.</returns>
    public Dictionary<string, List<MovementInformation>> AllPossibleMovesPerTeam(Team queriedTeam)
    {
        var teamMoves = new Dictionary<string, List<MovementInformation>>();

        foreach (ChessPiece chessPiece in _chessPiecesByTeam[queriedTeam].Values.Where(x => !x.Captured))
        {
            teamMoves.Add(chessPiece.ID, AvailableMoves(chessPiece));
        }
        return teamMoves;
    }

    /// <summary>
    /// Replaces or moves a <see cref="ChessPiece"/> object within the <paramref name ="GameBoard"/> array.
    /// </summary>
    /// <param name ="move">Struct that contains details for a given movement or capture.</param>
    private void EditGameBoard(MovementInformation move)
    {
        // For simplicity the second piece is moved first.
        if (move.SecondaryCopy is not null && move.SecondaryNewLocation is not null)
        {
            ChessPiece secondaryOnBoard = GetPieceFromMovement(move, false);
            AdjustChessPieceLocationProperty(secondaryOnBoard, (Vector2)move.SecondaryNewLocation);
            if (move.CastlingWithSecondary) secondaryOnBoard.IncreaseMovementCount();
        }

        ChessPiece pieceToChange = GetPieceFromMovement(move, true);
        AdjustChessPieceLocationProperty(pieceToChange, move.MainNewLocation);

        if (move.EnPassantCapturePossible) pieceToChange.EnableEnPassantCaptures();
        else if (move.NewType is not null) pieceToChange.ChangePieceType((PieceType)move.NewType);
        pieceToChange.IncreaseMovementCount();
    }

    /// <summary>
    /// Undoes a change to a <see cref="ChessPiece"/> array using information from <paramref name="movementToUndo"/>.
    /// </summary>
    /// <param name="movementToUndo">Struct that contains information on the change to undo.</param>
    private void UndoGameBoardEdit(MovementInformation movementToUndo)
    {
        ChessPiece mainChessPiece = GetPieceFromMovement(movementToUndo, true);
        AdjustChessPieceLocationProperty(mainChessPiece, movementToUndo.MainCopy.CurrentLocation);

        if (movementToUndo.EnPassantCapturePossible) mainChessPiece.DisableEnPassantCaptures();
        if (movementToUndo.NewType is not null) mainChessPiece.ChangePieceType(movementToUndo.MainCopy.AssignedType);
        mainChessPiece.DecreaseMovementCount();

        if (movementToUndo.SecondaryCopy is not null)
        {
            ChessPiece pieceTwo = GetPieceFromMovement(movementToUndo, false);
            AdjustChessPieceLocationProperty(pieceTwo, movementToUndo.SecondaryCopy.CurrentLocation);
            if (movementToUndo.CastlingWithSecondary) pieceTwo.DecreaseMovementCount();
        }
    }

    /// <summary>
    /// Moves <paramref name="pieceToMove"/> on <see cref="GameBoard"/> and changes its 
    /// <see cref="ChessPiece.CurrentLocation"/> property to <paramref name="newLocation"/>. 
    /// </summary>
    /// <remarks>Method can handle undoing a movement as well.</remarks>
    /// <param name="pieceToMove">ChessPiece instance that will have its location changed.</param>
    /// <param name="newLocation">Vector2 instance of where <paramref name="pieceToMove"/> will be placed.</param>
    private void AdjustChessPieceLocationProperty(ChessPiece pieceToMove, Vector2 newLocation)
    {
        if (!pieceToMove.Captured) GameBoard[pieceToMove.CurrentRow, pieceToMove.CurrentColumn] = null;
        // If pieceToMove isn't being captured, move its current location within GameBoard.
        if (!newLocation.Equals(ChessPiece.s_capturedLocation))
        {
            (int row, int column) = ((int)newLocation.Y, (int)newLocation.X);

            if (GameBoard[row, column] is null)
            {
                GameBoard[row, column] = pieceToMove;
            }
            else
            {
                throw new InvalidOperationException($"GameBoard[{row},{column}] must be null before replacing its value.");
            }
        }
        pieceToMove.CurrentLocation = newLocation;
    }
    /// <summary>
    /// This method is used to submit finalized changes to <see cref="GameBoard"/> and exchanges <see cref="ActiveTeam"/>
    /// with the opposite <see cref="Team"/>.
    /// </summary>
    /// <param name="newMove"><see cref="MovementInformation"/> to submit to <see cref="GameBoard"/>.</param>
    // /// <exception cref="InvalidOperationException">If an invalid move is submitted or a move on the wrong turn is detected.</exception>
    public bool SubmitFinalizedChange(MovementInformation newMove, bool piecesAlreadyMovedOnGUI)
    {
        bool success = false;
        if (newMove.SubmittingTeam == ActiveTeam && !GameEnded)
        {
            bool localPlayerMove = ActiveTeam.Equals(PlayerTeam);
            var hostileMoveCheck = from possibleMove in AvailableMoves(GetPieceFromMovement(newMove, true))
                                   where possibleMove.MainCopy.ID.Equals(newMove.MainCopy.ID) && possibleMove.MainNewLocation.Equals(newMove.MainNewLocation)
                                   select true;

            if (localPlayerMove || hostileMoveCheck.Any())
            {
                _gameMoves.Add(newMove);
                if (!piecesAlreadyMovedOnGUI) UpdateSquaresOnGUI(newMove, piecesAlreadyMovedOnGUI);

                EditGameBoard(newMove);

                if (newMove.CapturingSecondary) _movesSinceLastCapture = 0;
                else ++_movesSinceLastCapture;

                // The local player isn't allowed to submit a move that will place themselves in check, So just determine if the opponent
                // has placed the local player in check.

                Team newActiveTeam = ActiveTeam = ReturnOppositeTeam(ActiveTeam);
                DisableTeamVulnerabilityToEnPassant(newActiveTeam);
                if (IsStalemate())
                {
                    ChangeGameState(GameState.GameDraw);
                }
                else if (!localPlayerMove && IsKingCheckMated(PlayerTeam))
                {
                    ChangeGameState(GameState.LocalLoss);
                }
                else if (localPlayerMove && IsKingCheckMated(ReturnOppositeTeam(PlayerTeam)))
                {
                    ChangeGameState(GameState.LocalWin);
                }
                success = true;
            }
            else
            {
                throw new InvalidOperationException("Opponent has submitted an invalid move.");
            }
        }
        else
        {
            throw new InvalidOperationException("The wrong team has submitted a move.");
        }
        return success;
    }

    /// <summary>Disables vulnerability to En Passant for the current <paramref name="activeTeam"/>.</summary>
    /// <param name="activeTeam">The Team that will have its Pawns be no longer captureable via En Passant.</param>
    private void DisableTeamVulnerabilityToEnPassant(Team activeTeam)
    {
        foreach (ChessPiece chessPiece in _chessPiecesByTeam[activeTeam].Values.Where(x => x.CanBeCapturedViaEnPassant))
        {
            chessPiece.DisableEnPassantCaptures();
        }
    }

    /// <summary>
    /// Updates a client-side <see cref="GameEnvironment"/> instance and corresponding visuals using <paramref name="newMove"/>.
    /// </summary>
    /// <param name="newMove">Board movement used to update a <see cref="GameEnvironment"/> instance.</param>
    /// <param name="piecesAlreadyMovedOnGUI">If <see langword="true"/>, then the GUI has already been updated with <paramref name="newMove"/>.</param>
    private void UpdateSquaresOnGUI(MovementInformation newMove, bool piecesAlreadyMovedOnGUI)
    {
        if (GameBoard is not null && Squares is not null && ActiveTeam == newMove.SubmittingTeam && !piecesAlreadyMovedOnGUI)
        {
            // Updates Graphics
            // It is important to move the secondary piece first if available.
            if (newMove.SecondaryCopy is not null)
            {
                ChessPiece secPiece = newMove.SecondaryCopy;
                // Interface with the board using coordinates rather than the object.
                PictureBox? secBox = Squares[secPiece.CurrentRow, secPiece.CurrentColumn];

                if (secBox is not null)
                {
                    if (newMove.CapturingSecondary)
                    {   // Setting equal to null allows the image to be replaced even if this is an en passant capture.
                        secBox.Image = null;
                    }
                    else if (newMove.CastlingWithSecondary && newMove.NewSecondaryCoords is not null)
                    {
                        Squares[(int)newMove.NewSecondaryCoords?.RowIndex!, (int)newMove.NewSecondaryCoords?.ColumnIndex!]!.Image = secBox.Image;
                        secBox.Image = null;
                    }
                }
            }

            ChessPiece mainPiece = newMove.MainCopy;
            PictureBox? mainBox = Squares[mainPiece.CurrentRow, mainPiece.CurrentColumn];

            if (mainBox is not null)
            {
                Squares[newMove.NewMainCoords.RowIndex, newMove.NewMainCoords.ColumnIndex]!.Image = mainBox.Image;
                mainBox.Image = null;
            }
        }
    }
    /// <summary>
    /// Determines the available moves for <paramref name="piece"/> and returns the result.
    /// </summary>
    /// <returns>A <see cref="List{MovementInformation}"/> of available movements/attacks for <paramref name="piece"/>.</returns>
    /// <param name="piece"><see cref="ChessPiece"/> instance to determine possible moves for.</param>
    public List<MovementInformation> AvailableMoves(ChessPiece piece)
    {
        var viableMoves = new List<MovementInformation>();
        if (piece.Captured)
        {
            return viableMoves;
        }
        bool targetSquareIsEmpty;
        ChessPiece copyOfPiece = piece.Copy();

        foreach (Vector2 movementVector in piece.DirectionVectors)
        {
            // If castling vector, determine if it is possible to castle.
            if (piece.IsKing() && Math.Abs(movementVector.X) == 2)
            {
                if (piece.TimesMoved != 0 || IsKingChecked(piece.AssignedTeam)) continue;
                // A castleDirection of -1 means it's towards the left.
                int castleDirection = (int)movementVector.X / 2;
                int friendlySpecialLine = piece.CurrentRow;

                int rookColumn = castleDirection == -1 ? 0 : GameBoard.GetUpperBound(1);

                ChessPiece? pairedRook = GameBoard[friendlySpecialLine, rookColumn];

                // Ensure that the king hasn't been moved and isn't already in check.
                if (pairedRook is not null && pairedRook.AssignedType == PieceType.Rook && pairedRook.TimesMoved == 0)
                {   // Now check to make sure that all spaces between the king and that rook are clear.
                    int lesserColumnIndex = Math.Min(rookColumn, piece.CurrentColumn);
                    int greaterColumnIndex = Math.Max(rookColumn, piece.CurrentColumn);
                    bool castlePathIsClear = true;
                    // Ensure that squares between the King and Rook are empty.
                    for (int columnIndex = lesserColumnIndex + 1; columnIndex < greaterColumnIndex; columnIndex++)
                    {
                        if (GameBoard[friendlySpecialLine, columnIndex] is not null)
                        {
                            castlePathIsClear = false;
                            break;
                        }
                    }

                    if (castlePathIsClear)
                    {
                        bool cannotCastleInThisDirection = false;
                        var singleSquareMovement = new Vector2(castleDirection, 0);
                        // Initialize movement at the current location for addition purposes in the following loop.
                        Vector2 movement = piece.CurrentLocation;
                        // Ensure that the King will not be moving into or through check.
                        for (int i = 0; i < 2; ++i)
                        {
                            movement = Vector2.Add(movement, singleSquareMovement);

                            var singleSquareMovementInfo = new MovementInformation(copyOfPiece, null, new Coords(movement),
                                                                null, false,
                                                                capturingSecondary: false, castlingWithSecondary: false, newType: null);

                            if (WillChangeResultInCheck(singleSquareMovementInfo, piece.AssignedTeam))
                            {
                                cannotCastleInThisDirection = true;
                                break;
                            }
                        }

                        if (!cannotCastleInThisDirection)
                        {
                            var newKingLocation = Vector2.Add(piece.CurrentLocation, movementVector);
                            var newRookLocation = Vector2.Add(piece.CurrentLocation, new Vector2(castleDirection, 0));

                            var moveInfo = new MovementInformation(copyOfPiece, pairedRook.Copy(), new Coords(newKingLocation), new Coords(newRookLocation),
                                enPassantCapturePossible: false, capturingSecondary: false,
                                castlingWithSecondary: true, newType: null);

                            viableMoves.Add(moveInfo);
                        }
                    }
                }
            }
            else
            {
                // This loop serves the purpose of calculating possible movements for pieces that can move multiple squares in a given direction.
                // Loop at most 7 times to account for any piece moving across the board.
                // Start at 1 for multiplication purposes.
                for (int i = 1; i < 8; ++i)
                {
                    var calculatedPosition = Vector2.Add(piece.CurrentLocation, Vector2.Multiply(movementVector, i));
                    (int row, int column) = ((int)calculatedPosition.Y, (int)calculatedPosition.X);
                    // If coordinate isn't out of bounds on the chess board then propagate the vector.
                    if (row is >= 0 and <= 7 && column is >= 0 and <= 7)
                    {
                        targetSquareIsEmpty = GameBoard[row, column] is null;
                        bool canCaptureEnemy = piece.IsComparedPieceHostile(GameBoard[row, column], out ChessPiece? captureablePiece);
                        bool pawnAttackVector, movementWillExposeToEnPassant = pawnAttackVector = false;

                        if (piece.AssignedType == PieceType.Pawn)
                        {
                            // Disable the starting move of moving 2 spaces forward if the pawn has already been moved.
                            if (Math.Abs(movementVector.Y) == 2)
                            {
                                int initialRowJumped = piece.AssignedTeam == Team.White ? 2 : 5;
                                if (piece.TimesMoved != 0 || GameBoard[initialRowJumped, piece.CurrentColumn] is not null) break;
                                movementWillExposeToEnPassant = true;
                            }

                            if (movementVector.X == 0)
                            {   // If there is no horizontal movement, disable attacking;
                                canCaptureEnemy = false;
                                #region  Adding pawn promotion moves if conditions are met.
                                if (row == (piece.AssignedTeam.Equals(Team.White) ? 7 : 0) && targetSquareIsEmpty)
                                {
                                    foreach (PieceType pieceType in Enum.GetValues(typeof(PieceType)))
                                    {
                                        var pawnPromotionMove = new MovementInformation(copyOfPiece, null, new Coords(calculatedPosition), null, false, false, false, pieceType);

                                        if (!pieceType.Equals(PieceType.King) && !pieceType.Equals(PieceType.Pawn) && !WillChangeResultInCheck(pawnPromotionMove, piece.AssignedTeam))
                                        {
                                            viableMoves.Add(pawnPromotionMove);
                                        }
                                    }
                                    // Ignore just moving and remaining a pawn.
                                    continue;
                                }
                                #endregion
                            }
                            else
                            {
                                pawnAttackVector = true;
                                #region Check for possible En Passant captures.
                                if (piece.IsComparedPieceHostile(GameBoard[piece.CurrentRow, column], out ChessPiece? captureablePawn))
                                {
                                    if (captureablePawn is not null && captureablePawn.AssignedType.Equals(PieceType.Pawn)
                                    && captureablePawn.CanBeCapturedViaEnPassant)
                                    {
                                        var enPassantCapture = new MovementInformation(copyOfPiece, captureablePawn.Copy(), new Coords(calculatedPosition),
                                            new Coords(ChessPiece.s_capturedLocation), movementWillExposeToEnPassant, capturingSecondary: true,
                                            castlingWithSecondary: false, newType: null);

                                        if (false == WillChangeResultInCheck(enPassantCapture, piece.AssignedTeam))
                                        {
                                            viableMoves.Add(enPassantCapture);
                                        }
                                    }
                                }
                                #endregion
                            }
                        }
                        // Special notes for pawns: if pawnAttackVector = true, then the only way to move to that space is if canCaptureEnemy == true.
                        if ((targetSquareIsEmpty && !pawnAttackVector) || canCaptureEnemy)
                        {
                            var moveInfo = new MovementInformation(copyOfPiece, captureablePiece?.Copy(), new Coords(calculatedPosition),
                                canCaptureEnemy ? new Coords(ChessPiece.s_capturedLocation) : null, movementWillExposeToEnPassant,
                                capturingSecondary: canCaptureEnemy, castlingWithSecondary: false, newType: null);

                            if (!WillChangeResultInCheck(moveInfo, piece.AssignedTeam))
                            {
                                viableMoves.Add(moveInfo);
                            }
                        }
                        if (!targetSquareIsEmpty || !piece.CanMoveAcrossBoard) break;
                    }
                    else
                    {   // Further multiplication of the vector will result in out of bounds values.
                        break;
                    }
                }
            }
        }
        return viableMoves;
    }

    /// <summary>Changes the <see cref="MatchState"/> property to the input parameter <paramref name="newState"/> and ends the game.</summary>
    /// <param name="newState">New game state for the current instance.</param>
    public void ChangeGameState(GameState newState)
    {
        MatchState = newState;
    }
    /// <summary>Returns a chess piece within the current instance based on the ID property of a ChessPiece found in <paramref name="move"/> parameter.</summary>
    /// <param name="move">Data used to help retrieve a chess piece.</param>
    /// <param name="wantPrimary"><see langword="true"/> if you want the MainCopy; otherwise, the secondary piece will be retrieved.</param>
    /// <returns>A chess piece instance.</returns>
    /// <exception cref="KeyNotFoundException"></exception>
    private ChessPiece GetPieceFromMovement(MovementInformation move, bool wantPrimary)
    {
        ChessPiece pieceFromMove;
        try
        {
            Team wantedTeam = (wantPrimary || move.CastlingWithSecondary) ? move.SubmittingTeam : ReturnOppositeTeam(move.SubmittingTeam);
            string pieceID = wantPrimary ? move.MainCopy.ID : move.SecondaryCopy!.ID;
            pieceFromMove = _chessPiecesByTeam[wantedTeam][pieceID];
        }
        catch (KeyNotFoundException e)
        {
            Console.WriteLine(e);
            throw;
        }
        return pieceFromMove;
    }

    /// <summary>Returns the opposite <see cref="Team"/>.</summary>
    /// <param name="value"></param>
    /// <returns><see cref="Team"/> opposite of the <paramref name="value"/> parameter.</returns>
    public static Team ReturnOppositeTeam(Team value) => value == Team.Black ? Team.White : Team.Black;

    /// <summary>A friendly King from the current board.</summary>
    /// <param name="teamColor">Enum to determine which team to return a king for.</param>
    /// <returns>A friendly <see cref="ChessPiece"/> object with a <see cref="ChessPiece._pieceType"/> property of <see cref="PieceType.King"/>.</returns>
    public ChessPiece ReturnKing(Team teamColor) => teamColor == Team.White ? _whiteKing : _blackKing;
}
