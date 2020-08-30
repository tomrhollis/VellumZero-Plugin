// VellumZero Plugin
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
        private uint msCountdown;
        private bool alreadyStopping = false;

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

            // need to make these events even if plugin is disabled so if vellum is reloaded to enable it we'll have the info they get
            if (!busEventsMade)
            {
                // detect that the bus is loaded properly for chat
                Host.Bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for ChatAPI$", (object sender, MatchedEventArgs e) =>
                {
                    if (vzConfig.ServerSync.EnableServerSync && _bus == null)
                    {
                        _bus = new EZBus(this);
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
                    if (_bus != null) _bus.commandSupportLoaded = true;
                });
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
                if (vzConfig.ServerStatusMessages && !serverEventsMade)
                {
                    Host.Bds.OnServerStarted += (object sender, EventArgs e) =>
                    {
                        // broadcast the server online message
                        string message = String.Format(vzConfig.VZStrings.ServerUpMsg, Host.WorldName);
                        Broadcast(message);

                        // set up automatic restart timer
                        if (vzConfig.AutoRestartMins > 0)
                        {
                            autoRestartTimer = new Timer(vzConfig.AutoRestartMins * 60000);
                            autoRestartTimer.AutoReset = false;
                            autoRestartTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                            {
                                Host.Bds.SendInput("stop");
                            };

                            // set up shutdown messages
                            StartNotifyTimers();
                            
                            autoRestartTimer.Start();
                        }
                    };
                    Host.Bds.OnServerExited += (object sender, EventArgs e) =>
                    {
                        // send server disconnect message
                        string message = String.Format(vzConfig.VZStrings.ServerDownMsg, Host.WorldName);
                        if (_bus != null) _bus.Broadcast(message);
                        if (_discord != null) _discord.SendMessage(message).GetAwaiter().GetResult();

                        StopTimers();
                    };                    
                    Host.Bds.OnShutdownScheduled += (object sender, ShutdownScheduledEventArgs e) =>
                    {
                        // if someone already ran "stop ##" in the console, doing it again doesn't overwrite the previous timer
                        // so if this has already happened, don't redo anything here
                        if (!alreadyStopping)
                        {
                            StopTimers();
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
                    };

                    Host.Bds.OnPlayerLeft += (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();
                                                
                        // display leave message
                        if (vzConfig.PlayerConnMessages) LeaveMessage(user);
                    };
                    playerEventsMade = true;
                }
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
        }

        private void Broadcast(string message)
        {
            if (_discord != null) _discord.SendMessage(message);
            if (_bus != null) _bus.Broadcast(message);
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
                hiVisWarnTimer = new Timer((timerMins * 60000) + 1);
                hiVisWarnTimer.AutoReset = false;
                hiVisWarnTimer.Elapsed += (object sender, ElapsedEventArgs e) =>
                {
                    // repeating countdown for each warning message
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
                uint countdown = (s > 0) ? s : (vzConfig.AutoRestartMins > 5) ? 300 : vzConfig.AutoRestartMins * 60;
                string units = (countdown > 119) ? vzConfig.VZStrings.MinutesWord : vzConfig.VZStrings.SecondsWord;
                countdown = (units == vzConfig.VZStrings.SecondsWord) ? countdown : countdown / 60;
                uint timerTime = (vzConfig.AutoRestartMins > 5) ? vzConfig.AutoRestartMins - 5 : 0;
                
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
                        BusPort = 8234
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
                        MinutesWord = "minutes",
                        SecondsWord = "seconds"
                    }
                };
                using (StreamWriter writer = new StreamWriter(configFile))
                {
                    writer.Write(JsonConvert.SerializeObject(vzConfig, Formatting.Indented));
                }
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