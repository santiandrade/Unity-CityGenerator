using UnityEngine;

namespace CityGenerator.Editor
{
    /// <summary>
    /// Builds the AudioSources described by <see cref="AudioSettings"/>: one 2D, looping
    /// AudioSource per <see cref="AmbienceClipEntry"/> as a direct child of cityRoot, and one 3D,
    /// looping AudioSource per <see cref="PlazaAudioClipEntry"/> inside a plaza block's own
    /// group. Both hang off geometry that CityGeneratorContentAssembler/CityGeneratorPlazaBuilder
    /// already rebuild wholesale on every Build/Re-Build, so no reconciliation-by-name logic is
    /// needed here (unlike the Directional Light's Day/Night Cycle).
    /// </summary>
    internal static class CityGeneratorAudioBuilder
    {
        public static void BuildAmbience(Transform cityRoot, AmbienceSettings ambience)
        {
            if (!ambience.enabled)
                return;

            for (int i = 0; i < ambience.clips.Count; i++)
            {
                AmbienceClipEntry entry = ambience.clips[i];
                if (entry.clip == null)
                    continue;

                var instance = new GameObject($"Ambience_{i}");
                instance.transform.SetParent(cityRoot, worldPositionStays: false);

                var source = instance.AddComponent<AudioSource>();
                source.clip = entry.clip;
                source.volume = entry.volume;
                source.spatialBlend = 0f;
                source.loop = true;
                source.playOnAwake = true;
            }
        }

        public static void BuildPlazaAudio(Transform blockGroup, Vector3 center, PlazaAudioSettings plazaAudio)
        {
            if (!plazaAudio.enabled)
                return;

            for (int i = 0; i < plazaAudio.clips.Count; i++)
            {
                PlazaAudioClipEntry entry = plazaAudio.clips[i];
                if (entry.clip == null)
                    continue;

                var instance = new GameObject($"PlazaAudio_{i}");
                instance.transform.SetParent(blockGroup, worldPositionStays: false);
                instance.transform.position = center;

                var source = instance.AddComponent<AudioSource>();
                source.clip = entry.clip;
                source.volume = entry.volume;
                source.spatialBlend = 1f;
                source.loop = true;
                source.playOnAwake = true;
                source.minDistance = entry.minDistance;
                source.maxDistance = entry.maxDistance;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
            }
        }
    }
}
