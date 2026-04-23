using System.Collections.Concurrent;
using Moq;
using StackExchange.Redis;

namespace TelegramSearchBot.Test.Service.AI.LLM {
    internal sealed class InMemoryRedisTestHarness {
        private readonly object _gate = new();
        private readonly ConcurrentDictionary<string, List<string>> _lists = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Dictionary<string, string>> _hashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryRedisTestHarness() {
            Database = new Mock<IDatabase>(MockBehavior.Strict);
            Connection = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            Connection.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(Database.Object);

            Database.Setup(d => d.ListLeftPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, When _, CommandFlags _) => {
                    lock (_gate) {
                        var list = _lists.GetOrAdd(key.ToString(), _ => []);
                        list.Insert(0, value.ToString());
                        return list.Count;
                    }
                });

            Database.Setup(d => d.ListRightPushAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, When _, CommandFlags _) => {
                    lock (_gate) {
                        var list = _lists.GetOrAdd(key.ToString(), _ => []);
                        list.Add(value.ToString());
                        return list.Count;
                    }
                });

            Database.Setup(d => d.ListRangeAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, long start, long stop, CommandFlags _) => {
                    lock (_gate) {
                        if (!_lists.TryGetValue(key.ToString(), out var list)) {
                            return Array.Empty<RedisValue>();
                        }

                        var normalizedStart = ( int ) Math.Max(0, start);
                        var normalizedStop = stop < 0 ? list.Count - 1 : ( int ) Math.Min(stop, list.Count - 1);
                        if (normalizedStart > normalizedStop || normalizedStart >= list.Count) {
                            return Array.Empty<RedisValue>();
                        }

                        return list.Skip(normalizedStart).Take(normalizedStop - normalizedStart + 1).Select(x => ( RedisValue ) x).ToArray();
                    }
                });

            Database.Setup(d => d.ListLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => {
                    lock (_gate) {
                        return _lists.TryGetValue(key.ToString(), out var list) ? list.Count : 0;
                    }
                });

            Database.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
                .Returns((RedisKey key, HashEntry[] entries, CommandFlags _) => {
                    lock (_gate) {
                        var hash = _hashes.GetOrAdd(key.ToString(), _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                        foreach (var entry in entries) {
                            hash[entry.Name.ToString()] = entry.Value.ToString();
                        }
                    }

                    return Task.CompletedTask;
                });

            Database.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue field, RedisValue value, When _, CommandFlags _) => {
                    lock (_gate) {
                        var hash = _hashes.GetOrAdd(key.ToString(), _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                        hash[field.ToString()] = value.ToString();
                    }

                    return true;
                });

            Database.Setup(d => d.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => {
                    lock (_gate) {
                        return _hashes.TryGetValue(key.ToString(), out var hash)
                            ? hash.Select(entry => new HashEntry(entry.Key, entry.Value)).ToArray()
                            : Array.Empty<HashEntry>();
                    }
                });

            Database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? _, When _, CommandFlags _) => {
                    _strings[key.ToString()] = value.ToString();
                    return true;
                });

            Database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? _, When _) => {
                    _strings[key.ToString()] = value.ToString();
                    return true;
                });

            Database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, TimeSpan? _, bool _, When _, CommandFlags _) => {
                    _strings[key.ToString()] = value.ToString();
                    return true;
                });

            // SE.Redis 2.12+ Expiration/ValueCondition overload
            Database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(), It.IsAny<ValueCondition>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, RedisValue value, Expiration _, ValueCondition _, CommandFlags _) => {
                    _strings[key.ToString()] = value.ToString();
                    return true;
                });

            Database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => _strings.TryGetValue(key.ToString(), out var value) ? ( RedisValue ) value : RedisValue.Null);

            Database.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags flags) => {
                    var removed = false;
                    removed |= _strings.TryRemove(key.ToString(), out _);
                    removed |= _lists.TryRemove(key.ToString(), out _);
                    removed |= _hashes.TryRemove(key.ToString(), out _);
                    return removed;
                });

            Database.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
        }

        public Mock<IConnectionMultiplexer> Connection { get; }
        public Mock<IDatabase> Database { get; }

        public string? PeekFirstListValue(string key) {
            lock (_gate) {
                return _lists.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;
            }
        }

        public string? PopFirstListValue(string key) {
            lock (_gate) {
                if (!_lists.TryGetValue(key, out var list) || list.Count == 0) {
                    return null;
                }

                var value = list[0];
                list.RemoveAt(0);
                return value;
            }
        }

        public IReadOnlyList<string> GetListValues(string key) {
            lock (_gate) {
                return _lists.TryGetValue(key, out var list) ? list.ToList() : [];
            }
        }

        public IReadOnlyDictionary<string, string> GetHash(string key) {
            lock (_gate) {
                return _hashes.TryGetValue(key, out var hash)
                    ? new Dictionary<string, string>(hash, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void SetHash(string key, IDictionary<string, string> values) {
            _hashes[key] = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }

        public string? GetString(string key) => _strings.TryGetValue(key, out var value) ? value : null;
    }
}
