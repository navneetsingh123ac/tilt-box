using System;

namespace TiltBox.Core
{
    /// <summary>What a generated level slot must look like. Built by <see cref="DifficultyPlan.SpecFor"/>.</summary>
    public sealed class SlotSpec
    {
        public int World;
        public int Number;
        public LevelKind Kind;
        public int Width, Height;
        public int MinColours, MaxColours;
        public int MinBlocks, MaxBlocks;
        public int MinWalls, MaxWalls;
        public int MinIce, MaxIce;
        public int MinGates, MaxGates;
        public int ParMin, ParMax;
        public int Slack;
        public double MaxRandomWin;
        public bool RequireDeadEnd;
        public int MaxOptimalSolutions;

        /// <summary>The optimal solution must use the world's new mechanic (ice as a stopper / a gate).</summary>
        public bool RequireMechanicUse;

        /// <summary>Teach levels: removing the world's mechanic must make the level unwinnable (§4).</summary>
        public bool RequireMechanicEssential;

        public string Id => DifficultyPlan.IdFor(World, Number);

        public override string ToString() =>
            Id + " " + Kind + " " + Width + "x" + Height + " par " + ParMin + "-" + ParMax +
            " colours " + MinColours + "-" + MaxColours + " blocks " + MinBlocks + "-" + MaxBlocks +
            " ice " + MinIce + "-" + MaxIce + " gates " + MinGates + "-" + MaxGates;
    }

    /// <summary>
    /// GDD §4 and §6 as code: the difficulty wave, par bands, slack, board sizes and mechanics per world.
    /// The generator builds levels from this plan and the content tests check shipped levels against it,
    /// so the two can never disagree.
    /// </summary>
    public static class DifficultyPlan
    {
        public const int LevelsPerWorld = 30;
        public const int MaxOptimalSolutions = 3;

        /// <summary>World 1 levels 1-10 are the hand-made first session (§11); they follow §11, not the bands.</summary>
        public const int HandMadeTutorialLevels = 10;

        public static readonly string[] WorldNames =
        {
            "", "Toy Room", "Ice Rink", "Train Yard", "Candy Shop", "Space Lab",
            "Art Studio", "Castle", "Factory", "Workshop", "Rooftop",
        };

        // Index = world. §6 tables.
        private static readonly int[] BandMin = { 0, 1, 4, 5, 5, 6, 6, 7, 7, 8, 8 };
        private static readonly int[] BandMax = { 0, 6, 8, 9, 10, 10, 11, 12, 12, 13, 14 };
        private static readonly int[] SlackByWorld = { 0, 6, 5, 5, 4, 4, 4, 3, 3, 3, 3 };

        /// <summary>Worlds whose mechanics the game supports so far.</summary>
        public const int SupportedWorlds = 3;

        public static string IdFor(int world, int number) => "w" + world + "_" + number.ToString("D2");

        public static int Slack(int world) => SlackByWorld[CheckWorld(world)];

        public static int Limit(int world, int par) => par + Slack(world);

        public static bool IsHandMade(int world, int number) =>
            (world == 1 && number <= HandMadeTutorialLevels) || KindFor(world, number) == LevelKind.Teach;

        /// <summary>§6 wave: in every 10 levels, 1-7 normal, 8-9 easy, 10 hard. The first 3 of a world teach.</summary>
        public static LevelKind KindFor(int world, int number)
        {
            CheckWorld(world);
            if (number < 1 || number > LevelsPerWorld) throw new ArgumentOutOfRangeException(nameof(number));
            if (number <= 3) return LevelKind.Teach;
            int pos = (number - 1) % 10 + 1;
            if (pos == 10) return LevelKind.Hard;
            if (pos >= 8) return LevelKind.Easy;
            return LevelKind.Normal;
        }

        /// <summary>
        /// Allowed par for a slot. Normal: the world's band, rising across its 30 levels (target ± 1).
        /// Easy: band minimum − 1 (never below 2, so a breather is still a puzzle). Hard: band maximum + 2.
        /// Teach: short (2 to 4/5).
        /// </summary>
        public static void ParRange(int world, int number, out int min, out int max)
        {
            int lo = BandMin[CheckWorld(world)], hi = BandMax[world];
            switch (KindFor(world, number))
            {
                case LevelKind.Teach:
                    min = 2;
                    max = world == 1 ? 3 : Math.Min(lo, 5);
                    if (max < min) max = min;
                    return;
                case LevelKind.Easy:
                    min = Math.Max(2, lo - 1);
                    max = Math.Max(2, lo);
                    return;
                case LevelKind.Hard:
                    min = max = hi + 2;
                    return;
                default:
                    double t = (number - 1) / (double)(LevelsPerWorld - 1);
                    int target = (int)Math.Round(lo + t * (hi - lo), MidpointRounding.AwayFromZero);
                    min = Math.Max(lo, target - 1);
                    max = Math.Min(hi, target + 1);
                    return;
            }
        }

