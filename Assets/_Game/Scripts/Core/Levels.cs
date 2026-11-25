namespace ExpandPuzzle
{
    /// <summary>One level: the map, plus an optional colour layer of the same size.</summary>
    public struct LevelDef
    {
        /// <summary>Fewest moves the level can be solved in. Decides the star rating.</summary>
        public int par;
        public string[] rows;
        public string[] colors;

        public LevelDef(int par, string[] rows, string[] colors = null)
        {
            this.par = par;
            this.rows = rows;
            this.colors = colors;
        }
    }

    /// <summary>
    /// Built-in levels. See <see cref="LevelData.Parse"/> for the legend.
    /// Add a new level by appending another block.
    /// </summary>
    public static class Levels
    {
        public static readonly LevelDef[] All =
        {
            // 1 - walk to the pump, push into it once, park on the marks.
            new LevelDef(7, new[]
            {
                "######v##",
                "#H.....T#",
                "#......T#",
                "#########",
            }),
            // 2 - pump more than once, then carry the long shape.
            new LevelDef(7, new[]
            {
                "#########",
                "#H......#",
                ">.......#",
                "#.......#",
                "#..TTT..#",
                "#########",
            }),
            // 3 - two pumps, two directions: make a square that fits the shaft.
            new LevelDef(13, new[]
            {
                "###v#####",
                "#..H....#",
                "#.......<",
                "#.......#",
                "###..####",
                "###TT####",
                "###TT####",
                "#########",
            }),
            // 4 - forbidden tiles: bigger is not always better.
            new LevelDef(13, new[]
            {
                "#########",
                "#H......#",
                "#.......<",
                "#.......#",
                "#.XTTTX.#",
                "#########",
            }),
            // 5 - a wall stops part of the growth: use it to sculpt an L.
            new LevelDef(16, new[]
            {
                "####v####",
                "#...H...#",
                "#.....#.<",
                "#.......#",
                "#.......#",
                "#XT######",
                "#TT######",
                "#########",
            }),
            // 6 - order matters: only a flat shape fits through the door.
            new LevelDef(13, new[]
            {
                "##########v####",
                "#...#.........#",
                ">H......XTTTX.#",
                "#...#...XTTTX.#",
                "#...#.........#",
                "###############",
            }),
            // 7 - two moulds, one T.
            new LevelDef(23, new[]
            {
                "###########",
                "#....H....#",
                ">....P....<",
                "#.#....#.##",
                "#..XTTTX..#",
                "#####T#####",
                "###########",
            }),
            // 8 - first coloured pump: new cells come out green, and the marks want matching colours.
            new LevelDef(13, new[]
            {
                "#########",
                "#H......#",
                "#.......<",
                "#.......#",
                "#..TT...#",
                "#########",
            }, new[]
            {
                ".........",
                ".........",
                "........1",
                ".........",
                "...10....",
                ".........",
            }),
            // 9 - plain pump copies the colour next to it, green pump paints: the order decides the pattern.
            new LevelDef(17, new[]
            {
                "###########",
                "#H........<",
                "#.........<",
                "#.........#",
                "#.XTTTX...#",
                "###########",
            }, new[]
            {
                "...........",
                "...........",
                "..........1",
                "...........",
                "...100.....",
                "...........",
            }),
            // 10 - sculpt and paint at once: a green foot on the L.
            new LevelDef(16, new[]
            {
                "####v####",
                "#...H...#",
                "#.....#.<",
                "#.......#",
                "#.......<",
                "#XT######",
                "#TT######",
                "#########",
            }, new[]
            {
                ".........",
                ".........",
                "........1",
                ".........",
                ".........",
                "..0......",
                ".10......",
                ".........",
            }),
        };

        public static int Count { get { return All.Length; } }

        public static LevelData Load(int index)
        {
            index = ((index % All.Length) + All.Length) % All.Length;
            LevelData level = LevelData.Parse("Level " + (index + 1), All[index].rows, All[index].colors);
            level.par = All[index].par;
            return level;
        }
    }
}
