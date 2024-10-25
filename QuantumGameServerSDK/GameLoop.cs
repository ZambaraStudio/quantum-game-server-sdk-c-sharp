using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace QuantumGameServer
{
    
    public struct GameLoopConfig<S, M, P> where S : class where M : IBaseMessage where P : class
    {
        public int tickRate;
        public IDatabase redisDatabase;
        public Func<GameInstance<S, M, P>, QuantumGameServer<S, M, P>, S> tick;
        public QuantumGameServer<S, M, P> quantumGameServer;
    }
    
    public class GameLoop<S, M, P> where S : class where M : IBaseMessage where P : class
    {
        private bool _runing;
        private int _tickRate;
        private IDatabase _redisDatabase;
        private Func<GameInstance<S, M, P>, QuantumGameServer<S, M, P>, S> _tick;
        private QuantumGameServer<S, M, P> _quantumGameServer;
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
        
        public GameLoop(GameLoopConfig<S, M, P> config)
        {
            _tickRate = config.tickRate;
            _redisDatabase = config.redisDatabase;
            _tick = config.tick;
            _quantumGameServer = config.quantumGameServer;
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

                GameInstance<S, M, P> gameInstance = new GameInstance<S, M, P>();
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
                            gameInstance.state = JsonConvert.DeserializeObject<S>(hashEntry.Value.ToString());
                            break;
                        case "players":
                            var data = JsonConvert.DeserializeObject<PlayerData<P>[]>(hashEntry.Value.ToString())!;
                            gameInstance.players = data;
                            break;
                        default:
                            break;
                    }
                }

                gameInstance.messages = Array.ConvertAll(messageArray, message =>
                {
                    BaseMessage baseMessage = JsonConvert.DeserializeObject<BaseMessage>(message.ToString());
                    M genericMessage =  JsonConvert.DeserializeObject<M>(baseMessage.data.ToString());
                    genericMessage.playerId = baseMessage.playerId;
                    return genericMessage;
                })!;

                var newState = _tick(gameInstance, _quantumGameServer);

                await _redisDatabase.KeyDeleteAsync(
                    AccessPatterns.GetGameInstanceMessagesKeyWithRedisGameInstanceKey(entryId)
                );

                long updatedTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                await _redisDatabase.HashSetAsync($"{entryId}", new[]
                {
                    new HashEntry("state", JsonConvert.SerializeObject(newState)),
                    new HashEntry("updated", updatedTimestamp)
                });

                await _redisDatabase.SortedSetAddAsync(AccessPatterns.GetUpdatedGameInstancesKey(), new SortedSetEntry[]{new SortedSetEntry(entryId, updatedTimestamp)});
            }
        }
        
        public void Stop() => _runing = false;
    }
}