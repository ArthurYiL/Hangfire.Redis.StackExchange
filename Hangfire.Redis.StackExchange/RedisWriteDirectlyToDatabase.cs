﻿// Copyright © 2013-2015 Sergey Odinokov, Marco Casamento 
// This software is based on https://github.com/HangfireIO/Hangfire.Redis 

// Hangfire.Redis.StackExchange is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as 
// published by the Free Software Foundation, either version 3 
// of the License, or any later version.
// 
// Hangfire.Redis.StackExchange is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public 
// License along with Hangfire.Redis.StackExchange. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Linq;
using System.Collections.Generic;
using Hangfire.Annotations;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using StackExchange.Redis;

namespace Hangfire.Redis
{
    internal class RedisWriteDirectlyToDatabase : JobStorageTransaction
    {
        private readonly RedisStorage _storage;
        private readonly IDatabase _database;

        public RedisWriteDirectlyToDatabase([NotNull] RedisStorage storage, [NotNull] IDatabase database)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        public override void AddRangeToSet([NotNull] string key, [NotNull] IList<string> items)
        {
            _database.SortedSetAddAsync(_storage.GetRedisKey(key), items.Select(x => new SortedSetEntry(x, 0)).ToArray());
        }

        public override void ExpireHash([NotNull] string key, TimeSpan expireIn)
        {
            _database.KeyExpireAsync(_storage.GetRedisKey(key), expireIn);
        }

        public override void ExpireList([NotNull] string key, TimeSpan expireIn)
        {
            _database.KeyExpireAsync(_storage.GetRedisKey(key), expireIn);
        }

        public override void ExpireSet([NotNull] string key, TimeSpan expireIn)
        {
            _database.KeyExpireAsync(_storage.GetRedisKey(key), expireIn);
        }

        public override void PersistHash([NotNull] string key)
        {
            _database.KeyPersistAsync(_storage.GetRedisKey(key));
        }

        public override void PersistList([NotNull] string key)
        {
            _database.KeyPersistAsync(_storage.GetRedisKey(key));
        }

        public override void PersistSet([NotNull] string key)
        {
            _database.KeyPersistAsync(_storage.GetRedisKey(key));
        }

        public override void RemoveSet([NotNull] string key)
        {
            _database.KeyDeleteAsync(_storage.GetRedisKey(key));
        }

        public override void Commit()
        {
            //nothing to be done
        }

        public override void ExpireJob([NotNull] string jobId, TimeSpan expireIn)
        {
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));

            _database.KeyExpireAsync(_storage.GetRedisKey($"job:{jobId}"), expireIn);
            _database.KeyExpireAsync(_storage.GetRedisKey($"job:{jobId}:history"), expireIn);
            _database.KeyExpireAsync(_storage.GetRedisKey($"job:{jobId}:state"), expireIn);
        }

        public override void PersistJob([NotNull] string jobId)
        {
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));

            _database.KeyPersistAsync(_storage.GetRedisKey($"job:{jobId}"));
            _database.KeyPersistAsync(_storage.GetRedisKey($"job:{jobId}:history"));
            _database.KeyPersistAsync(_storage.GetRedisKey($"job:{jobId}:state"));
        }

        public override void SetJobState([NotNull] string jobId, [NotNull] IState state)
        {
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));
            if (state == null) throw new ArgumentNullException(nameof(state));

            _database.HashSetAsync(_storage.GetRedisKey($"job:{jobId}"), "State", state.Name);
            _database.KeyDeleteAsync(_storage.GetRedisKey($"job:{jobId}:state"));

            var storedData = new Dictionary<string, string>(state.SerializeData())
            {
                { "State", state.Name }
            };

            if (state.Reason != null)
                storedData.Add("Reason", state.Reason);

            _database.HashSetAsync(_storage.GetRedisKey($"job:{jobId}:state"), storedData.ToHashEntries());

            AddJobState(jobId, state);
        }

        public override void AddJobState([NotNull] string jobId, [NotNull] IState state)
        {
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var storedData = new Dictionary<string, string>(state.SerializeData())
            {
                { "State", state.Name },
                { "Reason", state.Reason },
                { "CreatedAt", JobHelper.SerializeDateTime(DateTime.UtcNow) }
            };

            _database.ListRightPushAsync(
                _storage.GetRedisKey($"job:{jobId}:history"),
                SerializationHelper.Serialize(storedData));
        }

        public override void AddToQueue([NotNull] string queue, [NotNull] string jobId)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));
            if (jobId == null) throw new ArgumentNullException(nameof(jobId));

            _database.SetAddAsync(_storage.GetRedisKey("queues"), queue);
            if (_storage.LifoQueues != null && _storage.LifoQueues.Contains(queue, StringComparer.OrdinalIgnoreCase))
            {
                _database.ListRightPushAsync(_storage.GetRedisKey($"queue:{queue}"), jobId);
            }
            else
            {
                _database.ListLeftPushAsync(_storage.GetRedisKey($"queue:{queue}"), jobId);
            }
            _database.PublishAsync(_storage.SubscriptionChannel, jobId);
        }

        public override void IncrementCounter([NotNull] string key)
        {
            _database.StringIncrementAsync(_storage.GetRedisKey(key));
        }

        public override void IncrementCounter([NotNull] string key, TimeSpan expireIn)
        {
            _database.StringIncrementAsync(_storage.GetRedisKey(key));
            _database.KeyExpireAsync(_storage.GetRedisKey(key), expireIn);
        }

        public override void DecrementCounter([NotNull] string key)
        {
            _database.StringDecrementAsync(_storage.GetRedisKey(key));
        }

        public override void DecrementCounter([NotNull] string key, TimeSpan expireIn)
        {
            _database.StringDecrementAsync(_storage.GetRedisKey(key));
            _database.KeyExpireAsync(_storage.GetRedisKey(key), expireIn);
        }

        public override void AddToSet([NotNull] string key, [NotNull] string value)
        {
            AddToSet(key, value, 0);
        }

        public override void AddToSet([NotNull] string key, [NotNull] string value, double score)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            _database.SortedSetAddAsync(_storage.GetRedisKey(key), value, score);
        }

        public override void RemoveFromSet([NotNull] string key, [NotNull] string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));

            _database.SortedSetRemoveAsync(_storage.GetRedisKey(key), value);
        }

        public override void InsertToList([NotNull] string key, string value)
        {
            _database.ListLeftPushAsync(_storage.GetRedisKey(key), value);
        }

        public override void RemoveFromList([NotNull] string key, string value)
        {
            _database.ListRemoveAsync(_storage.GetRedisKey(key), value);
        }

        public override void TrimList([NotNull] string key, int keepStartingFrom, int keepEndingAt)
        {
            _database.ListTrimAsync(_storage.GetRedisKey(key), keepStartingFrom, keepEndingAt);
        }

        public override void SetRangeInHash(
            [NotNull] string key, [NotNull] IEnumerable<KeyValuePair<string, string>> keyValuePairs)
        {
            if (keyValuePairs == null) throw new ArgumentNullException(nameof(keyValuePairs));

            _database.HashSetAsync(_storage.GetRedisKey(key), keyValuePairs.ToHashEntries());
        }

        public override void RemoveHash([NotNull] string key)
        {
            _database.KeyDeleteAsync(_storage.GetRedisKey(key));
        }

        public override void Dispose()
        {
            //Don't have to dispose anything
        }
    }
}
