using System;

namespace TiltBox.Core
{
    /// <summary>
    /// Special rules for Weekend Tower levels (GDD §9): "only 3 directions allowed, swipes reversed, or
    /// conveyors that move twice". Normal levels use <see cref="Standard"/>. Immutable.
    /// </summary>
    public sealed class RuleSet
    {
        public static readonly RuleSet Standard = new RuleSet(null, false, 1);

        /// <summary>A direction the player may not swipe, or null.</summary>
        public Direction? Forbidden { get; }

        /// <summary>The board moves the opposite way to the swipe.</summary>
        public bool Reversed { get; }

        /// <summary>How many conveyor steps run after each swipe (normally 1).</summary>
        public int ConveyorPasses { get; }

        public RuleSet(Direction? forbidden, bool reversed, int conveyorPasses)
        {
            if (conveyorPasses < 1 || conveyorPasses > 3) throw new ArgumentOutOfRangeException(nameof(conveyorPasses));
            Forbidden = forbidden;
            Reversed = reversed;
            ConveyorPasses = conveyorPasses;
        }

        public bool IsStandard => Forbidden == null && !Reversed && ConveyorPasses == 1;

        /// <summary>Whether the board may move this way (after reversal).</summary>
        public bool AllowsMove(Direction boardDirection) => Forbidden != boardDirection;

        /// <summary>The board direction a player swipe produces.</summary>
        public Direction BoardDirection(Direction swipe) => Reversed ? Opposite(swipe) : swipe;

        /// <summary>The swipe that produces a board direction (for showing hints).</summary>
        public Direction SwipeFor(Direction boardDirection) => Reversed ? Opposite(boardDirection) : boardDirection;

        public static Direction Opposite(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                case Direction.Left: return Direction.Right;
                default: return Direction.Left;
            }
        }

        /// <summary>Short text for the HUD.</summary>
        public string Describe()
        {
            if (IsStandard) return "";
            var parts = new System.Collections.Generic.List<string>();
            if (Forbidden != null) parts.Add("No swiping " + Forbidden.Value.ToString().ToLowerInvariant());
            if (Reversed) parts.Add("Swipes are reversed");
            if (ConveyorPasses > 1) parts.Add("Conveyors move " + (ConveyorPasses == 2 ? "twice" : ConveyorPasses + " times"));
            return string.Join(" · ", parts);
        }
    }
}
