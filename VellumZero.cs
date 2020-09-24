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
        private bool discLibrariesLoaded = false;
        private bool serverEventsMade;
        private bool playerEventsMade;
        private bool busEventsMade;
        private bool chatEventMade;
        private bool sawChatAPIstring;
        private bool sawCmdAPIstring;
        private Timer autoRestartTimer;
        private Timer hiVisWarnTimer;
        private Timer hiVisWarnMsgs;
        private Timer normalWarnMsg;
        private Timer rollCallTimer;
        private uint msCountdown;
        private bool crashing = false;
        //private bool alreadyStopping = false;

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
        BackupManager backupManager;
        RenderManager renderManager;
        Watchdog bdsWatchdog;

        // This is required to load the plugins default settings when it gets registered by the host for the very first time
        public static object GetDefaultRunConfiguration()
        {
            return new VZConfig()
            {
                PlayerConnMessages = true,
                ServerStatusMessages = true,
                AutoRestartTime = "12:00p",
                HiVisShutdown = false,
                IgnorePatterns = new String[] { },
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
                    BroadcastChat = true,
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
                    LogBusConn = "Bus Connected",
                    LogEnd = "Unloading plugin",
                    ChatMsg = "(§6{0}§r) <{1}> {2}",
                    PlayerJoinMsg = "(§6{0}§r) {1} Connected",
                    PlayerLeaveMsg = "(§6{0}§r) {1} Left",
                    ServerUpMsg = "(§6{0}§r): §aOnline§r",
                    ServerDownMsg = "(§6{0}§r): §cOffline§r",
                    MsgFromDiscord = "(§d{0}§r) [§b{1}§r] {2}",
                    RestartOneWarn = "The server will restart in {0} {1}",
                    RestartMinWarn = "§c§lLess than {0} min to scheduled restart!",
                    RestartSecTitle = "§c{0}",
                    RestartSecSubtl = "§c§lseconds until restart",
                    RestartAbort = "An important process is still running, can't restart. Trying again in 30 minutes",
                    MinutesWord = "minutes",
                    SecondsWord = "seconds",
                    CrashMsg = "§c{0} crashed, attempting to revive it",
                    GiveUpMsg = "§cGiving up trying to revive {0}",
                    RecoverMsg = "§a{0} has recovered from its crash",
                    RestartMsg = "§6{0} is going down for a scheduled restart"
                }
            };
        }

        public void Initialize(IHost host)
        {
            Host = host;

            // Load the plugin configuration from the hosts run-config
            vzConfig = host.LoadPluginConfiguration<VZConfig>(this.GetType());
            if(vzConfig.AutoRestartTime != "" && vzConfig.ServerSync.EnableServerSync)
            {
                vzConfig.AutoRestartTime = "";
                Log("WARNING: Auto restart doesn't play well with the bus yet.  Since the bus is enabled, auto restart has been disabled");
            }

            System.Console.WriteLine(vzConfig.ServerSync.BusAddress);

            // Probably have to rework the plugin system a bit to expose stuff like the world name like in your fork...
            // In the meantime loading the server.properties once again should do the job :D.
            // - clarkx86

            using (StreamReader reader = new StreamReader(File.OpenRead(Path.Join(Path.GetDirectoryName(host.RunConfig.BdsBinPath), "server.properties"))))
                _worldName = Regex.Match(reader.ReadToEnd(), @"^level\-name\=(.+)", RegexOptions.Multiline).Groups[1].Value.Trim();


            bds = (ProcessManager)host.GetPluginByName("ProcessManager");
            backupManager = (BackupManager)host.GetPluginByName("BackupManager");
            renderManager = (RenderManager)host.GetPluginByName("RenderManager");
            bdsWatchdog = (Watchdog)host.GetPluginByName("Watchdog");

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
                // add console ignore patterns
                foreach (string s in vzConfig.IgnorePatterns)
                {
                    if (s != null) bds.AddIgnorePattern(s);
                }
                busEventsMade = true;
            }
            Log(vzConfig.VZStrings.LogInit);

            if (vzConfig.ServerSync.EnableServerSync || vzConfig.DiscordSync.EnableDiscordSync)
            {
                if (vzConfig.DiscordSync.EnableDiscordSync && _discord == null)
                {
                    _discord = new DiscordBot(this);
                }

                // set up events for server up/down: online/offline messages & auto restart handling
                if (!serverEventsMade)
                {
                    bds.RegisterMatchHandler(Vellum.CommonRegex.ServerStarted, (object sender, MatchedEventArgs e) =>
                    {
                        // broadcast the server online message                        
                        if (_discord != null && vzConfig.ServerStatusMessages) _discord.SendMessage(String.Format(vzConfig.VZStrings.ServerUpMsg, _worldName)).GetAwaiter().GetResult();

                        // set up automatic restart timer
                        if (vzConfig.AutoRestartTime != "")
                        {
                            DateTime restartTime = DateTime.Parse(vzConfig.AutoRestartTime);
                            if (restartTime < DateTime.Now)
                            {
                                restartTime.AddDays(1);
                            }
                            double restartMins = restartTime.Subtract(DateTime.Now).TotalMinutes;

                            if (autoRestartTimer != null) autoRestartTimer.Stop();
                            autoRestartTimer = new Timer((restartMins) * 60000);
                            autoRestartTimer.AutoReset = false;
                            autoRestartTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                            {
                                if (!backupManager.Processing && !renderManager.Processing)
                                {
                                    if (vzConfig.ServerStatusMessages && vzConfig.VZStrings.RestartMsg != "") Broadcast(String.Format(vzConfig.VZStrings.RestartMsg, _worldName));
                                    bdsWatchdog.RetryLimit = 0;
                                    bds.Close();
                                    Log("Rebooting Server, waiting 10 seconds to be safe");
                                    System.Threading.Thread.Sleep(10000);
                                    bds.Start();
                                    bdsWatchdog.RetryLimit = 3;

                                    // set up for next restart
                                    DateTime restartTime = DateTime.Parse(vzConfig.AutoRestartTime);
                                    if (restartTime < DateTime.Now)
                                    {
                                        restartTime.AddDays(1);
                                    }
                                    double restartMins = restartTime.Subtract(DateTime.Now).TotalMinutes;
                                    autoRestartTimer.Interval = restartMins * 60000;
                                    autoRestartTimer.Start();
                                }
                                else  
                                {   
                                    // if a backup or render is still going, abort restart and try again in 30 mins
                                    RelayToServer(vzConfig.VZStrings.RestartAbort);
                                    Log(vzConfig.VZStrings.RestartAbort);
                                    autoRestartTimer.Interval = 1800 * 1000;
                                    StartNotifyTimers(1800);
                                }
                            };

                            // set up shutdown messages
                            StartNotifyTimers();
                            autoRestartTimer.Start();
                        }
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
                        crashing = false;
                        if (vzConfig.ServerStatusMessages && vzConfig.VZStrings.RecoverMsg != "") Broadcast(String.Format(vzConfig.VZStrings.RecoverMsg, _worldName));
                    });

                    /*
                    bds.OnShutdownScheduled += (object sender, ShutdownScheduledEventArgs e) =>
                    {
                        // if someone already ran "stop ##" in the console, doing it again doesn't overwrite the previous timer
                        // so if this has already happened, don't redo anything here
                        if (!alreadyStopping)
                        {
                            StartNotifyTimers(e.Seconds);
                            alreadyStopping = true;
                        }
                    };
                    */
                    serverEventsMade = true;
                } else if (serverEventsMade && _bus == null && (sawChatAPIstring || sawCmdAPIstring))
                {
                    _bus = new EZBus(this);
                    _bus.chatSupportLoaded = sawChatAPIstring;
                    _bus.commandSupportLoaded = sawCmdAPIstring;
                }


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
            if (autoRestartTimer != null) autoRestartTimer.Stop();
            if (hiVisWarnTimer != null) hiVisWarnTimer.Stop();
            if (hiVisWarnMsgs != null) hiVisWarnMsgs.Stop();
            if (normalWarnMsg != null) normalWarnMsg.Stop();
            if (rollCallTimer != null) rollCallTimer.Stop();
        }

        private void Broadcast(string message)
        {
            if (_discord != null) _discord.SendMessage(message).GetAwaiter().GetResult();
            if (vzConfig.ServerSync.BroadcastChat && _bus != null) _bus.Broadcast(message);
        }

        private void SendTellraw(string message)
        {
            bds.SendInput("/tellraw @a {\"rawtext\":[{\"text\":\"" + message + "\"}]}");
        }

        internal void RelayToServer(string message)
        {
            // if there's a bus, use that to avoid console confirmation messages. Otherwise use console
            if (_bus != null && _bus.chatSupportLoaded) _bus.Announce(_worldName, message);
            else SendTellraw(message);
        }

        /// <summary>
        /// run a command on this server
        /// </summary>
        /// <param name="command">the command to run</param>
        /// <param name="avoidBus">if true, use SendInput even if the bus is active</param>
        public void Execute(string command, bool avoidBus = false)
        {
            if (_bus != null && _bus.commandSupportLoaded && !avoidBus) _bus.ExecuteCommand(_worldName, command); // make sure all console commands work this way
            else bds.SendInput(command); // this needs extra testing for character/encoding issues and if all in-game commands work this way
        }

        private void StartNotifyTimers(uint s = 0)
        {
            DateTime restartTime = DateTime.Parse(vzConfig.AutoRestartTime);
            if (restartTime < DateTime.Now)
            {
                restartTime.AddDays(1);
            }
            double restartMins = restartTime.Subtract(DateTime.Now).TotalMinutes;

            // high visibility shutdown timers
            if (vzConfig.HiVisShutdown)
            {
                uint timerMins;
                if (s > 0)
                {
                    timerMins = (s > 600) ? (s - 600) / 60 : 0;
                    msCountdown = (s > 600) ? 600000 : s * 1000;
                }
                else if (restartMins > 10)
                {
                    timerMins = (uint)restartMins - 10;
                    msCountdown = 600000;
                }
                else
                {
                    timerMins = 0;
                    msCountdown = (uint)restartMins * 60000;
                }
                // countdown for the warning messages to start
                if (hiVisWarnTimer != null) hiVisWarnTimer.Stop();
                hiVisWarnTimer = new Timer((timerMins * 60000) + 1);
                hiVisWarnTimer.AutoReset = false;
                hiVisWarnTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                {
                    // repeating countdown for each warning message
                    if (hiVisWarnMsgs != null) hiVisWarnMsgs.Stop();
                    hiVisWarnMsgs = new Timer(1000);
                    hiVisWarnMsgs.AutoReset = true;
                    hiVisWarnMsgs.Elapsed += (object sender, ElapsedEventArgs e) =>
                    {
                        msCountdown -= 1000;
                        if ((msCountdown > 60500 && msCountdown % 60000 < 1000) || (msCountdown < 60500 && msCountdown > 10500))
                        {
                            Execute(String.Format("title @a actionbar " + vzConfig.VZStrings.RestartMinWarn, (int)Math.Ceiling((decimal)msCountdown / 60000m)));
                        }
                        else if (msCountdown < 10500)
                        {
                            if (vzConfig.VZStrings.RestartSecSubtl != "")
                                Execute(String.Format("title @a actionbar " + vzConfig.VZStrings.RestartSecSubtl, (int)Math.Ceiling((decimal)msCountdown / 1000m)));
                            if (vzConfig.VZStrings.RestartSecTitle != "")
                                Execute(String.Format("title @a title " + vzConfig.VZStrings.RestartSecTitle, (int)Math.Ceiling((decimal)msCountdown / 1000m)));
                        }
                    };
                    hiVisWarnMsgs.Start();
                };
                hiVisWarnTimer.Start();
            }
            else
            {
                // normal warning message up to 5 mins out from restart
                double countdown = (s > 0) ? s : (restartMins > 5) ? 300 : restartMins * 60;
                string units = (countdown > 119) ? vzConfig.VZStrings.MinutesWord : vzConfig.VZStrings.SecondsWord;
                countdown = (units == vzConfig.VZStrings.SecondsWord) ? countdown : countdown / 60;
                double timerTime = (restartMins > 5) ? restartMins - 5 : 0;

                if (normalWarnMsg != null) normalWarnMsg.Stop();
                normalWarnMsg = new Timer((timerTime * 60000) + 1);
                normalWarnMsg.AutoReset = false;
                normalWarnMsg.Elapsed += (object sender, ElapsedEventArgs e) =>
                {
                    RelayToServer(String.Format(vzConfig.VZStrings.RestartOneWarn, countdown, units));
                };
                normalWarnMsg.Start();
            }
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
        public string AutoRestartTime;
        public bool HiVisShutdown;
        public string[] IgnorePatterns;
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
        public bool BroadcastChat;
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
        public string RestartOneWarn;
        public string RestartMinWarn;
        public string RestartSecTitle;
        public string RestartSecSubtl;
        public string RestartAbort;
        public string MinutesWord;
        public string SecondsWord;
        public string CrashMsg;
        public string GiveUpMsg;
        public string RecoverMsg;
        public string RestartMsg;
    }

    public class MessageEventArgs : EventArgs
    {
        public string Server { get; private set; }
        public string User { get; private set; }
        public string Text { get; private set; }

        public MessageEventArgs(string s, string u, string t)
        {
            Server = s;
            User = u;
            Text = t;
        }
    }
}