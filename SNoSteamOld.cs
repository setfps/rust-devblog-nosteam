/*
      

   ▄████████    ▄████████     ███        ▄████████    ▄███████▄    ▄████████
  ███    ███   ███    ███ ▀█████████▄   ███    ███   ███    ███   ███    ███
  ███    █▀    ███    █▀     ▀███▀▀██   ███    █▀    ███    ███   ███    █▀ 
  ███         ▄███▄▄▄         ███   ▀  ▄███▄▄▄       ███    ███   ███       
▀███████████ ▀▀███▀▀▀         ███     ▀▀███▀▀▀     ▀█████████▀  ▀███████████
         ███   ███    █▄      ███       ███          ███                 ███
   ▄█    ███   ███    ███     ███       ███          ███           ▄█    ███
 ▄████████▀    ██████████    ▄████▀     ███         ▄████▀       ▄████████▀ 


 POWERED BY SETFPS | TG: @setfps
 
*/


//#define isOldVersion // Закомментируйте для новых версий Rust (266 devblog и выше)



// Reference: 0Harmony
using Harmony;
using Network;
using Oxide.Core;
using Rust.Platform.Steam;
using Steamworks;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Facepunch.Math;
using Facepunch;
using Rust;
using UnityEngine;
using Oxide.Core.Plugins;


namespace Oxide.Plugins
{
    [Info("SNoSteam", "setfps", "1.5")]
    [Description("nosteam for super old devblogs (with old harmony)")]
    public class SNoSteamOld : RustPlugin
    {
        #region Fields
        private static HarmonyInstance _harmony;
        private static MethodInfo _onAuthenticatedLocal;
        private static MethodInfo _onAuthenticatedRemote;
        private static ConnectionAuth _connectionAuth;
        private static SNoSteamOld _instance;
        private Configuration config;
        private int _lastFakeOnline = -1;
        private DateTime _lastMajorChange = DateTime.MinValue;
        private readonly System.Random _random = new System.Random();
        #endregion

        #region API
        private int GetCurrentFakeOnline() => _lastFakeOnline;

        /// <summary>
        /// API метод для получения текущего фейкового онлайна
        /// </summary>
        /// <returns>Текущее количество фейковых игроков на сервере</returns>
        [HookMethod("GetFakeOnline")]
        private int GetFakeOnline()
        {
            return _lastFakeOnline != -1 ? _lastFakeOnline : 0;
        }
        #endregion

        #region Configuration
        private class Configuration
        {
            [JsonProperty(PropertyName = "Временные слоты (настройка онлайна для разных периодов времени)")]
            public List<TimeSlot> TimeSlots { get; set; }

            [JsonProperty(PropertyName = "Включить реалистичные изменения онлайна (плавные изменения и учет времени суток)")]
            public bool UseRealisticPatterns { get; set; }

            [JsonProperty(PropertyName = "Максимальное отклонение от текущего онлайна за одно обновление")]
            public int MaxPlayersChangeAtOnce { get; set; }

            [JsonProperty(PropertyName = "Вероятность изменения количества игроков при каждом обновлении (0.0 - 1.0)")]
            public float PlayerChangeChance { get; set; }

            [JsonProperty(PropertyName = "Максимальное случайное отклонение от целевого значения")]
            public int MaxRandomDeviation { get; set; }

            public static Configuration DefaultConfig()
            {
                return new Configuration
                {
                    TimeSlots = new List<TimeSlot>
                    {
                        new TimeSlot { StartHour = 0, EndHour = 6, MinPlayers = 5, MaxPlayers = 15, ChangeFrequency = 8, Description = "Ночное время (00:00 - 06:00)" },
                        new TimeSlot { StartHour = 6, EndHour = 12, MinPlayers = 15, MaxPlayers = 35, ChangeFrequency = 5, Description = "Утреннее время (06:00 - 12:00)" },
                        new TimeSlot { StartHour = 12, EndHour = 18, MinPlayers = 35, MaxPlayers = 65, ChangeFrequency = 3, Description = "Дневное время (12:00 - 18:00)" },
                        new TimeSlot { StartHour = 18, EndHour = 24, MinPlayers = 25, MaxPlayers = 45, ChangeFrequency = 4, Description = "Вечернее время (18:00 - 00:00)" }
                    },
                    UseRealisticPatterns = true,
                    MaxPlayersChangeAtOnce = 2,
                    PlayerChangeChance = 0.7f,
                    MaxRandomDeviation = 3
                };
            }
        }

        private class TimeSlot
        {
            [JsonProperty(PropertyName = "Начальный час периода (0-23)")] public int StartHour { get; set; }
            [JsonProperty(PropertyName = "Конечный час периода (1-24)")] public int EndHour { get; set; }
            [JsonProperty(PropertyName = "Минимальное количество фейковых игроков")] public int MinPlayers { get; set; }
            [JsonProperty(PropertyName = "Максимальное количество фейковых игроков")] public int MaxPlayers { get; set; }
            [JsonProperty(PropertyName = "Частота изменения базового количества игроков (в минутах)")] public int ChangeFrequency { get; set; }
            [JsonProperty(PropertyName = "Описание временного периода")] public string Description { get; set; }
            [JsonIgnore] private int _lastPlayerCount = -1;
            [JsonIgnore] private DateTime _lastChange = DateTime.MinValue;

