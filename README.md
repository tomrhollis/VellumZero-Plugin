# VellumZero
A plugin for Vellum 1.3+ to take advantage of ElementZero capabilities.  What does that mean?

[**Vellum**](https://github.com/clarkx86/vellum) is a **Minecraft: Bedrock Dedicated Server** (BDS) maintenance and automation tool by [**clarkx86**](https://github.com/clarkx86) and contributors, which can (among other things) create backups and render web-based maps of your Bedrock world using [**PapyrusCS**](https://github.com/mjungnickel18/papyruscs).

[**ElementZero**](https://github.com/Element-0/ElementZero) is an altered version of Bedrock Dedicated Server that unlocks all the single-player features Mojang disabled and adds even more, including more powerful server side JavaScript, and plugin development using compiled C++.  As of this writing there aren't any complex Java-style plugins yet that are both free and in English, but the possibilities are exciting, and several people have published useful scripts.

**VellumZero** ties the two together for more behind the scenes functionality!

## Features
- Connect to a Discord bot to sync chat with a Discord channel
- Create Discord mentions within Minecraft by using `@<User Name>` -- with the <> included
- Use ElementZero bus to sync chats between multiple servers
- Update scoreboards on your servers with lists of what servers are online and the names of players on your network
- Option to automatically add your other servers' players to the online list on the menu screen
- Customizable text for any message the plugin sends
- Release package includes [**any other Vellum plugin I've made**](https://github.com/tomrhollis/Vellum-Plugins), such as AutoRestart
- More features to come! Check the issues list for issues with the Enhancements tag
- **Now works in Linux through Wine -- see the bottom of this document for new Linux instructions**

## Installation (Windows)
- Requires the following already set up: Bedrock Dedicated Server, ElementZero, and [**Vellum**](https://github.com/clarkx86/vellum). If using Discord then also a Discord bot, if you have multiple servers then also the [**ElementZero Minibus**](https://github.com/codehz/mini_bus_rust)
- To use this plugin, the [**.NET Core Runtime**](https://dotnet.microsoft.com/download/dotnet-core/thank-you/runtime-3.1.8-windows-x64-installer) is required. Please install that, and use the non-bundled version of Vellum.
- In ElementZero's custom.yaml, enable the ChatAPI mod if it's not already enabled.
- **Only if you're using the Minibus**, make sure the Bus mod is enabled in custom.yaml with these settings:
```
  Bus:
    enabled: true
    name: *the_world_name_here*
    host: 127.0.0.1
    port: 4040
    reconnect-delay: 10
```
- Notes on the Bus configuration: `name:` needs to be the same as this server's world name for all the other servers with VellumZero to find it.  Also the `port:` in custom.yaml is DIFFERENT from the port in configuration.json.  It's 4040 in custom.yaml, 8234 in configuration.json.
- Download the latest release from this repository and unzip it based on the instructions on the release page
- Copy the sample configuration below into the Plugins section of vellum's configuration.json if it's not there already (or run vellum once or twice and it will generate the default settings for the plugin in configuration.json)
- Read the descriptions of the settings below, and make any changes as needed
- Set your Windows terminal defaults to *Disable QuickEdit Mode* (see the first issue in Common Issues below for detailed instructions)
- Run vellum as normal, and the plugin will be loaded
- If there are strange characters in the text the plugin sends to Minecraft, see the second issue below.

## Common Issues / Troubleshooting (Mostly Windows)
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
          "ChatMsg": "(§6{0}§r) <{3}{1}{4}> {2}",
          "PlayerJoinMsg": "(§6{0}§r) {2}{1}{3} Connected",
          "PlayerLeaveMsg": "(§6{0}§r) {2}{1}{3} Left",
          "ServerUpMsg": "(§6{0}§r): §aOnline§r",
          "ServerDownMsg": "(§6{0}§r): §cOffline§r",
          "MsgFromDiscord": "(§d{0}§r) [§b{1}§r] {2}",
          "CrashMsg": "§c{0} crashed, attempting to revive it",
          "GiveUpMsg": "§cGiving up trying to revive {0}",
          "RecoverMsg": "§a{0} has recovered from its crash",
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
PlayerConnMessages         (true/false) Generate messages when a player connects or disconnects

ServerStatusMessages       (true/false) Generate messages related to the server going up or down

UserDB                     Path to the ElementZero user information database.

EssentialsDB               Path to the ElementZero essentials mod database (name tags stored there)

-----------------------
DISCORD SETTINGS
-----------------------
EnableDiscordSync          (true/false) Whether to use any of the features in this section

DiscordToken               The secret token for the Discord bot you added to your Discord server

DiscordChannel             The number ID of the channel on your server that messagse should go to
                           If you can't find it, you probably need to enable Developer Mode on your Discord
                           
DiscordMentions            (true/false) Whether to let people create mentions on discord using
                           @<User Name> (with the <>) -- note, @everyone and @here are disabled no matter what
                           
LatinOnly                  (true/false) Remove any non-Latin characters before sending a Discord message 
                           to Minecraft. This is to defend against trolls using extended Unicode 
                           characters, which can cause lag for some reason
                           
DiscordCharLimit           Trim the size of Discord messages to this amount before sending to Minecraft
                           0 = Unlimited

-----------------------
SERVER SYNC SETTINGS
-----------------------
EnableServerSync          (true/false) Whether to use any of the features in this section

OtherServers              A list of the bus names of all the other servers on your network
                          Each server's bus name should be the same as their world name for best results
                          [ ] = none
                          ["Survival", "Creative", "Testing Ground", "Etc"] = example of a list
                          
BusAddress                The address of the server where the Minibus is running
                          Leave as the default 127.0.0.1 if it's in the same place as this copy of Vellum
                          
BusPort                   The port the Minibus is using--not the same as the port in config.yml!
                          Unless you've changed it from 8234 when compiling the minibus yourself,
                          this should not be changed.

DisplayOnlineList         (true/false) Whether to add players from other servers into 
                          the online list on this server

OnlineListScoreboard      The name of the scoreboard in game where 
                          the names of players on other servers are stored
                          THE SCOREBOARD NAME MUST BE THE SAME ON ALL OF YOUR SERVERS TO WORK PROPERLY

ServerListScoreboard      The name of the scoreboard in game storing 
                          the names of all online servers on the network
                          THE SCOREBOARD NAME MUST BE THE SAME ON ALL OF YOUR SERVERS TO WORK PROPERLY

-----------------------------------------------------------------------------------------------------
Text Settings - Don't worry about these unless you really want to change the wording the plugin uses
-----------------------------------------------------------------------------------------------------
ChatMsg                   The format of a chat message sent by a Minecraft player
                          {0} = The server's world name
                          {1} = The player's name
                          {2} = The message they sent
                          {3} = The player's prefix from essentials.db
                          {4} = The player's postfix from essentials.db
                          
PlayerJoinMsg             The format of the message broadcast when a player joins
                          {0} = The server's world name
                          {1} = The player's name
                          {2} = The player's prefix from essentials.db
                          {3} = The player's postfix from essentials.db

PlayerLeaveMsg            The format of the message broadcast when a player leaves
                          {0} = The server's world name
                          {1} = The player's name
                          {2} = The player's prefix from essentials.db
                          {3} = The player's postfix from essentials.db

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

## Linux Installation
Thanks to help from @bennydiamond, there's now instructions for running Vellum and ElementZero together in Wine.  To do so:
- Install Wine version 5 or greater (search for instructions related to your distro)
- Follow Microsoft's [**instructions for installing .NET Core 3.1 runtime**](https://docs.microsoft.com/en-us/dotnet/core/install/linux) for your distro
- Using your package manager, install `expect-dev` or `expect-devel` (whichever it is in your distro)
- Download and unzip the WINDOWS version of Bedrock Dedicated Server
- Download and unzip ElementZero into the same directory.
- Copy vcruntime140_1.dll and vcruntime140.dll from a Windows 10 machine, place in the BDS server directory in Linux
- Download and unzip the LINUX version of Vellum in the same directory. `chmod +x vellum` if necessary. Run vellum once so it creates configuration.json
- Edit custom.yaml to enable the ChatAPI mod. If using Minibus, configure that as shown in the Windows section
- Create a `plugins` subdirectory and unzip any Vellum plugins you're using into there
- Create a launch script like this in your server directory:
```
#!/bin/bash
export WINEDLLOVERRIDES="vcruntime140_1,vcruntime140=n;mscoree,mshtml,explorer.exe,winemenubuilder.exe,services.exe,playplug.exe=d"
export WINEDEBUG=-all
unbuffer -p wine64 bedrock_server_mod.exe
```
- `chmod +x ./thescriptyoujustmade.sh`
- Open configuration.json and set `BdsBinPath: "thescriptyoujustmade.sh"`. Save and exit
- Run Vellum once to check that it starts.  Once the server is done loading, type `stop` to close the server. Vellum will now write all the plugin options to configuration.json
- Open configuration.json again and set everything up the way you like it
- That should be it, run vellum to start the server.

Vellum may be able to launch the official EZ docker container in a similar way, but I've had mixed results with that so far.  Nothing I'm confident enough in to put here yet.  I will come back to looking into docker at a later time.  If you figure it out before me, please let me know so I can update this section!

Minecraft formatting will probably not work properly using the instructions above due to an encoding problem somewhere in the Russian nesting dolls of programs that are running to make this work. Until I figure out a workaround, you'll probably have to use this for the VZStrings section of configuration.json instead:
```
        "VZStrings": {
          "ChatMsg": "({0}) <{3}{1}{4}> {2}",
          "PlayerJoinMsg": "({0}) {2}{1}{3} Connected",
          "PlayerLeaveMsg": "({0}) {2}{1}{3} Left",
          "ServerUpMsg": "({0}): Online",
          "ServerDownMsg": "({0}): Offline",
          "MsgFromDiscord": "({0}) [{1}] {2}",
          "CrashMsg": "{0} crashed, attempting to revive it",
          "GiveUpMsg": "Giving up trying to revive {0}",
          "RecoverMsg": "{0} has recovered from its crash"
          "LogInit": "Initialized",
          "LogDiscInit": "Starting Discord",
          "LogDiscConn": "Discord Connected",
          "LogDiscDC": "Discord Disconnected",
          "LogBusConn": "Bus Connected",
          "LogEnd": "Unloading plugin",
        }
```