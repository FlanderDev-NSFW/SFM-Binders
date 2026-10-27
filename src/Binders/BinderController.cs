using System;
using System.Globalization;
using System.Text;
using Binders.Logic;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Master.Common;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;

namespace Binders
{
    /// <summary>
    /// The binders each frame: the lock (struggle with F), keeping her crawling, her folded limbs (LateUpdate) and the sleeves
    /// (Camera.onPreCull). Bridge diagnostics: <c>game get Binders.BinderController::Status</c>, <c>game call
    /// Binders.BinderController LockHere</c> / <c>Release</c> / <c>Probe</c>.
    /// </summary>
    public sealed class BinderController : MonoBehaviour
    {
        /// <summary>RingItemType value of the entry (registry in docs/mods/RestraintHub.md).</summary>
        public const int RingItemValue = 9400;

        internal static BinderController Instance;
        internal static BinderModel Model;
        internal readonly LockMachine Machine = new LockMachine();
        public static string Status = "off";

        private static int _failures;

        public BinderController(IntPtr pointer) : base(pointer) { }

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake() => Instance = this;

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value; // live: config edits apply without a restart
            Machine.BaseChance = Plugin.SuccessChance.Value;
            bool unlockAllowed = Plugin.Entry.StruggleHeld && Machine.State == LockState.Locked;
            switch (Machine.Tick(Time.deltaTime, unlockAllowed, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked:
                    GameUi.PlaySe(InGameSoundManager.SeType.AttachHandcuffs);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: binders locked");
                    break;
                case LockMachine.Event.AttemptFailed:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: failed, chance now " + F(Machine.Chance));
                    break;
                case LockMachine.Event.Unlocking:
                    GameUi.PlaySe(InGameSoundManager.SeType.Unlock);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: success after " + Machine.Failures + " failures");
                    break;
                case LockMachine.Event.Unlocked:
                    Release();
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] release: struggled free");
                    break;
            }
            try { UpdateSpeed(); }
            catch (Exception e) { Fail("crawling speed", e); }
        }

        // --- putting them on / off -----------------------------------------------------------------------------------------------

        /// <summary>The ring entry or a number key, when the binders are off and nothing worn holds her arms or legs (the hub checked).</summary>
        [HideFromIl2Cpp]
        internal void OnRingChosen()
        {
            string result = LockHere();
            if (Machine.Worn) GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            else GameUi.Message("Can't put the binders on: " + (result.StartsWith("refused: ") ? result.Substring(9) : result));
        }

        /// <summary>Put the binders on (the ring entry; also a bridge diagnostic, which skips the hub's conflict check).</summary>
        public static string LockHere()
        {
            try
            {
                var c = Instance;
                var player = PlayerController.Instance;
                if (c == null || player == null || player.Pca == null || player.Pca.Animator == null || GameState.GameStateData == null) return "not in game";
                if (c.Machine.Worn) return "already on";
                // Pose skills need her arms and legs: end one that runs.
                if (InPose(player)) player.Pca.PlayerAction.ChangeAction(ActionType.None);
                c.Machine.RequestLock();
                Plugin.Logger.LogInfo("[SFM-ACCEPT] ring: binders put on");
                return "locked";
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("lock failed: " + e);
                return "failed: " + e.Message;
            }
        }

        public static string Release()
        {
            var c = Instance;
            if (c == null) return "no controller";
            bool was = c.Machine.Worn;
            c.Machine.Reset();
            c._rig?.SetVisible(false);
            BinderPoser.ForgetDrawn();
            c.Unfit();
            Status = "off";
            if (was) Plugin.Logger.LogInfo("[SFM-ACCEPT] release: binders off");
            return "released";
        }

        /// <summary>She is in a pose skill (action types 50000 and up) [observed 2026-10-24, Yoke].</summary>
        internal static bool InPose(PlayerController player)
        {
            var action = player == null || player.Pca == null || player.Pca.PlayerState == null ? null : player.Pca.PlayerState.CurrentAction;
            return action != null && (int)action.Type >= (int)ActionType.GanimataWalk;
        }

        // --- save identity ----------------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        internal void OnSaveLoaded(bool locked, float chance)
        {
            Release();
            if (locked) Machine.Restore(true, chance);
        }

        /// <summary>A new game or the title screen replaces the game data without a load (RestraintHub's OnNewGame): off.</summary>
        [HideFromIl2Cpp]
        internal void OnNewGame()
        {
            if (Machine.Worn) Release();
        }

        // --- crawling: she stays crouched (crawling = the crouch state, C) ------------------------------------------------------

        private float _nextCrouchTry;

