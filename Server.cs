namespace Pieces;
public class Server
{
    private List<GameEnvironment> _activeGames = new List<GameEnvironment>();

    public void AddGame(GameEnvironment newGame)
    {
        _activeGames.Add(newGame);
    }
}