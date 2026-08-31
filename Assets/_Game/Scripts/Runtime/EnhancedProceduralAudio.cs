using System;
using UnityEngine;

namespace ValeMesozoico
{
    internal static class EnhancedProceduralAudio
    {
        private const int SampleRate = 22050;
        private const float Tau = Mathf.PI * 2f;

        internal static AudioClip CreateJungleAmbience()
        {
            const float duration = 8f;
            const float crossfadeDuration = 0.35f;
            const int channels = 2;
            int frames = Mathf.RoundToInt(duration * SampleRate);
            int crossfadeFrames = Mathf.RoundToInt(crossfadeDuration * SampleRate);
            int generatedFrames = frames + crossfadeFrames;
            float[] generated = new float[generatedFrames * channels];

            NoiseGenerator centerNoise = new(0x13579B);
            float windSlow = 0f;
            float windBody = 0f;
            float dcLeft = 0f;
            float dcRight = 0f;

            for (int frame = 0; frame < generatedFrames; frame++)
            {
                float time = frame / (float)SampleRate;
                float center = centerNoise.NextSigned();
                windSlow += (center - windSlow) * 0.0018f;
                windBody += (center - windBody) * 0.018f;

                float gust = 0.72f
                    + Mathf.Sin(Tau * time / 5.3f + 0.4f) * 0.18f
                    + Mathf.Sin(Tau * time / 2.1f + 1.7f) * 0.08f;
                float canopy = windSlow * 2.6f + (windBody - windSlow) * 1.25f;
                float leafSwayLeft = Mathf.Sin(Tau * time * 0.37f + 0.6f)
                    * Mathf.Sin(Tau * time * 0.83f) * 0.012f;
                float leafSwayRight = Mathf.Sin(Tau * time * 0.31f + 1.8f)
                    * Mathf.Sin(Tau * time * 0.71f + 0.4f) * 0.012f;

                float cicadaGate = Mathf.Pow(
                    Mathf.Max(0f, Mathf.Sin(Tau * time * 0.31f + 0.9f)),
                    12f);
                float cicadaLeft = Mathf.Sin(
                    Tau * (2380f * time + Mathf.Sin(Tau * time * 4.1f) * 0.035f));
                float cicadaRight = Mathf.Sin(
                    Tau * (2710f * time + Mathf.Sin(Tau * time * 3.7f + 1.1f) * 0.03f));

                float birdEnvelope = EventEnvelope(time, 2.35f, 0.58f)
                    + EventEnvelope(time, 6.15f, 0.44f) * 0.72f;
                float bird = Mathf.Sin(Tau * (880f * time + 28f * time * time)) * birdEnvelope;

                float leftSample = canopy * gust * 0.34f
                    + leafSwayLeft
                    + cicadaLeft * cicadaGate * 0.018f
                    + bird * 0.022f;
                float rightSample = canopy * gust * 0.32f
                    + leafSwayRight
                    + cicadaRight * cicadaGate * 0.016f
                    + bird * 0.014f;

                dcLeft += (leftSample - dcLeft) * 0.00035f;
                dcRight += (rightSample - dcRight) * 0.00035f;
                int sample = frame * channels;
                generated[sample] = SoftLimit(leftSample - dcLeft);
                generated[sample + 1] = SoftLimit(rightSample - dcRight);
            }

            float[] samples = MakeSeamless(generated, frames, channels, crossfadeFrames);
            NormalizePeak(samples, 0.62f);
            return CreateClip("Enhanced Jungle Ambience", samples, channels);
        }

        internal static AudioClip CreateTrackLoop()
        {
            const float duration = 4f;
            const float crossfadeDuration = 0.16f;
            int frames = Mathf.RoundToInt(duration * SampleRate);
            int crossfadeFrames = Mathf.RoundToInt(crossfadeDuration * SampleRate);
            int generatedFrames = frames + crossfadeFrames;
            float[] generated = new float[generatedFrames];

            float dc = 0f;

            for (int frame = 0; frame < generatedFrames; frame++)
            {
                float time = frame / (float)SampleRate;
                float speedPulse = 0.78f + Mathf.Sin(Tau * time * 0.5f) * 0.12f;
                float wheelTone = Mathf.Sin(Tau * 38f * time) * 0.15f
                    + Mathf.Sin(Tau * 57f * time + 0.8f) * 0.085f
                    + Mathf.Sin(Tau * 83f * time + 1.7f) * 0.035f;
                float bearingTone = Mathf.Sin(
                    Tau * (116f * time + Mathf.Sin(Tau * time * 1.3f) * 0.025f)) * 0.022f;
                float rumble = (wheelTone + bearingTone) * speedPulse;

                float jointAge = Mathf.Repeat(time, 0.28f);
                float railJoint = 0f;
                if (jointAge < 0.034f)
                {
                    float envelope = (1f - Mathf.Exp(-jointAge * 320f))
                        * Mathf.Exp(-jointAge * 105f);
                    railJoint = envelope
                        * (Mathf.Sin(Tau * 520f * jointAge) * 0.10f
                            + Mathf.Sin(Tau * 910f * jointAge + 0.4f) * 0.045f);
                }

                float value = rumble * 0.76f + railJoint;
                dc += (value - dc) * 0.00045f;
                generated[frame] = SoftLimit(value - dc);
            }

            float[] samples = MakeSeamless(generated, frames, 1, crossfadeFrames);
            NormalizePeak(samples, 0.66f);
            return CreateClip("Enhanced Wheel Rail Roll", samples, 1);
        }

