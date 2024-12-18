
namespace Quantum
{
    public enum GameState
    {
        NotStarted = 0,
        Started = 1,
        Paused = 2,
        Ended = 3,
    }
    
    public class GameInstance<S, P> where S : class where P : class
    {
        public string id;
        public S state;
        public QuantumEvent[] messages;
        public long updated;
        public PlayerData<P>[] players;
        public string gameIdentifier;
        public GameState gameState;
        // public Tread { identifier, status, response}
    }
}