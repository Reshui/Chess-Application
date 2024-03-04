using System.Numerics;
using System.Text.Json.Serialization;

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
    ///<summary>Constructor used when initializing with a Vector2 struct.</summary>
    public Coords(Vector2 vector)
    {
        RowIndex = (int)vector.Y;
        ColumnIndex = (int)vector.X;
    }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
}
/// <summary>
/// Struct that contains information about a given chess movement.
/// </summary>
public struct MovementInformation
{
    /// <summary>Class that contains the new coordinates to move <see cref="MainCopy"/> to.</summary>
    public Coords NewMainCoords { get; init; }
    /// <summary>Class that contains the new coordinates to move <see cref="SecondaryCopy"/> to if available.</summary>
    public Coords? NewSecondaryCoords { get; init; }
    /// <summary>Primary ChessPiece that is moved.</summary>
    public ChessPiece MainCopy { get; init; }

    /// <summary>Nullable property that refrences any secondary chess pieces involved in the given movement.
    /// For example: <example>A rook being castled with or an opponent chess piece that's being captured</example>.</summary>
    public ChessPiece? SecondaryCopy { get; init; }

    /// <summary>New location to place <see cref="MainCopy"/></summary>
    public readonly Vector2 MainNewLocation { get => new(NewMainCoords.ColumnIndex, NewMainCoords.RowIndex); }

    /// <summary>New location to place <see cref="SecondaryCopy"/>.</summary>
    public readonly Vector2? SecondaryNewLocation
    {
        get
        {
            return NewSecondaryCoords == null ? null : new Vector2((float)NewSecondaryCoords?.ColumnIndex!, (float)NewSecondaryCoords?.RowIndex!);
        }
    }

    /// <summary> Gets a value indicating if <see cref="MainCopy"/> is vulnerable to En Passant.</summary>
    /// <value><see langword="true"/> if <see cref="MainCopy"/> is vulnerable to En Passant; otherwise, <see langword="false"/></value>
    public bool EnPassantCapturePossible { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainCopy"/> is capturing <see cref="SecondaryCopy"/>.</summary>
    /// <value><see langword="true"/> if <see cref="MainCopy"/> is capturing <see cref="SecondaryCopy"/>; otherwise, <see langword="false"/></value>
    public bool CapturingSecondary { get; init; }

    /// <summary> Gets a value indicating if <see cref="MainCopy"/> is castling with <see cref="SecondaryCopy"/>.</summary>
    /// <value><see langword="true"/> if <see cref="MainCopy"/> is castling with <see cref="SecondaryCopy"/>; otherwise, <see langword="false"/></value>
    public bool CastlingWithSecondary { get; init; }

    /// <summary>Gets a value indicating which <see cref="Team"/> is submitting the <see cref="MovementInformation"/> instance.</summary>
    /// <value>The current value of <see cref="MovementInformation.MainCopy"/>.<c>AssignedTeam</c>.</value>
    public readonly Team SubmittingTeam => MainCopy.AssignedTeam;

    /// <summary>Gets a value representing if the <see cref="MovementInformation"/> instance describes if <see cref="MainCopy"/> is capturing <see cref="SecondaryCopy"/>
    /// via En Passant.</summary>
    /// <value><see langword="true"/> if capturing via En Passant; otherwise, <see langword="false"/>.</value>
    public readonly bool CapturingViaEnPassant
    {
        get
        {
            if (CapturingSecondary && SecondaryCopy is not null && !new Coords(SecondaryCopy.CurrentLocation).Equals(NewMainCoords))
            {
                return true;
            }
            return false;
        }
    }
    /// <value>
    /// If not null then MainCopy will have its PieceType replaced with if it is a pawn.
    /// </value>
    public PieceType? NewType { get; set; } = null;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovementInformation"/> class.
    /// </summary>
    /// <param name="mainPieceCopy">Copy of piece doing the move.</param>
    /// <param name="secondaryPieceCopy">Copy or null of secondary piece being acted upon.</param>
    /// <param name="newMainCoords">Coordinates to move <paramref name="mainPieceCopy"/> to.</param>
    /// <param name="newSecondaryCoords">Coordinates to move <paramref name="secondaryPieceCopy"/> to.</param>
    /// <param name="enPassantCapturePossible"><see langword="true"/> if <paramref name="mainPieceCopy"/> is captureable via En Passant; otherwise, ><see langword="false"/>.</param>
    /// <param name="capturingSecondary"></param>
    /// <param name="castlingWithSecondary"><see langword="true"/> if <paramref name="mainPieceCopy"/> should castle with <paramref name="secondaryPieceCopy"/>; otherwise, <see langword="false"/>   </param>
    /// <param name="newType">PieceType to convert <paramref name="mainPieceCopy"/> to.</param>
    /// <exception cref="ArgumentException"></exception>
    public MovementInformation(ChessPiece mainPieceCopy, ChessPiece? secondaryPieceCopy, Coords newMainCoords, Coords? newSecondaryCoords, bool enPassantCapturePossible, bool capturingSecondary, bool castlingWithSecondary, PieceType? newType)
    {
        if (mainPieceCopy.IsCopy && (secondaryPieceCopy?.IsCopy ?? true))
        {
            SecondaryCopy = secondaryPieceCopy;
            MainCopy = mainPieceCopy;
        }
        else
        {
            throw new ArgumentException($"Both {nameof(mainPieceCopy)} and {nameof(secondaryPieceCopy)} must have a {nameof(ChessPiece.IsCopy)} property of true");
        }
        NewMainCoords = newMainCoords;
        NewSecondaryCoords = newSecondaryCoords;
        EnPassantCapturePossible = enPassantCapturePossible;
        CapturingSecondary = capturingSecondary;
        CastlingWithSecondary = castlingWithSecondary;
        NewType = newType;
    }
}

public class ChessPiece
{
    [JsonIgnore]
    /// <summary>A list of <see cref="Vector2"/> instances that represent all movement directions available to the current instance.</summary>
    public List<Vector2> DirectionVectors
    {
        get
        {
            if (_directionVectors.Count == 0) { _directionVectors = AvailableDirectionVectors(); }
            return _directionVectors;
        }
        set => _directionVectors = value;
    }
    private List<Vector2> _directionVectors = new();

