from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / 'Assets/_Game/Scripts/Runtime/TriceratopsGrounding.cs'
s = source.read_text()
s = s.replace('internal Vector3 Sole, SoleUp, Planted, Target;',
'''internal Vector3 Sole, SoleUp, Planted, Target, Home, SwingStart, SwingEnd;
            internal float PhaseOffset;''')
s = s.replace('private static Material _skinMaterial;', '''// Keep these phases and duty factor in sync with import_downloaded_triceratops.py.
        private const float StanceFraction = 0.75f;
        private static readonly float[] PhaseOffsets = { 0f, 0.5f, 0.25f, 0.75f };
        private static Material _skinMaterial;''')
s = s.replace('private float _settleUntil;', '''private float _settleUntil;
        private Vector3 _previousPosition, _velocity;
        private float _previousYaw, _turnSpeed;
        private bool _motionInitialized;''')
s = s.replace('_legs[legIndex++] = leg;', 'leg.PhaseOffset = PhaseOffsets[legIndex];\n                _legs[legIndex++] = leg;')
s = s.replace('/ 0.65f);', '/ StanceFraction);')
s = s.replace('DinosaurAnimationRuntime.PlayPreferred(gameObject, "walk", 1f);', '''// Mid-support is the anatomical footprint, before world-space foot locking.
            foreach (Leg leg in _legs)
            {
                float phase = Mathf.Repeat(StanceFraction * 0.5f - leg.PhaseOffset, 1f);
                clip.SampleAnimation(_animation.gameObject, clip.length * phase);
                leg.Home = transform.InverseTransformPoint(leg.Contact);
            }
            DinosaurAnimationRuntime.PlayPreferred(gameObject, "walk", 1f);''')
s = s.replace('''_movementSpeed = speed;
            if (_walk != null) _walk.speed = Mathf.Clamp(speed * _walk.length / _strideLength, 0.08f, 1.3f);''', '''_movementSpeed = Mathf.Lerp(_movementSpeed, speed, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            if (_walk != null) _walk.speed = Mathf.Clamp(_movementSpeed * _walk.length / _strideLength, 0f, 1.3f);''')
