using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace ServidorFIX
{
    public class RedisExposureStore
    {
        private readonly IDatabase _db;
        private readonly bool _redisOnline;
        private readonly ConcurrentDictionary<string, decimal> _memoryCache = new();

        public RedisExposureStore()
        {
            var host = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";

            try
            {
                var redis = ConnectionMultiplexer.Connect($"{host}:{port}");
                _db = redis.GetDatabase();
                _redisOnline = redis.IsConnected;
                Console.WriteLine("[Redis] Conectado com sucesso.");
            }
            catch (Exception ex)
            {
                _redisOnline = false;
                Console.WriteLine("[Redis] Indisponível. Usando cache em memória. Erro: " + ex.Message);
            }
        }

        private string GetKey(string symbol) => $"exposure:{symbol.ToUpper()}";

        public decimal GetExposure(string symbol)
        {
            if (_redisOnline)
            {
                var val = _db.StringGet(GetKey(symbol));
                return val.HasValue ? decimal.Parse(val) : 0m;
            }

            return _memoryCache.TryGetValue(symbol.ToUpper(), out var value) ? value : 0m;
        }

        public void SetExposure(string symbol, decimal exposure)
        {
            if (_redisOnline)
            {
                _db.StringSet(GetKey(symbol), exposure.ToString(), TimeSpan.FromMinutes(1));
            }
            else
            {
                _memoryCache[symbol.ToUpper()] = exposure;
            }
        }
    }
}