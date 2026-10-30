using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Procedural fighter animation drawn with tapered LineRenderers (each with a dark outline):
    /// curved spine and neck, 3/4-view shoulders and hips, legs with feet, hands, a masked head
    /// with a hachimaki whose tails trail in the wind, two-handed katana grip, walk cycle with
    /// heel lift, landing squash, hit flinch/flash and a death slump. Root transform = hip.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class StickFigureRig : MonoBehaviour
    {
        // Proportions in world units. Hip→shoulder must match Fighter.ShoulderHeight (0.64) and
        // hip→head centre the HeadHitbox (0.99).
        // Legs are just shorter than hip→ankle so the idle stance has only a slight knee bend.
        private const float Thigh = 0.47f;
        private const float Shin = 0.45f;
        private const float AnkleHeight = 0.05f;
        private const float FootLength = 0.17f;
        private const float Waist = 0.4f;
        private const float Chest = 0.3f;
        private const float Neck = 0.1f;
        private const float HeadRadius = 0.19f;
        private const float UpperArm = 0.4f;
        private const float Forearm = 0.41f;
        private const float HandRadius = 0.065f;
        private const float RibbonSegment = 0.11f;

        [SerializeField] private Fighter fighter;
        [SerializeField] private Material lineMaterial;
        [SerializeField] private Sprite headSprite;
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField] private Color outlineColor = new Color(0.06f, 0.03f, 0.09f, 1f);
        [SerializeField, Min(0.1f)] private float thickness = 1f;
        [SerializeField, Min(0f)] private float outlineWidth = 0.06f;
        [SerializeField, Min(0.1f)] private float strideLength = 0.62f;

        [Header("Run cycle (visual only)")]
        [Tooltip("Speed (0..1 of MoveSpeed) where the walk starts turning into a run; full run at full speed.")]
        [SerializeField, Range(0f, 0.95f)] private float runStartSpeed = 0.45f;
        [Tooltip("Ground covered per step at a full run (strideLength is the walking step).")]
        [SerializeField, Min(0.1f)] private float runStrideLength = 1f;
        [Tooltip("How fast the run blends in/out (per second): starting to run, jumping, rolling, kicking, spinning or being hit.")]
        [SerializeField, Min(0f)] private float runBlendSpeed = 6f;
        [Tooltip("Flinch offset at which the run pose is fully suppressed (the hit pose wins).")]
        [SerializeField, Min(0.001f)] private float flinchSuppressDistance = 0.05f;
        [Tooltip("Share of each leg's cycle spent on the floor; under 0.5 leaves a flight phase with both feet up.")]
        [SerializeField, Range(0.2f, 0.5f)] private float runStanceFraction = 0.36f;
        [Tooltip("Where the foot lands, as a share of its stance travel ahead of the hip.")]
        [SerializeField, Range(0f, 1f)] private float runLandAhead = 0.35f;
        [Tooltip("Toe-off: the heel peels up through this last share of the stance to this toe angle (degrees).")]
        [SerializeField, Range(0f, 1f)] private float runHeelPeelShare = 0.45f;
        [SerializeField, Range(0f, 80f)] private float runToeOffAngle = 40f;
        [Tooltip("Heel kick after toe-off: ankle this far behind the hip and this high off the floor.")]
        [SerializeField, Min(0f)] private float runHeelKickBack = 0.22f;
        [SerializeField, Min(0f)] private float runHeelKickHeight = 0.42f;
        [Tooltip("Knee drive: the ankle swings through this far ahead of the hip at this height.")]
        [SerializeField, Min(0f)] private float runKneeDriveForward = 0.16f;
        [SerializeField, Min(0f)] private float runKneeDriveHeight = 0.34f;
        [Tooltip("Just before landing the foot reaches this much past its landing spot, low.")]
        [SerializeField, Min(0f)] private float runReachPast = 0.06f;
        [SerializeField, Min(0f)] private float runReachHeight = 0.1f;
        [Tooltip("Pelvis up/down through each step: lowest at mid-stance, highest in flight.")]
        [SerializeField, Min(0f)] private float runBobDepth = 0.035f;
        [Tooltip("Extra knee bend while running (pelvis drop).")]
        [SerializeField, Min(0f)] private float runCrouch = 0.05f;
        [Tooltip("Extra forward lean at a full run, in degrees.")]
        [SerializeField, Range(0f, 20f)] private float runLeanDegrees = 5f;
        [Tooltip("Free arm pumps: upper arm swing either way (degrees) with the elbow bent this much.")]
        [SerializeField, Range(0f, 90f)] private float armPumpAngle = 50f;
        [SerializeField, Range(0f, 150f)] private float armPumpElbowBend = 95f;
        [Tooltip("Arm swing timing relative to the opposite leg, as a share of the cycle.")]
        [SerializeField, Range(-0.25f, 0.25f)] private float armPumpLead = 0.05f;
        [Tooltip("How quickly the free hand follows its target while running (walk uses 14).")]
        [SerializeField, Min(1f)] private float runArmFollowRate = 30f;
        [Tooltip("While running with a sword, the drawn sword shoulder settles back by up to this much so the arm extends.")]
        [SerializeField, Min(0f)] private float swordRunShoulderPullback = 0.08f;
        [Tooltip("The sword arm never straightens past this fraction of its length (keeps a natural elbow, never stretches).")]
        [SerializeField, Range(0.8f, 1f)] private float swordArmMaxExtension = 0.96f;

        private Line spine, frontLeg, backLeg, frontArm, backArm, band, eye, ribbon, belt;
        private Dot head, frontHand, backHand;
        private SpriteRenderer headShine;
        private Color accent;

        // Spin attack: the drawn body is squashed horizontally by cos(turn) around the hip, which
        // reads as the fighter rotating about their vertical axis (front → edge-on → back → front).
        private float spinScale = 1f;
        private float spinPivotX;

        private float gaitPhase, walkBlend, airBlend, lean, twoHand, landDip, lastVerticalSpeed, flashTimer;
        private float runBlend, currentStride; // 0 walk .. 1 run; step length blended between the two
        private bool wasGrounded = true;
        private Vector2 flinch, flinchVelocity, freeBackHand;
        private readonly bool[] footLifted = new bool[2];

        // Switching pose (stand ↔ roll ↔ dead) eases the limbs over from the last drawn pose.
        private int poseState;
        private float blendTime, blendDuration;
        private Vector2 lastHeadCenter, lastHeadUp = Vector2.up, headFrom, headUpFrom = Vector2.up;
        private float headWeight = 1f;
        private int rollDust;
        private static readonly float[] RollDustAt = { 0.04f, 0.5f, 0.9f };
        private readonly Vector2[] ribbonPoints = new Vector2[5];
        private readonly Vector2[] ribbonPrevious = new Vector2[5];

        // ------------------------------------------------------------------ drawing primitives

        private sealed class Line
        {
            public LineRenderer Fill;
            public LineRenderer Outline;
            public LineRenderer Highlight; // lighter stripe nudged towards the light (upper left)
            /// <summary>Below 1 the drawn pose is still easing in from the captured one.</summary>
            public float Weight = 1f;
            private readonly Vector3[] points;
            private readonly Vector3[] lit;
            private readonly Vector3[] from;
            private static readonly Vector3 LightOffset = new Vector3(-0.012f, 0.016f, -0.001f); // -z: sorts in front of the fill at the same order

            public Line(int count)
            {
                points = new Vector3[count];
                lit = new Vector3[count];
                from = new Vector3[count];
            }

            public void Capture() => System.Array.Copy(points, from, points.Length);

            public void Set(Vector2 a, Vector2 b) { points[0] = a; points[1] = b; Apply(); }
            public void Set(Vector2 a, Vector2 b, Vector2 c) { points[0] = a; points[1] = b; points[2] = c; Apply(); }
            public void Set(Vector2 a, Vector2 b, Vector2 c, Vector2 d) { points[0] = a; points[1] = b; points[2] = c; points[3] = d; Apply(); }

            public void Set(Vector2[] source)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = source[i];
                }

                Apply();
            }

            private void Apply()
            {
                if (Weight < 1f)
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        points[i] = Vector3.Lerp(from[i], points[i], Weight);
                    }
                }

                Fill.positionCount = points.Length;
                Fill.SetPositions(points);
                if (Outline != null)
                {
                    Outline.positionCount = points.Length;
                    Outline.SetPositions(points);
                }

                if (Highlight != null)
                {
                    for (int i = 0; i < points.Length; i++)
                    {
                        lit[i] = points[i] + LightOffset;
                    }

                    Highlight.positionCount = points.Length;
                    Highlight.SetPositions(lit);
                }
            }

            public void SetColor(Color color)
            {
                Fill.startColor = color;
                Fill.endColor = color;
                if (Highlight != null)
                {
                    Color shine = Color.Lerp(color, Color.white, 0.45f);
                    Highlight.startColor = shine;
                    Highlight.endColor = shine;
                }
            }
        }

        private sealed class Dot
        {
            public SpriteRenderer Fill;
            public SpriteRenderer Outline;
            public float Weight = 1f;
            private Vector2 last, from;

            public void Capture() => from = last;

            public void Place(Vector2 position)
            {
                position = Vector2.Lerp(from, position, Weight);
                last = position;
                var p = new Vector3(position.x, position.y, 0f);
                Fill.transform.position = p;
                Outline.transform.position = p;
            }
        }

        private void Awake()
        {
            accent = Color.Lerp(fighter.color, outlineColor, 0.55f);

            //                     name        pts order  width   taper (root → end)
            ribbon = CreateLine("Ribbon", 5, 8, 0.06f, Taper(1f, 0.3f), accent, false);
            backLeg = CreateLine("BackLeg", 4, 9, 0.15f, Taper(1.1f, 0.75f, 0.65f), fighter.color, true);
            backArm = CreateLine("BackArm", 3, 10, 0.12f, Taper(1.1f, 0.75f), fighter.color, true);
            // Torso: hips, narrower waist, broad chest/shoulders, then the neck.
            spine = CreateLine("Spine", 4, 11, 0.22f, new AnimationCurve(
                new Keyframe(0f, 0.95f), new Keyframe(0.25f, 0.8f), new Keyframe(0.62f, 1.15f), new Keyframe(0.85f, 0.7f), new Keyframe(1f, 0.42f)), fighter.color, true);
            frontLeg = CreateLine("FrontLeg", 4, 12, 0.15f, Taper(1.1f, 0.75f, 0.65f), fighter.color, true);
            frontArm = CreateLine("FrontArm", 3, 15, 0.12f, Taper(1.1f, 0.75f), fighter.color, true);
            belt = CreateLine("Obi", 2, 13, 0.07f, Taper(1f, 1f), accent, true);
            band = CreateLine("Hachimaki", 2, 14, 0.07f, Taper(1f, 1f), accent, false);
            eye = CreateLine("Eye", 2, 14, 0.035f, Taper(1f, 0.6f), outlineColor, false);

            head = CreateDot("Head", 13, new Vector2(HeadRadius * 2f, HeadRadius * 2.15f));
            frontHand = CreateDot("FrontHand", 16, Vector2.one * HandRadius * 2f);
            backHand = CreateDot("BackHand", 16, Vector2.one * HandRadius * 2f);
            headShine = CreateSprite("HeadShine", 14, Color.white, new Vector2(0.07f, 0.05f));
            Snap();
        }

        private static AnimationCurve Taper(params float[] widths)
        {
            var keys = new Keyframe[widths.Length];
            for (int i = 0; i < widths.Length; i++)
            {
                keys[i] = new Keyframe(widths.Length == 1 ? 0f : i / (widths.Length - 1f), widths[i]);
            }

            return new AnimationCurve(keys);
        }

        private Line CreateLine(string lineName, int count, int order, float width, AnimationCurve taper, Color color, bool outlined)
        {
            var line = new Line(count)
            {
                Fill = CreateRenderer(lineName, order, width * thickness, taper, color),
            };
            if (outlined)
            {
                float outlineTotal = width * thickness + outlineWidth;
                line.Outline = CreateRenderer(lineName + "Outline", 2, outlineTotal, taper, outlineColor);
                line.Highlight = CreateRenderer(lineName + "Shine", order, width * thickness * 0.35f, taper, color);
            }

            return line;
        }

        private LineRenderer CreateRenderer(string lineName, int order, float width, AnimationCurve taper, Color color)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = lineMaterial;
            line.useWorldSpace = true;
            line.widthCurve = taper;
            line.widthMultiplier = width;
            line.numCapVertices = 6;
            line.numCornerVertices = 6;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = order;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private Dot CreateDot(string dotName, int order, Vector2 size)
        {
            return new Dot
            {
                Outline = CreateSprite(dotName + "Outline", 2, outlineColor, size + Vector2.one * outlineWidth),
                Fill = CreateSprite(dotName, order, fighter.color, size),
            };
        }

        private SpriteRenderer CreateSprite(string spriteName, int order, Color color, Vector2 size)
        {
            var go = new GameObject(spriteName);
            go.transform.SetParent(transform, false);
            float unit = headSprite.bounds.size.x;
            go.transform.localScale = new Vector3(size.x / unit, size.y / unit, 1f);
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = headSprite;
            sprite.color = color;
            sprite.sortingOrder = order;
            return sprite;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Clears animation state, e.g. after a respawn.</summary>
        public void Snap()
        {
            walkBlend = airBlend = lean = landDip = twoHand = runBlend = 0f;
            currentStride = strideLength;
            flinch = flinchVelocity = Vector2.zero;
            flashTimer = 0f;
            wasGrounded = true;
            poseState = 0;
            blendTime = blendDuration = 0f;
            SetBlendWeight(1f);
            freeBackHand = fighter.ShoulderPosition + new Vector2(-fighter.Facing * 0.2f, -0.55f);
            Vector2 knot = (Vector2)transform.position + new Vector2(-fighter.Facing * 0.2f, 1.04f);
            for (int i = 0; i < ribbonPoints.Length; i++)
            {
                ribbonPoints[i] = ribbonPrevious[i] = knot + new Vector2(-fighter.Facing * RibbonSegment * i, -0.03f * i);
            }
        }

        public void Flinch(Vector2 knockback, bool headshot)
        {
            flinchVelocity += knockback.normalized * (headshot ? 4.5f : 3f);
            flashTimer = 0.12f;
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            flashTimer -= dt;
            bool flash = flashTimer > 0f;
            Color front = flash ? Color.white : fighter.color;
            Color back = flash ? Color.white : Color.Lerp(fighter.color, outlineColor, 0.3f);
            spine.SetColor(front);
            frontLeg.SetColor(front);
            frontArm.SetColor(front);
            backLeg.SetColor(back);
            backArm.SetColor(back);
            head.Fill.color = front;
            frontHand.Fill.color = front;
            backHand.Fill.color = back;
            headShine.color = Color.Lerp(front, Color.white, 0.55f);

            int state = fighter.IsDead ? 2 : fighter.IsRolling ? 1 : 0;
            if (state != poseState)
            {
                if (poseState == 1 && state == 0)
                {
                    landDip = Mathf.Max(landDip, 0.14f); // come out of the roll low and rise
                }

                rollDust = 0;
                poseState = state;
                StartBlend(state == 1 ? 0.07f : 0.14f);
            }

            blendTime += dt;
            SetBlendWeight(blendDuration > 0f ? Mathf.SmoothStep(0f, 1f, blendTime / blendDuration) : 1f);

            if (fighter.IsDead)
            {
                DrawDead(dt);
            }
            else if (fighter.IsRolling)
            {
                DrawRoll(fighter.RollProgress, dt);
            }
            else
            {
                DrawAlive(dt);
            }
        }

        private void StartBlend(float duration)
        {
            foreach (Line line in new[] { spine, frontLeg, backLeg, frontArm, backArm, belt })
            {
                line.Capture();
            }

            frontHand.Capture();
            backHand.Capture();
            headFrom = lastHeadCenter;
            headUpFrom = lastHeadUp;
            blendTime = 0f;
            blendDuration = duration;
        }

        private void SetBlendWeight(float weight)
        {
            spine.Weight = frontLeg.Weight = backLeg.Weight = frontArm.Weight = backArm.Weight = belt.Weight = weight;
            frontHand.Weight = backHand.Weight = weight;
            headWeight = weight;
        }

        private void DrawAlive(float dt)
        {
            int facing = fighter.Facing;
            Vector2 right = new Vector2(facing, 0f);
            Vector2 hip = transform.position;
            Vector2 velocity = fighter.Body.linearVelocity;
            bool grounded = fighter.IsGrounded;

            float speed01 = Mathf.Clamp01(Mathf.Abs(velocity.x) / Mathf.Max(0.01f, fighter.MoveSpeed));
            walkBlend = Mathf.MoveTowards(walkBlend, grounded ? speed01 : 0f, dt * 6f);
            airBlend = Mathf.MoveTowards(airBlend, grounded ? 0f : 1f, dt * 8f);
            float spin = fighter.sword != null ? fighter.sword.SpinProgress : -1f;
            float moveDir = Mathf.Abs(velocity.x) > 0.05f ? Mathf.Sign(velocity.x) : facing;

            // Running forward turns the walk into a run; never in the air, backpedalling, kicking,
            // spinning or flinching from a hit.
            bool runAllowed = grounded && spin < 0f && fighter.KickProgress < 0f && moveDir == facing;
            float flinchCalm = 1f - Mathf.Clamp01(flinch.magnitude / flinchSuppressDistance);
            float runTarget = runAllowed ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(runStartSpeed, 1f, walkBlend)) * flinchCalm : 0f;
            runBlend = Mathf.MoveTowards(runBlend, runTarget, dt * runBlendSpeed);

            // The phase advances by distance, so the planted foot stays locked to the floor.
            currentStride = Mathf.Lerp(strideLength, runStrideLength, runBlend);
            gaitPhase += Mathf.Abs(velocity.x) * dt / currentStride * Mathf.PI;

            // Landing: knees absorb the impact proportionally to the fall speed.
            if (grounded && !wasGrounded)
            {
                landDip = Mathf.Max(landDip, Mathf.Clamp01(-lastVerticalSpeed / 14f) * 0.24f);
            }

            wasGrounded = grounded;
            lastVerticalSpeed = velocity.y;
            landDip = Mathf.MoveTowards(landDip, 0f, dt * 0.9f);

            // Damped spring pulls the flinch offset back to rest.
            flinchVelocity += (-flinch * 260f - flinchVelocity * 20f) * dt;
            flinch = Vector2.ClampMagnitude(flinch + flinchVelocity * dt, 0.14f); // a hit rocks the body, never folds it

            float t = Time.time;
            float breathe = Mathf.Sin(t * 2.3f) * 0.012f;
            float sway = Mathf.Sin(t * 1.1f) * 0.02f * (1f - walkBlend) * (1f - airBlend);
            // Walk: dip at mid-stance. Run: lowest at mid-stance, highest in the flight between steps.
            float walkBob = -Mathf.Abs(Mathf.Sin(gaitPhase)) * 0.06f * walkBlend;
            float halfCycle = Mathf.Repeat(gaitPhase / Mathf.PI, 1f);
            float runBob = -Mathf.Cos((halfCycle - runStanceFraction) * Mathf.PI * 2f) * runBobDepth - runCrouch;
            float bob = Mathf.Lerp(walkBob, runBob, runBlend);
            spinScale = spin >= 0f ? Mathf.Cos(spin * Mathf.PI * 2f) : 1f;
            spinPivotX = hip.x;
            float spinCrouch = spin >= 0f ? Mathf.Sin(spin * Mathf.PI) * 0.12f : 0f;
            float crouch = -0.025f * (1f - walkBlend) * (1f - airBlend) - spinCrouch;
            Vector2 pelvis = hip + new Vector2(sway, bob + crouch - landDip);

            // Spine: lean into motion, away from hits, slightly forward in stance.
            float targetLean = Mathf.Clamp(-velocity.x * 1.8f, -12f, 12f) - moveDir * runLeanDegrees * runBlend - flinch.x * 35f - facing * (2f + landDip * 40f) * (1f - airBlend);
            lean = Mathf.Clamp(Mathf.Lerp(lean, -targetLean, 1f - Mathf.Exp(-10f * dt)), -18f, 18f);
            // Kicking: the torso tips back to counterbalance the raised leg.
            Vector2 up = Rotate(Vector2.up, -(lean - facing * KickReach(fighter.KickProgress) * 24f));
            Vector2 chest = pelvis + up * Waist + right * 0.02f + flinch * 0.4f + Vector2.up * breathe;
            Vector2 neckBase = chest + Rotate(up, -lean * 0.3f) * Chest + flinch * 0.6f;

            // Head looks a little towards the aim.
            Vector2 look = fighter.aimPoint - neckBase;
            float tilt = Mathf.Clamp(Mathf.Atan2(look.y, Mathf.Abs(look.x) + 0.01f) * Mathf.Rad2Deg * 0.25f, -14f, 14f) * facing;
            Vector2 headUp = Rotate(up, tilt);
            Vector2 headCenter = neckBase + headUp * (Neck + HeadRadius) + flinch * 0.3f;

            Vector2 shoulderBase = neckBase - up * 0.06f;
            Vector2 frontShoulder = shoulderBase + right * 0.035f;
            Vector2 backShoulder = shoulderBase - right * 0.035f;

            spine.Set(S(pelvis), S(chest), S(neckBase), S(neckBase + headUp * Neck));
            DrawBelt(pelvis, up, true);
            DrawHead(S(headCenter), headUp, facing, dt);
            DrawLegs(hip, pelvis, velocity, facing, grounded);
            DrawArms(frontShoulder, backShoulder, facing, dt);
        }

        private void DrawLegs(Vector2 hip, Vector2 pelvis, Vector2 velocity, int facing, bool grounded)
        {
            Vector2 right = new Vector2(facing, 0f);
            float groundY = hip.y - Fighter.HipHeight;
            float slope = 0f;
            RaycastHit2D groundHit = Physics2D.Raycast(hip, Vector2.down, 2.5f, groundMask);
            // Ignore a surface above our feet (the underside of a castle stair we are walking beneath).
            if (groundHit.collider != null && groundHit.point.y <= groundY + 0.35f)
            {
                groundY = groundHit.point.y;
                if (groundHit.normal.y > 0.1f)
                {
                    slope = -groundHit.normal.x / groundHit.normal.y; // feet step up or down the stairs
                }
            }

            float moveDir = Mathf.Abs(velocity.x) > 0.05f ? Mathf.Sign(velocity.x) : facing;
            for (int i = 0; i < 2; i++)
            {
                bool isFront = i == 0;
                Vector2 hipJoint = pelvis + right * (isFront ? 0.05f : -0.04f);

                // Fencing stance: lead foot forward, rear foot back and turned.
                Vector2 idle = new Vector2(hip.x + (isFront ? 0.22f : -0.17f) * facing, groundY + AnkleHeight);
                idle.y += (idle.x - hip.x) * slope;

                float phase = gaitPhase + i * Mathf.PI;
                // Walk: cosine slide with a half-sine lift. Run: planted stance, heel kick, knee drive.
                float walkLift = Mathf.Max(0f, -Mathf.Sin(phase));
                Vector3 run = RunFoot(Mathf.Repeat(phase / (Mathf.PI * 2f), 1f), out bool runPlanted);
                float along = Mathf.Lerp(Mathf.Cos(phase) * currentStride * 0.5f, run.x, runBlend);
                float height = Mathf.Lerp(walkLift * 0.2f, run.y, runBlend);
                float footAngle = Mathf.Lerp(walkLift * 35f, run.z, runBlend);
                Vector2 walk = new Vector2(hip.x + along * moveDir, groundY + AnkleHeight + height);
                walk.y += (walk.x - hip.x) * slope;

                Vector2 air = hipJoint + new Vector2((isFront ? 0.24f : -0.12f) * facing, isFront ? -0.58f : -0.74f);
                Vector2 ankle = Vector2.Lerp(Vector2.Lerp(idle, walk, walkBlend), air, airBlend);
                float kick = fighter.KickProgress;
                if (isFront && kick >= 0f)
                {
                    ankle = KickAnkle(kick, ankle, hipJoint, facing);
                }
                Vector2 knee = SolveJoint(hipJoint, ref ankle, Thigh, Shin, facing);

                // Toes point forward; they drop while the foot is lifted or in the air.
                float toeDrop = (footAngle * walkBlend + airBlend * 50f) * Mathf.Deg2Rad;
                Vector2 toe = ankle + new Vector2(Mathf.Cos(toeDrop) * facing, -Mathf.Sin(toeDrop)) * FootLength;
                if (isFront && kick >= 0f)
                {
                    // Foot flexes up so the sole/heel leads the strike.
                    Vector2 sole = Rotate(fighter.KickDirection, 80f * facing);
                    toe = Vector2.Lerp(toe, ankle + sole * FootLength, KickReach(kick));
                }

                bool lifted = (runBlend > 0.5f ? !runPlanted : walkLift > 0.05f) && walkBlend > 0.3f;
                if (footLifted[i] && !lifted && grounded && Fx.Instance != null)
                {
                    Fx.Instance.Step(ankle, walkBlend);
                }

                footLifted[i] = lifted;
                (isFront ? frontLeg : backLeg).Set(S(hipJoint), S(knee), S(ankle), S(toe));
            }
        }

        /// <summary>
        /// One foot through a run cycle (<paramref name="cycle"/> 0..1, 0 = landing): x ahead of the
        /// hip along the motion, y ankle height off the floor, z toe drop in degrees. On the floor
        /// the foot slides back at exactly the body speed and the heel peels up for toe-off; in the
        /// air it kicks up behind, drives forward under a high knee, reaches and lands.
        /// </summary>
        private Vector3 RunFoot(float cycle, out bool planted)
        {
            float travel = runStanceFraction * 2f * currentStride; // ground covered while this foot is down
            float land = travel * runLandAhead;
            float toeOff = land - travel;
            float peelLift = Mathf.Sin(runToeOffAngle * Mathf.Deg2Rad) * FootLength; // ankle height that keeps the toe down

            planted = cycle < runStanceFraction;
            if (planted)
            {
                float t = cycle / runStanceFraction;
                float peel = Mathf.SmoothStep(0f, 1f, (t - (1f - runHeelPeelShare)) / runHeelPeelShare);
                float angle = peel * runToeOffAngle;
                return new Vector3(Mathf.Lerp(land, toeOff, t), Mathf.Sin(angle * Mathf.Deg2Rad) * FootLength, angle);
            }

            RunSwingKeys[0] = new Vector4(0f, toeOff, peelLift, runToeOffAngle);
            RunSwingKeys[1] = new Vector4(0.3f, -runHeelKickBack, runHeelKickHeight, 70f);
            RunSwingKeys[2] = new Vector4(0.62f, runKneeDriveForward, runKneeDriveHeight, 35f);
            RunSwingKeys[3] = new Vector4(0.85f, land + runReachPast, runReachHeight, -5f);
            RunSwingKeys[4] = new Vector4(1f, land, 0f, 0f);
            return Spline(RunSwingKeys, (cycle - runStanceFraction) / (1f - runStanceFraction));
        }

        // Swing keys: (time, x, y, toe angle). Times and toe angles shape the swing; distances are fields.
        private static readonly Vector4[] RunSwingKeys = new Vector4[5];

        /// <summary>Smooth curve through timed keys (x = time, yzw = value), Catmull-Rom style tangents.</summary>
        private static Vector3 Spline(Vector4[] keys, float time)
        {
            int k = 0;
            while (k < keys.Length - 2 && time > keys[k + 1].x)
            {
                k++;
            }

            Vector4 a = keys[k], b = keys[k + 1];
            float span = b.x - a.x;
            float u = Mathf.Clamp01((time - a.x) / span);
            Vector3 Tangent(int i)
            {
                Vector4 before = keys[Mathf.Max(i - 1, 0)], after = keys[Mathf.Min(i + 1, keys.Length - 1)];
                return new Vector3(after.y - before.y, after.z - before.z, after.w - before.w) / (after.x - before.x) * span;
            }

            var p0 = new Vector3(a.y, a.z, a.w);
            var p1 = new Vector3(b.y, b.z, b.w);
            float u2 = u * u, u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * p0 + (u3 - 2f * u2 + u) * Tangent(k) + (-2f * u3 + 3f * u2) * p1 + (u3 - u2) * Tangent(k + 1);
        }

        private void DrawArms(Vector2 frontShoulder, Vector2 backShoulder, int facing, float dt)
        {
            Sword sword = fighter.sword;
            bool held = sword != null && sword.IsHeld;

            // Sword hand rides on the hilt (interpolated transform = what is drawn). If the drawn
            // shoulder is a bit off, stretch the arm slightly rather than leave a gap.
            // Running, the arms pump opposite the legs: the far arm with the near leg, and back.
            float pump = Mathf.Cos((Mathf.Repeat(gaitPhase / (Mathf.PI * 2f), 1f) + armPumpLead) * Mathf.PI * 2f);
            Vector2 unarmed = Vector2.Lerp(frontShoulder + new Vector2(facing * 0.3f, -0.6f), PumpHand(frontShoulder, -pump, facing), runBlend);
            Vector2 grip = held ? (Vector2)sword.transform.position : unarmed;
            if (!held)
            {
                // Unarmed jab: the fist shoots out towards the aim and comes back.
                Vector2 jab = frontShoulder + fighter.PunchDirection * (UpperArm + Forearm) * 0.97f;
                grip = Vector2.Lerp(grip, jab, fighter.PunchExtension);
            }

            if (held)
            {
                // Running, the drawn torso leans forward onto the sword and cramps the arm; settle the
                // sword shoulder back so the arm extends, but never past swordArmMaxExtension. The
                // sword itself (physics, hitbox) is untouched.
                float slack = (UpperArm + Forearm) * swordArmMaxExtension - Vector2.Distance(frontShoulder, grip);
                frontShoulder -= new Vector2(facing, 0f) * Mathf.Clamp(swordRunShoulderPullback * runBlend, 0f, Mathf.Max(0f, slack));
            }

            Vector2 hand = grip;
            // Shoulders turn with the spinning body; the sword hand stays on the real (physics) grip.
            frontShoulder = S(frontShoulder);
            float stretch = Mathf.Max(1f, Vector2.Distance(frontShoulder, hand) / (UpperArm + Forearm - 0.002f));
            Vector2 elbow = SolveJoint(frontShoulder, ref hand, UpperArm * stretch, Forearm * stretch, -facing);
            frontArm.Set(frontShoulder, elbow, hand);
            frontHand.Place(hand);

            // Off hand: free (guard / balance) or on the pommel end of the hilt for a two-handed grip.
            float swing = Mathf.Cos(gaitPhase) * 0.2f * walkBlend;
            Vector2 guard = Vector2.Lerp(backShoulder + new Vector2(-facing * (0.22f + swing), -0.55f), PumpHand(backShoulder, pump, facing), runBlend);
            Vector2 flail = backShoulder + new Vector2(-facing * 0.45f, 0.2f);
            float follow = Mathf.Lerp(14f, runArmFollowRate, runBlend);
            freeBackHand = Vector2.Lerp(freeBackHand, S(Vector2.Lerp(guard, flail, airBlend)), 1f - Mathf.Exp(-follow * dt));
            backShoulder = S(backShoulder);

            Vector2 pommel = grip - (Vector2)(held ? sword.transform.right : Vector3.right) * 0.15f;
            // At a run the off hand lets go of the pommel and pumps.
            bool canTwoHand = held && !fighter.backGuard && airBlend < 0.5f && sword.SpinProgress < 0f && runBlend < 0.5f
                              && Vector2.Distance(backShoulder, pommel) < (UpperArm + Forearm) * 0.98f;
            twoHand = Mathf.MoveTowards(twoHand, canTwoHand ? 1f : 0f, dt * 6f);

            Vector2 offHand = Vector2.Lerp(freeBackHand, pommel, twoHand);
            Vector2 offElbow = SolveJoint(backShoulder, ref offHand, UpperArm, Forearm, -facing);
            backArm.Set(backShoulder, offElbow, offHand);
            backHand.Place(offHand);
        }

        /// <summary>Pumping hand: upper arm swung <paramref name="swing"/> (-1 back .. 1 forward), elbow bent.</summary>
        private Vector2 PumpHand(Vector2 shoulder, float swing, int facing)
        {
            float upper = swing * armPumpAngle * facing;
            return shoulder + Rotate(Vector2.down, upper) * UpperArm + Rotate(Vector2.down, upper + armPumpElbowBend * facing) * Forearm;
        }

        private void DrawHead(Vector2 center, Vector2 headUp, int facing, float dt)
        {
            if (headWeight < 1f)
            {
                center = Vector2.Lerp(headFrom, center, headWeight);
                headUp = ((Vector2)Vector3.Slerp(headUpFrom, headUp, headWeight)).normalized;
            }

            lastHeadCenter = center;
            lastHeadUp = headUp;
            head.Place(center);
            float angle = Mathf.Atan2(headUp.y, headUp.x) * Mathf.Rad2Deg - 90f;
            head.Fill.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            head.Outline.transform.rotation = head.Fill.transform.rotation;

            Vector2 headRight = new Vector2(headUp.y, -headUp.x); // headUp rotated -90°
            headShine.transform.position = center + headUp * 0.12f - headRight * 0.05f;
            headShine.transform.rotation = head.Fill.transform.rotation;
            Vector2 bandCenter = center + headUp * 0.06f;
            float halfWidth = HeadRadius * 1.02f * Mathf.Max(Mathf.Abs(spinScale), 0.15f);
            band.Set(bandCenter - headRight * halfWidth, bandCenter + headRight * halfWidth);

            Vector2 eyeCenter = center - headUp * 0.015f;
            // Mirrors to the far side (and hides behind the head) while the back is turned.
            float side = facing * spinScale;
            eye.Fill.enabled = spinScale > 0.2f;
            eye.Set(eyeCenter + headRight * side * 0.05f, eyeCenter + headRight * side * 0.15f);

            SimulateRibbon(bandCenter - headRight * side * HeadRadius, dt);
        }

        /// <summary>Verlet chain for the hachimaki tails: gravity plus a gusty wind from the right.</summary>
        private void SimulateRibbon(Vector2 knot, float dt)
        {
            dt = Mathf.Min(dt, 1f / 30f);
            float gust = -1.6f - Mathf.PerlinNoise(Time.time * 0.7f, 3.1f) * 2.2f;
            Vector2 acceleration = new Vector2(gust, -3.5f + Mathf.Sin(Time.time * 9f) * 1.2f);
            ribbonPoints[0] = knot;
            for (int i = 1; i < ribbonPoints.Length; i++)
            {
                Vector2 current = ribbonPoints[i];
                ribbonPoints[i] += (current - ribbonPrevious[i]) * 0.9f + acceleration * dt * dt;
                ribbonPrevious[i] = current;
            }

            for (int iteration = 0; iteration < 3; iteration++)
            {
                for (int i = 1; i < ribbonPoints.Length; i++)
                {
                    Vector2 delta = ribbonPoints[i] - ribbonPoints[i - 1];
                    float distance = Mathf.Max(delta.magnitude, 0.0001f);
                    ribbonPoints[i] = ribbonPoints[i - 1] + delta / distance * RibbonSegment;
                }
            }

            ribbon.Set(ribbonPoints);
        }

        /// <summary>
        /// Forward roll in three beats: drop into a low crouch and tuck, turn one full revolution as
        /// a ball (eased: slow in, fast through the middle, slow out), then open back up into the
        /// crouch. Each joint lerps between a crouch pose (ground space) and a ball pose (rotating
        /// around the ball centre), so tucking and opening are continuous with the spin.
        /// </summary>
        private void DrawRoll(float progress, float dt)
        {
            spinScale = 1f;
            runBlend = 0f;
            int dir = fighter.RollDirection;
            Vector2 hip = transform.position;
            Vector2 ground = new Vector2(hip.x, hip.y - Fighter.HipHeight);
            Vector2 ballCenter = ground + Vector2.up * 0.5f; // ball radius ~0.5 (head), so it never sinks into the floor
            float tuck = Mathf.SmoothStep(0f, 1f, progress / 0.2f) * Mathf.SmoothStep(0f, 1f, (1f - progress) / 0.25f);
            float turn = progress * progress * (3f - 2f * progress);
            float angle = -dir * 360f * turn;

            // (cx, cy) = crouch pose above the ground point, (bx, by) = ball pose around its centre.
            Vector2 J(float cx, float cy, float bx, float by)
                => Vector2.Lerp(ground + new Vector2(cx * dir, cy), ballCenter + Rotate(new Vector2(bx * dir, by), angle), tuck);

            Vector2 pelvis = J(-0.05f, 0.62f, -0.12f, -0.12f);
            Vector2 chest = J(0.12f, 0.95f, -0.1f, 0.15f);
            Vector2 neckBase = J(0.27f, 1.13f, 0f, 0.24f);
            Vector2 crouchUp = new Vector2(0.45f * dir, 0.9f).normalized;
            Vector2 ballUp = Rotate(new Vector2(0.95f * dir, -0.32f).normalized, angle);
            Vector2 headUp = Vector2.Lerp(crouchUp, ballUp, tuck).normalized;

            spine.Set(pelvis, chest, neckBase, neckBase + headUp * Neck);
            DrawBelt(pelvis, (chest - pelvis).normalized, false);
            DrawHead(neckBase + headUp * (Neck + HeadRadius), headUp, dir, dt);

            // Crouch: lead knee forward, rear knee low; ball: knees pulled to the chest.
            frontLeg.Set(pelvis, J(0.33f, 0.42f, 0.28f, 0.13f), J(0.12f, AnkleHeight, 0.06f, -0.26f), J(0.29f, 0.03f, 0.2f, -0.3f));
            backLeg.Set(pelvis, J(0.15f, 0.25f, 0.24f, 0.06f), J(-0.27f, 0.08f, 0f, -0.3f), J(-0.1f, 0.03f, 0.14f, -0.36f));

            // Arms brace forward in the crouch and hug the shins in the ball; the sword hand stays
            // on the hilt while the blade is held.
            Sword sword = fighter.sword;
            Vector2 frontShoulder = J(0.24f, 1.07f, 0.02f, 0.2f);
            Vector2 hand = sword != null && sword.IsHeld ? (Vector2)sword.transform.position : J(0.55f, 0.5f, 0.18f, -0.14f);
            float stretch = Mathf.Max(1f, Vector2.Distance(frontShoulder, hand) / (UpperArm + Forearm - 0.002f));
            Vector2 elbow = SolveJoint(frontShoulder, ref hand, UpperArm * stretch, Forearm * stretch, -dir);
            frontArm.Set(frontShoulder, elbow, hand);
            frontHand.Place(hand);

            Vector2 backShoulder = J(0.2f, 1.07f, -0.02f, 0.2f);
            Vector2 offHand = J(0.42f, 0.42f, 0.12f, -0.2f);
            Vector2 offElbow = SolveJoint(backShoulder, ref offHand, UpperArm, Forearm, -dir);
            backArm.Set(backShoulder, offElbow, offHand);
            backHand.Place(offHand);
            freeBackHand = offHand;
            twoHand = 0f;

            // Dust as the feet push off, the back meets the floor and the feet land again.
            while (rollDust < RollDustAt.Length && progress >= RollDustAt[rollDust])
            {
                rollDust++;
                if (Fx.Instance != null)
                {
                    Fx.Instance.Step(ground, 1f);
                }
            }
        }

        private void DrawDead(float dt)
        {
            spinScale = 1f;
            runBlend = 0f;
            Vector2 hip = transform.position;
            Vector2 up = transform.up;
            Vector2 side = transform.right;
            Vector2 chest = hip + up * Waist;
            Vector2 neckBase = chest + up * Chest;
            Vector2 shoulder = neckBase - up * 0.06f;
            // Limbs lie along the body, angled away from the floor, so they never sink into it.
            Vector2 limp = -up;
            Vector2 away = Vector2.Dot(side, Vector2.up) >= 0f ? side : -side;

            spine.Set(hip, chest, neckBase, neckBase + up * Neck);
            DrawBelt(hip, up, false);
            Vector2 frontAnkle = hip - up * (Thigh + Shin) + away * 0.04f;
            Vector2 backAnkle = hip - up * (Thigh + Shin) + away * 0.14f;
            frontLeg.Set(hip, hip - up * Thigh + away * 0.1f, frontAnkle, frontAnkle + away * FootLength);
            backLeg.Set(hip, hip - up * Thigh + away * 0.04f, backAnkle, backAnkle + away * FootLength);

            Vector2 frontEnd = shoulder + (limp + away * 0.15f).normalized * (UpperArm + Forearm);
            Vector2 backEnd = shoulder + (limp + away * 0.05f).normalized * (UpperArm + Forearm);
            frontArm.Set(shoulder, shoulder + (limp + away * 0.35f).normalized * UpperArm, frontEnd);
            backArm.Set(shoulder, shoulder + (limp + away * 0.2f).normalized * UpperArm, backEnd);
            frontHand.Place(frontEnd);
            backHand.Place(backEnd);

            DrawHead(neckBase + up * (Neck + HeadRadius), up, fighter.Facing, dt);
        }

        private void DrawBelt(Vector2 pelvis, Vector2 up, bool spun)
        {
            Vector2 c = pelvis + up * 0.08f;
            Vector2 half = new Vector2(up.y, -up.x) * 0.1f;
            belt.Set(spun ? S(c - half) : c - half, spun ? S(c + half) : c + half);
        }

        // ------------------------------------------------------------------ math

        private Vector2 S(Vector2 p) => new Vector2(spinPivotX + (p.x - spinPivotX) * spinScale, p.y);

        /// <summary>
        /// Two-bone IK. Returns the middle joint; clamps <paramref name="end"/> to the reachable range.
        /// <paramref name="bendSide"/> picks which side of the root→end line the joint bends to.
        /// </summary>
        /// <summary>
        /// Front-leg ankle through a kick: ease the knee up to the chest (chamber), snap the leg
        /// straight out fast, hold it there, then ease back to the normal stance.
        /// </summary>
        private Vector2 KickAnkle(float progress, Vector2 stance, Vector2 hipJoint, int facing)
        {
            Vector2 chamber = hipJoint + new Vector2(0.3f * facing, -0.28f);
            Vector2 extended = hipJoint + fighter.KickDirection * (Thigh + Shin) * 0.98f;
            if (progress < Fighter.KickSnapStart)
            {
                return Vector2.Lerp(stance, chamber, Mathf.SmoothStep(0f, 1f, progress / Fighter.KickSnapStart));
            }

            if (progress < Fighter.KickHoldStart)
            {
                float t = (progress - Fighter.KickSnapStart) / (Fighter.KickHoldStart - Fighter.KickSnapStart);
                return Vector2.Lerp(chamber, extended, 1f - (1f - t) * (1f - t) * (1f - t));
            }

            if (progress < Fighter.KickRecoverStart)
            {
                return extended;
            }

            float back = (progress - Fighter.KickRecoverStart) / (1f - Fighter.KickRecoverStart);
            return Vector2.Lerp(extended, stance, Mathf.SmoothStep(0f, 1f, back));
        }

        /// <summary>How far into the kick pose (0→1→0); used for the lean and the foot angle.</summary>
        private static float KickReach(float progress)
        {
            if (progress < 0f)
            {
                return 0f;
            }

            if (progress < Fighter.KickHoldStart)
            {
                return Mathf.SmoothStep(0f, 1f, progress / Fighter.KickHoldStart);
            }

            return progress < Fighter.KickRecoverStart ? 1f : Mathf.SmoothStep(1f, 0f, (progress - Fighter.KickRecoverStart) / (1f - Fighter.KickRecoverStart));
        }

        public static Vector2 SolveJoint(Vector2 root, ref Vector2 end, float upper, float lower, float bendSide)
        {
            Vector2 delta = end - root;
            float distance = Mathf.Clamp(delta.magnitude, 0.001f, upper + lower - 0.001f);
            Vector2 direction = delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector2.down;
            end = root + direction * distance;

            float along = (upper * upper - lower * lower + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector2 perpendicular = new Vector2(-direction.y, direction.x) * Mathf.Sign(bendSide);
            return root + direction * along + perpendicular * height;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r);
            float s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
