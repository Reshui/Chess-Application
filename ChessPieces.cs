using Microsoft.Win32.SafeHandles;
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
    {//capturingSecondaryViaEnPassant
        SecondaryPiece = secondaryPiece;
        MainPiece = mainPiece;
        MainNewLocation = mainNewLocation;
        SecondaryNewLocation = secondaryNewLocation;
        EnPassantCapturePossible = enPassantCapturePossible;
        CapturingSecondary = capturingSecondary;
        CastlingWithSecondary = castlingWithSecondary;
        //CapturingSecondaryViaEnPassant = capturingSecondaryViaEnPassant;
    }
    public ChessPiece MainPiece { get; init; }
    public ChessPiece? SecondaryPiece { get; init; }
    public Vector2 MainNewLocation { get; init; }
    public Vector2 SecondaryNewLocation { get; init; }
    public bool EnPassantCapturePossible { get; init; }
    public bool CapturingSecondary { get; init; }
    public bool CastlingWithSecondary { get; init; }
    //public bool CapturingSecondaryViaEnPassant { get; init; }
}

public class ChessPiece
{
    private List<Vector2> directionVectors;
    public Vector2 currentLocation;
    private PieceType _pieceType;
    public Team PieceTeam;
    private bool _enPassantCapturePossible = false;
    private bool _canMoveAcrossBoard = false;
    private int _timesMoved = 0;
    public static readonly Vector2 DefaultLocation = new Vector2(-1);
    //private int _lastMovedOnTurn = 0;

    public bool Captured = false;

    public ChessPiece(PieceType piece, Coords startingLocation, Team pieceTeam)
    {
        _pieceType = piece;
        currentLocation = new Vector2(startingLocation.RowIndex, startingLocation.ColumnIndex);
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
        var directions = new List<Vector2>();

        switch (_pieceType)
        {
            case PieceType.Knight:
                // L shaped movement.
                directions = KnightDirectionVectors();
                break;
            case PieceType.Pawn:
            case PieceType.Bishop:
            case PieceType.Rook:
            case PieceType.Queen:
            case PieceType.King:
                directions = AllDirectionVectors();
                break;
        }

        return directions;
    }
    /// <summary>
    /// Generates direction vectors for pieces that are capable of moving backwards,forwards,laterally and diagonally.
    /// Used for Kings,Queens,Bishops and Rooks.
    /// </summary>
    /// <returns> A List{Vector2} is returned for a given piece.</returns>
    private List<Vector2> AllDirectionVectors()
    {
        bool defaultEntry = true;
        int forwardDirection = 0;
        int possibleInitialJump = 0;

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
            {   // Only if perpendicular.
                bool enterRook = (_pieceType == PieceType.Rook) && (horizontalScalar == 0 || verticalScalar == 0);
                // Only if diagonal.
                bool enterBishop = (_pieceType == PieceType.Bishop) && (horizontalScalar != 0 && verticalScalar != 0);

                bool enterPawn = (_pieceType == PieceType.Pawn) && verticalScalar == forwardDirection;

                // Exclude the current space.
                if (!(horizontalScalar == 0 && verticalScalar == 0) && (enterBishop || enterRook || enterPawn || defaultEntry))
                {
                    directions.Add(new Vector2(verticalScalar, horizontalScalar));

                    if (_pieceType == PieceType.King && verticalScalar == 0 && horizontalScalar != 0)
                    {   // This vector will allow the king to castle in either direction.
                        directions.Add(new Vector2(verticalScalar, 2 * horizontalScalar));
                    }
                    else if (_pieceType == PieceType.Pawn && horizontalScalar == 0)
                    {
                        directions.Add(new Vector2(possibleInitialJump, horizontalScalar));
                    }
                }
            }
        }