        /// <summary>§5 random-win limits. The GDD gives none for easy levels; 25% keeps them from solving themselves.</summary>
        public static double MaxRandomWin(LevelKind kind)
        {
            switch (kind)
            {
                case LevelKind.Hard: return 0.05;
                case LevelKind.Normal: return 0.15;
                case LevelKind.Easy: return 0.25;
                default: return 1.0;
            }
        }

        public static bool RequiresDeadEnd(LevelKind kind) => kind == LevelKind.Normal || kind == LevelKind.Hard;

        public static SlotSpec SpecFor(int world, int number)
        {
            LevelKind kind = KindFor(world, number);
            ParRange(world, number, out int parMin, out int parMax);
            int decade = (number - 1) / 10;
            var spec = new SlotSpec
            {
                World = world,
                Number = number,
                Kind = kind,
                ParMin = parMin,
                ParMax = parMax,
                Slack = Slack(world),
                MaxRandomWin = MaxRandomWin(kind),
                RequireDeadEnd = RequiresDeadEnd(kind),
                MaxOptimalSolutions = MaxOptimalSolutions,
            };

            // Board sizes (§4: W1 3x3 -> 6x6, W2 5x5 -> 6x6, W3 5x6 -> 6x7; teach boards are tiny).
            int[][] sizes;
            switch (world)
            {
                case 1: sizes = new[] { new[] { 5, 5 }, new[] { 5, 6 }, new[] { 6, 6 } }; break;
                case 2: sizes = new[] { new[] { 5, 5 }, new[] { 5, 6 }, new[] { 6, 6 } }; break;
                default: sizes = new[] { new[] { 5, 6 }, new[] { 6, 6 }, new[] { 6, 7 } }; break;
            }
            int[] size = sizes[decade];
            if (kind == LevelKind.Teach)
                size = number == 1 ? new[] { 4, 4 } : number == 2 ? new[] { 4, 5 } : new[] { 5, 5 };
            spec.Width = size[0];
            spec.Height = size[1];

            // Colours (§4: W1 1-3, W2 2-3, W3 2-4), rising through the world.
            switch (world)
            {
                case 1: spec.MinColours = 2; spec.MaxColours = decade == 1 ? 2 : 3; break;
                case 2: spec.MinColours = 2; spec.MaxColours = decade == 0 ? 2 : 3; break;
                default: spec.MinColours = decade == 2 ? 3 : 2; spec.MaxColours = decade == 0 ? 3 : 4; break;
            }
            if (kind == LevelKind.Teach) { spec.MinColours = 1; spec.MaxColours = 2; }
            if (kind == LevelKind.Hard) spec.MinColours = spec.MaxColours;

            spec.MinBlocks = spec.MinColours;
            spec.MaxBlocks = Math.Min(LevelParser.MaxBlocks, spec.MaxColours + (kind == LevelKind.Teach ? 1 : 2));

            int cells = spec.Width * spec.Height;
            spec.MinWalls = cells / 9;
            spec.MaxWalls = cells / 5;

            // Mechanics: each world adds one and keeps the earlier ones (§4).
            if (world == 2) { spec.MinIce = 1; spec.MaxIce = kind == LevelKind.Teach ? 1 : 2; }
            if (world >= 3)
            {
                spec.MaxIce = kind == LevelKind.Teach ? 0 : 1;
                spec.MinGates = 1;
                spec.MaxGates = kind == LevelKind.Teach ? 2 : 3;
            }
            spec.RequireMechanicUse = world >= 2;
            spec.RequireMechanicEssential = world >= 2 && kind == LevelKind.Teach;
            return spec;
        }

        private static int CheckWorld(int world)
        {
            if (world < 1 || world >= WorldNames.Length) throw new ArgumentOutOfRangeException(nameof(world));
            return world;
        }
    }
}
