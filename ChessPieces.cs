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
    private bool canMoveAcrossBoard = false;
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
                canMoveAcrossBoard = true;
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
                // can only go in one direction 1 space at a time.
                //directions.Add( new int[]{1* (PieceTeam==Team.White?1:-1)});
                directions = PawnDirectionVectors();
                break;
            case PieceType.Knight:
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

    private List<Vector2> AllDirectionVectors()
    {
        bool defaultEntry;

        switch (Piece)
        {
            case PieceType.Rook:
            case PieceType.Bishop:
                defaultEntry = false;
                break;
            default:
                defaultEntry = true;
                break;
        }

        var directions = new List<Vector2>();

        for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
        {
            for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
            {
                bool enterRook = Piece == PieceType.Rook && (horizontalScalar == 0 || verticalScalar == 0);
                bool enterBishop = Piece == PieceType.Bishop && (horizontalScalar != 0 && verticalScalar != 0);
                // Exclude the current space.
                if (!(horizontalScalar == 0 & verticalScalar == 0) && (enterBishop || enterRook || defaultEntry))
                {
                    directions.Add(new Vector2(horizontalScalar, verticalScalar));
                }
            }
        }
        return directions;
    }
    private List<Vector2> PawnDirectionVectors()
    {
        var directions = new List<Vector2>();
        int direction = (PieceTeam == Team.White) ? 1 : -1;
        int possibleInitialJump = 2 * direction;

        for (int i = -1; i < 2; i++)
        {
            directions.Add(new Vector2(direction, i));
            if (i == 0) directions.Add(new Vector2(possibleInitialJump, i));
        }

        return directions;
    }
    List<Vector2> KnightDirectionVectors()
    {
        var directions = new List<Vector2>();

        var axis1 = new int[] { 3, -3, 1, -1 };

        foreach (int horizontalScalar in axis1)
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
    /// Determines if a chess piece can be attacked.
    /// </summary>
    /// <returns>A boolean value that determines if two pieces have different allegiances.</returns>
    bool CanAttackSquare(ChessPiece? piecetoCompare)
    {
        if (piecetoCompare is null) return false;
        return this.PieceTeam != piecetoCompare.PieceTeam;
    }
    /// <summary>
    /// Determine the available moves for a given chess piece.
    /// </summary>
    /// <returns> Returns a List(Vector2) object of available movements/attacks for a given chess piece.</returns>

    public List<Vector2> AvailableMoves(ChessPiece?[,] gameBoard)
    {
        var moves = new List<Vector2>();
        bool spaceIsEmpty = true;
        bool canAttackSquare = false;

        foreach (Vector2 movementVector in directionVectors)
        {
            for (float i = 1; i < 7; i++)
            {   // Loop at most 7 times to account for any piece moving across the board
                // Start at 1 for multiplication purposes.
                var movementScaled = Vector2.Multiply(movementVector, i);
                var movement = Vector2.Add(currentLocation, movementScaled);

                int xCoord = (int)movement.X;
                int yCoord = (int)movement.Y;

                if (xCoord is >= 0 and <= 7 && yCoord is >= 0 and <= 7)
                {
                    spaceIsEmpty = gameBoard[xCoord, yCoord] == null;
                    canAttackSquare = this.CanAttackSquare(gameBoard[xCoord, yCoord]);
                    bool disableDiagonalMovement = false;

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

                    if ((spaceIsEmpty & !disableDiagonalMovement) || canAttackSquare) moves.Add(movement);

                    if (!spaceIsEmpty || !canMoveAcrossBoard)
                    {
                        break;
                    }
                }
                else break;
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
}

