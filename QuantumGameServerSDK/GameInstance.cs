namespace QuantumGameServer
{
    public class GameInstance<S, M, P> where S : class where M : IBaseMessage where P : class
    {
        public string id;
        public S state;
        public M[] messages;
        public long updated;
        public PlayerData<P>[] players;
    }
}