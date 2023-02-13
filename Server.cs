namespace Pieces;

using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;
using System;

public class Server
{
    public enum CommandType
    {
        ClientDisconnected,
        NewMove,
        DisconnectClient,
        StartGameInstance,
        Defeat,
        Winner,
        Draw,
        ServerAvailabilityTest,
        RegisterForGame
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
        public ServerCommand(CommandType cmdType, int gameID=0, MovementInformation? moveDetails = null, Team? assignedTeam = null)
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

    /// <value>Boolean representation of whether or not the <c>Server</c> instance is accepting new clients.</value>
    private bool _acceptingNewClients = false;
    /// <value>List of connected <c>TcpClients</c> to the current <c>Server</c> instance. Limited to 2.</value>
    private List<TcpClient> _connectedClients = new();
    /// <value>The <c>GameEnvironment</c> inatance used with the current server.</value>
    private GameEnvironment? _newGame;
    /// <value>Integer used to specify which instance of the <c>Server</c> is being targeted.</value>
    private static int _gameID = 0;
    /// <value>The current <c>Server</c> instance.</value>
    public int GameID = 0;
    /// <value>Listens for user responses and connections.</value>
    private TcpListener _gameServer;
    public Server()
    {
        // Set the TcpListener on port 13000.
        int port = 13000;
        IPAddress localAddr = IPAddress.Parse("127.0.0.1");
        // TcpListener server = new TcpListener(port);
        _gameServer = new TcpListener(localAddr, port);
        (_acceptingNewClients, GameID) = (true, ++_gameID);
    }
    public async Task<bool> StartServerAsync()
    {
        try
        {
            // Start listening for client requests.
            _gameServer.Start();
            
            var userHandler = new List<Task<bool>>();
            // Enter the listening loop.
            for (int i = 0; i < 2; i++)
            {
                if (_acceptingNewClients)
                {
                    // Perform a blocking call to accept requests.
                    // You could also use server.AcceptSocket() here.

                    using TcpClient client = _gameServer.AcceptTcpClient();

                    _connectedClients.Add(client);

                    if (_connectedClients.Count == 2)
                    {
                        // [To Do: Verify that both clients are still connected.]

                        //throw new NotImplementedException("Client connection confirmation needs to be implemented.");

                        _acceptingNewClients = false;

                        var playerOne = new Player(_connectedClients[0]);
                        var playerTwo = new Player(_connectedClients[1]);

                        _newGame = new GameEnvironment(playerOne, playerTwo);

                        foreach (var player in new Player[] { playerOne, playerTwo })
                        {
                            userHandler.Add(InitiateGameWithClientAsync(player));
                        }
                    }
                }

            }
            await Task.WhenAny(userHandler);
        }
        catch (SocketException e)
        {
            Console.WriteLine("SocketException: {0}", e);
        }
        finally
        {
             _gameServer.Stop();
        }

        return true;
    }

    /// <summary>
    /// Sends a given <paramref name="client"/> a message.
    /// </summary>
    /// <param name="client"><c>TcpClient</c> that is sent a message.</param>
    /// <param name="message">Message to be sent to <paramref name="client"/>.</param>
    private static void SendClientMessage(string message, TcpClient client)
    {
        NetworkStream stream = client.GetStream();

        byte[] msg = Encoding.ASCII.GetBytes(message);

        stream.WriteAsync(msg, 0, msg.Length);
    }

    /// <summary>
    /// Handles responses from a <paramref name="user"/> asynchronously.
    /// </summary>
    private async Task<bool> InitiateGameWithClientAsync(Player user)
    {
        var clientCommand = new ServerCommand(CommandType.StartGameInstance, GameID, assignedTeam: user.CurrentTeam);

        SendClientMessage(JsonSerializer.Serialize(clientCommand), user.Client!);

        Player opposingUser = (user == _newGame!.WhitePlayer) ? _newGame.BlackPlayer! : _newGame.WhitePlayer!;

        string data;

        NetworkStream stream = user.Client!.GetStream();

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
                    if (deserializedData.GameIdentifier == GameID)
                    {
                        if (deserializedData.CMD == CommandType.NewMove && deserializedData.MoveDetails != null)
                        {
                            _newGame!.SubmitFinalizedChange((MovementInformation)deserializedData.MoveDetails);
                            // Send back a response to the opposing player.
                            opposingUser.Client.GetStream().Write(bytes, 0, bytes.Length);

                            if (_newGame.IsKingCheckMated(_newGame.ReturnKing(opposingUser.CurrentTeam)))
                            {
                                var clients = new[] { opposingUser.Client, user.Client };
                                var linkedCommands = new[] { CommandType.Defeat, CommandType.Winner };
                                var clientCommands = clients.Zip(linkedCommands, (c, lc) => new { Client = c, Command = lc });

                                foreach (var msg in clientCommands)
                                {
                                    clientCommand = new ServerCommand(msg.Command, GameID);
                                    SendClientMessage(JsonSerializer.Serialize(clientCommand), msg.Client);
                                }

                                gameFinished = true;
                                break;
                            }
                            else if (_newGame.IsStalemate())
                            {
                                // Send both connected clients a Draw command.
                                clientCommand = new ServerCommand(CommandType.Draw, GameID);

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
                            // Disconnect the other connected client.                      
                            clientCommand = new ServerCommand(CommandType.DisconnectClient, GameID);
                            SendClientMessage(JsonSerializer.Serialize(clientCommand), opposingUser.Client);
                            clientDisconnected = true;
                            break;
                        }
                    }
                }
            }

            if (clientDisconnected || gameFinished) break;
        }

        return true;

    }
}