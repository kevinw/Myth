namespace Myth;

 /// <summary>
/// This is a simple RingBuffer implementation for SeeingSharp,
/// originally created for the Input event system.
///
/// More info on cyclic buffers: https://en.wikipedia.org/wiki/Circular_buffer
/// </summary>
public class RingBuffer<T>
{
    // all buffer properties
    T?[] _buffer;
    int _itemStart;
    int _itemLength;
    /// <summary>
    /// Gets the object at the specified index.
    /// </summary>
    public T this[int index]
    {
        get
        {
            if (index < 0){ throw new IndexOutOfRangeException(); }
            if (index >= _itemLength) { throw new IndexOutOfRangeException(); }
            return _buffer[(_itemStart + index) % _buffer.Length]!;
        }
    }
    /// <summary>
    /// Gets the total count of items.
    /// </summary>
    public int Count => _itemLength;
    /// <summary>
    /// Gets the maximum capacity of the buffer.
    /// </summary>
    public int MaxCapacity => _buffer.Length;
    /// <summary>
    /// Initializes a new instance of the <see cref="RingBuffer{T}"/> class.
    /// </summary>
    /// <param name="maxItemCount">The maximum count of items.</param>
    public RingBuffer(int maxItemCount)
    {
    	if (maxItemCount < 2) throw new Exception("must create RingBuffer with at least 2 max item count");
        _buffer = new T[maxItemCount];
        _itemStart = 0;
        _itemLength = 0;
    }
    /// <summary>
    /// Adds a new item to the buffer and overrides the oldest item
    /// if the count of items reached the maximum.
    /// </summary>
    /// <param name="newItem">The new item to be added.</param>
    public void Add(T newItem)
    {
        if (_itemLength < _buffer.Length)
        {
            _buffer[(_itemStart + _itemLength) % _buffer.Length] = newItem;
            _itemLength++;
        }
        else
        {
            _itemStart = (_itemStart + 1) % _buffer.Length;
            var nextIndex = _itemStart - 1;
            if (nextIndex < 0) { nextIndex = _buffer.Length - 1; }
            _buffer[nextIndex] = newItem;
        }
    }

    public void RemoveLast()
    {
	    if (_itemLength == 0)
	    {
	        throw new IndexOutOfRangeException();
	    }
		_itemLength--;
    }

    // ...
    /// <summary>
    /// Removes the first entry.
    /// </summary>
    public void RemoveFirst()
    {
        if (_itemLength == 0)
        {
            throw new IndexOutOfRangeException();
        }
        _itemStart++;
        _itemLength--;
        if (_itemStart == _buffer.Length)
        {
            _itemStart = 0;
        }
    }
    /// <summary>
    /// Clears this collection.
    /// </summary>
    public void Clear()
    {
        _itemLength = 0;
        _itemStart = 0;
    }
}
