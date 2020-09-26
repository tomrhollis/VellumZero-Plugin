// VellumZero Plugin
// VellumZero.cs: Main Class
// Author: Tom Hollis
// August 2020

using System.Collections.Generic;
using System.IO;
using Vellum.Extension;
using System;
using Vellum.Automation;
using System.Text.RegularExpressions;
using System.Timers;
using System.Linq;

namespace VellumZero
{

    /// <summary>
    /// Plugin for Vellum to make use of ElementZero's added functionality
    /// </summary>
    public class VellumZero : IPlugin
    {
        public VZConfig vzConfig;

        private DiscordBot _discord;
        private EZBus _bus;
        private bool serverEventsMade = false;
        private bool playerEventsMade = false;
        private bool busEventsMade = false;
        private bool chatEventMade = false;
        private bool sawChatAPIstring;
        private bool sawCmdAPIstring;
        private Timer rollCallTimer;
        private bool crashing = false;
        private bool restartEventMade = false;

        #region PLUGIN
        internal IHost Host;
        private string _worldName = "Bedrock level";
        public PluginType PluginType { get { return PluginType.EXTERNAL; } }
        private Dictionary<byte, IPlugin.HookHandler> _hookCallbacks = new Dictionary<byte, IPlugin.HookHandler>();
        public enum Hook
        {
            LOC_PLAYER_CHAT,
            LOC_PLAYER_CONN,
            LOC_PLAYER_DC,
            LOC_SERVER_CONN,
            LOC_SERVER_DC,
            DISCORD_REC,
        }

        ProcessManager bds;
        //BackupManager backupManager;
        //RenderManager renderManager;
        Watchdog bdsWatchdog;
        IPlugin autoRestart;

        // This is required to load the plugins default settings when it gets registered by the host for the very first time
        public static object GetDefaultRunConfiguration()
        {
            return new VZConfig()
            {
                PlayerConnMessages = true,
                ServerStatusMessages = true,
                DiscordSync = new DiscordSyncConfig()
                {
                    EnableDiscordSync = false,
                    DiscordToken = "",
                    DiscordChannel = 0,
                    DiscordMentions = true,
                    LatinOnly = false,
                    DiscordCharLimit = 0
                },
                ServerSync = new ServerSyncConfig()
                {
                    EnableServerSync = false,
                    OtherServers = new string[] { },
                    BusAddress = "127.0.0.1",
                    BusPort = 8234,
                    DisplayOnlineList = true,
                    OnlineListScoreboard = "Online",
                    ServerListScoreboard = "Servers"
                },
                VZStrings = new VZTextConfig()
                {
                    LogInit = "Initialized",
                    LogDiscInit = "Starting Discord",
                    LogDiscConn = "Discord Connected",
                    LogDiscDC = "Discord Disconnected",
                    LogBusConn = "Bus Detected",
                    LogEnd = "Plugin Unloaded",
                    ChatMsg = "(§6{0}§r) <{1}> {2}",
                    PlayerJoinMsg = "(§6{0}§r) {1} Connected",
                    PlayerLeaveMsg = "(§6{0}§r) {1} Left",
                    ServerUpMsg = "(§6{0}§r): §aOnline§r",
                    ServerDownMsg = "(§6{0}§r): §cOffline§r",
                    MsgFromDiscord = "(§d{0}§r) [§b{1}§r] {2}",
                    CrashMsg = "§c{0} crashed, attempting to revive it",
                    GiveUpMsg = "§cGiving up trying to revive {0}",
                    RecoverMsg = "§a{0} has recovered from its crash"
                }
            };
        }

