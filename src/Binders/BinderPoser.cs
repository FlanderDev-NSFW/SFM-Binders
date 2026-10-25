using System;
using Binders.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Binders
{
    /// <summary>
    /// Folds her limbs on top of the game's crawl (LateUpdate: after the animator, before skinning) and places the sleeves on
    /// the drawn bones (onPreCull). Only bone directions are set, so the avatar keeps its own bone lengths.
    /// </summary>
    internal static class BinderPoser
    {
        public static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);

        // How far the elbow and knee joints sit up from the outside of the sleeves' cups (model metres, before scaling): the
        // cup's wall plus the joint's radius - until the sleeves are fitted to her (BinderFitter measures it).
        public const float ArmCup = 0.035f, LegCup = 0.05f;
        // How much of the animation's hand step the elbows take (the crawl's hands swing about +-10 cm), at most this part of the upper arm.
        private const float ArmSwing = 0.6f, MaxSwing = 0.45f;
        // How much of the chest's tilt the neck turns back, so she looks ahead and not at the floor.
        private const float HeadLift = 0.7f;

        /// <summary>One sleeve's place on her: the segment it holds (<c>Upper</c>, joint to its far end), the folded segment
        /// (<c>End</c>: the joint to its far end) and which side it folds to (forearms in front, shins behind).</summary>
        public struct SleeveBones
        {
            public BinderKind Kind;
            public int Side;
            public HumanBodyBones Upper, Joint, End;
            public bool FoldsForward;
        }

        /// <summary>The four sleeves, by index (<see cref="Index"/>): arm left, arm right, leg left, leg right.</summary>
        public static readonly SleeveBones[] Sleeves =
        {
            new SleeveBones { Kind = BinderKind.Arm, Side = 0, Upper = HumanBodyBones.LeftUpperArm, Joint = HumanBodyBones.LeftLowerArm, End = HumanBodyBones.LeftHand, FoldsForward = true },
            new SleeveBones { Kind = BinderKind.Arm, Side = 1, Upper = HumanBodyBones.RightUpperArm, Joint = HumanBodyBones.RightLowerArm, End = HumanBodyBones.RightHand, FoldsForward = true },
            new SleeveBones { Kind = BinderKind.Leg, Side = 0, Upper = HumanBodyBones.LeftUpperLeg, Joint = HumanBodyBones.LeftLowerLeg, End = HumanBodyBones.LeftFoot },
            new SleeveBones { Kind = BinderKind.Leg, Side = 1, Upper = HumanBodyBones.RightUpperLeg, Joint = HumanBodyBones.RightLowerLeg, End = HumanBodyBones.RightFoot },
        };

        public static int Index(BinderKind kind, int side) => (int)kind * 2 + side;

        /// <summary>
        /// The sleeves' sizes, per sleeve index: <c>Scale</c> along the sleeve (Model.ArmLength / LegLength of her upper arm /
        /// thigh), <c>Cup</c> how far the elbow / knee joint sits up from the outside of the cup (m), <c>Half</c> its widest reach
        /// to either side (m). Cup and Half come from the model until <see cref="BinderFitter"/> has fitted the sleeve to her.
        /// </summary>
        public sealed class Sizes
        {
            public readonly float[] Scale = new float[4], Cup = new float[4], Half = new float[4];
            public readonly bool[] Fitted = new bool[4];

            public void Unfit() => Array.Clear(Fitted, 0, Fitted.Length);
        }

        // Bone lengths as drawn. Between LateUpdate and the render the game changes the bones' local positions for her body
        // settings (thigh 40 cm as animated, 32 cm as drawn: the folded legs' knees floated 13 cm over the floor) [observed
        // 2026-10-25; the same in WallRestraint 2026-10-24]. The offset it adds (drawn - animated, per bone) is recorded at
        // render time; the pose is solved with animated + offset, then the animated values go back for the game to adjust.
        private static readonly HumanBodyBones[] Posed = {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
        private static readonly Vector3[] Animated = new Vector3[Posed.Length], Offset = new Vector3[Posed.Length];
        private static bool _haveAnimated, _haveOffset;

        /// <summary>LateUpdate, before the pose: remember the animator's local positions and give the bones their drawn lengths.</summary>
        public static void UseDrawnLengths(Animator anim)
        {
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t == null) continue;
                Animated[i] = t.localPosition;
                if (_haveOffset) t.localPosition = Animated[i] + Offset[i];
            }
            _haveAnimated = true;
        }

        /// <summary>LateUpdate, after the pose: the animator's local positions back (the game adds its offset again).</summary>
        public static void RestoreAnimatedLengths(Animator anim)
        {
            if (!_haveAnimated) return;
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t != null) t.localPosition = Animated[i];
            }
        }

        /// <summary>At render time (onPreCull): the offset the game added since LateUpdate.</summary>
        public static void RecordDrawn(Animator anim)
        {
            if (!_haveAnimated) return;
            for (int i = 0; i < Posed.Length; i++)
            {
                var t = anim.GetBoneTransform(Posed[i]);
                if (t != null) Offset[i] = t.localPosition - Animated[i];
            }
            _haveOffset = true;
        }

        public static void ForgetDrawn() => _haveAnimated = _haveOffset = false;

        private static Transform B(Animator anim, HumanBodyBones bone) => anim.GetBoneTransform(bone);

        /// <summary>Turn <paramref name="bone"/> so that <paramref name="child"/> lies in the direction of <paramref name="target"/>.</summary>
        internal static void Aim(Transform bone, Transform child, Vector3 target)
        {
            Vector3 cur = child.position - bone.position, want = target - bone.position;
            if (cur.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }

        /// <summary>The sleeves' lengths from her bones (drawn): the arm sleeve <paramref name="armLength"/> times her upper arm,
        /// the leg sleeve <paramref name="legLength"/> times her thigh; the model's cup and width for sleeves not fitted yet.</summary>
        public static void Scale(Animator anim, BinderModel model, Sizes s, float armLength, float legLength)
        {
            for (int i = 0; i < Sleeves.Length; i++)
            {
                var b = Sleeves[i];
                var part = model.Get(b.Kind, b.Side);
                float along = b.Kind == BinderKind.Arm ? armLength : legLength;
                s.Scale[i] = along * Vector3.Distance(B(anim, b.Upper).position, B(anim, b.Joint).position) / part.Length;
                if (s.Fitted[i]) continue;
                s.Cup[i] = (b.Kind == BinderKind.Arm ? ArmCup : LegCup) * s.Scale[i];
                s.Half[i] = part.HalfWidth() * s.Scale[i];
            }
        }

        /// <summary>A sleeve's axis and fold direction on the bones now (see <see cref="CrawlPose.Sleeve"/>).</summary>
        public static (NVector3 Joint, NVector3 Axis, NVector3 Fold) Frame(Animator anim, Transform root, int i)
        {
            var b = Sleeves[i];
            NVector3 fwd = N(root.forward), j = N(B(anim, b.Joint).position);
            var (axis, fold) = CrawlPose.Sleeve(j, N(B(anim, b.Upper).position), N(B(anim, b.End).position), b.FoldsForward ? fwd : -fwd);
            return (j, axis, fold);
        }

        private const float KneeRoom = 0.01f; // m between the leg sleeves
        public static float LastKneeSpread;  // diagnostics: m the knees were moved apart
        public static float LastChestDeg, LastHipDrop, LastElbowHeightL, LastElbowHeightR, LastKneeHeightL, LastKneeHeightR; // diagnostics

        /// <summary>The crawl on elbows and knees (see <see cref="CrawlPose"/>).</summary>
        public static void Apply(Animator anim, Transform root, Sizes s, float armGapDeg, float legGapDeg, float handBendDeg)
        {
            Transform spine = B(anim, HumanBodyBones.Spine), neck = B(anim, HumanBodyBones.Neck);
            Transform uaL = B(anim, HumanBodyBones.LeftUpperArm), uaR = B(anim, HumanBodyBones.RightUpperArm);
            Transform laL = B(anim, HumanBodyBones.LeftLowerArm), laR = B(anim, HumanBodyBones.RightLowerArm);
            Transform hdL = B(anim, HumanBodyBones.LeftHand), hdR = B(anim, HumanBodyBones.RightHand);
            Transform ulL = B(anim, HumanBodyBones.LeftUpperLeg), ulR = B(anim, HumanBodyBones.RightUpperLeg);
            Transform llL = B(anim, HumanBodyBones.LeftLowerLeg), llR = B(anim, HumanBodyBones.RightLowerLeg);
            Transform ftL = B(anim, HumanBodyBones.LeftFoot), ftR = B(anim, HumanBodyBones.RightFoot);
            Transform hips = B(anim, HumanBodyBones.Hips);
            if (hips == null || spine == null || neck == null || uaL == null || uaR == null || laL == null || laR == null || hdL == null || hdR == null
                || ulL == null || ulR == null || llL == null || llR == null || ftL == null || ftR == null) return;

            NVector3 up = N(root.up), fwd = N(root.forward), floor = N(root.position);

            // Hips down until the lower knee rests on the floor in its sleeve's cup (her thighs as drawn are shorter than the
            // crawl animation's, which leaves the knees in the air once the shins no longer lie on the floor).
            // Knees apart so the two leg sleeves fit side by side (her thick thighs fill them; together they cut into each other
            // between her knees) [observed 2026-10-25].
            NVector3 right = N(root.right);
            float apart = NVector3.Dot(N(llR.position) - N(llL.position), right), need = s.Half[2] + s.Half[3] + KneeRoom;
            LastKneeSpread = Math.Max(need - apart, 0f);
            if (LastKneeSpread > 0f)
            {
                Aim(ulL, llL, llL.position - root.right * (LastKneeSpread / 2f));
                Aim(ulR, llR, llR.position + root.right * (LastKneeSpread / 2f));
            }

            float kneeL = NVector3.Dot(N(llL.position) - floor, up) - s.Cup[2];
            float kneeR = NVector3.Dot(N(llR.position) - floor, up) - s.Cup[3];
            LastHipDrop = Math.Max(Math.Min(kneeL, kneeR), 0f);
            hips.position -= U(up) * LastHipDrop;
            float armL = Vector3.Distance(uaL.position, laL.position), armR = Vector3.Distance(uaR.position, laR.position);
            float elbowL = s.Cup[0], elbowR = s.Cup[1];

            // Elbow steps from the animation's hand steps, then the chest lowered so the elbows reach the floor.
            var stepL = CrawlPose.ElbowStep((N(hdL.position) - N(uaL.position)) * ArmSwing, up, armL, MaxSwing);
            var stepR = CrawlPose.ElbowStep((N(hdR.position) - N(uaR.position)) * ArmSwing, up, armR, MaxSwing);
            float shoulderHeight = ((elbowL + stepL.Drop) + (elbowR + stepR.Drop)) / 2f;
            NVector3 mid = (N(uaL.position) + N(uaR.position)) / 2f, pivot = N(spine.position);
            NVector3 lowered = CrawlPose.LowerShoulders(pivot, mid, floor, up, shoulderHeight);
            Quaternion tilt = Quaternion.FromToRotation(U(mid - pivot), U(lowered - pivot));
            spine.rotation = tilt * spine.rotation;
            neck.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(tilt), HeadLift) * neck.rotation;
            LastChestDeg = Quaternion.Angle(Quaternion.identity, tilt);

            // Upper arms down to the elbows' places; forearms folded up in front of them.
            NVector3 elbowAtL = N(uaL.position) + stepL.Offset - up * stepL.Drop;
            NVector3 elbowAtR = N(uaR.position) + stepR.Offset - up * stepR.Drop;
            Aim(uaL, laL, U(elbowAtL));
            Aim(uaR, laR, U(elbowAtR));
            float gapA = armGapDeg * (float)Math.PI / 180f, gapL = legGapDeg * (float)Math.PI / 180f;
            Aim(laL, hdL, laL.position + U(CrawlPose.Fold(N(uaL.position - laL.position), fwd, gapA)));
            Aim(laR, hdR, laR.position + U(CrawlPose.Fold(N(uaR.position - laR.position), fwd, gapA)));
            // Wrists bent towards the upper arms, so the hands stay in the sleeves (straight on, the fingers stuck out of the
            // opening by her face; bent fully back, 155 deg, looked broken) [observed 2026-10-25].
            float bend = handBendDeg * (float)Math.PI / 180f;
            BendHand(hdL, B(anim, HumanBodyBones.LeftMiddleProximal), laL, -fwd, bend);
            BendHand(hdR, B(anim, HumanBodyBones.RightMiddleProximal), laR, -fwd, bend);
            LastElbowHeightL = NVector3.Dot(N(laL.position) - floor, up);
            LastElbowHeightR = NVector3.Dot(N(laR.position) - floor, up);

            // Shins folded up behind the thighs (the knees stay where the crawl puts them).
            Aim(llL, ftL, llL.position + U(CrawlPose.Fold(N(ulL.position - llL.position), -fwd, gapL)));
            Aim(llR, ftR, llR.position + U(CrawlPose.Fold(N(ulR.position - llR.position), -fwd, gapL)));
            LastKneeHeightL = NVector3.Dot(N(llL.position) - floor, up);
            LastKneeHeightR = NVector3.Dot(N(llR.position) - floor, up);
        }

        /// <summary>The hand turned <paramref name="radians"/> off the forearm's line towards <paramref name="towards"/>.</summary>
        private static void BendHand(Transform hand, Transform finger, Transform forearm, NVector3 towards, float radians)
        {
            if (finger == null) return;
            NVector3 along = N(hand.position - forearm.position); // elbow to wrist
            Aim(hand, finger, hand.position + U(CrawlPose.Fold(along, towards, radians)));
        }

        /// <summary>The sleeves on the bones as drawn: cup under the elbow / knee, axis between the two folded segments.</summary>
        public static void Place(Animator anim, Transform root, BinderRig rig, Sizes s)
        {
            for (int i = 0; i < Sleeves.Length; i++)
            {
                var (j, axis, fold) = Frame(anim, root, i);
                rig.Place(Sleeves[i].Kind, Sleeves[i].Side, j - axis * s.Cup[i], axis, fold, s.Scale[i]);
            }
        }
    }
}
