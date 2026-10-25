using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Binders.Logic;
using Il2CppInterop.Runtime.Injection;
using RestraintHub;

namespace Binders
{
    /// <summary>
    /// Arm and leg binders: one restraint (ring entry via RestraintHub) that folds her forearms against her upper arms and her
    /// shins against her thighs, in leather sleeves, and keeps her crawling on elbows and knees until she struggles free (F).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "0.3.0")] // OnSave / OnLoad / OnNewGame
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.local.binders";
        public const string PluginName = "Binders";
        public const string PluginVersion = "0.1.1";
        private const string ModelResource = "Binders.assets.binders.bnd";
        internal const string TextureResource = "Binders.assets.binders_color.png";

        internal static ManualLogSource Logger;
        /// <summary>The binders' registration with RestraintHub (ring entry, selection, F).</summary>
        internal static Restraint Entry;
        public static ConfigEntry<string> RingLabel;
        public static ConfigEntry<float> AttemptSeconds, SuccessChance, CrawlSpeed, ArmFoldGap, LegFoldGap, HandBend, ArmLength, LegLength, ClearanceCm;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            RingLabel = Config.Bind("General", "RingLabel", "Arm & Leg Binders", "Name of the ring menu entry.");
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, new ConfigDescription(
                "Seconds of holding the interact key (F) per attempt to struggle free.", new AcceptableValueRange<float>(0.5f, 30f)));
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, new ConfigDescription(
                "Chance that an attempt frees her (1 = the first attempt always does; failures raise it like the game's handcuffs).",
                new AcceptableValueRange<float>(0.01f, 1f)));
            CrawlSpeed = Config.Bind("Crawl", "SpeedFactor", 0.6f, new ConfigDescription(
                "Her crawling speed in the binders as a factor of the game's (which Z / X set). Applies live.",
                new AcceptableValueRange<float>(0.1f, 1f)));
            ArmFoldGap = Config.Bind("Pose", "ArmFoldGap", 12f, new ConfigDescription(
                "Angle between upper arm and folded forearm (degrees; 0 = flat against each other).", new AcceptableValueRange<float>(0f, 60f)));
            LegFoldGap = Config.Bind("Pose", "LegFoldGap", 18f, new ConfigDescription(
                "Angle between thigh and folded shin (degrees).", new AcceptableValueRange<float>(0f, 60f)));
            HandBend = Config.Bind("Pose", "HandBend", 75f, new ConfigDescription(
                "How far her wrists bend her hands back towards her upper arms inside the arm sleeves (degrees; 0 = straight on).",
                new AcceptableValueRange<float>(0f, 90f)));
            ArmLength = Config.Bind("Model", "ArmLength", 1.12f, new ConfigDescription(
                "Length of the arm sleeves relative to her upper arms (1 = elbow to shoulder; a little longer holds her wrists). Applies live.", new AcceptableValueRange<float>(0.5f, 1.6f)));
            LegLength = Config.Bind("Model", "LegLength", 1.12f, new ConfigDescription(
                "Length of the leg sleeves relative to her thighs (1 = knee to hip; a little longer reaches her hips). Applies live.", new AcceptableValueRange<float>(0.5f, 1.6f)));
            ClearanceCm = Config.Bind("Model", "ClearanceCm", 0.6f, new ConfigDescription(
                "Room between her limbs and the sleeves' inner walls (cm). The sleeves are shaped to her limbs ring by ring (measured " +
                "from her body twice a second); raise it if skin shows through, lower it (down to -1) for a tighter fit. Applies live.",
                new AcceptableValueRange<float>(-1f, 5f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0,
                "Bit mask for tests (live): 1 = do not pose her, 2 = do not keep her crawling, 4 = crawl at the game's speed, 8 = hide the sleeves, 16 = sleeves as modelled (not shaped to her). (Input blocking: RestraintHub's Debug.Disable.)");

            try
            {
                BinderController.Model = LoadModel();
            }
            catch (Exception e)
            {
                Logger.LogError("Binders model could not be loaded, the mod is disabled: " + e.Message);
                return;
            }
            ClassInjector.RegisterTypeInIl2Cpp<BinderController>();
            AddComponent<BinderController>();
            Entry = new Restraint
            {
                Id = "Binders",
                RingValue = BinderController.RingItemValue,
                Label = () => RingLabel.Value,
                ShortcutSaveKey = Patches.SaveKeyShortcuts,
                OnSave = Patches.Save,
                OnLoad = Patches.Load,
                OnNewGame = () => BinderController.Instance?.OnNewGame(),
                Holds = BodyParts.Arms | BodyParts.Legs,
                IsWorn = () => BinderController.Instance != null && BinderController.Instance.Machine.Worn,
                // Crawling only (the controller keeps her crouched), nothing that needs her hands, no pose skills, no peeing.
                Blocks = () => RestraintHub.Blocks.Running | RestraintHub.Blocks.HandActions | RestraintHub.Blocks.PoseSkills | RestraintHub.Blocks.Pee,
                OnChosen = () => { var c = BinderController.Instance; if (c != null) c.OnRingChosen(); },
                Attempt = () => BinderController.Instance == null ? 0f : BinderController.Instance.Machine.Attempt,
                OnInput = i => { var c = BinderController.Instance; if (c != null) c.OnInput(i); },
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready (" + BinderController.Model.Parts.Count + " sleeves)");
        }

        private static BinderModel LoadModel()
        {
            using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(ModelResource)
                             ?? throw new FileNotFoundException("embedded resource " + ModelResource + " missing");
            return BinderModel.Read(s);
        }
    }
}
