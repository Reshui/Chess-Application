namespace Pieces;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

public class Server
{
    /// <summary>Maximum number of users allowed to connect to connect to the server.</summary>
    private const int MaxConnectionCount = 20;

    /// <summary>List of connected <see cref="TcpClient"/> instances to the current <see cref="Server"/> instance.</summary>
    private readonly ConcurrentDictionary<int, TrackedUser> _connectedPlayers = new();

    /// <summary>Listens for user responses and connections.</summary>
    private readonly TcpListener _gameServer;

    /// <summary>Dictionary of <see cref="TrackedGame"/> instances that have been started.</summary>
    private readonly ConcurrentDictionary<int, TrackedGame> _startedGames = new();

    /// <summary>Stores players that are currently waiting for a game.</summary>
    private ConcurrentQueue<TrackedUser> _waitingForGameLobby = new();

    /// <summary>Stores Tasks that listen for <see cref="TrackedUser"/> responses.</summary>
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

    public Server(int port, string ipAddress)
    {   // Set the TcpListener on port 13000.
        IPAddress localAddr = IPAddress.Parse(ipAddress);
        // TcpListener server = new TcpListener(port);
        _gameServer = new TcpListener(localAddr, port);
    }
    /// <summary>
    /// Starts asynchronous server tasks.
    /// </summary>
    public void StartServer()
    {
        _serverTasks.Add(ListenFornNewConnectionsAsync());
        _serverTasks.Add(ManageLookingForGroupLobbyAsync());
    }

