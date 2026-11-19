using System;

namespace TiltBox.Core
{
    /// <summary>What a generated level slot must look like. Built by <see cref="DifficultyPlan"/>.</summary>
    public sealed class SlotSpec
    {
        public string Id;
        public int World;
        public int Number;
        public LevelKind Kind;
        public int Width, Height;
        public int MinColours, MaxColours;

        /// <summary>Coloured blocks beyond one per colour (keys and linked pairs come on top).</summary>
        public int MinExtraBlocks, MaxExtraBlocks;

        public int MinWalls, MaxWalls;
        public int MinIce, MaxIce;
        public int MinGates, MaxGates;
        public int MinSticky, MaxSticky;
        public int MinPortalPairs, MaxPortalPairs;
        public int MinPaint, MaxPaint;

        /// <summary>0 or 1: a key block, a keyhole and lock walls.</summary>
        public int MinKeys, MaxKeys;

        public int MinLocks, MaxLocks;
        public int MinConveyors, MaxConveyors;
        public int MinLinks, MaxLinks;

        /// <summary>World 10: pick 2-3 earlier mechanics at random for each board.</summary>
        public bool Mastery;

        public int ParMin, ParMax;
        public int Slack;
        public double MaxRandomWin;
        public bool RequireDeadEnd;
        public int MaxOptimalSolutions;

        /// <summary>The optimal solution must use the world's mechanic (§4).</summary>
        public bool RequireMechanicUse;

        /// <summary>Teach levels: removing the world's mechanic must make the level unwinnable (§4).</summary>
        public bool RequireMechanicEssential;

        /// <summary>Tower rule (§9); the rule must change the puzzle (see <see cref="LevelGenerator"/>).</summary>
        public RuleSet Rules = RuleSet.Standard;

        public override string ToString() =>
            Id + " " + Kind + " W" + World + " " + Width + "x" + Height + " par " + ParMin + "-" + ParMax +
            " colours " + MinColours + "-" + MaxColours + (Mastery ? " mastery" : "") +
            (Rules.IsStandard ? "" : " [" + Rules.Describe() + "]");
    }

    /// <summary>
    /// GDD §4, §6 and §9 as code: the difficulty wave, par bands, slack, board sizes, colours and mechanics per
    /// world, plus the daily-puzzle and Weekend Tower specs. The generator builds levels from this plan and the
    /// content tests check shipped levels against it, so the two can never disagree.
    /// </summary>
    public static class DifficultyPlan
    {
        public const int LevelsPerWorld = 30;
        public const int MaxOptimalSolutions = 3;
        public const int SupportedWorlds = 10;
        public const int DailyCount = 365;
        public const int TowerCount = 8;
        public const int LevelsPerTower = 10;

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

        // §4 board sizes by decade (levels 1-10, 11-20, 21-30): width, height.
        private static readonly int[][][] Sizes =
        {
            null,
            new[] { new[] { 5, 5 }, new[] { 5, 6 }, new[] { 6, 6 } },
            new[] { new[] { 5, 5 }, new[] { 5, 6 }, new[] { 6, 6 } },
            new[] { new[] { 5, 6 }, new[] { 6, 6 }, new[] { 6, 7 } },
            new[] { new[] { 6, 6 }, new[] { 6, 7 }, new[] { 6, 7 } },
            new[] { new[] { 6, 6 }, new[] { 6, 7 }, new[] { 6, 8 } },
            new[] { new[] { 6, 7 }, new[] { 7, 7 }, new[] { 7, 7 } },
            new[] { new[] { 6, 7 }, new[] { 7, 7 }, new[] { 7, 8 } },
            new[] { new[] { 6, 7 }, new[] { 7, 7 }, new[] { 7, 8 } },
            new[] { new[] { 6, 7 }, new[] { 7, 7 }, new[] { 7, 8 } },
            new[] { new[] { 7, 8 }, new[] { 7, 8 }, new[] { 7, 8 } },
        };

