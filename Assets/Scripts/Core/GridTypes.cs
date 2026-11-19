using System;

namespace TiltBox.Core
{
    /// <summary>Swipe direction. Grid origin is the top-left cell, so Up means y - 1.</summary>
    public enum Direction
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3,
    }

    public static class DirectionExtensions
    {
        public static readonly Direction[] All = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

        public static GridPos Offset(this Direction dir)
        {
            switch (dir)
            {
                case Direction.Up: return new GridPos(0, -1);
                case Direction.Down: return new GridPos(0, 1);
                case Direction.Left: return new GridPos(-1, 0);
                case Direction.Right: return new GridPos(1, 0);
                default: throw new ArgumentOutOfRangeException(nameof(dir), dir, null);
            }
        }

        /// <summary>Single-letter form used by tools and logs: U D L R.</summary>
        public static char ToLetter(this Direction dir)
        {
            switch (dir)
            {
                case Direction.Up: return 'U';
                case Direction.Down: return 'D';
                case Direction.Left: return 'L';
                case Direction.Right: return 'R';
                default: throw new ArgumentOutOfRangeException(nameof(dir), dir, null);
            }
        }
    }

    /// <summary>Integer cell coordinate. X grows to the right, Y grows downwards.</summary>
    public readonly struct GridPos : IEquatable<GridPos>
    {
        public readonly int X;
        public readonly int Y;

        public GridPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static GridPos operator +(GridPos a, GridPos b) => new GridPos(a.X + b.X, a.Y + b.Y);
        public static bool operator ==(GridPos a, GridPos b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(GridPos a, GridPos b) => !(a == b);

        public GridPos Step(Direction dir) => this + dir.Offset();

        public bool Equals(GridPos other) => this == other;
        public override bool Equals(object obj) => obj is GridPos other && this == other;
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => "(" + X + "," + Y + ")";
    }

    /// <summary>The six block colours. Each has a symbol so the game is playable without colour vision.</summary>
    public enum BlockColor
    {
        Red = 0,    // r  circle
        Blue = 1,   // b  triangle
        Yellow = 2, // y  square
        Green = 3,  // g  star
        Purple = 4, // p  heart
        Black = 5,  // k  diamond

        /// <summary>World 7 key block: no colour, fits only a keyhole, never needs binning to win.</summary>
        Key = 6,    // written as "key": true in level files; 'x' in debug text
    }

    public static class BlockColors
    {
        /// <summary>Real colours (excludes <see cref="BlockColor.Key"/>).</summary>
        public const int Count = 6;
        private const string Letters = "rbygpk";

        /// <summary>Maps a lowercase letter (block) or uppercase letter (bin) to a colour. Never returns Key.</summary>
        public static bool TryFromLetter(char c, out BlockColor color)
        {
            int i = Letters.IndexOf(char.ToLowerInvariant(c));
            color = i < 0 ? BlockColor.Red : (BlockColor)i;
            return i >= 0;
        }

        public static char ToLetter(this BlockColor color) => color == BlockColor.Key ? 'x' : Letters[(int)color];
    }
}
