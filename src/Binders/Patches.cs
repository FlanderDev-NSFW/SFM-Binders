using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub;
using SfmKit.Pure;

namespace Binders
{
    /// <summary>
    /// The binders' save data: whether they are on and the struggle chance, as two extra keys in the game's save slot (ignored
    /// by the game without the mod); the hub saves the number shortcuts. RestraintHub calls it and catches exceptions.
    /// </summary>
    internal static class Patches
    {
        public const string SaveKeyLocked = "Binders.Locked";
        public const string SaveKeyChance = "Binders.Chance";
        public const string SaveKeyShortcuts = "Binders.Shortcuts"; // written by the hub

        // Called by RestraintHub (Restraint.OnSave / OnLoad) right after the game wrote / read a save slot (never the system file).
        internal static void Save(SaveSlot slot)
        {
            try
            {
                var c = BinderController.Instance;
                if (c == null) return;
                slot.Save(SaveKeyLocked, c.Machine.Worn && c.Machine.State != LockState.Opening);
                slot.Save(SaveKeyChance, (double)c.Machine.Chance);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + c.Machine.Worn + " path=" + slot.Path);
            }
            catch (Exception e) { BinderController.Fail("save hook", e); }
        }

        internal static void Load(SaveSlot slot)
        {
            try
            {
                var c = BinderController.Instance;
                if (c == null) return;
                bool locked = slot.Load(SaveKeyLocked, false); // default when the save has no key (older save)
                double chance = slot.Load(SaveKeyChance, -1.0);
                c.OnSaveLoaded(locked, (float)Math.Max(chance, 0.0));
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " path=" + slot.Path);
            }
            catch (Exception e) { BinderController.Fail("load hook", e); }
        }
    }
}
