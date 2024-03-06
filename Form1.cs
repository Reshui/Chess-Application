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

        /// <summary>
        /// Dictionary used to keep track of started games.
        /// </summary>
        private readonly Dictionary<string, BoardGUI> _boardGuiByName = new();
        private Task<bool>? _joinServerTask;
        // public event StartServerHandlerAsync ServerStart;
        public Form1()
        {
            InitializeComponent();
            this.FormClosing += new FormClosingEventHandler(this.Form1_FormClosing!);
            //this.JoinServer.Click += new EventHandler();
        }

        private async void Form1_FormClosing(object sender, EventArgs evnt)
        {
            if (_localPlayer is not null) { await _localPlayer.CloseConnectionToServerAsync(true, false); _localPlayer = null; }

            if (_hostedServer is not null) { await _hostedServer.CloseServerAsync(); _hostedServer = null; }
        }
        private void StartServer_Click(object sender, EventArgs e)
        {
            if (_hostedServer is null)
            {
                _hostedServer = new Server();
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
                _localPlayer = new Player(this, new CancellationTokenSource());
                _localPlayer.AssignName(userName);

                if (_localPlayer.JoinServer())
                {
                    JoinServer.BackColor = Color.FromArgb(144, 12, 63);
                    JoinServer.ForeColor = Color.AntiqueWhite;

                    JoinServer.Enabled = false;
                    StartServer.Enabled = false;
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gameID"><see cref="GameEnvironment.GameID"/> of game to target.</param>
        /// <exception cref="NotImplementedException"></exception>
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
                    Text = messageToDisplay
                };

                wantedDisplay.Controls.Add(labelToShowUser);
            }
        }
        /// <summary>
        /// Makes changes to the GUI to reflect that the server that <see cref="_localPlayer"/> is connected too has disconnected.
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        public void ServerIsUnreachable()
        {
            foreach (var gameGUI in _boardGuiByName)
            {
                gameGUI.Value.Enabled = false;
            }
            var length = 100;
            var height = 50;
            var serverCloseLabel = new Label()
            {
                Size = new Size(length, height),
                Location = new Point(MainView.Left + MainView.Width / 2 - length / 2, MainView.Top + MainView.Height / 2 - height / 2),
                Text = "Server is unavailable",
                Name = "Server NA",
                BackColor = Color.Crimson
            };

            MainView.Controls.Add(serverCloseLabel);
            serverCloseLabel.BringToFront();

            try
            {
                _localPlayer = null;
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                JoinServer.BackColor = Color.FromArgb(34, 82, 57);
                JoinServer.ForeColor = Color.White;

                StartServer.BackColor = Color.FromArgb(34, 82, 57);
                StartServer.ForeColor = Color.White;

                JoinServer.Enabled = true;
                StartServer.Enabled = true;
            }
        }
        private void GameTracker_SelectedIndexChanged(object sender, EventArgs e)
        {
            MainView.Controls[GameTracker.SelectedItem as string].BringToFront();
        }
    }
}