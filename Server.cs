namespace Pieces;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System;
using System.Collections.Concurrent;
public class Server
{
    /// <summary>Boolean representation of whether or not the <see cref="Server"/> instance is accepting new clients.</summary>
    private bool _acceptingNewClients { get; set; } = false;
    /// <summary>List of connected <c>TcpClients</c> to the current <see cref="Server"/> instance.</summary>
    private ConcurrentDictionary<int, Player> _connectedPlayers = new();
    /// <summary>Listens for user responses and connections.</summary>
    private TcpListener _gameServer { get; init; }
    /// <summary>Dictionary of <c>GameEnvironment</c> instances that have been started.</summary>
    private ConcurrentDictionary<int, GameEnvironment> _startedGames = new();
    /// <summary>Stores players that are currently waiting for a game.</summary>
    private ConcurrentQueue<Player> _waitingForGameLobby = new();
    /// <summary>Stores Tasks that listen for <see cref="Player"/> responses.</summary>
    private ConcurrentDictionary<int, Task> _clientListeningTasks = new();
    /// <summary>Dictionary of Cancellation Tokens keyed to a given user</summary>
    private ConcurrentDictionary<int, CancellationTokenSource> _clientListeningCancelationTokens = new();
    public CancellationTokenSource ServerShutDownCancelSource { get; } = new();
    /// <summary><see cref="ServerShutDownCancelSource"/>'s <see cref="CancellationToken"/>,</summary>
    private CancellationToken _mainCancelationToken { get => ServerShutDownCancelSource.Token; }

    /// <summary>List that holds the asynchronous tasks started in <see cref="StartServer()"/>.</summary>
    private readonly List<Task> _serverTasks = new() { Capacity = 2 };
    private static readonly CommandType[] _endGameCommands = new CommandType[3] { CommandType.DeclareLoss, CommandType.DeclareWin, CommandType.DeclareStaleMate };
    public enum CommandType
    {
        ClientDisconnecting,
        NewMove,
        OpponentClientDisconnected,
        StartGameInstance,
        DeclareForefit,
        LookingForGame,
        ServerIsShuttingDown,
        DeclareWin,
        DeclareLoss,
        DeclareStaleMate


    }
    public class ServerCommand
    {
        /// <summary><see cref="CommandType"/> enum that specifies what this ServerCommand is intended to do.</summary>
        public CommandType CMD { get; init; }
        /// <summary>Optional class field used when <see cref="CMD"/> equals <see cref="CommandType.NewMove"/>.</summary>
        public MovementInformation? MoveDetails { get; init; } = null;
        /// <summary>Optional field used to assign a client-side <see cref="Player"/> to a given team.</summary>
        public Team? AssignedTeam { get; init; } = null;
        /// <summary>Specifies which instance of a <see cref="GameEnvironment"/> is being communicated with.</summary>
        public int GameIdentifier { get; init; } = 0;

        public ServerCommand(CommandType cmdType, int gameID = 0, MovementInformation? moveDetails = null, Team? assignedTeam = null)
        {
            CMD = cmdType;
            GameIdentifier = gameID;

            if (cmdType == CommandType.NewMove)
            {
                MoveDetails = moveDetails ?? throw new ArgumentNullException(nameof(moveDetails), "A new move command has been submitted without a non-null MovementInformation struct.");
            }
            else if (cmdType == CommandType.StartGameInstance)
            {
                AssignedTeam = assignedTeam ?? throw new ArgumentNullException(nameof(assignedTeam), "Value cannot be null with the given Command Type.");
            }
        }
    }
    public Server()
    {
        // Set the TcpListener on port 13000.
        int port = 13000;
        IPAddress localAddr = IPAddress.Parse("127.0.0.1");
        // TcpListener server = new TcpListener(port);
        _gameServer = new TcpListener(localAddr, port);
        _acceptingNewClients = true;
    }
    public void StartServer()
    {
        _serverTasks.Add(WaitForClientsAsync());
        _serverTasks.Add(MonitorLFGLobbyAsync());
    }

