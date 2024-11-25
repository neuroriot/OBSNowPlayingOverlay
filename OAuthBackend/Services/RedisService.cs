using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace OAuthBackend.Services
{
    public class RedisService
    {
        public ConnectionMultiplexer Redis { get; set; }
        public ISubscriber RedisSub { get; set; }
        public IDatabase RedisDb { get; set; }

        private readonly ILogger<RedisService> _logger;
        private readonly BlockingCollection<KeyValuePair<string, string>> _messageQueue = new(1);
        private readonly ConcurrentDictionary<string, KeyValuePair<string, string>> _needRePublishMessageList = new();

        private readonly IConfiguration _configuration;
        private readonly Timer _timer;

        public RedisService(ILogger<RedisService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                RedisConnection.Init(_configuration["RedisConnectOption"]);
                Redis = RedisConnection.Instance.ConnectionMultiplexer;
                RedisDb = Redis.GetDatabase(1);
                RedisSub = Redis.GetSubscriber();

                _logger.LogInformation("Redis已連線");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Redis 連線錯誤，請確認伺服器是否已開啟");
                throw;
            }
        }

        public void Dispose()
        {
            RedisSub.UnsubscribeAll();
            Redis.Dispose();

            _timer.Change(Timeout.Infinite, 0);
            _timer.Dispose();
        }
    }
}
