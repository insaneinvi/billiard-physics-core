using NUnit.Framework;
using UnityEngine;
using BilliardPhysics.AimAssist;

namespace BilliardPhysics.Tests
{
    /// <summary>
    /// EditMode unit tests for the pure-geometry helpers in <see cref="AimAssistRenderer"/>.
    ///
    /// <para>Tests cover:</para>
    /// <list type="number">
    ///   <item><see cref="AimAssistRenderer.SweptPointVsCircle"/>: head-on, miss, tangent,
    ///     already-overlapping, already-separating.</item>
    ///   <item><see cref="AimAssistRenderer.SweptCircleVsSegment"/>: head-on face,
    ///     approach from opposite side, endpoint corner, parallel miss.</item>
    /// </list>
    /// </summary>
    public class AimAssistGeometryTests
    {
        private const float Eps = 1e-4f;   // float tolerance for distance comparisons

        // ─────────────────────────────────────────────────────────────────────────
        // SweptPointVsCircle
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Head-on shot: origin is 3 units left of a circle whose radius is 1.
        /// Expected contact distance = 2 (origin moves 2 units to reach the circle edge).
        /// </summary>
        [Test]
        public void SweptPointVsCircle_HeadOn_ReturnsCorrectDistance()
        {
            bool hit = AimAssistRenderer.SweptPointVsCircle(
                origin:      new Vector2(-3f, 0f),
                dir:         new Vector2( 1f, 0f),
                target:      new Vector2( 0f, 0f),
                contactDist: 1f,
                distance:    out float d);

            Assert.IsTrue(hit, "Head-on shot must produce a hit.");
            Assert.AreEqual(2f, d, Eps, "Contact distance should be 2.");
        }

        /// <summary>
        /// Miss: the ray passes 2 units above the circle's centre; circle radius is 1.
        /// The closest point on the ray is at distance 2 > radius → no hit.
        /// </summary>
        [Test]
        public void SweptPointVsCircle_RayMisses_ReturnsFalse()
        {
            bool hit = AimAssistRenderer.SweptPointVsCircle(
                origin:      new Vector2(-5f, 2f),
                dir:         new Vector2( 1f, 0f),
                target:      new Vector2( 0f, 0f),
                contactDist: 1f,
                distance:    out float _);

            Assert.IsFalse(hit, "Ray that misses the circle must not report a hit.");
        }

        /// <summary>
        /// Already overlapping and approaching: origin is inside the circle.
        /// The hit must be returned at distance 0.
        /// </summary>
        [Test]
        public void SweptPointVsCircle_AlreadyInside_Approaching_HitAtZero()
        {
            bool hit = AimAssistRenderer.SweptPointVsCircle(
                origin:      new Vector2(-0.5f, 0f),   // 0.5 units inside circle of radius 1
                dir:         new Vector2( 1f,   0f),   // still approaching centre
                target:      new Vector2( 0f,   0f),
                contactDist: 1f,
                distance:    out float d);

            Assert.IsTrue(hit, "Inside + approaching must hit at t=0.");
            Assert.AreEqual(0f, d, Eps);
        }

        /// <summary>
        /// Already overlapping but separating: no hit expected.
        /// </summary>
        [Test]
        public void SweptPointVsCircle_AlreadyInside_Separating_ReturnsFalse()
        {
            bool hit = AimAssistRenderer.SweptPointVsCircle(
                origin:      new Vector2(-0.5f, 0f),   // inside the circle
                dir:         new Vector2(-1f,   0f),   // moving away from centre
                target:      new Vector2( 0f,   0f),
                contactDist: 1f,
                distance:    out float _);

            Assert.IsFalse(hit, "Already overlapping + separating must not report a hit.");
        }

