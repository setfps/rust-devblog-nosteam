// Reference: 0Harmony
using HarmonyLib;
using Network;
using Oxide.Core;
using Rust.Platform.Steam;
using Steamworks;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
    [Info("SNoSteam", "setfps", "1.0")]
    [Description("")]
    public class SNoSteam : RustPlugin
    {
        #region Fields
        private static HarmonyLib.Harmony _harmony;
        private static MethodInfo _onAuthenticatedLocal;
        private static MethodInfo _onAuthenticatedRemote;
        private static ConnectionAuth _connectionAuth;
        private static SNoSteam _instance;
        #endregion

        #region Oxide Hooks
        private void OnServerInitialized()
        {
            _instance = this;
            ParseReflections();

            PrintWarning("the developer of this modification in tg @setfps");

            if (_harmony == null)
                _harmony = new HarmonyLib.Harmony("com.setfps.SNoSteam");

            PatchMethods();
        }

        private void Unload()
        {
            _harmony?.UnpatchAll("com.setfps.SNoSteam");
        }
        #endregion

        #region Methods
        private static void ParseReflections()
        {
            _onAuthenticatedLocal = typeof(EACServer)
                .GetMethod("OnAuthenticatedLocal", BindingFlags.Static | BindingFlags.NonPublic);

            _onAuthenticatedRemote = typeof(EACServer)
                .GetMethod("OnAuthenticatedRemote", BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static void PatchMethods()
        {
            try
            {
                var steamPlatformType = typeof(SteamPlatform);
                var eacServerType = typeof(EACServer);
                var connectionAuthType = typeof(ConnectionAuth);
                var steamInventoryType = typeof(SteamInventory);
                var authSteamType = typeof(Auth_Steam);

                _harmony.Patch(AccessTools.Method(steamPlatformType, "LoadPlayerStats"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.LoadPlayerStats)));

                _harmony.Patch(AccessTools.Method(eacServerType, "OnJoinGame"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.OnJoinGame)));

                _harmony.Patch(AccessTools.Method(connectionAuthType, "OnNewConnection"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.OnNewConnection)));

                _harmony.Patch(AccessTools.Method(steamInventoryType, "OnRpcMessage"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.OnRpcMessage)));

                _harmony.Patch(AccessTools.Method(authSteamType, "ValidateConnecting"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.ValidateConnecting)));

                _harmony.Patch(AccessTools.Method(steamPlatformType, "UpdatePlayerSession"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.UpdatePlayerSession))); //idiot? 

                _harmony.Patch(AccessTools.Method(steamPlatformType, "EndPlayerSession"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.EndPlayerSession)));

                _harmony.Patch(AccessTools.Method(steamPlatformType, "BeginPlayerSession"),
                    new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.BeginPlayerSession)));
            }
            catch (Exception ex)
            {
                _instance.PrintError($"Обратитесь к разработчику! Ошибка: {ex.ToString()}");
            }
            
        }
        #endregion

        #region Patching Hooks
        private static class PatchingHooks
        {
            internal static bool BeginPlayerSession(ulong userId, byte[] authToken, ref bool __result)
            {
                bool authSuccess = SteamServer.BeginAuthSession(authToken, userId);
                var steamTicket = new SteamTicket(authToken);
                steamTicket.GetClientVersion();

                var connection = ConnectionAuth.m_AuthConnection
                    .First(x => x.userid == userId);

                if (!authSuccess)
                {
                    connection.authStatus = "ok";
                    _onAuthenticatedLocal.Invoke(null, new object[] { connection });
                    _onAuthenticatedRemote.Invoke(null, new object[] { connection });
                }

                object hookResult = Interface.CallHook("OnBeginPlayerSession", userId, authSuccess);

                if (hookResult == null)
                {
                    __result = true;
                    return false;
                }

                ConnectionAuth.Reject(connection, hookResult.ToString(), null);
                return true;
            }

            internal static bool EndPlayerSession(ulong userId) => false;

            internal static bool UpdatePlayerSession(ulong userId, string userName) => false;

            internal static bool ValidateConnecting(ref bool __result, ulong steamid, ulong ownerSteamID, AuthResponse response)
            {
                __result = true;
                return true;
            }

            internal static bool OnRpcMessage(BasePlayer player, uint rpc, Message msg) => false;

            internal static bool LoadPlayerStats(ulong userId, ref Task<bool> __result)
            {
                bool flag = true;
                bool flag2;
                if (flag)
                {
                    __result = Task.FromResult<bool>(true);
                    flag2 = false;
                }
                else
                {
                    flag2 = true;
                }
                return flag2;
            }

            internal static bool OnJoinGame(Connection connection)
            {
                _onAuthenticatedLocal.Invoke(null, new object[] { connection });
                _onAuthenticatedRemote.Invoke(null, new object[] { connection });
                return false;
            }

            internal static bool OnNewConnection(ConnectionAuth __instance, Connection connection)
            {
                if (_connectionAuth == null)
                    _connectionAuth = __instance;

                _instance.PrintWarning($"OnNewConnection => {connection.username} {connection.userid} " +
                    $"{connection.ipaddress} {connection.authStatus} {connection.token.Length}");

                if (DeveloperList.Contains(connection.userid))
                {
                    ConnectionAuth.Reject(connection, "Developer SteamId", null);
                    return false;
                }

                return true;
            }
        }
        #endregion
    }

    #region Structs
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 234)]
    public struct Ticket
    {
        public uint Length;
        public ulong ID;
        public ulong SteamID;
        public uint ConnectionTime;
        public SteamSession Session;
        public SteamTokendata Token;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SteamTokendata
    {
        public int Length;
        public int Unknown0x38;
        public int Unknown0x3C;
        public ulong UserID;
        public int AppID;
        public int Unknown0x4C;
        public byte Unknown0x50;
        public byte Unknown0x51;
        public byte Unknown0x52;
        public byte Unknown0x53;
        public uint Unknown0x54;
        public uint StartTime;
        public uint EndedTime;
        public byte Unknown0x60;
        public byte Unknown0x61;
        public byte Unknown0x62;
        public byte Unknown0x63;
        public short Unknown0x64;
        public short Unknown0x66;
        public short Unknown0x68;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] SHA128;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SteamSession
    {
        public uint Length;
        public uint Unknown0x1C;
        public uint Unknown0x20;
        public uint Unknown0x24;
        public uint Unknown0x28;
        public uint SessionID;
        public uint ConnectNumber;
    }
    #endregion

    #region SteamTicket
    public class SteamTicket
    {
        public enum ClientVersion
        {
            NoSteam,
            Steam,
            Unknown
        }

        private static readonly byte[] TokenHeader = { 84, 79, 75, 69, 78 };
        private static readonly Version TokenVersion = new Version(5, 8, 28);

        public ulong SteamId { get; }
        public Ticket Ticket { get; }
        public byte[] Token { get; }
        public string Username { get; }
        public string Version { get; }
        public ClientVersion _ClientVersion { get; private set; }

        private bool IsCrack => Token.Length == 234;
        private bool IsLicense => Token.Length == 240;

        public SteamTicket(Connection connection)
        {
            SteamId = connection?.userid ?? 0uL;
            Username = connection?.username ?? string.Empty;
            Token = connection?.token ?? new byte[0];

            if (IsCrack || IsLicense)
                Ticket = Token.Deserialize<Ticket>();
        }

        public SteamTicket(byte[] authToken)
        {
            SteamId = 0uL;
            Username = string.Empty;
            Ticket = default;
            Token = authToken ?? new byte[0];
        }

        public void GetClientVersion()
        {
            _ClientVersion = IsCrack ? ClientVersion.NoSteam
                : IsLicense ? ClientVersion.Steam
                : ClientVersion.Unknown;
        }

        public override string ToString() => $"[{SteamId}/{Username}]";
    }
    #endregion

    #region Serialization
    public static class Serialization
    {
        public static byte[] Serialize<T>(this T structure) where T : struct
        {
            byte[] array = new byte[Marshal.SizeOf(typeof(T))];
            IntPtr intPtr = Marshal.AllocHGlobal(array.Length);
            Marshal.StructureToPtr(structure, intPtr, fDeleteOld: true);
            Marshal.Copy(intPtr, array, 0, array.Length);
            Marshal.FreeHGlobal(intPtr);
            return array;
        }

        public static T Deserialize<T>(this byte[] bytes) where T : struct
        {
            try
            {
                GCHandle gCHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                T result = (T)Marshal.PtrToStructure(gCHandle.AddrOfPinnedObject(), typeof(T));
                gCHandle.Free();
                return result;
            }
            catch
            {
            }
            return default(T);
        }
    }
    #endregion
}