a = s.index('            Vector3 front =', s.index('private void LateUpdate'))
b = s.index('            // Constrain vertical reach', a)
s = s[:a] + '''            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (_motionInitialized)
            {
                Vector3 velocity = (transform.position - _previousPosition) / dt;
                velocity.y = 0f;
                _velocity = Vector3.Lerp(_velocity, velocity, 1f - Mathf.Exp(-dt * 6f));
                _turnSpeed = Mathf.Lerp(_turnSpeed,
                    Mathf.DeltaAngle(_previousYaw, transform.eulerAngles.y) / dt, 1f - Mathf.Exp(-dt * 6f));
            }
            _previousPosition = transform.position;
            _previousYaw = transform.eulerAngles.y;
            _motionInitialized = true;

            // Sample terrain beneath stable anatomical footprints, not the moving IK feet.
            Vector3 front = transform.TransformPoint((_legs[0].Home + _legs[1].Home) * 0.5f);
            Vector3 back = transform.TransformPoint((_legs[2].Home + _legs[3].Home) * 0.5f);
            Vector3 left = transform.TransformPoint((_legs[0].Home + _legs[2].Home) * 0.5f);
            Vector3 right = transform.TransformPoint((_legs[1].Home + _legs[3].Home) * 0.5f);
            front.y = Ground(front); back.y = Ground(back);
            left.y = Ground(left); right.y = Ground(right);
            Vector3 normal = Vector3.Cross(front - back, right - left).normalized;
            if (normal.y < 0f) normal = -normal;
            Quaternion slope = Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(normal));
            slope = Quaternion.RotateTowards(Quaternion.identity, slope, 18f);
            _slope = Quaternion.Slerp(_slope, slope, 1f - Mathf.Exp(-dt * 3f));
            _model.localRotation = _slope;

            _model.localPosition = _basePosition;
            float correction = 0f;
            foreach (Leg leg in _legs)
            {
                Vector3 sole = leg.Contact;
                float phase = Mathf.Repeat(_walk.normalizedTime + leg.PhaseOffset, 1f);
                float swing = Mathf.Clamp01((phase - StanceFraction) / (1f - StanceFraction));
                float lift = LiftHeight(leg) * Mathf.Pow(Mathf.Sin(Mathf.PI * swing), 2f);
                correction += Ground(sole) - (sole.y - lift);
            }
            float desiredOffset = Mathf.Clamp(correction * 0.25f - LengthMeters * 0.009f, -1.4f, 1.4f);
            _bodyOffset = Mathf.MoveTowards(_bodyOffset, desiredOffset, dt * 0.45f);
            _model.localPosition = _basePosition + Vector3.up * _bodyOffset;

            foreach (Leg leg in _legs)
            {
                float phase = Mathf.Repeat(_walk.normalizedTime + leg.PhaseOffset, 1f);
                bool swing = phase >= StanceFraction;
                float progress = Mathf.Clamp01((phase - StanceFraction) / (1f - StanceFraction));
                if (!leg.Initialized)
                {
                    leg.Planted = leg.Contact;
                    leg.Planted.y = Ground(leg.Planted) + 0.012f;
                    leg.SwingStart = leg.Planted;
                    leg.SwingEnd = PredictLanding(leg, 1f - phase);
                }
                else if (swing && !leg.Swing)
                {
                    leg.SwingStart = leg.Planted;
                    leg.SwingEnd = PredictLanding(leg, 1f - phase);
                }
                else if (!swing && leg.Swing)
                {
                    leg.Planted = leg.SwingEnd;
                    if (_movementSpeed > 0.08f && Time.time > _settleUntil)
                    {
                        Footfalls++;
                        EmitFootfall(leg.Planted);
                    }
                }
                // World-space quintic swing: zero foot speed at lift-off and touchdown.
                float blend = progress * progress * progress * (progress * (progress * 6f - 15f) + 10f);
                Vector3 target = swing ? Vector3.Lerp(leg.SwingStart, leg.SwingEnd, blend) : leg.Planted;
                if (swing) target.y += LiftHeight(leg) * Mathf.Pow(Mathf.Sin(Mathf.PI * progress), 2f);
                leg.Target = target;
                leg.Initialized = true;
                leg.Swing = swing;
            }

''' + s[b:]
a = s.index('                if (horizontalSquared > reach')
b = s.index('                float verticalReach', a)
s = s[:a] + s[b:]
s = s.replace('''// Constrain vertical reach without lowering a hip beneath its planted ankle.
            // A short-legged animal turning on a slope may also need to release an old support.''', '''// Keep support reachable without teleporting a planted foot.
            // Body corrections are speed-limited, including on slopes.''')
s = s.replace('_bodyOffset += Mathf.Clamp(bodyShift, -0.25f, 0.25f);', '_bodyOffset += Mathf.Clamp(bodyShift, -dt * 0.45f, dt * 0.45f);')
a = s.index('        private void SolveLeg(')
s = s[:a] + '''        private float LiftHeight(Leg leg) => Mathf.Clamp(
            (leg.MaximumY - leg.MinimumY) * _model.lossyScale.y, 0.06f, LengthMeters * 0.024f);

        private Vector3 PredictLanding(Leg leg, float remainingPhase)
        {
            float cycleSeconds = _walk.length / Mathf.Max(_walk.speed, 0.1f);
            float seconds = Mathf.Min(remainingPhase * cycleSeconds, 1.2f);
            float radians = _turnSpeed * Mathf.Deg2Rad;
            float angle = radians * seconds;
            Vector3 displacement = Mathf.Abs(radians) < 0.001f ? _velocity * seconds
                : _velocity * (Mathf.Sin(angle) / radians)
                    + Vector3.Cross(Vector3.up, _velocity) * ((1f - Mathf.Cos(angle)) / radians);
            Quaternion turn = Quaternion.AngleAxis(_turnSpeed * seconds, Vector3.up);
            Vector3 home = turn * transform.TransformVector(leg.Home);
            Vector3 footVelocity = turn * _velocity + Vector3.Cross(Vector3.up * radians, home);
            Vector3 anticipation = footVelocity * (cycleSeconds * StanceFraction * 0.45f);
            anticipation.y = 0f;
            anticipation = Vector3.ClampMagnitude(anticipation, LengthMeters * 0.075f);
            Vector3 landing = transform.position + displacement + home + anticipation;
            landing.y = Ground(landing) + 0.012f;
            return landing;
        }

''' + s[a:]
(root / 'work/triceratops-walk/TriceratopsGrounding.cs').write_text(s)
