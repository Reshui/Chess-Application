namespace Pieces;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System;
using System.Collections.Concurrent;
public class Server
{
    public const string eosText = "End_Of_Stream";

    /// <summary>List of connected <see cref="TcpClient"/>s to the current <see cref="Server"/> instance.</summary>
    private readonly ConcurrentDictionary<int, Player> _connectedPlayers = new();

    /// <summary>Listens for user responses and connections.</summary>
    private readonly TcpListener _gameServer;

    /// <summary>Dictionary of <see cref="GameEnvironment"/> instances that have been started.</summary>
    private readonly ConcurrentDictionary<int, GameEnvironment> _startedGames = new();

    /// <summary>Stores players that are currently waiting for a game.</summary>
    private ConcurrentQueue<Player> _waitingForGameLobby = new();

    /// <summary>Stores Tasks that listen for <see cref="Player"/> responses.</summary>
    private readonly ConcurrentDictionary<int, Task> _clientListeningTasks = new();

    /// <summary>Dictionary of Cancellation Tokens keyed to a given <see cref="Player.ServerAssignedID"/>.</summary>
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _clientListeningCancelationTokens = new();

    /// <summary>Token source used to shutdown all server functions.</summary>
    public readonly CancellationTokenSource ServerShutDownCancelSource = new();

    /// <summary>Gets <see cref="ServerShutDownCancelSource"/>'s <see cref="CancellationToken"/>.</summary>
    private CancellationToken MainCancellationToken { get => ServerShutDownCancelSource.Token; }

    /// <summary>List that holds the asynchronous tasks started in <see cref="StartServer()"/>.</summary>
    private readonly List<Task> _serverTasks = new() { Capacity = 2 };

    /// <summary>A series of commands that signal the server that a given game has ended.</summary>
    private static readonly CommandType[] _endGameCommands = { CommandType.DeclareLoss, CommandType.DeclareWin, CommandType.DeclareStaleMate };

    /// <summary>
    /// Enums used by either the Server or Client to help evaluate recieved data.
    /// </summary>
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
        DeclareStaleMate,
        RegisterUser
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
        public string? Name { get; set; }

