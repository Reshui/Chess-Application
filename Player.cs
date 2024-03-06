
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
    private bool PermitAccessToServer { get; set; } = false;
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
    public readonly CancellationTokenSource MainTokenSource;

    /// <summary>Form object used to visually represent games.</summary>
    private readonly Form1? _gui;

    /// <summary>List used to track long-running asynchronous tasks started by the Player instance.</summary>
    private readonly List<Task>? _asyncListeningTask;

    /// <summary>
    /// Gets or sets a boolean that describes if the user wants to quit playing.
    /// </summary>
    /// <value><see langword="true"/> if <see cref="CloseConnectionToServerAsync()"/> has been called; otherwise, <see langword="false"/>.</value>
    public bool UserWantsToQuit { get; private set; } = false;
    /// <summary>
    /// Client-side constructor.
    /// </summary>
    public Player(Form1 gui, CancellationTokenSource cancelSource)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        _asyncListeningTask = new();
        _gui = gui;
        MainTokenSource = cancelSource;
    }
    /// <summary>
    /// Constructor used by the <see cref="Server"/> class to track clients.
    /// </summary>
    public Player(TcpClient client, CancellationTokenSource cancelSource)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        Client = client;
        ServerAssignedID = ++s_instanceCount;
        MainTokenSource = cancelSource;
    }
    /// <summary>Joins a server and starts asynchronous tasks.</summary>
    /// <returns><see langword="true"/> if server was joined successfully; otherwise, <see langword="false"/>.</returns>
    public bool JoinServer()
    {
        try
        {
            _connectedServer = new TcpClient(_hostAddress, _hostPort);
        }
        catch (SocketException)
        {
            return false;
        }
        _asyncListeningTask?.Add(StartListeningAsync());
        return true;
    }

    /// <summary>
    /// Asynchonously connects to a server and starts waiting for server responses.
    /// </summary>
    private async Task StartListeningAsync()
    {
        var token = MainTokenSource.Token;
        PermitAccessToServer = true;
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
                        PermitAccessToServer = false;
                        throw new IOException("Shutdwon command recieved.");
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames.ContainsKey(serverSideGameID))
                    {
                        _activeGames[serverSideGameID].ChangeGameState(GameState.OpponentDisconnected);
                        _gui?.DisableGame(serverSideGameID);
                        _activeGames.Remove(serverSideGameID);
                    }
                    else if (response.CMD == CommandType.NewMove && _activeGames.TryGetValue(serverSideGameID, out GameEnvironment? targetedGame))
                    {
                        UpdateGameInstance(targetedGame, response.MoveDetails!.Value, false);
                    }
                }
            }
        }
        catch (IOException)
        {   // Couldn't reach host.
            Console.WriteLine("Host has disconnected.");
        }
        catch (OperationCanceledException)
        {
            // User has decided to disconnect using CloseConnectionToServerAsync().
            // Server has already been notified or will be.
        }
        finally
        {
            PermitAccessToServer = false;
            // Function has already been called if true.
            if (!UserWantsToQuit) await CloseConnectionToServerAsync(false, true);
        }
    }

    /// <summary>
    /// Submits move to Local game instance.
    /// </summary>
    /// <param name="targetGame">Game that chanes will target.</param>
    /// <param name="newMove">MovementInformation used to update <paramref name="targetGame"/>.</param>
    /// <param name="guiAlreadyUpdated"><see langword="true"/> if the GameBoard doesn't need to visually updated to reflect <paramref name="newMove"/>; otherwise, <see langword="false"/>.</param>
    private void UpdateGameInstance(GameEnvironment targetGame, MovementInformation newMove, bool guiAlreadyUpdated)
    {
        targetGame.SubmitFinalizedChange(newMove, piecesAlreadyMovedOnGUI: guiAlreadyUpdated);
        if (targetGame.GameEnded)
        {
            _gui?.DisableGame(targetGame.GameID);
        }
    }
    /// <summary>
    /// Submits a chess movement <paramref name="move"/> to the <see cref="_connectedServer"/> and updates the relevant <see cref="GameEnvironment"/> instance.
    /// </summary>
    /// <param name="move">Chess movement to submit to the server.</param>
    /// <param name="serverSideGameID">ID used to target a specific <see cref="GameEnvironment"/> instance.</param>
    /// <exception cref="IOException">The server can no longer be reached.</exception>
    /// <exception cref="InvalidOperationException">Attempted to submit move when it isn't this player's turn.</exception> 
    public async Task SubmitMoveToServerAsync(MovementInformation move, int serverSideGameID)
    {
        if (_activeGames.TryGetValue(serverSideGameID, out GameEnvironment? targetedGameInstance))
        {
            if (targetedGameInstance.ActiveTeam == move.SubmittingTeam)
            {
                UpdateGameInstance(targetedGameInstance, move, guiAlreadyUpdated: true);

                if (_connectedServer is not null && PermitAccessToServer)
                {
                    string submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, serverSideGameID, move));
                    try
                    {
                        await SendClientMessageAsync(submissionCommand, _connectedServer, MainTokenSource.Token);
                    }
                    catch (Exception e) when (e is IOException || e is OperationCanceledException || e is NullReferenceException || e is InvalidOperationException)
                    {
                        targetedGameInstance.ChangeGameState(GameState.ServerUnavailable);
                        IOException? newIOException = null;
                        if (e is IOException || e is NullReferenceException || e is InvalidOperationException)
                        {
                            // Server can't be reached.
                            PermitAccessToServer = false;
                            newIOException = new IOException("Unable to contact server.", e);
                        }
                        else if (!UserWantsToQuit && e is OperationCanceledException)
                        {
                            // Token was cancelled in either CloseConnectionToServerAsync().
                            newIOException = new IOException("The server is shutting down.", e);
                        }

                        if (newIOException is not null)
                        {
                            await CloseConnectionToServerAsync(false, false);
                            throw newIOException;
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Token was disposed of in CloseConnectionToServerAsync() and server will be shutdown and user notified.
                    }
                    /*catch (InvalidOperationException e)
                    {
                        // Stream connection is probably closed due to token invoke.
                        Console.WriteLine(e.Message);
                    }*/
                }
                else if ((_connectedServer?.Connected ?? false) == false || PermitAccessToServer == false)
                {
                    PermitAccessToServer = false;
                    throw new IOException("Server is no longer connected.");
                }
            }
            else
            {
                throw new InvalidOperationException("Movement submitted on the wrong turn.");
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
    public async Task CloseConnectionToServerAsync(bool userIsQuitting, bool calledFromListeningTask)
    {
        UserWantsToQuit = userIsQuitting;
        if (_connectedServer is not null)
        {
            try
            {   // Command is sent first rather than at the end of client listening because, 
                // when the token is invoked the stream cannot be sent any more messages.
                if (PermitAccessToServer && _connectedServer.Connected)
                {
                    string notifyServerCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.ClientDisconnecting));
                    await SendClientMessageAsync(notifyServerCommand, _connectedServer, CancellationToken.None);
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("Exception generated when sending disconnect to server.  " + e.ToString());
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine(e.Message);
            }
            finally
            {
                MainTokenSource.Cancel();
                // If this method wasn't called from StartListeningAsync() then wait for that Task to finish.
                if (!calledFromListeningTask)
                {
                    if (_asyncListeningTask is not null && _asyncListeningTask.Count > 0) await Task.WhenAll(_asyncListeningTask);
                }
                MainTokenSource.Dispose();
                _connectedServer.Close();
                _connectedServer = null;
            }
            if (!UserWantsToQuit) _gui?.ServerIsUnreachable();
        }
    }
}