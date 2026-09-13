#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class InversionRuntimeQa : MonoBehaviour
    {
        private const string RequestedKey = "ValeMesozoico.InversionsQA";
        private static string Output => Path.GetFullPath("Builds/Previews/Inversions");

        [InitializeOnLoadMethod]
        private static void RegisterRestart()
        {
            EditorApplication.playModeStateChanged -= RestartAfterExit;
            EditorApplication.playModeStateChanged += RestartAfterExit;
        }

        private static void RestartAfterExit(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(RequestedKey, false))
                EditorApplication.isPlaying = true;
        }

        [MenuItem("Vale Mesozoico/Preview/Loop and Corkscrew")]
        private static void Run()
        {
            SessionState.SetBool(RequestedKey, true);
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (!SessionState.GetBool(RequestedKey, false)) return;
            SessionState.SetBool(RequestedKey, false);
            new GameObject("Inversion Runtime QA").AddComponent<InversionRuntimeQa>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(Output);
            string reportPath = Path.Combine(Output, "qa.txt");
            File.WriteAllText(reportPath, "RUNNING\n");
            bool completed = false;
            RideDebugTimeline timeline = null;
            try
            {
                Time.timeScale = 1f;
                yield return null;
                yield return new WaitForSecondsRealtime(0.8f);
                TrackRuntimeQa trackQa = FindFirstObjectByType<TrackRuntimeQa>();
                while (trackQa != null && !trackQa.InitialValidationComplete) yield return null;
                // Let the timeline's startup check finish before locking controls.
                yield return null;
                RideController controller = FindFirstObjectByType<RideController>();
                Require(controller != null, "ride controller missing");
                timeline = controller.GetComponent<RideDebugTimeline>();
                if (timeline != null) timeline.enabled = false;
                RideSpline spline = controller.Spline;
                float start = spline.InversionsStartProgress;
                float end = spline.InversionsEndProgress;
                string frames = ValidateFrames(spline);
                File.AppendAllText(reportPath, frames + "\n");
                Require(EnvironmentRuntimeQa.ValidateMountainCaveForPreview(out string cave), cave);
                bool clearance = EnvironmentRuntimeQa.ValidateRideSectionForPreview(start - 0.006f, end + 0.006f, false, out string geometry);
                File.AppendAllText(reportPath, geometry + "\n");

                // Always produce the geometry views, including on a failed clearance probe.
                controller.DeveloperSeekToProgress(start);
                RidePose origin = spline.PoseAtDistance(start * spline.Length);
                Vector3 focus = origin.Position + Vector3.up * 10f - Vector3.right * 12f;
                Capture("01-loop-e-parafuso", focus + spline.InversionRight * 48f + Vector3.up * 19f, focus);
                float loopApex = FindMostInverted(spline, true);
                float corkApex = FindMostInverted(spline, false);
                controller.DeveloperSeekToProgress(loopApex);
                Capture("02-topo-do-loop");
                controller.DeveloperSeekToProgress(corkApex);
                Capture("03-parafuso-invertido");
                Require(clearance, geometry);

                controller.DeveloperSeekToProgress(spline.MapBaseProgress(0.40f));
                float deadline = Time.realtimeSinceStartup + 100f;
                float caveExitSpeed = 0f, minimumLoopSpeed = float.PositiveInfinity;
                float minimumCorkSpeed = float.PositiveInfinity, maximumPoseError = 0f;
                bool exitShot = false, loopShot = false, corkShot = false;
                while (controller.RideProgress < RideMotionProfile.DropRecoveryEnd && Time.realtimeSinceStartup < deadline)
                {
                    float progress = controller.RideProgress;
                    float distance = progress * spline.Length;
                    RidePose pose = spline.PoseAtDistance(distance);
                    if (!exitShot && progress >= MountainTrackCave.EndProgress)
                    {
                        caveExitSpeed = controller.RideSpeed;
                        Capture("04-saida-rapida-da-caverna");
                        exitShot = true;
                    }
                    if (spline.IsInversionAtDistance(distance))
                    {
                        maximumPoseError = Mathf.Max(maximumPoseError, Quaternion.Angle(controller.transform.rotation, pose.Rotation));
                        Require(controller.RideSpeed > 8f, "ride stalled in an inversion");
                    }
                    if (spline.IsLoopAtDistance(distance))
                    {
                        minimumLoopSpeed = Mathf.Min(minimumLoopSpeed, controller.RideSpeed);
                        if (!loopShot && Vector3.Dot(pose.Rotation * Vector3.up, Vector3.up) < -0.9f)
                        {
                            Capture("05-loop-em-movimento");
                            loopShot = true;
                        }
                    }
                    if (spline.IsCorkscrewAtDistance(distance))
                    {
                        minimumCorkSpeed = Mathf.Min(minimumCorkSpeed, controller.RideSpeed);
                        if (!corkShot && Vector3.Dot(pose.Rotation * Vector3.up, Vector3.up) < -0.9f)
                        {
                            Capture("06-parafuso-em-movimento");
                            corkShot = true;
                        }
                    }
                    yield return null;
                }
                Require(controller.RideProgress >= RideMotionProfile.DropRecoveryEnd, "inversion traversal timed out");
                Require(caveExitSpeed > 22f && minimumLoopSpeed > 12f && minimumCorkSpeed > 12f,
                    $"speeds: cave={caveExitSpeed:F2}, loop={minimumLoopSpeed:F2}, corkscrew={minimumCorkSpeed:F2}m/s");
                Require(maximumPoseError < 0.15f && Vector3.Dot(controller.transform.up, Vector3.up) > 0.97f,
                    $"cart alignment={maximumPoseError:F3}deg / upright exit");
                float recoverySpeed = controller.RideSpeed;
                Require(recoverySpeed < 12f, $"recovery speed={recoverySpeed:F2}m/s");
                Capture("07-retorno-ao-normal");
                Require(RideMotionProfile.DropRecoveryEnd < TyrannosaurusChaseSequence.TriggerProgress
                    && TyrannosaurusChaseSequence.RoarProgress < TrackMeshFactory.BreakStartProgress,
                    "next encounter overlaps the inversions");
                TyrannosaurusChaseSequence chase = FindFirstObjectByType<TyrannosaurusChaseSequence>();
                Require(chase != null, "next encounter missing");
                while (controller.RideProgress < TyrannosaurusChaseSequence.EndProgress + 0.006f
                    && Time.realtimeSinceStartup < deadline) yield return null;
                Require(chase.Completed && chase.Roared && chase.TrackBroken, "next encounter did not complete");
                string report = $"PASS | {frames}, caveExit={caveExitSpeed * 3.6f:F1}km/h, "
                    + $"minimumLoop={minimumLoopSpeed * 3.6f:F1}km/h, minimumCorkscrew={minimumCorkSpeed * 3.6f:F1}km/h, "
                    + $"recovery={recoverySpeed * 3.6f:F1}km/h, cartRotationError={maximumPoseError:F3}deg, "
                    + $"uprightExit=PASS, nextEncounter=PASS\nClearance: {geometry}\nCave: {cave}\n";
                File.WriteAllText(reportPath, report);
                Debug.Log("[Inversion QA] " + report);
                completed = true;
            }
            finally
            {
                Time.timeScale = 1f;
                if (timeline != null) timeline.enabled = true;
                if (!completed) File.AppendAllText(reportPath, "FAIL / interrupted; inspect Unity Console.\n");
                EditorApplication.isPlaying = false;
            }
        }

        private static string ValidateFrames(RideSpline spline)
        {
            float start = spline.InversionsStartProgress * spline.Length;
            float end = spline.InversionsEndProgress * spline.Length;
            RidePose previous = spline.PoseAtDistance(start - 0.1f);
            float loopTurn = 0f, corkTurn = 0f, maximumStepAngle = 0f, minGround = float.PositiveInfinity;
            Vector3 forward = Vector3.Cross(spline.InversionRight, Vector3.up);
            ForestFloorSurface ground = ForestFloorSurface.Load();
            for (float distance = start; distance <= end + 0.1f; distance += 0.1f)
            {
                RidePose pose = spline.PoseAtDistance(distance);
                maximumStepAngle = Mathf.Max(maximumStepAngle, Quaternion.Angle(previous.Rotation, pose.Rotation));
                if (spline.IsLoopAtDistance(distance))
                    loopTurn += Vector3.SignedAngle(Vector3.ProjectOnPlane(previous.Tangent, spline.InversionRight),
                        Vector3.ProjectOnPlane(pose.Tangent, spline.InversionRight), spline.InversionRight);
                if (spline.IsCorkscrewAtDistance(distance))
                    corkTurn += Vector3.SignedAngle(Vector3.ProjectOnPlane(previous.Rotation * Vector3.up, forward),
                        Vector3.ProjectOnPlane(pose.Rotation * Vector3.up, forward), forward);
                if (ground != null && ground.Sample(pose.Position.x, pose.Position.z, out Vector3 point, out _))
                    minGround = Mathf.Min(minGround, pose.Position.y - point.y);
                previous = pose;
            }
            Require(Mathf.Abs(loopTurn) > 350f && Mathf.Abs(loopTurn) < 370f
                && Mathf.Abs(corkTurn) > 350f && Mathf.Abs(corkTurn) < 370f,
                $"complete inversions: loop={loopTurn:F1}deg, corkscrew={corkTurn:F1}deg");
            Require(maximumStepAngle < 9f && minGround > 3.5f,
                $"frame continuity={maximumStepAngle:F2}deg/0.1m, railGroundClearance={minGround:F2}m");
            return $"loop={loopTurn:F1}deg, corkscrew={corkTurn:F1}deg, maxFrameStep={maximumStepAngle:F2}deg/0.1m, ground={minGround:F2}m";
        }

        private static float FindMostInverted(RideSpline spline, bool loop)
        {
            float result = 0f, minimumUp = 1f;
            for (float progress = spline.InversionsStartProgress; progress <= spline.InversionsEndProgress; progress += 0.00015f)
            {
                float distance = progress * spline.Length;
                if (loop ? !spline.IsLoopAtDistance(distance) : !spline.IsCorkscrewAtDistance(distance)) continue;
                float up = Vector3.Dot(spline.PoseAtDistance(distance).Rotation * Vector3.up, Vector3.up);
                if (up >= minimumUp) continue;
                minimumUp = up;
                result = progress;
            }
            return result;
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("[Inversion QA] FAIL | " + detail);
        }

        private static void Capture(string name, Vector3? position = null, Vector3? target = null)
            => CaveDropRuntimeQa.Capture(name, position, target, Output);
    }
}
#endif
