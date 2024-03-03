
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
        get { return _client ?? throw new NullReferenceException(nameof(_client)); }
        init => _client = value;
    }
    private TcpClient? _client;

    /// <summary>Server connected to the current <see cref="Player"/> instance.</summary>
    private TcpClient? _connectedServer;

    /// <summary>Stores a list of games that the user is involved in.</summary>
    private readonly Dictionary<int, GameEnvironment> _activeGames = new();
    private bool ServerIsConnected { get; set; } = false;
    /// <summary>ID used by server to track players.</summary>
    public int ServerAssignedID { get; init; }

    /// <summary>Gets the player name assigned to this instance</summary>
    /// <value>The current value of <see cref="_name"/>.</value>
    public string? Name { get => _name; }

    /// <summary>The name assigned to this <see cref="Player"/> instance.</summary>
    private string? _name = null;

    /// <summary>Static variable used by the <see cref="Player(TcpClient)"/> constructor to generate a value for <see cref="ServerAssignedID"/>.</summary>
    private static int s_instanceCount = 0;

    /// <summary>TokenSource used to stop server listening tasks.</summary>
    public readonly CancellationTokenSource MainTokenSource = new();

    /// <summary>Form object used to visually represent games.</summary>
    private readonly Form1? _gui;

    /// <summary>List used to track long-running asynchronous tasks started by the Player instance.</summary>
    private List<Task>? _asyncListeningTask;

    /// <summary>
    /// Client-side constructor.
    /// </summary>
    public Player(Form1 gui)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        _asyncListeningTask = new();
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
    /// <summary>Joins a server and starts asynchronous tasks.</summary>
    /// <returns><see langword="true"/> if server was joined successfully; otherwise, <see langword="false"/>.</returns>
    public bool JoinServer()
    {
        try
        {
            _connectedServer = new TcpClient(_hostAddress, _hostPort);
            _asyncListeningTask?.Add(StartListeningAsync());
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asynchonously connects to a server and starts waiting for server responses.
    /// </summary>
    private async Task StartListeningAsync()
    {
        var token = MainTokenSource.Token;
        ServerIsConnected = true;
        // Get a client stream for reading and writing.
        using NetworkStream stream = _connectedServer!.GetStream();

        try
        {
            var registerCommand = new ServerCommand(CommandType.RegisterUser, name: Name);
            var lfgCommand = new ServerCommand(CommandType.LookingForGame);

            foreach (ServerCommand commandToSend in new ServerCommand[2] { registerCommand, lfgCommand })
            {
                await SendClientMessageAsync(JsonSerializer.Serialize(commandToSend), _connectedServer, MainTokenSource.Token);
            }

            while (!token.IsCancellationRequested)
            {
                ServerCommand response = await RecieveCommandFromStreamAsync(stream, token);

                if (response is not null)
                {
                    int serverSideGameID = response.GameIdentifier;

                    if (response.CMD == CommandType.StartGameInstance)
                    {   // Create a GameEnvironmentInstance, track it and send to the main GUi as well.
                        var newGame = new GameEnvironment(serverSideGameID, (Team)response.AssignedTeam!);
                        _activeGames.Add(serverSideGameID, newGame);
                        _gui?.AddGame(newGame);
                    }
                    else if (response.CMD == CommandType.ServerIsShuttingDown)
                    {
                        MainTokenSource.Cancel();
                        throw new IOException("Shutdwon command recieved.");
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames.ContainsKey(serverSideGameID))
                    {
                        _activeGames[serverSideGameID].ChangeGameState(GameState.GameDraw);
                        _activeGames.Remove(serverSideGameID);
                        //throw new NotImplementedException("Opponent disconnection hasn't been fully implemented.");
                    }
                    else if (response.CMD == CommandType.NewMove && _activeGames.ContainsKey(serverSideGameID))
                    {
                        _activeGames[serverSideGameID].ChangeGameBoardAndGUI(response.MoveDetails!.Value, false);
                    }
                }
            }
        }
        catch (IOException e)
        {   // Couldn't reach host.
            Console.WriteLine("Host has disconnected. " + e);
        }
        catch (OperationCanceledException)
        {

        }
        finally
        {
            ServerIsConnected = false;
            _connectedServer.Close();
            _connectedServer = null;
        }
    }

    /// <summary>
    /// Submits a chess movement <paramref name="move"/> to the <see cref="_connectedServer"/> and updates the relevant <see cref="GameEnvironment"/> instance.
    /// </summary>
    /// <param name="move">Chess movement to submit to the server.</param>
    /// <param name="serverSideGameID">ID used to target a specific <see cref="GameEnvironment"/> instance.</param>
    public async Task SubmitMoveToServerAsync(MovementInformation move, int serverSideGameID, CancellationToken? token = null)
    {
        if (_activeGames.TryGetValue(serverSideGameID, out GameEnvironment? targetedGameInstance))
        {
            if (targetedGameInstance.ActiveTeam == move.SubmittingTeam)
            {
                targetedGameInstance.ChangeGameBoardAndGUI(move, piecesAlreadyMovedOnGUI: true);

                if (_connectedServer is not null)
                {
                    string submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, serverSideGameID, move));
                    try
                    {
                        await SendClientMessageAsync(submissionCommand, _connectedServer, token);
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
    }

    /// <summary>
    /// Assigns a name to the player instance.
    /// </summary>
    /// <param name="newName">This string will be used to assign a value to the <see cref="Name"/> property.</param>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Name"/> returns a value that isn't null.</exception>
    public void AssignName(string newName)
    {
        if (_name is null) _name = newName;
        else throw new InvalidOperationException($"{nameof(_name)} has already been assigned a value.");
    }

    /// <summary>
    /// Asynchronously alerts <see cref="_connectedServer"/> and stops listening to responses.
    /// </summary>
    public async Task CloseConnectionToServerAsync()
    {
        if (_connectedServer is not null)
        {
            try
            {   // Command is sent first rather than at the end of client listening because, 
                // when the token is invoked the stream cannot be sent any more messages.
                string notifyServerCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.ClientDisconnecting));
                await SendClientMessageAsync(notifyServerCommand, _connectedServer, null);
            }
            catch (IOException e)
            {
                Console.WriteLine("Exception generated when sending disconnect to server.  " + e.ToString());
            }
            finally
            {
                MainTokenSource.Cancel();
                if (_asyncListeningTask is not null && _asyncListeningTask.Count > 0) await Task.WhenAll(_asyncListeningTask);
                MainTokenSource.Dispose();
            }
        }
    }
}