using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace Quantum
{
    
    public struct GameLoopConfig<S, P> where S : class where P : class
    {
        public int tickRate;
        public IDatabase redisDatabase;
        public Func<GameInstance<S, P>, GameServer<S, P>, S> tick;
        public GameServer<S, P> quantumGameServer;
        public JsonSerializerSettings serializationSettings;
    }
    
    public class GameLoop<S, P> where S : class where P : class
    {
        private bool _runing;
        private int _tickRate;
        private IDatabase _redisDatabase;
        private Func<GameInstance<S, P>, GameServer<S, P>, S> _tick;
        private GameServer<S, P> _quantumGameServer;
        private const string _luaScript = @"
        local oldestEntry = redis.call('ZRANGE', KEYS[1], 0, 0)
            if #oldestEntry > 0 then
            local id = oldestEntry[1]
        local lockKey = 'lock:' .. id
        local lockAcquired = redis.call('SETNX', lockKey, 'locked')
            if lockAcquired == 1 then
            redis.call('PEXPIRE', lockKey, ARGV[1])
        return id
            end
        end
        return nil
        ";
        
        private JsonSerializerSettings _serializationSettings;
        
        public GameLoop(GameLoopConfig<S, P> config)
        {
            _tickRate = config.tickRate;
            _redisDatabase = config.redisDatabase;
            _tick = config.tick;
            _quantumGameServer = config.quantumGameServer;
            _serializationSettings = new JsonSerializerSettings(config.serializationSettings);
        }

        public async void Run()
        {
            _runing = true;
            while (_runing)
            {
                var keys = new RedisKey[] { AccessPatterns.GetUpdatedGameInstancesKey() };
                var arguments = new RedisValue[] { _tickRate.ToString() };
                var id = await _redisDatabase.ScriptEvaluateAsync(_luaScript, keys, arguments);

                if (id.IsNull)
                {
                    await Task.Delay(1);
                    continue;
                }

                string entryId = Convert.ToString(id);

                var gameInstanceMessagesKeyWithRedisGameInstanceKey =
                    AccessPatterns.GetGameInstanceMessagesKeyWithRedisGameInstanceKey(entryId);
                var messageArray =
                    await _redisDatabase.ListRangeAsync(gameInstanceMessagesKeyWithRedisGameInstanceKey, 0, -1);

                var instance = await _redisDatabase.HashGetAllAsync(entryId);

                if (instance.Length == 0)
                {
                    var updatedGameInstancesKey = AccessPatterns.GetUpdatedGameInstancesKey();
                    await _redisDatabase.GeoRemoveAsync(updatedGameInstancesKey, entryId);
                    Console.WriteLine($"No Instance! {entryId}");
                }

                GameInstance<S, P> gameInstance = new GameInstance<S, P>();
                foreach (var hashEntry in instance)
                {
                    switch (hashEntry.Name)
                    {
                        case "id":
                            gameInstance.id = hashEntry.Value.ToString();
                            break;
                        case "updated":
                            gameInstance.updated = Convert.ToInt64(hashEntry.Value);
                            break;
                        case "state":
                            gameInstance.state = JsonConvert.DeserializeObject<S>(hashEntry.Value.ToString(), _serializationSettings);
                            break;
                        case "players":
                            var data = JsonConvert.DeserializeObject<PlayerData<P>[]>(hashEntry.Value.ToString(), _serializationSettings)!;
                            gameInstance.players = data;
                            break;
                        default:
                            break;
                    }
                }

                gameInstance.messages = Array.ConvertAll(messageArray, message =>
                {
                    QuantumEvent qEvent = JsonConvert.DeserializeObject<QuantumEvent>(message.ToString(), _serializationSettings) ?? throw new NullReferenceException();
                    return qEvent;
                });
                Array.Sort(gameInstance.messages, (a, b) => a.id.CompareTo(b.id));

                var newState = _tick(gameInstance, _quantumGameServer);

                await _redisDatabase.KeyDeleteAsync(
                    AccessPatterns.GetGameInstanceMessagesKeyWithRedisGameInstanceKey(entryId)
                );

                long updatedTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                await _redisDatabase.HashSetAsync($"{entryId}", new[]
                {
                    new HashEntry("state", JsonConvert.SerializeObject(newState, _serializationSettings)),
                    new HashEntry("updated", updatedTimestamp)
                });

                await _redisDatabase.SortedSetAddAsync(AccessPatterns.GetUpdatedGameInstancesKey(), new SortedSetEntry[]{new SortedSetEntry(entryId, updatedTimestamp)});
            }
        }
        
        public void Stop() => _runing = false;
    }
}