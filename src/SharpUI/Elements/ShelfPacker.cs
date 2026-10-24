namespace SharpUI;

public sealed class ShelfPacker
{
    readonly int _width, _height;
    int _cursorX, _cursorY, _rowHeight;

    public ShelfPacker(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public (int X, int Y)? TryAlloc(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > _width) return null;
        if (_cursorX + width > _width)
        {
            _cursorY += _rowHeight;
            _cursorX = 0;
            _rowHeight = 0;
        }
        if (_cursorY + height > _height) return null;
        (int X, int Y) result = (_cursorX, _cursorY);
        _cursorX += width;
        _rowHeight = Math.Max(_rowHeight, height);
        return result;
    }

    public void Reset() => (_cursorX, _cursorY, _rowHeight) = (0, 0, 0);
}