        internal static AudioClip CreateLiftChainLoop()
        {
            const float duration = 2.4f;
            const float crossfadeDuration = 0.12f;
            const float tickInterval = 0.16f;
            const float wheelKnockInterval = 0.32f;
            int frames = Mathf.RoundToInt(duration * SampleRate);
            int crossfadeFrames = Mathf.RoundToInt(crossfadeDuration * SampleRate);
            int generatedFrames = frames + crossfadeFrames;
            float[] generated = new float[generatedFrames];
            NoiseGenerator mechanismNoise = new(0xC4A17);
            float chainAir = 0f;
            float chainBody = 0f;
            float dc = 0f;

            for (int frame = 0; frame < generatedFrames; frame++)
            {
                float time = frame / (float)SampleRate;
                float noise = mechanismNoise.NextSigned();
                chainAir += (noise - chainAir) * 0.24f;
                chainBody += (noise - chainBody) * 0.018f;
                float metallicNoise = chainAir - chainBody;

                float warpedTime = time
                    + Mathf.Sin(Tau * time / duration) * 0.006f
                    + Mathf.Sin(Tau * time * 3f / duration + 0.4f) * 0.003f;
                float primaryAge = Mathf.Repeat(warpedTime, tickInterval);
                float secondaryAge = Mathf.Repeat(warpedTime - 0.031f, tickInterval);
                int linkIndex = Mathf.FloorToInt(warpedTime / tickInterval);
                float linkVariation = 0.84f + Mathf.Repeat(linkIndex * 0.618f, 1f) * 0.22f;
                float primary = MechanicalTick(primaryAge, linkVariation, metallicNoise);
                float secondary = MechanicalTick(secondaryAge, 0.46f, -metallicNoise);

                float wheelAge = Mathf.Repeat(time - 0.075f, wheelKnockInterval);
                float wheelKnock = 0f;
                if (wheelAge < 0.052f)
                {
                    float wheelEnvelope = (1f - Mathf.Exp(-wheelAge * 420f))
                        * Mathf.Exp(-wheelAge * 74f);
                    wheelKnock = wheelEnvelope
                        * (Mathf.Sin(Tau * 390f * wheelAge) * 0.075f
                            + metallicNoise * 0.045f);
                }

                float chainDrag = chainBody * 0.11f
                    + metallicNoise * 0.026f
                    + Mathf.Sin(Tau * 185f * time + 0.3f) * 0.006f
                    + Mathf.Sin(Tau * 370f * time + 0.7f) * 0.003f;
                float value = primary + secondary + wheelKnock + chainDrag;
                dc += (value - dc) * 0.00055f;
                generated[frame] = SoftLimit(value - dc);
            }

            float[] samples = MakeSeamless(generated, frames, 1, crossfadeFrames);
            NormalizePeak(samples, 0.78f);
            return CreateClip("Lift Chain Tec Tec Loop", samples, 1);
        }

        private static float MechanicalTick(float age, float intensity, float noise)
        {
            if (age >= 0.058f)
            {
                return 0f;
            }

            float attack = 1f - Mathf.Exp(-age * 520f);
            float decay = Mathf.Exp(-age * 82f);
            float envelope = attack * decay * intensity;
            float strike = Mathf.Sin(Tau * 1380f * age) * 0.34f
                + Mathf.Sin(Tau * 2260f * age + 0.24f) * 0.21f
                + Mathf.Sin(Tau * 3540f * age + 0.67f) * 0.09f
                + Mathf.Sin(Tau * 470f * age + 0.9f) * 0.055f;
            return envelope * (strike + noise * 0.17f);
        }

