using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.IO.Caching
{
    public abstract class DataCache<TKey, TValue>
        where TKey : IEquatable<TKey>, ISerializable
        where TValue : class, ICloneable<TValue>, ISerializable, new()
    {
        public class Trackable
        {
            public TKey Key;
            public TValue Item;
            public TrackState State;
        }

        private readonly Dictionary<TKey, Trackable> dictionary = new Dictionary<TKey, Trackable>();

        public TValue this[TKey key]
        {
            get
            {
                lock (dictionary)
                {
                    if (dictionary.TryGetValue(key, out Trackable trackable))
                    {
                        if (trackable.State == TrackState.Deleted)
                            throw new KeyNotFoundException();
                    }
                    else
                    {
                        trackable = new Trackable
                        {
                            Key = key,
                            Item = GetInternal(key),
                            State = TrackState.None
                        };
                        dictionary.Add(key, trackable);
                    }
                    return trackable.Item;
                }
            }
        }

        /// <summary>
        /// Try to Add a specific key, with associated value, to the current cached dictionary.
        /// It will not read from internal state.
        /// However, if previously cached into Dictionary, request may fail.
        /// </summary>
        /// <param name="key">Key to be possible added.
        /// Key will not be added if value exists cached and the modification was not a Deleted one.
        /// </param>
        /// <param name="value">Corresponding value to be added, in the case of sucess.</param>
        /// <exception cref="ArgumentException">If cached on dictionary, with any state rather than `Deleted`, an Exception will be raised.</exception>
        public void Add(TKey key, TValue value)
        {
            lock (dictionary)
            {
                if (dictionary.TryGetValue(key, out Trackable trackable) && trackable.State != TrackState.Deleted)
                    throw new ArgumentException();
                dictionary[key] = new Trackable
                {
                    Key = key,
                    Item = value,
                    State = trackable == null ? TrackState.Added : TrackState.Changed
                };
            }
        }

        protected abstract void AddInternal(TKey key, TValue value);

        /// <summary>
        /// Update internals with all changes cached on Dictionary which are not None.
        /// </summary>
        public void Commit()
        {
            foreach (Trackable trackable in GetChangeSet())
                switch (trackable.State)
                {
                    case TrackState.Added:
                        AddInternal(trackable.Key, trackable.Item);
                        break;
                    case TrackState.Changed:
                        UpdateInternal(trackable.Key, trackable.Item);
                        break;
                    case TrackState.Deleted:
                        DeleteInternal(trackable.Key);
                        break;
                }
        }

        public DataCache<TKey, TValue> CreateSnapshot()
        {
            return new CloneCache<TKey, TValue>(this);
        }

        /// <summary>
        /// Delete key from cached Dictionary or search in Internal.
        /// </summary>
        /// <param name="key">Key to be deleted.</param>
        public void Delete(TKey key)
        {
            lock (dictionary)
            {
                if (dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Added)
                        dictionary.Remove(key);
                    else
                        trackable.State = TrackState.Deleted;
                }
                else
                {
                    TValue item = TryGetInternal(key);
                    if (item == null) return;
                    dictionary.Add(key, new Trackable
                    {
                        Key = key,
                        Item = item,
                        State = TrackState.Deleted
                    });
                }
            }
        }

        protected abstract void DeleteInternal(TKey key);

        public void DeleteWhere(Func<TKey, TValue, bool> predicate)
        {
            lock (dictionary)
            {
                foreach (Trackable trackable in dictionary.Where(p => p.Value.State != TrackState.Deleted && predicate(p.Key, p.Value.Item)).Select(p => p.Value))
                    trackable.State = TrackState.Deleted;
            }
        }

        /// <summary>
        /// Find the entries that start with the `key_prefix`
        /// </summary>
        /// <param name="key_prefix">Must maintain the deserialized format of TKey</param>
        /// <returns>Entries found with the desired prefix</returns>
        public IEnumerable<(TKey Key, TValue Value)> Find(byte[] key_prefix = null)
        {
            IEnumerable<(byte[], TKey, TValue)> cached;
            lock (dictionary)
            {
                cached = dictionary
                    .Where(p => p.Value.State != TrackState.Deleted && (key_prefix == null || p.Key.ToArray().AsSpan().StartsWith(key_prefix)))
                    .Select(p =>
                    (
                        KeyBytes: p.Key.ToArray(),
                        p.Key,
                        p.Value.Item
                    ))
                    .OrderBy(p => p.KeyBytes, ByteArrayComparer.Default)
                    .ToArray();
            }
            var uncached = FindInternal(key_prefix ?? Array.Empty<byte>())
                .Where(p => !dictionary.ContainsKey(p.Key))
                .Select(p =>
                (
                    KeyBytes: p.Key.ToArray(),
                    p.Key,
                    p.Value
                ));
            using (var e1 = cached.GetEnumerator())
            using (var e2 = uncached.GetEnumerator())
            {
                (byte[] KeyBytes, TKey Key, TValue Item) i1, i2;
                bool c1 = e1.MoveNext();
                bool c2 = e2.MoveNext();
                i1 = c1 ? e1.Current : default;
                i2 = c2 ? e2.Current : default;
                while (c1 || c2)
                {
                    if (!c2 || (c1 && ByteArrayComparer.Default.Compare(i1.KeyBytes, i2.KeyBytes) < 0))
                    {
                        yield return (i1.Key, i1.Item);
                        c1 = e1.MoveNext();
                        i1 = c1 ? e1.Current : default;
                    }
                    else
                    {
                        yield return (i2.Key, i2.Item);
                        c2 = e2.MoveNext();
                        i2 = c2 ? e2.Current : default;
                    }
                }
            }
        }

        protected abstract IEnumerable<(TKey Key, TValue Value)> FindInternal(byte[] key_prefix);

        public IEnumerable<Trackable> GetChangeSet()
        {
            lock (dictionary)
            {
                foreach (Trackable trackable in dictionary.Values.Where(p => p.State != TrackState.None))
                    yield return trackable;
            }
        }

        protected abstract TValue GetInternal(TKey key);

        /// <summary>
        /// Try to Get a specific key from current cached dictionary.
        /// Otherwise, tries to get from internal data with TryGetInternal.
        /// </summary>
        /// <param name="key">Key to be searched.</param>
        /// <param name="factory">Function that may replace current object stored. 
        /// If object already exists the factory passed as parameter will not be used.
        /// </param>
        public TValue GetAndChange(TKey key, Func<TValue> factory = null)
        {
            lock (dictionary)
            {
                if (dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Deleted)
                    {
                        if (factory == null) throw new KeyNotFoundException();
                        trackable.Item = factory();
                        trackable.State = TrackState.Changed;
                    }
                    else if (trackable.State == TrackState.None)
                    {
                        trackable.State = TrackState.Changed;
                    }
                }
                else
                {
                    trackable = new Trackable
                    {
                        Key = key,
                        Item = TryGetInternal(key)
                    };
                    if (trackable.Item == null)
                    {
                        if (factory == null) throw new KeyNotFoundException();
                        trackable.Item = factory();
                        trackable.State = TrackState.Added;
                    }
                    else
                    {
                        trackable.State = TrackState.Changed;
                    }
                    dictionary.Add(key, trackable);
                }
                return trackable.Item;
            }
        }

        public TValue GetOrAdd(TKey key, Func<TValue> factory)
        {
            lock (dictionary)
            {
                if (dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Deleted)
                    {
                        trackable.Item = factory();
                        trackable.State = TrackState.Changed;
                    }
                }
                else
                {
                    trackable = new Trackable
                    {
                        Key = key,
                        Item = TryGetInternal(key)
                    };
                    if (trackable.Item == null)
                    {
                        trackable.Item = factory();
                        trackable.State = TrackState.Added;
                    }
                    else
                    {
                        trackable.State = TrackState.None;
                    }
                    dictionary.Add(key, trackable);
                }
                return trackable.Item;
            }
        }

        public TValue TryGet(TKey key)
        {
            lock (dictionary)
            {
                if (dictionary.TryGetValue(key, out Trackable trackable))
                {
                    if (trackable.State == TrackState.Deleted) return null;
                    return trackable.Item;
                }
                TValue value = TryGetInternal(key);
                if (value == null) return null;
                dictionary.Add(key, new Trackable
                {
                    Key = key,
                    Item = value,
                    State = TrackState.None
                });
                return value;
            }
        }

        protected abstract TValue TryGetInternal(TKey key);

        protected abstract void UpdateInternal(TKey key, TValue value);
    }
}
