
namespace Pieces;

using Chess_GUi;
using static Pieces.Server;

using System.Net.Sockets;
using System.Text.Json;

public class Player
{
    /// <summary>IP address of <see cref="_connectedServer"/>.</summary>
    private readonly string _hostAddress;

    /// <summary>Port to connect to <see cref="_connectedServer"/> on.</summary>
    private readonly int _hostPort;

    /// <summary>Connection to the server if it exists used by the <see cref="Server"/> class.</summary>
    public TcpClient Client
    {
        get { return _client ?? throw new NullReferenceException(nameof(Client)); }
        init => _client = value;
    }
    private TcpClient? _client;

    /// <summary>Server connected to the current instance of the <see cref="Player"/> class.</summary>
    private TcpClient? _connectedServer;

    /// <summary>Stores a list of games that the user is involved in.</summary>
    private readonly Dictionary<int, GameEnvironment> _activeGames = new();
    private bool ServerIsConnected { get; set; } = false;
    public int ServerAssignedID { get; init; }

    /// <summary>Gets the player name assigned to this instance</summary>
    /// <value>The current value of <see cref="_name"/>.</value>
    public string? Name { get => _name; }

    /// <summary>The name assigned to this <see cref="Player"/> instance.</summary>
    private string? _name;

    /// <summary>Static variable used by the <see cref="Player.Player(TcpClient)"/> constructor to generate a value for <see cref="ServerAssignedID"/>.</summary>
    private static int s_instanceCount = 0;

    public readonly CancellationTokenSource MainTokenSource = new();

    private readonly Form1? _gui;

    /// <summary>
    /// Client-side constructor.
    /// </summary>
    public Player(Form1 gui)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        _gui = gui;
    }
    /// <summary>
    /// Constructor used by the <see cref="Server"/> class to track clients.
    /// </summary>
    public Player(TcpClient client)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        Client = client;
        ServerAssignedID = ++s_instanceCount;
    }

    /// <summary>
    /// Asynchonously connects to a server and starts waiting for server responses.
    /// </summary>
    public async Task StartListeningAsync()
    {

        var token = MainTokenSource.Token;

        _connectedServer =  new TcpClient(_hostAddress, _hostPort);

        ServerIsConnected = true;
        // Get a client stream for reading and writing.
        NetworkStream stream = _connectedServer.GetStream();

        try
        {
            var registerCommand = new ServerCommand(CommandType.RegisterUser, name: Name);

            var lfgCommand = new ServerCommand(CommandType.LookingForGame);

            foreach (ServerCommand commandToSend in new ServerCommand[2] { registerCommand, lfgCommand })
            {
                await SendClientMessageAsync(JsonSerializer.Serialize(commandToSend), _connectedServer,MainTokenSource.Token);
            }

            while (!token.IsCancellationRequested)
            {
                string recievedText = await RecieveMessageFromStreamAsync(stream, token);

                if (recievedText == string.Empty) continue;

                ServerCommand? response = JsonSerializer.Deserialize<ServerCommand>(recievedText);

                if (response != null)
                {
                    int serverSideGameID = response.GameIdentifier;

                    if (response.CMD == CommandType.StartGameInstance)
                    {
                        var newGame = new GameEnvironment(serverSideGameID, (Team)response.AssignedTeam!);
                        _activeGames.Add(serverSideGameID, newGame);
                        _gui?.AddGame(newGame);
                    }
                    else if (response.CMD == CommandType.ServerIsShuttingDown)
                    {
                        MainTokenSource.Cancel();
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames.ContainsKey(serverSideGameID))
                    {
                        _activeGames[serverSideGameID].ChangeGameState(GameState.GameDraw);

                        _activeGames.Remove(serverSideGameID);
                        throw new NotImplementedException("Opponent disconnection hasn't been fully implemented.");
                    }
                    else if (response.CMD == CommandType.NewMove && _activeGames.ContainsKey(serverSideGameID))
                    {
                        _activeGames[serverSideGameID].ChangeGameBoardAndGUI(response.MoveDetails!.Value,false);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Host has disconnected. " + e);        
        }
        finally
        {
            stream.Dispose();
            _connectedServer.Close();
            ServerIsConnected = false;
        }
    }
    /// <summary>
    /// Submits a chess movement <paramref name="move"/> to the <see cref="_connectedServer"/> and updates the relevant <see cref="GameEnvironment"/> instance.
    /// </summary>
    /// <param name="move">Chess movement to submit to the server.</param>
    /// <param name="serverSideGameID">ID used to target a specific <see cref="GameEnvironment"/> instance.</param>
    public async Task SubmitMoveToServerAsync(MovementInformation move, int serverSideGameID, CancellationToken? token = null)
    {
        GameEnvironment targetedGameInstance = _activeGames[serverSideGameID];

        if (targetedGameInstance.ActiveTeam == move.SubmittingTeam)
        {
            targetedGameInstance.ChangeGameBoardAndGUI(move,true);

            if (_connectedServer != null && ServerIsConnected)
            {
                string submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, serverSideGameID, move));
                try
                {
                    await SendClientMessageAsync(submissionCommand, _connectedServer,token);
                }
                catch (IOException)
                {
                    ServerIsConnected = false;
                    throw new NotImplementedException("Server not responding handling not implemented.");
                }
            }
            else if (_connectedServer?.Connected == false || ServerIsConnected == false)
            {
                ServerIsConnected = false;
                throw new Exception("Server is no longer connected.");
            }
        }
        else
        {
            throw new Exception("Movement submitted on the wrong turn.");
        }
    }

    /// <summary>
    /// Assigns a name to the player instance.
    /// </summary>
    /// <exception cref="Exception">Thrown if <see cref="Name"/> returns a value that isn't null.</exception>
    public void AssignName(string newName)
    {
        if (Name is null) _name = newName;
        else throw new Exception($"{nameof(_name)} has already been assigned a value.");
    }
}