    /// <summary>
    /// Starts <see cref="_gameServer"/> and creates <see cref="TrackedUser"/> instances for every <see cref="TcpClient"/> that attempts to connect.
    /// </summary>
    /// <returns>An asynchronous Task.</returns>
    private async Task ListenFornNewConnectionsAsync()
    {
        try
        {
            _gameServer.Start();
            Console.WriteLine("Server has started.");
            while (!ServerTasksCancellationToken.IsCancellationRequested)
            {
                TcpClient newClient = await _gameServer.AcceptTcpClientAsync(ServerTasksCancellationToken).ConfigureAwait(false);
                if (_connectedPlayers.Count < MaxConnectionCount)
                {
                    var newUser = new TrackedUser(newClient, ServerTasksCancellationToken);
                    _connectedPlayers.TryAdd(newUser.UserID, newUser);
                    _clientListeningTasks.TryAdd(newUser.UserID, HandlePlayerResponsesAsync(newUser));
                }
                else
                {
                    var deniedAccessCommand = new ServerCommand(CommandType.ServerFull, message: $"The server is full {MaxConnectionCount}/{MaxConnectionCount} users are connected.");
                    await SendClientMessageAsync(deniedAccessCommand, newClient, CancellationToken.None).ConfigureAwait(false);
                    newClient.Close();
                }
            }
        }
        catch (SocketException e)
        {
            ServerShutDownCancelSource.Cancel();
            Console.WriteLine("Server failed to start.\t" + e.Message);
        }
        finally
        {
            try
            {
                await Task.WhenAll(_clientListeningTasks.Values).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            _gameServer.Stop();
            Console.WriteLine("[Server]: Server has shutdown.");
        }
    }
    /// <summary>
    /// Stops all server tasks and notifies connected <see cref="TcpClient"/>s that the server has shut down.
    /// </summary>
    public async Task CloseServerAsync()
    {
        Console.WriteLine("[Server]: Attempting server shutdown.");
        ServerShutDownCancelSource.Cancel();
        _waitingForGameLobby.Clear();
        try
        {
            await Task.WhenAll(_serverTasks).ConfigureAwait(false);
        }
        catch (Exception)
        {
            foreach (var faultedTask in _serverTasks.Where(x => x.IsFaulted))
            {
                Console.WriteLine(faultedTask.Exception!.InnerException);
            }
        }
        finally
        {
            ServerShutDownCancelSource.Dispose();
        }
    }

    /// <summary>
    /// Removes <paramref name="user"/> from <see cref="_clientListeningTasks"/> and <see cref="_connectedPlayers"/> and sends a server shutdown message
    /// if conditions are met.
    /// </summary>
    /// <param name="user"><see cref="TrackedUser"/> instance that has its references removed.</param>
    private async Task DisconnectUserAsync(TrackedUser user)
    {
        if (_connectedPlayers.TryRemove(user.UserID, out _) && _clientListeningTasks.TryRemove(user.UserID, out _))
        {
            // Console.WriteLine($"[Server]: {user.UserName}  Cancellation Token Status; Server: ( {ServerTasksCancellationToken.IsCancellationRequested} ) , Personal: ( {user.PersonalCTS.IsCancellationRequested} ) , Connected: {user.UserClient.Connected}");

            // If server is shutting down then send a shutdown message.
            if (ServerTasksCancellationToken.IsCancellationRequested && !user.PersonalCTS.IsCancellationRequested && user.UserClient.Connected)
            {
                try
                {
                    var shutdownCommand = new ServerCommand(CommandType.ServerIsShuttingDown);
                    await SendClientMessageAsync(shutdownCommand, user.UserClient!, user.PersonalCTS.Token).ConfigureAwait(false);
                    Console.WriteLine($"[Server]: Shutdown notification sent => # {user.UserID}");
                }
                catch (IOException e)
                {
                    Console.WriteLine($"[Server]: {user.UserName} => couldn't be reached for shutdown notification.\n\n");
                    GetPossibleSocketErrorCode(e, true);
                }
                catch (InvalidOperationException e)
                {
                    Console.WriteLine($"[Server]: {user.UserName}: InvalidOperationException =>" + e.Message);
                }
                catch (Exception e)
                {
                    Console.WriteLine("[Server]: Server Failed to send shutdown message.\n\n" + e.Message);
                }
            }

            user.PersonalCTS.Cancel();
            if (_waitingForGameLobby.Contains(user))
            {   // Remove user from the LFG queue.
                _waitingForGameLobby = new ConcurrentQueue<TrackedUser>(_waitingForGameLobby.Where(x => !x.Equals(user)));
            }
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
                        TrackedUser opposingUser = user.Equals(game.WhitePlayer) ? game.BlackPlayer : game.WhitePlayer;
                        try
                        {
                            gameEndingNotificationTasks.Add(SendClientMessageAsync(clientCommand, opposingUser.UserClient, opposingUser.PersonalCTS.Token));
                        }
                        catch (ObjectDisposedException)
                        {   // opposingUser.Token has been Disposed.
                        }
                    }
                }

                try
                {
                    if (!ServerTasksCancellationToken.IsCancellationRequested)
                    {
                        await Task.WhenAll(gameEndingNotificationTasks).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                }
            }

            if (user.PingConnectedClientTask is not null)
            {
                try
                {
                    await user.PingConnectedClientTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                { }
            }
            user.Dispose();
            Console.WriteLine($"[Server]: # {user.UserID} has disconnected from the server.");
        }
    }

