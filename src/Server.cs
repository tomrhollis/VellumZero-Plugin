using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using Vellum.Automation;

namespace VellumZero
{
    public class Server
    {
        public string WorldName { get; private set; }
        public List<Player> Players { get; private set; }
        public DateTime BootTime { get; private set; }
        public bool Online { get; private set; }
        public uint PlayerSlots { get; private set; }

        private VellumZero vz;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="n">The name of the world</param>
        /// <param name="s">The max number of players the world can hold</param>
        /// <param name="o">Whether the server is currently online or not</param>
        public Server(VellumZero v, string n, uint s = 0, bool o = false)
        {
            vz = v;
            WorldName = n;
            PlayerSlots = s;
            Players = new List<Player>();
            if (o) MarkAsOnline();
            else Online = o;
        }

        public void MarkAsOnline()
        {
            Online = true;
            BootTime = DateTime.Now;

            // send updates
            if (this == vz.ThisServer)
            {
                if (vz.vzConfig.ServerStatusMessages) vz.Broadcast(String.Format(vz.vzConfig.VZStrings.ServerUpMsg, WorldName));
                if (vz.Discord != null) vz.Discord.UpdateDiscordTopic();
                if (vz.Bus != null && vz.vzConfig.ServerSync.ServerListScoreboard != "") vz.Bus.BroadcastCommand($"scoreboard players add \"{WorldName}\" \"{vz.vzConfig.ServerSync.ServerListScoreboard}\" 0");
            } 
            else if (vz.vzConfig.ServerSync.ServerListScoreboard != "")
                vz.Execute($"scoreboard players add \"{WorldName}\" \"{vz.vzConfig.ServerSync.ServerListScoreboard}\" 0");
}

        public void MarkAsOffline()
        {
            Online = false;
            foreach(Player p in Players)
            {
                p?.Leave();
            }

            // send updates
            if (this == vz.ThisServer)
            {
                if (vz.vzConfig.ServerStatusMessages) vz.Broadcast(String.Format(vz.vzConfig.VZStrings.ServerDownMsg, WorldName));
                if (vz.Discord != null) vz.Discord.UpdateDiscordTopic();
                if (vz.Bus != null && vz.vzConfig.ServerSync.ServerListScoreboard != "") 
                    vz.Bus.BroadcastCommand($"scoreboard players reset \"{WorldName}\" \"{vz.vzConfig.ServerSync.ServerListScoreboard}\"");
            }
            else if (vz.vzConfig.ServerSync.ServerListScoreboard != "")
                vz.Execute($"scoreboard players reset \"{WorldName}\" \"{vz.vzConfig.ServerSync.ServerListScoreboard}\"");
        }

        internal void AddPlayer(Player p)
        {
            Players.Add(p);
        }

        internal void RemovePlayer(ulong xuid)
        {
            Player gone = Players.FirstOrDefault(p => xuid == (p?.Xuid ?? ulong.MaxValue));
            if (gone != null)
            {
                gone.Leave();
                Players.Remove(gone);
            }
            else vz.Log("Error: could not remove record for player logging off with xuid " + xuid);
        }


        internal bool ConsoleInfoUpdate(MatchedEventArgs e)
        {
            bool updated = false;
            string playerList = "";

            if (e.Matches.Count > 0)
            {
                if (e.Matches[0].Groups.Count > 2)
                {
                    uint slots;
                    slots = uint.Parse(e.Matches[0].Groups[2].Value);
                    if (slots != PlayerSlots)
                    {
                        PlayerSlots = slots;
                        updated = true;
                    }
                }
                if (e.Matches[0].Groups.Count > 3) playerList = e.Matches[0].Groups[3].Value;
            }
            return UpdateInfo(playerList, updated);
        }

        internal bool BusInfoUpdate(string info)
        {
            Regex r;
            Match m;
            bool updated = false;
            string playerList = "";

            if (this != vz.ThisServer && !Online) // don't do this check for local server, could result in bad info when shutting down
            {
                MarkAsOnline();
                updated = true;
            }

            r = new Regex("\"maxPlayerCount\": \"(\\d+)\",");
            m = r.Match(info);
            if (m.Groups.Count > 1)
            {
                uint slots;
                slots = uint.Parse(m.Groups[1].Value);
                if (slots != PlayerSlots)
                {
                    PlayerSlots = slots;
                    updated = true;
                }
            }

            r = new Regex("\"players\": \"(.*)\",");
            m = r.Match(info);
            if (m.Groups.Count > 1)
                playerList = m.Groups[1].ToString();

            return UpdateInfo(playerList, updated);
        }

        private bool UpdateInfo(string list, bool updated = false)
        {
            List<string> names = (list != null && list.Trim() != "") ? list.Split(',').ToList() : new List<string>();
            List<string> currentList = new List<string>();
            List<Player> removeList = new List<Player>();
            Players.ForEach(p =>
            {
                if (!names.Contains(p?.Name))
                {
                    removeList.Add(p);
                    updated = true;
                }
                else if (p != null) currentList.Add(p?.Name);
            });
            removeList.ForEach(p =>
            {
                Players.Remove(p);
            });
            if(names.Count > 0)
            {
                names.ForEach(n =>
                {
                    if (!currentList.Contains(n))
                    {
                        Players.Add(Player.CreateInstance(vz, this, n, 0));
                        updated = true;
                    }
                });
            }
            return updated;
        }
    }
}
