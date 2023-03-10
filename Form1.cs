using Pieces;
using System.Collections.Concurrent;

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
        private readonly List<Task> _asyncTasks = new();
        private List<BoardGUI> _boards = new();
        // public event StartServerHandlerAsync ServerStart;
        public Form1()
        {
            InitializeComponent();
            this.StartServer.Click += new EventHandler(StartServerEvent!);
            //this.FormClosing += new EventHandler(ClosingEvents);
            //this.JoinServer.Click += new EventHandler();
        }
        private void StartServerEvent(object sender, EventArgs evnt)
        {
            if (_hostedServer == null)
            {
                _hostedServer = new Server();                
            }
        }

        private async void Form1_FormClosing(object sender, EventArgs evnt)
        {
            if (_hostedServer is not null)
            {
                await _hostedServer.CloseServerAsync();
            }

            if (_localPlayer is not null)
            {
                _localPlayer.MainTokenSource.Cancel();
                await Task.WhenAll(_asyncTasks);
                _localPlayer.MainTokenSource.Dispose();
            }
        }

        private void flowLayoutPanel1_Paint(object sender, PaintEventArgs e)
        {

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
            string userName = "Miller";// UserName.Text;
            
            if (userName != string.Empty && _localPlayer is null)
            {
                _localPlayer = new Player(this);
                _localPlayer.AssignName(userName);
                 _asyncTasks.Add(_localPlayer.StartListeningAsync());

                JoinServer.BackColor = Color.FromArgb(144, 12, 63);
                JoinServer.ForeColor = Color.AntiqueWhite;
                JoinServer.Enabled = false;
                StartServer.Enabled = false;
            }
        }

        public void AddGame(GameEnvironment newGame)
        {
            if (_localPlayer is not null)
            {
                var trackedElements = new BoardGUI(_localPlayer, newGame);

                _boards.Add(trackedElements);

                MainView.Controls.Add(trackedElements);

                trackedElements.Size = MainView.Size;

                MainView.Controls[trackedElements.Name].BringToFront();

                GameTracker.Items.Add(trackedElements.Name);
            }
        }

        private void GameTracker_SelectedIndexChanged(object sender, EventArgs e)
        {
            MainView.Controls[GameTracker.SelectedItem as string].BringToFront();
        }

    }
}