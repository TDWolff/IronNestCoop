using System.Runtime.InteropServices;

namespace NestCoop.Transport;

/// <summary>
/// Direct calls into the game's own steam_api64.dll (Steamworks "flat" C API), the same functions Steamworks.NET
/// wraps. The game has already called SteamAPI_Init, so we only fetch interface pointers and call methods.
/// Export names verified against the shipped DLL (Plugins/x86_64/steam_api64.dll): SteamAPI_SteamNetworking_v006,
/// SteamAPI_SteamFriends_v018, SteamAPI_SteamUser_v023, SteamAPI_SteamUtils_v010 and the methods below.
/// Using the DLL directly avoids depending on which parts of the game's IL2CPP Steamworks.NET survived stripping.
/// Steamworks C++ `bool` is 1 byte → MarshalAs(I1). SteamIDs are uint64.
/// </summary>
internal static class SteamApi
{
    const string Dll = "steam_api64";

    public enum P2PSend { Unreliable = 0, UnreliableNoDelay = 1, Reliable = 2, ReliableWithBuffering = 3 }
    const int FriendFlagImmediate = 0x04; // regular friends

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct FriendGameInfo
    {
        public ulong GameId;      // CGameID; low 24 bits = AppID
        public uint GameIP;
        public ushort GamePort;
        public ushort QueryPort;
        public ulong LobbyId;     // CSteamID
        public uint AppId => (uint)(GameId & 0xFFFFFF);
    }

    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)] static extern bool SteamAPI_IsSteamRunning();
    [DllImport(Dll)] static extern IntPtr SteamAPI_SteamNetworking_v006();
    [DllImport(Dll)] static extern IntPtr SteamAPI_SteamFriends_v018();
    [DllImport(Dll)] static extern IntPtr SteamAPI_SteamUser_v023();
    [DllImport(Dll)] static extern IntPtr SteamAPI_SteamUtils_v010();

    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamNetworking_SendP2PPacket(IntPtr self, ulong steamIdRemote, byte[] data, uint size, P2PSend type, int channel);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamNetworking_IsP2PPacketAvailable(IntPtr self, out uint msgSize, int channel);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamNetworking_ReadP2PPacket(IntPtr self, byte[] dest, uint destSize, out uint msgSize, out ulong steamIdRemote, int channel);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(IntPtr self, ulong steamIdRemote);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(IntPtr self, ulong steamIdRemote);

    [DllImport(Dll)] static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
    [DllImport(Dll)] static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
    [DllImport(Dll)] [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong steamIdFriend, out FriendGameInfo info);
    [DllImport(Dll)] static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong steamIdFriend);
    [DllImport(Dll)] static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);
    [DllImport(Dll)] static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
    [DllImport(Dll)] static extern uint SteamAPI_ISteamUtils_GetAppID(IntPtr self);

    static IntPtr net, friends, user, utils;

    /// <summary>Fetch interface pointers. Returns false (with a reason) if Steam isn't usable in this process.</summary>
    public static bool Init(out string why)
    {
        try
        {
            if (!SteamAPI_IsSteamRunning()) { why = "Steam client not running"; return false; }
            net = SteamAPI_SteamNetworking_v006();
            friends = SteamAPI_SteamFriends_v018();
            user = SteamAPI_SteamUser_v023();
            utils = SteamAPI_SteamUtils_v010();
            if (net == IntPtr.Zero || friends == IntPtr.Zero || user == IntPtr.Zero || utils == IntPtr.Zero)
            { why = "Steam interfaces unavailable (game hasn't initialised Steam?)"; return false; }
            if (MySteamId == 0) { why = "not logged in to Steam"; return false; }
            why = null;
            return true;
        }
        catch (Exception e) { why = $"steam_api64 call failed: {e.GetType().Name}: {e.Message}"; return false; }
    }

    public static ulong MySteamId => SteamAPI_ISteamUser_GetSteamID(user);
    public static uint AppId => SteamAPI_ISteamUtils_GetAppID(utils);
    public static string MyName => Utf8(SteamAPI_ISteamFriends_GetPersonaName(friends));
    public static string NameOf(ulong id) => Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(friends, id));

    public static bool Send(ulong to, byte[] data, bool reliable) =>
        SteamAPI_ISteamNetworking_SendP2PPacket(net, to, data, (uint)data.Length, reliable ? P2PSend.Reliable : P2PSend.Unreliable, 0);

    /// <summary>Next waiting packet on channel 0, or null.</summary>
    public static byte[] Read(out ulong from)
    {
        from = 0;
        if (!SteamAPI_ISteamNetworking_IsP2PPacketAvailable(net, out uint size, 0)) return null;
        var buf = new byte[size];
        if (!SteamAPI_ISteamNetworking_ReadP2PPacket(net, buf, size, out uint got, out from, 0)) return null;
        if (got != size) Array.Resize(ref buf, (int)got);
        return buf;
    }

    public static void Accept(ulong id) => SteamAPI_ISteamNetworking_AcceptP2PSessionWithUser(net, id);
    public static void Close(ulong id) => SteamAPI_ISteamNetworking_CloseP2PSessionWithUser(net, id);

    /// <summary>Friends currently in this game (same AppID).</summary>
    public static List<(ulong id, string name)> FriendsInThisGame()
    {
        var list = new List<(ulong, string)>();
        uint app = AppId;
        int n = SteamAPI_ISteamFriends_GetFriendCount(friends, FriendFlagImmediate);
        for (int i = 0; i < n; i++)
        {
            ulong id = SteamAPI_ISteamFriends_GetFriendByIndex(friends, i, FriendFlagImmediate);
            if (SteamAPI_ISteamFriends_GetFriendGamePlayed(friends, id, out var info) && info.AppId == app)
                list.Add((id, NameOf(id)));
        }
        return list;
    }

    static string Utf8(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
}
