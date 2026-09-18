# Minecraft server inside Valheim

A working Minecraft 1.8.9 server that runs inside Valheim. It speaks the real protocol and needs no server software. The Valheim game process itself holds the port and the world you connect to is the Valheim world you're standing in.

Terrain comes from Valheim's own world generator at one metre per block, so the coastline you sail in Valheim is the coastline you fly over in Minecraft. Greylings, boars and trolls walk across it as Minecraft mobs at their real positions, updated ten times a second. Your Viking is in there too.

## Why

Excel, Outlook, OBS, Obsidian, Blender, PowerPoint, VLC, Word, The Sims 4, Terraria, FL Studio, VS Code, DaVinci Resolve, Unity, Garry's Mod, the whole planet, now Valheim.

Every previous episode put a world inside a program. Valheim already has a world, so this one doesn't make up a flat land.

## What's different about this one

What's new is that the host has a world of its own, and it's alive. Earlier hosts held a Minecraft world. This one gives Minecraft its terrain, its creatures, its clock, its weather and your health bar and keeps them all in step while you play.

## What it does

- Server list ping with the Valheim icon, and an MOTD showing the live world name, in game day, your current biome, your health and whether it's raining
- Offline mode login, no encryption
- Survival mode with flight, so your hearts show and double tapping space still flies
- Terrain streamed from Valheim's `WorldGenerator`, with nine biomes mapped to blocks and water filled to sea level
- Chunks stream as you move, eleven by eleven around you
- Every loaded Valheim creature rendered as a Minecraft mob at its real position, updated ten times a second
- Your Valheim character rendered as a Minecraft player, with its name in the tab list
- Health synced, so taking a hit in Valheim takes hearts off you in Minecraft
- Time of day synced, so Valheim's sunset is Minecraft's sunset
- Weather synced, so rain falls in both at once
- Chat bridged both ways. Minecraft messages appear in Valheim as `[minecraft]`, and Valheim messages appear in Minecraft chat
- Keep alives every 15 seconds
- Starts when a world loads, stops when you go back to the main menu

## What it doesn't do

No saving, no inventory and no other dimensions. Minecraft players can't see each other, and Valheim can't see them at all.

A few limits in this build:

**One way, mostly.** Valheim to Minecraft is live. Minecraft to Valheim is chat only. Break a block and it's gone on your screen only. There's no block store: the Minecraft side is a read only view of Valheim's terrain function.

**Raw terrain.** The generator gives the ground, not what's on it. Your longhouse isn't there, and neither are trees, rocks or any ground you've dug or raised.

**Mobs are position only.** They slide rather than walk, they don't attack, you can't hit them and their Minecraft health bar means nothing. Creatures only exist where Valheim has loaded them, which is around your Viking. Fly far enough in Minecraft and you leave them behind.

**Nothing is let go.** Loaded chunks and sampled heights pile up for as long as you're in the world. Going back to the main menu clears them.

**Minecraft health never reaches zero.** It stops at half a heart. At zero a 1.8 client goes to the death screen, and this server has no way to bring it back.

It's a server in the sense that a client connects to it and receives a world. Set your expectations accordingly.

## Requirements

Valheim on Steam. Built and tested against 0.221.12.

BepInEx 5, installed from BepInExPack Valheim on Thunderstore.

Minecraft Java 1.8.9, protocol 47.

.NET SDK 8 or newer to build. The plugin targets .NET Standard 2.1 and needs nothing at runtime but the game.

Built and tested on macOS 27, Apple Silicon. Windows should work with one extra flag at build time (see Install), but hasn't been tested.

Steam launch options:

```
/usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh %command%
```

Leave the `arch -x86_64` off and the game launches fine with no mods loaded and no error.

## Install

**1.** Install BepInExPack Valheim into the Valheim folder and on a Mac set the launch options above. Start the game once and quit, so `BepInEx/plugins` exists.

**2.** Build. The build compiles against your own copy of the game, so nothing from Valheim or BepInEx ships in this repo.

```
cd McValheim
dotnet build -c Release
```

