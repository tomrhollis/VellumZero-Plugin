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
        internal bool commandSupportLoaded = false;

        /// <summary>
        /// Initializes the bus object
        /// </summary>
        /// <param name="parent">The object for the main class of this plugin</param>
        public EZBus(VellumZero parent)
        {
            _vz = parent;
            ssConfig = parent.vzConfig.ServerSync;
            localAddress = String.Format("http://{0}:{1}/", ssConfig.BusAddress, ssConfig.BusPort);
            _vz.Log("Bus Connected");
        }

        /// <summary>
        /// Make any character substitutions necessary to make the string work when passed to the bus
        /// </summary>
        /// <param name="text">the string to clean</param>
        /// <returns>the cleaned string</returns>
        private string CleanString(string text)
        {
            text = Regex.Replace(text, @"§", "\\u00a7");
            text = Regex.Replace(text, @"""", "\\\"");
            return text;
        }

        /// <summary>
        /// Send a text message to all the other servers on the bus list
        /// </summary>
        /// <param name="text">the message to send</param>
        public void Broadcast(string text)
        {
            try
            {
                HttpClient client = new HttpClient();
                text = CleanString(text);
                string address = "{0}map/{1}/announce";

                foreach (string name in ssConfig.OtherServers)
                {
                    StringContent content = new StringContent(text);
                    client.PostAsync(String.Format(address, localAddress, name), content);
                }
            }
            catch (Exception e)
            {
                _vz.Log("Something went wrong with the bus: " + e.Message);
            }
        }

        /// <summary>
        /// Get an update of the online status and player list of the servers on the bus
        /// </summary>
        public void UpdateStatus()
        {
            throw new NotImplementedException();
        }
    }
}