    [JsonIgnore]
    /// <summary>Current location of this <see cref="ChessPiece"/> instance within the current board.</summary>
    public Vector2 CurrentLocation
    {
        get => new(CurrentColumn, CurrentRow);
        set
        {
            CurrentRow = (int)value.Y;
            CurrentColumn = (int)value.X;
        }
    }

    /// <summary>Gets or sets the row that the <see cref="ChessPiece"/> instance is currently on.</summary>
    /// <value>The row the <see cref="ChessPiece"/> instance is currently in.</value>
    public int CurrentRow { get; set; }

    /// <summary>Gets or sets the column that the <see cref="ChessPiece"/> instance is currently in.</summary>
    /// <value>The column the <see cref="ChessPiece"/> instance is currently in.</value>
    public int CurrentColumn { get; set; }

    /// <summary>
    /// Gets or sets an enum to describe what type of chess piece this instance will act as.
    /// </summary>
    /// <value>One of (King,Pawn,Queen,Bishop,Rook,Knight).</value>
    public PieceType AssignedType { get; private set; }

    /// <summary>Gets or sets a <see cref="Team"/> enum for the current instance.</summary>
    /// <remarks>Must be either Team.(White/Black)</remarks>
    /// <value>The current Team that this <see cref="ChessPiece"/> is on.</value>
    public Team AssignedTeam
    {
        get => _assignedTeam ?? throw new NullReferenceException($"Attempted to access {nameof(AssignedTeam)}. Backing field {nameof(_assignedTeam)} is null.");
        set { _assignedTeam ??= value; }
    }
    private Team? _assignedTeam = null;

    /// <summary>
    /// Gets or sets a value representing whether or not the current <see cref="ChessPiece"/> instance is vulnerable to En Passant.
    /// </summary>
    /// <value><see langword="true"/> if a pawn can be captured via En Passant; otherwise, <see langword="false"/>.</value>
    public bool CanBeCapturedViaEnPassant
    {
        get => _enPassantCapturePossible;
        private set
        {
            if (AssignedType == PieceType.Pawn) _enPassantCapturePossible = value;
        }
    }
    private bool _enPassantCapturePossible = false;

    /// <summary>Gets a value that describes if the instance can move across the board.</summary>
    /// <value><see langword="true"/> if <see cref="AssignedType"/> is within <see cref="_multiSquareCapable"/>; otherwise, <see langword="false"/>.</value>
    public bool CanMoveAcrossBoard { get => _multiSquareCapable.Contains(AssignedType); }

    /// <summary>
    /// An array of PieceType enums for chess pieces that can move via vector propagation.
    /// </summary>
    private readonly static PieceType[] _multiSquareCapable = { PieceType.Queen, PieceType.Rook, PieceType.Bishop };

