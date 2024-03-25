using Microsoft.VisualBasic.Logging;
using Pieces;

namespace Chess_GUi
{
    /*
    public delegate void StartServerHandlerAsync(TEventArgs e);

    public class MeTEventArgs : System.ComponentModel.AsyncCompletedEventArgs
    {
        public MyReturnType Result { get; }
    }
    */
    public partial class Form1 : Form
    {
        private Server? _hostedServer;
        private Player? _localPlayer;

        private bool _allowClose = false;
        /// <summary>
        /// Dictionary used to keep track of started games.
        /// </summary>
        private readonly Dictionary<string, BoardGUI> _boardGuiByName = new();

        // public event StartServerHandlerAsync ServerStart;
        public Form1()
        {
            InitializeComponent();
            this.FormClosing += new FormClosingEventHandler(this.ClosingEventsAsync!);
            AddControls();
        }
        private void AddControls()
        {
            var JoinLobbyBTN = new Button()
            {
                Size = StartServer.Size,
                Location = new Point(GameTracker.Left, GameTracker.Bottom + 20),
                Enabled = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Look For Game",
                Visible = false,
                Name = "LFG",
                ForeColor = Color.White
            };
            JoinLobbyBTN.Click += new EventHandler(JoinLobby_Click!);
            panel1.Controls.Add(JoinLobbyBTN);
        }

        private async void ClosingEventsAsync(object sender, FormClosingEventArgs evnt)
        {
            if (!_allowClose)
            {
                evnt.Cancel = true;
                if (_localPlayer is not null)
                {
                    try
                    {
                        await _localPlayer.CloseConnectionToServerAsync(true, false, false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    _localPlayer = null;

                    var btn = panel1.Controls["LFG"];
                    btn.Visible = false;
                    btn.Enabled = false;

                    if (_hostedServer is not null) await Task.Delay(1000);
                }

                if (_hostedServer is not null)
                {
                    await _hostedServer.CloseServerAsync();
                    _hostedServer = null;
                }
                _allowClose = true;
                this.Close();
            }
        }
        private void StartServer_Click(object sender, EventArgs e)
        {
            if (_hostedServer is null)
            {
                _hostedServer = new Server(13_000, "127.0.0.1");
                _hostedServer.StartServer();
                StartServer.BackColor = Color.Wheat;
                StartServer.ForeColor = Color.Black;
                StartServer.Enabled = false;
            }
        }

        private void JoinServer_Click(object sender, EventArgs e)
        {
            string userName = "Default name";// UserName.Text;

            if (userName != string.Empty && _localPlayer is null)
            {
                _localPlayer = new Player(this, new CancellationTokenSource(), 13_000, "127.0.0.1");
                _localPlayer.AssignName(userName);

                if (_localPlayer.TryJoinServer())
                {
                    JoinServer.BackColor = Color.FromArgb(144, 12, 63);
                    JoinServer.ForeColor = Color.AntiqueWhite;

                    JoinServer.Enabled = false;
                    StartServer.Enabled = false;
                    panel1.Controls["LFG"].Enabled = true;
                    panel1.Controls["LFG"].Visible = true;
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }

        private void JoinLobby_Click(object sender, EventArgs e)
        {
            // _localPlayer is null if not connected to a server.
            _localPlayer?.JoinWaitingLobby();
        }

        public void AddGame(GameEnvironment newGame)
        {
            if (_localPlayer is not null)
            {
                var trackedChessBoardGUI = new BoardGUI(_localPlayer, newGame, newGame.GameID.ToString());

                _boardGuiByName.Add(trackedChessBoardGUI.Name, trackedChessBoardGUI);
                // Added the created GUI to the control.
                MainView.Controls.Add(trackedChessBoardGUI);
                trackedChessBoardGUI.Size = MainView.Size;
                MainView.Controls[trackedChessBoardGUI.Name].BringToFront();
                GameTracker.Items.Add(trackedChessBoardGUI.Name);
            }
        }

        public void UpdateGameInterface(MovementInformation newMove, int gameID)
        {
            _boardGuiByName[gameID.ToString()].UpdateBoardBasedOnMove(newMove);
        }

        /// <summary>
        /// Disables any GUI that uses <paramref name="gameID"/> as a name.
        /// </summary>
        /// <param name="gameID"><see cref="GameEnvironment.GameID"/> of game to target.</param>
        public void DisableGame(int gameID)
        {
            if (_localPlayer is not null && !_localPlayer.UserWantsToQuit)
            {
                BoardGUI wantedDisplay = _boardGuiByName[gameID.ToString()];
                wantedDisplay.Enabled = false;
                wantedDisplay.InteractionsDisabled = true;

                string messageToDisplay = wantedDisplay.StateOfGame switch
                {
                    GameState.LocalWin => "You Win",
                    GameState.LocalLoss => "You have lost.",
                    GameState.GameDraw => "Game Draw",
                    GameState.OpponentDisconnected => "Opponent Disconnected",
                    GameState.ServerUnavailable => "Server is unavailable",
                    _ => throw new ArgumentException("Unaccounted for GameState.")
                };

                var labelToShowUser = new Label()
                {
                    Location = wantedDisplay.ConfirmMoveBTN.Location,
                    Name = "Disclaimer",
                    Size = wantedDisplay.ConfirmMoveBTN.Size,
                    TabIndex = 0,
                    Text = messageToDisplay,
                    BackColor = Color.White,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.Black,
                    Font = new Font("Arial", 10)
                };

                wantedDisplay.Controls.Add(labelToShowUser);
                labelToShowUser.BringToFront();
            }
        }
        /// <summary>
        /// Makes changes to the GUI to reflect that the server that <see cref="_localPlayer"/> is connected too has disconnected.
        /// </summary>
        public void ServerIsUnreachable()
        {
            foreach (var gameGUI in _boardGuiByName.Values)
            {
                gameGUI.Enabled = false;
            }
            var length = 300;
            var height = 100;

            var serverCloseLabel = new Label()
            {
                Size = new Size(length, height),
                Location = new Point(MainView.Left, MainView.Top + (MainView.Height / 2) - (height / 2)),
                Text = "Server is unavailable",
                Name = "Server NA",
                BackColor = Color.FromArgb(233, 217, 37),
                Font = new Font("Arial", 15, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            MainView.Controls.Add(serverCloseLabel);
            serverCloseLabel.BringToFront();

            _localPlayer = null;

            JoinServer.BackColor = Color.FromArgb(34, 82, 57);
            JoinServer.ForeColor = Color.White;

            StartServer.BackColor = Color.FromArgb(34, 82, 57);
            StartServer.ForeColor = Color.White;

            JoinServer.Enabled = true;
            StartServer.Enabled = true;
            panel1.Controls["LFG"].Visible = false;
        }
        private void GameTracker_SelectedIndexChanged(object sender, EventArgs e)
        {
            MainView.Controls[GameTracker.SelectedItem as string].BringToFront();
        }
    }
}