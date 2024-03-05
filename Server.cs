namespace Pieces;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System;
using System.Collections.Concurrent;
public class Server
{
    /// <summary>List of connected <see cref="TcpClient"/> instances to the current <see cref="Server"/> instance.</summary>
    private readonly ConcurrentDictionary<int, Player> _connectedPlayers = new();

    /// <summary>Listens for user responses and connections.</summary>
    private readonly TcpListener _gameServer;

    /// <summary>Dictionary of <see cref="GameEnvironment"/> instances that have been started.</summary>
    private readonly ConcurrentDictionary<int, GameEnvironment> _startedGames = new();

    /// <summary>Stores players that are currently waiting for a game.</summary>
    private ConcurrentQueue<Player> _waitingForGameLobby = new();

    /// <summary>Stores Tasks that listen for <see cref="Player"/> responses.</summary>
    private readonly ConcurrentDictionary<int, Task> _clientListeningTasks = new();

    /// <summary>Token source used to shutdown all server functions.</summary>
    public readonly CancellationTokenSource ServerShutDownCancelSource = new();

    /// <summary>Gets <see cref="ServerShutDownCancelSource"/>'s <see cref="CancellationToken"/>.</summary>
    /// <exception cref="ObjectDisposedException"></exception>
    private CancellationToken ServerTasksCancellationToken => ServerShutDownCancelSource.Token;

    /// <summary>List that holds the asynchronous tasks started in <see cref="StartServer()"/>.</summary>
    private readonly List<Task> _serverTasks = new() { Capacity = 2 };

    /// <summary>A series of commands that signal the server that a given game has ended.</summary>
    private static readonly CommandType[] _endGameCommands = { CommandType.DeclareLoss, CommandType.DeclareWin, CommandType.DeclareStaleMate };

    /// <summary>
    /// Enums used by either the Server or Client to help evaluate recieved data.
    /// </summary>
    public enum CommandType
    {
        ClientDisconnecting, NewMove, OpponentClientDisconnected, StartGameInstance, DeclareForefit, LookingForGame, ServerIsShuttingDown,
        DeclareWin, DeclareLoss, DeclareStaleMate, RegisterUser, SuccessfullyRegistered
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

        public ServerCommand(CommandType cmd, int gameIdentifier = 0, MovementInformation? moveDetails = null, Team? assignedTeam = null, string? name = null)
        {
            CMD = cmd;
            GameIdentifier = gameIdentifier;

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
                Name = name ?? throw new ArgumentNullException(nameof(name), $"{nameof(name)} value not provided.");
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
    /// <summary>
    /// Starts asynchronous server tasks.
    /// </summary>
    public void StartServer()
    {
        _serverTasks.Add(WaitForClientsAsync());
        _serverTasks.Add(MonitorLFGLobbyAsync());
    }

    /// <summary>
    /// Asynchronously waits for <see cref="TcpClient"/> connections to <see cref="_gameServer"/>.
    /// </summary>
    private async Task WaitForClientsAsync()
    {
        try
        {
            _gameServer.Start();
            while (true)
            {
                TcpClient newClient = await _gameServer.AcceptTcpClientAsync(ServerTasksCancellationToken);

                var newPlayer = new Player(newClient);
                _connectedPlayers.TryAdd(newPlayer.ServerAssignedID, newPlayer);
                _clientListeningTasks.TryAdd(newPlayer.ServerAssignedID, ProcessClientDataAsync(newPlayer, newPlayer.MainTokenSource.Token).ContinueWith(x => ClientRemovalAsync(newPlayer)));
            }
        }
        catch (TaskCanceledException)
        {   // Error raised if _serverTasksCancellationToken.IsCancelRequested = true .
            Console.WriteLine($"Locally hosted server:{nameof(_gameServer)}, is shutting down.");
            foreach (Player connectedPlayer in _connectedPlayers.Values)
            {
                connectedPlayer.MainTokenSource.Cancel();
            }
            await Task.WhenAll(_clientListeningTasks.Values);
        }
        finally
        {
            _gameServer.Stop();
        }
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
            ServerShutDownCancelSource.Dispose();
        }
    }

