namespace Pieces;

using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using static Pieces.Server;
public class Player
{
    /// <value>Visual representation of <paramref name="CurrentGame"/>. Given value during constructo of <c>BoardGUI</c>.</value>
    public PictureBox?[,]? Squares;

    /// <value> Team enum:Black or White.</value>
    public Team CurrentTeam
    {
        get { return _currentTeam; }
        set
        {
            bool teamIsValid = (from num in (Team[])Enum.GetValues(typeof(Team)) where num == value select num).Any();
            if (teamIsValid) _currentTeam = value;
        }
    }
    private Team _currentTeam;

    /// <value>IP address of host server.</value>
    private readonly string _hostAddress;

    /// <value>Port to connect to the server on.</value>
    private readonly int _hostPort;

    /// <value>This value is used for instance targeting.</value>
    public int GameID
    {
        get => _gameID;
        set => _gameID = value;
    }
    private int _gameID;

    /// <value>Publicly accessible property that represents if the current game is still playable.</value>
    public bool GameEnded
    {
        get
        {
            if (CurrentGame != null) return CurrentGame.GameEnded;
            else return false;
        }
        set => CurrentGame!.GameEnded = value;
    }

    /// <value>Publicly accessible property that represents whether the <c>Player</c> instance is allowed to make a move.</value>
    public bool CanMakeMove = false;

    public GameEnvironment? CurrentGame;

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
    private bool _serverIsConnected = false;
    public Player()
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
    }
    public Player(TcpClient client)
    {
        _hostPort = 13000;
        _hostAddress = "127.0.0.1";
        Client = client;
    }

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
                        if (response.CMD == CommandType.StartGameInstance)
                        {
                            CurrentGame = new GameEnvironment();
                            CurrentTeam = (Team)response.AssignedTeam!;
                            GameID = response.GameIdentifier;
                            if (CurrentTeam == Team.White) CanMakeMove = true;  
                        }
                        else if (response.CMD == CommandType.DisconnectClient && CurrentGame != null)
                        {
                            GameEnded = true;
                            CanMakeMove = false;
                        }
                        else if (response.CMD == CommandType.NewMove && CurrentGame != null)
                        {
                            UpdateOpponentMove((MovementInformation)response.MoveDetails!);
                            CanMakeMove = true;
                        }
                        else if (response.CMD == CommandType.Defeat && CurrentGame != null)
                        {
                            GameEnded = true;
                            CanMakeMove = false;
                        }
                        else if (response.CMD == CommandType.Winner && CurrentGame != null)
                        {
                            GameEnded = true;
                            CanMakeMove = false;
                        }
                        else if (response.CMD == CommandType.Draw && CurrentGame != null)
                        {
                            GameEnded = true;
                            CanMakeMove = false;
                        }
                    }

                    if (GameEnded) break;
                }

                if (GameEnded) break;
            }
        }
        catch (SocketException e)
        {
            Console.WriteLine("Host has disconnected. " + e);
        }
        finally
        {
            _serverIsConnected = false;
            CanMakeMove = false;
            GameEnded = true;
        }
    }
    public void SubmitMoveToServer(MovementInformation move)
    {
        if (CanMakeMove && _connectedServer != null && _serverIsConnected && !GameEnded)
        {
            CanMakeMove = false;
            var submissionCommand = JsonSerializer.Serialize(new ServerCommand(CommandType.NewMove, GameID, move));
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
        else if (_connectedServer!.Connected == false || _serverIsConnected == false || GameEnded)
        {
            throw new Exception("Server is no longer connected.");
        }
    }

    public void UpdateOpponentMove(MovementInformation enemyMove)
    {
        if (CurrentGame != null && Squares != null)
        {
            CurrentGame.SubmitFinalizedChange(enemyMove);

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
        else throw new NullReferenceException("Player.CurrentGame is null");
    }

    public void SendServerMessage(string message)
    {
        throw new NotImplementedException();
    }
}