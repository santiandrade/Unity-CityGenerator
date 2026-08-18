using System.Collections.Generic;

namespace CityGenerator.Editor
{
    internal static class CityGeneratorRandomUtility
    {
        public static void Shuffle<T>(IList<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