The build copies `McValheim.dll` into `BepInEx/plugins`. If Valheim isn't in the default Steam location, pass the path:

```
dotnet build -c Release -p:Valheim="/path/to/Valheim"
```

On Windows the game's assemblies are in a different place too, so pass both paths:

```
dotnet build -c Release -p:Valheim="C:\Program Files (x86)\Steam\steamapps\common\Valheim" -p:Managed="C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
```

**3.** Start Valheim and load a world. `BepInEx/LogOutput.log` should say `listening on 25565`.

**4.** Connect Minecraft 1.8.9 to `localhost:25565` via Add Server or Direct Connect.

### Favicon

The server list icon is `assets/favicon.png`, which gets baked into the dll at build time. The repo doesn't ship one: the obvious choice is Valheim's own icon, and that's Iron Gate's artwork. On a Mac you can make one from your own copy of the game:

```
mkdir -p assets
sips -s format png -z 64 64 "$HOME/Library/Application Support/Steam/steamapps/common/Valheim/valheim.app/Contents/Resources/PlayerIcon.icns" --out assets/favicon.png
```

Or use any PNG that is exactly 64x64. The client drops anything else. Without the file the build still works but the server has no icon.

The icon is also baked in, so changing it needs a rebuild.

## Use

Load a world. The server starts itself and stops when you go back to the main menu.

You spawn above wherever your Viking is standing. Fly around and the world fills in as you go. Walk your Viking somewhere and you'll see them move in Minecraft.

Chat from either side lands on the other. There are no commands: anything typed in Minecraft, slash or not, goes to Valheim chat.

The server listens on every network interface, so anyone on your network can join with your machine's IP address.

### Config

`BepInEx/config/it.hostd.mcvalheim.cfg`, written the first time the plugin loads.

| Setting | Default | Effect |
| --- | --- | --- |
| `port` | `25565` | Minecraft listen port |
| `bridgeChat` | `true` | mirror Valheim chat into Minecraft. Minecraft chat always reaches Valheim |

## Troubleshooting

**The server isn't in the list, or the connection is refused.** It only runs while you're in a world. Search `BepInEx/LogOutput.log` for `McValheim`. If there's no `Loading [McValheim` line at all, BepInEx isn't loading.

**The build can't find BepInEx or assembly_valheim.** The game isn't where the build expects it. Pass `-p:Valheim`, plus `-p:Managed` on Windows.

**The plugin throws on load.** Iron Gate renames things between updates. Check these against your `assembly_valheim.dll` in ILSpy:

- `WorldGenerator.instance.GetHeight(float, float)` and `GetBiome(float, float)`
- `ZNet.instance.GetWorldName()`
- `EnvMan.instance.GetDay()`, `GetDayFraction()` and `GetCurrentEnvironment().m_isWet`
- `Player.m_localPlayer.GetCurrentBiome()`, `GetHealth()`, `GetMaxHealth()` and `GetPlayerName()`
- `Character.GetAllCharacters()`
- `Chat.instance.AddString(string)` and the `Chat.OnNewChatMessage` patch target

The chat patch binds by parameter name, so `sender` (a `UserInfo`) and `text` are what matter, not the method's full signature.

**Nothing hurts you in Valheim, so your hearts never move.** That isn't this plugin. A mod that's out of date for your Valheim version can break `Character.Damage` so that no hit lands. Look in `LogOutput.log` for a `MissingMethodException` with `Character.Damage` in the stack trace, then update or remove that mod.

**Minecraft's sun is six hours out of step with Valheim's.** The clock conversion assumes Valheim's day fraction starts at midnight, `fraction * 24000 + 18000`. If your version disagrees, the offset in `Entities.cs` is the number to change.

**The game stutters when you fly fast.** Chunk building runs on Valheim's main thread, four columns a frame. Lower `chunkBudgetPerTick` in `Server.cs` and the world fills in more slowly but more smoothly.

## How it works

**Three threads and one rule.** Anything that touches Valheim runs on the main thread. An accept loop and one thread per connection handle the sockets. `Plugin.Update` is the pump: every frame it snapshots game state, builds chunk columns, syncs creatures and drains the chat queues. Sockets are written from either side under a per-connection lock.

