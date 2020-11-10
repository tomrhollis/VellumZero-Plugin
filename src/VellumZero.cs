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

namespace VellumZero
{

    /// <summary>
    /// Plugin for Vellum to make use of ElementZero's added functionality
    /// </summary>
    public class VellumZero : IPlugin
    {
        public VZConfig vzConfig;
        internal DiscordBot Discord;
        internal EZBus Bus;

        private bool serverEventsMade = false;
        private bool playerEventsMade = false;
        private bool busEventsMade = false;
        private bool chatEventMade = false;
        private bool sawChatAPIstring;
        private bool sawCmdAPIstring;
        private Timer rollCallTimer;
        private bool crashing = false;
        private bool restartEventMade = false;
        private DateTime start = DateTime.Now;
        internal Server ThisServer;

        #region PLUGIN
        internal IHost Host;
        internal string _worldName = "Bedrock level";
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

        internal ProcessManager bds;
        BackupManager backupManager;
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
                UserDB = "./user.db",
                EssentialsDB = "./essentials.db",
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
                    DiscordController = false,
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
                    Playing = "Minecraft",
                    ChannelTopic = "{0}/{1} players online",
                    ChannelTopicMulti = "{0} players online across {1} servers",
                    ChannelTopicOffline = " ",
                    ChatMsg = "(§6{0}§r) <{3}{1}{4}> {2}",
                    PlayerJoinMsg = "(§6{0}§r) {3}{1}{4} Connected",
                    PlayerLeaveMsg = "(§6{0}§r) {3}{1}{4} Left",
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
            backupManager = (BackupManager)host.GetPluginByName("BackupManager");
            //renderManager = (RenderManager)host.GetPluginByName("RenderManager");
            bdsWatchdog = (Watchdog)host.GetPluginByName("Watchdog");
            autoRestart = host.GetPluginByName("AutoRestart");

            ThisServer = new Server(this, _worldName, 0, false);
            RepeatableSetup();

            // need to make these events even if plugin is disabled so if vellum is reloaded to enable it we'll have the info they get
            if (!busEventsMade)
            {
                // detect that the bus is loaded properly for chat
                bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for ChatAPI$", (object sender, MatchedEventArgs e) =>
                {
                    sawChatAPIstring = true;
                    if (vzConfig.ServerSync.EnableServerSync & Bus != null)
                    {
                        Bus.chatSupportLoaded = true;
                        Log(vzConfig.VZStrings.LogBusConn + ": ChatAPI");
                    }                    
                });
                // detect that the bus is loaded properly for commands 
                bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for CommandSupport$", (object sender, MatchedEventArgs e) =>
                {
                    Regex r = new Regex(@".+\[CHAT\].+");
                    Match m = r.Match(e.Matches[0].ToString());
                    if (m.Success) return; // this is someone who knows too much typing the string in chat -- abort!

                    sawCmdAPIstring = true;
                    if (vzConfig.ServerSync.EnableServerSync & Bus != null) {
                        Bus.commandSupportLoaded = true;
                        Log(vzConfig.VZStrings.LogBusConn + ": CommandSupport");

                        // set up fresh scoreboards
                        if (vzConfig.ServerSync.ServerListScoreboard != "")
                        {
                            Execute($"scoreboard objectives remove \"{vzConfig.ServerSync.ServerListScoreboard}\"");
                            Execute($"scoreboard objectives add \"{vzConfig.ServerSync.ServerListScoreboard}\" dummy \"{vzConfig.ServerSync.ServerListScoreboard}\"");
                            Bus.BroadcastCommand($"scoreboard players add \"{_worldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 0");
                        }

                        if (vzConfig.ServerSync.OnlineListScoreboard != "")
                        {
                            Execute($"scoreboard objectives remove \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
                            Execute($"scoreboard objectives add \"{vzConfig.ServerSync.OnlineListScoreboard}\" dummy \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
                            if (vzConfig.ServerSync.DisplayOnlineList) Execute($"scoreboard objectives setdisplay list \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
                        }

                        Bus.RefreshBusServerInfo();
                    }
                });
                busEventsMade = true;
            }

            // auto-restart plugin handler
            if (autoRestart != null && !restartEventMade)
            {
                autoRestart.RegisterHook((byte)1, (object sender, EventArgs e) =>
                {
                    RepeatableSetup();
                });
                restartEventMade = true;
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

                        if (!chat.StartsWith('.')) // ignore dot commands
                        {
                            HandleChat(user, chat);
                        }
                    });
                    chatEventMade = true;
                }

