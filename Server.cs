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
    private bool _acceptingNewClients = false;

    /// <value>List of connected <c>TcpClients</c> to the current <c>Server</c> instance. Limited to 2.</value>
    private List<Player> _connectedPlayers = new();

    /// <value>Listens for user responses and connections.</value>
    private TcpListener _gameServer;

    /// <value>Dictionary of <c>GameEnvironment</c> instances that have been started.</value>
    ConcurrentDictionary<int, GameEnvironment> _startedGames = new();

    /// <value>Data structure used to store players that are currently waiting for a game.</value>
    ConcurrentQueue<Player> _waitingForGameLobby = new();
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
        TestingForResponse
    }
    public class ServerCommand
    {
        /// <value><c>CommandType</c> enum that specifies what this ServerCommand is intended to do.</value>
        public CommandType CMD;
        /// <value>Optional class field used when <c>CMD</c> == <c>CommandType.newMove</c>.</value>
        public MovementInformation? MoveDetails = null;
        /// <value>Optional field used to assign a client to a given team.</value>
        public Team? AssignedTeam = null;
        /// <value>Specifies which instance of the <c>Server</c> is being communicated with.</value>
        public int GameIdentifier;
        public ServerCommand(CommandType cmdType, int gameID = 0, MovementInformation? moveDetails = null, Team? assignedTeam = null)
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

            var userHandler = new List<Task>();

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
                        _connectedPlayers.Remove(waitingPlayer);
                    }

                    if (matchedPlayers.Count == 2)
                    {
                        var newGame = new GameEnvironment(matchedPlayers[0], matchedPlayers[1]);

                        _startedGames.TryAdd(newGame.GameID, newGame);

                        foreach (var playerDetail in newGame.AssociatedPlayers)
                        {
                            var clientCommand = new ServerCommand(CommandType.StartGameInstance, newGame.GameID, assignedTeam: playerDetail.Key);
                            SendClientMessage(JsonSerializer.Serialize(clientCommand), playerDetail.Value.Client!);
                        }
                        matchedPlayers.Clear();
                    }
                }

                await Task.Delay(1000);
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
        List<Task> listeningTasks = new();

        while (token.IsCancellationRequested == false)
        {
            if (_gameServer.Pending() && _acceptingNewClients)
            {
                Player newPlayer = new Player(await _gameServer.AcceptTcpClientAsync());

                _connectedPlayers.Add(newPlayer);

                listeningTasks.Add(ProcessClientDataAsync(newPlayer));
            }

            await Task.Delay(2000);
        }
    }
    /// <summary>
    /// Tests if a given Socket recieves a response.
    /// <summary>
    /// <returns>true if socket is still connected; false otherwise.</returns>
    private static async Task<bool> IsClientActiveAsync(Socket client)
    {
        var command = new ServerCommand(CommandType.TestingForResponse);
        string message = JsonSerializer.Serialize(command);
        byte[] data = Encoding.ASCII.GetBytes(message);
        bool blockingState = client.Blocking;

        try
        {
            client.Blocking = false;
            await client.SendAsync(data, 0);
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
            client.Blocking = blockingState;
        }

        return client.Connected;
    }

    /// <summary>
    /// Sends a given <paramref name="client"/> a message.
    /// </summary>
    /// <param name="client"><c>TcpClient</c> that is sent a message.</param>
    /// <param name="message">Message to be sent to <paramref name="client"/>.</param>
    /// <exception cref=""></exception>
    private static void SendClientMessage(string message, TcpClient client)
    {
        NetworkStream stream = client.GetStream();

        byte[] msg = Encoding.ASCII.GetBytes(message);

        stream.WriteAsync(msg, 0, msg.Length);
    }

    /// <summary>
    /// Handles responses from a <paramref name="user"/> client asynchronously.
    /// </summary>
    private async Task ProcessClientDataAsync(Player user)
    {
        string data;

        NetworkStream stream = user.Client.GetStream();
        ServerCommand clientCommand;

        bool clientDisconnected = false, gameFinished = false;

        while (true)
        {
            byte[] bytes = new byte[256];
            int i;

            // Loop to receive all the data sent by the client.
            while ((i = await stream.ReadAsync(bytes)) != 0)
            {
                // Translate data bytes to a ASCII string.
                data = Encoding.ASCII.GetString(bytes, 0, i);

                ServerCommand? deserializedData = JsonSerializer.Deserialize<ServerCommand>(data);

                if (deserializedData != null)
                {
                    if (deserializedData.CMD == CommandType.LookingForGame)
                    {
                        _waitingForGameLobby.Enqueue(user);
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
                                SendClientMessage(JsonSerializer.Serialize(clientCommand), msg.Client);
                            }

                            gameFinished = true;
                            break;
                        }
                        else if (currentGame.IsStalemate())
                        {
                            // Send both connected clients a Draw command.
                            clientCommand = new ServerCommand(CommandType.Draw, currentGame.GameID);

                            foreach (var player in new Player[] { user, opposingUser })
                            {
                                SendClientMessage(JsonSerializer.Serialize(clientCommand), player.Client);
                            }

                            gameFinished = true;
                            break;
                        }
                    }
                    else if (deserializedData.CMD == CommandType.ClientDisconnected)
                    {
                        // Disconnect the other clients that this user is involved with.
                        List<GameEnvironment> gamesToDisconnect = (from gameKeyValue in _startedGames
                                                                   let game = gameKeyValue.Value
                                                                   where game.AssociatedPlayers[Team.White] == user || game.AssociatedPlayers[Team.Black] == user
                                                                   select game).ToList();
                        foreach (var game in gamesToDisconnect)
                        {
                            clientCommand = new ServerCommand(CommandType.OpponentClientDisconnected, game.GameID);
                            Player opposingUser = (user == game.AssociatedPlayers[Team.White]) ? game.AssociatedPlayers[Team.Black] : game.AssociatedPlayers[Team.White];
                            SendClientMessage(JsonSerializer.Serialize(clientCommand), opposingUser.Client);

                            _startedGames.TryRemove(new KeyValuePair<int, GameEnvironment>(game.GameID, game));
                        }

                        clientDisconnected = true;
                        break;
                    }
                }
            }

            if (clientDisconnected || gameFinished) break;
        }
    }
}