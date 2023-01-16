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

public class ChessPiece
{
    private List<Vector2> directionVectors;
    // Struct to hold the pieces current location
    public Vector2 currentLocation;
    // Enum value to differentiate the chess piece
    private PieceType Piece;
    // Enum value to determine where a piece is on the board
    public Team PieceTeam;
    private bool _canMoveAcrossBoard = false;
    private int _timesMoved = 0;
    public bool Captured = false;
    public ChessPiece(PieceType piece, Coords startingLocation, Team pieceTeam)
    {
        Piece = piece;
        currentLocation = new Vector2(startingLocation.RowIndex, startingLocation.ColumnIndex);
        PieceTeam = pieceTeam;

        switch (Piece)
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

        switch (Piece)
        {
            case PieceType.Pawn:
                // Can only go in one direction 1 space at a time.
                directions = PawnDirectionVectors();
                break;
            case PieceType.Knight:
                // L shaped movement.
                directions = KnightDirectionVectors();
                break;
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

        switch (Piece)
        {
            case PieceType.Rook:
            case PieceType.Bishop:
                defaultEntry = false;
                break;
        }

        var directions = new List<Vector2>();

        for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
        {
            for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
            {
                bool enterRook = (Piece == PieceType.Rook) && (horizontalScalar == 0 || verticalScalar == 0);
                bool enterBishop = (Piece == PieceType.Bishop) && (horizontalScalar != 0 && verticalScalar != 0);

                // Exclude the current space.
                if (!(horizontalScalar == 0 && verticalScalar == 0) && (enterBishop || enterRook || defaultEntry))
                {
                    directions.Add(new Vector2(horizontalScalar, verticalScalar));

                    if (Piece == PieceType.King && verticalScalar == 0 && horizontalScalar!=0)
                    {   // This vector will allow the king to castle in either direction.
                        directions.Add(new Vector2( verticalScalar, 2 * horizontalScalar));
                    }
                }
            }
        }

        return directions;
    }
    private List<Vector2> PawnDirectionVectors()
    {
        var directions = new List<Vector2>();
        // What is considered forward will change depending on what team you are on.
        int forwardDirection = (PieceTeam == Team.White) ? 1 : -1;

        int possibleInitialJump = 2 * forwardDirection;

        for (int i = -1; i < 2; i++)
        {   // A pawn has three possible movemnts: Forward and attacking diagonls on their path to the opposite side of the board.
            directions.Add(new Vector2(forwardDirection, i));
            // A Pawn can jump 2 spaces if it hasn't moved and there is nothing in the way.
            if (i == 0) directions.Add(new Vector2(possibleInitialJump, i));
        }

        return directions;
    }
    List<Vector2> KnightDirectionVectors()
    {
        var directions = new List<Vector2>();

        var horizontalMovements = new int[] { 3, -3, 1, -1 };

        foreach (int horizontalScalar in horizontalMovements)
        {
            int verticalScalar = (Math.Abs(horizontalScalar) == 3) ? 1 : 3;

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
    /// <returns>A boolean value that determines if two pieces have different allegiances.</returns>
    bool CanAttackSquare(ChessPiece? piecetoCompare)
    {
        if (piecetoCompare is null) return false;
        return this.PieceTeam != piecetoCompare.PieceTeam;
    }

    /// <summary>
    /// Determine the available moves for a given chess piece. Moves that will place a friendly king in check will be filtered.
    /// </summary>
    /// <returns> Returns a List(Vector2) object of available movements/attacks for a given chess piece.</returns>
    /// <param name="ignoreChecks">If true then checking to see if this piece's movements will unintenionally create a check on a friendly king.</param>
    public List<Vector2> AvailableMoves(ChessPiece?[,] gameBoard, bool ignoreChecks)
    {
        var moves = new List<Vector2>();
        bool spaceIsEmpty = true;
        bool canAttackSquare = false;
        bool kingIsChecked = false;

        foreach (Vector2 movementVector in directionVectors)
        {
            // If this is a castling vector determine if castling is possible.
            if (this.Piece == PieceType.King && Math.Abs(movementVector.Y) > 1)
            {
                // A castleDirection of -1 means that it is towards the left side of the board.
                int castleDirection = movementVector.Y < 0 ? -1 : 1;
                int friendlySpecialLine = PieceTeam == Team.White ? 0 : gameBoard.GetUpperBound(0);

                int rookColumn = castleDirection == -1 ? 0 : gameBoard.GetUpperBound(1);

                ChessPiece? pairedRook = gameBoard[friendlySpecialLine, rookColumn];

                // Ensure that the king hasn't been moved and isn't already in check.
                if (this._timesMoved == 0 && !kingIsChecked && pairedRook != null && pairedRook._timesMoved == 0)
                {   // Now check to make sure that all spaces between the king and that rook are clear.
                    int lesserColumnIndex = Math.Min(rookColumn, (int)this.currentLocation.Y);
                    int greaterColumnIndex = Math.Max(rookColumn, (int)this.currentLocation.Y);
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

                    if (!ignoreChecks)
                    {
                        // Vector: move 1 square towards the selected pawn.
                        Vector2 singleSquareMovement = new Vector2(0, castleDirection);
                        // Initialize at the current location.
                        Vector2 movement = this.currentLocation;

                        //float[] originalPosition = new float[2];
                        //movement.CopyTo(originalPosition);

                        // Ensure that the King will not be moving into or through check.
                        for (int i = 0; i < 2; i++)
                        {
                            movement = Vector2.Add(movement, singleSquareMovement);

                            if (GameEnvironment.WillChangeResultInFriendlyCheck(this, movement, gameBoard))
                            {
                                cannotCastleInThisDirection = true;
                                break;
                            }
                        }
                        //this.currentLocation = new Vector2(originalPosition[0], originalPosition[1]);

                    }

                    if (!cannotCastleInThisDirection)
                    {
                        moves.Add(movementVector);
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
                    var movement = Vector2.Add(currentLocation, movementScaled);

                    int xCoord = (int)movement.X;
                    int yCoord = (int)movement.Y;
                    // If coordinate isnt ot of bounds on the chess board then propagate the vector.
                    if (xCoord is >= 0 and <= 7 && yCoord is >= 0 and <= 7)
                    {
                        spaceIsEmpty = gameBoard[xCoord, yCoord] == null;
                        canAttackSquare = this.CanAttackSquare(gameBoard[xCoord, yCoord]);
                        bool disableDiagonalMovement = false;

                        bool validMovement = ignoreChecks || !GameEnvironment.WillChangeResultInFriendlyCheck(this, movement, gameBoard);

                        if (Piece == PieceType.Pawn)
                        {
                            if (movementVector.Y == 0)
                            {   // Disable attacking to the front.
                                canAttackSquare = false;
                            }
                            else
                            {   // Ignore moving to diagonals if nothing to attack
                                disableDiagonalMovement = true;
                            }
                            // Disable the starting move of moving 2 spaces forward if the pawn has already been moved.
                            if (Math.Abs(movementVector.X) == 2 & _timesMoved > 0) break;
                        }

                        if (validMovement && ((spaceIsEmpty & !disableDiagonalMovement) || canAttackSquare))
                        {
                            moves.Add(movement);
                        }

                        if (!spaceIsEmpty || !_canMoveAcrossBoard || !validMovement)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // Further multiplication of the vector will result in out of bounds values.
                        break;
                    }
                }
            }
        }
        return moves;
    }

    public ChessPiece Copy()
    {
        return new ChessPiece(Piece, new Coords((int)currentLocation.X, (int)currentLocation.Y), PieceTeam);
    }
    public bool IsKing()
    {
        return Piece == PieceType.King;
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
}

