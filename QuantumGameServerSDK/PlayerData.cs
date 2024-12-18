namespace Quantum
{
    public class PlayerData<T> where T : class
    {
        public string id { get; set; }
        public T data { get; set; }
    }
}