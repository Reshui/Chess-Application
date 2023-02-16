namespace Pieces;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System;
using System.Collections.Concurrent;
public class Server
{
    /// <value>Boolean representation of whether or not the <c>Server</c> instance is accepting new clients.</value>
    private bool _acceptingNewClients { get; set; } = false;
    /// <value>List of connected <c>TcpClients</c> to the current <c>Server</c> instance. Limited to 2.</value>
    private ConcurrentDictionary<int, Player> _connectedPlayers = new();
    /// <value>Listens for user responses and connections.</value>
    private TcpListener _gameServer { get; init; }
    /// <value>Dictionary of <c>GameEnvironment</c> instances that have been started.</value>
    private ConcurrentDictionary<int, GameEnvironment> _startedGames = new();
    /// <value>Data structure used to store players that are currently waiting for a game.</value>
    private ConcurrentQueue<Player> _waitingForGameLobby = new();
    /// <value>Stores Tasks that listen for <c>Player</c> responses.</value>
    private ConcurrentDictionary<int, Task> _clientListeningTasks = new();
    /// <value>Dictionary of Cancellation Tokens keyed to a given user</value>
    private ConcurrentDictionary<int, CancellationTokenSource> _clientListeningCancellationTokens = new();
    public enum CommandType
    {
        ClientDisconnected,
        NewMove,
        OpponentClientDisconnected,
        StartGameInstance,
        Defeat,
        Winner,
        Draw,
        ServerAvailabilityTest,
        RegisterForGame,
        LookingForGame,
        ServerIsShuttingDown,
        TestingForResponse,
        PrepareBuffer
    }
    public class ServerCommand
    {
        /// <value><c>CommandType</c> enum that specifies what this ServerCommand is intended to do.</value>
        public CommandType CMD { get; init; }
        /// <value>Optional class field used when <c>CMD</c> == <c>CommandType.newMove</c>.</value>
        public MovementInformation? MoveDetails { get; init; } = null;
        /// <value>Optional field used to assign a client to a given team.</value>
        public Team? AssignedTeam { get; init; } = null;
        /// <value>Specifies which instance of the <c>Server</c> is being communicated with.</value>
        public int GameIdentifier { get; init; } = 0;
        public int BufferSizeToPrepare { get; init; } = 0;
        public ServerCommand(CommandType cmdType, int gameID = 0, MovementInformation? moveDetails = null, Team? assignedTeam = null, int bufferSizeToPrepare = 0)
        {
            CMD = cmdType;
            GameIdentifier = gameID;

            if (cmdType == CommandType.NewMove)
            {
                if (moveDetails != null) MoveDetails = moveDetails;
                else throw new ArgumentNullException(nameof(moveDetails), "A new move command has been submitted without a non-null MovementInformation struct.");
            }
            else if (cmdType == CommandType.StartGameInstance)
            {
                AssignedTeam = assignedTeam;
            }
            else if (cmdType == CommandType.PrepareBuffer)
            {
                BufferSizeToPrepare = bufferSizeToPrepare;
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
    public async Task<bool> StartServerAsync()
    {
        var waitForClientsSource = new CancellationTokenSource();

        try
        {
            var waitingToken = waitForClientsSource.Token;
            // Start listening for client requests.
            _gameServer.Start();

            Task clientListener = WaitForClientsAsync(waitingToken);

            List<Player> matchedPlayers = new() { Capacity = 2 };

            while (true)
            {
                if (_waitingForGameLobby.Count > 0)
                {
                    if (_waitingForGameLobby.TryDequeue(out Player? waitingPlayer) && await IsClientActiveAsync(waitingPlayer.Client.Client))
                    {
                        matchedPlayers.Add(waitingPlayer);
                    }
                    else if (waitingPlayer != null && waitingPlayer.Client.Client.Connected == false)
                    {
                        await ClientRemovalAsync(waitingPlayer);
                    }

                    if (matchedPlayers.Count == 2)
                    {
                        var newGame = new GameEnvironment(matchedPlayers[0], matchedPlayers[1]);

                        _startedGames.TryAdd(newGame.GameID, newGame);

                        foreach (var playerDetail in newGame.AssociatedPlayers)
                        {
                            var clientCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);

                            if (_clientListeningCancellationTokens.ContainsKey(playerDetail.Value.ServerAssignedID))
                            {
                                await SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), playerDetail.Value.Client!, _clientListeningCancellationTokens[playerDetail.Value.ServerAssignedID].Token);
                            }
                        }
                        matchedPlayers.Clear();
                    }
                }
                await Task.Delay(500);
            }
        }
        catch (SocketException e)
        {
            Console.WriteLine("SocketException: {0}", e);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"\n{nameof(OperationCanceledException)} thrown\n");
        }
        finally
        {
            _gameServer.Stop();
            waitForClientsSource.Dispose();
        }

