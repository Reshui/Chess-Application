﻿using System.Numerics;
namespace Pieces;

/// <summary>
/// Class used to encapsulate a chess move.
/// </summary>
public class ChessMove
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
    public Vector2 MainNewLocation { get => new(NewMainCoords.ColumnIndex, NewMainCoords.RowIndex); }

    /// <summary>New location to place <see cref="SecondaryCopy"/>.</summary>
    public Vector2? SecondaryNewLocation
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

    /// <summary>Gets a value indicating which <see cref="Team"/> is submitting the <see cref="ChessMove"/> instance.</summary>
    /// <value>The current value of <see cref="MainCopy"/>.<c>AssignedTeam</c>.</value>
    public Team SubmittingTeam => MainCopy.AssignedTeam;

    /// <summary>Gets a value representing if the <see cref="ChessMove"/> instance describes if <see cref="MainCopy"/> is capturing <see cref="SecondaryCopy"/>
    /// via En Passant.</summary>
    /// <value><see langword="true"/> if capturing via En Passant; otherwise, <see langword="false"/>.</value>
    public bool CapturingViaEnPassant
    {
        get
        {
            if (CapturingSecondary && SecondaryCopy is not null && !SecondaryCopy.CurrentLocation.Equals(MainNewLocation) && SecondaryCopy.CanBeCapturedViaEnPassant && MainCopy.AssignedType == PieceType.Pawn)
            {
                return true;
            }
            return false;
        }
    }
    /// <value>
    /// If not null then <see cref="MainCopy"/> will have its <see cref="ChessPiece.AssignedType"/> property replaced with if it is a pawn.
    /// </value>

    private PieceType? _newType;
    public PieceType? NewType
    {
        get => _newType;
        set
        {
            var legalTypes = new PieceType[] { PieceType.Rook, PieceType.Bishop, PieceType.Knight, PieceType.Queen };

            if (PromotingPawn && MainCopy.AssignedType == PieceType.Pawn && legalTypes.Contains((PieceType)value!))
            {
                _newType = value;
            }
            else throw new InvalidOperationException($"{nameof(MainCopy.AssignedType)} must be {nameof(PieceType.Pawn)} to promote.");
        }
    }
    /// <summary>
    /// Gets a value that describes if the <see cref="ChessMove"/> instance involves promoting a pawn.
    /// </summary>
    public bool PromotingPawn { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChessMove"/> class.
    /// </summary>
    /// <param name="mainCopy">Copy of piece doing the move.</param>
    /// <param name="secondaryCopy">Copy or null of secondary piece being acted upon.</param>
    /// <param name="newMainCoords">Coordinates to move <paramref name="mainCopy"/> to.</param>
    /// <param name="newSecondaryCoords">Coordinates to move <paramref name="secondaryCopy"/> to.</param>
    /// <param name="enPassantCapturePossible"><see langword="true"/> if <paramref name="mainCopy"/> is captureable via En Passant; otherwise, ><see langword="false"/>.</param>
    /// <param name="capturingSecondary"><see langword="true"/> if <paramref name="secondaryCopy"/> is being captured; otherwise, <see langword="false"/>.</param>
    /// <param name="castlingWithSecondary"><see langword="true"/> if <paramref name="mainCopy"/> should castle with <paramref name="secondaryCopy"/>; otherwise, <see langword="false"/>   </param>
    /// <param name="newType">PieceType to convert <paramref name="mainCopy"/> to.</param>
    /// <exception cref="ArgumentException"></exception>
    public ChessMove(ChessPiece mainCopy, Coords newMainCoords, ChessPiece? secondaryCopy = null, Coords? newSecondaryCoords = null, bool enPassantCapturePossible = false, bool capturingSecondary = false, bool castlingWithSecondary = false, bool promotingPawn = false, PieceType? newType = null)
    {
        if (mainCopy.IsCopy && (secondaryCopy?.IsCopy ?? true))
        {
            SecondaryCopy = secondaryCopy;
            MainCopy = mainCopy;
        }
        else
        {
            throw new ArgumentException($"Both {nameof(mainCopy)} and {nameof(secondaryCopy)} must have a {nameof(ChessPiece.IsCopy)} property of true");
        }

        if (promotingPawn && mainCopy.AssignedType != PieceType.Pawn) throw new ArgumentException("Cannot promote chess piece", nameof(mainCopy));

        NewMainCoords = newMainCoords;
        NewSecondaryCoords = newSecondaryCoords;
        EnPassantCapturePossible = enPassantCapturePossible;
        CapturingSecondary = capturingSecondary;
        CastlingWithSecondary = castlingWithSecondary;
        _newType = newType;
        PromotingPawn = promotingPawn;
    }
}