        internal static AudioClip CreateWaterAmbience()
        {
            const float duration = 6f;
            const float crossfadeDuration = 0.32f;
            int frames = Mathf.RoundToInt(duration * SampleRate);
            int crossfadeFrames = Mathf.RoundToInt(crossfadeDuration * SampleRate);
            int generatedFrames = frames + crossfadeFrames;
            float[] generated = new float[generatedFrames];

            NoiseGenerator waterNoise = new(0x0A7E42);
            float slow = 0f;
            float body = 0f;
            float detail = 0f;
            float dc = 0f;

            for (int frame = 0; frame < generatedFrames; frame++)
            {
                float time = frame / (float)SampleRate;
                float noise = waterNoise.NextSigned();
                slow += (noise - slow) * 0.0032f;
                body += (noise - body) * 0.028f;
                detail += (noise - detail) * 0.105f;

                float surge = 0.62f
                    + Mathf.Sin(Tau * time / 3f - 0.7f) * 0.2f
                    + Mathf.Sin(Tau * time / 2f + 1.1f) * 0.08f;
                float wash = (body - slow) * 1.7f + slow * 1.35f + (detail - body) * 0.22f;

                float bubbleAge = Mathf.Repeat(time - 0.9f, 2f);
                float bubble = 0f;
                if (bubbleAge < 0.13f)
                {
                    float envelope = (1f - Mathf.Exp(-bubbleAge * 120f))
                        * Mathf.Exp(-bubbleAge * 32f);
                    bubble = Mathf.Sin(Tau * (460f - bubbleAge * 850f) * bubbleAge)
                        * envelope
                        * 0.055f;
                }

                float value = wash * surge + bubble;
                dc += (value - dc) * 0.0004f;
                generated[frame] = SoftLimit(value - dc);
            }

            float[] samples = MakeSeamless(generated, frames, 1, crossfadeFrames);
            NormalizePeak(samples, 0.58f);
            return CreateClip("Enhanced Lagoon Ambience", samples, 1);
        }

        internal static DinosaurAudioEmitter AttachDinosaurCall(GameObject target, string species, int seed)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            DinosaurProfile profile = GetDinosaurProfile(species);
            AudioClip clip = CreateDinosaurCall(species, seed, profile);
            DinosaurAudioEmitter emitter = target.GetComponent<DinosaurAudioEmitter>();
            if (emitter == null)
            {
                emitter = target.AddComponent<DinosaurAudioEmitter>();
            }

            emitter.Initialize(clip, MixSeed(seed, species), profile.MinimumSilence, profile.MaximumSilence);
            return emitter;
        }

