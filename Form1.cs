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
                _hostedServer.StartServer();
            }
        }

        private async void Form1_FormClosing(object sender, EventArgs evnt)
        {
            if (_hostedServer != null)
            {
                await _hostedServer.CloseServerAsync();
            }
        }
    }
}