        /// <summary>Her input after the hub's blocking (worn only): C never stands her up; while she is not crawling (put on
        /// standing, after an action) a crouch is asked for, at most twice a second.</summary>
        [HideFromIl2Cpp]
        internal void OnInput(InputData r)
        {
            if ((Plugin.DebugDisable.Value & 2) != 0) return;
            var pca = PlayerController.Instance == null ? null : PlayerController.Instance.Pca;
            if (pca == null || pca.PlayerState == null) return;
            if (pca.PlayerState.IsCrouch)
            {
                r.JustCrouch = false;
                return;
            }
            var action = pca.PlayerState.CurrentAction;
            bool busy = action != null && action.Type != ActionType.None;
            if (!busy && Time.time >= _nextCrouchTry)
            {
                r.JustCrouch = true;
                _nextCrouchTry = Time.time + 0.5f;
            }
        }

        // --- crawling speed ---------------------------------------------------------------------------------------------------
        // Crawling moves at MCommon.playerCrouchSpeed (0.6 m/s) times the Z / X scale [observed 2026-10-24, SpreaderBar 0.8.0].

        private readonly ScaledValue _crawlSpeed = new ScaledValue();

        [HideFromIl2Cpp]
        private void UpdateSpeed()
        {
            var common = MCommon.Instance;
            if (common == null) return;
            if (Machine.Worn && (Plugin.DebugDisable.Value & 4) == 0)
                common.playerCrouchSpeed = _crawlSpeed.Next(common.playerCrouchSpeed, Plugin.CrawlSpeed.Value);
            else if (_crawlSpeed.Active)
                common.playerCrouchSpeed = _crawlSpeed.Release(common.playerCrouchSpeed);
        }

        // --- the pose (LateUpdate) and the sleeves (onPreCull) ----------------------------------------------------------------

        private Camera.CameraCallback _preCull;
        private int _placedFrame = -1;

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        private void OnDestroy()
        {
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            try { if (_crawlSpeed.Active && MCommon.Instance != null) MCommon.Instance.playerCrouchSpeed = _crawlSpeed.Release(MCommon.Instance.playerCrouchSpeed); }
            catch (Exception e) { Fail("crawling speed", e); }
            _rig?.Destroy();
            _fitter?.Destroy();
            if (Instance == this) Instance = null;
        }

        private BinderRig _rig;
        private readonly BinderPoser.Sizes _sizes = new BinderPoser.Sizes();
        private BinderFitter _fitter;
        private float _nextFit;
        private bool _fitLogged;
        public static string FitStatus = "not fitted";
 // diagnostics: what the last fit measured

        /// <summary>The sleeves as modelled; fitted again at the next render.</summary>
        [HideFromIl2Cpp]
        private void Unfit()
        {
            _sizes.Unfit();
            _fitLogged = false;
            _fitter?.ForgetLabels();
            if (_rig != null && _rig.Built && _fitter != null) _fitter.Reset(_rig);
            _nextFit = 0f;
        }

        private const float FitInterval = 0.5f; // s between fits; each sleeve covers the last BinderFitter.HistoryFits of them

