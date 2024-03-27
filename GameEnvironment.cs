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

    /// <summary>
    /// Boolean used to signify if a temporary move has been pushed to <see cref="_gameMoves"/>.
    /// </summary>
    private bool _tempMoveSaved = false;

    /// <summary>Tracks moves within the current <see cref="GameEnvironment"/> instance.</summary>
    private readonly Stack<MovementInformation> _gameMoves = new();

    /// <summary>Stores how many moves have been submitted since the last capture was made.</summary>
    /// <remarks>If over 50 then a Draw is determined.</remarks>
    private int _movesSinceLastCapture = 0;

    /// <summary>Array used to hold <see cref="ChessPiece"/> instances and their locations withn the current game.</summary>
    public readonly ChessPiece?[,] GameBoard;

    /// <summary>Gets or sets an array used to store visual information about the current state of <see cref="GameBoard"/>.</summary>
    /// <remarks>Assigned a value when an instance of <see cref="Chess_GUi.BoardGUI"/> is generated.</remarks>
    public PictureBox[,]? Squares { get; set; }

    /// <summary>Dictionary of <see cref="ChessPiece"/> objects keyed to a <see cref="Team"/> enum.</summary>
    private readonly Dictionary<Team, Dictionary<string, ChessPiece>> _chessPieceByIdByTeam;

    /// <summary>Gets or initializes an ID number used to track the current instance on the server.</summary>
    /// <value>The ID of the current <see cref="GameEnvironment"/> instance on the server.</value>
    public int GameID { get; }

    /// <summary>Gets or initializes a <see cref="Team"/> enum that represents the assigned Team of the client.</summary>
    /// <remarks>Variable used by the client-side <see cref="BoardGUI"/> instance to limit interaction with the board until it is that team's turn.</remarks>
    /// <value><see cref="Team"/> assigned to the local user.</value>
    public Team PlayerTeam { get; }

    /// <summary>Gets or sets a value representing which <see cref="Team"/> is currently allowed to submit a <see cref="MovementInformation"/> instance.</summary>
    /// <remarks>Alternated whenever <see cref="SubmitFinalizedChange(MovementInformation)"/> is called.</remarks>
    ///<value>The <see cref="Team"/> that is currently allowed to submit <see cref="MovementInformation"/> to the <see cref="GameEnvironment"/> instance.</value>
    public Team ActiveTeam { get; private set; } = Team.White;

    /// <summary>Gets or sets the current game state.</summary>
    /// <value>The current game state.</value>
    public GameState MatchState { get; private set; } = GameState.Playing;

    /// <summary>Gets a boolean that denotes whether or not a given instance has ended.</summary>
    /// <value><see langword="true"/> if the <see cref="GameEnvironment"/> instance has ended; otherwise, <see langword="false"/>.</value>
    public bool GameEnded { get => MatchState != GameState.Playing; }
    /// <summary>
    /// Initializes a new instance of the <see cref="GameEnvironment"/> instance.
    /// </summary>
    /// <param name="playerTeam"><see cref="Team"/> assigned to client-side <see cref="GameEnvironment"/> instances.</param>
    /// <param name="serverSideID">Sever generated id assigned by server to identify this instance.</param>
    public GameEnvironment(int serverSideID, Team playerTeam)
    {
        GameID = serverSideID;
        PlayerTeam = playerTeam;
        GameBoard = new ChessPiece[8, 8];

        _chessPieceByIdByTeam = GenerateBoard();
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
    private Dictionary<Team, Dictionary<string, ChessPiece>> GenerateBoard()
    {
        var piecesPerTeam = new Dictionary<Team, Dictionary<string, ChessPiece>>
        {
            { Team.White, new Dictionary<string, ChessPiece>() },
            { Team.Black, new Dictionary<string, ChessPiece>() }
        };

        int indexOfLastRow = GameBoard.GetUpperBound(0);

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

            foreach (var selectedTeam in new Team[] { Team.White, Team.Black })
            {
                (int pawnRow, int specialRow) = (selectedTeam == Team.White) ? (1, 0) : (indexOfLastRow - 1, indexOfLastRow);

                var pawnPiece = new ChessPiece(PieceType.Pawn, new Coords(pawnRow, columnIndex), selectedTeam, columnIndex);
                var specialPiece = new ChessPiece(specialPieceType, new Coords(specialRow, columnIndex), selectedTeam, columnIndex);

                GameBoard[pawnRow, columnIndex] = pawnPiece;
                GameBoard[specialRow, columnIndex] = specialPiece;

                piecesPerTeam[selectedTeam].TryAdd(pawnPiece.ID, pawnPiece);
                piecesPerTeam[selectedTeam].TryAdd(specialPiece.ID, specialPiece);
            }
        }
        return piecesPerTeam;
    }

    /// <summary>
    /// Determines if king associated with <paramref name="teamToCheck"/> is in check.
    /// </summary>
    /// <param name="teamToCheck">The team for which the checked status is returned.</param>
    /// <returns><see langword="true"/> if the King associated with <paramref name="teamToCheck"/> is checked; otherwise, <see langword="false"/>.</returns>
    private bool IsTeamInCheck(Team teamToCheck)
    {
        ChessPiece queriedKing = ReturnKing(teamToCheck);
        // This variable is needed to determine if a king can be attacked by an enemy pawn(pawns can only attack towards one side of the board).
        int kingRowNumber = queriedKing.CurrentRow;

        for (int verticalScalar = -1; verticalScalar < 2; ++verticalScalar)
        {
            for (int horizontalScalar = -1; horizontalScalar < 2; ++horizontalScalar)
            {
                // Exclude the current space.
                if (verticalScalar == 0 && horizontalScalar == 0) continue;

                bool vectorIsDiagonal = Math.Abs(verticalScalar) + Math.Abs(horizontalScalar) == 2;
                var vectorDirection = new Vector2(horizontalScalar, verticalScalar);
                // Propagate the vector at most 7 times to get from the current space to the opposite side of the board.
                for (int propagationCount = 1; propagationCount < 8; ++propagationCount)
                {
                    var locationToCheck = Vector2.Add(queriedKing.CurrentLocation, Vector2.Multiply(vectorDirection, propagationCount));
                    (int queriedRow, int queriedColumn) = ((int)locationToCheck.Y, (int)locationToCheck.X);

                    if ((queriedRow is >= 0 and <= 7) && (queriedColumn is >= 0 and <= 7))
                    {
                        ChessPiece? piece = GameBoard[queriedRow, queriedColumn];

                        if (piece is not null)
                        {
                            if (!queriedKing.OnSameTeam(piece))
                            {
                                PieceType pieceType = piece.AssignedType;
                                // Certain captures are only available to specific combinations of vector scalars and piece types.
                                bool enemyRookFound = pieceType == PieceType.Rook && !vectorIsDiagonal;
                                bool enemyBishopFound = pieceType == PieceType.Bishop && vectorIsDiagonal;
                                bool enemyQueenOrKingFound = (pieceType == PieceType.Queen) || (pieceType == PieceType.King && propagationCount == 1);
                                bool enemyPawnFound = propagationCount == 1 && vectorIsDiagonal && pieceType == PieceType.Pawn
                                    && ((kingRowNumber > queriedRow && piece.AssignedTeam == Team.White) || (kingRowNumber < queriedRow && piece.AssignedTeam == Team.Black));

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
            (int queriedRow, int queriedColumn) = ((int)locationToCheck.Y, (int)locationToCheck.X);

            if ((queriedRow is >= 0 and <= 7) && (queriedColumn is >= 0 and <= 7))
            {
                ChessPiece? piece = GameBoard[queriedRow, queriedColumn];

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
    private bool WillChangeResultInCheck(MovementInformation moveInfo, Team teamToCheck)
    {
        EditGameBoard(moveInfo, false);
        bool returnValue = IsTeamInCheck(teamToCheck);
        UndoGameBoardEdit();

        return returnValue;
    }

    /// <summary>Determins if the king is checked and if so, determines if any move can undo the check.</summary>
    /// <returns><see langword="true"/> if the team associated with <paramref name="teamToCheck"/> is check-mated; otherwise, <see langword="false"/>.</returns>
    /// <param name ="teamToCheck">Used to determine which king should be queried for a check-mate state.</param>
    private bool IsTeamCheckmated(Team teamToCheck)
    {
        // It isn't possible to be checkmated without being in check first.
        if (!IsTeamInCheck(teamToCheck)) return false;
        // Determine if there are any moves that can be done to prevent the current check.
        bool moveThatDeniesCheckUnavailable = !(from piece in _chessPieceByIdByTeam[teamToCheck].Values
                                                where !piece.Captured && AvailableMoves(piece).Count > 0
                                                select true).Any();
        return moveThatDeniesCheckUnavailable;
    }

    /// <summary>Determines if a stalemate has been reached for the current instance.</summary>
    /// <returns><see langword="true"/> if a stalemate has been reached; otherwise, <see langword="false"/>.</returns>
    private bool IsStalemate()
    {
        if (_movesSinceLastCapture >= 50) return true;
        // Count Pieces on Board
        int piecesRemaining = (from teams in _chessPieceByIdByTeam.Values
                               from piece in teams.Values
                               where piece.Captured == false
                               select piece).Count();

        if (piecesRemaining == 2) return true;

        var moveAvailableForActiveTeam = (from piece in _chessPieceByIdByTeam[ActiveTeam].Values
                                          where !piece.Captured && AvailableMoves(piece).Count > 0
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

        foreach (ChessPiece chessPiece in _chessPieceByIdByTeam[queriedTeam].Values.Where(x => !x.Captured))
        {
            teamMoves.Add(chessPiece.ID, AvailableMoves(chessPiece));
        }
        return teamMoves;
    }

    /// <summary>
    /// Replaces or moves a <see cref="ChessPiece"/> object within the <see cref="GameBoard"/> array.
    /// </summary>
    /// <param name ="move">Contains details for a given move.</param>
    /// <param name="moveIsFinal"><see cref="true"/> if <paramref name="move"/> being submitted as the final move for the current turn.</param>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="_tempMoveSaved"/> is <see langword="true"/>.</exception>
    private void EditGameBoard(MovementInformation move, bool moveIsFinal)
    {
        if (_tempMoveSaved) throw new InvalidOperationException("There is a temporary move reflected in the current state of the board. Undo it before proceeding.");
        // For simplicity the second piece is moved first.
        if (move.SecondaryCopy is not null && move.SecondaryNewLocation is not null)
        {
            ChessPiece secondaryOnBoard = GetPieceFromMovement(move, false);
            if (move.CastlingWithSecondary) secondaryOnBoard.IncreaseMovementCount();
            if (move.CapturingSecondary) secondaryOnBoard.Captured = true;
            AdjustChessPieceLocationProperty(secondaryOnBoard, (Vector2)move.SecondaryNewLocation);
        }

        ChessPiece pieceToChange = GetPieceFromMovement(move, true);
        if (move.EnPassantCapturePossible) pieceToChange.EnableEnPassantCaptures();
        else if (move.NewType is not null) pieceToChange.ChangePieceType((PieceType)move.NewType);
        pieceToChange.IncreaseMovementCount();
        AdjustChessPieceLocationProperty(pieceToChange, move.MainNewLocation);

        _gameMoves.Push(move);
        _tempMoveSaved = !moveIsFinal;
    }

    /// <summary>
    /// Undoes the most recent move in the game.
    /// </summary>
    private void UndoGameBoardEdit()
    {
        MovementInformation movementToUndo = _gameMoves.Pop();
        ChessPiece mainChessPiece = GetPieceFromMovement(movementToUndo, true);

        if (movementToUndo.EnPassantCapturePossible) mainChessPiece.DisableEnPassantCaptures();
        if (movementToUndo.NewType is not null) mainChessPiece.ChangePieceType(movementToUndo.MainCopy.AssignedType);
        mainChessPiece.DecreaseMovementCount();
        AdjustChessPieceLocationProperty(mainChessPiece, movementToUndo.MainCopy.CurrentLocation);

        if (movementToUndo.SecondaryCopy is not null)
        {
            ChessPiece pieceTwo = GetPieceFromMovement(movementToUndo, false);
            if (movementToUndo.CastlingWithSecondary) pieceTwo.DecreaseMovementCount();
            if (movementToUndo.CapturingSecondary) pieceTwo.Captured = false;
            AdjustChessPieceLocationProperty(pieceTwo, movementToUndo.SecondaryCopy.CurrentLocation);
        }
        _tempMoveSaved = false;
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
        GameBoard[pieceToMove.CurrentRow, pieceToMove.CurrentColumn] = null;
        // If pieceToMove isn't being captured, move its current location within GameBoard.
        if (!pieceToMove.Captured)
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
            pieceToMove.CurrentLocation = newLocation;
        }
    }
    /// <summary>
    /// This method is used to submit finalized changes to <see cref="GameBoard"/> and exchanges <see cref="ActiveTeam"/>
    /// with the opposite <see cref="Team"/>.
    /// </summary>
    /// <returns><see langword="true"/> if <paramref name="newMove"/> was successfully submitted; otherwise, <see langword="false"/>.</returns>
    /// <param name="newMove"><see cref="MovementInformation"/> to submit to <see cref="GameBoard"/>.</param>
    /// <param name="piecesAlreadyMovedOnGUI">true if chesspieces on the GUI have already been updated; otherwise, false.</param>
    public bool SubmitFinalizedChange(MovementInformation newMove)
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
                EditGameBoard(newMove, true);

                if (newMove.CapturingSecondary) _movesSinceLastCapture = 0;
                else ++_movesSinceLastCapture;

                Team newActiveTeam = ActiveTeam = ReturnOppositeTeam(ActiveTeam);
                DisableTeamVulnerabilityToEnPassant(newActiveTeam);
                // The local player isn't allowed to submit a move that will place themselves in check, So just determine if the opponent
                // has placed the local player in check.
                if (!localPlayerMove && IsTeamCheckmated(PlayerTeam))
                {
                    ChangeGameState(GameState.LocalLoss);
                }
                else if (localPlayerMove && IsTeamCheckmated(ReturnOppositeTeam(PlayerTeam)))
                {
                    ChangeGameState(GameState.LocalWin);
                }
                else if (IsStalemate())
                {
                    ChangeGameState(GameState.GameDraw);
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
    /// <param name="activeTeam">Team that will have its Pawns be no longer captureable via En Passant.</param>
    private void DisableTeamVulnerabilityToEnPassant(Team activeTeam)
    {
        foreach (ChessPiece chessPiece in _chessPieceByIdByTeam[activeTeam].Values.Where(x => x.CanBeCapturedViaEnPassant))
        {
            chessPiece.DisableEnPassantCaptures();
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
        ChessPiece copyOfPiece = piece.Copy();

        foreach (Vector2 movementVector in piece.DirectionVectors)
        {
            // If castling vector, determine if it is possible to castle.
            if (piece.IsKing() && Math.Abs(movementVector.X) == 2)
            {
                if (piece.TimesMoved != 0 || IsTeamInCheck(piece.AssignedTeam)) continue;
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
                            var singleSquareMovementInfo = new MovementInformation(copyOfPiece, new Coords(movement));

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

                            var moveInfo = new MovementInformation(copyOfPiece, new Coords(newKingLocation), pairedRook.Copy(), new Coords(newRookLocation), castlingWithSecondary: true);
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
                        bool targetSquareIsEmpty = GameBoard[row, column] is null;
                        bool canCaptureEnemy = piece.TryGetHostileChessPiece(GameBoard[row, column], out ChessPiece? captureablePiece);
                        bool pawnAttackVector, promptUserForPawnPromotion, movementWillExposeToEnPassant = promptUserForPawnPromotion = pawnAttackVector = false;

                        if (piece.AssignedType == PieceType.Pawn)
                        {
                            if (movementVector.X == 0)
                            {
                                // If there is no horizontal movement, disable attacking;
                                canCaptureEnemy = false;
                                // Disable the starting move of moving 2 spaces forward if the pawn has already been moved.
                                if (Math.Abs(movementVector.Y) == 2)
                                {
                                    int initialRowJumped = piece.AssignedTeam == Team.White ? 2 : 5;
                                    if (piece.TimesMoved != 0 || GameBoard[initialRowJumped, piece.CurrentColumn] is not null) break;
                                    movementWillExposeToEnPassant = true;
                                }
                                else if (row == (piece.AssignedTeam.Equals(Team.White) ? 7 : 0) && targetSquareIsEmpty)
                                {
                                    promptUserForPawnPromotion = true;
                                }
                            }
                            else
                            {
                                pawnAttackVector = true;
                                #region Check for possible En Passant captures.
                                if (piece.TimesMoved >= 2 && piece.TryGetHostileChessPiece(GameBoard[piece.CurrentRow, column], out ChessPiece? captureablePawn))
                                {
                                    if (captureablePawn is not null && captureablePawn.AssignedType.Equals(PieceType.Pawn)
                                    && captureablePawn.CanBeCapturedViaEnPassant)
                                    {
                                        var enPassantCapture = new MovementInformation(copyOfPiece, new Coords(calculatedPosition), captureablePawn.Copy(),
                                            new Coords(ChessPiece.s_capturedLocation), capturingSecondary: true);

                                        if (!WillChangeResultInCheck(enPassantCapture, piece.AssignedTeam))
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
                            var moveInfo = new MovementInformation(copyOfPiece, new Coords(calculatedPosition), captureablePiece?.Copy(),
                                canCaptureEnemy ? new Coords(ChessPiece.s_capturedLocation) : null, movementWillExposeToEnPassant,
                                capturingSecondary: canCaptureEnemy, promotingPawn: promptUserForPawnPromotion);

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
    /// <returns>A <see cref="ChessPiece"/> instance.</returns>
    /// <exception cref="KeyNotFoundException"></exception>
    /// <exception cref="NullReferenceException"></exception>
    private ChessPiece GetPieceFromMovement(MovementInformation move, bool wantPrimary)
    {
        Team wantedTeam = (wantPrimary || move.CastlingWithSecondary) ? move.SubmittingTeam : ReturnOppositeTeam(move.SubmittingTeam);
        string pieceID = wantPrimary ? move.MainCopy.ID : move.SecondaryCopy!.ID;
        ChessPiece pieceFromMove = _chessPieceByIdByTeam[wantedTeam][pieceID];

        return pieceFromMove;
    }

    /// <summary>Returns the opposite <see cref="Team"/>.</summary>
    /// <param name="value"></param>
    /// <returns><see cref="Team"/> opposite of the <paramref name="value"/> parameter.</returns>
    public static Team ReturnOppositeTeam(Team value) => value == Team.Black ? Team.White : Team.Black;

    /// <summary>A friendly King from the current board.</summary>
    /// <param name="teamColor">Enum to determine which team to return a king for.</param>
    /// <returns>A friendly <see cref="ChessPiece"/> object with a <see cref="ChessPiece._pieceType"/> property of <see cref="PieceType.King"/>.</returns>
    private ChessPiece ReturnKing(Team teamColor) => teamColor == Team.White ? _whiteKing : _blackKing;
}
