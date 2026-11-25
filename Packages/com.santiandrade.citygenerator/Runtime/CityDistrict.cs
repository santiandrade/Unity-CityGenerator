using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// SPEC 23: runtime copy of one generator District (name and blocks), written into
    /// <see cref="CityGeneratorInfo.districts"/> by the content assembler on every Build/Re-Build so
    /// a game can ask which district a world position lies in, also in a player build.
    /// </summary>
    [Serializable]
    public struct CityDistrict
    {
        [Tooltip("District name as set in the generator (trimmed).")]
        public string name;
        [Tooltip("Blocks (gridX, gridY) of this district, same convention as the generator's settings.")]
        public List<Vector2Int> cells;
    }
}