        return directions;
    }
    /// <summary>
    /// Generates direction vectors for Knights.
    /// </summary>
    /// <returns>A list of Vector2 objects for which a knight is allowed to move.</returns>
    public static List<Vector2> KnightDirectionVectors()
    {
        var directions = new List<Vector2>();

        var horizontalMovements = new int[] { 2, -2, 1, -1 };

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
    /// Determines if the current instance of a ChessPiece object can attack another based on team allegiance.
    /// </summary>
    /// <param name="enemyPiece">This parameter is assigned a value if the piece being compared is hostile to the current instance of the ChessPiece class.</param>
    /// <returns>A boolean value that determines if two pieces have different allegiances.</returns>
    bool CanAttackSquare(ChessPiece? piecetoCompare, out ChessPiece? enemyPiece)
    {
        enemyPiece = null;

        if (piecetoCompare is null)
        {
            return false;
        }
        else
        {
            bool piecesOnDifferentTeams = this.PieceTeam != piecetoCompare.PieceTeam;
            if (piecesOnDifferentTeams) enemyPiece = piecetoCompare;
            return piecesOnDifferentTeams;
        }

    }

    /// <summary>
    /// Determine the available moves for a given chess piece. Moves that will place a friendly king in check will be filtered.
    /// </summary>
    /// <returns> Returns a List(Vector2) object of available movements/attacks for a given chess piece.</returns>
    /// <param name="ignoreFriendlyInducedChecks">If true then checking to see if this piece's movements will unintenionally create a check on a friendly king are ignored.</param>
    public List<MovementInformation> AvailableMoves(ChessPiece?[,] gameBoard, bool ignoreFriendlyInducedChecks, bool disableCastling)
    {
        var moves = new List<MovementInformation>();
        bool spaceIsEmpty;
        //bool kingIsChecked = false;

        foreach (Vector2 movementVector in directionVectors)
        {
            // If this is a castling vector determine if castling is possible.
            if (!disableCastling && this._pieceType == PieceType.King && Math.Abs(movementVector.Y) > 1)
            {
                // A castleDirection of -1 means that it is towards the left side of the board.
                int castleDirection = movementVector.Y < 0 ? -1 : 1;
                // Row that pieces like the King or Queen are originally placed on.
                int friendlySpecialLine = PieceTeam == Team.White ? 0 : gameBoard.GetUpperBound(0);
                Team opposingTeam = PieceTeam == Team.White ? Team.Black : Team.White;

                bool kingIsChecked = GameEnvironment.IsKingChecked(this, gameBoard);

                int rookColumn = castleDirection == -1 ? 0 : gameBoard.GetUpperBound(1);

                ChessPiece? pairedRook = gameBoard[friendlySpecialLine, rookColumn];

                // Ensure that the king hasn't been moved and isn't already in check.
                if (this._timesMoved == 0 && !kingIsChecked && pairedRook != null && pairedRook._pieceType == PieceType.Rook && pairedRook._timesMoved == 0)
                {   // Now check to make sure that all spaces between the king and that rook are clear.
                    int lesserColumnIndex = Math.Min(rookColumn, ReturnLocation(1));
                    int greaterColumnIndex = Math.Max(rookColumn, ReturnLocation(1));
                    bool squaresBetweenKingAndRookNotNull = false;

                    for (int columnIndex = lesserColumnIndex + 1; columnIndex < greaterColumnIndex; columnIndex++)
                    {   // Checking that squares between the king and rook are empty.
                        if (gameBoard[friendlySpecialLine, columnIndex] != null)
                        {
                            squaresBetweenKingAndRookNotNull = true;
                            break;
                        }
                    }

                    if (squaresBetweenKingAndRookNotNull) continue;

                    bool cannotCastleInThisDirection = false;
                    
                    if (!ignoreFriendlyInducedChecks)
                    {
                        Vector2 singleSquareMovement = new Vector2(0, castleDirection);
                        // Initialize at the current location for addition purposes.
                        Vector2 movement = this.currentLocation;

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
                        var newKingLocation = Vector2.Add(this.currentLocation, new Vector2(0, 2 * castleDirection));
                        var newRookLocation = Vector2.Add(this.currentLocation, new Vector2(0, castleDirection));

                        var moveInfo = new MovementInformation(this, pairedRook, newKingLocation, newRookLocation, false, capturingSecondary: false, castlingWithSecondary: true);

                        moves.Add(moveInfo);
                    }
                }
            }
            else
            {
                // This loop serves the purpose of calculating possible movements for pieces that can move multiple squares in a given direction.
                // Loop at most 7 times to account for any piece moving across the board.
                // Start at 1 for multiplication purposes.
                for (float i = 1; i < 8; i++)
                {
                    var movementScaled = Vector2.Multiply(movementVector, i);
                    var calculatedPosition = Vector2.Add(currentLocation, movementScaled);

                    int xCoord = (int)calculatedPosition.X;
                    int yCoord = (int)calculatedPosition.Y;
                    // If coordinate isnt ot of bounds on the chess board then propagate the vector.
                    if (xCoord is >= 0 and <= 7 && yCoord is >= 0 and <= 7)
                    {
                        spaceIsEmpty = gameBoard[xCoord, yCoord] == null;

                        bool canAttackSquare = this.CanAttackSquare(gameBoard[xCoord, yCoord], out ChessPiece? captureablePiece);

                        bool disablePawnDiagonalWithoutEnemy = false;
                        bool movementWillExposeToEnPassant = false;

                        if (_pieceType == PieceType.Pawn)
                        {
                            // Disable the starting move of moving 2 spaces forward if the pawn has already been moved.
                            if (Math.Abs(movementVector.X) > 1)
                            {
                                int initialRowJumped = this.PieceTeam == Team.White ? 2 : 5;

                                if (_timesMoved > 0 || gameBoard[initialRowJumped, this.ReturnLocation(1)] != null) break;
                                movementWillExposeToEnPassant = true;
                            }

                            if (movementVector.Y == 0)
                            {   // Disable attacking to the front.
                                canAttackSquare = false;
                            }
                            else
                            {   // Ignore moving to diagonals if nothing to attack.
                                disablePawnDiagonalWithoutEnemy = true;

                                // Check for possible En Passant captures.
                                if (this.CanAttackSquare(gameBoard[this.ReturnLocation(0), yCoord], out ChessPiece? captureablePawn))
                                {
                                    if (captureablePawn != null && captureablePawn._pieceType == PieceType.Pawn
                                    && captureablePawn._enPassantCapturePossible == true)
                                    {
                                        var enPassantCapture = new MovementInformation(this, captureablePawn, calculatedPosition, DefaultLocation, movementWillExposeToEnPassant, capturingSecondary: true, castlingWithSecondary: false);

                                        if (ignoreFriendlyInducedChecks || !GameEnvironment.WillChangeResultInFriendlyCheck(enPassantCapture, gameBoard))
                                        {
                                            moves.Add(enPassantCapture);
                                        }
                                    }
                                }
                            }
                        }

                        bool validMovement = false;

                        if ((spaceIsEmpty && !disablePawnDiagonalWithoutEnemy) || canAttackSquare)
                        {
                            var moveInfo = new MovementInformation(this, captureablePiece, calculatedPosition, DefaultLocation, movementWillExposeToEnPassant, capturingSecondary: canAttackSquare, castlingWithSecondary: false);

                            validMovement = ignoreFriendlyInducedChecks || !GameEnvironment.WillChangeResultInFriendlyCheck(moveInfo, gameBoard);

                            if (validMovement) moves.Add(moveInfo);
                        }

                        if (!spaceIsEmpty || !_canMoveAcrossBoard || !validMovement)
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
        return moves;
    }

    public ChessPiece Copy()
    {
        var pieceCopy = new ChessPiece(_pieceType, new Coords(ReturnLocation(0), ReturnLocation(1)), PieceTeam)
        {
            Captured = this.Captured,
            _enPassantCapturePossible = this._enPassantCapturePossible
        };
        return pieceCopy;
    }
    public bool IsKing()
    {
        return _pieceType == PieceType.King;
    }

    public void PromotePawn()
    {
        // Get user to select what type of chess piece they want.

        directionVectors = DetermineDirectionVectors();
    }
    public void IncreaseMovemntCount()
    {
        _timesMoved++;
    }
    public void EnableEnPassantCaptures()
    {
        this._enPassantCapturePossible = true;
    }
    public void DisableEnPassantCaptures()
    {
        this._enPassantCapturePossible = false;
    }
    /// <summary>
    /// Using the currentLocation property return an X or Y coordinate cast to an integer.
    /// </summary>
    /// <returns>An integer between 0 and 7.</returns>
    public int ReturnLocation(int dimension)
    {
        if (dimension == 0)
        {
            return (int)currentLocation.X;
        }
        else if (dimension == 1)
        {
            return (int)currentLocation.Y;
        }

        return -1;

    }
    public PieceType ReturnPieceType()
    {
        return _pieceType;
    }
    public string ReturnPieceTypeName()
    {
        string methodValue;

        switch (_pieceType)
        {
            case PieceType.King:
                methodValue = "King";
                break;
            case PieceType.Pawn:
                methodValue = "Pawn";
                break;
            case PieceType.Rook:
                methodValue = "Rook";
                break;
            case PieceType.Bishop:
                methodValue = "Bishop";
                break;
            case PieceType.Knight:
                methodValue = "Knight";
                break;
            case PieceType.Queen:
                methodValue = "Queen";
                break;
            default:
                methodValue = "Unknown";
                break;
        }

        string teamDesignation = PieceTeam == Team.White ? "< W >" : "|||";
        return methodValue += $"\n{teamDesignation}";
    }
}

