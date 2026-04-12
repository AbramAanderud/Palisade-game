# Palisade

## Dimension: 3D (gameplay) + 2D (map editor)

## Input Actions

| Action | Keys |
|--------|------|
| move_forward | W, Up |
| move_back | S, Down |
| move_left | A, Left |
| move_right | D, Right |
| attack | Mouse Button Left |
| interact | E |
| pause | Escape |

## Scenes

### MapEditor (existing)
- **File:** res://scenes/MapEditor.tscn
- **Root type:** Node2D
- **Script:** MapEditorMain.cs
- **Purpose:** 2D tile editor; has "Enter Dungeon" button that serializes map and switches to DungeonGame

### DungeonGame
- **File:** res://scenes/DungeonGame.tscn
- **Root type:** Node3D
- **Children:** DungeonMesh, CentralRoom, FPPlayer, EnemyAI, WeaponPickup, HUD (CanvasLayer), GameManager, DirectionalLight3D, Environment

### FPPlayer
- **File:** res://scenes/FPPlayer.tscn
- **Root type:** CharacterBody3D
- **Children:** CameraMount (Node3D), Camera3D, CollisionShape3D (Capsule r=0.35 h=1.6), HeldWeapon (Node3D, weapon mesh shown when picked up), AttackArea (Area3D box in front)

### EnemyAI (placeholder)
- **File:** res://scenes/EnemyAI.tscn
- **Root type:** CharacterBody3D
- **Children:** MeshInstance3D (capsule, dark material), CollisionShape3D, NavigationAgent3D

### WeaponPickup
- **File:** res://scenes/WeaponPickup.tscn
- **Root type:** Node3D
- **Children:** MeshInstance3D (sword mesh or box placeholder), Area3D + CollisionShape3D, OmniLight3D (gold glow), AnimationPlayer (hover bob)

## Scripts

### MapEditorMain (existing)
- **File:** res://scripts/MapEditorMain.cs
- **Extends:** Node2D
- **New:** "Enter Dungeon" button calls `_OnEnterDungeon()` → saves map via MapSerializer, switches scene to DungeonGame

### DungeonBuilder
- **File:** res://scripts/DungeonBuilder.cs
- **Extends:** Node3D
- **Purpose:** Reads MapSerializer save file, generates StaticBody3D with ArrayMesh dungeon geometry + torch OmniLight3Ds
- **Signals emitted:** DungeonReady

### FPController
- **File:** res://scripts/FPController.cs
- **Extends:** CharacterBody3D
- **Signals emitted:** Died, WeaponPickedUp
- **Signals received:** GameManager.GameOver → OnGameOver

### EnemyController
- **File:** res://scripts/EnemyController.cs
- **Extends:** CharacterBody3D
- **Signals emitted:** Died

### WeaponPickup (script)
- **File:** res://scripts/WeaponPickup.cs
- **Extends:** Node3D
- **Signals emitted:** PickedUp(Node player)

### GameManager
- **File:** res://scripts/GameManager.cs
- **Extends:** Node
- **Purpose:** State machine (Loading→Playing→Result), timer, gold tracking, win/lose detection
- **Signals emitted:** GameOver(string result, int gold), TimerTick(float remaining)
- **Signals received:** FPController.Died, EnemyController.Died, WeaponPickup.PickedUp

### HUDController
- **File:** res://scripts/HUDController.cs
- **Extends:** Control
- **Signals received:** GameManager.TimerTick, GameManager.GameOver

### TileData (existing autoload)
- **File:** res://scripts/TileData.cs

### MapSerializer (existing static)
- **File:** res://scripts/MapSerializer.cs

### MapValidator (existing static)
- **File:** res://scripts/MapValidator.cs

## Signal Map

- DungeonBuilder.DungeonReady → GameManager.OnDungeonReady
- FPController.Died → GameManager.OnPlayerDied
- FPController.WeaponPickedUp → GameManager.OnWeaponPickedUp
- EnemyController.Died → GameManager.OnEnemyDied
- WeaponPickup.PickedUp → GameManager.OnWeaponPickedUp
- GameManager.GameOver → HUDController.OnGameOver
- GameManager.TimerTick → HUDController.OnTimerTick

## Build Order

1. dotnet build
2. scenes/BuildFPPlayer.cs → scenes/FPPlayer.tscn
3. scenes/BuildEnemyAI.cs → scenes/EnemyAI.tscn
4. scenes/BuildWeaponPickup.cs → scenes/WeaponPickup.tscn
5. scenes/BuildDungeonGame.cs → scenes/DungeonGame.tscn (depends: FPPlayer.tscn, EnemyAI.tscn, WeaponPickup.tscn)

## Asset Hints

- Stone brick wall texture (tileable, 512×512, grey stone with mortar)
- Stone floor texture (tileable, 512×512, worn flagstone)
- Sword mesh (iron longsword ~1m, for weapon pickup and held in hand)
