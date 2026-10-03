# Flock Memory

BepInEx plugin for **For The King II** that makes herded sheep carry their flock's
highest level forward, instead of spawning each new one at the run's world level.

Part of this repo alongside [Rest Party Panel](../RestPartyPanel/README.md); build
and install both with the repo-level `build.sh`.

## The problem

The Shepherd's **Herd** skill is the only way sheep join the party, and it runs in
exactly one place — `SkillHelper.TryProcAndPerformAdventureSkills`, case
`SKILL_HERD`:

```csharp
string herdSheep = GetHerdSheep(pEntity, skillValue, pGameRandom);
Entity entity2 = CharacterHelper.CreateFollowerCharacter(pEntity, herdSheep, pGameRandom, null, pWorldLevel);
CharacterHelper.TryProgressCharacterEntityToLevel(entity2, pWorldLevel, pGameRandom);
```

`pWorldLevel` is `ProgressionHelper.GetCurrentLevel(pEnv.GameRun)`, captured at
the top of `AdventureHelper.EndTurnActions`. It is the **run's world level** —
never the Shepherd's own level — so a sheep you have invested kibble XP into
leaves nothing behind for the next one.

Worse, it cannot: `FollowerHelper.RemoveFollower` deletes the departing follower
from `GameRun.Entities`, and the overworld's `_decayEntities` sweep force-decays
any dead non-player entity afterwards. By the time the next sheep spawns there is
no record of the last one's level anywhere in the save.

## What this mod does

It keeps a **flock high-water mark** — the highest level any herded sheep has
reached — and spawns the next one at that level instead of the world level.

| | Hook | What |
| --- | --- | --- |
| Capture | prefix on `FollowerHelper.RemoveFollower` | On the way out, read `ProgressionHelper.GetEntityLevel` and raise the mark. This runs *before* `Entities.Remove`, so it is the last moment the departing sheep's level exists. Covers death, contract expiry and dismissal. |
| Apply | prefix on `CharacterHelper.CreateFollowerCharacter` | Floor the `pLevel` argument, so the entity's base stats are built for the right level. |
| Apply | prefix on `CharacterHelper.TryProgressCharacterEntityToLevel` | Floor the `pLevel` argument, so the sheep's XP actually reaches that level. |

Both apply-hooks are gated on the follower config carrying the **`HERD`** tag —
the same test `SkillHelper.GetHerdSheep` uses to pick a sheep. In the shipped
configs that is exactly five entries, all tagged `['COMPANION', 'HERD']`:

```
SHEPHERD_SHEEP_BASIC_00   COMMON     tier 0..3
SHEPHERD_SHEEP_TANK_00    UNCOMMON   tier 0..3
SHEPHERD_SHEEP_SUPPORT_00 UNCOMMON   tier 0..3
SHEPHERD_SHEEP_STUN_00    UNCOMMON   tier 0..3
SHEPHERD_SHEEP_WOLF_00    RARE       tier 0..3
```

Mercenaries from deeds and every other follower are untouched.

Nothing is ever lowered: the mark is a floor, so a sheep always spawns at
`max(worldLevel, flockMark)`. If the mark is unset (a fresh run, or before any
sheep has left) the behaviour is exactly the stock game.

## Where the mark lives, and why it matters for co-op

`GameRunData.Stats` — a plain `Dictionary<string, int>` on the run, under the key
`FTK2_FlockLevel`. The game writes to it directly
(`GameRun.Stats["ENEMIES_KILLED"] = n`) and serialises it into the `*.ftk2` run
save.

It is deliberately **not** `UserData.LocalStats`. That dictionary lives in each
player's own `User.ftk2`, so in a co-op session each peer would read a different
flock level and immediately diverge.

## Co-op

**Both peers must run this plugin with identical settings.**

This is not a host-side patch. FTK2 simulates on every peer — `EndTurnActions`
has no host guard at any of its three call sites — and peers must consume the
same `GameRandom` sequence and converge on hashed state. A change to simulation
output is therefore only safe if it produces the same result everywhere. Every
peer runs the same code over the same shared `GameRun`, so it does; but if only
one peer has the plugin, that peer computes different levels and the session
desyncs.

The plugin logs a warning the first time it sees `NetworkData.PlayingOnlineMultiplayer`
so a mixed session is obvious rather than mysterious. Set `Enabled` to `false` in
`BepInEx/config/cmayfield.ftk2.flockmemory.cfg` to stand down for such runs.

Note that `NetworkDesyncReport/` may already hold reports from vanilla play —
random-stream divergence in loot rolls, combat rolls, enemy selection and trickle
positions, with no plugin involved. This mod is not a cause of those and does not
fix them.

## Install

Unzip `FlockMemory-1.0.0.zip` into the For The King II folder, or drop
`FlockMemory.dll` into `BepInEx/plugins/`. Nothing else is touched. To remove,
delete the file. **Send the same DLL to your co-op partner.**

## Configuration

Written by BepInEx to `BepInEx/config/cmayfield.ftk2.flockmemory.cfg`:

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Apply the flock floor at all. Turn off for co-op sessions where the other peer has not installed the plugin. |
| `VerboseLogging` | `false` | Log every mark read/write and every spawn decision, not just the ones that change something. |
| `WarnWhenCoop` | `true` | Warn once per session when an online multiplayer session is detected. |

## Requirements

Same as any BepInEx 5/6 plugin for this game: **Mono** (not IL2CPP — the game
ships `MonoBleedingEdge`), BepInEx 5.x or 6.x, Harmony 2.x, `net472`. Verified
building against BepInEx 5.4.23.5 / Harmony 2.9.0.0 / Unity 2022.3.41 / Mono.

## Unverified

Behaviour has been traced through `FTK2.dll` and the shipped configs, and the
plugin builds and installs, but **it has not yet been exercised in a live game**.
Expect to shake out the level arithmetic on the first run — set `VerboseLogging`
to `true` for it.