namespace QuantumGameServer
{
    public interface IBaseMessage
    {
        public string type { get; set; }
        public object data { get; set; }
    }

    public interface IBaseMessage<T> : IBaseMessage where T : class
    {
        public new T data { get; set; }
    }
    
    public class BaseMessage : IBaseMessage
    {
        public string type { get; set; }
        public object data { get; set; }
    }
    
    public class BaseMessage<T> : IBaseMessage<T> where T : class
    {
        public string type { get; set; }
        public T data { get; set; }

        object IBaseMessage.data
        {
            get => data;
            set => data = (T)value;
        }
    }
}