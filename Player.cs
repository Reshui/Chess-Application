
namespace Pieces;

using Chess_GUi;
using static Pieces.Server;
using System.Net.Sockets;
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
    /// <summary>
    /// Gets or sets a value that will limit the user from sending messages to <see cref="_connectedServer"/>.
    /// </summary>
    /// <value><see langword="true"/> if messages shouldn't be sent to the server; otherwise, <see langword="false"/>.</value>
    private bool AllowedToMessageServer { get; set; } = false;
    /// <summary>ID used by server to track players.</summary>
    public int ServerAssignedID { get; init; }

    /// <summary>Gets the player name assigned to this instance</summary>
    /// <value>The current value of <see cref="_name"/>.</value>
    public string? Name { get => _name; }

    /// <summary>The name assigned to this <see cref="Player"/> instance.</summary>
    private string? _name = null;

    /// <summary>Static variable used by the server side constructor to generate a value for <see cref="ServerAssignedID"/>.</summary>
    private static int s_instanceCount = 0;

    /// <summary>TokenSource used to stop server listening tasks.</summary>
    public readonly CancellationTokenSource PersonalSource;
    public readonly CancellationTokenSource? CombinedSource = null;

    /// <summary>Form object used to visually represent games.</summary>
    private readonly Form1? _gui;

    /// <summary>List used to track long-running asynchronous tasks started by the Player instance.</summary>
    private Task? _listenForServerTask;
    public Task? PingConnectedClientTask { get; set; }

    /// <summary>
    /// Gets or sets a boolean that describes if the user wants to quit playing.
    /// </summary>
    /// <value><see langword="true"/> if <see cref="CloseConnectionToServerAsync"/> has been called from <see cref="_gui"/>; otherwise, <see langword="false"/>.</value>
    public bool UserWantsToQuit { get; private set; } = false;
    /// <summary>
    /// Client-side constructor.
    /// </summary>
    public Player(Form1 gui, CancellationTokenSource cancelSource)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        _gui = gui;
        PersonalSource = cancelSource;
    }
    /// <summary>
    /// Constructor used by the <see cref="Server"/> class to track clients.
    /// </summary>
    public Player(TcpClient client, CancellationTokenSource personalSource, CancellationTokenSource combinedSource)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        Client = client;
        ServerAssignedID = ++s_instanceCount;
        PersonalSource = personalSource;
        CombinedSource = combinedSource;
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
        PingConnectedClientTask = PingClientAsync(_connectedServer, PersonalSource, PersonalSource.Token);
        _listenForServerTask = ListenForServerResponseAsync();
        return true;
    }

    /// <summary>
    /// Asynchonously connects to a server and starts waiting for server responses.
    /// </summary>
    private async Task ListenForServerResponseAsync()
    {
        var token = PersonalSource.Token;
        AllowedToMessageServer = true;
        // Get a client stream for reading and writing.
        NetworkStream stream = _connectedServer!.GetStream();

        try
        {
            var gamesToIgnore = new List<int>();
            var registerCommand = new ServerCommand(CommandType.RegisterUser, name: Name);
            var lfgCommand = new ServerCommand(CommandType.LookingForGame);

            foreach (ServerCommand commandToSend in new ServerCommand[2] { registerCommand, lfgCommand })
            {
                await SendClientMessageAsync(commandToSend, _connectedServer, PersonalSource.Token);//.ConfigureAwait(false);
            }

            while (!token.IsCancellationRequested)
            {
                ServerCommand? response = await RecieveCommandFromStreamAsync(stream, token);//.ConfigureAwait(false);

                if (response is not null)
                {
                    int serverSideGameID = response.GameIdentifier;

                    if (response.CMD == CommandType.StartGameInstance)
                    {
                        if (!gamesToIgnore.Contains(serverSideGameID))
                        {
                            // Create a GameEnvironmentInstance, track it and send to the main GUi as well.
                            var newGame = new GameEnvironment(serverSideGameID, (Team)response.AssignedTeam!);
                            _activeGames.Add(serverSideGameID, newGame);
                            _gui?.AddGame(newGame);
                        }
                        else
                        {
                            gamesToIgnore.Remove(serverSideGameID);
                        }
                    }
                    else if (response.CMD == CommandType.ServerIsShuttingDown)
                    {
                        AllowedToMessageServer = false;
                        PersonalSource.Cancel();
                        Console.WriteLine("Host has disconnected.");
                        break;
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames.ContainsKey(serverSideGameID))
                    {
                        _activeGames[serverSideGameID].ChangeGameState(GameState.OpponentDisconnected);
                        _gui?.DisableGame(serverSideGameID);
                        _activeGames.Remove(serverSideGameID);
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && !_activeGames.ContainsKey(serverSideGameID))
                    {
                        gamesToIgnore.Add(serverSideGameID);
                    }
                    else if (response.CMD == CommandType.NewMove && _activeGames.TryGetValue(serverSideGameID, out GameEnvironment? targetedGame))
                    {
                        if (!TryUpdateGameInstance(targetedGame, response.MoveDetails!.Value, guiAlreadyUpdated: false))
                        {
                            var invalidMoveFromOpponent = new ServerCommand(CommandType.InvalidMove, targetedGame.GameID, response.MoveDetails.Value);
                            await SendClientMessageAsync(invalidMoveFromOpponent, _connectedServer, PersonalSource.Token);//.ConfigureAwait(false);
                        }
                    }
                    else if (response.CMD == CommandType.InvalidMove && _activeGames.Remove(serverSideGameID, out targetedGame))
                    {
                        targetedGame.ChangeGameState(GameState.GameDraw);
                        _gui?.DisableGame(targetedGame.GameID);
                    }
                }
            }
        }
        catch (IOException)
        {   // Couldn't reach host.
            Console.WriteLine("Host can't be reached.");
            AllowedToMessageServer = false;
        }
        catch (OperationCanceledException)
        {
            // Unable to reach server in ping task.
            if (!UserWantsToQuit) AllowedToMessageServer = false;
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            // Function has already been called if true.
            if (!UserWantsToQuit)
            {
                await CloseConnectionToServerAsync(userIsQuitting: false, calledFromListeningTask: true).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Submits move to Local game instance.
    /// </summary>
    /// <param name="targetGame">Game that chanes will target.</param>
    /// <param name="newMove">MovementInformation used to update <paramref name="targetGame"/>.</param>
    /// <param name="guiAlreadyUpdated"><see langword="true"/> if the GameBoard doesn't need to visually updated to reflect <paramref name="newMove"/>; otherwise, <see langword="false"/>.</param>
    private bool TryUpdateGameInstance(GameEnvironment targetGame, MovementInformation newMove, bool guiAlreadyUpdated)
    {
        bool success = targetGame.SubmitFinalizedChange(newMove, piecesAlreadyMovedOnGUI: guiAlreadyUpdated);
        if (!success) targetGame.ChangeGameState(GameState.GameDraw);
        if (targetGame.GameEnded) _gui?.DisableGame(targetGame.GameID);
        return success;
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
            if (TryUpdateGameInstance(targetedGameInstance, move, guiAlreadyUpdated: true))
            {
                if (_connectedServer is not null && AllowedToMessageServer)
                {
                    var submissionCommand = new ServerCommand(CommandType.NewMove, serverSideGameID, move);
                    try
                    {
                        await SendClientMessageAsync(submissionCommand, _connectedServer, PersonalSource.Token).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Token was disposed of in CloseConnectionToServerAsync() and server will be shutdown and user notified.
                    }
                    catch (Exception e) when (e is IOException || e is OperationCanceledException || e is NullReferenceException || e is InvalidOperationException)
                    {
                        targetedGameInstance.ChangeGameState(GameState.ServerUnavailable);
                        IOException? newIOException = null;
                        if (e is IOException || e is NullReferenceException || e is InvalidOperationException)
                        {
                            // Server can't be reached.
                            AllowedToMessageServer = false;
                            newIOException = new IOException("Unable to contact server.", e);
                        }
                        else if (!UserWantsToQuit && e is OperationCanceledException)
                        {
                            // Token was cancelled in either CloseConnectionToServerAsync().
                            newIOException = new IOException("The server is shutting down.", e);
                        }

                        if (newIOException is not null)
                        {
                            await CloseConnectionToServerAsync(false, false).ConfigureAwait(false);
                            throw newIOException;
                        }
                    }
                    /*catch (InvalidOperationException e)
                    {
                        // Stream connection is probably closed due to token invoke.
                        Console.WriteLine(e.Message);
                    }*/
                }
                else if ((_connectedServer?.Connected ?? false) == false || AllowedToMessageServer == false)
                {
                    AllowedToMessageServer = false;
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
    /// Asynchronously alerts <see cref="_connectedServer"/> that the user wants to quit.
    /// </summary>
    public async Task CloseConnectionToServerAsync(bool userIsQuitting, bool calledFromListeningTask)
    {
        UserWantsToQuit = userIsQuitting;
        if (_connectedServer is not null)
        {
            try
            {   // Command is sent first rather than at the end of client listening because, 
                // when the token is invoked the stream cannot be sent any more messages.                
                if (AllowedToMessageServer)
                {
                    var notifyServerCommand = new ServerCommand(CommandType.ClientDisconnecting);
                    await SendClientMessageAsync(notifyServerCommand, _connectedServer, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("Exception generated when sending disconnect to server.  " + e.ToString());
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine("Failed to notify server of disconnect. " + e.Message);
            }
            finally
            {
                AllowedToMessageServer = false;
                PersonalSource.Cancel();
                // If this method wasn't called from ListenForServerResponseAsync() then wait for that Task to finish.
                if (!calledFromListeningTask && _listenForServerTask is not null)
                {
                    await _listenForServerTask.ConfigureAwait(false);
                }

                if (PingConnectedClientTask is not null)
                {
                    await PingConnectedClientTask.ConfigureAwait(false);
                }

                PersonalSource.Dispose();
                _connectedServer.Close();
                _connectedServer = null;
                if (!UserWantsToQuit) _gui?.ServerIsUnreachable();
            }
        }
    }
    public async Task JoinWaitingLobby()
    {
        if (_connectedServer?.Connected ?? false && !PersonalSource.IsCancellationRequested && AllowedToMessageServer)
        {
            var lfgCommand = new ServerCommand(CommandType.LookingForGame);
            await SendClientMessageAsync(lfgCommand, _connectedServer, PersonalSource.Token).ConfigureAwait(false);
        }
    }
}