    /// <summary>
    /// Monitors <see cref="_waitingForGameLobby"/> for added <see cref="TrackedUser"/> instances and 
    /// notifies users when a game is available.
    /// </summary>
    private async Task ManageLookingForGroupLobbyAsync()
    {
        List<TrackedUser> matchedPlayers = new(2);

        while (!ServerTasksCancellationToken.IsCancellationRequested)
        {
            if (_waitingForGameLobby.TryDequeue(out TrackedUser? waitingPlayer))
            {
                matchedPlayers.Add(waitingPlayer);

                if (matchedPlayers.Count == 2)
                {
                    if (matchedPlayers[0].UserID == matchedPlayers[1].UserID)
                    {
                        matchedPlayers.RemoveAt(1);
                        continue;
                    }
                    bool bothPlayersAvailable = true;

                    List<Task<bool>> clientPingingTasks = new(2);
                    foreach (TrackedUser user in matchedPlayers)
                    {
                        try
                        {
                            clientPingingTasks.Add(IsClientActiveAsync(user.UserClient, user.ServerCombinedCTS!.Token));
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

                    while (!ServerTasksCancellationToken.IsCancellationRequested && bothPlayersAvailable && clientPingingTasks.Count > 0)
                    {
                        var completedTask = await Task.WhenAny(clientPingingTasks).ConfigureAwait(false);
                        TrackedUser user = matchedPlayers[clientPingingTasks.IndexOf(completedTask)];

                        if (!completedTask.Result || !_connectedPlayers.ContainsKey(user.UserID))
                        {
                            matchedPlayers.Remove(user);
                            // If error then Client is being actively removed.
                            try
                            {
                                user.PersonalCTS.Cancel();
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
                        var playersAlertedForGame = new List<TrackedUser>(2);
                        // Inform player code that it should start a GameEnvironment instance.
                        foreach (KeyValuePair<Team, TrackedUser> playerDetail in newGame.AssociatedPlayers)
                        {
                            string nameOfOpponent = playerDetail.Key.Equals(Team.Black) ? newGame.WhitePlayer.UserName! : newGame.BlackPlayer.UserName!;
                            var startGameCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key, opponentName: nameOfOpponent);
                            bool success = false;
                            try
                            {
                                await SendClientMessageAsync(startGameCommand, playerDetail.Value.UserClient, playerDetail.Value.ServerCombinedCTS!.Token).ConfigureAwait(false);
                                playersAlertedForGame.Add(playerDetail.Value);
                                success = true;
                            }
                            catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException)
                            {

                            }
                            catch (InvalidOperationException e)
                            {
                                Console.WriteLine("[Server]: Monitor LFG: InvalidOperationException while attempting start game notification.  " + e.Message);
                            }
                            catch (IOException e)
                            {
                                GetPossibleSocketErrorCode(e, true);
                            }
                            catch (Exception)
                            {
                            }

                            if (!success)
                            {
                                // Failed to message client or client is leaving the server.
                                matchedPlayers.Remove(playerDetail.Value);
                                bothPlayersAvailable = false;
                                try
                                {
                                    playerDetail.Value.PersonalCTS.Cancel();
                                }
                                catch (ObjectDisposedException)
                                { }
                                break;
                            }
                        }
                        // If there are no problems with notifying both players to start the game then track the game and clear the matchedPlayers list.
                        if (bothPlayersAvailable)
                        {
                            _startedGames.TryAdd(newGame.GameID, newGame);
                            matchedPlayers.Clear();
                        }
                        else if (playersAlertedForGame.Count > 0)
                        {
                            var notifyOpponentDisconnectCommand = new ServerCommand(CommandType.OpponentClientDisconnected, newGame.GameID);
                            // Inform players that are waiting for their opponent that they have disconnected.
                            foreach (TrackedUser playerWaitingForOpponent in playersAlertedForGame)
                            {
                                bool success = false, objectDisposed = false;
                                try
                                {
                                    await SendClientMessageAsync(notifyOpponentDisconnectCommand, playerWaitingForOpponent.UserClient, playerWaitingForOpponent.ServerCombinedCTS.Token).ConfigureAwait(false);
                                    success = true;
                                }
                                catch (OperationCanceledException)
                                {
                                }
                                catch (IOException e)
                                {
                                    GetPossibleSocketErrorCode(e, true);
                                }
                                catch (ObjectDisposedException)
                                {
                                    objectDisposed = true;
                                }
                                catch (InvalidOperationException)
                                {
                                    Console.WriteLine("[Server]: Monitor LFG: Failed to send opponent disconnect message.");
                                }
                                finally
                                {
                                    if (!objectDisposed && !success) playerWaitingForOpponent.PersonalCTS.Cancel();
                                    matchedPlayers.Remove(playerWaitingForOpponent);
                                }
                            }
                        }
                    }
                }
            }
            await Task.Delay(1000, ServerTasksCancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously tests if a given <paramref name="client"/> is still responsive.
    /// </summary>
    /// <returns>A <see cref="Task{bool}"/> that represents the async operation.: <see langword="true"/> if socket is still connected; otherwise, <see langword="false"/>.</returns>
    /// <param name="client"><see cref="TcpClient"/> that is tested.</param>
    public static async Task<bool> IsClientActiveAsync(TcpClient client, CancellationToken token)
    {
        // Sending only the prefix with a value of 0 will tell the reciever that the message is empty and should be ignored.
        byte[] data = BitConverter.GetBytes(0);
        try
        {
            await client.GetStream().WriteAsync(data.AsMemory(0, data.Length), token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Sends the <see cref="ServerCommand"/> <paramref name="commandToSend"/> in byte form to <paramref name="client"/>.
    /// </summary>
    /// <param name="client"><see cref="TcpClient"/> that is sent a message.</param>
    /// <param name="commandToSend">Command to send to <paramref name="client"/>.</param>
    /// <exception cref="IOException">Raised when an error occurs while attempting to use .WriteAsync.</exception>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public static async Task SendClientMessageAsync(ServerCommand commandToSend, TcpClient client, CancellationToken token)
    {
        string message = JsonSerializer.Serialize(commandToSend);
        byte[] msg = Encoding.ASCII.GetBytes(message);

        List<byte> constructedMessage = BitConverter.GetBytes(msg.Length).ToList();
        constructedMessage.AddRange(msg);
        byte[] msgConverted = constructedMessage.ToArray();

        await client.GetStream().WriteAsync(msgConverted.AsMemory(0, msgConverted.Length), token).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously waits for messages from <paramref name="stream"/>.
    /// </summary>
    /// <returns>An asynchronous <see cref="Task{ServerCommand}"/>.</returns>
    /// <param name="stream"><see cref="NetworkStream"/> that is awaited for its responses.</param>
    /// <param name="token"><see cref="CancellationToken"/> used to cancel asynchronous operations.</param>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="token"/> source is cancelled.</exception>
    /// <exception cref="IOException">Thrown if something goes wrong with <paramref name="stream.ReadAsync()"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="stream"/> is closed.</exception>
    public static async Task<ServerCommand?> RecieveCommandFromStreamAsync(NetworkStream stream, CancellationToken token)
    {
        var builder = new StringBuilder();
        int bufferByteCount = 0;
        do
        {
            // Incoming messages contain the length of the message in bytes within the first 4 bytes.
            byte[] buffer = new byte[sizeof(int)];

            int bytesReadFromMessage = -1 * buffer.Length, totalBytesToRead = 0, bufferOffset = 0, prefixBytesReadCount = 0;
            bool messageByteCountKnown = false;

            do
            {
                // While the entire message hasn't been recieved.
                do
                {
                    if (stream.DataAvailable && !token.IsCancellationRequested)
                    {   // If a token passed to ReadAsync is cancelled then the connection will be closed.
                        bufferByteCount = await stream.ReadAsync(buffer.AsMemory(bufferOffset), CancellationToken.None).ConfigureAwait(false);
                        if (bufferByteCount > 0) break;
                    }
                    await Task.Delay(700, token).ConfigureAwait(false);
                } while (!token.IsCancellationRequested);

                if (!token.IsCancellationRequested && bufferByteCount > 0)
                {
                    if (!messageByteCountKnown)
                    {
                        // Gets the byte count of the incoming data and sizes the bytes array to accomadate.
                        if ((prefixBytesReadCount += bufferByteCount) == sizeof(int))
                        {
                            totalBytesToRead = BitConverter.ToInt32(buffer, 0);
                            bytesReadFromMessage += prefixBytesReadCount;
                            messageByteCountKnown = true;
                            buffer = new byte[totalBytesToRead];
                            bufferOffset = 0;
                        }
                        else if (prefixBytesReadCount < sizeof(int))
                        {
                            bufferOffset += bufferByteCount;
                        }
                    }
                    else
                    {
                        string textSection = Encoding.ASCII.GetString(buffer, 0, bufferByteCount);
                        // If the expected number of bytes has been read then attempt to deserialize it into a ServerCommand
                        if ((bytesReadFromMessage += bufferByteCount) == totalBytesToRead)
                        {
                            if (builder.Length > 0) textSection = builder.Append(textSection).ToString();

                            try
                            {   // Making sure that the recieved text is a valid command.
                                ServerCommand? newCommand = JsonSerializer.Deserialize<ServerCommand>(textSection);
                                return newCommand!;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                                // Start waiting for another response.
                                if (builder.Length > 0) builder.Clear();
                                break;
                            }
                        }
                        else
                        {   // In case the message comes in multiple parts, store the partial text in a string builder.
                            builder.Append(textSection);
                        }
                    }
                }
            } while (!token.IsCancellationRequested && bytesReadFromMessage < totalBytesToRead);
        } while (!token.IsCancellationRequested);
        return null;
    }

    /// <summary>
    /// Handles responses from a <paramref name="connectedUser"/> client asynchronously.
    /// </summary>
    /// <remarks>Upon exiting the main loop <paramref name="connectedUser"/> will have all of its references on the server dealt with.</remarks>
    /// <param name="connectedUser"><see cref="TrackedUser"/> instance that is monitored for its responses.</param>
    private async Task HandlePlayerResponsesAsync(TrackedUser connectedUser)
    {
        try
        {
            NetworkStream stream = connectedUser.UserClient.GetStream();
            await SendClientMessageAsync(new ServerCommand(CommandType.WelcomeToServer), connectedUser.UserClient, ServerTasksCancellationToken).ConfigureAwait(false);
            connectedUser.PingConnectedClientTask = PingClientAsync(connectedUser.UserClient, connectedUser.PersonalCTS, connectedUser.ServerCombinedCTS.Token);

            CancellationToken token = connectedUser.ServerCombinedCTS.Token;
            bool userRegistered = false;

            while (!connectedUser.ServerCombinedCTS.IsCancellationRequested)
            {
                ServerCommand? clientResponse = await RecieveCommandFromStreamAsync(stream, token).ConfigureAwait(false);

                if (clientResponse is not null)
                {
                    if (clientResponse.CMD.Equals(CommandType.RegisterUser) && !userRegistered)
                    {
                        userRegistered = true;
                        connectedUser.UserName = clientResponse.Name;
                        Console.WriteLine($"[Server]: {connectedUser.UserName} has registered.");
                    }
                    else if (clientResponse.CMD.Equals(CommandType.ClientDisconnecting))
                    {
                        Console.WriteLine($"#{connectedUser.UserID} sent disconnect command.");
                        connectedUser.PersonalCTS.Cancel();
                    }
                    else if (userRegistered)
                    {
                        if (clientResponse.CMD.Equals(CommandType.LookingForGame) && !_waitingForGameLobby.Contains(connectedUser))
                        {   // Only add the user to the queue if they aren't already in it.
                            _waitingForGameLobby.Enqueue(connectedUser);
                        }
                        else if (clientResponse.CMD.Equals(CommandType.NewMove) && clientResponse.MoveDetails is not null)
                        {
                            // Send user response to the opposing player.                        
                            if (_startedGames.TryGetValue(clientResponse.GameIdentifier, out TrackedGame? currentGame) && currentGame.AssociatedPlayers.ContainsValue(connectedUser))
                            {
                                TrackedUser opposingUser = currentGame.AssociatedPlayers[GameEnvironment.ReturnOppositeTeam(clientResponse.MoveDetails.SubmittingTeam)];
                                try
                                {
                                    await SendClientMessageAsync(clientResponse, opposingUser.UserClient, opposingUser.PersonalCTS.Token).ConfigureAwait(false);
                                }
                                catch (Exception e) when (e is IOException || e is ObjectDisposedException || e is OperationCanceledException || e is InvalidOperationException)
                                {
                                    GetPossibleSocketErrorCode(e, true);
                                    // Send user that submitted the move a message to end the game as a draw.
                                    if (!ServerTasksCancellationToken.IsCancellationRequested)
                                    {
                                        try
                                        {
                                            opposingUser.PersonalCTS.Cancel();
                                        }
                                        catch (ObjectDisposedException)
                                        {

                                        }
                                        // If the opposingUser isn't reachable send player notification that the opponent couldn't be reached.
                                        // Errors thrown here will be caught in outermost catch statement.
                                        var opponentDisconnectedCommand = new ServerCommand(CommandType.OpponentClientDisconnected, currentGame.GameID);
                                        await SendClientMessageAsync(opponentDisconnectedCommand, connectedUser.UserClient, connectedUser.PersonalCTS.Token).ConfigureAwait(false);
                                    }
                                }
                            }
                            else
                            {
                                var invalidMoveCommand = new ServerCommand(CommandType.InvalidMove, clientResponse.GameIdentifier, clientResponse.MoveDetails);
                                await SendClientMessageAsync(invalidMoveCommand, connectedUser.UserClient, connectedUser.PersonalCTS.Token).ConfigureAwait(false);
                            }
                        }
                        else if (_endGameCommands.Contains(clientResponse.CMD) && _startedGames.TryGetValue(clientResponse.GameIdentifier, out TrackedGame? currentGame))
                        {
                            if (currentGame.AssociatedPlayers.ContainsValue(connectedUser))
                            {
                                if (_startedGames.TryRemove(new KeyValuePair<int, TrackedGame>(currentGame.GameID, currentGame)))
                                {
                                    // COnsider replacing woth uploading result of game to database.                                    
                                }
                            }
                            else
                            {
                                // User is sending a command for a game that they are not linked to.
                                var invalidMoveCommand = new ServerCommand(CommandType.InvalidMove, currentGame.GameID);
                                await SendClientMessageAsync(invalidMoveCommand, connectedUser.UserClient, connectedUser.PersonalCTS.Token).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Unhandled Command Recieved : " + clientResponse.ToString());
                        }
                    }
                }
            }
        }
        catch (IOException e)
        {
            GetPossibleSocketErrorCode(e, true);
        }
        catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException)
        {   // Client has likely disconnected.            
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message);
        }
        catch (Exception e)
        {
            Console.WriteLine("[Server]: Unhandled exception in ListenForPlayerResponsesAsync\n" + e);
            throw;
        }
        finally
        {
            await DisconnectUserAsync(connectedUser).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    /// <param name="e">Possible IOException to check for SocketExceptions.</param>
    /// <param name="thrownOnServer">Determines output string.</param>
    public static int? GetPossibleSocketErrorCode(Exception e, bool thrownOnServer)
    {
        int? errorCode = null;
        if (e is IOException && e.InnerException is SocketException exception)
        {
            Console.WriteLine($"[{(thrownOnServer ? "Server" : "Player")}] - IOException.SoceketException     Code: {exception.ErrorCode}");
            errorCode = exception.ErrorCode;
        }
        return errorCode;
    }
    /// <summary>
    /// Asynchronoulsy pings <paramref name="clientToPing"/> until it can no longer be reached or <paramref name="pingCancellationToken"/>.IsCancellationRequested returns <see langword="true"/>. 
    /// </summary>
    /// <param name="clientToPing"><see cref="TcpClient"/> that will be pinged in a loop.</param>
    /// <param name="sourceToInvoke"><seealso cref="CancellationTokenSource"/> that will be cancelled if <paramref name="clientToPing"/> cannot be reached.</param>
    /// <param name="pingCancellationToken"><see cref="CancellationToken"/> used to cancel this task.</param>
    /// <returns>An asynchronous Task.</returns>
    public static async Task PingClientAsync(TcpClient clientToPing, CancellationTokenSource sourceToInvoke, CancellationToken pingCancellationToken)
    {
        const byte SecondsBetweenPings = 30;
        try
        {
            while (!pingCancellationToken.IsCancellationRequested)
            {
                if (!await IsClientActiveAsync(clientToPing, pingCancellationToken).ConfigureAwait(false))
                {
                    sourceToInvoke.Cancel();
                    return;
                }
                await Task.Delay(1000 * SecondsBetweenPings, pingCancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Represents data related to connected users.
    /// </summary>
    private class TrackedUser : IDisposable
    {
        /// <summary>
        /// Static variable used to assign unique <see cref="UserID"/>s.
        /// </summary>
        private static int s_currentServerID = 0;
        /// <summary>
        /// Gets a unique identification code for the current instance.
        /// </summary>
        public int UserID { get; }
        /// <summary>
        /// Gets a <see cref="TcpClient"/> used to send messages to and recieve messages from the connected user.
        /// </summary>
        public TcpClient UserClient { get; }
        /// <summary>
        /// Gets or sets a username sent by <see cref="UserClient"/>.
        /// </summary>
        public string? UserName { get; set; } = null;
        /// <summary>
        /// Gets a <see cref="CancellationTokenSource"/> used to cancel server independent tasks.
        /// </summary>
        public CancellationTokenSource PersonalCTS { get; } = new();
        /// <summary>
        /// Gets a <see cref="CancellationTokenSource"/> that combines a unique <see cref="CancellationToken"/> and a cancellation token from the server.
        /// </summary>
        public CancellationTokenSource ServerCombinedCTS { get; }
        /// <summary>
        /// Gets or sets a Task that will ping <see cref="UserClient"/> until it can't be reacehd or a token is cancelled.
        /// </summary>
        public Task? PingConnectedClientTask { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TrackedUser"/> class.
        /// </summary>
        /// <param name="associatedClient">A <see cref="TcpClient"/> client used to recieve or send messages to a connected client.</param>
        /// <param name="serverToken">Server-side <see cref="CancellationToken"/> used to create a linked token source.</param>
        public TrackedUser(TcpClient associatedClient, CancellationToken serverToken)
        {
            UserID = ++s_currentServerID;
            UserClient = associatedClient;
            ServerCombinedCTS = CancellationTokenSource.CreateLinkedTokenSource(PersonalCTS.Token, serverToken);
        }
        public void Dispose()
        {
            PersonalCTS.Dispose();
            ServerCombinedCTS.Dispose();
            UserClient.Close();
        }
    }
    private class TrackedGame
    {
        /// <summary>Gets the <see cref="TrackedUser"/> associated with <see cref="Team.White"/>.</summary>
        public TrackedUser WhitePlayer { get => AssociatedPlayers[Team.White]; }
        /// <summary>Gets the <see cref="TrackedUser"/> associated with <see cref="Team.Black"/>.</summary>
        public TrackedUser BlackPlayer { get => AssociatedPlayers[Team.Black]; }
        /// <summary>Gets an identification code used to track relevant information for started <see cref="GameEnvironment"/> instances.</summary>
        public int GameID { get; }
        /// <summary>Holds <see cref="TrackedUser"/> instances keyed to their assigned team.</summary>
        public Dictionary<Team, TrackedUser> AssociatedPlayers { get; }
        /// <summary>Static variable used to track the number of tracked games on the server.</summary>
        private static int _gameID = 0;
        /// <summary>Random number generator used to assign teams to added <see cref="TrackedUser"/> instances.</summary>
        private static readonly Random _rand = new();
        /// <summary>Dictionary used to keep track of winners and losers.</summary>
        public Dictionary<Team, GameState> GameState { get; } = new()
        {
            {Team.Black,Pieces.GameState.Playing },
            {Team.White,Pieces.GameState.Playing }
        };

        /// <summary>Initializes a new instance of the <see cref="TrackedGame"/> class.</summary>       
        public TrackedGame(TrackedUser playerOne, TrackedUser playerTwo)
        {
            GameID = ++_gameID;
            var playerArray = new TrackedUser[] { playerOne, playerTwo };
            int whitePlayerIndex = _rand.Next(playerArray.Length);
            AssociatedPlayers = new(2)
            {
                {Team.White, playerArray[whitePlayerIndex]},
                {Team.Black, playerArray[whitePlayerIndex == 1 ? 0 : 1]}
            };
        }
    }
}