        /// <summary>Render time: the sleeves shaped to her limbs again every <see cref="FitInterval"/> (Debug.Disable 16: as modelled).</summary>
        [HideFromIl2Cpp]
        private void FitIfDue(Animator anim, Transform root)
        {
            if ((Plugin.DebugDisable.Value & 16) != 0)
            {
                if (_sizes.Fitted[0] || _sizes.Fitted[2]) Unfit();
                FitStatus = "off (Debug.Disable 16)";
                return;
            }
            if (_fitter == null) return;
            if (_fitter.Busy)
            {
                if (!_fitter.Step(_sizes, _rig, Plugin.ClearanceCm.Value / 100f)) return;
                FitStatus = "(" + _fitter.LastMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms at most per frame) " + _fitter.Status;
                if (!_fitLogged) Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: " + FitStatus);
                _fitLogged = true;
                return;
            }
            if (Time.time < _nextFit) return;
            _nextFit = Time.time + FitInterval;
            _fitter.Begin(anim, root, _sizes, _rig);
        }

        private void LateUpdate()
        {
            try
            {
                if (!Machine.Worn) return;
                var player = PlayerController.Instance;
                var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
                if (anim == null) return;
                if (_rig == null || !_rig.Built)
                {
                    _rig = new BinderRig(Model);
                    _rig.Build(anim.gameObject.layer);
                    _fitter ??= new BinderFitter(Model);
                    Unfit();
                }
                if (_fitter != null && !_fitter.HasLabels && (Plugin.DebugDisable.Value & 16) == 0)
                {
                    _fitter.Label(anim, _rig);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] fit: " + _fitter.LabelStatus);
                }
                _probeAnim = Snapshot();
                if ((Plugin.DebugDisable.Value & 1) == 0)
                {
                    BinderPoser.UseDrawnLengths(anim);
                    BinderPoser.Apply(anim, player.transform, _sizes, Plugin.ArmFoldGap.Value, Plugin.LegFoldGap.Value, Plugin.HandBend.Value);
                    _probeLate = Snapshot();
                    BinderPoser.RestoreAnimatedLengths(anim);
                }
                if (Time.frameCount % 30 == 0)
                    Status = Machine.State + ", crawling " + player.Pca.PlayerState.IsCrouch + ", knees apart +" + F(BinderPoser.LastKneeSpread) + " m, hips down " + F(BinderPoser.LastHipDrop)
                             + " m, chest down " + F(BinderPoser.LastChestDeg) + " deg, elbows " + F(BinderPoser.LastElbowHeightL) + " / "
                             + F(BinderPoser.LastElbowHeightR) + " m up, knees " + F(BinderPoser.LastKneeHeightL) + " / " + F(BinderPoser.LastKneeHeightR)
                             + " m up, sleeve scale arm " + F(_sizes.Scale[0]) + " leg " + F(_sizes.Scale[2]);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("holding her failed, binders off: " + e);
                Release();
            }
        }

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            try { DebugCamera.Aim(cam, DebugView); }
            catch (Exception e) { Fail("debug camera", e); }
            if (_placedFrame == Time.frameCount) return;
            _placedFrame = Time.frameCount;
            try
            {
                bool show = Machine.Worn && (Plugin.DebugDisable.Value & 8) == 0;
                _rig?.SetVisible(show);
                if (!show || _rig == null) return;
                // The sleeves follow the bones as drawn (they still move after LateUpdate) [observed 2026-10-24, WallRestraint].
                var player = PlayerController.Instance;
                var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
                if (anim == null) return;
                BinderPoser.RecordDrawn(anim);
                BinderPoser.Scale(anim, Model, _sizes, Plugin.ArmLength.Value, Plugin.LegLength.Value); // from the drawn bones (her body settings applied)
                FitIfDue(anim, player.transform);
                BinderPoser.Place(anim, player.transform, _rig, _sizes);
                _probeDrawn = Snapshot();
            }
            catch (Exception e) { Fail("placing the binders", e); }
        }

        // --- diagnostics ------------------------------------------------------------------------------------------------------

        /// <summary>Diagnostics (bridge: <c>game set Binders.BinderController::DebugView 'right 1.2'</c>): the main camera looks at
        /// her from a side at the distance given (m; a third word "feet" aims at her ankles) - SfmKit <see cref="DebugCamera"/>.
        /// Empty = off.</summary>
        public static string DebugView = "";

        /// <summary>Diagnostics (bridge: <c>game set Binders.BinderController::DebugGhost true</c>): the body bake the sleeves are
        /// fitted to, drawn as a green mesh where it was measured (updated with every fit), to compare with her drawn body.</summary>
        public static bool DebugGhost;

        private static string _probeAnim = "", _probeLate = "", _probeDrawn = "";

        private static readonly HumanBodyBones[] ProbeBones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };

        /// <summary>Bone positions in her frame (x right, y up from the floor under her, z forward), cm.</summary>
        [HideFromIl2Cpp]
        private static string Snapshot()
        {
            var player = PlayerController.Instance;
            var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
            if (anim == null) return "no player";
            Transform root = player.transform;
            var sb = new StringBuilder();
            sb.Append("crouch ").Append(player.Pca.PlayerState.IsCrouch).Append("; ");
            foreach (var b in ProbeBones)
            {
                var t = anim.GetBoneTransform(b);
                if (t == null) { sb.Append(b).Append(" -; "); continue; }
                Vector3 p = root.InverseTransformPoint(t.position) * 100f;
                sb.Append(b).Append(' ').Append(p.x.ToString("0", CultureInfo.InvariantCulture)).Append(',')
                  .Append(p.y.ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(p.z.ToString("0", CultureInfo.InvariantCulture)).Append("; ");
            }
            return sb.ToString();
        }

        /// <summary>Diagnostics: her bones as animated, as posed (LateUpdate) and as drawn (cm, her frame).</summary>
        public static string Probe()
        {
            string text = "animated: " + _probeAnim + "\nposed: " + _probeLate + "\ndrawn: " + _probeDrawn;
            Plugin.Logger.LogInfo("probe: " + text); // the bridge shortens long answers
            return text;
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