        // §4 colours by decade: min, max.
        private static readonly int[][][] Colours =
        {
            null,
            new[] { new[] { 2, 3 }, new[] { 2, 2 }, new[] { 2, 3 } },
            new[] { new[] { 2, 2 }, new[] { 2, 3 }, new[] { 2, 3 } },
            new[] { new[] { 2, 3 }, new[] { 2, 4 }, new[] { 3, 4 } },
            new[] { new[] { 3, 3 }, new[] { 3, 4 }, new[] { 3, 4 } },
            new[] { new[] { 3, 3 }, new[] { 3, 4 }, new[] { 3, 4 } },
            new[] { new[] { 3, 3 }, new[] { 3, 4 }, new[] { 4, 5 } },
            new[] { new[] { 3, 3 }, new[] { 3, 4 }, new[] { 4, 5 } },
            new[] { new[] { 3, 3 }, new[] { 3, 4 }, new[] { 4, 5 } },
            new[] { new[] { 4, 4 }, new[] { 4, 5 }, new[] { 5, 6 } },
            new[] { new[] { 4, 5 }, new[] { 4, 5 }, new[] { 5, 6 } },
        };

        // Tower t (1-8): source world and rule (§9 examples).
        private static readonly int[] TowerWorld = { 0, 4, 5, 8, 6, 7, 8, 9, 10 };

