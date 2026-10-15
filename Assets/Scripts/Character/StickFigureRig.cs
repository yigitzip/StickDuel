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
        private const float Thigh = 0.5f;
        private const float Shin = 0.47f;
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

        private Line spine, frontLeg, backLeg, frontArm, backArm, band, eye, ribbon;
        private Dot head, frontHand, backHand;
        private Color accent;

        // Spin attack: the drawn body is squashed horizontally by cos(turn) around the hip, which
        // reads as the fighter rotating about their vertical axis (front → edge-on → back → front).
        private float spinScale = 1f;
        private float spinPivotX;

        private float gaitPhase, walkBlend, airBlend, lean, twoHand, landDip, lastVerticalSpeed, flashTimer;
        private bool wasGrounded = true;
        private Vector2 flinch, flinchVelocity, freeBackHand;
        private readonly bool[] footLifted = new bool[2];
        private readonly Vector2[] ribbonPoints = new Vector2[5];
        private readonly Vector2[] ribbonPrevious = new Vector2[5];

        // ------------------------------------------------------------------ drawing primitives

        private sealed class Line
        {
            public LineRenderer Fill;
            public LineRenderer Outline;
            private readonly Vector3[] points;

            public Line(int count) => points = new Vector3[count];

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
                Fill.positionCount = points.Length;
                Fill.SetPositions(points);
                if (Outline != null)
                {
                    Outline.positionCount = points.Length;
                    Outline.SetPositions(points);
                }
            }

            public void SetColor(Color color)
            {
                Fill.startColor = color;
                Fill.endColor = color;
            }
        }

        private sealed class Dot
        {
            public SpriteRenderer Fill;
            public SpriteRenderer Outline;

            public void Place(Vector2 position)
            {
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
            spine = CreateLine("Spine", 4, 11, 0.2f, new AnimationCurve(
                new Keyframe(0f, 0.85f), new Keyframe(0.5f, 1.1f), new Keyframe(0.82f, 0.75f), new Keyframe(1f, 0.45f)), fighter.color, true);
            frontLeg = CreateLine("FrontLeg", 4, 12, 0.15f, Taper(1.1f, 0.75f, 0.65f), fighter.color, true);
            frontArm = CreateLine("FrontArm", 3, 15, 0.12f, Taper(1.1f, 0.75f), fighter.color, true);
            band = CreateLine("Hachimaki", 2, 14, 0.07f, Taper(1f, 1f), accent, false);
            eye = CreateLine("Eye", 2, 14, 0.035f, Taper(1f, 0.6f), outlineColor, false);

            head = CreateDot("Head", 13, new Vector2(HeadRadius * 2f, HeadRadius * 2.15f));
            frontHand = CreateDot("FrontHand", 16, Vector2.one * HandRadius * 2f);
            backHand = CreateDot("BackHand", 16, Vector2.one * HandRadius * 2f);
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
            walkBlend = airBlend = lean = landDip = twoHand = 0f;
            flinch = flinchVelocity = Vector2.zero;
            flashTimer = 0f;
            wasGrounded = true;
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

            if (fighter.IsDead)
            {
                DrawDead(dt);
            }
            else
            {
                DrawAlive(dt);
            }
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
            gaitPhase += Mathf.Abs(velocity.x) * dt / strideLength * Mathf.PI;

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
            float bob = -Mathf.Abs(Mathf.Sin(gaitPhase)) * 0.06f * walkBlend;
            float spin = fighter.sword != null ? fighter.sword.SpinProgress : -1f;
            spinScale = spin >= 0f ? Mathf.Cos(spin * Mathf.PI * 2f) : 1f;
            spinPivotX = hip.x;
            float spinCrouch = spin >= 0f ? Mathf.Sin(spin * Mathf.PI) * 0.12f : 0f;
            float crouch = -0.06f * (1f - walkBlend) * (1f - airBlend) - spinCrouch;
            Vector2 pelvis = hip + new Vector2(sway, bob + crouch - landDip);

            // Spine: lean into motion, away from hits, slightly forward in stance.
            float targetLean = Mathf.Clamp(-velocity.x * 1.8f, -12f, 12f) - flinch.x * 35f - facing * (5f + landDip * 40f) * (1f - airBlend);
            lean = Mathf.Clamp(Mathf.Lerp(lean, -targetLean, 1f - Mathf.Exp(-10f * dt)), -18f, 18f);
            Vector2 up = Rotate(Vector2.up, -lean);
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
            DrawHead(S(headCenter), headUp, facing, dt);
            DrawLegs(hip, pelvis, velocity, facing, grounded);
            DrawArms(frontShoulder, backShoulder, facing, dt);
        }

        private void DrawLegs(Vector2 hip, Vector2 pelvis, Vector2 velocity, int facing, bool grounded)
        {
            Vector2 right = new Vector2(facing, 0f);
            float groundY = hip.y - Fighter.HipHeight;
            RaycastHit2D groundHit = Physics2D.Raycast(hip, Vector2.down, 2.5f, groundMask);
            if (groundHit.collider != null)
            {
                groundY = groundHit.point.y;
            }

            float moveDir = Mathf.Abs(velocity.x) > 0.05f ? Mathf.Sign(velocity.x) : facing;
            for (int i = 0; i < 2; i++)
            {
                bool isFront = i == 0;
                Vector2 hipJoint = pelvis + right * (isFront ? 0.05f : -0.04f);

                // Fencing stance: lead foot forward, rear foot back and turned.
                Vector2 idle = new Vector2(hip.x + (isFront ? 0.25f : -0.2f) * facing, groundY + AnkleHeight);

                float phase = gaitPhase + i * Mathf.PI;
                float lift = Mathf.Max(0f, -Mathf.Sin(phase));
                Vector2 walk = new Vector2(hip.x + Mathf.Cos(phase) * strideLength * 0.5f * moveDir, groundY + AnkleHeight + lift * 0.2f);

                Vector2 air = hipJoint + new Vector2((isFront ? 0.24f : -0.12f) * facing, isFront ? -0.58f : -0.74f);
                Vector2 ankle = Vector2.Lerp(Vector2.Lerp(idle, walk, walkBlend), air, airBlend);
                Vector2 knee = SolveJoint(hipJoint, ref ankle, Thigh, Shin, facing);

                // Toes point forward; they drop while the foot is lifted or in the air.
                float toeDrop = (lift * walkBlend * 35f + airBlend * 50f) * Mathf.Deg2Rad;
                Vector2 toe = ankle + new Vector2(Mathf.Cos(toeDrop) * facing, -Mathf.Sin(toeDrop)) * FootLength;

                bool lifted = lift > 0.05f && walkBlend > 0.3f;
                if (footLifted[i] && !lifted && grounded && Fx.Instance != null)
                {
                    Fx.Instance.Step(ankle, walkBlend);
                }

                footLifted[i] = lifted;
                (isFront ? frontLeg : backLeg).Set(S(hipJoint), S(knee), S(ankle), S(toe));
            }
        }

        private void DrawArms(Vector2 frontShoulder, Vector2 backShoulder, int facing, float dt)
        {
            Sword sword = fighter.sword;
            bool held = sword != null && sword.IsHeld;

            // Sword hand rides on the hilt (interpolated transform = what is drawn). If the drawn
            // shoulder is a bit off, stretch the arm slightly rather than leave a gap.
            Vector2 grip = held ? (Vector2)sword.transform.position : frontShoulder + new Vector2(facing * 0.3f, -0.6f);
            Vector2 hand = grip;
            // Shoulders turn with the spinning body; the sword hand stays on the real (physics) grip.
            frontShoulder = S(frontShoulder);
            float stretch = Mathf.Max(1f, Vector2.Distance(frontShoulder, hand) / (UpperArm + Forearm - 0.002f));
            Vector2 elbow = SolveJoint(frontShoulder, ref hand, UpperArm * stretch, Forearm * stretch, -facing);
            frontArm.Set(frontShoulder, elbow, hand);
            frontHand.Place(hand);

            // Off hand: free (guard / balance) or on the pommel end of the hilt for a two-handed grip.
            float swing = Mathf.Cos(gaitPhase) * 0.2f * walkBlend;
            Vector2 guard = backShoulder + new Vector2(-facing * (0.22f + swing), -0.55f);
            Vector2 flail = backShoulder + new Vector2(-facing * 0.45f, 0.2f);
            freeBackHand = Vector2.Lerp(freeBackHand, S(Vector2.Lerp(guard, flail, airBlend)), 1f - Mathf.Exp(-14f * dt));
            backShoulder = S(backShoulder);

            Vector2 pommel = grip - (Vector2)(held ? sword.transform.right : Vector3.right) * 0.15f;
            bool canTwoHand = held && !fighter.backGuard && airBlend < 0.5f && sword.SpinProgress < 0f
                              && Vector2.Distance(backShoulder, pommel) < (UpperArm + Forearm) * 0.98f;
            twoHand = Mathf.MoveTowards(twoHand, canTwoHand ? 1f : 0f, dt * 6f);

            Vector2 offHand = Vector2.Lerp(freeBackHand, pommel, twoHand);
            Vector2 offElbow = SolveJoint(backShoulder, ref offHand, UpperArm, Forearm, -facing);
            backArm.Set(backShoulder, offElbow, offHand);
            backHand.Place(offHand);
        }

        private void DrawHead(Vector2 center, Vector2 headUp, int facing, float dt)
        {
            head.Place(center);
            float angle = Mathf.Atan2(headUp.y, headUp.x) * Mathf.Rad2Deg - 90f;
            head.Fill.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            head.Outline.transform.rotation = head.Fill.transform.rotation;

            Vector2 headRight = new Vector2(headUp.y, -headUp.x); // headUp rotated -90°
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

        private void DrawDead(float dt)
        {
            spinScale = 1f;
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

        // ------------------------------------------------------------------ math

        private Vector2 S(Vector2 p) => new Vector2(spinPivotX + (p.x - spinPivotX) * spinScale, p.y);

        /// <summary>
        /// Two-bone IK. Returns the middle joint; clamps <paramref name="end"/> to the reachable range.
        /// <paramref name="bendSide"/> picks which side of the root→end line the joint bends to.
        /// </summary>
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