    private int _timesMoved;
    /// <summary>Gets or sets the number of times the current <see cref="ChessPiece"/> instance has been moved.</summary>
    /// <value>Integer reprensentation of how many times the current <see cref="ChessPiece"/> instance has been moved.</value>
    /// <remarks>Incremented by 1 whenever moved.</remarks>
    public int TimesMoved
    {
        get => _timesMoved;
        private set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, $"Attempted to set {nameof(TimesMoved)} below 0.");
            _timesMoved = value;
        }
    }

    /// <summary>Location assigned to all captured pieces.</summary>
    public static readonly Vector2 s_capturedLocation = new(-1);

    /// <summary>Gets a value representing whether or not the current <see cref="ChessPiece"/> instance has been captured.</summary>
    /// <value><see langword="true"/> if the <see cref="CurrentLocation"/> Equals <see cref="s_capturedLocation"/> ; otherwise, <see langword="false"/>.</value>
    public bool Captured { get => CurrentLocation.Equals(s_capturedLocation); }

    /// <summary>Gets a string to serve as a key for <see cref="GameEnvironment._teamPieces"/>.</summary>
    public string ID
    {
        get => _id ?? throw new NullReferenceException($"{nameof(_id)} is null.");
        set { _id ??= value; }
    }
    private string? _id = null;
    public bool IsCopy { get; init; } = false;
    //[JsonConstructor]
    public ChessPiece(PieceType assignedType, Coords currentLocation, Team assignedTeam, int? initialColumnNumber = null)
    {
        AssignedType = assignedType;
        CurrentLocation = new Vector2(currentLocation.ColumnIndex, currentLocation.RowIndex);
        AssignedTeam = assignedTeam;

        if (initialColumnNumber != null) ID = ReturnPieceTypeName() + "_" + initialColumnNumber;

        DirectionVectors = AvailableDirectionVectors();
    }
    public ChessPiece()
    {

    }

    /// <summary>
    /// Generates a list of direction vectors for the current <see cref="AssignedType"/>.
    /// </summary>
    /// <returns>A <see cref="List{Vector2}"/> of available direction vectors for the current <see cref="AssignedType"/>.</returns>
    private List<Vector2> AvailableDirectionVectors()
    {
        bool canMoveInAnyDirection = true;
        int initialPawnJump = 0, forwardDirection = 0;

        switch (AssignedType)
        {
            case PieceType.Pawn:
            case PieceType.Rook:
            case PieceType.Bishop:
                if (AssignedType.Equals(PieceType.Pawn))
                {
                    forwardDirection = AssignedTeam.Equals(Team.White) ? 1 : -1;
                    initialPawnJump = 2 * forwardDirection;
                }
                canMoveInAnyDirection = false;
                break;
            case PieceType.Knight:
                return KnightDirectionVectors();
        }

        var directions = new List<Vector2>();

        for (int horizontalScalar = -1; horizontalScalar < 2; horizontalScalar++)
        {
            for (int verticalScalar = -1; verticalScalar < 2; verticalScalar++)
            {
                // Ignore the combination that results in no movement.
                if (horizontalScalar == 0 && verticalScalar == 0) continue;
                // Being equal to 1 implies that one dimension has a value of 0 and is therefore a perpendicular or parallel vector.
                bool vectorIsPerpendicularOrParallel = Math.Abs(verticalScalar) + Math.Abs(horizontalScalar) == 1;

                bool rookVector = (AssignedType == PieceType.Rook) && vectorIsPerpendicularOrParallel;
                bool bishopVector = (AssignedType == PieceType.Bishop) && vectorIsPerpendicularOrParallel == false;
                bool pawnVector = (AssignedType == PieceType.Pawn) && verticalScalar == forwardDirection;

                if (bishopVector || rookVector || pawnVector || canMoveInAnyDirection)
                {
                    directions.Add(new Vector2(horizontalScalar, verticalScalar));

                    if (AssignedType.Equals(PieceType.King) && verticalScalar == 0 && horizontalScalar != 0)
                    {   // This will allow the king to castle in either direction horizontally for castling purposes.
                        directions.Add(new Vector2(2 * horizontalScalar, 0));
                    }
                    else if (AssignedType.Equals(PieceType.Pawn) && horizontalScalar == 0)
                    {   // This will allow pawns to move forward 2 spaces if they haven't been moved yet.
                        directions.Add(new Vector2(horizontalScalar, initialPawnJump));
                    }
                }
            }
        }
        return directions;
    }

    /// <summary>Generates general direction vectors for Knights.</summary>
    /// <returns>A <see cref="List{Vector2}"/> of direction vectors for Knights.</returns>
    public static List<Vector2> KnightDirectionVectors()
    {
        var directions = new List<Vector2>();
        int[] horizontalMovements = { 2, -2, 1, -1 };

        foreach (int horizontalScalar in horizontalMovements)
        {
            int verticalScalar = (Math.Abs(horizontalScalar) == 2) ? 1 : 2;

            for (int i = 0; i < 2; ++i)
            {
                directions.Add(new Vector2(horizontalScalar, verticalScalar));
                verticalScalar *= -1;
            }
        }
        return directions;
    }

    /// <summary>
    /// Creates a copy of the current instance.
    /// </summary>
    /// <returns>A copy of the current <see cref="ChessPiece"/> instance.</returns>
    public ChessPiece Copy()
    {
        return new ChessPiece(AssignedType, new Coords(CurrentRow, CurrentColumn), AssignedTeam)
        {
            CanBeCapturedViaEnPassant = _enPassantCapturePossible,
            ID = ID,
            IsCopy = true
        };
    }

    /// <summary>
    /// Determines if the current instance of a <see cref="ChessPiece"/> object can attack another based on team allegiance.
    /// </summary>
    /// <param name="enemyPiece">This parameter is assigned a value if the piece being compared is hostile to the current instance of the <see cref="ChessPiece"/> class.</param>
    /// <param name="pieceToCompare"><see cref="ChessPiece"/> object that will have its allegiance tested.</param>
    /// <returns><see langword="true"/> if <paramref name="enemyPiece"/> and <paramref name="pieceToCompare"/> are both not null and are on different teams; otherwise, <see langword="false"/>.</returns>
    public bool IsComparedPieceHostile(ChessPiece? pieceToCompare, out ChessPiece? enemyPiece)
    {
        if (pieceToCompare is not null && !OnSameTeam(pieceToCompare))
        {
            enemyPiece = pieceToCompare;
            return true;
        }
        enemyPiece = null;
        return false;
    }

    /// <summary>Changes the <see cref="AssignedType"/> property to <paramref name="newType"/> and assigns new direction vectors.</summary>
    /// <param name="newType">A <see cref="PieceType"/> Enum that is used to designate what this chess piece should be promoted to.</param>
    public void ChangePieceType(PieceType newType)
    {
        AssignedType = newType;
        DirectionVectors = AvailableDirectionVectors();
    }

    /// <summary>Increases the <see cref="TimesMoved"/> property by one.</summary>
    public void IncreaseMovementCount() => ++TimesMoved;

    /// <summary>Decreases the <see cref="TimesMoved"/> property by one.</summary>
    public void DecreaseMovementCount() => --TimesMoved;

    /// <summary>
    /// Toggles <see cref="_enPassantCapturePossible"/> to <see langword="true"/>.
    /// </summary> 
    public void EnableEnPassantCaptures() => CanBeCapturedViaEnPassant = true;

    /// <summary>
    /// Sets <see cref="_enPassantCapturePossible"/> to <see langword="false"/>.
    /// </summary>
    public void DisableEnPassantCaptures() => CanBeCapturedViaEnPassant = false;

    /// <summary>
    /// Use the <see cref="AssignedType"/> and <see cref="AssignedTeam"/> to return a descriptive name for a chess piece.
    /// For example: <example>White_Queen</example>
    /// </summary>
    /// <returns>A string represntation of <see cref="AssignedType"/> and <see cref="AssignedTeam"/>.</returns>
    public string ReturnPieceTypeName() => $"{AssignedTeam}_{AssignedType}";

    /// <summary>Determines if two <see cref="ChessPiece"/> are on the same team.</summary>
    /// <param name="chessPieceToTest">Chess piece that is tested.</param>
    /// <returns><see cref="true"/> if <paramref name="chessPieceToTest"/> has the same <see cref="AssignedTeam"/> value; otherwise, <see cref="false"/>.</returns>
    public bool OnSameTeam(ChessPiece chessPieceToTest) => AssignedTeam == chessPieceToTest.AssignedTeam;

    /// <summary>
    /// Determines if a given <see cref="ChessPiece"/> instance has a <see cref="AssignedType"/> value of <see cref="PieceType.King"/>.
    /// </summary>
    /// <returns><see langword="true"/> if <see cref="ChessPiece"/> instance has a <see cref="AssignedType"/> property of <see cref="PieceType.King"/>; otherwise, <see langword="false"/>.</returns>
    public bool IsKing() => AssignedType == PieceType.King;
}

