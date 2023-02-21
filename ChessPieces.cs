using System.Numerics;

namespace Pieces;

public enum PieceType
{
    Pawn, Rook, Bishop, Knight, King, Queen
}
public enum Team
{
    White, Black
}
public struct Coords
{
    public Coords(int rowIndex, int columnIndex)
    {
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
    }
    public Coords(Vector2 vector)
    {
        RowIndex = (int)vector.X;
        ColumnIndex = (int)vector.Y;
    }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
}

public readonly struct MovementInformation
{
    public MovementInformation(ChessPiece mainPiece, ChessPiece? secondaryPiece, Coords mainCoords, Coords secondaryCoords, bool enPassantCapturePossible, bool capturingSecondary, bool castlingWithSecondary)
    {
        SecondaryPiece = secondaryPiece?.Copy();
        MainPiece = mainPiece.Copy();
        MainCoords = mainCoords;
        SecondaryCoords = secondaryCoords;
        EnPassantCapturePossible = enPassantCapturePossible;
        CapturingSecondary = capturingSecondary;
        CastlingWithSecondary = castlingWithSecondary;
    }

    public Coords MainCoords { get;init; }
    public Coords SecondaryCoords { get; init; }
    /// <summary>Primary ChessPiece that is moved.</summary>
    public ChessPiece MainPiece { get; init; }

    /// <summary>Nullable property that refrences any secondary chess pieces involved in the given movement.
    /// For example: A rook being castled with or an opponent chess piece that's being captured.</summary>
    public ChessPiece? SecondaryPiece { get; init; }

    /// <summary>New location to place <see cref="MainPiece"/></summary>
    public Vector2 MainNewLocation { get => new (MainCoords.RowIndex, MainCoords.ColumnIndex); }

    /// <summary>New location to place <see cref="SecondaryPiece"/>.</summary>
    public Vector2 SecondaryNewLocation { get => new(SecondaryCoords.RowIndex, SecondaryCoords.ColumnIndex); }

    /// <summary> Gets a value indicating if <see cref="MainPiece"/> is vulnerable to En Passant.</summary>
    /// <value><see langword="true"/> if <see cref="MainPiece"/> is vulnerable to En Passant; otherwise, <see langword="false"/></value>
    public bool EnPassantCapturePossible { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainPiece"/> is capturing <see cref="SecondaryPiece"/>.</summary>
    /// <value><see langword="true"/> if <see cref="MainPiece"/> is capturing <see cref="SecondaryPiece"/>; otherwise, <see langword="false"/></value>
    public bool CapturingSecondary { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainPiece"/> is castling with <see cref="SecondaryPiece"/>.</summary>
    /// <value><see langword="true"/> if <see cref="MainPiece"/> is castling with <see cref="SecondaryPiece"/>; otherwise, <see langword="false"/></value>
    public bool CastlingWithSecondary { get; init; }
    /// <summary>Gets a value indicating which <see cref="Team"/> is submitting the <see cref="MovementInformation"/> instance.</summary>
    /// <value>The current value of <c>MainPiece.PieceTeam</c></value>
    public Team SubmittingTeam { get => MainPiece.PieceTeam; }
}

public class ChessPiece
{
    /// <summary>List of default movement vectors for a given <see cref="ChessPiece"/> instance.</summary>
    private List<Vector2> _directionVectors = new();
    public List<Vector2> DirectionVectors
    {
        get
        {
            if (_directionVectors.Count == 0)
            {
                _directionVectors = (AssignedType != PieceType.Knight) ? AllDirectionVectors() : KnightDirectionVectors();
            }

            return _directionVectors;
        }
        set => _directionVectors = value!;
    }
    /// <summary>Current location of this <see cref="ChessPiece"/> instance within the current board.</summary>
    public Vector2 CurrentLocation { get => new(CurrentRow, CurrentColumn); }

    /// <summary>Gets the row that the <see cref="ChessPiece"/> instance is currently on.</summary>
    /// <value>The <see cref="CurrentLocation"/>.<c>X</c> property cast to an int.</value>
    public int CurrentRow { get; set; }

    /// <summary>Gets the column that the <see cref="ChessPiece"/> instance is currently in.</summary>
    /// <value>The <see cref="CurrentLocation"/>.<c>Y</c> property cast to an int.</value>
    public int CurrentColumn { get; set; }

    /// <summary>Gets the current value of <see cref="_pieceType"/>.</summary>
    public PieceType AssignedType { get => _pieceType; }

    /// <summary>Enum to describe what type of chess piece this instance will act as.</summary>
    /// <remarks>One of (King,Pawn,Queen,Bishop,Rook,Knight)</remarks>
    private PieceType _pieceType;

    /// <summary>Gets or initializes a Team enum to determine which team a <see cref="ChessPiece"/> is on.</summary>
    /// <remarks>Must be either Team.(White/Black)</remarks>
    /// <value>The current Team that this <see cref="ChessPiece"/> is on.</value>
    public Team PieceTeam { get; set; }

    /// <summary>Gets or sets a value representing whether or not the current <see cref="ChessPiece"/> instance is vulnerable to En Passant.</summary>
    /// <value><see langword="true"/> if a pawn can be captured via En Passant; otherwise, <see langword="false"/>.</value>
    private bool _enPassantCapturePossible = false;

    /// <summary>Gets or sets a value that describes if a <see cref="ChessPiece"/> instance can move across the board.</summary>
    /// <value><see cref="true"/> if <see cref="_pieceType"/> is PieceType.(Queen/Bishop/Rook); otherwise, <see langword="false"/>.</value>
    private bool _canMoveAcrossBoard = false;

    /// <summary>Gets or sets the number of times the current <see cref="ChessPiece"/> instance has been moved.</summary>
    /// <value>Integer reprensentation of how many a <see cref="ChessPiece"/> instance has been moved.</value>
    /// <remarks>Incremented by 1 whenever moved.</remarks>
    private int _timesMoved = 0;
    //public int TimesMoved{get;set;}

    /// <summary>Location assigned to all captured pieces.</summary>
    public static readonly Vector2 s_defaultLocation = new(-1);

    /// <summary>Gets or sets a value representing if the current <see cref="ChessPiece"/> instance has been captured.</summary>
    /// <value><see langword="true"/> if <see cref="ChessPiece"/> instance has been captured; otherwise, <see langword="false"/>.</value>
    public bool Captured { get; set; } = false;

    public ChessPiece(PieceType assignedType, Coords currentLocation, Team pieceTeam)
    {

        AssignPieceType(assignedType);
        AssignLocation(new Vector2(currentLocation.RowIndex, currentLocation.ColumnIndex));

        PieceTeam = pieceTeam;

        switch (_pieceType)
        {
            case PieceType.Bishop:
            case PieceType.Queen:
            case PieceType.Rook:
                _canMoveAcrossBoard = true;
                break;
        }

        DirectionVectors = DetermineDirectionVectors();
    }
    public ChessPiece()
    {

    }
    private List<Vector2> DetermineDirectionVectors()
    {

        if (AssignedType == PieceType.Knight) return KnightDirectionVectors();
        else return AllDirectionVectors();

    }
    /// <summary>
    /// Generates direction vectors for pieces that are capable of moving backwards,forwards,laterally and diagonally.
    /// </summary>
    /// <remarks>Used for Kings,Queens,Bishops,Pawns and Rooks.</remarks>
    /// <returns> A List{Vector2} is returned for a given piece.</returns>
    private List<Vector2> AllDirectionVectors()
    {

        bool defaultEntry = true;

        int possibleInitialJump = 0, forwardDirection = 0;

        switch (AssignedType)
        {
            case PieceType.Pawn:
            case PieceType.Rook:
            case PieceType.Bishop:
                if (AssignedType == PieceType.Pawn)
                {
                    forwardDirection = (PieceTeam == Team.White) ? 1 : -1;
                    possibleInitialJump = 2 * forwardDirection;
                }
                defaultEntry = false;
                break;
        }

        var directions = new List<Vector2>();

        for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
        {
            for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
            {
                if (horizontalScalar == 0 && verticalScalar == 0) continue;
                // Being equal to 1 implies that one dimension has a value of 0 and is therefore a perpendicular vector.
                bool perpendicularVector = Math.Abs(verticalScalar) + Math.Abs(horizontalScalar) == 1;

                bool enterRook = (AssignedType == PieceType.Rook) && perpendicularVector;
                bool enterBishop = (AssignedType == PieceType.Bishop) && !perpendicularVector;
                bool enterPawn = (AssignedType == PieceType.Pawn) && verticalScalar == forwardDirection;

                if (enterBishop || enterRook || enterPawn || defaultEntry)
                {
                    directions.Add(new Vector2(verticalScalar, horizontalScalar));

                    if (AssignedType == PieceType.King && verticalScalar == 0 && horizontalScalar != 0)
                    {   // This will allow the king to castle in either direction horizontally for castling purposes.
                        directions.Add(new Vector2(verticalScalar, 2 * horizontalScalar));
                    }
                    else if (AssignedType == PieceType.Pawn && horizontalScalar == 0)
                    {   // This will allow pawns to move forward 2 spaces if they haven't been moved yet.
                        directions.Add(new Vector2(possibleInitialJump, horizontalScalar));
                    }
                }
            }
        }

        return directions;
    }

    /// <summary>
    /// Generates general direction vectors for Knights.
    /// </summary>
    /// <returns>A list of Vector2 objects for which a Knight is allowed to move.</returns>
    public static List<Vector2> KnightDirectionVectors()
    {
        var directions = new List<Vector2>();

        int[] horizontalMovements = { 2, -2, 1, -1 };

        foreach (int horizontalScalar in horizontalMovements)
        {
            int verticalScalar = (Math.Abs(horizontalScalar) == 2) ? 1 : 2;

            for (int i = 0; i < 2; i++)
            {
                directions.Add(new Vector2(horizontalScalar, verticalScalar));
                verticalScalar *= -1;
            }
        }

        return directions;
    }
    /// <summary>
    /// Determines if the current instance of a <see cref="ChessPiece"/> object can attack another based on team allegiance.
    /// </summary>
    /// <param name="enemyPiece">This parameter is assigned a value if the piece being compared is hostile to the current instance of the <see cref="ChessPiece"/> class.</param>
    /// <param name="pieceToCompare"><see cref="ChessPiece"/> object that will have its allegiance tested.</param>
    /// <returns><see langword="true"/> if <paramref name="enemyPiece"/> and <paramref name="pieceToCompare"/> are both not null and are on different teams; otherwise, <see langword="false"/>.</returns>
    bool DoesSquareContainEnemy(ChessPiece? pieceToCompare, out ChessPiece? enemyPiece)
    {
        enemyPiece = null;

        if (pieceToCompare is null)
        {
            return false;
        }
        else
        {
            if (this.PieceTeam != pieceToCompare.PieceTeam)
            {
                enemyPiece = pieceToCompare;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Determine the available moves for a given <see cref="ChessPiece"/> instance. Moves that will place a friendly king in check will be filtered.
    /// </summary>
    /// <returns>A List(MovementInformation) object of available movements/attacks for the current <see cref="ChessPiece"/> instance.</returns>
    /// <param name="ignoreFriendlyInducedChecks">If <see langword="true"/> then checking to see if this piece's movements will unintenionally create a check on a friendly king are ignored.</param>
    /// <param name="disableCastling">If set to true then castling movements will be ignore.</param>
    /// <param name="gameBoard">Array used to calculate moves available to the current <see cref="ChessPiece"/> instance.</param>
    public List<MovementInformation> AvailableMoves(ChessPiece?[,] gameBoard, bool ignoreFriendlyInducedChecks, bool disableCastling)
    {
        var viableMoves = new List<MovementInformation>();
        bool spaceIsEmpty;

        foreach (Vector2 movementVector in DirectionVectors)
        {
            // If this is a castling vector determine if castling is possible.
            if (AssignedType == PieceType.King && Math.Abs(movementVector.Y) == 2)
            {
                if (disableCastling || _timesMoved != 0) continue;
                // A castleDirection of -1 means it is towards the left.
                int castleDirection = (int)movementVector.Y / 2;
                int friendlySpecialLine = (int)CurrentLocation.X;

                bool kingIsChecked = GameEnvironment.IsKingChecked(this, gameBoard);
                int rookColumn = castleDirection == -1 ? 0 : gameBoard.GetUpperBound(1);

                ChessPiece? pairedRook = gameBoard[friendlySpecialLine, rookColumn];

                // Ensure that the king hasn't been moved and isn't already in check.
                if (!kingIsChecked && pairedRook != null && pairedRook.AssignedType == PieceType.Rook && pairedRook._timesMoved == 0)
                {   // Now check to make sure that all spaces between the king and that rook are clear.
                    int lesserColumnIndex = Math.Min(rookColumn, CurrentColumn);
                    int greaterColumnIndex = Math.Max(rookColumn, CurrentColumn);
                    bool castlePathIsClear = true;

                    for (int columnIndex = lesserColumnIndex + 1; columnIndex < greaterColumnIndex; columnIndex++)
                    {   // Checking that squares between the king and rook are empty.
                        if (gameBoard[friendlySpecialLine, columnIndex] != null)
                        {
                            castlePathIsClear = false;
                            break;
                        }
                    }

                    if (!castlePathIsClear) continue;
                    else
                    {
                        bool cannotCastleInThisDirection = false;

                        if (!ignoreFriendlyInducedChecks)
                        {
                            var singleSquareMovement = new Vector2(0, castleDirection);
                            // Initialize at the current location for addition purposes.
                            Vector2 movement = CurrentLocation;
                            // Ensure that the King will not be moving into or through check.
                            for (int i = 0; i < 2; i++)
                            {
                                movement = Vector2.Add(movement, singleSquareMovement);

                                var singleSquareMovementInfo = new MovementInformation(this, null, new Coords(movement), new Coords(s_defaultLocation), false, capturingSecondary: false, castlingWithSecondary: false);

                                if (GameEnvironment.WillChangeResultInFriendlyCheck(singleSquareMovementInfo, gameBoard))
                                {
                                    cannotCastleInThisDirection = true;
                                    break;
                                }
                            }
                        }

                        if (!cannotCastleInThisDirection)
                        {
                            var newKingLocation = Vector2.Add(CurrentLocation, movementVector);
                            var newRookLocation = Vector2.Add(CurrentLocation, new Vector2(0, castleDirection));

                            var moveInfo = new MovementInformation(this, pairedRook, new Coords(newKingLocation), new Coords(newRookLocation), enPassantCapturePossible: false, capturingSecondary: false, castlingWithSecondary: true);

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
                for (int i = 1; i < 8; i++)
                {
                    var calculatedPosition = Vector2.Add(CurrentLocation, Vector2.Multiply(movementVector, i));

                    int xCoord = (int)calculatedPosition.X;
                    int yCoord = (int)calculatedPosition.Y;
                    // If coordinate isn't ot of bounds on the chess board then propagate the vector.
                    if (xCoord is >= 0 and <= 7 && yCoord is >= 0 and <= 7)
                    {
                        spaceIsEmpty = gameBoard[xCoord, yCoord] == null;

                        bool enemyPieceAtQueriedLocation = DoesSquareContainEnemy(gameBoard[xCoord, yCoord], out ChessPiece? captureablePiece);

                        bool pawnAttackVector = false, movementWillExposeToEnPassant = false;

                        if (AssignedType == PieceType.Pawn)
                        {
                            // Disable the starting move of moving 2 spaces forward if the pawn has already been moved.
                            if (Math.Abs(movementVector.X) == 2)
                            {
                                int initialRowJumped = PieceTeam == Team.White ? 2 : 5;

                                if (_timesMoved != 0 || gameBoard[initialRowJumped, CurrentColumn] != null) break;
                                movementWillExposeToEnPassant = true;
                            }

                            if (movementVector.Y == 0)
                            {   // Disables attacking in the forward direction.
                                enemyPieceAtQueriedLocation = false;
                            }
                            else
                            {
                                pawnAttackVector = true;
                                // Now check for possible En Passant captures.
                                if (DoesSquareContainEnemy(gameBoard[CurrentRow, yCoord], out ChessPiece? captureablePawn))
                                {
                                    if (captureablePawn != null && captureablePawn.AssignedType == PieceType.Pawn
                                    && captureablePawn._enPassantCapturePossible == true)
                                    {
                                        var enPassantCapture = new MovementInformation(this, captureablePawn, new Coords(calculatedPosition), new Coords(s_defaultLocation), movementWillExposeToEnPassant, capturingSecondary: true, castlingWithSecondary: false);

                                        if (ignoreFriendlyInducedChecks || !GameEnvironment.WillChangeResultInFriendlyCheck(enPassantCapture, gameBoard))
                                        {
                                            viableMoves.Add(enPassantCapture);
                                        }
                                    }
                                }
                            }
                        }

                        // Special notes for pawns: if pawnAttackVector = true, then the only way to move to that space is if enemyPieceAtQueriedLocation == true.
                        if ((spaceIsEmpty && !pawnAttackVector) || enemyPieceAtQueriedLocation)
                        {
                            var moveInfo = new MovementInformation(this, captureablePiece, new Coords(calculatedPosition), new Coords(s_defaultLocation), movementWillExposeToEnPassant, capturingSecondary: enemyPieceAtQueriedLocation, castlingWithSecondary: false);

                            if (ignoreFriendlyInducedChecks || !GameEnvironment.WillChangeResultInFriendlyCheck(moveInfo, gameBoard))
                            {
                                viableMoves.Add(moveInfo);
                            }
                        }

                        if (!spaceIsEmpty || !_canMoveAcrossBoard)
                        {
                            break;
                        }
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
    /// <summary>
    /// Creates a copy of the current instance.
    /// </summary>
    /// <returns>A a copy of the current <see cref="ChessPiece"/> instance.</returns>
    public ChessPiece Copy()
    {
        return new ChessPiece(AssignedType, new Coords(CurrentRow, CurrentColumn), PieceTeam)
        {
            Captured = this.Captured,
            _enPassantCapturePossible = this._enPassantCapturePossible
        };
    }
    /// <summary>
    /// Determines if a given <see cref="ChessPiece"/> instance has a <see cref="_pieceType"/> value of <see cref="PieceType.King"/>.
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="ChessPiece"/> instance has a <see cref="_pieceType"/> property of <see cref="PieceType.King"/>; otherwise, <see langword="false"/>.</returns>
    public bool IsKing()
    {
        return AssignedType == PieceType.King;
    }
    public void PromotePawn()
    {
        throw new NotImplementedException("Pawn promotion hasn't been implemented.");
        // Get user to select what type of chess piece they want.
        // _directionVectors = DetermineDirectionVectors();
    }
    /// <summary>
    /// Increases the <see cref="_timesMoved"/> property by one.
    /// </summary>
    public void IncreaseMovementCount()
    {
        _timesMoved++;
    }
    public void EnableEnPassantCaptures()
    {
        _enPassantCapturePossible = true;
    }
    public void DisableEnPassantCaptures()
    {
        _enPassantCapturePossible = false;
    }

    /// <summary>
    /// Use the <see cref="_pieceType"/> and <paramref name="PieceTeam"/> to return a descriptive name for a board square.
    /// For example: <example>White_Queen</example>
    /// </summary>
    /// <exception cref="ArguemntOutOfRangeException">Exception is thrown if <see cref="_pieceType"/> isn't a valid <see cref="PieceType"/> enum.</exception>
    /// <returns>A string represntation of <see cref="_pieceType"/> and <paramref name="PieceTeam"/>.</returns>
    public string ReturnPieceTypeName()
    {
        string methodValue = AssignedType switch
        {
            PieceType.King => "King",
            PieceType.Pawn => "Pawn",
            PieceType.Rook => "Rook",
            PieceType.Bishop => "Bishop",
            PieceType.Knight => "Knight",
            PieceType.Queen => "Queen",
            _ => throw new ArgumentOutOfRangeException(nameof(_pieceType), AssignedType, "Unrecognized PieceType submitted."),
        };
        return PieceTeam + "_" + methodValue;
    }
    public void AssignPieceType(PieceType newType)
    {
        _pieceType = newType;
    }
    public void AssignLocation(Vector2 newLocation)
    {
        CurrentRow = (int)newLocation.X;
        CurrentColumn = (int)newLocation.Y;
    }
}

