/*
    MIT License

    Copyright (c) 2021 Tautvydas Versockas
*/

namespace Kafka.OffsetManagement;

/// <summary>
/// Manages out of order offsets.
/// </summary>
public class KafkaOffsetManager : IDisposable
{
    /// <summary>
    /// Interval between unsuccessfull reset tries.
    /// </summary>
    public TimeSpan ResetCheckInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    private readonly object _lock = new();
    private readonly IntegerArrayLinkedList _unackedOffsets;
    private readonly SemaphoreSlim _addSemaphore;

    private long? _lastOffset;
    private bool _disposed;

    /// <summary>
    /// Creates offset manager.
    /// </summary>
    /// <param name="maxOutstanding">Max number of unacknowledged offsets at the same time.</param>
    public KafkaOffsetManager(int maxOutstanding)
    {
        _unackedOffsets = new IntegerArrayLinkedList(maxOutstanding);
        _addSemaphore = new SemaphoreSlim(maxOutstanding, maxOutstanding);
    }

    /// <summary>
    /// Returns offset acknowledgement ID
    /// that can later be used to acknowledge the offset.
    /// Waits if offset manager has maxOutstanding unacknowledged offsets.
    /// </summary>
    public async Task<AckId> GetAckIdAsync(long offset, CancellationToken token = default)
    {
        await _addSemaphore.WaitAsync(token);

        lock (_lock)
        {
            UpdateLastOffset(offset);
            return _unackedOffsets.Add(offset);
        }
    }

    /// <summary>
    /// Acknowledges.
    /// </summary>
    public void Ack(AckId ackId)
    {
        lock (_lock)
        {
            _unackedOffsets.Remove(ackId);
        }

        _addSemaphore.Release();
    }

    /// <summary>
    /// Marks offset as acknowledged. Can only be used in a sequential manner.
    /// </summary>
    public void MarkAsAcked(long offset)
    {
        lock (_lock)
        {
            UpdateLastOffset(offset);
        }
    }

    /// <summary>
    /// Returns offset that can be safely commited. 
    /// Returns null if no offset can be commited safely.
    /// </summary>
    public long? GetCommitOffset()
    {
        lock (_lock)
        {
            return _unackedOffsets.First() ?? _lastOffset + 1;
        }
    }

    /// <summary>
    /// Waits until there are no unacknowledged offsets 
    /// and resets current manager instance to the initial state.
    /// </summary>
    public async Task ResetAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            lock (_lock)
            {
                if (TryReset())
                    return;
            }

            await Task.Delay(ResetCheckInterval, token);
        }
    }

    private void UpdateLastOffset(long offset)
    {
        if (offset <= _lastOffset)
            throw KafkaOffsetManagementException.OffsetOutOfOrder(
                $"Offset {offset} must be greater than last added offset {_lastOffset}.");

        _lastOffset = offset;
    }

    private bool TryReset()
    {
        if (_unackedOffsets.First() is not null)
            return false;

        _lastOffset = null;
        return true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _addSemaphore.Dispose();
        }

        _disposed = true;
    }

    internal sealed class IntegerArrayLinkedList
    {
        private readonly LinkedList<long> _list = new();
        private readonly LinkedListNode<long>?[] _array;
        private readonly Queue<int> _freeAddresses = new();

        public IntegerArrayLinkedList(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentException("Capacity must be greater than 0.", nameof(capacity));

            _array = new LinkedListNode<long>?[capacity];

            for (var i = 0; i < capacity; i++)
                _freeAddresses.Enqueue(i);
        }

        public int Add(long val)
        {
            if (_freeAddresses.Count is 0)
                throw new InvalidOperationException("The list is full.");

            var node = _list.AddLast(val);
            var address = _freeAddresses.Dequeue();
            _array[address] = node;
            return address;
        }

        public void Remove(int address)
        {
            if (!IsAddressValid(address))
                throw new ArgumentException("Bad address.", nameof(address));

            var node = _array[address];
            if (node is not null)
            {
                _array[address] = null;
                _list.Remove(node);
                _freeAddresses.Enqueue(address);
            }
        }

        public long? First()
        {
            return _list.First?.Value;
        }

        private bool IsAddressValid(int address)
        {
            return address >= 0 && address < _array.Length;
        }
    }

    ~KafkaOffsetManager()
    {
        Dispose(false);
    }
}


/// <summary>
/// Acknowledgement ID.
/// </summary>
public struct AckId : IEquatable<AckId>
{
    /// <summary>
    /// Acknowledgement ID value.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Creates acknowledgement ID based on integer value.
    /// </summary>
    public AckId(int value)
    {
        Value = value;
    }

    public static implicit operator int(AckId ackId)
    {
        return ackId.Value;
    }

    public static implicit operator AckId(int value)
    {
        return new(value);
    }

    public override bool Equals(object? obj)
    {
        return obj is AckId ackId && Equals(ackId);
    }

    public bool Equals(AckId other)
    {
        return other.Value == Value;
    }

    public static bool operator ==(AckId a, AckId b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(AckId a, AckId b)
    {
        return !(a == b);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }
}

/// <summary>
/// Offset management exception.
/// </summary>
[Serializable]
public class KafkaOffsetManagementException : Exception
{
    /// <summary>
    /// Returns offset out of order exception.
    /// </summary>
    public static KafkaOffsetManagementException OffsetOutOfOrder(string message) =>
        new(KafkaOffsetManagementErrorCode.OffsetOutOfOrder, message);

    /// <summary>
    /// Offset management error code.
    /// </summary>
    public KafkaOffsetManagementErrorCode ErrorCode { get; }

    internal KafkaOffsetManagementException(KafkaOffsetManagementErrorCode errorCode, string message)
       : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Offset management error code.
/// </summary>
public enum KafkaOffsetManagementErrorCode
{
    /// <summary>
    /// No error.
    /// </summary>
    NoError = 0,

    /// <summary>
    /// Offset is out of order.
    /// </summary>
    OffsetOutOfOrder = 1
}