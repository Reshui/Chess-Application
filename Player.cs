
using Pieces;
using System.Net.Sockets;

public class Player
{
    public Team CurrentTeam;
    public TcpClient Client;
    public bool OpponentIsWaiting = false;
    public bool WaitingForOpponent = false;
    public Player(TcpClient connectedClient)
    {
        Client = connectedClient;
    }
    public void SendServerMessage(string message)
    {
        throw new NotImplementedException();
    }
    public void AssignTeam(Team newTeam)
    {
        CurrentTeam = newTeam;
    }
}