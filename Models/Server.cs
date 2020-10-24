using System;
using System.Collections.Generic;

namespace VellumZero.Models
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
            if (o) MarkAsOnline();
            else MarkAsOffline();
            Players = new List<Player>();
        }

        public void MarkAsOnline()
        {
            Online = true;
            BootTime = DateTime.Now;

            // send updates
            vz.Broadcast(String.Format(vz.vzConfig.VZStrings.ServerUpMsg, vz._worldName));
            vz.UpdateDiscordTopic();
        }

        public void MarkAsOffline()
        {
            Online = false;

            // send updates
            vz.Broadcast(String.Format(vz.vzConfig.VZStrings.ServerUpMsg, vz._worldName));
            vz.UpdateDiscordTopic();
        }

        /// <summary>
        /// Update information for this server based on a string returned from commandline or bus
        /// </summary>
        /// <param name="info">the string to parse</param>
        /// <param name="bus">if it came from the bus or not (or not = it came from the command line)</param>
        /// <returns>whether anything was updated</returns>
        public bool UpdateInfo(string info, bool bus = false)
        {
            bool updated = false;
            if (!Online)
            {
                MarkAsOnline();
                updated = true;
            }

            if (bus)
            {

            } else
            {

            }

            // if updated, pass info where it needs to go
            return updated;
        }


        /// <summary>
        /// Update the player count. If there is no minibus, it will use a console command
        /// The string for the console command will be processed by the match event defined when VZ initialized
        /// If it used the bus, the result is processed here instead
        /// </summary>
        private void UpdatePlayerInfo()
        {
            //if (_bus == null) bds.AddIgnorePattern(@"$There are \d+/\d+ players online:");
            string result = Execute("list");
            // if (_bus == null) bds.RemoveIgnorePattern(@"$There are \d+/\d+ players online:");
            if (result != null)
            {
                Regex r;
                Match m;

                if (Servers.Count == 0)
                {
                    r = new Regex("\"maxPlayerCount\": \"(\\d+)\",");
                    m = r.Match(result);
                    if (m.Groups.Count > 1)
                        Servers.Add(new Server(_worldName, Convert.ToUInt32(m.Groups[1].Value), true));
                    if (vzConfig.ServerSync.EnableServerSync && vzConfig.ServerSync.OtherServers.Length > 0) InitializeServerList();
                }

                r = new Regex("\"players\": \"(.*)\",");
                m = r.Match(result);
                if (m.Groups.Count > 1)
                    Servers[0].UpdatePlayers(m.Groups[1].ToString().Split(',').ToList());
            }
        }
    }
}
