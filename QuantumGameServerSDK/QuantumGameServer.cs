using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace QuantumGameServer
{
    public struct QuantumGameServerConfig
    {
        public readonly int tickRate;
        public readonly int redisPort;
        public readonly string redisHost;

        public QuantumGameServerConfig(string redisHost = "localhost", int redisPort = 0, int tickRate = 30)
        {
            this.tickRate = tickRate;
            this.redisPort = redisPort;
            this.redisHost = redisHost;
        }
    }
    
    public class QuantumGameServer<S, M, P> where S : class where M : IBaseMessage where P : class
    {
        private ConnectionMultiplexer _redis;
        private IDatabase _database;
        private QuantumGameServerConfig _config;
        private GameLoop<S, M, P> _gameLoop;
        private Func<GameInstance<S, M, P>, QuantumGameServer<S, M, P>, S> _tick;
        
        public QuantumGameServer(QuantumGameServerConfig config, Func<GameInstance<S, M, P>, QuantumGameServer<S, M, P>, S> tick)
        {
            _config = config;
            _tick = tick;
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
            
            var gameLoopConfig = new GameLoopConfig<S, M, P>
            {
                tickRate = _config.tickRate,
                redisDatabase = _database,
                tick = _tick,
                quantumGameServer = this
            };

            _gameLoop = new GameLoop<S, M, P>(gameLoopConfig);
            _gameLoop.Run();
        }

        public void Stop()
        {
            _gameLoop.Stop();
        }

        public async void SendMessage(IFromGSBaseMessage payload)
        {
            string key = AccessPatterns.GetPlayerMessageChannelKey(payload.playerId);
            RedisChannel channel = new RedisChannel(key, RedisChannel.PatternMode.Auto);
            await _database.PublishAsync(channel, JsonConvert.SerializeObject(payload));
        }

        public async Task<uint[]> GetConnectedPlayers(string[] playerIds, uint threshold = 0)
        {
            var keys = Array.ConvertAll(playerIds, input => new RedisKey(AccessPatterns.GetPlayerHeartbeatKey(input)));
            var resp = await _database.StringGetAsync(keys);

            return Array.ConvertAll(resp, redisValue => redisValue.TryParse(out long test) ? (uint)test : uint.MaxValue);
        }
    }
}