        return true;
    }
    /// <summary>
    /// Asynchronously waits for client connections.
    /// </summary>
    private async Task WaitForClientsAsync(CancellationToken token)
    {
        while (token.IsCancellationRequested == false)
        {
            TcpClient? newClient = await _gameServer.AcceptTcpClientAsync(token);

            if (token.IsCancellationRequested == false && newClient != null)
            {
                Player newPlayer = new Player(newClient);
                var cancellationSource = new CancellationTokenSource();
                _connectedPlayers.TryAdd(newPlayer.ServerAssignedID, newPlayer);
                _clientListeningCancellationTokens.TryAdd(newPlayer.ServerAssignedID, cancellationSource);
                _clientListeningTasks.TryAdd(newPlayer.ServerAssignedID, ProcessClientDataAsync(newPlayer, cancellationSource.Token));
            }
        }
    }
    /// <summary>
    /// Tests if a given Socket recieves a response.
    /// <summary>
    /// <returns>true if socket is still connected; false otherwise.</returns>
    private static async Task<bool> IsClientActiveAsync(Socket clientSocket)
    {
        var command = new ServerCommand(CommandType.TestingForResponse);
        string message = JsonSerializer.Serialize(command);
        byte[] data = Encoding.ASCII.GetBytes(message);
        bool blockingState = clientSocket.Blocking;

        try
        {
            clientSocket.Blocking = false;
            await clientSocket.SendAsync(data, 0);
        }
        catch (SocketException e)
        {
            // 10035 == WSAEWOULDBLOCK
            if (e.NativeErrorCode.Equals(10035))
            {
                Console.WriteLine("Still Connected, but the Send would block");
            }
            else
            {
                Console.WriteLine("Disconnected: error code {0}!", e.NativeErrorCode);
            }
        }
        finally
        {
            clientSocket.Blocking = blockingState;
        }

        return clientSocket.Connected;
    }

    /// <summary>
    /// Sends a given <paramref name="client"/> a message.
    /// </summary>
    /// <param name="client"><c>TcpClient</c> that is sent a message.</param>
    /// <param name="message">Message to be sent to <paramref name="client"/>.</param>
    private static async Task SendClientMessageAsync(string message, TcpClient client, CancellationToken token)
    {
        byte[] msg = Encoding.ASCII.GetBytes(message);
        NetworkStream stream = client.GetStream();

        await stream.WriteAsync(msg, 0, msg.Length, token);
    }

    /// <summary>
    /// Handles responses from a <paramref name="user"/> client asynchronously.
    /// </summary>
    /// <param name="user"><c>Player</c> instance that is monitored for its responses.</param>
    private async Task ProcessClientDataAsync(Player user, CancellationToken token)
    {
        NetworkStream stream = user.Client.GetStream();
        ServerCommand clientCommand;

        bool clientDisconnected = false;

        while (token.IsCancellationRequested == false)
        {
            var builder = new StringBuilder();
            int responseByteCount;
            byte[] bytes = new byte[256];

            do
            {   // Loop to receive all the data sent by the client.
                responseByteCount = await stream.ReadAsync(bytes, token);
                if (responseByteCount > 0) builder.Append(Encoding.ASCII.GetString(bytes, 0, responseByteCount));
            } while (token.IsCancellationRequested == false && responseByteCount > 0);

            if (token.IsCancellationRequested) return;

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

                        foreach (var msg in clientCommands)
                        {
                            clientCommand = new ServerCommand(msg.Command, currentGame.GameID);
                            await SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), msg.Client, token);
                        }

                        currentGame.GameEnded = true;
                        break;
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
                        break;
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

                    foreach (GameEnvironment game in gamesToDisconnect)
                    {
                        clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);
                        Player opposingUser = (user == game.AssociatedPlayers[Team.White]) ? game.AssociatedPlayers[Team.Black] : game.AssociatedPlayers[Team.White];

                        await SendClientMessageAsync(JsonSerializer.Serialize(clientCommand), opposingUser.Client, token);

                        _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(game.GameID, game));
                    }

                    _connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user));
                    clientDisconnected = true;
                    break;
                }
            }

            if (clientDisconnected) return;
        }
    }
    /// <summary>
    /// Removes <paramref name="user"/> from <paramref name="_clientListeningTasks"/> and <paramref name="_connectedPlayers"/>. 
    /// </summary>
    /// <param name="user">Player that has its references removed.</param>
    private async Task ClientRemovalAsync(Player user)
    {
        _clientListeningCancellationTokens[user.ServerAssignedID].Cancel();
        await _clientListeningTasks[user.ServerAssignedID];
        _clientListeningCancellationTokens[user.ServerAssignedID].Dispose();

        _connectedPlayers.TryRemove(new KeyValuePair<int, Player>(user.ServerAssignedID, user));
    }
}