**Chunk budget.** Four columns per frame across all players. Uncapped, 121 columns of noise sampling in one frame stalls the game for a second. Capped, the world fills in visibly as you fly, which looks better anyway.

**Height mapping.** Valheim's water sits at y=30 and Minecraft's at y=62, so world height plus 32 lines the two sea levels up. Mountains at 200 land at 232 and ocean trenches stay above zero, so the whole Valheim height range fits in 1.8's 0 to 255 without scaling.

**Sections.** A column only sends sections up to its highest block or sea level, whichever is higher. Meadows at the coast is four sections, a mountain is fifteen. Sending all sixteen every time triples the bytes spent on air.

**The 1.8 chunk format.** All sections' block arrays, then all block light, then all sky light, then 256 biome bytes. The block array is little endian unsigned shorts of `(id << 4) | meta`, the one place in the protocol that isn't big endian. Sky light is full everywhere, so nothing is dark.

**Join order.** Login Success, Join Game, Spawn Position, Player Abilities, then the 5x5 chunks around spawn, then Player Position And Look. The client sits on "Loading world" until that last packet arrives, no matter what else it has, so each connection waits until the main thread has sent enough chunks.

**Creature mapping.** Valheim's `gameObject.name` minus the `(Clone)` suffix, looked up in a table. Greydwarfs and Draugr become zombies, boars become pigs, lox become cows, trolls become iron golems, surtlings become blazes, Moder becomes the ender dragon and Yagluth the wither. Anything Iron Gate adds later falls through to zombie rather than breaking.

**Entity sync is a diff.** Every 100ms the snapshot of loaded creatures is compared against the last one. New ones get a Spawn Mob, surviving ones get an Entity Teleport, missing ones get a Destroy Entities. It uses Teleport rather than relative move because relative move caps at four blocks, and a charging troll covers more than that between syncs.

**Positions are fixed point.** 1.8 sends entity positions as ints in thirty-seconds of a block, so a Valheim position multiplied by 32 and rounded lands within 3cm of the real one.

**Your Viking needs a tab list entry.** A 1.8 client won't render a player entity it has never seen in the player list, so Player List Item goes out immediately before Spawn Player. The UUID is derived from the character's Unity instance id, and the entry is removed when the character goes, so dying doesn't leave a ghost in the tab list.

**Health.** Valheim health over max health, scaled to Minecraft's 20 points and never below 1. You're in survival so the bar shows, and the Player Abilities packet grants flight and invulnerability. Creative would hide the hearts.

**Time and weather.** Time Update goes out every two seconds from Valheim's day fraction, with the day count as world age. Weather is a Change Game State, sent only when Valheim's environment flips between wet and dry.

**The status ping is a separate connection.** The client connects, pings and disconnects, then reconnects when you click Join. The MOTD in your server list was written for a connection that has already gone.

**Favicon.** Read once from the dll's embedded resources and sent as a base64 `data:image/png` URI in the status JSON.

## Layout

```
src/Packets.cs
src/ChunkBuilder.cs
src/Entities.cs
src/ValheimWorld.cs
src/Server.cs
src/Plugin.cs
tests/
assets/favicon.png   (not in the repo, see Favicon)
```

- `Packets`: varints, strings, big endian numbers, fixed point, angles, UUIDs, the packet framer and a stream reassembler.
- `ChunkBuilder`: turns a sampled height and biome into a 1.8 chunk column.
- `Entities`: the creature mapping table and the clock conversion.

None of those three touch Unity, so all three are unit tested without the game.

- `ValheimWorld`: the sampler that calls `WorldGenerator`, plus the per-frame snapshot of the world, the player and the creatures.
- `Server`: the listener, the connections and the packet handlers.
- `Plugin`: the BepInEx entry point, the main thread pump and the chat patch.

## Licence

MIT. Do what you like with it.

Valheim belongs to Iron Gate. Nothing from the game ships here: the build references your own install.
