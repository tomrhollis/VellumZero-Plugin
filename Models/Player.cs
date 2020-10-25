using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;

namespace VellumZero.Models
{
    public class Player
    {
        public string Name { get; private set; }
        public ulong Xuid { get; private set; }
        public string Uuid { get; private set; }
        public string Prefix { get; private set; }
        public string Postfix { get; private set; }
        // for later: public string DiscordUID { get; private set; }

        private VellumZero vz;
        private Server s;

        private Player(VellumZero v, Server s, string n, ulong x=0)
        {
            vz = v;
            Name = n;
            Xuid = x;

            GetUUID();
            RefreshAffixes();

            if (s == vz.ThisServer)
            {
                // display join message
                if (vz.vzConfig.PlayerConnMessages) vz.JoinMessage(this);

                // update Discord topic unless other servers will do it
                if (vz.Discord != null) vz.Discord.UpdateDiscordTopic().GetAwaiter().GetResult();

                // update all servers
                if (vz.Bus != null)
                {
                    vz.Bus.BroadcastCommand($"scoreboard players add \"{Name}\" \"{vz.vzConfig.ServerSync.OnlineListScoreboard}\" 0");
                    vz.Bus.BroadcastCommand($"scoreboard players add \"{s.WorldName}\" \"{vz.vzConfig.ServerSync.ServerListScoreboard}\" 1");
                }
            }
        }

        public static Player CreateInstance(VellumZero v, Server s, string n, ulong x = 0)
        {
            if (x == 0 && s == v.ThisServer) return null; // xuid required for local server (may change this later)
            return new Player(v, s, n, x);
        }

        /// <summary>
        /// Cleanup to run when a player leaves
        /// </summary>
        ~Player()
        {
            if (vz.ThisServer.Online && s == vz.ThisServer) // don't gum up the works with DC messages if the server is going down
            {
                // display leave message
                if (vz.vzConfig.PlayerConnMessages) vz.LeaveMessage(this);

                // update all servers
                if (vz.Bus != null)
                {
                    vz.Bus.BroadcastCommand($"scoreboard players reset \"{Name}\" \"{vz.vzConfig.ServerSync.OnlineListScoreboard}\"");
                    vz.Bus.BroadcastCommand($"scoreboard players remove \"{s.WorldName}\" \"{vz.vzConfig.ServerSync.ServerListScoreboard}\" 1");
                }
            }           
        }

        /// <summary>
        /// Fill in the UUID property for this player using EZ's database
        /// </summary>
        public void GetUUID()
        {
            try
            {
                using var conn = new SQLiteConnection(@"Data Source=" + vz.vzConfig.UserDB + ";Version=3;");
                conn.Open();
                using (var cmd = new SQLiteCommand($"SELECT hex(uuid) FROM user WHERE xuid='{Xuid}'", conn))
                {
                    using SQLiteDataReader rdr = cmd.ExecuteReader();
                    if (rdr.Read())
                    {
                        Uuid = rdr.GetString(0);
                    }
                    if (rdr.Read())
                    {
                        vz.Log("Error: Database lookup returned more than one user for XUID " + Xuid);
                        return;
                    }
                }
                conn.Close();
            }
            catch (SQLiteException)
            {
                vz.Log("Error accessing user information in a SQLite database");
            }
        }

        /// <summary>
        /// Get EZ prefix and postfix for a specific user
        /// </summary>
        public void RefreshAffixes()
        {
            try
            {
                using var conn = new SQLiteConnection(@"Data Source=" + vz.vzConfig.EssentialsDB + ";Version=3;");
                conn.Open();
                using (var cmd = new SQLiteCommand($"SELECT prefix,postfix FROM custom_name WHERE uuid = x'{Uuid}'", conn))
                {
                    using SQLiteDataReader rdr = cmd.ExecuteReader();
                    if (rdr.Read())
                    {
                        Prefix = rdr.GetString(0);
                        Postfix = rdr.GetString(1);
                    }
                    if (rdr.Read())
                    {
                        vz.Log("Error: Database lookup returned more than one user for UUID " + Uuid);
                        return;
                    }
                }
                conn.Close();
            }
            catch (SQLiteException)
            {
                vz.Log("Error accessing name affix information in a SQLite database");
            }
        }
    }
}