        public static string IdFor(int world, int number) => "w" + world + "_" + number.ToString("D2");
        public static string DailyId(int day) => "d" + day.ToString("D3");
        public static string TowerId(int tower, int level) => "t" + tower + "_" + level.ToString("D2");

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
        /// Easy: band minimum − 1 (never below 2). Hard: band maximum + 2. Teach: short (2 to 5).
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
                case LevelKind.Normal:
                case LevelKind.Daily: return 0.15;
                case LevelKind.Easy: return 0.25;
                default: return 1.0;
            }
        }

        public static bool RequiresDeadEnd(LevelKind kind) => kind == LevelKind.Normal || kind == LevelKind.Hard || kind == LevelKind.Daily;

        public static SlotSpec SpecFor(int world, int number)
        {
            LevelKind kind = KindFor(world, number);
            ParRange(world, number, out int parMin, out int parMax);
            int decade = (number - 1) / 10;
            var spec = Base(world, decade, kind);
            spec.Id = IdFor(world, number);
            spec.Number = number;
            spec.ParMin = parMin;
            spec.ParMax = parMax;

            if (kind == LevelKind.Teach)
            {
                int[] size = number == 1 ? new[] { 4, 4 } : number == 2 ? new[] { 4, 5 } : new[] { 5, 5 };
                spec.Width = size[0];
                spec.Height = size[1];
                spec.MinColours = 1;
                spec.MaxColours = 2;
                spec.MinExtraBlocks = 0;
                spec.MaxExtraBlocks = 1;
                spec.MinWalls = 1;
                spec.MaxWalls = spec.Width * spec.Height / 5;
                OnlyNewMechanic(spec, world);
                spec.RequireMechanicEssential = world >= 2;
            }
            return spec;
        }

        /// <summary>Daily puzzle <paramref name="day"/> (1-365): par 7-10 (§9), mechanics cycle through worlds 2-10.</summary>
        public static SlotSpec DailySpec(int day)
        {
            if (day < 1 || day > DailyCount) throw new ArgumentOutOfRangeException(nameof(day));
            int world = 2 + (day - 1) % 9;
            var spec = Base(world, 1, LevelKind.Daily);
            spec.Id = DailyId(day);
            spec.Number = day;
            spec.ParMin = 7;
            spec.ParMax = 10;
            spec.MinColours = Math.Min(spec.MinColours, 3); // keep daily boards quick to read
            spec.MaxColours = Math.Min(spec.MaxColours, 4);
            return spec;
        }

        /// <summary>Weekend Tower <paramref name="tower"/> (1-8), level 1-10: hard levels under one special rule (§9).</summary>
        public static SlotSpec TowerSpec(int tower, int level)
        {
            if (tower < 1 || tower > TowerCount) throw new ArgumentOutOfRangeException(nameof(tower));
            if (level < 1 || level > LevelsPerTower) throw new ArgumentOutOfRangeException(nameof(level));
            int world = TowerWorld[tower];
            var spec = Base(world, 1, LevelKind.Hard);
            spec.Id = TowerId(tower, level);
            spec.Number = level;
            spec.Rules = TowerRules(tower);
            spec.ParMin = 6 + level / 2;       // 6 .. 11, rising through the tower
            spec.ParMax = spec.ParMin + 1;
            spec.MaxRandomWin = 0.05;
            spec.MinColours = Math.Min(spec.MinColours, 3);
            spec.MaxColours = Math.Min(spec.MaxColours, 4);
            if (spec.Rules.ConveyorPasses > 1) { spec.MinConveyors = Math.Max(spec.MinConveyors, 2); spec.MaxConveyors = Math.Max(spec.MaxConveyors, 4); }
            return spec;
        }

        public static RuleSet TowerRules(int tower)
        {
            switch (tower)
            {
                case 1: return new RuleSet(Direction.Up, false, 1);
                case 2: return new RuleSet(null, true, 1);
                case 3: return new RuleSet(null, false, 2);
                case 4: return new RuleSet(Direction.Left, false, 1);
                case 5: return new RuleSet(null, true, 1);
                case 6: return new RuleSet(null, false, 2);
                case 7: return new RuleSet(Direction.Down, false, 1);
                default: return new RuleSet(Direction.Right, false, 1);
            }
        }

        /// <summary>Size, colours, walls and mechanic counts for a world and decade; the slot adjusts par and kind.</summary>
        private static SlotSpec Base(int world, int decade, LevelKind kind)
        {
            CheckWorld(world);
            var spec = new SlotSpec
            {
                World = world,
                Kind = kind,
                Width = Sizes[world][decade][0],
                Height = Sizes[world][decade][1],
                MinColours = Colours[world][decade][0],
                MaxColours = Colours[world][decade][1],
                MinExtraBlocks = 0,
                MaxExtraBlocks = world <= 3 ? 2 : 1,
                Slack = Slack(world),
                MaxRandomWin = MaxRandomWin(kind),
                RequireDeadEnd = RequiresDeadEnd(kind),
                MaxOptimalSolutions = MaxOptimalSolutions,
                RequireMechanicUse = world >= 2,
            };
            int cells = spec.Width * spec.Height;
            spec.MinWalls = cells / 9;
            spec.MaxWalls = cells / 5;

            // Each world adds one mechanic (required) and keeps the earlier ones in small doses (§4).
            if (world == 2) { spec.MinIce = 1; spec.MaxIce = 2; }
            if (world >= 3) spec.MaxIce = 1;
            if (world == 3) { spec.MinGates = 1; spec.MaxGates = 3; }
            if (world >= 4) spec.MaxGates = 1;
            if (world == 4) { spec.MinSticky = 1; spec.MaxSticky = 3; }
            if (world >= 5) spec.MaxSticky = 1;
            if (world == 5) { spec.MinPortalPairs = 1; spec.MaxPortalPairs = 2; }
            if (world >= 6) spec.MaxPortalPairs = 1;
            if (world == 6) { spec.MinPaint = 1; spec.MaxPaint = 2; }
            if (world >= 7) spec.MaxPaint = 1;
            if (world == 7) { spec.MinKeys = 1; spec.MaxKeys = 1; spec.MinLocks = 1; spec.MaxLocks = 3; }
            if (world == 8) { spec.MinConveyors = 1; spec.MaxConveyors = 4; }
            if (world == 9) { spec.MinLinks = 1; spec.MaxLinks = 2; spec.MaxConveyors = 1; }
            if (world == 10)
            {
                // Mastery: the generator switches on 2-3 mechanics per board, so clear the defaults.
                spec.Mastery = true;
                spec.MaxIce = spec.MaxGates = spec.MaxSticky = spec.MaxPortalPairs = spec.MaxPaint = 0;
            }
            if (world >= 8 && world < 10) { spec.MaxGates = spec.MaxSticky = 1; }
            return spec;
        }

        /// <summary>Teach boards show only the new mechanic.</summary>
        private static void OnlyNewMechanic(SlotSpec s, int world)
        {
            s.MinIce = s.MaxIce = world == 2 ? 1 : 0;
            s.MinGates = world == 3 ? 1 : 0; s.MaxGates = world == 3 ? 2 : 0;
            s.MinSticky = world == 4 ? 1 : 0; s.MaxSticky = world == 4 ? 2 : 0;
            s.MinPortalPairs = s.MaxPortalPairs = world == 5 ? 1 : 0;
            s.MinPaint = s.MaxPaint = world == 6 ? 1 : 0;
            s.MinKeys = s.MaxKeys = world == 7 ? 1 : 0;
            s.MinLocks = world == 7 ? 1 : 0; s.MaxLocks = world == 7 ? 2 : 0;
            s.MinConveyors = world == 8 ? 1 : 0; s.MaxConveyors = world == 8 ? 2 : 0;
            s.MinLinks = s.MaxLinks = world == 9 ? 1 : 0;
            if (world == 10) { s.Mastery = true; }
        }

        private static int CheckWorld(int world)
        {
            if (world < 1 || world >= WorldNames.Length) throw new ArgumentOutOfRangeException(nameof(world));
            return world;
        }
    }
}