        private static AudioClip CreateDinosaurCall(string species, int seed, DinosaurProfile profile)
        {
            int frames = Mathf.RoundToInt(profile.Duration * SampleRate);
            float[] samples = new float[frames];
            NoiseGenerator noise = new(MixSeed(seed, species));
            float phase = noise.Next01() * Tau;
            float secondPhase = noise.Next01() * Tau;
            float wobblePhase = noise.Next01() * Tau;
            float breathBody = 0f;
            float breathDetail = 0f;
            float dc = 0f;
            float detune = Mathf.Lerp(1.008f, 1.021f, noise.Next01());

            for (int frame = 0; frame < frames; frame++)
            {
                float time = frame / (float)SampleRate;
                float normalized = frame / (float)Mathf.Max(1, frames - 1);
                float sweep = Mathf.SmoothStep(0f, 1f, normalized);
                float warble = 1f
                    + Mathf.Sin(Tau * time * (2.1f + profile.Roughness * 2.2f) + wobblePhase) * 0.035f
                    + Mathf.Sin(Tau * time * 11f + wobblePhase * 0.7f) * profile.Roughness * 0.018f;
                float frequency = Mathf.Max(24f, Mathf.Lerp(profile.StartFrequency, profile.EndFrequency, sweep) * warble);
                phase += Tau * frequency / SampleRate;
                secondPhase += Tau * frequency * detune / SampleRate;

                float random = noise.NextSigned();
                breathBody += (random - breathBody) * 0.014f;
                breathDetail += (random - breathDetail) * profile.NoiseResponse;

                float attack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / profile.Attack));
                float release = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01((profile.Duration - time) / profile.Release));
                float envelope = attack * release;
                float throatPulse = 0.72f
                    + Mathf.Sin(Tau * time * (9f + profile.Roughness * 15f) + wobblePhase) * 0.28f;
                float voice = Mathf.Sin(phase) * 0.58f
                    + Mathf.Sin(phase * 2f + 0.25f) * 0.25f
                    + Mathf.Sin(phase * 3f + 1.2f) * 0.1f
                    + Mathf.Sin(secondPhase) * 0.16f;
                float breath = (breathDetail - breathBody) * profile.BreathLevel
                    + breathBody * profile.RumbleLevel;
                float value = envelope * (voice * throatPulse * profile.VoiceLevel + breath);
                dc += (value - dc) * 0.0005f;
                samples[frame] = SoftLimit(value - dc);
            }

            NormalizePeak(samples, 0.82f);
            string clipName = string.IsNullOrWhiteSpace(species)
                ? "Dinosaur Call"
                : $"Dinosaur Call - {species}";
            return CreateClip(clipName, samples, 1);
        }

        private static DinosaurProfile GetDinosaurProfile(string species)
        {
            if (Contains(species, "tyrann") || Contains(species, "trex") || Contains(species, "t-rex"))
            {
                return new DinosaurProfile(2.35f, 72f, 39f, 0.42f, 0.11f, 0.72f, 0.34f, 0.08f, 0.46f, 7f, 14f);
            }

            if (Contains(species, "sauropod")
                || Contains(species, "brachio")
                || Contains(species, "bronto")
                || Contains(species, "apato"))
            {
                return new DinosaurProfile(3.1f, 48f, 28f, 0.23f, 0.075f, 0.78f, 0.24f, 0.12f, 0.7f, 10f, 19f);
            }

            if (Contains(species, "raptor") || Contains(species, "velo"))
            {
                return new DinosaurProfile(1.25f, 285f, 138f, 0.38f, 0.16f, 0.68f, 0.42f, 0.035f, 0.24f, 5f, 11f);
            }

            if (Contains(species, "ptero"))
            {
                return new DinosaurProfile(1.05f, 520f, 245f, 0.3f, 0.2f, 0.62f, 0.36f, 0.025f, 0.2f, 6f, 13f);
            }

            return new DinosaurProfile(1.75f, 118f, 69f, 0.3f, 0.12f, 0.72f, 0.32f, 0.07f, 0.36f, 7f, 15f);
        }

        private static bool Contains(string value, string term)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float EventEnvelope(float time, float start, float duration)
        {
            float age = time - start;
            if (age <= 0f || age >= duration)
            {
                return 0f;
            }

            float attack = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / 0.08f));
            float release = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((duration - age) / 0.22f));
            return attack * release;
        }

        private static float[] MakeSeamless(float[] generated, int frames, int channels, int crossfadeFrames)
        {
            crossfadeFrames = Mathf.Clamp(crossfadeFrames, 1, frames - 1);
            float[] result = new float[frames * channels];
            Array.Copy(generated, result, result.Length);

            for (int frame = 0; frame < crossfadeFrames; frame++)
            {
                float amount = Mathf.SmoothStep(
                    0f,
                    1f,
                    (frame + 1f) / (crossfadeFrames + 1f));
                int head = frame * channels;
                int continuation = (frames + frame) * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    result[head + channel] = Mathf.Lerp(
                        generated[continuation + channel],
                        generated[head + channel],
                        amount);
                }
            }

            return result;
        }

        private static void NormalizePeak(float[] samples, float targetPeak)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }

            if (peak < 0.0001f)
            {
                return;
            }

            float scale = Mathf.Min(targetPeak, 0.92f) / peak;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] *= scale;
            }
        }

        private static float SoftLimit(float value)
        {
            return value / (1f + Mathf.Abs(value) * 0.45f);
        }

        private static AudioClip CreateClip(string name, float[] samples, int channels)
        {
            int frames = samples.Length / channels;
            AudioClip clip = AudioClip.Create(name, frames, channels, SampleRate, false);
            if (!clip.SetData(samples, 0))
            {
                UnityEngine.Object.Destroy(clip);
                throw new InvalidOperationException($"Não foi possível criar o clip procedural '{name}'.");
            }

            return clip;
        }

        private static int MixSeed(int seed, string value)
        {
            unchecked
            {
                uint hash = (uint)seed ^ 2166136261u;
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= char.ToUpperInvariant(value[i]);
                        hash *= 16777619u;
                    }
                }

                return (int)(hash == 0u ? 0x6D2B79F5u : hash);
            }
        }

        private struct NoiseGenerator
        {
            private uint _state;

            public NoiseGenerator(int seed)
            {
                _state = unchecked((uint)seed);
                if (_state == 0u)
                {
                    _state = 0x6D2B79F5u;
                }
            }

            public float Next01()
            {
                uint value = NextUInt();
                return (value & 0x00FFFFFFu) / 16777216f;
            }

            public float NextSigned()
            {
                return Next01() * 2f - 1f;
            }

            private uint NextUInt()
            {
                uint value = _state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                _state = value;
                return value;
            }
        }

        private readonly struct DinosaurProfile
        {
            public DinosaurProfile(
                float duration,
                float startFrequency,
                float endFrequency,
                float roughness,
                float noiseResponse,
                float voiceLevel,
                float breathLevel,
                float rumbleLevel,
                float attackAndRelease,
                float minimumSilence,
                float maximumSilence)
            {
                Duration = duration;
                StartFrequency = startFrequency;
                EndFrequency = endFrequency;
                Roughness = roughness;
                NoiseResponse = noiseResponse;
                VoiceLevel = voiceLevel;
                BreathLevel = breathLevel;
                RumbleLevel = rumbleLevel;
                Attack = Mathf.Max(0.02f, attackAndRelease * 0.22f);
                Release = Mathf.Max(0.08f, attackAndRelease);
                MinimumSilence = minimumSilence;
                MaximumSilence = maximumSilence;
            }

            public float Duration { get; }
            public float StartFrequency { get; }
            public float EndFrequency { get; }
            public float Roughness { get; }
            public float NoiseResponse { get; }
            public float VoiceLevel { get; }
            public float BreathLevel { get; }
            public float RumbleLevel { get; }
            public float Attack { get; }
            public float Release { get; }
            public float MinimumSilence { get; }
            public float MaximumSilence { get; }
        }
    }

    internal sealed class DinosaurAudioEmitter : MonoBehaviour
    {
        private AudioSource _source;
        private AudioClip _clip;
        private uint _randomState;
        private float _minimumSilence;
        private float _maximumSilence;
        private float _nextCallTime;
        private bool _initialized;

        internal int PlayCount { get; private set; }
        internal float LastPlayedTime { get; private set; } = -1f;

        internal void Initialize(AudioClip clip, int seed, float minimumSilence, float maximumSilence)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
            }
            else
            {
                _source.Stop();
            }

            if (_clip != null && _clip != clip)
            {
                Destroy(_clip);
            }

            _clip = clip;
            _randomState = unchecked((uint)seed);
            if (_randomState == 0u)
            {
                _randomState = 0x6D2B79F5u;
            }

            _minimumSilence = Mathf.Max(0.5f, minimumSilence);
            _maximumSilence = Mathf.Max(_minimumSilence, maximumSilence);

            _source.clip = _clip;
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Logarithmic;
            _source.minDistance = 3.5f;
            _source.maxDistance = 58f;
            _source.dopplerLevel = 0.12f;
            _source.spread = 18f;
            _source.volume = 0.58f;
            _source.priority = 145;

            _initialized = true;
            _nextCallTime = Time.time + Mathf.Lerp(1.5f, 4f, Next01());
        }

        internal bool PlayNow(float volumeScale = 1f, float pitch = 1f)
        {
            if (!_initialized || _source == null || _clip == null)
            {
                return false;
            }

            _source.Stop();
            _source.volume = Mathf.Clamp01(0.58f * volumeScale);
            _source.pitch = Mathf.Clamp(pitch, 0.72f, 1.25f);
            _source.Play();
            PlayCount++;
            LastPlayedTime = Time.time;
            _nextCallTime = Time.time + _clip.length / Mathf.Max(0.01f, _source.pitch) + _maximumSilence;
            return true;
        }

        private void OnEnable()
        {
            if (_initialized)
            {
                _nextCallTime = Time.time + Mathf.Lerp(1f, 3f, Next01());
            }
        }

        private void OnDisable()
        {
            if (_source != null)
            {
                _source.Stop();
            }
        }

        private void Update()
        {
            if (!_initialized || _source == null || _clip == null || Time.time < _nextCallTime)
            {
                return;
            }

            _source.pitch = Mathf.Lerp(0.96f, 1.04f, Next01());
            _source.Play();
            PlayCount++;
            LastPlayedTime = Time.time;
            float playbackDuration = _clip.length / Mathf.Max(0.01f, _source.pitch);
            _nextCallTime = Time.time
                + playbackDuration
                + Mathf.Lerp(_minimumSilence, _maximumSilence, Next01());
        }

        private void OnDestroy()
        {
            if (_clip != null)
            {
                Destroy(_clip);
            }
        }

        private float Next01()
        {
            uint value = _randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _randomState = value;
            return (value & 0x00FFFFFFu) / 16777216f;
        }
    }
}