    /// <summary>
    /// Stops all server tasks and notifies connected <c>TcpClient</c>s that the server has shut down.
    /// </summary>
    public async Task CloseServerAsync()
    {
        try
        {
            ServerShutDownCancelSource.Cancel();
            await Task.WhenAll(_serverTasks);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        finally
        {
            ServerShutDownCancelSource.Dispose();
        }

        await BrodcastServerShutDown();
    }
    /// <summary>
    /// Monitors <see cref="_waitingForGameLobby"/> for added <see cref="Player"/> instances and notifies users when a game is available.
    /// </summary>
    private async Task MonitorLFGLobbyAsync()
    {
        List<Player> matchedPlayers = new() { Capacity = 2 };

        while (!_mainCancelationToken.IsCancellationRequested)
        {
            if (_waitingForGameLobby.TryDequeue(out Player? waitingPlayer))
            {
                matchedPlayers.Add(waitingPlayer);

                if (matchedPlayers.Count == 2)
                {
                    List<Task<bool>> clientPingingTasks = new();
                    List<Task> clientRemovalTasks = new();
                    foreach (Player user in matchedPlayers) { clientPingingTasks.Add(IsClientActiveAsync(user.Client)); }

                    while (clientPingingTasks.Count > 0)
                    {
                        Task<bool> completedTask = await Task.WhenAny(clientPingingTasks);
                        if (!completedTask.Result)
                        {
                            int index = clientPingingTasks.IndexOf(completedTask);
                            Player user = matchedPlayers[index];
                            matchedPlayers.RemoveAt(index);
                            clientRemovalTasks.Add(ClientRemovalAsync(user));
                        }
                        clientPingingTasks.Remove(completedTask);
                    }

                    if (clientRemovalTasks.Count > 0) await Task.WhenAll(clientRemovalTasks);

                    if (matchedPlayers.Count == 2)
                    {
                        var newGame = new GameEnvironment(matchedPlayers[0], matchedPlayers[1]);

                        foreach (KeyValuePair<Team, Player> playerDetail in newGame.AssociatedPlayers)
                        {
                            var clientCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);
                            bool cancelationTokenAvailable = _clientListeningCancelationTokens.TryGetValue(playerDetail.Value.ServerAssignedID, out CancellationTokenSource? cancelSource);

                            if (cancelationTokenAvailable && cancelSource != null)
                            {
                                try
                                {
                                    await SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), playerDetail.Value.Client!, cancelSource.Token);
                                }
                                catch (Exception e) when (e is TaskCanceledException || e is IOException)
                                {   // Failed to message client or client is leaving the server.
                                    matchedPlayers.Remove(playerDetail.Value);
                                }
                            }
                        }
                        // If they are no problems with notifying both players to start the game then track the game.
                        if (matchedPlayers.Count == 2)
                        {
                            _startedGames.TryAdd(newGame.GameID, newGame);
                            matchedPlayers.Clear();
                        }
                        else if (matchedPlayers.Count == 1)
                        {
                            var notifyOpponentDisconnectCommand = new ServerCommand(CommandType.OpponentClientDisconnected);
                        }
                    }
                }
            }
            await Task.Delay(700, _mainCancelationToken);
        }
    }
    /// <summary>
    /// Asynchronously waits for <see cref="TcpClient"/> connections to <see cref="_gameServer"/>.
    /// </summary>
    private async Task WaitForClientsAsync()
    {
        // Start listening for client requests.
        _gameServer.Start();

        try
        {
            while (true)
            {
                TcpClient? newClient = await _gameServer.AcceptTcpClientAsync(_mainCancelationToken);

                if (newClient != null)
                {
                    Player newPlayer = new Player(newClient);
                    var cancelationSource = new CancellationTokenSource();

                    _connectedPlayers.TryAdd(newPlayer.ServerAssignedID, newPlayer);
                    _clientListeningCancelationTokens.TryAdd(newPlayer.ServerAssignedID, cancelationSource);
                    _clientListeningTasks.TryAdd(newPlayer.ServerAssignedID, ProcessClientDataAsync(newPlayer, cancelationSource.Token));
                }
            }
        }
        catch (TaskCanceledException)
        {   // Error raised if _mainCancelationToken.IsCancelRequested = true .
            Console.WriteLine($"Locally hosted server:{nameof(_gameServer)}, is shutting down.");
        }
        finally
        {   /// The above tasks will be dealt with when ClienTRemovalAsync is called when shutting down the server. 
            _gameServer.Stop();
        }
    }
    /// <summary>
    /// Asynchronously tests if a given <paramref name="client"/> is still responsive.
    /// </summary>
    /// <returns><see cref="Task<bool>"/> <see langword="true"/> if socket is still connected; otherwise, <see langword="false"/>.</returns>
    /// <param name="client"><see cref="TcpClient"/> that is tested.</param>
    private static async Task<bool> IsClientActiveAsync(TcpClient client)
    {
        byte[] data = new byte[1];

        try
        {
            await client.GetStream().WriteAsync(data, 0, 0);
        }
        catch (IOException)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Sends a given <paramref name="client"/> a message.
    /// </summary>
    /// <param name="client"><see cref="TcpClient"/> that is sent a message.</param>
    /// <param name="message">Message to be sent to <paramref name="client"/>.</param>
    /// <exception cref="IOException">Raised when an error occurs while attempting to use .WriteAsync.</exception>
    /// <exception cref="TaskCanceledException">Raised if <paramref name="token"/> is canceled.</exception>
    private static async Task SendClientMessageAsync(string message, TcpClient client, CancellationToken? token)
    {
        byte[] msg = Encoding.ASCII.GetBytes(message);
        NetworkStream stream = client.GetStream();

        if (token == null)
        {
            await stream.WriteAsync(msg, 0, msg.Length);
        }
        else
        {
            await stream.WriteAsync(msg, 0, msg.Length, (CancellationToken)token);
        }
    }

    /// <summary>
    /// Handles responses from a <paramref name="user"/> client asynchronously.
    /// </summary>
    /// <remarks>Upon exiting the main loop <paramref name="user"/> will have all of its references on the server dealt with.</remarks>
    /// <param name="user"><see cref="Player"/> instance that is monitored for its responses.</param>
    /// <param name="token">A <see cref="CancellationToken"/> that is specificly for <paramref name ="user"/>.</param>
    private async Task ProcessClientDataAsync(Player user, CancellationToken token)
    {
        using NetworkStream stream = user.Client.GetStream();
        ServerCommand clientCommand;

        bool clientDisconnected = false;

        try
        {
            while (!clientDisconnected)
            {
                var builder = new StringBuilder();
                int responseByteCount;
                byte[] bytes = new byte[256];

                do
                {   // Loop to receive all the data sent by the client.
                    responseByteCount = await stream.ReadAsync(bytes, token);
                    if (responseByteCount > 0) builder.Append(Encoding.ASCII.GetString(bytes, 0, responseByteCount));
                } while (responseByteCount > 0);

                if (builder.Length == 0) continue;

                string recievedText = builder.ToString();
                ServerCommand? deserializedData = JsonSerializer.Deserialize<ServerCommand>(recievedText);

                if (deserializedData != null)
                {
                    if (deserializedData.CMD == CommandType.LookingForGame)
                    {   // Only add the user to the queue if they aren't already in it.
                        if (!_waitingForGameLobby.Contains(user)) _waitingForGameLobby.Enqueue(user);
                    }
                    else if (deserializedData.CMD == CommandType.NewMove && deserializedData.MoveDetails != null)
                    {
                        if (_startedGames.TryGetValue(deserializedData.GameIdentifier, out GameEnvironment? currentGame))
                        {
                            Player opposingUser = (user == currentGame.AssociatedPlayers[Team.White]) ? currentGame.AssociatedPlayers[Team.Black] : currentGame.AssociatedPlayers[Team.White];
                            // Send back a response to the opposing player.
                            int iterationCount = 0;
                            while (true)
                            {
                                try
                                {
                                    await SendClientMessageAsync(recievedText, opposingUser.Client, null);
                                    break;
                                }
                                catch (IOException)
                                {
                                    if (!await IsClientActiveAsync(opposingUser.Client) || ++iterationCount == 5)
                                    {
                                        await ClientRemovalAsync(opposingUser);
                                        break;
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                            }
                        }
                    }
                    else if (_endGameCommands.Contains(deserializedData.CMD))
                    {
                        if (_startedGames.TryGetValue(deserializedData.GameIdentifier, out GameEnvironment? currentGame))
                        {
                            _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(currentGame!.GameID, currentGame));
                        }
                    }
                    else if (deserializedData.CMD == CommandType.ClientDisconnecting)
                    {
                        clientDisconnected = true;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException || e is TaskCanceledException)
        { // Client has likely disconnected.
            clientDisconnected = true;
        }
        finally
        {
            // Gather all the games that user is taking part in and notify the opponent os user's disconnect.
            List<GameEnvironment> gamesToDisconnect = (from game in _startedGames.Values
                                                       where game.AssociatedPlayers.ContainsValue(user)
                                                       select game).ToList();
            var disconnectionTasks = new List<Task>();

            foreach (GameEnvironment game in gamesToDisconnect)
            {
                // If !_mainCancelationToken.IsCancellationRequested is true then the server is shutting down and there is no need to notify the opponent.
                if (!_mainCancelationToken.IsCancellationRequested)
                {
                    clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);
                    Player opposingUser = (user == game.AssociatedPlayers[Team.White]) ? game.AssociatedPlayers[Team.Black] : game.AssociatedPlayers[Team.White];
                    disconnectionTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), opposingUser.Client, token));
                }
                _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(game.GameID, game));
            }

            _connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user));

            CancellationTokenSource cancellationSource = _clientListeningCancelationTokens[user.ServerAssignedID];
            // Dispose of the CancellationTokenSource.
            cancellationSource.Dispose();
            // Remove references to the Token.
            _clientListeningCancelationTokens.TryRemove(new KeyValuePair<int, CancellationTokenSource>(user.ServerAssignedID, cancellationSource));

            if (_waitingForGameLobby.Contains(user))
            {   /// Remove user from the LFG queue.
                _waitingForGameLobby = new ConcurrentQueue<Player>(_waitingForGameLobby.Where(x => !x.Equals(user)));
            }

            if (disconnectionTasks.Count > 0) await Task.WhenAll(disconnectionTasks);
        }
    }
    /// <summary>
    /// Removes <paramref name="user"/> from <see cref="_clientListeningTasks"/> and <see cref="_connectedPlayers"/>. 
    /// </summary>
    /// <param name="user"><see cref="Player"/> instance that has its references removed.</param>
    private async Task ClientRemovalAsync(Player user)
    {
        // Cancel the listening for response task and dispose of the token.
        try
        {
            if (_clientListeningCancelationTokens.TryGetValue(user.ServerAssignedID, out CancellationTokenSource? cancelSource))
            {
                cancelSource.Cancel();
                // Cancel Source is Disposed in the following Task.
                await _clientListeningTasks[user.ServerAssignedID];
            }
        }
        catch (ObjectDisposedException)
        {

        }
        finally
        {
            user.Client.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously tells all connected <see cref="TcpClients"/>  in <see cref="_connectedPlayers"/> that the server is shutting down and removes relevant references.
    /// </summary>
    private async Task BrodcastServerShutDown()
    {
        string shutdownCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.ServerIsShuttingDown));

        var shutDownBroadcastTasks = new List<Task>();

        // Tell clients that the server is shutting down and then dispose of their resources.
        foreach (var player in _connectedPlayers.Values)
        {
            shutDownBroadcastTasks.Add(SendClientMessageAsync(shutdownCommand, player.Client, token: null)
                                        .ContinueWith(x => ClientRemovalAsync(player)));
        }

        await Task.WhenAll(shutDownBroadcastTasks);
    }
}