        public void Initialize(IHost host)
        {
            Host = host;

            // Load the plugin configuration from the hosts run-config
            vzConfig = host.LoadPluginConfiguration<VZConfig>(this.GetType());

            // Probably have to rework the plugin system a bit to expose stuff like the world name like in your fork...
            // In the meantime loading the server.properties once again should do the job :D.
            // - clarkx86

            using (StreamReader reader = new StreamReader(File.OpenRead(Path.Join(Path.GetDirectoryName(host.RunConfig.BdsBinPath), "server.properties"))))
                _worldName = Regex.Match(reader.ReadToEnd(), @"^level\-name\=(.+)", RegexOptions.Multiline).Groups[1].Value.Trim();


            bds = (ProcessManager)host.GetPluginByName("ProcessManager");
            //backupManager = (BackupManager)host.GetPluginByName("BackupManager");
            //renderManager = (RenderManager)host.GetPluginByName("RenderManager");
            bdsWatchdog = (Watchdog)host.GetPluginByName("Watchdog");
            autoRestart = host.GetPluginByName("AutoRestart");

            // need to make these events even if plugin is disabled so if vellum is reloaded to enable it we'll have the info they get
            if (!busEventsMade)
            {
                // detect that the bus is loaded properly for chat
                bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for ChatAPI$", (object sender, MatchedEventArgs e) =>
                {
                    if (vzConfig.ServerSync.EnableServerSync && _bus == null)
                    {
                        _bus = new EZBus(this);
                        if (vzConfig.ServerStatusMessages) _bus.Broadcast(String.Format(vzConfig.VZStrings.ServerUpMsg, _worldName));
                        _bus.chatSupportLoaded = true;
                    }
                    sawChatAPIstring = true;
                });
                // detect that the bus is loaded properly for commands 
                bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for CommandSupport$", (object sender, MatchedEventArgs e) =>
                {
                    Regex r = new Regex(@".+\[CHAT\].+");
                    Match m = r.Match(e.Matches[0].ToString());
                    if (m.Success) return; // this is someone who knows too much typing the string in chat -- abort!
                    sawCmdAPIstring = true;
                    if (_bus != null)
                    {
                        _bus.commandSupportLoaded = true;
                        RefreshBusServerInfo();
                        // update other servers' scoreboards
                        foreach (string server in vzConfig.ServerSync.OtherServers)
                        {
                            _bus.ExecuteCommand(server, $"scoreboard players add \"{_worldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 0");
                        }
                        rollCallTimer.Start();
                    }
                });

                busEventsMade = true;
            }

            if (autoRestart != null && !restartEventMade)
            {
                autoRestart.RegisterHook((byte)1, (object sender, EventArgs e) =>
                {
                    Initialize(Host);
                });
            }

            if (vzConfig.DiscordSync.EnableDiscordSync)
            {
                _discord = new DiscordBot(this);
            }

            if (vzConfig.ServerSync.EnableServerSync)
            {
                // double check player lists and servers every 5 minutes
                rollCallTimer = new System.Timers.Timer(300000);
                rollCallTimer.AutoReset = true;
                rollCallTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                {
                    RefreshBusServerInfo();
                };
            }

            if (vzConfig.ServerSync.EnableServerSync || vzConfig.DiscordSync.EnableDiscordSync)
            {
                if (!chatEventMade)
                {
                    bds.RegisterMatchHandler(@".+\[CHAT\](.+)", (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@"\[.+\].+\[(.+)\] (.+)");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();
                        string chat = m.Groups[2].ToString().Trim();
                        if (chat.StartsWith('.'))
                        {
                            return; // don't transmit dot commands
                        }

                        HandleChat(user, chat);
                    });
                    chatEventMade = true;
                }

