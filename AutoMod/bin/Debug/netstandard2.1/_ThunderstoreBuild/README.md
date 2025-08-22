# AutoModeration (as known as AutoMod)
### Automods like discord!

## Instructions (Provided by Zera)
In order to utilize this plugin, you must install the plugin and load the game for it to generate the configuration file.
Once loaded, close the game and head into `BepinEx\Configs` (or if you use Thunderstore, Gale, or R2ModMan, should be in the config settings of your profile). Once there, look for a file called `com.s0apy.cfg` (or search AutoModeration in the config settings if you use Thunderstore, Gale, or R2ModMan).

## General Configurations
| **Setting**               | **Default** | **Description**                                                                 |
|---------------------------|-------------|---------------------------------------------------------------------------------|
| `Enable Mod`              | ✅        | Rather or not you want to enable AutoModeration.                               |
| `Disable in Singleplayer`| ✅        | If you use this shit on singleplayer, We're not helping you. Don't PM the dev. |
| `Monitored Channels`      | GLOBAL      | Comma-separated list of chat channels to monitor. Case-insensitive.            |

## Word Filters
| **Setting**                  | **Default** | **Description**                                                                 |
|------------------------------|-------------|---------------------------------------------------------------------------------|
| `Blocked Words`              | —           | Comma-separated list. Use `*` for wildcards. (Ex: `*badword*, rude*, *insult`) |
| `Allowed phrases whitelist`  | —           | Optional. Phrases exempt from blocking. (Ex: `grapefruit, have a nice day`)    |

## Advanced Filters (.NET 7.0 (C#) / Insensitive)
| **Setting**        | **Default** | **Description**                                             |
|--------------------|-------------|-------------------------------------------------------------|
| `Regex Patterns`   | —           | Comma-separated list of Regex patterns for advanced filtering. |

## Punishments
| **Setting**            | **Default** | **Description**                                                  |
|------------------------|-------------|------------------------------------------------------------------|
| `Enable Host Actions`  | ✅        | Enables Host actions like `BAN` or `KICK`.                       |
| `Action Type`          | KICK        | Action taken when warning limit is reached.                      |

## Warning System
| **Setting**                  | **Default** | **Description**                                                                 |
|------------------------------|-------------|---------------------------------------------------------------------------------|
| `Enabled`                    | ✅        | Enables warning system before punishment.                                      |
| `Warnings Until Action`      | 3           | Infractions allowed before action is triggered.                                |
| `Reset Warnings on Disconnect`| ✅       | Clears warning count on disconnect.                                            |
