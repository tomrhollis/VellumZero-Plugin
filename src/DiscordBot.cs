// VellumZero Plugin
// DiscordBot.cs
// Author: Tom Hollis
// August 2020

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Discord;
using Discord.WebSocket;

namespace VellumZero
{
    /// <summary>
    /// Handles interaction with Discord for VellumZero
    /// </summary>
    internal class DiscordBot
    {
        private DiscordSocketClient _client;
        private DiscordSyncConfig dsConfig;
        private IMessageChannel Channel;
        private VellumZero _vz;
        private DateTime lastModify;
        private string currentTopic;
        private string nextTopic;
        private Task msgSender = null;
        private System.Timers.Timer topicUpdater;
        private Queue<string> messageQueue;
        
        public DiscordBot(VellumZero parent)
        {            
            _vz = parent;
            dsConfig = parent.vzConfig.DiscordSync;
            lastModify = DateTime.Now;
            messageQueue = new Queue<string>();
            _vz.Log(_vz.vzConfig.VZStrings.LogDiscInit);
            Task.Run(RunBotAsync);
        }

        /// <summary>
        /// Set up and start monitoring discord
        /// </summary>
        public async Task RunBotAsync()
        {
            _client = new DiscordSocketClient();
            if (!(_vz.vzConfig.ServerSync.EnableServerSync && !_vz.vzConfig.ServerSync.DiscordController)) _client.MessageReceived += ReceiveMessage;

            _client.Ready += async () =>
            {
                Channel = (ITextChannel)_client.GetChannel(dsConfig.DiscordChannel);
                _vz.Log(_vz.vzConfig.VZStrings.LogDiscConn);
            };

            await _client.LoginAsync(TokenType.Bot, dsConfig.DiscordToken);
            await _client.StartAsync();            

            // set game (but not if it's a server sync situation and this isn't the discord controller instance of vellum)
            if (_vz.vzConfig.VZStrings.Playing != "" && !(_vz.vzConfig.ServerSync.EnableServerSync && !_vz.vzConfig.ServerSync.DiscordController))
                await _client.SetGameAsync(String.Format(_vz.vzConfig.VZStrings.Playing));
        }

        /// <summary>
        /// Shut down cleanly
        /// </summary>
        public async Task ShutDown()
        {
            UpdateDiscordTopic(_vz.vzConfig.VZStrings.ChannelTopicOffline);
            await _client.LogoutAsync();
            await _client.StopAsync();
            _vz.Log(_vz.vzConfig.VZStrings.LogDiscDC);
        }

        /// <summary>
        /// Handle messages received from discord. Process them to translate user and channel tags and apply any configuration settings
        /// </summary>
        /// <param name="arg">The message data received</param>
        private async Task ReceiveMessage(SocketMessage arg)
        {
            Regex userRegex = new Regex(@"<@[!]?(\d+)>");
            Regex channelRegex = new Regex(@"<#(\d+)>");

            // set up the channel connection
            if (Channel == null) Channel = (ITextChannel)_client.GetChannel(dsConfig.DiscordChannel);

            var message = arg as SocketUserMessage;
            string msgText = message.Content;

            if (message.Channel.Id != dsConfig.DiscordChannel || message.Author.Id == _client.CurrentUser.Id) return;

            // if it's blank, don't send anything to server
            // but for now, check the embeds and see what's there. might do something with them later
            if (msgText.Trim() == "")
            {
                // handle DiscordSRV event messages
                if (message.Embeds.Count > 0)
                {
                    foreach(Embed e in message.Embeds)
                    {
                        if (msgText != "") msgText += "\n";
                        msgText += e.Author.Value.Name;
                    }
                } else return;
            }

            // find user and channel mention codes
            MatchCollection userMatches = userRegex.Matches(msgText);
            MatchCollection channelMatches = channelRegex.Matches(msgText);

            // replace each userID strings with @Name
            foreach (Match um in userMatches)
            {
                ulong user = ulong.Parse(um.Groups[1].ToString());
                msgText = Regex.Replace(msgText, um.Value, (await Channel.GetUserAsync(user)).Username);
            }
            
            // replace channelID strings with #name
            foreach (Match cm in channelMatches)
            {
                ulong channelId = ulong.Parse(cm.Groups[1].ToString());
                var channel = _client.GetChannel(channelId) as SocketTextChannel;  
                
                string name = channel?.Name;
                msgText = Regex.Replace(msgText, cm.Value, "#"+name);
            }

            // handle size and text type exclusions (lots of extended unicode can cause client performance issues)
            if (dsConfig.LatinOnly) msgText = Regex.Replace(msgText, @"[^\u0009-\u024f]", "");
            if (dsConfig.DiscordCharLimit > 0 && msgText.Length > dsConfig.DiscordCharLimit)
                msgText = msgText.Substring(0, dsConfig.DiscordCharLimit) + "...";
            msgText = Regex.Replace(msgText, @"[\r\n]{1,2}", "\\n");


            // post on this server (other servers will handle discord themselves)
            MessageEventArgs a = new MessageEventArgs("Discord", message.Author.Username, msgText);
            _vz.CallHook(VellumZero.Hook.DISCORD_REC, a);
            string finishedMessage = String.Format(_vz.vzConfig.VZStrings.MsgFromDiscord, a.Server, a.User, a.Text);

            // if the bus is running, prioritize sending messages through that
            // otherwise send through console
            if (_vz.Bus != null && _vz.Bus.chatSupportLoaded) _vz.Bus.Broadcast(finishedMessage, skipLocal: false);
            else if (_vz.bds.IsRunning) _vz.SendTellraw(finishedMessage);
            else _vz.Log("Error: No bus or BDS process to send this Discord message to: " + finishedMessage); 
        }
        
