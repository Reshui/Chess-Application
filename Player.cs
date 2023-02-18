namespace Pieces;

using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using static Pieces.Server;
public class Player
{
    /// <summary>IP address of host server.</summary>
    private readonly string _hostAddress;
    /// <summary>Port to connect to the server on.</summary>
    private readonly int _hostPort;

    /// <summary>Connection to the server if it exists used by the <see cref="Server"/> class.</summary>
    public TcpClient Client
    {
        get { return _client ?? throw new NullReferenceException(nameof(Client)); }
        init => _client = value;
    }
    private TcpClient? _client;

    /// <summary>Server connected to the current instance of the <see cref="Player"/> class.</summary>
    private readonly TcpClient? _connectedServer;
    private Dictionary<int, GameEnvironment> _activeGames = new();
    private bool _serverIsConnected { get; set; } = false;
    public int ServerAssignedID { get; init; }

    /// <summary>Static variable used by the <see cref="Server"/> class to generate a value for <see cref="ServerAssignedID"/> via a constructor.</summary>
    private static int _instanceCount = 0;
    /// <summary>
    /// Client-side constructor.
    /// </summary>
    public Player()
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
    }
    /// <summary>
    /// Constructor used by the Server class to track clients.
    /// </summary>
    public Player(TcpClient client)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        Client = client;
        ServerAssignedID = ++_instanceCount;
    }

    /// <summary>
    /// Asynchonously connects to a server and starts waiting for server responses.
    /// </summary>
    public async void StartListeningAsync(CancellationToken token)
    {
        try
        {
            using TcpClient _connectedServer = new TcpClient(_hostAddress, _hostPort);
            _serverIsConnected = true;
            // Get a client stream for reading and writing.
            NetworkStream stream = _connectedServer.GetStream();

            // Tell the server to mark the TcpClient as looking for group.
            ServerCommand markAsLFGCommand = new ServerCommand(CommandType.LookingForGame);
            byte[] initialCommandBytes = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(markAsLFGCommand));
            stream.Write(initialCommandBytes, 0, initialCommandBytes.Length);

            while (!token.IsCancellationRequested)
            {
                byte[] bytes = new byte[256];
                var builder = new StringBuilder();
                int bytesRead;

                do
                {
                    bytesRead = await stream.ReadAsync(bytes, token);
                    if (bytesRead > 0) builder.Append(Encoding.ASCII.GetString(bytes, 0, bytesRead));
                } while (!token.IsCancellationRequested && bytesRead > 0);

                if (builder.Length == 0) continue;

                ServerCommand? response = JsonSerializer.Deserialize<ServerCommand>(builder.ToString());

                if (response != null)
                {
                    int serverSideGameID = response.GameIdentifier;

                    if (response.CMD == CommandType.StartGameInstance)
                    {
                        var newGame = new GameEnvironment(serverSideGameID, (Team)response.AssignedTeam!);
                        _activeGames.Add(serverSideGameID, newGame);
                    }
                    else if (response.CMD == CommandType.ServerIsShuttingDown)
                    {
                        throw new NotImplementedException();
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames[serverSideGameID] != null)
                    {
                        _activeGames.Remove(serverSideGameID);
                        throw new NotImplementedException("Opponent disconnection hasn't been fully implemented.");
                    }
                    else if (response.CMD == CommandType.NewMove)
                    {
                        _activeGames[serverSideGameID].ChangeGameBoardAndGUI((MovementInformation)response.MoveDetails!);
                    }
                }
            }
        }
        catch (IOException e)
        {
            Console.WriteLine("Host has disconnected. " + e);
        }
        finally
        {
            _serverIsConnected = false;
        }
    }
    /// <summary>
    /// Submits a chess movement <paramref name="move"/> to the <see cref="_connectedServer"/> and updates the relevant <see cref="GameEnvironment"/> instance.
    /// </summary>
    /// <param name="move">Chess movement to submit to the server.</param>
    /// <param name="serverSideGameID">ID used to target a specific <see cref="GameEnvironment"/> instance.</param>
    public async Task SubmitMoveToServerAsync(MovementInformation move, int serverSideGameID, CancellationToken token)
    {
        GameEnvironment targetedGameInstance = _activeGames[serverSideGameID];

        if (targetedGameInstance.ActiveTeam == move.SubmittingTeam)
        {
            targetedGameInstance.ChangeGameBoardAndGUI(move);

            if (_connectedServer != null && _serverIsConnected)
            {
                string submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, serverSideGameID, move));
                try
                {
                    await SendServerMessageAsync(submissionCommand, token);
                }
                catch (IOException)
                {
                    _serverIsConnected = false;
                    throw new NotImplementedException("Server not responding handling not implemented.");
                }
                catch (TaskCanceledException)
                {
                    throw new NotImplementedException("Task cancellation handling not implemented.");
                }
            }
            else if (_connectedServer!.Connected == false || _serverIsConnected == false)
            {
                _serverIsConnected = false;
                throw new Exception("Server is no longer connected.");
            }
        }
        else
        {
            throw new Exception("Movement submitted on the wrong turn.");
        }
    }

    /// <summary>
    /// Sends <see cref="_connectedServer"/> a given <paramref name="message"/> asynchronously.
    /// </summary>
    /// <param name="message">Serialized <see cref="Server.ServerCommand"/> to send.</param>
    /// <exception cref="IOException">Thrown if NetworkStream.WriteAsync generates an error.</exception>
    /// <exception cref="TaskCanceledException">Thrown if <paramref name="token"/> source is canceled.</exception> 
    public async Task SendServerMessageAsync(string message, CancellationToken token)
    {
        if (_connectedServer != null)
        {
            byte[] msg = Encoding.ASCII.GetBytes(message);
            NetworkStream stream = _connectedServer.GetStream();

            await stream.WriteAsync(msg, 0, msg.Length, token);
        }
    }
}