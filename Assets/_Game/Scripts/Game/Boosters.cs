using System;
using UnityEngine;

namespace ExpandPuzzle
{
    public enum BoosterType
    {
        /// <summary>Shows the next moves of the shortest solution.</summary>
        Hint,
        /// <summary>Takes the last move back, move count included.</summary>
        StepBack,
        /// <summary>Destroys one block of the blob.</summary>
        Pop,
        /// <summary>Turns every forbidden tile into normal floor for this level.</summary>
        Clear
    }

    /// <summary>How many of each booster the player owns (remembered between sessions).</summary>
    public static class Boosters
    {
        /// <summary>What a new player starts with, per booster.</summary>
        public const int StartAmount = 3;

        const string Key = "ExpandPuzzle.Booster.";

        /// <summary>Raised whenever a count changes, so the bar can update its badges.</summary>
        public static event Action Changed;

        public static readonly BoosterType[] All = { BoosterType.Hint, BoosterType.StepBack, BoosterType.Pop, BoosterType.Clear };

        public static int Count(BoosterType type)
        {
            return Mathf.Max(0, PlayerPrefs.GetInt(Key + type, StartAmount));
        }

        public static void Add(BoosterType type, int amount)
        {
            PlayerPrefs.SetInt(Key + type, Mathf.Max(0, Count(type) + amount));
            PlayerPrefs.Save();
            if (Changed != null) Changed();
        }

        /// <summary>Takes one from the stock. False (and nothing happens) when there is none left.</summary>
        public static bool Spend(BoosterType type)
        {
            if (Count(type) <= 0) return false;
            Add(type, -1);
            return true;
        }

        public static string Title(BoosterType type)
        {
            switch (type)
            {
                case BoosterType.Hint: return "Hint";
                case BoosterType.StepBack: return "Step back";
                case BoosterType.Pop: return "Bomb";
                default: return "Star shower";
            }
        }
    }
}
