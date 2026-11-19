namespace TiltBox.Core
{
    public enum LevelKind
    {
        Normal = 0,
        Easy = 1,
        Hard = 2,
        Teach = 3,
    }

    /// <summary>A parsed, validated level (GDD §5). Immutable.</summary>
    public sealed class LevelDefinition
    {
        public string Id { get; }
        public int World { get; }
        public BoardLayout Layout { get; }
        public BoardState Start { get; }

        /// <summary>Fewest swipes that win (from the solver). 3 stars at or under par.</summary>
        public int Par { get; }

        /// <summary>Swipes allowed before "Out of moves". par + world slack (§6).</summary>
        public int Limit { get; }

        public LevelKind Kind { get; }

        public LevelDefinition(string id, int world, BoardLayout layout, BoardState start, int par, int limit, LevelKind kind)
        {
            Id = id;
            World = world;
            Layout = layout;
            Start = start;
            Par = par;
            Limit = limit;
            Kind = kind;
        }
    }
}
