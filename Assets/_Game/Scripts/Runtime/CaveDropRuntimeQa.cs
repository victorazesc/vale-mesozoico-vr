#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ValeMesozoico
{
    internal sealed class CaveDropRuntimeQa : MonoBehaviour
    {
        private const string RequestedKey = "ValeMesozoico.CaveDropQA";
        private static string Output => Path.GetFullPath("Builds/Previews/CaveDrop");

        [MenuItem("Vale Mesozoico/Preview/Cave Drop Encounter %&g")]
        private static void RunPreview()
        {
            if (EditorApplication.isPlaying)
            {
                if (FindFirstObjectByType<CaveDropRuntimeQa>() == null)
                    new GameObject("Cave Drop QA").AddComponent<CaveDropRuntimeQa>();
                return;
            }
            SessionState.SetBool(RequestedKey, true);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (!SessionState.GetBool(RequestedKey, false)) return;
            SessionState.SetBool(RequestedKey, false);
            new GameObject("Cave Drop QA").AddComponent<CaveDropRuntimeQa>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "qa.txt"), "Cave capture: RUNNING\n");
            bool pass = false;
            float previousTimeScale = Time.timeScale;
            RideDebugTimeline timeline = null;
            Time.timeScale = 1f;
            try
            {
                yield return new WaitForSecondsRealtime(.8f);
                TrackRuntimeQa trackQa = FindFirstObjectByType<TrackRuntimeQa>();
                while (trackQa != null && !trackQa.InitialValidationComplete) yield return null;
                while (FindFirstObjectByType<RideDebugTimelineQa>() != null) yield return null;
                RideController controller = FindFirstObjectByType<RideController>();
                PteranodonDropSequence sequence = FindFirstObjectByType<PteranodonDropSequence>();
                Require(controller != null && sequence != null && sequence.Ready, "controller / Pteranodon flight and feet");
                timeline = controller.GetComponent<RideDebugTimeline>();
                if (timeline != null) timeline.enabled = false;
                Require(EnvironmentRuntimeQa.ValidateMountainCaveForPreview(out string geometry), geometry);
                controller.DeveloperSeekToProgress(RideMotionProfile.CrestStart - .028f);
                controller.SetDeveloperPlaybackRate(2f);
                float deadline = Time.realtimeSinceStartup + 90f;
                float peakBrake = 0f, stopStep = controller.RideSpeed;
                bool brakeObserved = false;
                while (!sequence.IsHolding && Time.realtimeSinceStartup < deadline)
                {
                    if (controller.CrestBraking)
                    { brakeObserved = true; peakBrake = Mathf.Max(peakBrake, -controller.RideAcceleration); stopStep = controller.RideSpeed; }
                    yield return null;
                }
                Require(sequence.IsHolding && brakeObserved && peakBrake < 1.2f && stopStep < .05f,
                    $"crest brake: peak={peakBrake:F3}, final speed={stopStep:F4}");
                Vector3 crest = controller.transform.position;
                Quaternion crestRotation = controller.transform.rotation;
                Vector3 crestForward = Vector3.ProjectOnPlane(controller.transform.forward, Vector3.up).normalized;
                Require(crest.y > 65f && sequence.PanelsMeasured, "high crest and measured cart rim");
                File.AppendAllText(Path.Combine(Output, "qa.txt"), $"Brake: PASS {peakBrake:F3}m/s2, rim=PASS\n");
                controller.SetDeveloperPlaybackRate(2f);
                deadline = Time.realtimeSinceStartup + 150f;
                var shots = new System.Collections.Generic.HashSet<PteranodonDropSequence.Phase>();
                var flightSamples = new System.Collections.Generic.List<FlightClearanceRuntimeQa.Sample>();
                float drift = 0f, maxGrip = 0f, maxActualGap = 0f, headClearance = float.PositiveInfinity;
                float lowLake = float.PositiveInfinity;
                bool paused = false, forward = false, lakeShot = false;
                bool frontPass = false, behindPass = false, rearPickup = false, rearShot = false;
                float minCarryAlignment = 1f;
                bool fallingObserved = false, impactObserved = false;
                float previousFallTime = -1f, previousFallHeight = 0f, firstFallSpeed = 0f, lastFallSpeed = 0f;
                float maxSuspensionTravel = 0f, maxCameraShake = 0f;
                string poseFailure = null;
                while ((!sequence.Released || sequence.CurrentPhase == PteranodonDropSequence.Phase.Impacting)
                    && Time.realtimeSinceStartup < deadline)
                {
                    var phase = sequence.CurrentPhase;
                    if (phase < PteranodonDropSequence.Phase.Lifting)
                    {
                        drift = Mathf.Max(drift, Vector3.Distance(crest, controller.transform.position));
                        Require(drift < .001f && controller.RideSpeed < .001f
                            && Quaternion.Angle(crestRotation, controller.transform.rotation) < .1f,
                            "cart stays still and never turns during pickup");
                    }
                    float birdOffset = Vector3.Dot(sequence.Actor.transform.position - crest, crestForward);
                    float alignment = Vector3.Dot(
                        Vector3.ProjectOnPlane(controller.transform.forward, Vector3.up).normalized,
                        Vector3.ProjectOnPlane(sequence.Actor.transform.forward, Vector3.up).normalized);
                    if (phase == PteranodonDropSequence.Phase.Swooping)
                    {
                        frontPass |= birdOffset > 6f;
                        behindPass |= frontPass && birdOffset < -6f;
                    }
                    if (phase == PteranodonDropSequence.Phase.RearApproaching)
                    {
                        rearPickup |= behindPass && birdOffset < -6f && alignment > .98f;
                        if (!rearShot && sequence.PhaseTime > 3.8f)
                        {
                            Vector3 cart = controller.transform.position;
                            Capture("rear-pickup", cart + sequence.Actor.transform.right * 11f
                                - crestForward * 9f + Vector3.up * 5f, cart + Vector3.up * 2f);
                            rearShot = true;
                        }
                    }
                    if (phase == PteranodonDropSequence.Phase.Grabbing)
                        Require(rearPickup && alignment > .98f, "pickup follows the pass and approach from behind");
                    bool impactPhase = phase >= PteranodonDropSequence.Phase.Releasing
                        && phase <= PteranodonDropSequence.Phase.Impacting;
                    if (sequence.PhaseTime > (impactPhase ? .035f : 1f) && shots.Add(phase))
                    {
                        Time.timeScale = 0f;
                        yield return null;
                        Vector3 cart = controller.transform.position;
                        Capture($"capture-{(int)phase:00}-{phase}");
                        Capture($"external-{(int)phase:00}-{phase}", cart + sequence.Actor.transform.right * 11f
                            - sequence.Actor.transform.forward * 9f + Vector3.up * 5f, cart + Vector3.up * 2f);
                        Time.timeScale = 1f;
                        File.AppendAllText(Path.Combine(Output, "qa.txt"), $"Phase {phase}: cart={cart}, grip={sequence.GripGap:F3}m\n");
                    }
                    if (sequence.GripLocked)
                    {
                        minCarryAlignment = Mathf.Min(minCarryAlignment, alignment);
                        Require(minCarryAlignment > .98f, "passenger faces forward throughout the flight");
                        maxGrip = Mathf.Max(maxGrip, sequence.GripGap);
                        if (maxGrip >= .10f && poseFailure == null)
                            poseFailure = $"claws stay on cart: gap={maxGrip:F3}m phase={phase}";
                        {
                            flightSamples.Add(FlightClearanceRuntimeQa.Record(controller, sequence));
                            float actual = MeasureActualContact(sequence, out float head, out string nearestBone);
                            maxActualGap = Mathf.Max(maxActualGap, actual);
                            headClearance = Mathf.Min(headClearance, head);
                            if ((actual >= .2f || head <= .4f) && poseFailure == null)
                            {
                                poseFailure = $"skin contact={actual:F3}m head clearance={head:F3}m phase={phase} time={sequence.PhaseTime:F2} near={nearestBone}";
                                Capture("contact-failure");
                                Vector3 cart = controller.transform.position;
                                Capture("contact-failure-external", cart + sequence.Actor.transform.right * 6f
                                    - sequence.Actor.transform.forward * 5f + Vector3.up * 3f, cart + Vector3.up);
                            }
                        }
                    }
                    if (phase == PteranodonDropSequence.Phase.Carrying)
                    {
                        float horizontal = Vector3.ProjectOnPlane(controller.transform.position - sequence.LakeCenter, Vector3.up).magnitude;
                        if (horizontal < 36f) lowLake = Mathf.Min(lowLake, controller.transform.position.y - sequence.LakeCenter.y);
                        if (!lakeShot && lowLake < 10f)
                        {
                            Capture("capture-low-lake");
                            Vector3 cart = controller.transform.position;
                            Capture("external-low-lake", cart + new Vector3(14f, 7f, -14f), cart + Vector3.up * 2f);
                            lakeShot = true;
                        }
                        if (!paused && sequence.PhaseTime > 3f)
                        {
                            float phaseTime = sequence.PhaseTime;
                            Vector3 cart = controller.transform.position, bird = sequence.Actor.transform.position;
                            Time.timeScale = 0f;
                            yield return new WaitForSecondsRealtime(.3f);
                            Require(Mathf.Abs(sequence.PhaseTime - phaseTime) < .001f
                                && Vector3.Distance(cart, controller.transform.position) < .001f
                                && Vector3.Distance(bird, sequence.Actor.transform.position) < .001f, "pause freezes cart and animal");
                            Time.timeScale = 1f;
                            paused = true;
                        }
                    }
                    if (phase == PteranodonDropSequence.Phase.Releasing)
                        forward |= Vector3.Dot(controller.transform.forward, sequence.Actor.transform.forward) > .98f;
                    if (phase == PteranodonDropSequence.Phase.Falling)
                    {
                        fallingObserved = true;
                        float fallHeight = controller.transform.position.y - sequence.LandingPosition.y;
                        if (previousFallTime >= 0f && sequence.PhaseTime > previousFallTime)
                        {
                            float speed = (previousFallHeight - fallHeight) / (sequence.PhaseTime - previousFallTime);
                            Require(speed > 5f && speed + .1f >= lastFallSpeed, "free fall accelerates into the rail");
                            if (firstFallSpeed == 0f) firstFallSpeed = speed;
                            lastFallSpeed = speed;
                        }
                        previousFallTime = sequence.PhaseTime;
                        previousFallHeight = fallHeight;
                    }
                    if (phase == PteranodonDropSequence.Phase.Impacting)
                    {
                        impactObserved = true;
                        RidePose trackPose = controller.Spline.PoseAtDistance(controller.RideProgress * controller.Spline.Length);
                        Quaternion trackRotation = Quaternion.LookRotation(trackPose.Tangent, Vector3.up)
                            * Quaternion.AngleAxis(trackPose.BankDegrees, Vector3.forward);
                        Vector3 railPosition = trackPose.Position + trackRotation * Vector3.up * .4f;
                        maxSuspensionTravel = Mathf.Max(maxSuspensionTravel, Vector3.Distance(controller.transform.position, railPosition));
                        maxCameraShake = Mathf.Max(maxCameraShake, sequence.CameraShakeOffset);
                        Require(!sequence.IsHolding && controller.RideSpeed > 4f,
                            "cart keeps descending while suspension and camera react");
                    }
                    yield return null;
                }
                Require(sequence.Released && sequence.CompletedLaps == 2 && sequence.Screeched, "release follows two circles and screech");
                Require(frontPass && behindPass && rearPickup && forward && paused && lowLake > 3f && lowLake < 14f,
                    $"front pass, rear pickup, forward flight, pause, lake height={lowLake:F2}m");
                Require(fallingObserved && impactObserved && sequence.ReleaseHeight > 2.5f
                    && sequence.ImpactCount == 1 && sequence.ImpactSpeed > 9f && lastFallSpeed > firstFallSpeed + .5f,
                    $"airborne throw and one impact: height={sequence.ReleaseHeight:F2}, speed={sequence.ImpactSpeed:F2}");
                Require(maxSuspensionTravel > .02f && maxSuspensionTravel < .17f, "short suspension response on impact");
                Require(maxCameraShake > .005f && sequence.CameraShakeOffset < .0001f,
                    "camera shakes briefly at impact and returns to the seat pose");
                Require(sequence.ReturnPositionGap < .001f && sequence.ReturnRotationGap < .1f, "suspension settles on the rail after impact");
                Require(controller.RideSpeed > 2f, "ride resumes with momentum after the slam");
                float impactTravel = controller.RideProgress * controller.Spline.Length - sequence.LandingDistance;
                Require(sequence.LandingSlope <= -.5f && sequence.LandingPosition.y < crest.y - 1f
                    && impactTravel > 1.5f, $"release directly on descent, moving through impact: {impactTravel:F2}m");
                bool flightClear = FlightClearanceRuntimeQa.Validate(controller, flightSamples, out string flightGeometry);
                File.AppendAllText(Path.Combine(Output, "qa.txt"), "Flight: " + flightGeometry + "\n");
                if (poseFailure != null) File.AppendAllText(Path.Combine(Output, "qa.txt"), "Pose: " + poseFailure + $", minHead={headClearance:F3}, maxGrip={maxGrip:F3}\n");
                Require(flightClear && poseFailure == null, flightGeometry + (poseFailure == null ? "" : " | " + poseFailure));
                float maximumSpeed = 0f, caveEntrySpeed = 0f;
                bool caveShot = false;
                deadline = Time.realtimeSinceStartup + 90f;
                while (controller.RideProgress < RideMotionProfile.DropRecoveryEnd && Time.realtimeSinceStartup < deadline)
                {
                    maximumSpeed = Mathf.Max(maximumSpeed, controller.RideSpeed);
                    if (!caveShot && controller.RideProgress >= MountainTrackCave.StartProgress)
                    { caveEntrySpeed = controller.RideSpeed; Capture("capture-15-cave-drop"); caveShot = true; }
                    yield return null;
                }
                Require(maximumSpeed >= 23f && caveEntrySpeed >= 20f && caveShot, "fast cave drop after release");
                Require(controller.RideProgress >= RideMotionProfile.DropRecoveryEnd && controller.RideSpeed < 13f, "loop and corkscrew recovery");
                controller.DeveloperSeekToProgress(RideMotionProfile.DropRecoveryEnd + .004f);
                yield return null;
                Require(sequence.Released && !sequence.IsHolding && !sequence.Actor.activeSelf, "forward seek skips capture");
                controller.DeveloperSeekToProgress(RideMotionProfile.CrestStart - .008f);
                Require(!sequence.Released && sequence.CompletedLaps == 0 && !sequence.Screeched
                    && sequence.ImpactCount == 0 && sequence.ImpactSpeed == 0f
                    && sequence.CameraShakeOffset < .0001f, "backward seek resets capture and impact");
                controller.SetDeveloperPlaybackRate(4f);
                deadline = Time.realtimeSinceStartup + 90f;
                while ((!sequence.Released || sequence.CurrentPhase == PteranodonDropSequence.Phase.Impacting)
                    && Time.realtimeSinceStartup < deadline) yield return null;
                Require(sequence.Released && sequence.CompletedLaps == 2 && sequence.Screeched
                    && sequence.ImpactCount == 1, "complete replay with exactly one impact");
                controller.SetDeveloperPlaybackRate(1f);
                string report = $"PASS | crest={crest.y:F2}m, laps=2, holdDrift={drift:F4}m, "
                    + $"peakCrestBrake={peakBrake:F3}m/s2, stopSpeedStep={stopStep:F4}m/s, "
                    + $"grip={maxGrip:F3}m, actualSkinGap={maxActualGap:F3}m, headClearance={headClearance:F2}m, "
                    + $"lakeHeight={lowLake:F2}m, returnGap={sequence.ReturnPositionGap:F4}m, "
                    + $"releaseHeight={sequence.ReleaseHeight:F2}m, impactSpeed={sequence.ImpactSpeed:F2}m/s, "
                    + $"suspensionTravel={maxSuspensionTravel:F3}m, cameraShake={maxCameraShake:F3}m, "
                    + $"landingSlope={sequence.LandingSlope:F3}, impactTravel={impactTravel:F2}m, downhillRelease=PASS, "
                    + $"freeFall=PASS, impact=PASS, cameraReset=PASS, "
                    + $"peakSpeed={maximumSpeed*3.6f:F1}km/h, caveEntry={caveEntrySpeed*3.6f:F1}km/h, "
                    + $"frontPass=PASS, rearPickup=PASS, forwardFlight=PASS, minAlignment={minCarryAlignment:F4}, "
                    + $"pause=PASS, seek=PASS, replay=PASS\nGeometry: {geometry}\nFlight: {flightGeometry}\n";
                File.WriteAllText(Path.Combine(Output, "qa.txt"), report);
                Debug.Log("[Cave Drop QA] " + report);
                pass = true;
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                if (timeline != null) timeline.enabled = true;
                if (!pass) File.AppendAllText(Path.Combine(Output, "qa.txt"), "FAIL / interrupted; inspect Unity Console.\n");
                EditorApplication.isPlaying = false;
            }
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("[Cave Drop QA] FAIL | " + detail);
        }

        private static float MeasureActualContact(PteranodonDropSequence sequence, out float headClearance, out string nearestBone)
        {
            float minimum = float.PositiveInfinity;
            headClearance = float.PositiveInfinity;
            nearestBone = "";
            Vector3 head = Camera.main.transform.position;
            Vector3 grip = sequence.GripPoints[0];
            foreach (SkinnedMeshRenderer skin in sequence.Actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Mesh mesh = skin.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4[] bind = mesh.bindposes, matrices = new Matrix4x4[bind.Length];
                for (int i = 0; i < bind.Length; i++) matrices[i] = skin.bones[i].localToWorldMatrix * bind[i];
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight w = weights[i];
                    Vector3 world = matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i]) * w.weight0
                        + matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i]) * w.weight1
                        + matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i]) * w.weight2
                        + matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i]) * w.weight3;
                    minimum = Mathf.Min(minimum, Vector3.Distance(world, grip));
                    float distance = Vector3.Distance(world, head);
                    if (distance < headClearance)
                    { headClearance = distance; nearestBone = skin.bones[w.boneIndex0].name; }
                }
            }
            return minimum;
        }

        internal static void Capture(string name, Vector3? position = null, Vector3? target = null, string outputDirectory = null)
        {
            Camera source = Camera.main;
            GameObject temporary = new("Cave Drop Preview Camera");
            Camera camera = temporary.AddComponent<Camera>();
            camera.CopyFrom(source);
            camera.enabled = false;
            UniversalAdditionalCameraData data = temporary.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            if (position.HasValue)
            {
                camera.transform.position = position.Value;
                camera.transform.LookAt(target.Value);
                camera.fieldOfView = 62f;
            }
            RenderTexture previous = RenderTexture.active;
            RenderTexture texture = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply();
                string directory = outputDirectory ?? Output;
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(texture);
                DestroyImmediate(image);
                DestroyImmediate(temporary);
            }
        }
    }
}
#endif