                // set up player join/leave event messages
                if (!playerEventsMade)
                {
                    bds.RegisterMatchHandler(Vellum.CommonRegex.PlayerConnected, (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();

                        // display join message
                        if (vzConfig.PlayerConnMessages) JoinMessage(user);

                        // update all servers
                        if (_bus != null)
                        {
                            foreach (string server in vzConfig.ServerSync.OtherServers)
                            {
                                _bus.ExecuteCommand(server, $"scoreboard players add \"{user}\" \"{vzConfig.ServerSync.OnlineListScoreboard}\" 0");
                                _bus.ExecuteCommand(server, $"scoreboard players add \"{_worldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 1");
                            }
                        }
                    });

                    bds.RegisterMatchHandler(Vellum.CommonRegex.PlayerDisconnected, (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();

                        // display leave message
                        if (vzConfig.PlayerConnMessages) LeaveMessage(user);

                        // update all servers
                        if (_bus != null)
                        {
                            foreach (string server in vzConfig.ServerSync.OtherServers)
                            {
                                _bus.ExecuteCommand(server, $"scoreboard players reset \"{user}\" \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
                                _bus.ExecuteCommand(server, $"scoreboard players remove \"{_worldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 1");
                            }
                        }
                    });
                    playerEventsMade = true;
                }                

                // set up events for server up/down: online/offline messages & auto restart handling
                if (!serverEventsMade)
                {
                    bds.RegisterMatchHandler(Vellum.CommonRegex.ServerStarted, (object sender, MatchedEventArgs e) =>
                    {
                        // broadcast the server online message                        
                        if (_discord != null && vzConfig.ServerStatusMessages) _discord.SendMessage(String.Format(vzConfig.VZStrings.ServerUpMsg, _worldName)).GetAwaiter().GetResult();
                    });

                    bds.Process.Exited += (object sender, EventArgs e) =>
                    {
                        System.Threading.Thread.Sleep(1000); // give the watchdog a second to run the crashing hook if it's crashing
                        if (!crashing)
                        {
                            // update servers' scoreboards
                            if (_bus != null)
                            {
                                foreach (string server in vzConfig.ServerSync.OtherServers)
                                {
                                    _bus.ExecuteCommand(server, $"scoreboard players reset \"{_worldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\"");
                                }
                            }

                            // send server disconnect message
                            if (vzConfig.ServerStatusMessages) Broadcast(String.Format(vzConfig.VZStrings.ServerDownMsg, _worldName));
                            Unload();
                        }                        
                    };
                    ((IPlugin)bdsWatchdog).RegisterHook((byte)Watchdog.Hook.CRASH, (object sender, EventArgs e) => {
                        crashing = true;
                        if(vzConfig.ServerStatusMessages && vzConfig.VZStrings.CrashMsg != "") Broadcast(String.Format(vzConfig.VZStrings.CrashMsg, _worldName));
                    });
                    ((IPlugin)bdsWatchdog).RegisterHook((byte)Watchdog.Hook.LIMIT_REACHED, (object sender, EventArgs e) => {
                        if (crashing && vzConfig.ServerStatusMessages && vzConfig.VZStrings.GiveUpMsg != "") Broadcast(String.Format(vzConfig.VZStrings.GiveUpMsg, _worldName));
                        Unload();
                    });
                    ((IPlugin)bdsWatchdog).RegisterHook((byte)Watchdog.Hook.STABLE, (object sender, EventArgs e) => {                        
                        if (crashing && vzConfig.ServerStatusMessages && vzConfig.VZStrings.RecoverMsg != "") Broadcast(String.Format(vzConfig.VZStrings.RecoverMsg, _worldName));
                        crashing = false;
                    });
                    serverEventsMade = true;
                } else if (serverEventsMade && _bus == null && (sawChatAPIstring || sawCmdAPIstring))
                {
                    _bus = new EZBus(this);
                    _bus.chatSupportLoaded = sawChatAPIstring;
                    _bus.commandSupportLoaded = sawCmdAPIstring;
                }                    
            }            
            Log(vzConfig.VZStrings.LogInit);
        }

        public void Unload()
        {
            // gracefully disconnect from discord
            if (_discord != null) _discord.ShutDown().GetAwaiter().GetResult();
            StopTimers();
            _discord = null;
            _bus = null;
            Log(vzConfig.VZStrings.LogEnd);            
        }

        public Dictionary<byte, string> GetHooks()
        {
            Dictionary<byte, string> hooks = new Dictionary<byte, string>();

            foreach (byte hookId in Enum.GetValues(typeof(Hook)))
                hooks.Add(hookId, Enum.GetName(typeof(Hook), hookId));

            return hooks;
        }

        public void RegisterHook(byte id, IPlugin.HookHandler callback)
        {
            if (!_hookCallbacks.ContainsKey(id))
            {
                _hookCallbacks.Add(id, callback);
            }
            else
            {
                _hookCallbacks[id] += callback;
            }
        }

        internal void CallHook(Hook hook, EventArgs e = null)
        {
            if (_hookCallbacks.ContainsKey((byte)hook))
                _hookCallbacks[(byte)hook]?.Invoke(this, e);
        }
        #endregion

        private void RefreshBusServerInfo()
        {
            if (_bus == null) return;
            Execute($"scoreboard objectives add \"{vzConfig.ServerSync.ServerListScoreboard}\" dummy \"{vzConfig.ServerSync.ServerListScoreboard}\"");
            List<string> players = new List<string>();
            // get list of people from other servers
            foreach (string server in vzConfig.ServerSync.OtherServers)
            {
                Execute($"scoreboard players reset \"{server}\" \"{vzConfig.ServerSync.ServerListScoreboard}\"");
                string result = _bus.ExecuteCommand(server, "list");
                if (result != "")
                {
                    // update the online count from those servers while we're at it
                    Regex r = new Regex("\"currentPlayerCount\": (\\d+),");
                    Match m = r.Match(result);
                    if (m.Groups.Count > 1)
                        Execute($"scoreboard players add \"{server}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" {m.Groups[1]}");

                    // parse the player list
                    r = new Regex("\"players\": \"([^\"]+?)\"");
                    m = r.Match(result);
                    if (m.Groups.Count > 1)
                        players.AddRange(m.Groups[1].ToString().Split(',').ToList());
                }
            }
            // apply that list to the scoreboard     
            Execute($"scoreboard objectives remove \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
            Execute($"scoreboard objectives add \"{vzConfig.ServerSync.OnlineListScoreboard}\" dummy \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
            if (vzConfig.ServerSync.DisplayOnlineList) Execute($"scoreboard objectives setdisplay list \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
            foreach (string player in players)
            {
                if (player.Length < 2) continue;
                Execute($"scoreboard players add \"{player}\" Online 0");
            }
        }

        private void HandleChat(string user, string chat)
        {
            MessageEventArgs a = new MessageEventArgs(_worldName, user, chat);
            CallHook(Hook.LOC_PLAYER_CHAT, a);

            Broadcast(String.Format(vzConfig.VZStrings.ChatMsg, a.Server, a.User, a.Text));
        }

        private void JoinMessage(string user)
        {
            MessageEventArgs a = new MessageEventArgs(_worldName, user, "");
            CallHook(Hook.LOC_PLAYER_CONN, a);

            Broadcast(String.Format(vzConfig.VZStrings.PlayerJoinMsg, a.Server, a.User));
        }

        private void LeaveMessage(string user)
        {
            MessageEventArgs a = new MessageEventArgs(_worldName, user, "");
            CallHook(Hook.LOC_PLAYER_DC, a);

            Broadcast(String.Format(vzConfig.VZStrings.PlayerLeaveMsg, a.Server, a.User));
        }

        private void StopTimers()
        {
            if (rollCallTimer != null) rollCallTimer.Stop();
        }

        public void Broadcast(string message)
        {
            if (_discord != null) _discord.SendMessage(message).GetAwaiter().GetResult();
            if (_bus != null) _bus.Broadcast(message);
        }

        private void SendTellraw(string message)
        {
            message.Replace("\"", "'");
            bds.SendInput("/tellraw @a {\"rawtext\":[{\"text\":\"" + message + "\"}]}");
        }

        public void RelayToServer(string message)
        {
            // if there's a bus, use that to avoid console confirmation messages. Otherwise use console
            if (_bus != null && _bus.chatSupportLoaded) _bus.Announce(_worldName, message);
            else SendTellraw(message);
        }

        /// <summary>
        /// run a command on this server
        /// </summary>
        /// <param name="command">the command to run</param>
        public void Execute(string command)
        {
            if (_bus != null && _bus.commandSupportLoaded) _bus.ExecuteCommand(_worldName, command); // make sure all console commands work this way
            else bds.SendInput(command); // this needs extra testing for character/encoding issues and if all in-game commands work this way
        }


        internal void Log(string line)
        {
            Console.WriteLine("[       VELLUMZERO       ] " + line);
        }
    }


    public struct VZConfig
    {
        public bool PlayerConnMessages;
        public bool ServerStatusMessages;
        public ServerSyncConfig ServerSync;
        public DiscordSyncConfig DiscordSync;
        public VZTextConfig VZStrings;
    }

    public struct DiscordSyncConfig
    {
        public bool EnableDiscordSync;
        public string DiscordToken;
        public ulong DiscordChannel;
        public bool DiscordMentions;
        public bool LatinOnly;
        public int DiscordCharLimit;
    }

    public struct ServerSyncConfig
    {
        public bool EnableServerSync;
        public string[] OtherServers;
        public string BusAddress;
        public uint BusPort;
        public bool DisplayOnlineList;
        public string OnlineListScoreboard;
        public string ServerListScoreboard;
    }

    public struct VZTextConfig
    {
        public string LogInit;
        public string LogDiscInit; 
        public string LogDiscConn;
        public string LogDiscDC;
        public string LogBusConn;
        public string LogEnd;
        public string ChatMsg;
        public string PlayerJoinMsg;
        public string PlayerLeaveMsg;
        public string ServerUpMsg;
        public string ServerDownMsg;
        public string MsgFromDiscord;
        public string CrashMsg;
        public string GiveUpMsg;
        public string RecoverMsg;
    }    
}