
namespace Pieces;

using Chess_GUi;
using static Pieces.Server;
using System.Net.Sockets;
public class Player
{
    /// <summary>IP address of <see cref="_connectedServer"/>.</summary>
    private readonly string? _hostAddress = null;

    /// <summary>Port to connect to <see cref="_connectedServer"/> on.</summary>
    private readonly int? _hostPort = null;

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
    public int ServerAssignedID { get; private set; }

    /// <summary>Gets the player name assigned to this instance</summary>
    public string? Name { get; }

    /// <summary>TokenSource used to stop server listening tasks.</summary>
    public readonly CancellationTokenSource PersonalSource;

    /// <summary>Form object used to visually represent games.</summary>
    private readonly Form1 _gui;

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
    public Player(Form1 gui, string userName, CancellationTokenSource cancelSource, int portToConnectTo, string serverAddress)
    {
        _hostPort = portToConnectTo;
        _hostAddress = serverAddress;
        Name = userName;
        _gui = gui;
        PersonalSource = cancelSource;
    }

    /// <summary>Joins a server and starts asynchronous tasks.</summary>
    /// <returns><see langword="true"/> if server was joined successfully; otherwise, <see langword="false"/>.</returns>
    public bool TryJoinServer()
    {
        try
        {
            _connectedServer = new TcpClient(_hostAddress!, (int)_hostPort!);
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
        AllowedToMessageServer = false;
        // Get a client stream for reading and writing.
        NetworkStream stream = _connectedServer!.GetStream();
        bool serverDeclinedConnectionAttempt = true;
        try
        {
            // Just waiting for welcome message.
            ServerCommand? response = await RecieveCommandFromStreamAsync(stream, token);

            if (response is not null && response.CMD == CommandType.WelcomeToServer)
            {
                AllowedToMessageServer = true;
                serverDeclinedConnectionAttempt = false;
                var gamesToIgnore = new List<int>();
                var registerCommand = new ServerCommand(CommandType.RegisterUser, name: Name);
                var lfgCommand = new ServerCommand(CommandType.LookingForGame);

                foreach (ServerCommand commandToSend in new ServerCommand[2] { registerCommand, lfgCommand })
                {
                    await SendClientMessageAsync(commandToSend, _connectedServer, PersonalSource.Token);//.ConfigureAwait(false);
                }

                while (!token.IsCancellationRequested)
                {
                    response = await RecieveCommandFromStreamAsync(stream, token);//.ConfigureAwait(false);

                    if (response is not null)
                    {
                        int serverSideGameID = response.GameIdentifier;

                        if (response.CMD == CommandType.StartGameInstance)
                        {
                            if (!gamesToIgnore.Contains(serverSideGameID))
                            {
                                var newGame = new GameEnvironment(serverSideGameID, (Team)response.AssignedTeam!);
                                _activeGames.Add(serverSideGameID, newGame);
                                _gui.AddGame(newGame);
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
                        }
                        else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames.ContainsKey(serverSideGameID))
                        {
                            _activeGames[serverSideGameID].ChangeGameState(GameState.OpponentDisconnected);
                            _gui.DisableGame(serverSideGameID);
                            _activeGames.Remove(serverSideGameID);
                        }
                        else if (response.CMD == CommandType.OpponentClientDisconnected && !_activeGames.ContainsKey(serverSideGameID))
                        {
                            gamesToIgnore.Add(serverSideGameID);
                        }
                        else if (response.CMD == CommandType.NewMove && _activeGames.TryGetValue(serverSideGameID, out GameEnvironment? targetedGame))
                        {
                            if (!TryUpdateGameInstance(targetedGame, response.MoveDetails!, guiAlreadyUpdated: false))
                            {
                                var invalidMoveFromOpponent = new ServerCommand(CommandType.InvalidMove, targetedGame.GameID, response.MoveDetails);
                                await SendClientMessageAsync(invalidMoveFromOpponent, _connectedServer, PersonalSource.Token);//.ConfigureAwait(false);
                            }
                        }
                        else if (response.CMD == CommandType.InvalidMove && _activeGames.Remove(serverSideGameID, out targetedGame))
                        {
                            targetedGame.ChangeGameState(GameState.GameDraw);
                            _gui.DisableGame(targetedGame.GameID);
                        }
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
                await CloseConnectionToServerAsync(userIsQuitting: false, calledFromListeningTask: true, serverDeniedConnection: serverDeclinedConnectionAttempt).ConfigureAwait(false);
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
        bool success = false;
        if (targetGame.SubmitFinalizedChange(newMove))
        {
            if (!guiAlreadyUpdated) _gui.UpdateGameInterface(newMove, targetGame.GameID);
            success = true;
        }
        else
        {
            targetGame.ChangeGameState(GameState.GameDraw);
        }
        if (targetGame.GameEnded) _gui.DisableGame(targetGame.GameID);
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
                if (_connectedServer is not null && _connectedServer.Connected && AllowedToMessageServer)
                {
                    var submissionCommand = new ServerCommand(CommandType.NewMove, serverSideGameID, move);
                    try
                    {
                        await SendClientMessageAsync(submissionCommand, _connectedServer, PersonalSource.Token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is ObjectDisposedException or OperationCanceledException or NullReferenceException)
                    {
                        // ObjectDisposedException or NullReferenceException => CloseConnectionToServerAsync() was called.
                        // OperationCancelledException No longer want/aable to message server.
                    }
                    catch (Exception e) when (e is IOException || e is InvalidOperationException)
                    {
                        targetedGameInstance.ChangeGameState(GameState.ServerUnavailable);
                        AllowedToMessageServer = false;
                        await CloseConnectionToServerAsync(false, false, false).ConfigureAwait(false);
                        throw new IOException("Unable to contact server.", e);
                    }
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
    /// Asynchronously alerts <see cref="_connectedServer"/> that the user wants to quit.
    /// </summary>
    public async Task CloseConnectionToServerAsync(bool userIsQuitting, bool calledFromListeningTask, bool serverDeniedConnection)
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
                    await SendClientMessageAsync(notifyServerCommand, _connectedServer, CancellationToken.None);
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
                    await _listenForServerTask;
                }

                if (PingConnectedClientTask is not null)
                {
                    await PingConnectedClientTask;
                }

                PersonalSource.Dispose();
                _connectedServer.Close();
                _connectedServer = null;
                if (!UserWantsToQuit && !serverDeniedConnection) _gui?.ServerIsUnreachable();
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