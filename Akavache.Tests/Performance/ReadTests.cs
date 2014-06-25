﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Akavache.Sqlite3;
using Xunit;

namespace Akavache.Tests.Performance
{
    public abstract class ReadTests
    {
        protected abstract IBlobCache CreateBlobCache(string path);
        readonly Random prng = new Random();

        [Fact]
        public async Task SequentialSimpleReads()
        {
            var results = new Dictionary<int, long>();
            var dbName = default(string);

            var dirPath = default(string);
            using (Utility.WithEmptyDirectory(out dirPath))
            using (var cache = await GenerateAGiantDatabase(dirPath))
            {
                var keys = await cache.GetAllKeys();
                dbName = dbName ?? cache.GetType().Name;

                foreach (var size in GetPerfRanges())
                {
                    var st = new Stopwatch();
                    var toFetch = Enumerable.Range(0, size)
                        .Select(_ => keys[prng.Next(0, keys.Count - 1)])
                        .ToArray();

                    st.Start();

                    foreach (var v in toFetch) {
                        await cache.Get(v);
                    }

                    st.Stop();
                    results[size] = st.ElapsedMilliseconds;
                }
            }

            Console.WriteLine(dbName);
            foreach (var kvp in results) {
                Console.WriteLine("{0}: {1}", kvp.Key, kvp.Value);
            }
        }

        async Task<List<string>> GenerateDatabase(IBlobCache targetCache, int size)
        {
            var ret = new List<string>();

            // Write out in groups of 4096
            while (size > 0)
            {
                var toWriteSize = Math.Min(4096, size);

                var toWrite = Enumerable.Range(0, toWriteSize)
                    .Select(_ => GenerateRandomKey())
                    .Distinct()
                    .ToDictionary(k => k, _ => GenerateRandomBytes());

                await targetCache.Insert(toWrite);

                foreach (var k in toWrite.Keys) ret.Add(k);

                size -= toWrite.Count;
                Console.WriteLine(size);
            }

            return ret;
        }

        byte[] GenerateRandomBytes()
        {
            var ret = new byte[prng.Next(1, 256)];

            prng.NextBytes(ret);
            return ret;
        }

        string GenerateRandomKey()
        {
            var bytes = GenerateRandomBytes();

            // NB: Mask off the MSB and set bit 5 so we always end up with
            // valid UTF-8 characters that aren't control characters
            for (int i = 0; i < bytes.Length; i++) { bytes[i] = (byte)((bytes[i] & 0x7F) | 0x20); }
            return Encoding.UTF8.GetString(bytes, 0, Math.Min(256, bytes.Length));
        }

        int[] GetPerfRanges()
        {
            return new[] { 1, 10, 100, 1000, 10000, 100000 };
        }

        async Task<IBlobCache> GenerateAGiantDatabase(string path)
        {
            path = path ?? IntegrationTestHelper.GetIntegrationTestRootDirectory();

            var giantDbSize = 100000;
            var cache = CreateBlobCache(path);

            var keys = await cache.GetAllKeys();
            if (keys.Count == giantDbSize) return cache;;

            await cache.InvalidateAll();
            await GenerateDatabase(cache, giantDbSize);
            return cache;
        }
    }

    public class Sqlite3ReadTests : ReadTests
    {
        protected override IBlobCache CreateBlobCache(string path)
        {
            return new SqlitePersistentBlobCache(Path.Combine(path, "blob.db"));
        }
    }
}
