# VellumZero-Plugin
A plugin for Vellum 1.3+ to take advantage of ElementZero capabilities

This is now the home of Vellum for Element Zero.  Vellum is currently working on a transition to a plugin model, so I have updated my code accordingly.  Once this transition is complete, I will publish a full release here.  Until then, I'm continuing to work on bugs and features, which will see the light of day in that future release.  

If you want to compile this for some reason, the code in this repository depends on the /dev tree of my fork of vellum

You can find a download of the previous version here though, and the documentation for that version follows below.


# Vellum for Element Zero

**Vellum** is a **Minecraft: Bedrock Dedicated Server** (BDS) backup and map-rendering **automation tool** by [**clarkx86**](https://github.com/clarkx86) and contributors, primarily made to create backups and render interactive maps of your world using [**PapyrusCS**](https://github.com/mjungnickel18/papyruscs).

[**ElementZero**](https://github.com/Element-0/ElementZero) is an altered version of Bedrock Dedicated Server that unlocks many features and adds even more, including APIs for more powerful server side JavaScript and plugin development using compiled C++.  As of this writing there aren't any complex Java-style plugins yet that are both free and in English, but the possibilities are exciting.

**Vellum for ElementZero** is a version of Vellum that takes advantage of ElementZero's capabilities to add even more behind-the-scenes functionality.

## Additional Features Compared to Vellum for Vanilla BDS
- Use ElementZero bus to sync chats between multiple servers
- Connect to a Discord bot to sync chat with a Discord channel
- Create Discord mentions within Minecraft by using `@<User Name>` -- with the <> included
- More features to come! Check the issues list for issues with the Enhancements tag and note their expected release milestone

## Setup
Since some people are finding ElementZero through this fork, these instructions will start from the very beginning and give some background.

ElementZero is only available to run on Windows, or using Wine in Linux.  This is for two reasons as I have been told:
- The Linux version of BDS has been neglected by Mojang and is currently a mess of performance bugs compared to the Windows version
- The Windows version of BDS also gets a performance boost from certain libraries that are optimized for Windows systems.

For these performance reasons, ElementZero has abandoned the Linux version of BDS.

One of ElementZero's stated missions is to run in Linux using Wine, but this adds overhead and is prone to memory issues.  I personally recommend using it on a pure Windows machine. If Linux is the way for you though, see the [**EZ Wine Instructions**](https://github.com/Element-0/ElementZero/wiki/Installing-using-docker-image-for-wine), then come back here to step 3

1. Unzip the **Windows** version of [**Bedrock Dedicated Server**](https://www.minecraft.net/en-us/download/server/bedrock/) into a folder. Edit server.properties and do any other usual vanilla setup at this time.

2. Grab the [**latest ElementZero release**](https://github.com/Element-0/ElementZero/releases) and unzip it into the same folder.

3. Run ElementZero once (using bedrock_server_mod instead of bedrock_server) so it can complete the custom.yaml configuration file. If it doesn't stop itself due to being unconfigured, stop the server as soon as it's done loading.

4. See [**the instructions for using the config.yaml file**](https://github.com/Element-0/ElementZero/wiki/Configuration) and set it up the way you prefer, but the following options must be enabled:
```
mod-enabled: true
ChatAPI:
  enabled: true
```
If you're syncing chat between multiple Bedrock servers, you also need:
```
Bus:
  enabled: true
  name: Your world's name, exactly as it is in server.properties
  host: 127.0.0.1
  port: 4040
```
If you're not familiar with .yaml files, be aware that **indentation is very important**.  If you have to add any of the lines above, it must be indented exactly as shown.

For more context, there's a [**wiki article**](https://github.com/Element-0/ElementZero/wiki/Mod-documentation) describing options for many of the basic plugins included with EZ.

5. If you're syncing chat between multiple Bedrock servers, Install the ElementZero minibus. If not, skip to the next step. *This is not required if you're only syncing chat with Discord*<br />
a. [**Install Rust**](https://www.rust-lang.org/tools/install)<br />
b. Download [**the minibus source code**](https://github.com/codehz/mini_bus_rust) (click the green **Code** button then select Download Zip) and unzip into a temporary folder<br />
c. In a command prompt, navigate to the place in the unzipped folder where **cargo.toml** is<br />
d. Run `cargo install minibus` -- after a while it will be compiled and placed inside your OS's user folder<br />
e. Try running it by typing `minibus`.  If it just sits there not doing anything, **it is working**. If it gives you an error or you're right back at a command prompt again, it's not working.<br />
f. If it's working, you can hit Ctrl-C to stop it for now.  You may also delete the unzipped source code folder.

6. Install [**.NET Core 3.1**](https://dotnet.microsoft.com/download/dotnet-core/3.1) from Microsoft -- you only need the one under the heading **.NET Core Runtime 3.1.X**, the other options have more in them than you need. If you're using Wine, see [**this post**](https://github.com/tomrhollis/vellum-for-EZ/issues/3#issuecomment-667678447)

7. Download the latest release of Vellum for EZ from this repository and unzip it into the same busy server folder that everything else is in

8. Run vellum.exe for the first time. It will quickly generate **configuration.json** and then quit.  If you run it and there's no configuration.json, run it from a command prompt so you can see the error message. (Usually a problem at this stage is from the .NET Core step being skipped.)

9. Edit configuration.json -- see the next section for a full description of your options.  The most important things not to miss are:
 ```
 BdsBinPath: bedrock_server_mod.exe
 WorldName: Your world name exactly as in server.properties
 ```
 For everything else, it's all up to you but if you need help feel free to ask.
 
10. If you're not using the Minibus, simply run vellum.exe and it will start the server for you.  If you're using the EZ Minibus, you'll need to write a script that starts the minibus first, then runs vellum.exe.  Done!

## Configuration 
When you run this tool for the first time, it will generate a `configuration.json` and terminate. Before restarting the tool, edit this file to your needs. Here is a quick overview:

NOTE: This fork is still based on Vellum 1.2.2 for now, so does not have the Render LowPriority setting
```
KEY               VALUE               ABOUT
----------------------------------------------------------
REQUIRED SETTINGS
-----------------
BdsBinPath         String  (!)        Absolute path to the the Bedrock Server
                                      executable (similar to "/../../bedrock_server"
                                      on Linux or "/../../bedrock_server.exe" on 
                                      Windows)

WorldName          String  (!)        Name of the world located in the servers
                                      /worlds/ directory (specify merely the name and
                                      not the full path)
---------------
BACKUP SETTINGS
---------------
EnableBackups      Boolean (!)        Whether to create world-backups as .zip-archives

BackupInterval     Double             Time in minutes to take a backup and create a
                                      .zip-archive

ArchivePath        String             Path where world-backup archives should be
                                      created

BackupsToKeep      Integer            Amount of backups to keep in the "ArchivePath"-
                                      directory, old backups automatically get deleted

OnActiviyOnly      Boolean            If set to "true", vellum will only perform a
                                      backup if at least one player has connected
                                      since the previous backup was taken, in order to
                                      only archive worlds which have actually been
                                      modified

StopBeforeBackup   Boolean            Whether to stop, take a backup and then restart
                                      the server instead of taking a hot-backup

NotifyBeforeStop   Integer            Time in seconds before stopping the server for a
                                      backup, players on the server will be
                                      notified with a chat message
                                      
HiVisNotifications Boolean            Show actionbar and title warnings starting 10
                                      minutes before backup

BackupOnStartup    Boolean            Whether to create a full backup of the specified
                                      world before starting the BDS process
                                      IMPORTANT: It is highly encouraged to leave
                                      this setting on "true"

PreExec            String             An arbitrary command that gets executed before
                                      each backup starts

PostExec           String             An arbitrary command that gets executed after
                                      each has finished
---------------
RENDER SETTINGS
---------------
EnableRenders      Boolean (!)        Whether to create an interactive map of the world
                                      using PapyrusCS

PapyrusBinPath     String             Absolute path to the papyrus executable (similar
                                      to "/../../PapyrusCs" on Linux or
                                      "/../../PapyrusCs.exe" on Windows)

PapyrusOutputPath  String             Output path for the rendered papyrus map

RenderInterval     Double             Time in minutes to run a backup and render map

PapyrusGlobalArgs  String             Global arguments that are present for each
                                      rendering task specified in the "PapyrusArgs"-
                                      array
                                      IMPORTANT: Do not change the already provided
                                      --world and --ouput arguments

PapyrusTasks       String [Array]     An array of additional arguments for papyrus,
                                      where each array entry executes another
                                      PapyrusCS process after the previous one has
                                      finished (e.g. for rendering of multiple
                                      dimensions)                                   
-----------------
CHAT SETTINGS
-----------------
EnableChatSync        Boolean            Enables this whole chat section

BusAddress            String  (!)        The address of the ElementZero bus that all
                                         the servers are connected to
                                      
BusPort               Integer (!)        The port where the bus is listening

OtherServers          String Array (!)   The world names of all the other servers to
                                         broadcast messages to

PlayerConnMessages    Boolean            Broadcasts player connect/disconnect
                                         messages

ServerStatusMessages  Boolean            Broadcasts server up/down messages
                                      
EnableDiscord         Boolean            Enables the discord functionality
                                         **If you're not using the EZ minibus,
                                         keep EnableChatSync true but leave
                                         OtherServers as an empty array: [ ]
                                     
DiscordToken          String             The secret token for the discord bot

DiscordChannel        ULong              The numerical ID for the discord channel where
                                         the bot should send the messages

DiscordMentions       Boolean            Whether to allow users to create @ mentions
                                         from inside Minecraft using @<Username>
                                         (@everyone and @here are completely disabled
                                         no matter how this option is set)
                                         
LatinOnly             Boolean            Only allow basic latin characters. (Extended
                                         Unicode characters can cause lag issues,
                                         which trolls can weaponize)
                                         
DiscordCharLimit      Integer (!)        Cuts off Discord messages at a specific length.
                                         0 means unlimited
-------------------
ADDITIONAL SETTINGS
-------------------
QuietMode          Boolean (!)        Suppress notifying players in-game that vellum
                                      is creating a backup and render

HideStdout         Boolean (!)        Whether to hide the console output generated by
                                      the PapyrusCS rendering process, setting this
                                      to "true" may help debug your configuration but
                                      will result in a more verbose output

BusyCommands       Boolean (!)        Allow executing BDS commands while the tool is
                                      taking backups

CheckForUpdates    Boolean (!)        Whether to check for updates on startup

StopBdsOnException Boolean (!)        Should vellum unexpectedly crash due to an
                                      unhandled exception, this sets whether to send a 
                                      "stop" command to the BDS process to prevent it
                                      from keep running in detached mode otherwise 

BdsWatchdog        Boolean (!)        Monitor BDS process and restart if unexpectedly
                                      exited. Will try to restart process a maximum of 
                                      3 times. This retry count is reset when BDS
                                      instance is deemed stable.
----------------------------------------------------------
* values marked with (!) are required, non-required values should be provided depending on your specific configuration
```

## Parameters & Commands
### Parameters
Overview of available launch parameters:
```
PARAMETER                             ABOUT
----------------------------------------------------------
-c | --configuration                  Specifies a custom configuration file.
                                      (Default: configuration.json)

-h | --help                           Prints an overview of available parameters.
```
Parameters are optional and will default to their default values if not specified.

### Commands
vellum also provides a few new, and overloads some existing commands that you can execute to force-invoke backup- or rendering tasks and schedule server shutdowns.
```
COMMAND                               ABOUT
----------------------------------------------------------
force start backup                    Forces taking a (hot-)backup (according to your
                                      "StopBeforeBackup" setting)

force start render                    Forces PapyrusCS to execute and render your
                                      world

stop <time in seconds>                Schedules a server shutdown and notifies players
                                      in-game

reload vellum                         Reloads the previously specified (or default)
                                      configuration file

updatecheck                           Fetches the latest BDS & vellum version and
                                      displays them in the console
```

## How should I contribute?
If you encounter any problems using this version, please use the issue tracker here. If it turns out to be a problem with the main codebase, the issue will be closed here and directed over there.  Any suggestions related to functions handled by both versions of Vellum should go to the main Vellum repository.

## Linux version?
Since ElementZero only runs on the Windows version of BDS, a Linux version of this fork wouldn't make sense.

## License?
Vellum has not selected a license at the time of writing.  I have obtained permission for what's going on here.

