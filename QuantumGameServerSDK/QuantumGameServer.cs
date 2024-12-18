using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Quantum
{
    public class QuantumEvent
    {
        public string gameInstanceId { get; set; }
        public string playerId { get; set; }
        public string type { get; set; }
        public uint id { get; set; }
        public GenericMessage message { get; set; }
    }
     
    public class GenericMessage
    {
        public string type { get; set; }
        public object data { get; set; }

        public T GetData<T>()
        {
            return JsonConvert.DeserializeObject<T>(data.ToString());
        }
    }
    
    public struct GameServerConfig
    {
        public readonly int tickRate;
        public readonly int redisPort;
        public readonly string redisHost;
        public readonly JsonSerializerSettings serializationSettings;

        public GameServerConfig(string redisHost = "localhost", int redisPort = 0, int tickRate = 30, JsonSerializerSettings serializationSettings = null)
        {
            this.tickRate = tickRate;
            this.redisPort = redisPort;
            this.redisHost = redisHost;
            this.serializationSettings = serializationSettings;
        }
    }
    
    public class GameServer<S, P> where S : class where P : class
    {
        private ConnectionMultiplexer _redis;
        private IDatabase _database;
        private GameServerConfig _config;
        private GameLoop<S, P> _gameLoop;
        private Func<GameInstance<S, P>, GameServer<S, P>, S> _tick;
        private uint _currentEventId;

        public Action OnPlayerConnected { get; set; }
        public Action OnPlayerDisconnected { get; set; }
        public Action<GameState> OnGameStateChanged { get; set; }
        
        public GameServer(GameServerConfig config, Func<GameInstance<S, P>, GameServer<S, P>, S> tick)
        {
            _config = config;
            _tick = tick;
            _currentEventId = 0;
        }
        
        public async void Start()
        {
            string redisURL = _config.redisHost;
            if (_config.redisPort > 0)
            {
                redisURL += $":{_config.redisPort}";
            }
            
            _redis = await ConnectionMultiplexer.ConnectAsync(redisURL);
            _database = _redis.GetDatabase();
            
            var gameLoopConfig = new GameLoopConfig<S, P>
            {
                tickRate = _config.tickRate,
                redisDatabase = _database,
                tick = _tick,
                quantumGameServer = this,
                serializationSettings = _config.serializationSettings
            };

            _gameLoop = new GameLoop<S, P>(gameLoopConfig);
            _gameLoop.Run();
        }

        public void Stop()
        {
            _gameLoop.Stop();
        }

        public async void SendMessageToAll(string gameInstance, GenericMessage message)
        {
            foreach (var playerId in GetAllPlayers())
            {
                SendMessage(gameInstance, playerId, message);
            }
            
            string[] GetAllPlayers() => Array.Empty<string>();
        }
        
        public async void SendMessage(string gameInstance, string playerId, GenericMessage message)
        {
            QuantumEvent qEvent = new QuantumEvent()
            {
                gameInstanceId = gameInstance,
                playerId = playerId,
                type = "generic-message",
                id = _currentEventId++,
                message = message,
            };
            await SendEvent(qEvent);
        }
        
        private async Task SendEvent(QuantumEvent qEvent)
        {
            string key = AccessPatterns.GetPlayerMessageChannelKey(qEvent.playerId);
            RedisChannel channel = new RedisChannel(key, RedisChannel.PatternMode.Auto);
            await _database.PublishAsync(channel, JsonConvert.SerializeObject(qEvent, _config.serializationSettings));
        }

        public async void SendStart(){}
        public async void SendPause(){}
        public async void SendEnd(){}

        public async Task<uint[]> GetConnectedPlayers(string[] playerIds, uint threshold = 0)
        {
            var keys = Array.ConvertAll(playerIds, input => new RedisKey(AccessPatterns.GetPlayerHeartbeatKey(input)));
            var resp = await _database.StringGetAsync(keys);

            return Array.ConvertAll(resp, redisValue => redisValue.TryParse(out long test) ? (uint)test : uint.MaxValue);
        }
    }
}
