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

    /// <summary>Dictionary of <see cref="TrackedGame"/> instances that have been started.</summary>
    private readonly ConcurrentDictionary<int, TrackedGame> _startedGames = new();

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
    private class TrackedGame
    {
        public Player WhitePlayer { get => AssociatedPlayers[Team.White]; }
        public Player BlackPlayer { get => AssociatedPlayers[Team.Black]; }
        public int GameID { get; }
        public Dictionary<Team, Player> AssociatedPlayers;
        private static int _gameID = 0;
        private static readonly Random _rand = new();
        public TrackedGame(Player playerOne, Player playerTwo)
        {
            GameID = ++_gameID;
            var playerArray = new Player[] { playerOne, playerTwo };
            int whitePlayerIndex = _rand.Next(playerArray.Length);
            AssociatedPlayers = new(2)
            {
                {Team.White, playerArray[whitePlayerIndex]},
                {Team.Black, playerArray[whitePlayerIndex == 1 ? 0 : 1]}
            };
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
                var cancelSourceForPlayer = new CancellationTokenSource();
                var serverAndPlayerSource = CancellationTokenSource.CreateLinkedTokenSource(cancelSourceForPlayer.Token, ServerTasksCancellationToken);

                var newPlayer = new Player(newClient, cancelSourceForPlayer, serverAndPlayerSource)
                {
                    PingConnectedClientTask = PingClientAsync(newClient, cancelSourceForPlayer, serverAndPlayerSource.Token)
                };
                _connectedPlayers.TryAdd(newPlayer.ServerAssignedID, newPlayer);
                _clientListeningTasks.TryAdd(newPlayer.ServerAssignedID, ListenForPlayerResponseAsync(newPlayer));
            }
        }
        catch (OperationCanceledException)
        {   // Error raised if _serverTasksCancellationToken.IsCancelRequested = true.
        }
        finally
        {
            try
            {
                await Task.WhenAll(_clientListeningTasks.Values);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                _gameServer.Stop();
                Console.WriteLine("[Server]: Server has shutdown.");
            }
        }
    }

    /// <summary>
    /// Stops all server tasks and notifies connected <see cref="TcpClient"/>s that the server has shut down.
    /// </summary>
    public async Task CloseServerAsync()
    {
        // Cancel all listening and waiting tasks.
        try
        {
            Console.WriteLine("[Server]: Attempting server shutdown.");
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
    /// <param name="user"><see cref="Player"/> instance that has its references removed.</param>
    private async Task ClientRemovalAsync(Player user)
    {
        if (_connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user)))
        {
            Console.WriteLine($"[Server]: {user.Name}  Cancellation Token Status; Server: ( {ServerTasksCancellationToken.IsCancellationRequested} ) , Personal: ( {user.PersonalSource.IsCancellationRequested} ) , Connected: {user.Client.Connected}");
            try
            {
                // If server is shutting down then send a shutdown message.
                if (ServerTasksCancellationToken.IsCancellationRequested && !user.PersonalSource.IsCancellationRequested)
                {
                    bool success = false;
                    string shutdownCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.ServerIsShuttingDown));
                    try
                    {
                        await SendClientMessageAsync(shutdownCommand, user.Client!, user.PersonalSource.Token);
                        success = true;
                    }
                    catch (IOException)
                    {
                        Console.WriteLine($"[Server]: {user.Name} => couldn't be reached for shutdown notification.\n\n");
                    }
                    catch (InvalidOperationException e)
                    {
                        Console.WriteLine($"[Server]: {user.Name}: InvalidOperationException =>" + e.Message);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[Server]: Server Failed to send shutdown message.\n\n" + e.Message);
                    }
                    finally
                    {
                        if (success) Console.WriteLine($"[Server]: Shutdown notification sent => {user.Name}");
                    }
                }

                user.PersonalSource.Cancel();
                if (_waitingForGameLobby.Contains(user))

                {   // Remove user from the LFG queue.
                    _waitingForGameLobby = new ConcurrentQueue<Player>(_waitingForGameLobby.Where(x => !x.Equals(user)));
                }

                if (user.PingConnectedClientTask is not null)
                {
                    await user.PingConnectedClientTask;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
            finally
            {
                user.PersonalSource.Dispose();
                user.CombinedSource?.Dispose();
                user.Client.Close();
                Console.WriteLine($"[Server]: {user.Name} has disconnected from the server.");
            }
        }
    }

    /// <summary>
    /// Monitors <see cref="_waitingForGameLobby"/> for added <see cref="Player"/> instances and 
    /// notifies users when a game is available.
    /// </summary>
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
                            clientPingingTasks.Add(IsClientActiveAsync(user.Client, user.CombinedSource!.Token));
                        }
                        catch (ObjectDisposedException)
                        {
                            // TaskCancelledExceptions will just return a false rather than throw themselves.
                            // Either server is shutting down or user no longer wants to play.
                            matchedPlayers.Remove(user);
                            bothPlayersAvailable = false;
                            break;
                        }
                    }

                    while (!ServerTasksCancellationToken.IsCancellationRequested && bothPlayersAvailable && clientPingingTasks.Any())
                    {
                        var completedTask = await Task.WhenAny(clientPingingTasks);

                        if (!completedTask.Result)
                        {
                            Player user = matchedPlayers[clientPingingTasks.IndexOf(completedTask)];
                            matchedPlayers.Remove(user);
                            // If error then Client is being actively removed.
                            try
                            {
                                user.PersonalSource.Cancel();
                            }
                            catch (ObjectDisposedException)
                            { }
                            bothPlayersAvailable = false;
                        }
                        clientPingingTasks.Remove(completedTask);
                    }

                    if (!ServerTasksCancellationToken.IsCancellationRequested && bothPlayersAvailable)
                    {
                        var newGame = new TrackedGame(matchedPlayers[0], matchedPlayers[1]);
                        var playersAlertedForGame = new List<Player>();
                        // Inform player code that it should start a GameEnvironment instance.
                        foreach (KeyValuePair<Team, Player> playerDetail in newGame.AssociatedPlayers)
                        {
                            var startGameCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);
                            try
                            {
                                await SendClientMessageAsync(JsonSerializer.Serialize(startGameCommand), playerDetail.Value.Client, playerDetail.Value.CombinedSource!.Token);
                                playersAlertedForGame.Add(playerDetail.Value);
                            }
                            catch (Exception e) when (e is OperationCanceledException || e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                            {
                                // Failed to message client or client is leaving the server.
                                matchedPlayers.Remove(playerDetail.Value);
                                bothPlayersAvailable = false;

                                if (e is InvalidOperationException)
                                {
                                    Console.WriteLine("[Server]: Monitor LFG: InvalidOperationException while attempting start game notification.  " + e.Message);
                                }
                                else if (e is not ObjectDisposedException)
                                {
                                    try
                                    {
                                        playerDetail.Value.PersonalSource.Cancel();
                                    }
                                    catch (ObjectDisposedException)
                                    { }
                                }
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
                                    await SendClientMessageAsync(JsonSerializer.Serialize(notifyOpponentDisconnectCommand), playerWaitingForOpponent.Client!, playerWaitingForOpponent.CombinedSource!.Token);
                                }
                                catch (Exception e) when (e is OperationCanceledException || e is IOException || e is ObjectDisposedException || e is InvalidOperationException)
                                {
                                    matchedPlayers.Remove(playerWaitingForOpponent);

                                    if (e is InvalidOperationException)
                                    {
                                        Console.WriteLine("[Server]: Monitor LFG: Failed to send opponent disconnect message.");
                                    }

                                    try
                                    {
                                        if (e is not ObjectDisposedException) playerWaitingForOpponent.PersonalSource.Cancel();
                                    }
                                    catch (ObjectDisposedException)
                                    { }
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
            catch (OperationCanceledException)
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
    /// <exception cref="InvalidOperationException"></exception>
    public static async Task SendClientMessageAsync(string message, TcpClient client, CancellationToken token)
    {
        byte[] msg = Encoding.ASCII.GetBytes(message);
        List<byte> constructedMessage = BitConverter.GetBytes(msg.Length).ToList();
        constructedMessage.AddRange(msg);
        byte[] msgConverted = constructedMessage.ToArray();

        await client.GetStream().WriteAsync(msgConverted, token);
    }

    /// <summary>
    /// Asynchronously waits for messages from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream"><see cref="NetworkStream"/> that is awaited for its responses.</param>
    /// <param name="token">CancellationToken used to cancel asynchronous operations.</param>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="token"/> source is cancelled.</exception>
    /// <exception cref="IOException">Thrown if something goes wrong with <paramref name="stream"/>.ReadAsync().</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="token"/> is invoked while using .ReadAsync().</exception>
    public static async Task<ServerCommand> RecieveCommandFromStreamAsync(NetworkStream stream, CancellationToken token)
    {
        var builder = new StringBuilder();
        int responseByteCount;

        do
        {
            // Incoming messages contain the length of the message in bytes within the first 4 bytes.
            byte[] adjustableBytesReciever = new byte[sizeof(int)];
            int totalRecieved = -1 * adjustableBytesReciever.Length, incomingMessageByteCount = 0;
            bool byteCountRecieved = false;

            do
            {
                do
                {
                    if (stream.DataAvailable && !token.IsCancellationRequested)
                    {
                        // If a token passed to ReadAsync is cancelled then the connection will be closed.
                        responseByteCount = await stream.ReadAsync(adjustableBytesReciever, CancellationToken.None);
                        break;
                    }
                    else
                    {
                        await Task.Delay(400, token);
                    }
                } while (true);

                if (responseByteCount > 0)
                {
                    if (!byteCountRecieved)
                    {   // Ensure first 4 bytes are a number.
                        // Gets the byte count of the incoming data and sizes the bytes array to accomadate. 
                        if (responseByteCount == sizeof(int))
                        {
                            try
                            {   // Verify that the first 4 bytes are a number.
                                incomingMessageByteCount = BitConverter.ToInt32(adjustableBytesReciever, 0);
                            }
                            catch (Exception)
                            {
                                continue;
                            }
                            totalRecieved += responseByteCount;
                            byteCountRecieved = true;
                            // Resize the array to fit incoming data.
                            adjustableBytesReciever = new byte[incomingMessageByteCount];
                        }
                    }
                    else
                    {
                        string textSection = Encoding.ASCII.GetString(adjustableBytesReciever, 0, responseByteCount);
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
    private async Task ListenForPlayerResponseAsync(Player user)
    {
        NetworkStream stream = user.Client.GetStream();

        (bool clientDisconnected, bool userRegistered) = (false, false);
        try
        {
            //using var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(ServerTasksCancellationToken, user.PersonalSource.Token);
            while (!clientDisconnected && !(user.CombinedSource?.IsCancellationRequested ?? true))
            {
                ServerCommand clientResponse = await RecieveCommandFromStreamAsync(stream, user.CombinedSource.Token).ConfigureAwait(false);

                if (clientResponse is not null)
                {
                    if (clientResponse.CMD == CommandType.RegisterUser && !userRegistered)
                    {
                        try
                        {
                            user.AssignName(clientResponse.Name! + $" > {user.ServerAssignedID}");
                            userRegistered = true;
                            Console.WriteLine($"[Server]: {user.Name} has registered.");
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
                        if (_startedGames.TryGetValue(clientResponse.GameIdentifier, out TrackedGame? currentGame))
                        {
                            Player opposingUser = currentGame.AssociatedPlayers[GameEnvironment.ReturnOppositeTeam(clientResponse.MoveDetails.Value.SubmittingTeam)];
                            try
                            {
                                await SendClientMessageAsync(JsonSerializer.Serialize(clientResponse), opposingUser.Client, opposingUser.PersonalSource.Token);
                            }
                            catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is OperationCanceledException)
                            {
                                // Failed to notify user.
                                if (!ServerTasksCancellationToken.IsCancellationRequested)
                                {
                                    try
                                    {
                                        opposingUser.PersonalSource.Cancel();
                                    }
                                    catch (ObjectDisposedException)
                                    { }
                                    // If the opposingUser isn't reachable send player notification that the opponent couldn't be reached.
                                    // Errors thrown here will be caught in outermost catch statement.
                                    var opponentDisconnectedCommand = new ServerCommand(CommandType.OpponentClientDisconnected, currentGame.GameID);
                                    await SendClientMessageAsync(JsonSerializer.Serialize(opponentDisconnectedCommand), user.Client, CancellationToken.None);
                                }
                            }
                        }
                    }
                    else if (_endGameCommands.Contains(clientResponse.CMD) && _startedGames.TryGetValue(clientResponse.GameIdentifier, out TrackedGame? currentGame))
                    {
                        _startedGames.TryRemove(new KeyValuePair<int, TrackedGame>(currentGame!.GameID, currentGame));
                    }
                    else if (clientResponse.CMD == CommandType.ClientDisconnecting)
                    {
                        user.PersonalSource.Cancel();
                        clientDisconnected = true;
                        Console.WriteLine($"[Server]: {user.Name} has sent disconnect.");
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException || e is OperationCanceledException || e is ObjectDisposedException || e is TaskCanceledException)
        {   // Client has likely disconnected.
            clientDisconnected = true;
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e);
            throw;
        }
        catch (Exception e)
        {
            Console.WriteLine("[Server]: Unhandled exception in ListenForPlayerResponsesAsync\n" + e);
            throw;
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
                foreach (TrackedGame game in gamesToDisconnect)
                {
                    bool gameFound = _startedGames.TryRemove(new KeyValuePair<int, TrackedGame>(game.GameID, game));
                    // If !_serverTasksCancellationToken.IsCancellationRequested is true then the server is shutting down and there is no need to notify the opponent.
                    if (gameFound && !ServerTasksCancellationToken.IsCancellationRequested)
                    {
                        var clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);
                        Player opposingUser = user.Equals(game.WhitePlayer) ? game.BlackPlayer : game.WhitePlayer;

                        try
                        {
                            gameEndingNotificationTasks.Add(SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), opposingUser.Client, opposingUser.PersonalSource.Token));
                        }
                        catch (ObjectDisposedException)
                        {   // opposingUser.Token has been Disposed.
                        }
                    }
                }

                try
                {
                    if (gameEndingNotificationTasks.Any()) await Task.WhenAll(gameEndingNotificationTasks);
                }
                catch (Exception)
                {
                }
            }
            await ClientRemovalAsync(user);
        }
    }
    /// <summary>
    /// Asynchronoulsy pings <paramref name="clientToPing"/> until it can no longer be reached or <paramref name="pingCancellationToken"/>.IsCancellationRequested. 
    /// </summary>
    /// <param name="clientToPing"><see cref="TcpClient"/> that will be pinged in a loop.</param>
    /// <param name="sourceToInvoke"><seealso cref="CancellationTokenSource"/> that will be cancelled if <paramref name="clientToPing"/> cannot be reached.</param>
    /// <param name="pingCancellationToken"><see cref="CancellationToken"/> used to cancel this task.</param>
    /// <returns>An async Task.</returns>
    public static async Task PingClientAsync(TcpClient clientToPing, CancellationTokenSource sourceToInvoke, CancellationToken pingCancellationToken)
    {
        try
        {
            while (!pingCancellationToken.IsCancellationRequested)
            {
                if (await IsClientActiveAsync(clientToPing, pingCancellationToken))
                {
                    await Task.Delay(1000 * 10, pingCancellationToken);
                }
                else
                {
                    sourceToInvoke.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // See pingCancellationToken.
        }
        catch (ObjectDisposedException)
        {
            // sourceToInvoke or pingCancellationToken was disposed.
        }
    }
}
