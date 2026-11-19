using System.Collections.Generic;
using System.Linq;

namespace TiltBox.Meta
{
    public enum ShelfItemType
    {
        Skin = 0,       // how the board and blocks look
        Background = 1,
        Figurine = 2,   // stands on the shelf
    }

    /// <summary>How an item can be obtained.</summary>
    public enum ShelfSource
    {
        Shop = 0,        // buy with coins
        WorldChest = 1,  // the figurine from finishing a world
        Calendar = 2,    // day 7 of the gift calendar
        Tower = 3,       // reaching the top of a Weekend Tower
    }

    public sealed class ShelfItem
    {
        public string Id { get; }
        public string Name { get; }
        public ShelfItemType Type { get; }
        public ShelfSource Source { get; }
        public int Price { get; }

        /// <summary>World for world-chest figurines, cycle for calendar skins, tower for tower skins.</summary>
        public int Number { get; }

        public ShelfItem(string id, string name, ShelfItemType type, ShelfSource source, int price, int number = 0)
        {
            Id = id;
            Name = name;
            Type = type;
            Source = source;
            Price = price;
            Number = number;
        }
    }

    /// <summary>
    /// GDD §8 shelf: 40 items (12 box skins 150-400, 8 backgrounds 300-800, 20 figurines 200-1,200 of which one per
    /// world comes from the world chest). The calendar-only and tower-exclusive skins (§9) are extra, outside the 40:
    /// the GDD gives both a fixed shelf of 12 skins and 8 rotating tower skins, which cannot both fit in 12.
    /// </summary>
    public static class ShelfCatalog
    {
        public const int CalendarSkinCycles = 4;

        public static readonly IReadOnlyList<ShelfItem> All = Build();

        public static ShelfItem Find(string id) => All.FirstOrDefault(i => i.Id == id);

        public static IEnumerable<ShelfItem> Shop => All.Where(i => i.Source == ShelfSource.Shop);

        /// <summary>The 40 launch items (shop + world-chest figurines).</summary>
        public static IEnumerable<ShelfItem> LaunchShelf => All.Where(i => i.Source == ShelfSource.Shop || i.Source == ShelfSource.WorldChest);

        public static ShelfItem WorldFigurine(int world) => All.First(i => i.Source == ShelfSource.WorldChest && i.Number == world);
        public static ShelfItem CalendarSkin(int cycle) => All.FirstOrDefault(i => i.Source == ShelfSource.Calendar && i.Number == cycle);
        public static ShelfItem TowerSkin(int tower) => All.First(i => i.Source == ShelfSource.Tower && i.Number == tower);

        private static List<ShelfItem> Build()
        {
            var items = new List<ShelfItem>();
            string[] skins = { "Wood", "Candy", "Chalk", "Felt", "Marble", "Jelly", "Cardboard", "Brick", "Neon", "Metal", "Glass", "Gold" };
            for (int i = 0; i < skins.Length; i++)
                items.Add(new ShelfItem("skin_" + skins[i].ToLowerInvariant(), skins[i] + " box", ShelfItemType.Skin, ShelfSource.Shop, 150 + 250 * i / (skins.Length - 1) / 10 * 10));

            string[] backgrounds = { "Toy Room", "Ice Rink", "Train Yard", "Candy Shop", "Space Lab", "Art Studio", "Castle", "Rooftop" };
            for (int i = 0; i < backgrounds.Length; i++)
                items.Add(new ShelfItem("bg_" + backgrounds[i].Replace(" ", "").ToLowerInvariant(), backgrounds[i], ShelfItemType.Background,
                    ShelfSource.Shop, 300 + 500 * i / (backgrounds.Length - 1) / 10 * 10));

            string[] worldFigs = { "Teddy", "Penguin", "Train", "Gummy Bear", "Astronaut", "Easel", "Knight", "Robot Arm", "Wrench", "Pigeon" };
            for (int w = 1; w <= worldFigs.Length; w++)
                items.Add(new ShelfItem("fig_w" + w, worldFigs[w - 1], ShelfItemType.Figurine, ShelfSource.WorldChest, 0, w));

            string[] shopFigs = { "Rubber Duck", "Spinning Top", "Yo-yo", "Dice", "Rocket", "Dino", "Owl", "Cactus", "Lighthouse", "Golden Box" };
            for (int i = 0; i < shopFigs.Length; i++)
                items.Add(new ShelfItem("fig_" + shopFigs[i].Replace(" ", "").Replace("-", "").ToLowerInvariant(), shopFigs[i], ShelfItemType.Figurine,
                    ShelfSource.Shop, 200 + 1000 * i / (shopFigs.Length - 1) / 50 * 50));

            for (int c = 1; c <= CalendarSkinCycles; c++)
                items.Add(new ShelfItem("skin_calendar" + c, "Calendar box " + c, ShelfItemType.Skin, ShelfSource.Calendar, 0, c));
            for (int t = 1; t <= 8; t++)
                items.Add(new ShelfItem("skin_tower" + t, "Tower box " + t, ShelfItemType.Skin, ShelfSource.Tower, 0, t));
            return items;
        }
    }
}