        /// <summary>
        /// Moving exactly perpendicular to the line joining origin to target:
        /// the ray never reaches the circle → no hit.
        /// </summary>
        [Test]
        public void SweptPointVsCircle_PerpendicularDirection_ReturnsFalse()
        {
            bool hit = AimAssistRenderer.SweptPointVsCircle(
                origin:      new Vector2(-3f,  0f),
                dir:         new Vector2( 0f,  1f),   // moving upward, not toward circle
                target:      new Vector2( 0f,  0f),
                contactDist: 1f,
                distance:    out float _);

            Assert.IsFalse(hit, "Perpendicular direction that never reaches circle must miss.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SweptCircleVsSegment
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ball (radius 1) moving straight into a horizontal segment.
        /// Segment: y = 5, x from −3 to 3.  Ball moves from y = 0 upward.
        /// Expected: contact when ball centre is at y = 4  →  distance = 4.
        /// </summary>
        [Test]
        public void SweptCircleVsSegment_HeadOnFace_ReturnsCorrectDistance()
        {
            bool hit = AimAssistRenderer.SweptCircleVsSegment(
                origin: new Vector2(0f, 0f),
                dir:    new Vector2(0f, 1f),
                segA:   new Vector2(-3f, 5f),
                segB:   new Vector2( 3f, 5f),
                radius: 1f,
                distance: out float d);

            Assert.IsTrue(hit,  "Ball heading into face of horizontal segment must hit.");
            Assert.AreEqual(4f, d, Eps, "Contact must occur when ball is 1 unit from segment (distance = 4).");
        }

        /// <summary>
        /// Ball approaches the segment from the opposite (negative-normal) side.
        /// The reversed face test must still detect the collision.
        /// </summary>
        [Test]
        public void SweptCircleVsSegment_ApproachFromOtherSide_ReturnsHit()
        {
            // Segment at y = 0, ball starts at y = 6 and moves down.
            bool hit = AimAssistRenderer.SweptCircleVsSegment(
                origin: new Vector2(0f, 6f),
                dir:    new Vector2(0f, -1f),
                segA:   new Vector2(-3f, 0f),
                segB:   new Vector2( 3f, 0f),
                radius: 1f,
                distance: out float d);

            Assert.IsTrue(hit, "Ball approaching from below the segment normal must also hit.");
            Assert.AreEqual(5f, d, Eps, "Contact at distance 5 (ball starts 6 away, radius 1).");
        }

        /// <summary>
        /// Ball heading toward the endpoint of a segment — a corner contact.
        /// </summary>
        [Test]
        public void SweptCircleVsSegment_EndpointCorner_ReturnsHit()
        {
            // Horizontal segment at y = 0, x from 0 to 5.
            // Ball at (-4, 0) moving right: it hits the left endpoint (0, 0) at distance 4-1=3.
            bool hit = AimAssistRenderer.SweptCircleVsSegment(
                origin: new Vector2(-4f, 0f),
                dir:    new Vector2( 1f, 0f),
                segA:   new Vector2( 0f, 0f),
                segB:   new Vector2( 5f, 0f),
                radius: 1f,
                distance: out float d);

            Assert.IsTrue(hit, "Ball heading at the left endpoint must produce a corner hit.");
            Assert.AreEqual(3f, d, Eps, "Corner hit should occur when ball rim reaches the endpoint.");
        }

        /// <summary>
        /// Ball moving parallel to the segment and too far away: no hit.
        /// </summary>
        [Test]
        public void SweptCircleVsSegment_ParallelAndFarAway_ReturnsFalse()
        {
            // Segment at y = 5, ball moves along y = 0 rightward.
            bool hit = AimAssistRenderer.SweptCircleVsSegment(
                origin: new Vector2(-5f, 0f),
                dir:    new Vector2( 1f, 0f),
                segA:   new Vector2(-3f, 5f),
                segB:   new Vector2( 3f, 5f),
                radius: 1f,
                distance: out float _);

            Assert.IsFalse(hit, "Ball parallel and 5 units below segment (radius 1) must miss.");
        }

        /// <summary>
        /// Ball moving away from the face of a segment: no hit, even if it would
        /// intersect the segment's infinite-line extension behind the ball.
        /// </summary>
        [Test]
        public void SweptCircleVsSegment_MovingAway_ReturnsFalse()
        {
            // Segment at y = 3, ball at y = 1 moving downward (away from segment).
            bool hit = AimAssistRenderer.SweptCircleVsSegment(
                origin: new Vector2(0f,  1f),
                dir:    new Vector2(0f, -1f),
                segA:   new Vector2(-3f, 3f),
                segB:   new Vector2( 3f, 3f),
                radius: 0.5f,
                distance: out float _);

            Assert.IsFalse(hit, "Ball moving away from the segment must not register a hit.");
        }
    }
}