                // set up player join/leave event messages
                if (!playerEventsMade)
                {
                    // when player connects
                    bds.RegisterMatchHandler(@".+Player connected: (.+), xuid: (\d+)", (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@".+\[CHAT\].+");
                        Match m = r.Match(e.Matches[0].Value);
                        if (m.Success) return; // this is someone who knows too much typing the string in chat -- abort!

                        string user = e.Matches[0].Groups[1].Value;
                        ulong xuid = ulong.Parse(e.Matches[0].Groups[2].Value);

                        Player player = Player.CreateInstance(this, ThisServer, user, xuid);
                        ThisServer.AddPlayer(player);

                        //if (Discord != null) Discord.UpdateDiscordTopic();
                    });

                    // when player disconnects
                    bds.RegisterMatchHandler(@".+Player disconnected: (.+), xuid: (\d+)", (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@".+\[CHAT\].+");
                        Match m = r.Match(e.Matches[0].Value);
                        if (m.Success) return; // this is someone who knows too much typing the string in chat -- abort!

                        ulong xuid = ulong.Parse(e.Matches[0].Groups[2].Value);

                        ThisServer.RemovePlayer(xuid);
                        //if (Discord != null) Discord.UpdateDiscordTopic();
                    });
                    playerEventsMade = true;
                }                

                // set up events for server up/down: online/offline messages & auto restart handling
                if (!serverEventsMade)
                {
                    // when the list command is run, harvest its data
                    bds.RegisterMatchHandler(@"^There are (\d+)/(\d+) players online:\n(.*)", (object sender, MatchedEventArgs e) =>
                    {
                        ThisServer.ConsoleInfoUpdate(e);
                    });

                    bds.RegisterMatchHandler(Vellum.CommonRegex.ServerStarted, (object sender, MatchedEventArgs e) =>
                    {
                        ThisServer.MarkAsOnline();
                        Execute("list"); // to get the total available player slots

                        if (Discord != null) Discord.UpdateDiscordTopic();

                        rollCallTimer.Start();
                    });

                    bds.Process.Exited += (object sender, EventArgs e) =>
                    {
                        ThisServer.MarkAsOffline();
                        System.Threading.Thread.Sleep(1000); // give the watchdog a second to run the crashing hook if it's crashing
                        if (!crashing)
                        {
                            Unload();
                            //Log(vzConfig.VZStrings.LogEnd);
                        }                        
                    };

                    // when server crashes and watchdog catches it
                    ((IPlugin)bdsWatchdog).RegisterHook((byte)Watchdog.Hook.CRASH, (object sender, EventArgs e) => {
                        if (!crashing && vzConfig.ServerStatusMessages && vzConfig.VZStrings.CrashMsg != "")
                            Broadcast(String.Format(vzConfig.VZStrings.CrashMsg, _worldName));
                        crashing = true;
                    });

                    // when watchdog gives up trying to revive the server
                    ((IPlugin)bdsWatchdog).RegisterHook((byte)Watchdog.Hook.LIMIT_REACHED, (object sender, EventArgs e) => {
                        if (crashing)
                        {
                            if (vzConfig.ServerStatusMessages && vzConfig.VZStrings.GiveUpMsg != "") Broadcast(String.Format(vzConfig.VZStrings.GiveUpMsg, _worldName));                            
                            Unload();
                            Log(vzConfig.VZStrings.LogEnd);
                        }
                    });

                    // when watchdog successfully recovers the server
                    ((IPlugin)bdsWatchdog).RegisterHook((byte)Watchdog.Hook.STABLE, (object sender, EventArgs e) => {                        
                        if (crashing && vzConfig.ServerStatusMessages && vzConfig.VZStrings.RecoverMsg != "") Broadcast(String.Format(vzConfig.VZStrings.RecoverMsg, _worldName));
                        crashing = false;
                    });

                    // when a backup ends
                    ((IPlugin)backupManager).RegisterHook((byte)BackupManager.Hook.END, (object sender, EventArgs e) =>
                    {
                        if(DateTime.Now.Subtract(start).TotalMinutes > 5 && Host.RunConfig.Backups.StopBeforeBackup) RepeatableSetup();
                    });

                    serverEventsMade = true;

                } else if (serverEventsMade && Bus == null && (sawChatAPIstring || sawCmdAPIstring))
                {
                    Bus = new EZBus(this);
                    Bus.chatSupportLoaded = sawChatAPIstring;
                    Bus.commandSupportLoaded = sawCmdAPIstring;
                }                    
            }
           
            Log(vzConfig.VZStrings.LogInit);
        }

        private void RepeatableSetup()
        {
            if (vzConfig.DiscordSync.EnableDiscordSync) Discord = new DiscordBot(this);
            if (vzConfig.ServerSync.EnableServerSync) Bus = new EZBus(this);

            // double check player lists and servers every minute
            rollCallTimer = new Timer(60000);
            rollCallTimer.AutoReset = true;
            rollCallTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
            {
                if (vzConfig.ServerSync.EnableServerSync && Bus != null) Bus.RefreshBusServerInfo();
                string result = Execute("list"); // to account for partial connects without disconnect messages
                if (result != "") ThisServer.BusInfoUpdate(result);
            };
        }

        public void Unload()
        {
            
            // gracefully disconnect from discord
            if (Discord != null) Discord.ShutDown().GetAwaiter().GetResult();

            // reset everything else
            StopTimers();
            Discord = null;
            Bus = null;                      
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

        /// <summary>
        /// What to do when a chat message is seen in console.
        /// Later: change this completely to monitor chat.db instead since that uses UUIDs
        /// </summary>
        /// <param name="user">the username of the person who wrote the message</param>
        /// <param name="chat">the message</param>
        private void HandleChat(string user, string chat)
        {
            MessageEventArgs a = new MessageEventArgs(_worldName, user, chat);
            CallHook(Hook.LOC_PLAYER_CHAT, a);
            Player player = ThisServer.Players.Find(p => { return p.Name == user; });
            player.RefreshAffixes();
            Broadcast(String.Format(vzConfig.VZStrings.ChatMsg, a.Server, a.User, a.Text, player.Prefix, player.Postfix));
        }

        internal void JoinMessage(Player user)
        {
            MessageEventArgs a = new MessageEventArgs(_worldName, user.Name, "");
            user.RefreshAffixes();
            CallHook(Hook.LOC_PLAYER_CONN, a);
            Broadcast(String.Format(vzConfig.VZStrings.PlayerJoinMsg, a.Server, a.User, user.Prefix, user.Postfix));
        }

        internal void LeaveMessage(Player user)
        {
            MessageEventArgs a = new MessageEventArgs(_worldName, user.Name, "");
            user.RefreshAffixes();
            CallHook(Hook.LOC_PLAYER_DC, a);
            Broadcast(String.Format(vzConfig.VZStrings.PlayerLeaveMsg, a.Server, a.User, user.Prefix, user.Postfix));
        }

        private void StopTimers()
        {
            if (rollCallTimer != null) rollCallTimer.Stop();
        }

        public void Broadcast(string message)
        {
            if (Discord != null) Discord.SendMessage(message);
            if (Bus != null) Bus.Broadcast(message);
        }

        internal void SendTellraw(string message)
        {
            message.Replace("\"", "'");
            bds.SendInput("tellraw @a {\"rawtext\":[{\"text\":\"" + message + "\"}]}");
        }

        /// <summary>
        /// run a command on this server
        /// </summary>
        /// <param name="command">the command to run</param>
        public string Execute(string command)
        {
            if (Bus != null && Bus.commandSupportLoaded) return Bus.ExecuteCommand(_worldName, command); 
            else if (bds.IsRunning) bds.SendInput(command);
            return null;
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
        public string UserDB;
        public string EssentialsDB;
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
        public bool DiscordController;
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
        public string Playing;
        public string ChannelTopic;
        public string ChannelTopicMulti;
        public string ChannelTopicOffline;
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