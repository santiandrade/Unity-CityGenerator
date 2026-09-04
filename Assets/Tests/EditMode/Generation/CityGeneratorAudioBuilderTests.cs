using System.Collections.Generic;
using CityGenerator.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityGenerator.Tests.EditMode.Generation
{
    /// <summary>
    /// SPEC 09: Ambience (2D, cityRoot-parented) and Plaza (3D, one per plaza block) AudioSources,
    /// exercised through the full pipeline (mirroring <see cref="CustomPlaceBuilderTests"/>'s own
    /// convention) plus the blocking validation rules CityGeneratorValidator adds for both cards.
    /// </summary>
    internal class CityGeneratorAudioBuilderTests
    {
        private readonly List<GameObject> spawnedRoots = new();
        private float nextOffset = 40000f;

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in spawnedRoots)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
            }
            spawnedRoots.Clear();
        }

        private Transform CreateOffsetCityRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(nextOffset, 0f, nextOffset);
            nextOffset += 5000f;
            spawnedRoots.Add(root);
            return root.transform;
        }

        [Test]
        public void DefaultAssets_ApplyTo_Populates_Default_Ambience_And_Plaza_Clips()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);

            Assert.IsTrue(settings.audio.ambience.enabled);
            Assert.AreEqual(1, settings.audio.ambience.clips.Count);
            Assert.IsNotNull(settings.audio.ambience.clips[0].clip, "Default ambience clip must load.");
            Assert.AreEqual(1f, settings.audio.ambience.clips[0].volume);

            Assert.IsTrue(settings.audio.plazaAudio.enabled);
            Assert.AreEqual(2, settings.audio.plazaAudio.clips.Count);
            Assert.IsNotNull(settings.audio.plazaAudio.clips[0].clip, "Default plaza clip must load.");
            Assert.AreEqual(1f, settings.audio.plazaAudio.clips[0].volume);
            Assert.AreEqual(4f, settings.audio.plazaAudio.clips[0].minDistance);
            Assert.AreEqual(20f, settings.audio.plazaAudio.clips[0].maxDistance);
            Assert.IsNotNull(settings.audio.plazaAudio.clips[1].clip, "Second default plaza clip must load.");
            Assert.AreEqual(1f, settings.audio.plazaAudio.clips[1].volume);
            Assert.AreEqual(20f, settings.audio.plazaAudio.clips[1].minDistance);
            Assert.AreEqual(50f, settings.audio.plazaAudio.clips[1].maxDistance);

            bool valid = CityGeneratorValidator.Validate(settings, out List<string> errors);
            Assert.IsTrue(valid, "Default settings must not be blocked by the new Audio validation rules. Errors: " + string.Join("; ", errors));
        }

        [Test]
        public void Ambience_And_PlazaAudio_Are_Built_With_Expected_Properties()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 3;
            settings.general.gridHeight = 3;
            settings.general.plazaCells = new List<Vector2Int> { new Vector2Int(1, 1) };
            settings.general.useCustomSeed = true;
            settings.general.seed = 42;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Packages/com.santiandrade.citygenerator/DefaultAssets/Audio/city-ambiance.wav");
            Assert.IsNotNull(clip, "Default ambience clip must load.");

            settings.audio.plazaAudio.enabled = true;
            settings.audio.plazaAudio.clips = new List<PlazaAudioClipEntry>
            {
                new PlazaAudioClipEntry { clip = clip, volume = 0.7f, minDistance = 10f, maxDistance = 40f },
            };

            Transform root = CreateOffsetCityRoot("AudioBuilderRoot");
            CityGeneratorContentAssembler.Assemble(settings, root);

            Transform ambience = root.Find("Ambience_0");
            Assert.IsNotNull(ambience, "Ambience_0 must be a direct child of cityRoot.");
            Assert.AreEqual(root, ambience.parent);
            var ambienceSource = ambience.GetComponent<AudioSource>();
            Assert.IsNotNull(ambienceSource);
            Assert.AreEqual(clip, ambienceSource.clip);
            Assert.AreEqual(1f, ambienceSource.volume);
            Assert.AreEqual(0f, ambienceSource.spatialBlend);
            Assert.IsTrue(ambienceSource.loop);
            Assert.IsTrue(ambienceSource.playOnAwake);

            Transform plazaGroup = root.Find("Plaza/Plaza_1_1");
            Assert.IsNotNull(plazaGroup, "Plaza_1_1 group must exist for the configured plaza cell.");
            Transform plazaAudio = plazaGroup.Find("PlazaAudio_0");
            Assert.IsNotNull(plazaAudio, "PlazaAudio_0 must exist inside the plaza block group.");
            var plazaSource = plazaAudio.GetComponent<AudioSource>();
            Assert.IsNotNull(plazaSource);
            Assert.AreEqual(clip, plazaSource.clip);
            Assert.AreEqual(0.7f, plazaSource.volume);
            Assert.AreEqual(1f, plazaSource.spatialBlend);
            Assert.IsTrue(plazaSource.loop);
            Assert.AreEqual(10f, plazaSource.minDistance);
            Assert.AreEqual(40f, plazaSource.maxDistance);
            Assert.AreEqual(AudioRolloffMode.Logarithmic, plazaSource.rolloffMode);
            Assert.AreEqual(plazaGroup.position, plazaAudio.position, "Plaza audio must sit at the block's center.");
        }

        [Test]
        public void PlazaAudio_Enabled_But_No_PlazaCells_Builds_Without_Error_And_No_AudioSources()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);
            settings.general.gridWidth = 2;
            settings.general.gridHeight = 2;
            settings.general.plazaCells = new List<Vector2Int>();
            settings.general.useCustomSeed = true;
            settings.general.seed = 1;

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Packages/com.santiandrade.citygenerator/DefaultAssets/Audio/city-ambiance.wav");
            settings.audio.plazaAudio.enabled = true;
            settings.audio.plazaAudio.clips = new List<PlazaAudioClipEntry>
            {
                new PlazaAudioClipEntry { clip = clip, volume = 1f, minDistance = 10f, maxDistance = 40f },
            };

            Transform root = CreateOffsetCityRoot("AudioBuilderNoPlazaRoot");
            Assert.DoesNotThrow(() => CityGeneratorContentAssembler.Assemble(settings, root));

            Transform plaza = root.Find("Plaza");
            Assert.IsNotNull(plaza);
            Assert.AreEqual(0, plaza.childCount, "No plaza blocks -> no plaza audio group at all.");
        }

        [Test]
        public void Validator_Blocks_Enabled_Audio_With_Empty_Or_Missing_Clips()
        {
            var settings = new CityGeneratorSettings();
            CityGeneratorDefaultAssets.ApplyTo(settings);

            settings.audio.ambience.enabled = true;
            settings.audio.ambience.clips = new List<AmbienceClipEntry>();
            settings.audio.plazaAudio.enabled = false;

            bool valid = CityGeneratorValidator.Validate(settings, out List<string> errors);
            Assert.IsFalse(valid);
            Assert.IsTrue(errors.Exists(e => e.Contains("Ambience")));

            settings.audio.ambience.clips = new List<AmbienceClipEntry> { new AmbienceClipEntry { clip = null, volume = 1f } };
            valid = CityGeneratorValidator.Validate(settings, out errors);
            Assert.IsFalse(valid);
            Assert.IsTrue(errors.Exists(e => e.Contains("Ambience") && e.Contains("clip")));

            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Packages/com.santiandrade.citygenerator/DefaultAssets/Audio/city-ambiance.wav");
            settings.audio.ambience.clips = new List<AmbienceClipEntry> { new AmbienceClipEntry { clip = clip, volume = 1f } };
            valid = CityGeneratorValidator.Validate(settings, out errors);
            Assert.IsTrue(valid, "Ambience with a valid clip must not block generation. Errors: " + string.Join("; ", errors));

            settings.audio.plazaAudio.enabled = true;
            settings.audio.plazaAudio.clips = new List<PlazaAudioClipEntry>();
            valid = CityGeneratorValidator.Validate(settings, out errors);
            Assert.IsFalse(valid);
            Assert.IsTrue(errors.Exists(e => e.Contains("Plazas")));
        }
    }
}
