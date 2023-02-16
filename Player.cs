namespace Pieces;

using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using static Pieces.Server;
public class Player
{
    /// <value>IP address of host server.</value>
    private readonly string _hostAddress;
    /// <value>Port to connect to the server on.</value>
    private readonly int _hostPort;

    /// <value>Connection to the server if it exists used by the <c>Server</c> class.</value>
    public TcpClient Client
    {
        get
        {
            if (_client != null) return _client;
            else throw new NullReferenceException(nameof(Client));
        }
        init => _client = value;
    }
    private TcpClient? _client;

    /// <value>Server connected to the current instance of the <c>Player</c> class.</value>
    private readonly TcpClient? _connectedServer;
    private Dictionary<int, GameEnvironment> _activeGames = new();
    private bool _serverIsConnected { get; set; } = false;
    public int ServerAssignedID { get; init; }
    private static int _instanceCount = 0;
    /// <summary>
    /// Constructor used by clients connected to a host server.
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
    public async void StartListening(CancellationToken token)
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

            while (true)
            {
                byte[] bytes = new byte[256];
                var builder = new StringBuilder();
                int bytesRead;

                do
                {
                    bytesRead = await stream.ReadAsync(bytes, token);
                    if (bytesRead > 0) builder.Append(Encoding.ASCII.GetString(bytes, 0, bytesRead));
                } while (!token.IsCancellationRequested && bytesRead > 0);

                if (token.IsCancellationRequested) return;

                ServerCommand? response = JsonSerializer.Deserialize<ServerCommand>(builder.ToString());

                if (response != null)
                {
                    int serverSideGameID = response.GameIdentifier;

                    if (response.CMD == CommandType.StartGameInstance)
                    {
                        var newGame = new GameEnvironment(serverSideGameID, (Team)response.AssignedTeam!);
                        _activeGames.Add(serverSideGameID, newGame);
                    }
                    else if (response.CMD == CommandType.OpponentClientDisconnected && _activeGames[serverSideGameID] != null)
                    {
                        throw new NotImplementedException("Opponent disconnection hasn't been implemented.");
                        //_activeGames.Remove(serverSideGameID);
                    }
                    else if (response.CMD == CommandType.NewMove && _activeGames[serverSideGameID] != null)
                    {
                        UpdateOpponentMove((MovementInformation)response.MoveDetails!, serverSideGameID);
                    }
                    else if (response.CMD == CommandType.Defeat && _activeGames[serverSideGameID] != null)
                    {

                    }
                    else if (response.CMD == CommandType.Winner && _activeGames[serverSideGameID] != null)
                    {

                    }
                    else if (response.CMD == CommandType.Draw && _activeGames[serverSideGameID] != null)
                    {

                    }
                }
            }
        }
        catch (SocketException e)
        {
            Console.WriteLine("Host has disconnected. " + e);
        }
        finally
        {
            _serverIsConnected = false;
        }
    }
    /// <summary>
    /// Submits a chess movement <paramref name="move"/> to the <paramref name="_connectedServer"/> as well as updating the local version of the game.
    /// </summary>
    /// <param name="move">Chess movement to submit to the server.</param>
    /// <param name="serverSideGameID">ID used to target a specific <c>GameEnvironment</c> instance.</param>
    public async Task SubmitMoveToServerAsync(MovementInformation move, int serverSideGameID, CancellationToken token)
    {
        GameEnvironment targetedGameInstance = _activeGames[serverSideGameID];

        targetedGameInstance.SubmitFinalizedChange(move);

        if (targetedGameInstance.CanBeInteractedWith && _connectedServer != null && _serverIsConnected && !targetedGameInstance.GameEnded)
        {
            targetedGameInstance.CanBeInteractedWith = false;

            string submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, serverSideGameID, move));

            await SendServerMessageAsync(submissionCommand, token);

        }
        else if (_connectedServer!.Connected == false || _serverIsConnected == false || targetedGameInstance.GameEnded)
        {
            throw new Exception("Server is no longer connected.");
        }
    }
    /// <summary>
    /// Updates a client-side <c>GameEnvironment</c> instance when a movement is recieved from <paramref name="_connectedServer"/>.
    /// </summary>
    /// <param name="enemyMove">Board movement used to update a <c>GameEnvironment</c> instance.</param>
    /// <param name="serverSideGameID">ID number used to target a specific <c>GameEnvironment</c> instance within <paramref name="_activeGames"/>.</param>
    public void UpdateOpponentMove(MovementInformation enemyMove, int serverSideGameID)
    {
        try
        {
            GameEnvironment targetedGameInstance = _activeGames[serverSideGameID];

            if (targetedGameInstance != null && targetedGameInstance.Squares != null)
            {
                targetedGameInstance.SubmitFinalizedChange(enemyMove);

                if (enemyMove.SecondaryPiece != null)
                {
                    ChessPiece secPiece = enemyMove.SecondaryPiece;
                    PictureBox? secBox = targetedGameInstance.Squares[secPiece.ReturnLocation(0), secPiece.ReturnLocation(1)];

                    if (secBox != null)
                    {
                        if (enemyMove.CapturingSecondary)
                        {
                            secBox.Image = null;
                        }
                        else if (enemyMove.CastlingWithSecondary)
                        {
                            targetedGameInstance.Squares[(int)enemyMove.SecondaryNewLocation.X, (int)enemyMove.SecondaryNewLocation.Y]!.Image = secBox.Image;
                            secBox.Image = null;
                        }
                    }
                }

                ChessPiece enemyPiece = enemyMove.MainPiece;
                PictureBox? mainBox = targetedGameInstance.Squares[enemyPiece.ReturnLocation(0), enemyPiece.ReturnLocation(1)];

                if (mainBox != null)
                {
                    targetedGameInstance.Squares[(int)enemyMove.MainNewLocation.X, (int)enemyMove.MainNewLocation.Y]!.Image = mainBox.Image;
                    mainBox.Image = null;
                }
            }
        }
        catch (KeyNotFoundException e)
        {
            throw new NullReferenceException($"{nameof(serverSideGameID)} couldn't be found within {nameof(_activeGames)}.", e);
        }
    }
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