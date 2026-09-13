#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace ValeMesozoico
{
    // Editor export only. The game clock and main audio mix advance together;
    // raw frames go straight to ffmpeg instead of filling disk with PNGs.
    [DefaultExecutionOrder(10000)]
    internal sealed class RideVideoCapture : MonoBehaviour
    {
        private const string RequestKey = "ValeMesozoico.FullRideVideo";
        private const int Width = 1920, Height = 1080, Fps = 30;
        private RideController _ride;
        private PteranodonDropSequence _encounter;
        private RideDebugTimeline _timeline;
        private Camera _source, _camera;
        private RenderTexture _target;
        private Texture2D _pixels;
        private Process _encoder;
        private Task<string> _encoderErrors;
        private FileStream _audio;
        private NativeArray<float> _audioSamples;
        private float[] _floatBuffer;
        private byte[] _audioBytes, _frameBytes;
        private string _directory;
        private int _frames, _arrivalFrames, _limitSeconds, _channels, _sampleRate, _previousCaptureRate;
        private long _sampleFrames;
        private float _previousTimeScale, _peak;
        private bool _recording, _audioStarted, _finishing, _closedInput, _savedSettings, _previousBackground;

        [MenuItem("Vale Mesozoico/Preview/Record Full Ride Video")]
        private static void RecordFullRide() => Request(0);

        [MenuItem("Vale Mesozoico/Preview/Test Video Capture (8 seconds)")]
        private static void TestCapture() => Request(8);

        private static void Request(int limit)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Ride Video] Stop Play Mode before starting a recording from the station.");
                return;
            }
            SessionState.SetInt(RequestKey, limit + 1);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            int request = SessionState.GetInt(RequestKey, 0);
            if (request == 0) return;
            SessionState.SetInt(RequestKey, 0);
            var capture = new GameObject("Full Ride Video Capture").AddComponent<RideVideoCapture>();
            capture._limitSeconds = request - 1;
        }

        private IEnumerator Start()
        {
            while ((_ride = FindFirstObjectByType<RideController>()) == null) yield return null;
            _ride.enabled = false;
            TrackRuntimeQa qa = FindFirstObjectByType<TrackRuntimeQa>();
            while (qa != null && !qa.InitialValidationComplete) yield return null;
            while (FindFirstObjectByType<RideDebugTimelineQa>() != null) yield return null;
            try
            {
                _directory = Path.GetFullPath(_limitSeconds > 0 ? "Builds/Videos/Smoke" : "Builds/Videos/FullRide");
                Directory.CreateDirectory(_directory);
                _source = Camera.main;
                _encounter = FindFirstObjectByType<PteranodonDropSequence>();
                if (_source == null || _encounter == null || !_encounter.Ready)
                    throw new InvalidOperationException("Ride camera or approved Pteranodon unavailable.");
                _timeline = _ride.GetComponent<RideDebugTimeline>();
                if (_timeline != null) _timeline.enabled = false;
                _previousCaptureRate = Time.captureFramerate;
                _previousTimeScale = Time.timeScale;
                _previousBackground = Application.runInBackground;
                _savedSettings = true;
                Time.captureFramerate = Fps;
                Time.timeScale = 1f;
                Application.runInBackground = true;
                _ride.SetDeveloperPlaybackRate(1f);

                _camera = new GameObject("Ride Export Camera").AddComponent<Camera>();
                _camera.CopyFrom(_source);
                _camera.enabled = false;
                _camera.aspect = Width / (float)Height;
                _camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
                _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                _target.Create();
                _camera.targetTexture = _target;
                _pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                _frameBytes = new byte[Width * Height * 3];

                string executable = File.Exists("/opt/homebrew/bin/ffmpeg") ? "/opt/homebrew/bin/ffmpeg" : "ffmpeg";
                _encoder = Process.Start(new ProcessStartInfo(executable,
                    $"-hide_banner -loglevel warning -y -f rawvideo -pixel_format rgb24 -video_size {Width}x{Height} "
                    + $"-framerate {Fps} -i pipe:0 -vf vflip -an -c:v libx264 -preset veryfast -crf 18 "
                    + $"-pix_fmt yuv420p -movflags +faststart \"{Path.Combine(_directory, "video.mp4")}\"")
                    { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true });
                if (_encoder == null) throw new IOException("ffmpeg did not start.");
                _encoderErrors = _encoder.StandardError.ReadToEndAsync();
                _sampleRate = AudioSettings.outputSampleRate;
                _channels = AudioSettings.speakerMode switch
                {
                    AudioSpeakerMode.Mono => 1, AudioSpeakerMode.Quad => 4,
                    AudioSpeakerMode.Surround => 5, AudioSpeakerMode.Mode5point1 => 6,
                    AudioSpeakerMode.Mode7point1 => 8, _ => 2
                };
                _audio = File.Create(Path.Combine(_directory, "audio.f32le"));
                _audioStarted = AudioRenderer.Start();
                if (!_audioStarted) throw new InvalidOperationException("Audio engine is already recording.");
                _recording = true;
                _ride.enabled = true;
                WriteStatus("RECORDING");
            }
            catch (Exception error) { Fail(error); }
        }

        private void LateUpdate()
        {
            if (!_recording) return;
            try
            {
                _camera.transform.SetPositionAndRotation(_source.transform.position, _source.transform.rotation);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    _camera.Render();
                    RenderTexture.active = _target;
                    _pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                    _pixels.GetRawTextureData<byte>().CopyTo(_frameBytes);
                }
                finally { RenderTexture.active = previous; }
                _encoder.StandardInput.BaseStream.Write(_frameBytes, 0, _frameBytes.Length);
                CaptureAudio();
                _frames++;
                if (_frames == Fps * 5) File.WriteAllBytes(Path.Combine(_directory, "preview.png"), _pixels.EncodeToPNG());
                if (_frames % (Fps * 10) == 0) WriteStatus("RECORDING");

                if (_ride.RideProgress > .9999f && _ride.RideSpeed <= .15f) _arrivalFrames++;
                else _arrivalFrames = 0;
                if ((_limitSeconds > 0 && _frames >= Fps * _limitSeconds) || _arrivalFrames >= Fps * 4.3f)
                {
                    _recording = false;
                    StartCoroutine(Finish());
                }
                else if (_frames > Fps * 1200) throw new TimeoutException("Ride did not arrive within 20 minutes of captured time.");
            }
            catch (Exception error) { Fail(error); }
        }

        private void CaptureAudio()
        {
            int frames = AudioRenderer.GetSampleCountForCaptureFrame();
            int count = frames * _channels;
            if (count <= 0) throw new InvalidOperationException("No audio samples available for capture frame.");
            if (!_audioSamples.IsCreated || _audioSamples.Length != count)
            {
                if (_audioSamples.IsCreated) _audioSamples.Dispose();
                _audioSamples = new NativeArray<float>(count, Allocator.Persistent);
                _floatBuffer = new float[count];
                _audioBytes = new byte[count * sizeof(float)];
            }
            if (!AudioRenderer.Render(_audioSamples)) throw new IOException("AudioRenderer failed to capture the main mix.");
            _audioSamples.CopyTo(_floatBuffer);
            foreach (float sample in _floatBuffer) _peak = Mathf.Max(_peak, Mathf.Abs(sample));
            Buffer.BlockCopy(_floatBuffer, 0, _audioBytes, 0, _audioBytes.Length);
            _audio.Write(_audioBytes, 0, _audioBytes.Length);
            _sampleFrames += frames;
        }

        private IEnumerator Finish()
        {
            _finishing = true;
            _ride.enabled = false;
            CloseCapture();
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!_encoder.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            if (!_encoder.HasExited) { Fail(new TimeoutException("Video encoder did not finish.")); yield break; }
            string errors = _encoderErrors.IsCompleted ? _encoderErrors.Result : "";
            File.WriteAllText(Path.Combine(_directory, "ffmpeg.log"), errors);
            if (_encoder.ExitCode != 0) { Fail(new IOException("ffmpeg: " + errors)); yield break; }
            WriteStatus("CAPTURED");
            Debug.Log($"[Ride Video] CAPTURED | {_frames / (float)Fps:F2}s | {_directory}");
            EditorApplication.isPlaying = false;
        }

        private void WriteStatus(string state, string error = "")
        {
            if (string.IsNullOrEmpty(_directory)) return;
            File.WriteAllText(Path.Combine(_directory, "capture.json"), JsonUtility.ToJson(new Status
            {
                state = state, frames = _frames, fps = Fps, width = Width, height = Height,
                seconds = _frames / (float)Fps, progress = _ride != null ? _ride.RideProgress : 0f,
                phase = _encounter != null ? _encounter.CurrentPhase.ToString() : "",
                audioRate = _sampleRate, audioChannels = _channels, audioSampleFrames = _sampleFrames,
                audioPeak = _peak, error = error
            }, true));
        }

        private void Fail(Exception error)
        {
            _recording = false;
            Debug.LogException(error);
            WriteStatus("FAILED", error.Message);
            CloseCapture();
            EditorApplication.isPlaying = false;
        }

        private void CloseCapture()
        {
            if (_audioStarted) { AudioRenderer.Stop(); _audioStarted = false; }
            _audio?.Dispose(); _audio = null;
            if (_encoder != null && !_closedInput)
            { _encoder.StandardInput.Close(); _closedInput = true; }
            if (_savedSettings)
            {
                Time.captureFramerate = _previousCaptureRate;
                Time.timeScale = _previousTimeScale;
                Application.runInBackground = _previousBackground;
                _savedSettings = false;
            }
        }

        private void OnDestroy()
        {
            if (_recording && !_finishing) WriteStatus("INTERRUPTED");
            CloseCapture();
            if (_audioSamples.IsCreated) _audioSamples.Dispose();
            if (_camera != null) DestroyImmediate(_camera.gameObject);
            if (_target != null) { _target.Release(); DestroyImmediate(_target); }
            if (_pixels != null) DestroyImmediate(_pixels);
            _encoder?.Dispose();
            if (_ride != null) _ride.enabled = true;
            if (_timeline != null) _timeline.enabled = true;
        }

        [Serializable]
        private sealed class Status
        {
            public string state, phase, error;
            public int frames, fps, width, height, audioRate, audioChannels;
            public long audioSampleFrames;
            public float seconds, progress, audioPeak;
        }
    }
}
#endif
