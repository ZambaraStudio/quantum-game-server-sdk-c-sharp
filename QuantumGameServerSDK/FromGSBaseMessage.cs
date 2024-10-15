namespace QuantumGameServer
{
    public interface IFromGSBaseMessage
    {
        public string gameInstanceId { get; set; }
        public string playerId { get; set; }
    }
}