namespace Pieces;

using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using static Pieces.Server;
public class Player
{
    /// <value>Visual representation of <paramref name="CurrentGame"/>. Given value during constructo of <c>BoardGUI</c>.</value>
    public PictureBox?[,]? Squares;

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
        set => _client = value;
    }
    private TcpClient? _client;

    /// <value>Server connected to the current instance of the <c>Player</c> class.</value>
    private readonly TcpClient? _connectedServer;
    private Dictionary<int, GameEnvironment> _activeGames = new();
    private bool _serverIsConnected = false;

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
    }

    /// <summary>Asynchonously connects to a server and starts waiting for server responses.</summary>
    public async void StartListening()
    {
        var newCommand = new ServerCommand(CommandType.RegisterForGame);
        byte[] pingServer = Encoding.ASCII.GetBytes(JsonSerializer.Serialize(newCommand));

        try
        {
            using TcpClient _connectedServer = new TcpClient(_hostAddress, _hostPort);
            _serverIsConnected = true;
            // Get a client stream for reading and writing.
            NetworkStream stream = _connectedServer.GetStream();
            // Send the message to the connected TcpServer.
            stream.Write(pingServer, 0, pingServer.Length);

            string data;
            //bool gameInitialized = false;
            while (true)
            {
                byte[] bytes = new byte[256];
                int i;

                // Loop to receive all the data sent by the client.
                while ((i = await stream.ReadAsync(bytes)) != 0)
                {
                    data = Encoding.ASCII.GetString(bytes, 0, i);

                    ServerCommand? response = JsonSerializer.Deserialize<ServerCommand>(data);

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
    /// Submits a chess movement <paramref name="move"/> to the <paramref name="_connectedServer"/>.
    /// </summary>
    /// <param name="move">Chess movement to submit to the server.</param>
    public void SubmitMoveToServer(MovementInformation move, int serverSideGameID)
    {
        GameEnvironment targetedGameInstance = _activeGames[serverSideGameID];

        if (targetedGameInstance.CanBeInteractedWith && _connectedServer != null && _serverIsConnected && !targetedGameInstance.GameEnded)
        {
            targetedGameInstance.CanBeInteractedWith = false;

            var submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, serverSideGameID, move));

            byte[] data = Encoding.ASCII.GetBytes(submissionCommand);

            try
            {
                NetworkStream stream = _connectedServer.GetStream();
                stream.Write(data);
            }
            catch (SocketException e)
            {

            }
            catch (ObjectDisposedException e)
            {

            }
        }
        else if (_connectedServer!.Connected == false || _serverIsConnected == false || targetedGameInstance.GameEnded)
        {
            throw new Exception("Server is no longer connected.");
        }
    }
    /// <summary>
    /// Updates the GUI and <paramref name ="CurrentGame"/> when a movement is recieved from <paramref name="_connectedServer"/>.
    /// </summary>
    /// <param name="enemyMove">Board movement used to update the GUI and <paramref name="CurrentGame"/>.</param>
    public void UpdateOpponentMove(MovementInformation enemyMove, int serverSideGameID)
    {
        GameEnvironment targetedGameInstance = _activeGames[serverSideGameID];

        if (targetedGameInstance != null && Squares != null)
        {
            targetedGameInstance.SubmitFinalizedChange(enemyMove);

            if (enemyMove.SecondaryPiece != null)
            {
                ChessPiece secPiece = enemyMove.SecondaryPiece;
                PictureBox? secBox = Squares[secPiece.ReturnLocation(0), secPiece.ReturnLocation(1)];

                if (secBox != null)
                {
                    if (enemyMove.CapturingSecondary)
                    {
                        secBox.Image = null;
                    }
                    else if (enemyMove.CastlingWithSecondary)
                    {
                        Squares[(int)enemyMove.SecondaryNewLocation.X, (int)enemyMove.SecondaryNewLocation.Y]!.Image = secBox.Image;
                        secBox.Image = null;
                    }
                }
            }

            ChessPiece enemyPiece = enemyMove.MainPiece;
            PictureBox? mainBox = Squares[enemyPiece.ReturnLocation(0), enemyPiece.ReturnLocation(1)];

            if (mainBox != null)
            {
                Squares[(int)enemyMove.MainNewLocation.X, (int)enemyMove.MainNewLocation.Y]!.Image = mainBox.Image;
                mainBox.Image = null;
            }
        }
        else throw new NullReferenceException("Player.targetedGameInstance is null");
    }
    public void SendServerMessage(string message)
    {
        throw new NotImplementedException();
    }
}