    /// <summary>
    /// Removes <paramref name="user"/> from <see cref="_clientListeningTasks"/> and <see cref="_connectedPlayers"/>.
    /// </summary>
    /// <remarks>If <see cref="ServerTasksCancellationToken.IsCancellationRequested"/> then <paramref name="user"/> will be sent a server shutdown message</remarks>
    /// <param name="user"><see cref="Player"/> instance that has its references removed.</param>
    private async Task ClientRemovalAsync(Player user)
    {
        // Cancel the listening for response task and dispose of the token.
        if (_connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user)))
        {
            // If server is shutting down then send a shutdown message.
            if (ServerTasksCancellationToken.IsCancellationRequested)
            {
                string shutdownCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.ServerIsShuttingDown));
                try
                {
                    await SendClientMessageAsync(shutdownCommand, user.Client!, null);
                }
                catch (IOException)
                {
                    // Don't care.
                }
            }
            else if (_waitingForGameLobby.Contains(user))
            {   // Remove user from the LFG queue.
                _waitingForGameLobby = new ConcurrentQueue<Player>(_waitingForGameLobby.Where(x => !x.Equals(user)));
            }

            try { user.MainTokenSource.Dispose(); } catch (ObjectDisposedException) { }
            user.Client.Close();
            Console.WriteLine($"{user.Name} has disconnected from the server.");
        }
    }

    /// <summary>
    /// Monitors <see cref="_waitingForGameLobby"/> for added <see cref="Player"/> instances and 
    /// notifies users when a game is available.
    /// </summary>
    /// <remarks>When 2 <see cref="Player"/> instances are in the queue they are matched and then removed from the queue.</remarks>
    private async Task MonitorLFGLobbyAsync()
    {
        List<Player> matchedPlayers = new() { Capacity = 2 };

        while (!ServerTasksCancellationToken.IsCancellationRequested)
        {
            if (_waitingForGameLobby.TryDequeue(out Player? waitingPlayer))
            {
                matchedPlayers.Add(waitingPlayer);

                if (matchedPlayers.Count == 2)
                {
                    bool bothPlayersAvailable = true;

                    List<Task<bool>> clientPingingTasks = new();
                    foreach (Player user in matchedPlayers)
                    {
                        try
                        {
                            clientPingingTasks.Add(IsClientActiveAsync(user.Client, user.MainTokenSource.Token));
                        }
                        catch (ObjectDisposedException)
                        {
                            matchedPlayers.Remove(user);
                            bothPlayersAvailable = false;
                            break;
                        }
                    }

                    while (bothPlayersAvailable && clientPingingTasks.Count > 0)
                    {
                        Task<bool> completedTask = await Task.WhenAny(clientPingingTasks);
                        if (completedTask.IsFaulted || !completedTask.Result)
                        {
                            Player user = matchedPlayers[clientPingingTasks.IndexOf(completedTask)];
                            matchedPlayers.Remove(user);
                            // If error then Client is being actively removed.
                            try { user.MainTokenSource.Cancel(); } catch (ObjectDisposedException) { }
                            bothPlayersAvailable = false;
                        }
                        clientPingingTasks.Remove(completedTask);
                    }

                    if (bothPlayersAvailable)
                    {
                        var newGame = new GameEnvironment(matchedPlayers[0], matchedPlayers[1]);
                        var playersAlertedForGame = new List<Player>();
                        // Inform player code that it should start a GameEnvironment instance.
                        foreach (KeyValuePair<Team, Player> playerDetail in newGame.AssociatedPlayers)
                        {
                            var startGameCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);
                            try
                            {
                                await SendClientMessageAsync(JsonSerializer.Serialize(startGameCommand), playerDetail.Value.Client, playerDetail.Value.MainTokenSource.Token);
                                playersAlertedForGame.Add(playerDetail.Value);
                            }
                            catch (Exception e) when (e is TaskCanceledException || e is IOException || e is ObjectDisposedException)
                            {   // Failed to message client or client is leaving the server.
                                matchedPlayers.Remove(playerDetail.Value);
                                bothPlayersAvailable = false;
                                break;
                            }
                        }
                        // If there are no problems with notifying both players to start the game then track the game and clear the matchedPlayers list.
                        if (bothPlayersAvailable)
                        {
                            _startedGames.TryAdd(newGame.GameID, newGame);
                            matchedPlayers.Clear();
                        }
                        else if (playersAlertedForGame.Any())
                        {
                            var notifyOpponentDisconnectCommand = new ServerCommand(CommandType.OpponentClientDisconnected, newGame.GameID);
                            // Inform players that are waiting for their opponent that they have disconnected.
                            foreach (Player playerWaitingForOpponent in playersAlertedForGame)
                            {
                                try
                                {
                                    await SendClientMessageAsync(JsonSerializer.Serialize(notifyOpponentDisconnectCommand), playerWaitingForOpponent.Client!, playerWaitingForOpponent.MainTokenSource.Token);
                                }
                                catch (Exception e) when (e is TaskCanceledException || e is IOException || e is ObjectDisposedException)
                                {
                                    matchedPlayers.Remove(playerWaitingForOpponent);
                                }
                            }
                        }
                    }
                }
            }

            try
            {
                await Task.Delay(700, ServerTasksCancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Asynchronously tests if a given <paramref name="client"/> is still responsive.
    /// </summary>
    /// <returns><see cref="Task{bool}"/> : <see langword="true"/> if socket is still connected; otherwise, <see langword="false"/>.</returns>
    /// <param name="client"><see cref="TcpClient"/> that is tested.</param>
    public static async Task<bool> IsClientActiveAsync(TcpClient client, CancellationToken token)
    {
        byte[] data = new byte[1];

        try
        {   // Send 0 bytes to test the connection.
            await client.GetStream().WriteAsync(data.AsMemory(0, 0), token);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Sends <paramref name="message"/> in byte form to a given <paramref name="client"/>.
    /// </summary>
    /// <param name="client"><see cref="TcpClient"/> that is sent a message.</param>
    /// <param name="message">Message to be sent to <paramref name="client"/>.</param>
    /// <exception cref="IOException">Raised when an error occurs while attempting to use .WriteAsync.</exception>
    /// <exception cref="ObjectDisposedException"></exception>
    public static async Task SendClientMessageAsync(string message, TcpClient client, CancellationToken? token)
    {
        byte[] msg = Encoding.ASCII.GetBytes(message);
        List<byte> constructedMessage = BitConverter.GetBytes(msg.Length).ToList();
        constructedMessage.AddRange(msg);
        byte[] msgConverted = constructedMessage.ToArray();

        if (token is not null) await client.GetStream().WriteAsync(msgConverted, (CancellationToken)token);
        else await client.GetStream().WriteAsync(msgConverted);
    }

    /// <summary>
    /// Asynchronously waits for messages from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream"><see cref="NetworkStream"/> that is awaited for its responses.</param>
    /// <exception cref="TaskCanceledException">Thrown if <paramref name="token"/> source is cancelled.</exception>
    /// <exception cref="IOException">Thrown if something goes wrong with <paramref name="stream"/>.ReadAsync().</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="token"/> is invoked while using .ReadAsync().</exception>
    public static async Task<ServerCommand> RecieveCommandFromStreamAsync(NetworkStream stream, CancellationToken token)
    {
        var builder = new StringBuilder();
        int responseByteCount;

        do
        {
            // Incoming messages contain the length of the message in bytes within the first 4 bytes.
            byte[] bytes = new byte[sizeof(int)];
            int totalRecieved = -1 * bytes.Length, incomingMessageByteCount = 0;
            bool byteCountRecieved = false;

            do
            {   // Loop to receive all the data sent by the client.
                responseByteCount = await stream.ReadAsync(bytes, token);

                if (responseByteCount > 0)
                {
                    if (!byteCountRecieved)
                    {   // Small test to ensure that response is 4 bytes long.
                        // Gets the byte count of the incoming data and sizes the bytes array to accomadate. 
                        if (responseByteCount == sizeof(int))
                        {
                            try
                            {   // Verify that the first 4 bytes are a number.
                                incomingMessageByteCount = BitConverter.ToInt32(bytes, 0);
                            }
                            catch (Exception)
                            {
                                continue;
                            }
                            totalRecieved += responseByteCount;
                            byteCountRecieved = true;
                            // Resize the array to fit incoming data.
                            bytes = new byte[incomingMessageByteCount];
                        }
                    }
                    else
                    {
                        string textSection = Encoding.ASCII.GetString(bytes, 0, responseByteCount);
                        // If the expected number of bytes has been read then attempt to deserialize it into a ServerCommand
                        if ((totalRecieved += responseByteCount) == incomingMessageByteCount)
                        {   // All message bytes have been recieved.
                            if (builder.Length > 0) textSection = builder.Append(textSection).ToString();

                            try
                            {   // Making sure that the recieved text is a valid command.
                                ServerCommand? newCommand = JsonSerializer.Deserialize<ServerCommand>(textSection);
                                return newCommand!;
                            }
                            catch (Exception)
                            {
                                if (builder.Length > 0) builder = new StringBuilder();
                                // Start waiting for another response.
                                break;
                            }
                        }
                        else
                        {   // In case the message comes in multiple parts, store the partial text in a string builder.
                            builder.Append(textSection);
                        }
                    }
                }
                // While the entire message hasn't been recieved.
            } while (totalRecieved < incomingMessageByteCount);

        } while (true);
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

        (bool clientDisconnected, bool userRegistered) = (false, false);
        try
        {
            while (!clientDisconnected && !user.MainTokenSource.Token.IsCancellationRequested && !ServerTasksCancellationToken.IsCancellationRequested)
            {
                ServerCommand clientResponse = await RecieveCommandFromStreamAsync(stream, token);

                if (clientResponse is not null)
                {
                    if (clientResponse.CMD == CommandType.RegisterUser && !userRegistered)
                    {
                        try
                        {
                            user.AssignName(clientResponse.Name!);
                            userRegistered = true;
                            Console.WriteLine($"{user.Name} has registered.");
                        }
                        catch (Exception)
                        { // Thrown if Name already has a value.

                        }
                    }
                    else if (clientResponse.CMD == CommandType.LookingForGame && userRegistered && !_waitingForGameLobby.Contains(user))
                    {   // Only add the user to the queue if they aren't already in it.
                        _waitingForGameLobby.Enqueue(user);
                    }
                    else if (clientResponse.CMD == CommandType.NewMove && clientResponse.MoveDetails is not null && userRegistered)
                    {   // Send user response to the opposing player.
                        if (_startedGames.TryGetValue(clientResponse.GameIdentifier, out GameEnvironment? currentGame))
                        {
                            Player opposingUser = currentGame.AssociatedPlayers[GameEnvironment.ReturnOppositeTeam(clientResponse.MoveDetails.Value.SubmittingTeam)];
                            try
                            {
                                await SendClientMessageAsync(JsonSerializer.Serialize(clientResponse), opposingUser.Client, opposingUser.MainTokenSource.Token);
                            }
                            catch (Exception e) when (e is IOException || e is ObjectDisposedException)
                            {
                                if (!ServerTasksCancellationToken.IsCancellationRequested)
                                {
                                    try { opposingUser.MainTokenSource.Cancel(); } catch (ObjectDisposedException) { }
                                    // If the opposingUser isn't reachable send player notification that the opponent couldn't be reached.
                                    // Errors thrown here will be caught in outermost catch statement.
                                    var opponentDisconnectedCommand = new ServerCommand(CommandType.OpponentClientDisconnected, currentGame.GameID);
                                    await SendClientMessageAsync(JsonSerializer.Serialize(opponentDisconnectedCommand), user.Client, user.MainTokenSource.Token);
                                }
                            }
                        }
                    }
                    else if (_endGameCommands.Contains(clientResponse.CMD) && _startedGames.TryGetValue(clientResponse.GameIdentifier, out GameEnvironment? currentGame))
                    {
                        _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(currentGame!.GameID, currentGame));
                    }
                    else if (clientResponse.CMD == CommandType.ClientDisconnecting)
                    {
                        user.MainTokenSource.Cancel();
                        clientDisconnected = true;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException || e is TaskCanceledException || e is ObjectDisposedException)
        {   // Client has likely disconnected.
            clientDisconnected = true;
        }
        finally
        {
            // Gather all the games that user is taking part in and notify the opponent of the user's disconnect.
            if (!ServerTasksCancellationToken.IsCancellationRequested)
            {
                var gamesToDisconnect = from game in _startedGames.Values
                                        where game.AssociatedPlayers.ContainsValue(user)
                                        select game;

                var gameEndingNotificationTasks = new List<Task>();
                // Send the opposing player a notification about their opponent disconnecting
                foreach (GameEnvironment game in gamesToDisconnect)
                {
                    bool gameFound = _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(game.GameID, game));
                    // If !_serverTasksCancellationToken.IsCancellationRequested is true then the server is shutting down and there is no need to notify the opponent.
                    if (gameFound && !ServerTasksCancellationToken.IsCancellationRequested)
                    {
                        clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);
                        Player opposingUser = user.Equals(game.AssociatedPlayers[Team.White]) ? game.AssociatedPlayers[Team.Black] : game.AssociatedPlayers[Team.White];
                        gameEndingNotificationTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), opposingUser.Client, ServerTasksCancellationToken));
                    }
                }
                await Task.WhenAll(gameEndingNotificationTasks);
            }
        }
    }
}