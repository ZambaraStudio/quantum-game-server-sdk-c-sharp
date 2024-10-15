namespace QuantumGameServer
{
    public static class AccessPatterns
    { 
      public static string GetGameInstanceKey(string instanceId) => $"{instanceId}:game_instance";
      public static string GetGameInstanceMessagesKey(string instanceId) => $"{GetGameInstanceKey(instanceId)}:messages";
      public static string GetGameInstanceMessagesKeyWithRedisGameInstanceKey(string redisGameInstanceKey) => $"{redisGameInstanceKey}:messages";
      public static string GetPlayerHeartbeatKey(string playerId) => $"{playerId}:heartbeat";
      public static string GetPlayerMessageChannelKey(string playerId) => $"{playerId}:output_to_player";
      public static string GetUpdatedGameInstancesKey() => $"updated_game_instances";
    };
}