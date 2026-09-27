# Rest Party Panel (FTK2)

BepInEx plugin for **For The King II** that keeps the left-hand party panel populated on the
dungeon rest screen.

## The problem

The game has a left panel, `combat-detail-holder-left` (`CombatDetailHolderViewHelper`), that lists
the non-player characters standing in the venue next to the party's active hero: portrait, name,
HP bar, armour, status effects. It is wired up in `VenueLayoutViewHelper.Initialize` and lives
under the UI root rather than inside a per-phase layout, so it exists in every phase.

It is only ever filled by combat, though. `CombatPhase._refreshCombatDetailsLeft` is the sole
caller of `LeftCombatDetailHolder.ShowDetails(...)`, and it selects entities with:

```csharp
x.Has<CombatComponent>() && x.Has<VenueComponent>() && !CharacterHelper.IsDead(x)
    && !CharacterHelper.IsEnemy(x) && !x.Has<PlayerComponent>()
    && x.Get<CharacterComponent>().CharacterType != eCharacterTypes.INANIMATE
```

`RestPhase` never calls it, and `VenueLayoutViewHelper.HideAll()` / `HideLayout(eRoutes.COMBAT)`
blank it on every phase change. Result: the panel is empty while you rest between dungeon rounds,
even though the party cards at the bottom right are still drawn.

## What this mod does

On the rest route only, it fills that same panel through the game's own
`CombatDetailHolderViewHelper.ShowDetails(...)`, using the same character filter, so the rows look
exactly like the ones you see in combat. Nothing about the stylesheet, the row pool or the input
navigation is reimplemented, so overflow falls back to the game's own "and N more" divider.

Hooks:

| Target | Why |
| --- | --- |
| `VenueLayoutViewHelper.ShowLayout(eRoutes)` postfix | fills the panel when the rest layout is shown, and clears it again when leaving rest for another non-combat phase (combat keeps ownership of the panel and refills it itself) |
| `RestPhase._refreshUI()` postfix | keeps HP, armour and status icons current as the party is revived, fed or damaged during the rest |

The row pool is fixed by the UI template, so a large party is summarised by the game's overflow
divider rather than growing the panel.

## Build and install

```bash
./build.sh
```

Requires the `dotnet` SDK. The game directory defaults to
`~/.local/share/Steam/steamapps/common/For The King II`; override with `FTK2_DIR=/path/to/game`.

The build targets `net472` against the game's own `FTK2.dll`, BepInEx, Harmony and the UnityEngine
UI Toolkit modules, and drops `RestPartyPanel.dll` into `BepInEx/plugins/`.

## Configuration

Written by BepInEx to `BepInEx/config/cmayfield.ftk2.restpartypanel.cfg`:

| Key | Default | Meaning |
| --- | --- | --- |
| `FallbackToParty` | `true` | Combat's filter only lists non-player allies. In a co-op party of human heroes that can be nothing at all, so fall back to listing the whole party rather than an empty panel. |
| `KeepDeadVisible` | `false` | List downed party members too, so it is obvious who still needs reviving. |
| `VerboseLogging` | `false` | Log the panel contents on every refresh into `BepInEx/LogOutput.log`. |

## Notes

* The `Evil <name>` enemies in a dungeon are reflection enemies - `VenueViewHelper.CreateReflectionEntity`
  copies a party member with `pIsNpc: true`, which gives it `GroupIndex = 1`, so
  `CharacterHelper.IsEnemy` is true for them. They belong to the *right* panel (the targeted enemy,
  `CombatPhase._refreshCombatDetailsRight`) and are deliberately not listed here.
* The stylesheet may hide the holder outside the combat layout. The mod only ever forces
  `display: flex` on the container, and only if it measures as `display: none`; it never hides it.