        public ServerCommand(CommandType cmd, int gameIdentifier = 0, MovementInformation? moveDetails = null, Team? assignedTeam = null, string? name = null)
        {
            CMD = cmd;
            GameIdentifier = gameIdentifier;

            /*
            MoveDetails = moveDetails;
            AssignedTeam = assignedTeam;
            Name = name;
            */
            
            if (cmd == CommandType.NewMove)
            {
                MoveDetails = moveDetails ?? throw new ArgumentNullException(nameof(moveDetails), "A new move command has been submitted without a non-null MovementInformation struct.");
            }
            else if (cmd == CommandType.StartGameInstance)
            {
                AssignedTeam = assignedTeam ?? throw new ArgumentNullException(nameof(assignedTeam), "Value cannot be null with the given Command Type.");
            }
            else if (cmd == CommandType.RegisterUser)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name), "name value not provided.");
            }            
        }
    }
    public Server()
    {   // Set the TcpListener on port 13000.
        int port = 13000;
        IPAddress localAddr = IPAddress.Parse("127.0.0.1");
        // TcpListener server = new TcpListener(port);
        _gameServer = new TcpListener(localAddr, port);
    }
    public void StartServer()
    {
        _serverTasks.Add(WaitForClientsAsync());
        _serverTasks.Add(MonitorLFGLobbyAsync());
    }

    /// <summary>
    /// Stops all server tasks and notifies connected <see cref="TcpClient"/>s that the server has shut down.
    /// </summary>
    public async Task CloseServerAsync()
    {
        try
        {
            ServerShutDownCancelSource.Cancel();
            _waitingForGameLobby.Clear();
            await Task.WhenAll(_serverTasks);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
        finally
        {
            await AnnounceServerShutDown();
            ServerShutDownCancelSource.Dispose();
        }
    }
    /// <summary>
    /// Monitors <see cref="_waitingForGameLobby"/> for added <see cref="Player"/> instances and notifies users when a game is available.
    /// </summary>
    private async Task MonitorLFGLobbyAsync()
    {
        List<Player> matchedPlayers = new() { Capacity = 2 };

        while (!MainCancellationToken.IsCancellationRequested)
        {
            if (_waitingForGameLobby.TryDequeue(out Player? waitingPlayer))
            {
                matchedPlayers.Add(waitingPlayer);

                if (matchedPlayers.Count == 2)
                {
                    bool bothPlayersAvailable = true;

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
                            bothPlayersAvailable = false;
                        }
                        clientPingingTasks.Remove(completedTask);
                    }

                    if (!bothPlayersAvailable)
                    {
                        await Task.WhenAll(clientRemovalTasks);
                    }
                    else
                    {
                        var newGame = new GameEnvironment(matchedPlayers[0], matchedPlayers[1]);

                        foreach (KeyValuePair<Team, Player> playerDetail in newGame.AssociatedPlayers)
                        {
                            var clientCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);
                            bool cancellationTokenAvailable = _clientListeningCancelationTokens.TryGetValue(playerDetail.Value.ServerAssignedID, out CancellationTokenSource? cancelSource);

                            if (cancellationTokenAvailable && cancelSource != null)
                            {
                                try
                                {
                                    await SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), playerDetail.Value.Client!, cancelSource.Token);
                                }
                                catch (Exception e) when (e is TaskCanceledException || e is IOException)
                                {   // Failed to message client or client is leaving the server.
                                    matchedPlayers.Remove(playerDetail.Value);
                                    bothPlayersAvailable = false;
                                }
                            }
                            else
                            {
                                bothPlayersAvailable = false;
                            }
                        }
                        // If they are no problems with notifying both players to start the game then track the game.
                        if (bothPlayersAvailable)
                        {
                            _startedGames.TryAdd(newGame.GameID, newGame);
                            matchedPlayers.Clear();
                        }
                        else if (matchedPlayers.Count == 1)
                        {
                            try
                            {
                                var notifyOpponentDisconnectCommand = new ServerCommand(CommandType.OpponentClientDisconnected, newGame.GameID);
                                await SendClientMessageAsync(JsonSerializer.Serialize(notifyOpponentDisconnectCommand), matchedPlayers[0].Client!, null);
                            }
                            catch (IOException)
                            {   // Failed to message client or client is leaving the server.
                                matchedPlayers.RemoveAt(0);
                            }
                        }
                    }
                }
            }
            await Task.Delay(700, MainCancellationToken);
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
                TcpClient? newClient = await _gameServer.AcceptTcpClientAsync(MainCancellationToken);

                if (newClient is not null)
                {
                    var newPlayer = new Player(newClient);
                    var cancellationSource = new CancellationTokenSource();

                    _connectedPlayers.TryAdd(newPlayer.ServerAssignedID, newPlayer);
                    _clientListeningCancelationTokens.TryAdd(newPlayer.ServerAssignedID, cancellationSource);
                    _clientListeningTasks.TryAdd(newPlayer.ServerAssignedID, ProcessClientDataAsync(newPlayer, cancellationSource.Token));
                }
            }
        }
        catch (TaskCanceledException)
        {   // Error raised if MainCancellationToken.IsCancelRequested = true .
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
    /// <returns><see cref="Task(bool)"/> <see langword="true"/> if socket is still connected; otherwise, <see langword="false"/>.</returns>
    /// <param name="client"><see cref="TcpClient"/> that is tested.</param>
    private static async Task<bool> IsClientActiveAsync(TcpClient client)
    {
        byte[] data = new byte[1];

        try
        {
            await client.GetStream().WriteAsync(data);
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
    public static async Task SendClientMessageAsync(string message, TcpClient client, CancellationToken? token)
    {
        byte[] msg = Encoding.ASCII.GetBytes(message);

        List<byte> constructedMessage = BitConverter.GetBytes(msg.Length).ToList();

        constructedMessage.AddRange(msg);

        byte[] msgConverted = constructedMessage.ToArray();
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(msgConverted);

    }

    /// <summary>
    /// Handles responses from a <paramref name="user"/> client asynchronously.
    /// </summary>
    /// <remarks>Upon exiting the main loop <paramref name="user"/> will have all of its references on the server dealt with.</remarks>
    /// <param name="user"><see cref="Player"/> instance that is monitored for its responses.</param>
    /// <param name="token">A <see cref="CancellationToken"/> made for <paramref name ="user"/>.</param>
    private async Task ProcessClientDataAsync(Player user, CancellationToken token)
    {
        using NetworkStream stream = user.Client.GetStream();
        ServerCommand clientCommand;

        bool clientDisconnected = false;
        bool userRegistered = false;
        try
        {
            while (!clientDisconnected)
            {
                var builder = new StringBuilder();
                byte[] bytes = new byte[4];
                int totalRecieved = 0, totalMessageByteCount = 0, responseByteCount = 0;
                bool byteCountRecieved = false;

                do
                {   // Loop to receive all the data sent by the client.
                    responseByteCount = await stream.ReadAsync(bytes, token);

                    if (!byteCountRecieved)
                    {
                        byteCountRecieved = true;
                        totalMessageByteCount = BitConverter.ToInt32(bytes, 0);
                        bytes = new byte[totalMessageByteCount];
                    }
                    else
                    {
                        var textSection = Encoding.ASCII.GetString(bytes, 0, responseByteCount);
                        builder.Append(textSection);
                    }

                } while ((totalRecieved += responseByteCount) < totalMessageByteCount);

                if (builder.Length == 0) continue;

                string recievedText = builder.ToString();

                ServerCommand? deserializedData = JsonSerializer.Deserialize<ServerCommand>(recievedText);

                if (deserializedData is not null)
                {
                    if (deserializedData.CMD == CommandType.RegisterUser && !userRegistered)
                    {
                        try
                        {
                            user.AssignName(deserializedData.Name!);
                            userRegistered = true;
                        }
                        catch (Exception)
                        { // Thrown if Name already has a value.

                        }
                    }
                    else if (deserializedData.CMD == CommandType.LookingForGame && userRegistered)
                    {   // Only add the user to the queue if they aren't already in it.
                        if (!_waitingForGameLobby.Contains(user)) _waitingForGameLobby.Enqueue(user);
                    }
                    else if (deserializedData.CMD == CommandType.NewMove && deserializedData.MoveDetails != null && userRegistered)
                    {
                        if (_startedGames.TryGetValue(deserializedData.GameIdentifier, out GameEnvironment? currentGame))
                        {
                            Team submittingTeam = deserializedData.MoveDetails.Value.SubmittingTeam;

                            Player opposingUser = currentGame.AssociatedPlayers[submittingTeam == Team.Black ? Team.White : Team.Black];
                            // Send back a response to the opposing player.
                            int iterationCount = 0;
                            while (++iterationCount <= 3)
                            {
                                try
                                {
                                    await SendClientMessageAsync(recievedText, opposingUser.Client, null);
                                    break;
                                }
                                catch (IOException)
                                {
                                    if (!await IsClientActiveAsync(opposingUser.Client))
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
                    else if (_endGameCommands.Contains(deserializedData.CMD) && _startedGames.TryGetValue(deserializedData.GameIdentifier, out GameEnvironment? currentGame))
                    {
                        _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(currentGame!.GameID, currentGame));
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
            // Gather all the games that user is taking part in and notify the opponent of the user's disconnect.
            List<GameEnvironment> gamesToDisconnect = (from game in _startedGames.Values
                                                       where game.AssociatedPlayers.ContainsValue(user)
                                                       select game).ToList();
            var disconnectionTasks = new List<Task>();

            foreach (GameEnvironment game in gamesToDisconnect)
            {
                // If !MainCancellationToken.IsCancellationRequested is true then the server is shutting down and there is no need to notify the opponent.
                if (!MainCancellationToken.IsCancellationRequested)
                {
                    clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);
                    Player opposingUser = (user == game.AssociatedPlayers[Team.White]) ? game.AssociatedPlayers[Team.Black] : game.AssociatedPlayers[Team.White];
                    disconnectionTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), opposingUser.Client, token));
                }
                _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(game.GameID, game));
            }

            if (!MainCancellationToken.IsCancellationRequested)
            {
                disconnectionTasks.Add(ClientRemovalAsync(user));
                await Task.WhenAll(disconnectionTasks);
            }
        }
    }
    /// <summary>
    /// Removes <paramref name="user"/> from <see cref="_clientListeningTasks"/> and <see cref="_connectedPlayers"/>. 
    /// </summary>
    /// <param name="user"><see cref="Player"/> instance that has its references removed.</param>
    private async Task ClientRemovalAsync(Player user)
    {
        // Cancel the listening for response task and dispose of the token.

        if (_clientListeningCancelationTokens.TryRemove(user.ServerAssignedID, out CancellationTokenSource? cancelSource))
        {
            cancelSource.Cancel();
            _connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user));

            if (MainCancellationToken.IsCancellationRequested)
            {
                await _clientListeningTasks[user.ServerAssignedID];
                // _waitingForGameLobby should already be cleared in CloseServerAsync()
            }
            else if (_waitingForGameLobby.Contains(user))
            {   // Remove user from the LFG queue.
                _waitingForGameLobby = new ConcurrentQueue<Player>(_waitingForGameLobby.Where(x => !x.Equals(user)));
            }

            cancelSource.Dispose();
            user.Client.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously tells all connected <see cref="Player"/> instances in <see cref="_connectedPlayers"/>
    /// that the server is shutting down and removes relevant references.
    /// </summary>
    private async Task AnnounceServerShutDown()
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