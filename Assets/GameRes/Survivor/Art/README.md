# Yato Survivor 首批美术素材

导入日期：2026-11-01。来源：用户提供的 `E:/GameTest/Bang-/Assets/Sprites`。
此目录保存幸存者示例的原始美术与切片；完整游戏已在 [SurvivorSample](../../../SurvivorSample/README.md) 实现，场景、Prefab 和玩法代码位于对应的游戏目录。

共 **49 张原始 PNG、152 个可直接引用的 Sprite**，PNG 合计 139,178 字节（约 136 KiB）。
所有 PNG 与来源文件逐字节一致；使用本工程生成的新 `.meta`，没有沿用来源工程的 GUID。

| 目录 | 选用内容 | PNG / Sprite | 用途 |
|---|---|---:|---|
| `Characters/Player` | 骑士待机、跑动，各 6 帧 | 2 / 12 | 玩家角色 |
| `Characters/Enemies` | skeleton1、skeleton2、skull、vampire，各 4 帧 | 16 / 16 | 普通骷髅、持刃骷髅、幽灵头骨、吸血鬼；可分配不同敌人行为 |
| `Environment/Floors` | floor_1、floor_5 至 floor_10 | 7 / 7 | 16×16 地板及随机变化 |
| `Environment/Walls` | wall_1、wall_2、wall_3、wall_crack | 4 / 4 | 墙面与装饰；这不是完整的边墙拼接套件 |
| `Pickups/Coins` | 金币 4 帧 | 4 / 4 | 金币掉落 |
| `Pickups/Health` | 红色药水 4 帧 | 4 / 4 | 回复掉落 |
| `Pickups/Chests` | 宝箱待机、开启，各 4 帧 | 8 / 8 | 奖励宝箱 |
| `Weapons` | Weapons 2-Sheet、Bullets 3-Sheet | 2 / 78 | 53 个攻击特效帧、25 种投射物外观 |
| `Effects` | explosion_anim_spritesheet，7 帧 | 1 / 7 | 爆炸与命中闪光，包含黑白闪帧 |
| `Icons` | FantasyIcons，选切 12 个图标 | 1 / 12 | 武器、经验、生命与升级展示 |

## 武器与界面切片

`Icons/FantasyIcons.png` 来自 Shikashi 图标集的无阴影原图，只为以下 12 个图标创建 Sprite；原图保持完整。

| Sprite 名 | 预定用途 |
|---|---|
| `Sword` | 剑：初始近战武器 |
| `Axe` | 斧：旋转 / 范围武器 |
| `Bow` | 弓：远程武器 |
| `BlueStaff`、`GreenStaff` | 两种法杖：魔法武器 |
| `ExperienceGem` | 经验掉落 / 经验显示 |
| `HealthHeart` | 生命显示 |
| `FireSkill`、`IceSkill` | 火焰、冰系升级 |
| `SpeedSkill`、`DamageSkill`、`ShieldSkill` | 移速、伤害、防御升级 |

攻击特效图集保留来源 `.meta` 中的全部有效矩形，按来源顺序命名为 `Attack_00` 至 `Attack_52`。
这些帧包含多种攻击，不应将整张图集串成一个动画。
投射物命名为 `Projectile_00` 至 `Projectile_24`；每个是独立外观。

## 导入约定

- 全部设置为 Sprite、Point 采样、无压缩、关闭 Mipmap、Clamp、保留透明度、不允许 NPOT 缩放。
- 角色、环境、拾取物、爆炸使用 16 PPU；32×32 的攻击、投射物和图标使用 32 PPU。
- 使用居中 Pivot 与 Full Rect 网格，不自动生成物理轮廓。后续角色碰撞体应独立配置。
- 骑士帧名为 `Knight_Idle_00..05`、`Knight_Run_00..05`，爆炸帧名为 `Explosion_00..06`。
- 怪物选用去掉彩色边角标记的 `v2` 版本；来源只提供该组循环动画，尚无独立跑动 / 攻击动画。
- 本目录只保存 Sprite；动画速度、武器行为与敌人数值由示例 SpriteSet / Config 资产和运行时代码配置，未改写原始 PNG。

[SurvivorArtManifest.json](../../../../docs/SurvivorArtManifest.json) 记录每张图的来源相对路径、工程路径、用途、SHA-256、原始尺寸、PPU 和全部切片矩形；坐标采用 Unity 左下角原点。
导入验证记录位于工程本地 `Logs/Survivor-Art-Import-Verification.json`。

首个可玩示例所需的角色、敌人、地面、武器、掉落物与基础图标已经具备。
