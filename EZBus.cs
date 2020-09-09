// VellumZero Plugin
// EZBus.cs
// Author: Tom Hollis
// August 2020

using System;
using System.Net.Http;
using System.Text.RegularExpressions;

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
            _vz.Log("Bus Connected");
            httpClient = new HttpClient();
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
                string result = httpClient.PostAsync(String.Format(address, localAddress, destination), content).Result.Content.ReadAsStringAsync().Result;                
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
                result = httpClient.PostAsync(String.Format(address, localAddress, destination), content).Result.Content.ReadAsStringAsync().Result;
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
                result = httpClient.PostAsync(String.Format(address, localAddress, destination), content).Result.Content.ReadAsStringAsync().Result;
            }
            catch (Exception e)
            {
                _vz.Log("Something went wrong with the bus: " + e.Message);
            }
            return result;
        }
    }
}
