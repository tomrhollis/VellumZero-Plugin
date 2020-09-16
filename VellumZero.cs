﻿// VellumZero Plugin
// VellumZero.cs: Main Class
// Author: Tom Hollis
// August 2020

using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using Vellum.Extension;
using System;
using Vellum.Automation;
using System.Text.RegularExpressions;
using System.Timers;
using System.Linq;
using Microsoft.VisualBasic;
using System.Reflection;

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
        private bool alreadyStopping = false;
        private DateTime lastBackup;
        private DateTime lastRender;

        #region PLUGIN
        internal IHost Host;
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

        public void Initialize(IHost host)
        {
            Host = host;
            vzConfig = LoadConfiguration();
            alreadyStopping = false;

            // need to make these events even if plugin is disabled so if vellum is reloaded to enable it we'll have the info they get
            if (!busEventsMade)
            {
                // detect that the bus is loaded properly for chat
                Host.Bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for ChatAPI$", (object sender, MatchedEventArgs e) =>
                {
                    if (vzConfig.ServerSync.EnableServerSync && _bus == null)
                    {
                        _bus = new EZBus(this);
                        if (vzConfig.ServerStatusMessages) _bus.Broadcast(String.Format(vzConfig.VZStrings.ServerUpMsg, Host.WorldName));
                        _bus.chatSupportLoaded = true;
                    }
                    sawChatAPIstring = true;
                });
                // detect that the bus is loaded properly for commands 
                Host.Bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for CommandSupport$", (object sender, MatchedEventArgs e) =>
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
                            _bus.ExecuteCommand(server, $"scoreboard players add \"{Host.WorldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 0");
                        }
                        rollCallTimer.Start();
                    }
                });
                // add console ignore patterns
                foreach(string s in vzConfig.IgnorePatterns)
                {
                    Host.Bds.AddConsoleIgnorePattern(s);
                }
                busEventsMade = true;
            }
            if (!vzConfig.EnableVZ) return;
            Log(vzConfig.VZStrings.LogInit);
                                    
            if (vzConfig.ServerSync.EnableServerSync || vzConfig.DiscordSync.EnableDiscordSync)
            {
                // load the embedded resources for discord connectivity
                if (vzConfig.DiscordSync.EnableDiscordSync && _discord == null)
                {
                    if (!discLibrariesLoaded)
                    {
                        // tell the runtime how to find embedded assemblies
                        // (note: if someone else loads a library with the same name first, it should use the one already loaded in theory)
                        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(
                                (object sender, ResolveEventArgs args) => { return EmbeddedAssembly.Get(args.Name); });

                        EmbeddedAssembly.Load("System.Collections.Immutable.dll");
                        EmbeddedAssembly.Load("System.Interactive.Async.dll");
                        EmbeddedAssembly.Load("Discord.Net.Core.dll");
                        EmbeddedAssembly.Load("Discord.Net.Commands.dll");
                        EmbeddedAssembly.Load("Discord.Net.Rest.dll");
                        EmbeddedAssembly.Load("Discord.Net.Webhook.dll");
                        EmbeddedAssembly.Load("Discord.Net.WebSocket.dll");
                        discLibrariesLoaded = true;
                        Log(vzConfig.VZStrings.LogDiscLib);
                    }                                        
                    _discord = new DiscordBot(this);                    
                }

                // set up events for server up/down: online/offline messages & auto restart handling
                if (!serverEventsMade)
                {
                    Host.Bds.OnServerStarted += (object sender, EventArgs e) =>
                    {
                        // broadcast the server online message                        
                        if (_discord != null && vzConfig.ServerStatusMessages) _discord.SendMessage(String.Format(vzConfig.VZStrings.ServerUpMsg, Host.WorldName));

                        // start tracking backup and render times
                        if (lastBackup == null) lastBackup = DateTime.Now;
                        if (lastRender == null) lastRender = DateTime.Now;

                        if (!serverEventsMade) // have to check again
                        {
                            IPlugin _bm = Host.GetPluginByName("BackupManager");
                            if (_bm != null)
                            {
                                _bm.RegisterHook((byte)BackupManager.Hook.BEGIN, (object sender, EventArgs e) =>
                                {
                                    lastBackup = DateTime.Now;
                                });
                            }

                            IPlugin _rm = Host.GetPluginByName("RenderManager");
                            if (_rm != null)
                            {
                                _rm.RegisterHook((byte)RenderManager.Hook.BEGIN, (object sender, EventArgs e) =>
                                {
                                    lastRender = DateTime.Now;
                                });
                            }
                        }

                        // set up automatic restart timer
                        if (vzConfig.AutoRestartMins > 0)
                        {
                            // move auto restart to prevent overlap with backups and renders
                            int offset = 0;
                            int tries = 0;
                            while ((tries < 10) &&
                                (Math.Abs(IHost.RunConfig.Backups.BackupInterval - DateTime.Now.Subtract(lastBackup).Minutes - (vzConfig.AutoRestartMins + offset)) < 30) &&
                                (Math.Abs(IHost.RunConfig.Renders.RenderInterval - DateTime.Now.Subtract(lastRender).Minutes - (vzConfig.AutoRestartMins + offset)) < 15))
                            {
                                offset += 15;
                                tries++;
                            }

                            if (autoRestartTimer != null) autoRestartTimer.Stop();
                            autoRestartTimer = new Timer((vzConfig.AutoRestartMins + offset) * 60000);
                            autoRestartTimer.AutoReset = false;
                            autoRestartTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                            {
                                if (!Host.Bds.Processing) 
                                {
                                    Host.Bds.SendInput("stop");
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
                    };

                    Host.Bds.OnServerExited += (object sender, EventArgs e) =>
                    {
                        // update servers' scoreboards
                        if (_bus != null)
                        {
                            foreach (string server in vzConfig.ServerSync.OtherServers)
                            {
                                _bus.ExecuteCommand(server, $"scoreboard players reset \"{Host.WorldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\"");
                            }
                        }

                        // send server disconnect message
                        if (vzConfig.ServerStatusMessages)
                        {
                            string message = String.Format(vzConfig.VZStrings.ServerDownMsg, Host.WorldName);
                            if (_discord != null) _discord.SendMessage(message).GetAwaiter().GetResult(); 
                            if (_bus != null) _bus.Broadcast(message);                            
                        }

                        StopTimers();
                    };                    

                    Host.Bds.OnShutdownScheduled += (object sender, ShutdownScheduledEventArgs e) =>
                    {
                        // if someone already ran "stop ##" in the console, doing it again doesn't overwrite the previous timer
                        // so if this has already happened, don't redo anything here
                        if (!alreadyStopping)
                        {
                            StartNotifyTimers(e.Seconds);
                            alreadyStopping = true;
                        }
                    };
                    serverEventsMade = true;
                }

                if (!chatEventMade)
                {
                    Host.Bds.RegisterMatchHandler(@".+\[CHAT\](.+)", (object sender, MatchedEventArgs e) =>
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
                    Host.Bds.OnPlayerJoined += (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();

                        // display join message
                        if (vzConfig.PlayerConnMessages) JoinMessage(user);

                        // update all servers
                        if (_bus != null)
                        {
                            foreach(string server in vzConfig.ServerSync.OtherServers)
                            {
                                _bus.ExecuteCommand(server, $"scoreboard players add \"{user}\" \"{vzConfig.ServerSync.OnlineListScoreboard}\" 0");
                                _bus.ExecuteCommand(server, $"scoreboard players add \"{Host.WorldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 1");
                            }
                        }
                    };

                    Host.Bds.OnPlayerLeft += (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();
                                                
                        // display leave message
                        if (vzConfig.PlayerConnMessages) LeaveMessage(user);

                        // update all servers
                        if (_bus != null)
                        {
                            foreach(string server in vzConfig.ServerSync.OtherServers)
                            {
                                _bus.ExecuteCommand(server, $"scoreboard players reset \"{user}\" \"{vzConfig.ServerSync.OnlineListScoreboard}\"");
                                _bus.ExecuteCommand(server, $"scoreboard players remove \"{Host.WorldName}\" \"{vzConfig.ServerSync.ServerListScoreboard}\" 1");
                            }
                        }
                    };

                    playerEventsMade = true;
                }
            }

            if (vzConfig.ServerSync.EnableServerSync)
            {
                // double check player lists and servers every 5 minutes
                rollCallTimer = new Timer(300000);
                rollCallTimer.AutoReset = true;
                rollCallTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                {
                    RefreshBusServerInfo();                    
                };                
            }
        }

        public void Reload()
        {
            Unload();
            _discord = null;
            _bus = null;
            LoadConfiguration();
            Initialize(Host);
            if (vzConfig.EnableVZ && vzConfig.ServerSync.EnableServerSync && (sawChatAPIstring || sawCmdAPIstring))
            {
                _bus = new EZBus(this);
                _bus.chatSupportLoaded = sawChatAPIstring;
                _bus.commandSupportLoaded = sawCmdAPIstring;
            }
        }

        public void Unload()
        {
            Log(vzConfig.VZStrings.LogEnd);

            // gracefully disconnect from discord
            if (_discord != null) _discord.ShutDown().GetAwaiter().GetResult();

            StopTimers();
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
            MessageEventArgs a = new MessageEventArgs(Host.WorldName, user, chat);
            CallHook(Hook.LOC_PLAYER_CHAT, a);

            Broadcast(String.Format(vzConfig.VZStrings.ChatMsg, a.Server, a.User, a.Text));
        }

        private void JoinMessage(string user)
        {
            MessageEventArgs a = new MessageEventArgs(Host.WorldName, user, "");
            CallHook(Hook.LOC_PLAYER_CONN, a);

            Broadcast(String.Format(vzConfig.VZStrings.PlayerJoinMsg, a.Server, a.User));
        }

        private void LeaveMessage(string user)
        {
            MessageEventArgs a = new MessageEventArgs(Host.WorldName, user, "");
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

        internal void RelayToServer(string message)
        {
            // if there's a bus, use that to avoid console confirmation messages. Otherwise use console
            if (_bus != null && _bus.chatSupportLoaded) _bus.Announce(Host.WorldName, message);
            else Host.Bds.SendTellraw(prefix: "", message: message);                        
        }

        /// <summary>
        /// run a command on this server
        /// </summary>
        /// <param name="command">the command to run</param>
        /// <param name="avoidBus">if true, use SendInput even if the bus is active</param>
        public void Execute(string command, bool avoidBus=false)
        {
            if (_bus != null && _bus.commandSupportLoaded && !avoidBus) _bus.ExecuteCommand(Host.WorldName, command); // make sure all console commands work this way
            else Host.Bds.SendInput(command); // this needs extra testing for character/encoding issues and if all in-game commands work this way
        }

        private void StartNotifyTimers(uint s=0)
        {
            // high visibility shutdown timers
            if (vzConfig.HiVisShutdown)
            {
                uint timerMins;
                if (s > 0)
                {
                    timerMins = (s > 600) ? (s - 600) / 60 : 0;
                    msCountdown = (s > 600) ? 600000 : s * 1000;
                }
                else if (vzConfig.AutoRestartMins > 10)
                {
                    timerMins = vzConfig.AutoRestartMins - 10;
                    msCountdown = 600000;
                }
                else
                {
                    timerMins = 0;
                    msCountdown = vzConfig.AutoRestartMins * 60000;
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
                uint countdown = (s > 0) ? s : (vzConfig.AutoRestartMins > 5) ? 300 : vzConfig.AutoRestartMins * 60;
                string units = (countdown > 119) ? vzConfig.VZStrings.MinutesWord : vzConfig.VZStrings.SecondsWord;
                countdown = (units == vzConfig.VZStrings.SecondsWord) ? countdown : countdown / 60;
                uint timerTime = (vzConfig.AutoRestartMins > 5) ? vzConfig.AutoRestartMins - 5 : 0;

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

        private VZConfig LoadConfiguration()
        {
            VZConfig vzConfig;
            string configFile = Path.Join(Directory.GetCurrentDirectory(), Host.PluginDir, "VellumZero.json");

            if (File.Exists(configFile))
            {
                using (StreamReader reader = new StreamReader(configFile))
                {
                    vzConfig = JsonConvert.DeserializeObject<VZConfig>(reader.ReadToEnd());
                }
            } 
            else
            {
                vzConfig = new VZConfig()
                {
                    EnableVZ = false,
                    PlayerConnMessages = true,
                    ServerStatusMessages = true,
                    AutoRestartMins = 1440,
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
                        LogDiscLib = "Discord Libraries Loaded",
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
                        RestartAbort = "An important process is still running, can't restart now. Trying again in 30 minutes",
                        MinutesWord = "minutes",
                        SecondsWord = "seconds"
                    }
                };
            }

            // write it out in any case so people can see new settings that were added
            using (StreamWriter writer = new StreamWriter(configFile))
            {
                writer.Write(JsonConvert.SerializeObject(vzConfig, Formatting.Indented));
            }

            return vzConfig;
        }

        internal void Log(string line)
        {
            Host.Bds.ConsoleOut("[       VELLUMZERO       ] " + line);
        }
    }
       

    public struct VZConfig
    {
        public bool EnableVZ;
        public bool PlayerConnMessages;
        public bool ServerStatusMessages;
        public uint AutoRestartMins;
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
        public string LogDiscLib;
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