        /// <summary>
        /// Process text to make it ready for discord and send it
        /// </summary>
        /// <param name="text">The text to send to discord</param>
        public void SendMessage(string text, Embed embed = null)
        {   
            string message = Regex.Replace(text, @"[§\u00a7][0-9a-gk-or]", ""); // strip minecraft formatting if any
            message = Regex.Replace(message, @"@everyone", "everyone");
            message = Regex.Replace(message, @"@here", "here");
            var location = _client.GetChannel(dsConfig.DiscordChannel) as SocketTextChannel;

            if (dsConfig.DiscordMentions)
            {
                // set up to do the find and replace for discord mentions
                Regex userRegex = new Regex(@"@<([^>]+)>");
                Regex channelRegex = new Regex(@"#([-a-z]+)", RegexOptions.IgnoreCase);
                MatchCollection userMatches = userRegex.Matches(message);
                MatchCollection channelMatches = channelRegex.Matches(message);

                // replace @<username> with a mention string
                foreach (Match um in userMatches)
                {
                    var users = location.Guild.Users.Where(user => user.Username.ToLower() == um.Groups[1].Value.ToLower());
                    string userMention = null;

                    if (users.Count() > 0)
                    {
                        userMention = users.First().Mention;
                        message = Regex.Replace(message, "@<" + um.Groups[1].Value + ">", userMention);
                    }
                }

                // replace #channel-name with a mention string
                foreach (Match cm in channelMatches)
                {
                    var channels = location.Guild.Channels.Where(channel => channel.Name == cm.Groups[1].Value.ToLower());
                    ulong? channelId = null;

                    if (channels.Count() > 0)
                    {
                        channelId = channels.First().Id;
                        message = Regex.Replace(message, "#" + cm.Groups[1].Value, "<#" + channelId + ">");
                    }
                }
            }
            // send
            messageQueue.Enqueue(message);
 
            if (msgSender == null || msgSender.IsCompleted)
            {
                msgSender = Task.Run(() =>
                {
                    while(messageQueue.Count > 0)
                    {
                        Thread.Sleep(1000);
                        string msg = messageQueue.Dequeue();
                        try
                        {
                            location.SendMessageAsync(msg, embed: embed).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            _vz.Log("Discord Error, Message Not Sent: " + msg + "\nUnderlying Problem: " + ex.GetType().ToString() + " - " + ex.Message);
                        }
                    }
                });
            }
        }


        /// <summary>
        /// Update the Discord channel's topic message
        /// </summary>
        internal void UpdateDiscordTopic(string topic = "")
        {
            // if there are multiple servers and this one isn't authorized to send status updates to discord, abort
            if (_vz.vzConfig.ServerSync.EnableServerSync && !_vz.vzConfig.ServerSync.DiscordController) return;

            // otherwise if there are multiple servers and it is authorized, try to send using the multi-server string first
            if (_vz.vzConfig.ServerSync.EnableServerSync && _vz.vzConfig.ServerSync.DiscordController && _vz.vzConfig.VZStrings.ChannelTopicMulti != "")
            {
                if (topic == "") topic = String.Format(_vz.vzConfig.VZStrings.ChannelTopicMulti, _vz.Bus.PlayerCount, _vz.Bus.OnlineServerCount);
            }

            // as a last resort, check if we should send a single-server message instead
            else if (_vz.vzConfig.VZStrings.ChannelTopic != "")
            {
                if (topic == "") topic = String.Format(_vz.vzConfig.VZStrings.ChannelTopic, _vz.ThisServer.Players.Count, _vz.ThisServer.PlayerSlots);
            }

            // if the situation doesn't fall into either of the categories above
            if (topic == "" || topic == currentTopic) return;

            nextTopic = topic;
            if (DateTime.Now.Subtract(lastModify).TotalMinutes < 5)
            {
                if (topicUpdater == null)
                {
                    topicUpdater = new System.Timers.Timer((5 - DateTime.Now.Subtract(lastModify).TotalMinutes) * 60000);
                    topicUpdater.AutoReset = false;
                    topicUpdater.Elapsed += TopicFunction;
                    topicUpdater.Start();
                }
            } else
            {
                TopicFunction(null, EventArgs.Empty);
            }
        }

        private void TopicFunction(object sender, EventArgs e)
        {
            try
            {
                if (Channel == null) Channel = (ITextChannel)_client.GetChannel(dsConfig.DiscordChannel);

                lastModify = DateTime.Now;
                ((ITextChannel)Channel).ModifyAsync(chan =>
                {
                    chan.Topic = nextTopic;
                }).GetAwaiter().GetResult();
                currentTopic = nextTopic;
               
                topicUpdater = null;
            }
            catch (Exception ex)
            {
                _vz.Log("Discord Error, Topic Not Updated.\nUnderlying Problem: " + ex.GetType().ToString() + " - " + ex.Message);
            }
        }
    }
}