namespace Pieces;

/// <summary>
/// Enums used by either the Server or Client to help evaluate recieved data.
/// </summary>
public enum CommandType
{
    ClientDisconnecting, NewMove, OpponentClientDisconnected, StartGameInstance, DeclareForefit, LookingForGame, ServerIsShuttingDown,
    DeclareWin, DeclareLoss, DeclareStaleMate, RegisterUser, ServerFull, InvalidMove, WelcomeToServer
}
[Serializable]
public class ServerCommand
{
    /// <summary><see cref="CommandType"/> enum that specifies what this ServerCommand is intended to do.</summary>
    public CommandType CMD { get; set; }
    /// <summary>Optional class field used when <see cref="CMD"/> equals <see cref="CommandType.NewMove"/>.</summary>
    public MovementInformation? MoveDetails { get; set; } = null;
    /// <summary>Optional field used to assign a client-side <see cref="Player"/> to a given team.</summary>
    public Team? AssignedTeam { get; set; } = null;
    /// <summary>Specifies which instance of a <see cref="GameEnvironment"/> is being communicated with.</summary>
    public int GameIdentifier { get; set; } = 0;
    /// <summary>Optional parameter used to assign a name to a <see cref="Player"/> instance.</summary>
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? OpponentName { get; set; }

    public ServerCommand(CommandType cmd, int gameIdentifier = 0, MovementInformation? moveDetails = null, Team? assignedTeam = null, string? name = null, string? message = null, string? opponentName = null)
    {
        CMD = cmd;
        GameIdentifier = gameIdentifier;
        Message = message;
        if (cmd == CommandType.NewMove)
        {
            MoveDetails = moveDetails ?? throw new ArgumentNullException(nameof(moveDetails), "A new move command has been submitted without a non-null MovementInformation struct.");
        }
        else if (cmd == CommandType.StartGameInstance)
        {
            AssignedTeam = assignedTeam ?? throw new ArgumentNullException(nameof(assignedTeam), "Value cannot be null with the given Command Type.");
            OpponentName = opponentName ?? throw new ArgumentNullException(nameof(opponentName), "The opponents name cannot be null if starting a new game.");
        }
        else if (cmd == CommandType.RegisterUser)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name), $"{nameof(name)} value not provided.");
        }
    }
}

