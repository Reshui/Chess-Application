
using Pieces;

public class Player
{
    public int CurrentTurnNumber = 0;
    public Team CurrentTeam;
    public void IncrementTurnCount()
    {
        CurrentTurnNumber++;
    }
}