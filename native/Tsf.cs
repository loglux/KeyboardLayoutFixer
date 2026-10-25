using System.Runtime.InteropServices;

namespace KeyboardLayoutFixer;

/// <summary>What the Win+Space switcher actually lists: enabled TSF profiles (documented ITfInputProcessorProfileMgr).</summary>
internal static class Tsf
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Profile
    {
        public uint ProfileType;
        public ushort LangId;
        public Guid Clsid;
        public Guid GuidProfile;
        public Guid Catid;
        public IntPtr HklSubstitute;
        public uint Caps;
        public IntPtr Hkl;
        public uint Flags;
    }

    [ComImport, Guid("71C6E74D-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumProfiles
    {
        void Clone(out IEnumProfiles e);
        [PreserveSig] int Next(uint count, out Profile p, out uint fetched);
        void Reset();
        void Skip(uint count);
    }

    [ComImport, Guid("71C6E74C-0F28-11D8-A82A-00065B84435C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IProfileMgr
    {
        void ActivateProfile(uint type, ushort lang, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
        void DeactivateProfile(uint type, ushort lang, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);
        void GetProfile(uint type, ushort lang, ref Guid clsid, ref Guid profile, IntPtr hkl, out Profile p);
        void EnumProfiles(ushort lang, out IEnumProfiles e);
    }

    /// <summary>Enabled profiles as (langId, hkl, type); null when TSF can't be queried.</summary>
    public static List<(int Lang, long Hkl, uint Type, uint Flags)>? Enabled()
    {
        List<(int, long, uint, uint)>? res = null;
        var t = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("33C53A50-F456-4884-B049-85FD643ECFED"))!;
                var mgr = (IProfileMgr)Activator.CreateInstance(type)!;
                mgr.EnumProfiles(0, out var e);
                var l = new List<(int, long, uint, uint)>();
                while (e.Next(1, out var p, out var n) == 0 && n == 1)
                    if ((p.Flags & 2) != 0 && p.ProfileType == 2) l.Add((p.LangId, p.Hkl.ToInt64(), p.ProfileType, p.Flags));
                res = l;
            }
            catch { res = null; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        return res;
    }
}
