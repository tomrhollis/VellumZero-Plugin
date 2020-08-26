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
        private bool librariesLoaded;
        private bool serverEventsMade;
        private bool playerEventsMade;
        private bool busEventsMade;
        private bool chatEventMade;

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
            if (!vzConfig.EnableVZ) return;
            Log(vzConfig.VZStrings.LogInit);

            if (vzConfig.ServerSync.EnableServerSync || vzConfig.DiscordSync.EnableDiscordSync)
            {
                // load the embedded resources for discord connectivity
                if (vzConfig.DiscordSync.EnableDiscordSync && _discord == null)
                {
                    if (!librariesLoaded)
                    {
                        AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(
                        (object sender, ResolveEventArgs args) => { return EmbeddedAssembly.Get(args.Name); });

                        EmbeddedAssembly.Load("System.Collections.Immutable.dll");
                        EmbeddedAssembly.Load("System.Interactive.Async.dll");
                        EmbeddedAssembly.Load("Discord.Net.Core.dll");
                        EmbeddedAssembly.Load("Discord.Net.Commands.dll");
                        EmbeddedAssembly.Load("Discord.Net.Rest.dll");
                        EmbeddedAssembly.Load("Discord.Net.Webhook.dll");
                        EmbeddedAssembly.Load("Discord.Net.WebSocket.dll");
                        librariesLoaded = true;
                        Log(vzConfig.VZStrings.LogDiscLib);
                    }                                        
                    _discord = new DiscordBot(this);                    
                }

                // set up events for server online/offline messages
                if (vzConfig.ServerStatusMessages && !serverEventsMade)
                {
                    Host.Bds.OnServerStarted += (object sender, EventArgs e) =>
                    {
                        string message = String.Format(vzConfig.VZStrings.ServerUpMsg, Host.WorldName);
                        Broadcast(message);                      
                    };
                    Host.Bds.OnServerExited += (object sender, EventArgs e) =>
                    {
                        string message = String.Format(vzConfig.VZStrings.ServerDownMsg, Host.WorldName);
                        if (_bus != null) _bus.Broadcast(message);
                        if (_discord != null) _discord.SendMessage(message).GetAwaiter().GetResult();                        
                    };
                    serverEventsMade = true;
                }

                // connect to the bus only after the bus mod is loaded
                if (vzConfig.ServerSync.EnableServerSync && !busEventsMade)
                {
                    // detect that the bus is loaded properly for chat
                    Host.Bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for ChatAPI$", (object sender, MatchedEventArgs e) =>
                    {
                        if (_bus == null) _bus = new EZBus(this);  
                    });
                    // detect that the bus is loaded properly for commands 
                    Host.Bds.RegisterMatchHandler(@".+\[Bus\].+Load builtin extension for CommandSupport$", (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@".+\[CHAT\].+");
                        Match m = r.Match(e.Matches[0].ToString());
                        if (m.Success) return; // this is someone who knows too much typing the string in chat -- abort!
                    });
                    busEventsMade = true;
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
                if (vzConfig.PlayerConnMessages && !playerEventsMade)
                {
                    Host.Bds.OnPlayerJoined += (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();

                        HandleJoin(user);
                    };

                    Host.Bds.OnPlayerLeft += (object sender, MatchedEventArgs e) =>
                    {
                        Regex r = new Regex(@": (.+),");
                        Match m = r.Match(e.Matches[0].ToString());
                        string user = m.Groups[1].ToString();

                        HandleLeave(user);
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
        }

        public void Unload()
        {
            Log(vzConfig.VZStrings.LogEnd);

            // gracefully disconnect from discord
            if (_discord != null) _discord.ShutDown().GetAwaiter().GetResult();
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

        private void HandleJoin(string user)
        {
            MessageEventArgs a = new MessageEventArgs(Host.WorldName, user, "Connected");
            CallHook(Hook.LOC_PLAYER_CONN, a);

            Broadcast(String.Format(vzConfig.VZStrings.PlayerJoinMsg, a.Server, a.User));
        }

        private void HandleLeave(string user)
        {
            MessageEventArgs a = new MessageEventArgs(Host.WorldName, user, "Left");
            CallHook(Hook.LOC_PLAYER_DC, a);

            Broadcast(String.Format(vzConfig.VZStrings.PlayerLeaveMsg, a.Server, a.User));
        }

        private void Broadcast(string message)
        {
            if (_discord != null) _discord.SendMessage(message);
            if (_bus != null) _bus.Broadcast(message);
        }

        internal void RelayFromDiscord(string server, string user, string text)
        {
            string message = String.Format(vzConfig.VZStrings.MsgFromDiscord, server, user, text);

            // if there's a bus, use that to avoid console confirmation messages. Otherwise use console
            if (_bus != null) _bus.Announce(Host.WorldName, message);
            else Host.Bds.SendTellraw(prefix: "", message: message);                        
        }

        /// <summary>
        /// run a command on this server
        /// </summary>
        /// <param name="command">the command to run</param>
        public void Execute(string command)
        {
            throw new NotImplementedException();
            if (_bus != null && _bus.commandSupportLoaded) _bus.ExecuteCommand(Host.WorldName, command); // make sure all console commands work this way
            // else Host.Bds.SendInput(command); // this needs extra testing for character/encoding issues and if all in-game commands work this way
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
                    HiVisShutdown = true,
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
                        ChatMsg = "(§6{0}§r) [§e{1}§r] {2}",
                        PlayerJoinMsg = "(§6{0}§r) [§e{1}§r] Connected",
                        PlayerLeaveMsg = "(§6{0}§r) [§e{1}§r] Left",
                        ServerUpMsg = "(§6{0}§r): §aOnline§r",
                        ServerDownMsg = "(§6{0}§r): §cOffline§r",
                        MsgFromDiscord = "(§d{0}§r) [§b{1}§r] {2}",
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
            Host.Bds.ConsoleOut("[    VELLUMZERO    ] " + line);
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