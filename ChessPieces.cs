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

    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
}

public readonly struct MovementInformation
{
    public MovementInformation(ChessPiece mainPiece, ChessPiece? secondaryPiece, Vector2 mainNewLocation, Vector2 secondaryNewLocation, bool enPassantCapturePossible, bool capturingSecondary, bool castlingWithSecondary)
    {
        SecondaryPiece = secondaryPiece;
        MainPiece = mainPiece;
        MainNewLocation = mainNewLocation;
        SecondaryNewLocation = secondaryNewLocation;
        EnPassantCapturePossible = enPassantCapturePossible;
        CapturingSecondary = capturingSecondary;
        CastlingWithSecondary = castlingWithSecondary;
    }
    /// <summary>Primary ChessPiece that is moved.</summary>
    public ChessPiece MainPiece { get; init; }

    /// <summary>Nullable property that refrences any secondary chess pieces involved in the given movement.
    /// For example: A rook being castled with or an opponent chess piece that's being captured.</summary>
    public ChessPiece? SecondaryPiece { get; init; }

    /// <summary>New location to place <see cref="MainPiece"/></summary>
    public Vector2 MainNewLocation { get; init; }

    /// <summary>New location to place <see cref="SecondaryPiece"/>.</summary>
    public Vector2 SecondaryNewLocation { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainPiece"/> is vulnerable to En Passant.</summary>
    /// <value><see langword="true"/> if <see cref="MainPiece"/> is vulnerable to En Passant; otherwise, <see langword="false"/></value>
    public bool EnPassantCapturePossible { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainPiece"/> is capturing <see cref="SecondaryPiece"/>.</summary>
    /// <value><see langword="true"/> if <see cref="MainPiece"/> is capturing <see cref="SecondaryPiece"/>; otherwise, <see langword="false"/></value>
    public bool CapturingSecondary { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainPiece"/> is castling with <see cref="SecondaryPiece"/>.</summary>
    /// <value><see langword="true"/> if <see cref="MainPiece"/> is castling with <see cref="SecondaryPiece"/>; otherwise, <see langword="false"/></value>
    public bool CastlingWithSecondary { get; init; }
}

public class ChessPiece
{
    /// <summary>List of default movement vectors for a given <see cref="ChessPiece"/> instance.</summary>
    private List<Vector2> directionVectors { get; set; }

    /// <summary>Current location of this <see cref="ChessPiece"/> instance within the current board.</summary>
    public Vector2 CurrentLocation { get; set; }

    /// <summary>Enum to determine piece type. One of (King,Pawn,Queen,Bishop,Rook,Knight)</summary>
    private PieceType _pieceType { get; set; }

    /// <summary>Enum to determine which team a given piece is on. Must be either Team.(White/Black)</summary>
    public Team PieceTeam { get; init; }

    /// <summary>Gets or sets a value representing whether or not the current <see cref="ChessPiece"/> instance is vulnerable to En Passant.</summary>
    /// <value><see langword="true"/> if a pawn can be captured via En Passant; otherwise, <see langword="false"/>.</value>
    private bool _enPassantCapturePossible { get; set; } = false;

    /// <summary>true if <see cref="_pieceType"/> is PieceType.(Queen/Bishop/Rook) else false.</summary>
    private bool _canMoveAcrossBoard { get; set; } = false;

    /// <summary>Gets or sets the number of times the current <see cref="ChessPiece"/> instance has been moved.</summary>
    /// <value>Integer reprensentation of how many a <see cref="ChessPiece"/> instance has been moved.</value>
    /// <remarks>Incremented by 1 whenever moved.</remarks>
    private int _timesMoved { get; set; } = 0;

    /// <summary>Location assigned to all captured pieces.</summary>
    public static readonly Vector2 DefaultLocation = new Vector2(-1);

    /// <summary>Gets or sets a value representing if the current <see cref="ChessPiece"/> instance has been captured.</summary>
    /// <value><see langword="true"/> if <see cref="ChessPiece"/> instance has been captured; otherwise, <see langword="false"/>.</value>
    public bool Captured { get; set; } = false;

    public ChessPiece(PieceType piece, Coords startingLocation, Team pieceTeam)
    {
        _pieceType = piece;
        CurrentLocation = new Vector2(startingLocation.RowIndex, startingLocation.ColumnIndex);
        PieceTeam = pieceTeam;

        switch (_pieceType)
        {
            case PieceType.Bishop:
            case PieceType.Queen:
            case PieceType.Rook:
                _canMoveAcrossBoard = true;
                break;
        }

        directionVectors = DetermineDirectionVectors();
    }
    private List<Vector2> DetermineDirectionVectors()
    {

        if (_pieceType == PieceType.Knight) return KnightDirectionVectors();
        else return AllDirectionVectors();

    }
    /// <summary>
    /// Generates direction vectors for pieces that are capable of moving backwards,forwards,laterally and diagonally.
    /// Used for Kings,Queens,Bishops,Pawns and Rooks.
    /// </summary>
    /// <returns> A List{Vector2} is returned for a given piece.</returns>
    private List<Vector2> AllDirectionVectors()
    {

        bool defaultEntry = true;

        int possibleInitialJump = 0, forwardDirection = 0;

        switch (_pieceType)
        {
            case PieceType.Pawn:
            case PieceType.Rook:
            case PieceType.Bishop:
                if (_pieceType == PieceType.Pawn)
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

                bool enterRook = (_pieceType == PieceType.Rook) && perpendicularVector;
                bool enterBishop = (_pieceType == PieceType.Bishop) && !perpendicularVector;
                bool enterPawn = (_pieceType == PieceType.Pawn) && verticalScalar == forwardDirection;

                if (enterBishop || enterRook || enterPawn || defaultEntry)
                {
                    directions.Add(new Vector2(verticalScalar, horizontalScalar));

                    if (_pieceType == PieceType.King && verticalScalar == 0 && horizontalScalar != 0)
                    {   // This vector will allow the king to castle in either direction.
                        directions.Add(new Vector2(verticalScalar, 2 * horizontalScalar));
                    }
                    else if (_pieceType == PieceType.Pawn && horizontalScalar == 0)
                    {   // This vector will allow pawns to move forward 2 spaces if they haven't been moved.
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
    /// <returns><see langword="true"/> if <paramref name="enemyPiece"/> and <paramref name="pieceToCompare"/> are both not null and are on different teams; otherwise <see langword="false"/>.</returns>
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
    /// <param name="ignoreFriendlyInducedChecks">If true then checking to see if this piece's movements will unintenionally create a check on a friendly king are ignored.</param>
    /// <param name="disableCastling">If set to true then castling movements will be ignore.</param>
    /// <param name="gameBoard">Array used to calculate moves available to the current <see cref="ChessPiece"/> instance.</param>
    public List<MovementInformation> AvailableMoves(ChessPiece?[,] gameBoard, bool ignoreFriendlyInducedChecks, bool disableCastling)
    {
        var viableMoves = new List<MovementInformation>();
        bool spaceIsEmpty;

        foreach (Vector2 movementVector in directionVectors)
        {
            // If this is a castling vector determine if castling is possible.
            if (_pieceType == PieceType.King && Math.Abs(movementVector.Y) == 2)
            {
                if (disableCastling || _timesMoved != 0) continue;
                // A castleDirection of -1 means it is towards the left.
                int castleDirection = (int)movementVector.Y / 2;
                int friendlySpecialLine = (int)CurrentLocation.X;

                bool kingIsChecked = GameEnvironment.IsKingChecked(this, gameBoard);
                int rookColumn = castleDirection == -1 ? 0 : gameBoard.GetUpperBound(1);

                ChessPiece? pairedRook = gameBoard[friendlySpecialLine, rookColumn];

                // Ensure that the king hasn't been moved and isn't already in check.
                if (!kingIsChecked && pairedRook != null && pairedRook._pieceType == PieceType.Rook && pairedRook._timesMoved == 0)
                {   // Now check to make sure that all spaces between the king and that rook are clear.
                    int lesserColumnIndex = Math.Min(rookColumn, ReturnLocation(1));
                    int greaterColumnIndex = Math.Max(rookColumn, ReturnLocation(1));
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

                    bool cannotCastleInThisDirection = false;

                    if (!ignoreFriendlyInducedChecks)
                    {
                        Vector2 singleSquareMovement = new Vector2(0, castleDirection);
                        // Initialize at the current location for addition purposes.
                        Vector2 movement = CurrentLocation;
                        // Ensure that the King will not be moving into or through check.
                        for (int i = 0; i < 2; i++)
                        {
                            movement = Vector2.Add(movement, singleSquareMovement);

                            var singleSquareMovementInfo = new MovementInformation(this, null, movement, DefaultLocation, false, capturingSecondary: false, castlingWithSecondary: false);

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

                        var moveInfo = new MovementInformation(this, pairedRook, newKingLocation, newRookLocation, enPassantCapturePossible: false, capturingSecondary: false, castlingWithSecondary: true);

                        viableMoves.Add(moveInfo);
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

                        if (_pieceType == PieceType.Pawn)
                        {
                            // Disable the starting move of moving 2 spaces forward if the pawn has already been moved.
                            if (Math.Abs(movementVector.X) == 2)
                            {
                                int initialRowJumped = PieceTeam == Team.White ? 2 : 5;

                                if (_timesMoved != 0 || gameBoard[initialRowJumped, ReturnLocation(1)] != null) break;
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
                                if (DoesSquareContainEnemy(gameBoard[ReturnLocation(0), yCoord], out ChessPiece? captureablePawn))
                                {
                                    if (captureablePawn != null && captureablePawn._pieceType == PieceType.Pawn
                                    && captureablePawn._enPassantCapturePossible == true)
                                    {
                                        var enPassantCapture = new MovementInformation(this, captureablePawn, calculatedPosition, DefaultLocation, movementWillExposeToEnPassant, capturingSecondary: true, castlingWithSecondary: false);

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
                            var moveInfo = new MovementInformation(this, captureablePiece, calculatedPosition, DefaultLocation, movementWillExposeToEnPassant, capturingSecondary: enemyPieceAtQueriedLocation, castlingWithSecondary: false);

                            if (ignoreFriendlyInducedChecks || !GameEnvironment.WillChangeResultInFriendlyCheck(moveInfo, gameBoard))
                            {
                                viableMoves.Add(moveInfo);
                            }
                        }

                        if ((spaceIsEmpty || _canMoveAcrossBoard) == false) break;
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
        return new ChessPiece(_pieceType, new Coords(ReturnLocation(0), ReturnLocation(1)), PieceTeam)
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
        return _pieceType == PieceType.King;
    }
    public void PromotePawn()
    {
        throw new NotImplementedException("Pawn promotion hasn't been implemented.");
        // Get user to select what type of chess piece they want.
        // directionVectors = DetermineDirectionVectors();
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
    /// Using the <see cref="CurrentLocation"/> property return an X or Y coordinate cast to an integer.
    /// </summary>
    /// <param name="dimension">Integer that is either 0 and 1</param>
    /// <exception cref="ArguemntOutOfRangeException">Exception is thrown if <paramref name="dimension"/> isn't 0 or 1.</exception>
    /// <returns>An integer between 0 and 7.</returns>
    public int ReturnLocation(int dimension)
    {
        if (dimension == 0)
        {
            return (int)CurrentLocation.X;
        }
        else if (dimension == 1)
        {
            return (int)CurrentLocation.Y;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension parameter must be either 0 or 1.");
        }
    }
    /// <summary>
    /// Returns the current value of the <see cref="_pieceType"/> property.
    /// </summary>
    /// <returns>A <see cref="PieceType"/> enum.</returns>
    public PieceType ReturnPieceType()
    {
        return _pieceType;
    }

    /// <summary>
    /// Use the <see cref="_pieceType"/> and <paramref name="PieceTeam"/> to return a descriptive name for a board square.
    /// For example: <example>White_Queen</example>
    /// </summary>
    /// <exception cref="ArguemntOutOfRangeException">Exception is thrown if <see cref="_pieceType"/> isn't a valid <see cref="PieceType"/> enum.</exception>
    /// <returns>A string represntation of <see cref="_pieceType"/> and <paramref name="PieceTeam"/>.</returns>
    public string ReturnPieceTypeName()
    {
        string methodValue = _pieceType switch
        {
            PieceType.King => "King",
            PieceType.Pawn => "Pawn",
            PieceType.Rook => "Rook",
            PieceType.Bishop => "Bishop",
            PieceType.Knight => "Knight",
            PieceType.Queen => "Queen",
            _ => throw new ArgumentOutOfRangeException(nameof(_pieceType), _pieceType, "Unrecognized PieceType submitted."),
        };
        return PieceTeam + "_" + methodValue;
    }
}

