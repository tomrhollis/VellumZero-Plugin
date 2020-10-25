// VellumZero Plugin
// EZBus.cs
// Author: Tom Hollis
// August 2020

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using VellumZero.Models;

namespace VellumZero
{
    /// <summary>
    /// Handles interaction with the ElementZero minibus for VellumZero
    /// </summary>
    class EZBus
    {
        private ServerSyncConfig ssConfig;
        private VellumZero _vz;
        private string localAddress;
        internal bool commandSupportLoaded;
        internal bool chatSupportLoaded;
        private HttpClient httpClient;
        public List<Server> Network;

        static readonly CancellationTokenSource s_cts = new CancellationTokenSource();

        internal uint PlayerCount
        {
            get
            {
                return (uint)Network.Sum(s => { return (s.Online) ? s.Players.Count : 0; });
            }
        }

        internal uint TotalSlots
        {
            get
            {
                return (uint)Network.Sum(s => { return (s.Online) ? s.PlayerSlots : 0; });
            }
        }

        internal uint OnlineServerCount
        {
            get
            {
                return (uint)Network.Sum(s => { return (s.Online) ? 1 : 0; });
            }
        }


        /// <summary>
        /// Initializes the bus object
        /// </summary>
        /// <param name="parent">The object for the main class of this plugin</param>
        public EZBus(VellumZero parent)
        {
            _vz = parent;
            commandSupportLoaded = false;
            chatSupportLoaded = false;
            ssConfig = parent.vzConfig.ServerSync;
            localAddress = String.Format("http://{0}:{1}/", ssConfig.BusAddress, ssConfig.BusPort);

            httpClient = new HttpClient();
            
            Network = new List<Server>();
            Network.Add(_vz.ThisServer);
            foreach(string s in _vz.vzConfig.ServerSync.OtherServers)
            {
                Network.Add(new Server(_vz, s, 0, false));
            }
        }

        /// <summary>
        /// Make any character substitutions necessary to make the string work when passed to the bus
        /// </summary>
        /// <param name="text">the string to clean</param>
        /// <returns>the cleaned string</returns>
        private string CleanString(string text)
        {
            text = Regex.Replace(text, @"§", "\u00a7");
            text = Regex.Replace(text, @"""", @"\\""");
            return text;
        }

        /// <summary>
        /// Send a text message to all the other servers on the bus list
        /// </summary>
        /// <param name="text">the message to send</param>
        public void Broadcast(string text)
        {            
            foreach (string name in ssConfig.OtherServers)
            {
                Announce(name, text);
            }
        }

        /// <summary>
        /// Sends an announcement to one server on the bus
        /// </summary>
        /// <param name="destination">the name of the server</param>
        /// <param name="text">the text to send</param>
        public void Announce(string destination, string text)
        {
            text = CleanString(text);
            string address = "{0}map/{1}/announce";
            StringContent content = new StringContent(text);

            try
            {
                s_cts.CancelAfter(3000);
                string result = httpClient.PostAsync(String.Format(address, localAddress, destination), content).Result.Content.ReadAsStringAsync().Result;
                Thread.Sleep(200);
            }
            catch (Exception e)
            {
                _vz.Log("Something went wrong with the bus: " + e.Message);
            }
        }

        /// <summary>
        /// Execute a command on the specified server on the bus
        /// </summary>
        /// <param name="destination">which server to execute the command on</param>
        /// <param name="command">the command to execute</param>
        /// <returns>A string of the JSON output from the command</returns>
        internal string ExecuteCommand(string destination, string command)
        {
            command = Regex.Replace(command, @"§", "\u00a7");
            StringContent content = new StringContent(command);
            string address = "{0}map/{1}/execute_command.json";
            string result = "";
            
            try
            {
                s_cts.CancelAfter(3000);
                result = httpClient.PostAsync(String.Format(address, localAddress, destination), content).Result.Content.ReadAsStringAsync().Result;
                Thread.Sleep(200);
            }
            catch (Exception e)
            {
                _vz.Log("Something went wrong with the bus: " + e.Message);
            }
            return result;
        }

        /// <summary>
        /// Check whether the specified player is known to the server
        /// Don't use this for the current server, use playerDB instead for compatibility when people don't use bus
        /// </summary>
        /// <param name="destination">which server to check for the player</param>
        /// <param name="player">which player name/xuid/uuid to look for</param>
        /// <returns>A string of the output from the server</returns>
        internal string FindPlayer(string destination, string player)
        {
            player = CleanString(player);
            StringContent content = new StringContent(player);
            string address = "{0}map/{1}/find-player";
            string result = "";

            try
            {
                s_cts.CancelAfter(3000);
                result = httpClient.PostAsync(String.Format(address, localAddress, destination), content).Result.Content.ReadAsStringAsync().Result;
                Thread.Sleep(200);
            }
            catch (Exception e)
            {
                _vz.Log("Something went wrong with the bus: " + e.Message);
            }
            return result;
        }

        internal bool BroadcastCommand(string command, bool skipLocal=true)
        {
            bool infoChanged = false;
            foreach(Server s in Network)
            {
                if (skipLocal && s == _vz.ThisServer) continue;
                if (s.Online)
                {
                    if (ExecuteCommand(s.WorldName, command) == "")
                    {
                        s.MarkAsOffline();
                        infoChanged = true;
                    }
                }
            }
            return infoChanged;
        }


        internal bool RefreshBusServerInfo()
        {
            List<string> players = new List<string>();
            bool updated = false;

            // get list of people from other servers
            foreach (Server s in Network)
            {
                string result = ExecuteCommand(s.WorldName, "list");
                if (result != "")
                {
                    // parse the player list
                    Regex r = new Regex("\"players\": \"([^\"]+?)\"");
                    Match m = r.Match(result);
                    if (m.Groups.Count > 1)
                        updated = (updated) ? updated : s.BusInfoUpdate(m.Groups[1].ToString());
                } else
                {                    
                    s.MarkAsOffline();
                    updated = true;
                }
            }
            return updated;
        }
    }
}
