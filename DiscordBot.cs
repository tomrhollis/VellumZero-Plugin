// VellumZero Plugin
// DiscordBot.cs
// Author: Tom Hollis
// August 2020

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        public DiscordBot(VellumZero parent)
        {            
            _vz = parent;
            dsConfig = parent.vzConfig.DiscordSync;
            _vz.Log(_vz.vzConfig.VZStrings.LogDiscInit);
            Task.Run(RunBotAsync);
        }

        /// <summary>
        /// Set up and start monitoring discord
        /// </summary>
        public async Task RunBotAsync()
        {
            _client = new DiscordSocketClient();
            _client.MessageReceived += ReceiveMessage;

            await _client.LoginAsync(TokenType.Bot, dsConfig.DiscordToken);
            await _client.StartAsync();

            Channel = (ITextChannel)_client.GetChannel(dsConfig.DiscordChannel);

            _vz.Log(_vz.vzConfig.VZStrings.LogDiscConn);
        }

        /// <summary>
        /// Shut down cleanly
        /// </summary>
        public async Task ShutDown()
        {
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
            
            if (message.Author.IsBot || message.Channel.Id != dsConfig.DiscordChannel) return;

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
            if (dsConfig.LatinOnly) msgText = Regex.Replace(msgText, @"[\P{IsBasicLatin}]", "");
            if (dsConfig.DiscordCharLimit > 0)
            {
                int msgLength = msgText.Length;
                msgText = msgText.Substring(0, dsConfig.DiscordCharLimit);
                if (msgLength > dsConfig.DiscordCharLimit) msgText += "...";
            }
            msgText = Regex.Replace(msgText, @"[\r\n]{1,2}", "\\n");


            // post on this server (other servers will handle discord themselves)
            MessageEventArgs a = new MessageEventArgs("Discord", message.Author.Username, msgText);
            _vz.CallHook(VellumZero.Hook.DISCORD_REC, a);
            _vz.RelayToServer(String.Format(_vz.vzConfig.VZStrings.MsgFromDiscord, a.Server, a.User, a.Text));
        }
        
        /// <summary>
        /// Process text to make it ready for discord and send it
        /// </summary>
        /// <param name="text">The text to send to discord</param>
        public async Task SendMessage(string text)
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
            await location.SendMessageAsync(message);
        }
    }
}