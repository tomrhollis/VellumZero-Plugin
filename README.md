(Note: not quite ready for a release, but it's almost there. Just a little more testing, not long now)

# VellumZero
A plugin for Vellum 1.3+ to take advantage of ElementZero capabilities.  What does that mean?

[**Vellum**](https://github.com/clarkx86/vellum) is a **Minecraft: Bedrock Dedicated Server** (BDS) maintenance and automation tool by [**clarkx86**](https://github.com/clarkx86) and contributors, which can (among other things) create backups and render web-based maps of your Bedrock world using [**PapyrusCS**](https://github.com/mjungnickel18/papyruscs).

[**ElementZero**](https://github.com/Element-0/ElementZero) is an altered version of Bedrock Dedicated Server that unlocks all the single-player features Mojang disabled and adds even more, including more powerful server side JavaScript, and plugin development using compiled C++.  As of this writing there aren't any complex Java-style plugins yet that are both free and in English, but the possibilities are exciting, and several people have published useful scripts.

**VellumZero** ties the two together for more behind the scenes functionality!

## Important Note about Linux
Be aware that since ElementZero is only available for the Windows version of Bedrock Dedicated Server, this plugin will only work with Vellum on Windows.  Future support is planned for a Docker-Wine container, but for now Linux is not supported in any way.

## Features
- Connect to a Discord bot to sync chat with a Discord channel
- Create Discord mentions within Minecraft by using `@<User Name>` -- with the <> included
- Use ElementZero bus to sync chats between multiple servers
- Updates scoreboards on your servers with lists of what servers are online and the names of players on your network
- Option to automatically add players on other servers to the visible online list on all servers
- Customizable text for any message the plugin sends
- VellumZero package includes [**any other Vellum plugins I make**](https://github.com/tomrhollis/Vellum-Plugins), such as AutoRestart to restart your server daily
- More features to come! Check the issues list for issues with the Enhancements tag

## Installation
- Requires the following already set up: Bedrock Dedicated Server, ElementZero, and Vellum. If using Discord then also a Discord bot, if you have multiple servers then also the [**ElementZero Minibus**](https://github.com/codehz/mini_bus_rust)
- Download the latest release on the right
- In whatever folder your vellum.exe is in, unzip it into a *plugins* subfolder
- Copy the sample configuration below into the Plugins section of vellum's configuration.json (or run vellum once and quit, and it will generate the default settings for the plugin in configuration.json)
- Read the descriptions of the settings below, and make any changes as needed
- Set your Windows terminal defaults to *Disable QuickEdit Mode* (see the first issue in Common Issues below for detailed instructions)
- Run vellum as normal, and the plugin will be loaded

## Common Issues / Troubleshooting
**Problem:** The server will freeze randomly until I hit enter, then minutes or hours of stuff it hadn't been doing all happens at once<br/>
**Solution:** The command line entered QuickEdit mode, which is a very unhelpful feature in this case. To turn this off completely:
- Right click the icon in the top left corner of any terminal window
- Select Defaults
- On the Options tab, Uncheck QuickEdit mode
- Close all terminal windows and start fresh

**Problem:** Strange characters appear in game or in Discord, especially when there should be color codes in the message.<br/>
**Solution:** Windows terminals often don't use the modern standard of character encoding by default. To fix this, copy vellumzero.bat from the package zip file and use that to start the server.  It will switch the terminal to UTF-8 encoding and then run vellum as normal.  (Or add `chcp 65001` to your server script if you already have one)

**Problem:** NullReferenceException crash on startup<br/>
**Solution:** Something in configuration.json is probably not formatted properly.  Try backing it up, deleting it, and running vellum to generate a new configuration.json.  Copy your settings back in, being careful to use the same format as the defaults.

## Sample Configuration
```
"VellumZero": {
      "Enable": true,
      "Config": {
        "PlayerConnMessages": true,
        "ServerStatusMessages": true,
        "DiscordSync": {
          "EnableDiscordSync": false,
          "DiscordToken": "",
          "DiscordChannel": 0,
          "DiscordMentions": true,
          "LatinOnly": false,
          "DiscordCharLimit": 0
        },
        "ServerSync": {
          "EnableServerSync": false,
          "OtherServers": [ ],
          "BusAddress": "127.0.0.1",
          "BusPort": 8234,
          "DisplayOnlineList": true,
          "OnlineListScoreboard": "Online",
          "ServerListScoreboard": "Servers"
        },
        "VZStrings": {
          "ChatMsg": "(§6{0}§r) <{1}> {2}",
          "PlayerJoinMsg": "(§6{0}§r) {1} Connected",
          "PlayerLeaveMsg": "(§6{0}§r) {1} Left",
          "ServerUpMsg": "(§6{0}§r): §aOnline§r",
          "ServerDownMsg": "(§6{0}§r): §cOffline§r",
          "MsgFromDiscord": "(§d{0}§r) [§b{1}§r] {2}",
          "CrashMsg": "§c{0} crashed, attempting to revive it",
          "GiveUpMsg": "§cGiving up trying to revive {0}",
          "RecoverMsg": "§a{0} has recovered from its crash"
          "LogInit": "Initialized",
          "LogDiscInit": "Starting Discord",
          "LogDiscConn": "Discord Connected",
          "LogDiscDC": "Discord Disconnected",
          "LogBusConn": "Bus Connected",
          "LogEnd": "Unloading plugin",
        }
      }
    },
```

## Configuration Guide
```
-----------------------
GENERAL SETTINGS
-----------------------
PlayerConnMessages        (true/false) Generate messages when a player connects or disconnects

ServerStatusMessages      (true/false) Generate messages related to the server going up or down

-----------------------
DISCORD SETTINGS
-----------------------
EnableDiscordSync          (true/false) Whether to use any of the features in this section

DiscordToken               The secret token for the Discord bot you added to your Discord server

DiscordChannel             The number ID of the channel on your server that messagse should go to
                           If you can't find it, you probably need to enable Developer Mode on your Discord
                           
DiscordMentions            (true/false) Whether to let people create mentions on discord using
                           @<User Name> (with the <>) -- note, @everyone and @here are disabled no matter what
                           
LatinOnly                  (true/false) Remove any non-Latin characters before sending a Discord message to Minecraft
                           This is to defend against trolls using extended Unicode characters, which can
                           cause lag for some reason
                           
DiscordCharLimit           Trim the size of Discord messages to this amount before sending to Minecraft
                           0 = Unlimited

-----------------------
SERVER SYNC SETTINGS
-----------------------
EnableServerSync          (true/false) Whether to use any of the features in this section

OtherServers              A list of the world names of all the other servers on your network
                          [ ] = none
                          ["Survival", "Creative", "Testing Ground", "Etc"] = example of a list
                          
BusAddress                The address of the server where the Minibus is running
                          Leave as the default 127.0.0.1 if it's in the same place as this copy of Vellum
                          
BusPort                   The port the Minibus is using                          

DisplayOnlineList         (true/false) Whether to add players from other servers into the online list on this server

OnlineListScoreboard      The name of the scoreboard in game where the names of players on other servers are stored
                          THE SCOREBOARD NAME MUST BE THE SAME ON ALL OF YOUR SERVERS TO WORK PROPERLY

ServerListScoreboard      The name of the scoreboard in game storing the names of all online servers on the network
                          THE SCOREBOARD NAME MUST BE THE SAME ON ALL OF YOUR SERVERS TO WORK PROPERLY

-----------------------------------------------------------------------------------------------------
Text Settings - Don't worry about these unless you really want to change the wording the plugin uses
-----------------------------------------------------------------------------------------------------
ChatMsg                   The format of a chat message sent by a Minecraft player
                          {0} = The server's world name
                          {1} = The player's name
                          {2} = The message they sent
                          
PlayerJoinMsg             The format of the message broadcast when a player joins
                          {0} = The server's world name
                          {1} = The player's name

PlayerLeaveMsg            The format of the message broadcast when a player leaves
                          {0} = The server's world name
                          {1} = The player's name

ServerUpMsg               The format of the message broadcast when a server comes online
                          {0} = The server world name

ServerDownMsg             The format of the message broadcast when a server goes offline
                          {0} = The server world name

MsgFromDiscord            What a player sees in game when a message arrives from Discord
                          {0} = The word "Discord"
                          {1} = The Discord user's name
                          {2} = The message they sent
                          
CrashMsg                  The message broadcast when the server crashes. Set to "" to disable
                          {0} = The server's world name

GiveUpMsg                 The message broadcast when Vellum's watchdog was unable to revive the server
                          Set to "" to disable this message
                          {0} = The server's world name

RecoverMsg                The message broadcast when Vellum's watchdog successfully revived the server
                          Set to "" to disable this message
                          {0} = The server's world name

LogInit                   The console message when the plugin loads

LogDiscInit               The console message when the Discord functions start

LogDiscConn               The console message when the plugin finishes connecting to the Discord bot

LogDiscDC                 The console message when the plugin disconnects from Discord

LogBusConn                The console message when the plugin detects that ElementZero's bus mod is loaded

LogEnd                    The console message when the plugin is unloaded
```
