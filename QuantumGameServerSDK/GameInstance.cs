namespace QuantumGameServer
{
    public class GameInstance<S, P> where S : class where P : class
    {
        public string id;
        public S state;
        public QuantumEvent[] messages;
        public long updated;
        public PlayerData<P>[] players;
    }
}