            public int GetTargetPlayers(DateTime now, int currentPlayers, System.Random random)
            {
                if (_lastPlayerCount == -1 || (now - _lastChange).TotalMinutes >= ChangeFrequency)
                {
                    _lastPlayerCount = random.Next(MinPlayers, MaxPlayers + 1);
                    _lastChange = now;
                }
                return _lastPlayerCount;
            }
        }
        #endregion

        #region Oxide Hooks
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
                SaveConfig();
            }
            catch
            {
                PrintError("Ошибка чтения конфигурации, создаю новую!");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = Configuration.DefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        private void OnServerInitialized()
        {
            _instance = this;
            
            ParseReflections();
            Server.Command("encryption 0"); //disable EAC encryption unuseble trash
            PrintWarning("the developer of this modification in tg @setfps");

            if (_harmony == null)
                _harmony = HarmonyInstance.Create("com.setfps.SNoSteamOld");

            PatchMethods();
        }

        private void Unload()
        {
            _harmony?.UnpatchAll("com.setfps.SNoSteamOld");
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
            var steamPlatformType = typeof(SteamPlatform);
            var eacServerType = typeof(EACServer);
            var connectionAuthType = typeof(ConnectionAuth);
            var steamInventoryType = typeof(SteamInventory);
            var authSteamType = typeof(Auth_Steam);
            var serverMgrType = typeof(ServerMgr);

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
                new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.UpdatePlayerSession)));

            _harmony.Patch(AccessTools.Method(steamPlatformType, "EndPlayerSession"),
                new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.EndPlayerSession)));

            _harmony.Patch(AccessTools.Method(steamPlatformType, "BeginPlayerSession"),
                new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.BeginPlayerSession)));

            _harmony.Patch(AccessTools.Method(serverMgrType, "UpdateServerInformation"),
                new HarmonyMethod(typeof(PatchingHooks), nameof(PatchingHooks.UpdateServerInformation)));
        }
        #endregion

        #region Helper Methods
        private int CalculateFakeOnline()
        {
            var now = DateTime.Now;
            var currentHour = now.Hour;
            var currentMinute = now.Minute;
            var basePlayerCount = BasePlayer.activePlayerList.Count;

            var currentSlot = config.TimeSlots.FirstOrDefault(slot =>
                currentHour >= slot.StartHour && currentHour < slot.EndHour);

            if (currentSlot == null)
                return basePlayerCount;

            if (_lastFakeOnline == -1)
            {
                _lastFakeOnline = basePlayerCount + _random.Next(currentSlot.MinPlayers, currentSlot.MaxPlayers + 1);
                return _lastFakeOnline;
            }

            var targetPlayers = currentSlot.GetTargetPlayers(now, _lastFakeOnline, _random);

            if (_random.NextDouble() > config.PlayerChangeChance)
                return _lastFakeOnline;

            var change = CalculateRealisticChange(now, currentHour, _lastFakeOnline, targetPlayers);

            var newOnline = Math.Max(basePlayerCount, _lastFakeOnline + change);
            newOnline = Math.Min(newOnline, ConVar.Server.maxplayers);
            
            _lastFakeOnline = newOnline;
            return newOnline;
        }

        private int CalculateRealisticChange(DateTime now, Int32 currentHour, Int32 currentOnline, Int32 targetOnline)
        {
            if (!config.UseRealisticPatterns)
                return _random.Next(-config.MaxPlayersChangeAtOnce, config.MaxPlayersChangeAtOnce + 1);

            if ((now - _lastMajorChange).TotalMinutes >= 30 && _random.NextDouble() < 0.2)
            {
                _lastMajorChange = now;
                Int32 difference = targetOnline - currentOnline;
                return (int)(difference * _random.NextDouble() * 0.5);
            }

            Int32 change = _random.Next(-config.MaxPlayersChangeAtOnce, config.MaxPlayersChangeAtOnce + 1);

            if (Math.Abs(targetOnline - currentOnline) > 10)
            {
                change = Math.Sign(targetOnline - currentOnline) * Math.Abs(change);
            }

            if (currentHour >= 1 && currentHour <= 5)
            {
                if (_random.NextDouble() < 0.7)
                    change = -Math.Abs(change);
            }
            else if (currentHour >= 17 && currentHour <= 22)
            {
                if (_random.NextDouble() < 0.7)
                    change = Math.Abs(change);
            }

            return change;
        }


        private string GetAssemblyHash(ServerMgr serverMgr)
        {
            string assemblyHash = "il2cpp";
            var hashField = serverMgr.GetType().GetField("_AssemblyHash", BindingFlags.NonPublic | BindingFlags.Instance);
            if (hashField != null)
            {
                assemblyHash = (string)hashField.GetValue(serverMgr) ?? "il2cpp";
            }
            return assemblyHash;
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

            internal static bool UpdateServerInformation()
            {
                if (!SteamServer.IsValid)
                    return false;

                using (TimeWarning.New("UpdateServerInformation", 0))
                {
                    var serverMgr = SingletonComponent<ServerMgr>.Instance;
                    if (serverMgr == null)
                        return false;

                    SteamServer.ServerName = ConVar.Server.hostname;
                    SteamServer.MaxPlayers = ConVar.Server.maxplayers;
                    SteamServer.Passworded = false;

                    #if isOldVersion
                    SteamServer.MapName = World.Name;
                    #else
                    SteamServer.MapName = World.GetServerBrowserMapName();
                    #endif

                    string status = "stok";
                    if (serverMgr.Restarting)
                        status = "strst";

                    string born = $"born{Epoch.FromDateTime(SaveRestore.SaveCreatedTime)}";
                    string gameMode = $"gm{ServerMgr.GamemodeName()}";
                    string pve = ConVar.Server.pve ? ",pve" : string.Empty;
                    
                    string customTags = (ConVar.Server.tags?.Trim(',') ?? "");
                    string additionalTags = (!string.IsNullOrWhiteSpace(customTags) ? "," + customTags : "");

                    var fakeOnline = _instance.CalculateFakeOnline();
                    string assemblyHash = _instance.GetAssemblyHash(serverMgr);

                    #if isOldVersion
                    SteamServer.GameTags = string.Format("mp{0},cp{1},pt{2},qp{3},v{4}{5}{6},h{7},{8},{9},{10}",
                        ConVar.Server.maxplayers,
                        fakeOnline,
                        Network.Net.sv.ProtocolId,
                        serverMgr.connectionQueue.Queued,
                        Protocol.printable.Split('.')[0],
                        pve,
                        additionalTags,
                        assemblyHash,
                        status,
                        born,
                        gameMode);

                    Interface.CallHook("IOnUpdateServerInformation");
                    #else
                    string changeId = BuildInfo.Current?.Scm?.ChangeId ?? "0";
                    SteamServer.GameTags = string.Format("mp{0},cp{1},pt{2},qp{3},v{4}{5}{6},h{7},{8},{9},{10},cs{11}",
                        ConVar.Server.maxplayers,
                        fakeOnline,
                        Network.Net.sv.ProtocolId,
                        serverMgr.connectionQueue.Queued,
                        Protocol.printable.Split('.')[0],
                        pve,
                        additionalTags,
                        assemblyHash,
                        status,
                        born,
                        gameMode,
                        changeId);

                    Interface.CallHook("OnServerInformationUpdated");
                    #endif

                    if (ConVar.Server.description != null && ConVar.Server.description.Length > 100)
                    {
                        string[] array = ConVar.Server.description.SplitToChunks(100).ToArray();
                        for (int i = 0; i < 16; i++)
                        {
                            if (i < array.Length)
                            {
                                SteamServer.SetKey(string.Format("description_{0:00}", i), array[i]);
                            }
                            else
                            {
                                SteamServer.SetKey(string.Format("description_{0:00}", i), string.Empty);
                            }
                        }
                    }
                    else
                    {
                        SteamServer.SetKey("description_0", ConVar.Server.description);
                        for (int j = 1; j < 16; j++)
                        {
                            SteamServer.SetKey(string.Format("description_{0:00}", j), string.Empty);
                        }
                    }

                    SteamServer.SetKey("hash", assemblyHash);
                    SteamServer.SetKey("world.seed", World.Seed.ToString());
                    SteamServer.SetKey("world.size", World.Size.ToString());
                    SteamServer.SetKey("pve", ConVar.Server.pve.ToString());
                    SteamServer.SetKey("headerimage", ConVar.Server.headerimage);
                    SteamServer.SetKey("logoimage", ConVar.Server.logoimage);
                    SteamServer.SetKey("url", ConVar.Server.url);
                    SteamServer.SetKey("gmn", ServerMgr.GamemodeName());
                    SteamServer.SetKey("gmt", ServerMgr.GamemodeTitle());
                    SteamServer.SetKey("uptime", ((int)UnityEngine.Time.realtimeSinceStartup).ToString());
                    SteamServer.SetKey("gc_mb", Performance.report.memoryAllocations.ToString());
                    SteamServer.SetKey("gc_cl", Performance.report.memoryCollections.ToString());
                    SteamServer.SetKey("fps", Performance.report.frameRate.ToString());
                    SteamServer.SetKey("fps_avg", Performance.report.frameRateAverage.ToString("0.00"));
                    SteamServer.SetKey("ent_cnt", BaseNetworkable.serverEntities.Count.ToString());
                    SteamServer.SetKey("build", BuildInfo.Current.Scm.ChangeId);

                    /*_instance.Puts(assemblyHash);
                    _instance.Puts(fakeOnline.ToString());
                    _instance.Puts(Protocol.printable.Split('.')[0]);*/
                }

                return false;
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
            Ticket = new Ticket();
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