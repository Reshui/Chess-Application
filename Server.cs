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
    /// <summary>List of connected <c>TcpClients</c> to the current <see cref="Server"/> instance. Limited to 2.</summary>
    private ConcurrentDictionary<int, Player> _connectedPlayers = new();
    /// <summary>Listens for user responses and connections.</summary>
    private TcpListener _gameServer { get; init; }
    /// <summary>Dictionary of <c>GameEnvironment</c> instances that have been started.</summary>
    private ConcurrentDictionary<int, GameEnvironment> _startedGames = new();
    /// <summary>Data structure used to store players that are currently waiting for a game.</summary>
    private ConcurrentQueue<Player> _waitingForGameLobby = new();
    /// <summary>Stores Tasks that listen for <see cref="Player"/> responses.</summary>
    private ConcurrentDictionary<int, Task> _clientListeningTasks = new();
    /// <summary>Dictionary of Cancellation Tokens keyed to a given user</summary>
    private ConcurrentDictionary<int, CancellationTokenSource> _clientListeningCancellationTokens = new();
    public CancellationTokenSource ServerShutDownCancelSource { get; } = new();
    /// <summary><see cref="ServerShutDownCancelSource"/>'s <c>CancellationToken</c>,</summary>
    private CancellationToken _mainCancellationToken { get => ServerShutDownCancelSource.Token; }

    /// <summary>List that holds the asynchronous tasks started in <see cref="StartServer()"/>.</summary>
    private readonly List<Task> _serverTasks = new();
    public enum CommandType
    {
        ClientDisconnected,
        NewMove,
        OpponentClientDisconnected,
        StartGameInstance,
        Defeat,
        Winner,
        Draw,
        Forefit,
        LookingForGame,
        ServerIsShuttingDown,
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
        ServerShutDownCancelSource.Cancel();
        await Task.WhenAll(_serverTasks);
        ServerShutDownCancelSource.Dispose();
        await BrodcastServerShutDown();
    }
    /// <summary>
    /// Monitors <see cref="_waitingForGameLobby"/> for added <see cref="Player"/> instances and notifies users when a game is available.
    /// </summary>
    private async Task MonitorLFGLobbyAsync()
    {
        List<Player> matchedPlayers = new() { Capacity = 2 };

        while (!_mainCancellationToken.IsCancellationRequested)
        {
            if (_waitingForGameLobby.TryDequeue(out Player? waitingPlayer))
            {
                matchedPlayers.Add(waitingPlayer);

                if (matchedPlayers.Count == 2)
                {
                    bool bothPlayersAvailable = true;

                    foreach (Player user in matchedPlayers)
                    {
                        if (!await IsClientActiveAsync(user.Client))
                        {
                            matchedPlayers.Remove(user);
                            await ClientRemovalAsync(user);
                            bothPlayersAvailable = false;
                        }
                    }

                    if (bothPlayersAvailable)
                    {
                        var newGame = new GameEnvironment(matchedPlayers[0], matchedPlayers[1]);
                        var notificationTasks = new List<Task>();

                        foreach (KeyValuePair<Team, Player> playerDetail in newGame.AssociatedPlayers)
                        {
                            var clientCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);

                            bool cancellationTokenAvailable = _clientListeningCancellationTokens.TryGetValue(playerDetail.Value.ServerAssignedID, out CancellationTokenSource? cancelSource);

                            if (cancellationTokenAvailable && cancelSource != null)
                            {
                                notificationTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), playerDetail.Value.Client!, cancelSource.Token));
                            }
                        }

                        await Task.WhenAll(notificationTasks);

                        _startedGames.TryAdd(newGame.GameID, newGame);

                        matchedPlayers.Clear();
                    }

                }
            }
            await Task.Delay(1000);
        }
    }
    /// <summary>
    /// Asynchronously waits for <c>TcpClient</c> connections to <see cref="_gameServer"/>.
    /// </summary>
    private async Task WaitForClientsAsync()
    {
        // Start listening for client requests.
        _gameServer.Start();

        while (!_mainCancellationToken.IsCancellationRequested)
        {
            TcpClient? newClient = await _gameServer.AcceptTcpClientAsync(_mainCancellationToken);

            if (_mainCancellationToken.IsCancellationRequested == false && newClient != null)
            {
                Player newPlayer = new Player(newClient);
                var cancellationSource = new CancellationTokenSource();

                _connectedPlayers.TryAdd(newPlayer.ServerAssignedID, newPlayer);
                _clientListeningCancellationTokens.TryAdd(newPlayer.ServerAssignedID, cancellationSource);
                _clientListeningTasks.TryAdd(newPlayer.ServerAssignedID, ProcessClientDataAsync(newPlayer, cancellationSource.Token));
            }
        }

        _gameServer.Stop();
    }
    /// <summary>
    /// Tests if a given <paramref name="client"/> is still responsive.
    /// </summary>
    /// <returns><see langword="true"/> if socket is still connected; otherwise, <see langword="false"/>.</returns>
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
    /// <param name="client"><c>TcpClient</c> that is sent a message.</param>
    /// <param name="message">Message to be sent to <paramref name="client"/>.</param>
    /// <exception cref="IOException">Raised when an error occurs while attempting to use .WriteAsync.</exception>
    private static async Task SendClientMessageAsync(string message, TcpClient client, CancellationToken token)
    {
        byte[] msg = Encoding.ASCII.GetBytes(message);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(msg, 0, msg.Length, token);
    }

    /// <summary>
    /// Handles responses from a <paramref name="user"/> client asynchronously.
    /// </summary>
    /// <param name="user"><see cref="Player"/> instance that is monitored for its responses.</param>
    /// <param name="token">A <c>CancellationToken</c> that is specificly for <paramref name ="user"/>.</param>
    private async Task ProcessClientDataAsync(Player user, CancellationToken token)
    {
        NetworkStream stream = user.Client.GetStream();
        ServerCommand clientCommand;

        bool clientDisconnected = false;

        while (!(token.IsCancellationRequested || clientDisconnected))
        {
            var builder = new StringBuilder();
            int responseByteCount;
            byte[] bytes = new byte[256];

            do
            {   // Loop to receive all the data sent by the client.
                responseByteCount = await stream.ReadAsync(bytes, token);
                if (responseByteCount > 0) builder.Append(Encoding.ASCII.GetString(bytes, 0, responseByteCount));
            } while (token.IsCancellationRequested == false && responseByteCount > 0);

            if (token.IsCancellationRequested) break;
            else if (builder.Length == 0) continue;

            ServerCommand? deserializedData = JsonSerializer.Deserialize<ServerCommand>(builder.ToString());

            if (deserializedData != null)
            {
                if (deserializedData.CMD == CommandType.LookingForGame)
                {
                    // Only add the user to the queue if they aren't already in it.
                    if (!_waitingForGameLobby.Contains(user)) _waitingForGameLobby.Enqueue(user);
                }
                else if (deserializedData.CMD == CommandType.NewMove && deserializedData.MoveDetails != null)
                {
                    GameEnvironment currentGame = _startedGames[deserializedData.GameIdentifier];

                    (Player opposingUser, Team opposingTeamColor) = (user == currentGame.AssociatedPlayers[Team.White]) ? (currentGame.AssociatedPlayers[Team.Black], Team.Black) : (currentGame.AssociatedPlayers[Team.White], Team.White);

                    currentGame.SubmitFinalizedChange((MovementInformation)deserializedData.MoveDetails);
                    // Send back a response to the opposing player.
                    opposingUser.Client.GetStream().Write(bytes, 0, bytes.Length);

                    if (currentGame.IsKingCheckMated(currentGame.ReturnKing(opposingTeamColor)))
                    {
                        var clients = new[] { opposingUser.Client, user.Client };
                        var linkedCommands = new[] { CommandType.Defeat, CommandType.Winner };
                        var clientCommands = clients.Zip(linkedCommands, (c, lc) => new { Client = c, Command = lc });

                        var winLoseTasks = new List<Task>();

                        foreach (var msg in clientCommands)
                        {
                            clientCommand = new ServerCommand(msg.Command, currentGame.GameID);
                            winLoseTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), msg.Client, token));
                        }
                        await Task.WhenAll(winLoseTasks);
                        currentGame.GameEnded = true;
                    }
                    else if (currentGame.IsStalemate())
                    {
                        // Send both connected clients a Draw command.
                        clientCommand = new ServerCommand(CommandType.Draw, currentGame.GameID);

                        foreach (var player in new Player[] { user, opposingUser })
                        {
                            await SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), player.Client, token);
                        }

                        currentGame.GameEnded = true;
                    }

                    if (currentGame.GameEnded)
                    {   // Stop tracking the game.
                        _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(currentGame.GameID, currentGame));
                    }
                }
                else if (deserializedData.CMD == CommandType.ClientDisconnected)
                {
                    // Notify the opponent.
                    List<GameEnvironment> gamesToDisconnect = (from gameKeyValue in _startedGames
                                                               let game = gameKeyValue.Value
                                                               where game.AssociatedPlayers[Team.White] == user || game.AssociatedPlayers[Team.Black] == user
                                                               select game).ToList();

                    var disconnectionTasks = new List<Task>();
                    foreach (GameEnvironment game in gamesToDisconnect)
                    {
                        clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);

                        Player opposingUser = (user == game.AssociatedPlayers[Team.White]) ? game.AssociatedPlayers[Team.Black] : game.AssociatedPlayers[Team.White];

                        disconnectionTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), opposingUser.Client, token));

                        _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(game.GameID, game));
                    }

                    await Task.WhenAll(disconnectionTasks);

                    _connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user));
                    clientDisconnected = true;
                }
            }
        }
    }
    /// <summary>
    /// Removes <paramref name="user"/> from <see cref="_clientListeningTasks"/> and <see cref="_connectedPlayers"/>. 
    /// </summary>
    /// <param name="user"><see cref="Player"/> instance that has its references removed.</param>
    private async Task ClientRemovalAsync(Player user)
    {
        _clientListeningCancellationTokens[user.ServerAssignedID].Cancel();
        await _clientListeningTasks[user.ServerAssignedID];
        _clientListeningCancellationTokens[user.ServerAssignedID].Dispose();
        user.Client.Dispose();
        RemoveFromWaitingLobby(user);
        _connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user));
    }

    /// <summary>
    /// Removes <paramref name="user"/> instance from <see cref="_waitingForGameLobby"/> if it exists.
    /// </summary>
    private void RemoveFromWaitingLobby(Player user)
    {
        if (_waitingForGameLobby.Contains(user))
        {
            _waitingForGameLobby = new ConcurrentQueue<Player>(_waitingForGameLobby.Where(x => !x.Equals(user)));
        }
    }

    /// <summary>
    /// Tells all connected TcpClients that the server is shutting down and removes relevant references.
    /// </summary>
    private async Task BrodcastServerShutDown()
    {
        string shutdownCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.ServerIsShuttingDown));

        var shutDownBroadcastTasks = new List<Task>();

        var bb = new CancellationTokenSource();

        foreach (var player in _connectedPlayers.Values)
        {
            shutDownBroadcastTasks.Add(SendClientMessageAsync(shutdownCommand, player.Client, bb.Token).ContinueWith(x => ClientRemovalAsync(player)));
        }

        await Task.WhenAll(shutDownBroadcastTasks);
    }
}