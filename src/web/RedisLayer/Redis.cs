using System;
using System.Collections.Generic;
using ServiceStack.Redis;

namespace RedisLayer
{
    
    public class Redis
    {
        private int database { get; set; }
        private string redisConnectionString { get; }

        public Redis(int db)
        {
            database = db;
            redisConnectionString = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT").Equals("release", StringComparison.OrdinalIgnoreCase)
                ? "db:6379"
                : "127.0.0.1:6379";
        }

        public void SetRedisValue<T>(T value, string key)
        {
            var clientsManager = new BasicRedisClientManager(redisConnectionString);
            using (IRedisClient redis = clientsManager.GetClient())
            {
                redis.Db = database;
                redis.Set<T>(key, value);
            }
        }

        public T GetRedisValue<T>(string key)
        {
            var clientsManager = new BasicRedisClientManager(redisConnectionString);
            using (IRedisClient redis = clientsManager.GetClient())
            {
                redis.Db = database;
                return redis.Get<T>(key);
            }
        }
        public List<string> GetAllKeys()
        {
            var clientsManager = new BasicRedisClientManager(redisConnectionString);
            using (IRedisClient redis = clientsManager.GetClient())
            {
                redis.Db = database;
                return redis.GetAllKeys();

            }
        }

    }
}