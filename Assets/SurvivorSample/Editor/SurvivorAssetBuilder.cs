using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Yato.App;
using Yato.Config;
using Yato.Editor;
using Yato.UI;
using Yato.UI.Input;

namespace Yato.Survivor.Editor
{
    public static class SurvivorAssetBuilder
    {
        public const string Root = "Assets/GameRes/Survivor";
        private const string Presentation = Root + "/Presentation";
        private const string ConfigRoot = "Assets/GameRes/Config/Survivor";
        private const string UiRoot = "Assets/GameRes/UI/Survivor";
        private static readonly Color Ink = Hex("10151e"), Panel = Hex("1c2431"), Light = Hex("f2e9d7"),
            Muted = Hex("9ba8b8"), Gold = Hex("dab977"), Teal = Hex("71d9c2"), Red = Hex("d56b70");
        private static Font s_font;
        private static Sprite s_uiSprite;
        private static Sprite s_fillSprite;
        private static SurvivorSpriteSet s_art;

        [MenuItem("Yato/Survivor/Create or Update Sample Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring sample assets");
            foreach (var folder in new[] { Presentation, ConfigRoot, UiRoot, Root + "/Prefabs", "Assets/SurvivorSample/Scenes", "Assets/GameRes/Audio/Survivor" })
                Directory.CreateDirectory(folder);
            if (!File.Exists(Presentation + "/SurvivorSans.ttf"))
                File.Copy("Assets/TextMesh Pro/Fonts/LiberationSans.ttf", Presentation + "/SurvivorSans.ttf");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            s_font = AssetDatabase.LoadAssetAtPath<Font>(Presentation + "/SurvivorSans.ttf");
            s_uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var whitePath = Presentation + "/UIWhite.png";
            if (!File.Exists(whitePath))
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
                File.WriteAllBytes(whitePath, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(whitePath);
                var importer = (TextureImporter)AssetImporter.GetAtPath(whitePath); importer.textureType = TextureImporterType.Sprite;
                importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            s_fillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(whitePath);
            BuildConfig(); BuildInput(); BuildArt();
            foreach (var name in new[] { "Player", "Enemy", "Projectile", "Pickup", "Effect" }) BuildEntityPrefab(name);
            foreach (SurvivorPageKind kind in Enum.GetValues(typeof(SurvivorPageKind))) BuildPage(kind);
            BuildScenes();
            EditorUtility.SetDirty(s_art); AssetDatabase.SaveAssets();
            Debug.Log("[Survivor] Assets created: 8 routed pages, 8 tables, 5 pooled/owned entity prefabs, Bootstrap and Arena scenes.");
        }
        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static void Rows<TTable, TRecord>(string name, TRecord[] rows, List<ConfigTableBase> tables)
            where TRecord : ConfigRecord where TTable : ConfigTable<TRecord>
        {
            var table = Asset<TTable>(ConfigRoot + "/" + name + ".asset");
            table.Rows = rows; EditorUtility.SetDirty(table); tables.Add(table);
        }
        private static void BuildConfig()
        {
            var tables = new List<ConfigTableBase>();
            Rows<CharacterTable, CharacterRecord>("Characters", new[] { new CharacterRecord { Id = "knight", Health = 100, Speed = 4, PickupRadius = 1.8f } }, tables);
            Rows<EnemyTable, EnemyRecord>("Enemies", new[] {
                new EnemyRecord { Id = "skeleton", Visual = "skeleton", Health = 9, Speed = 1.25f, Damage = 8, Experience = 1 },
                new EnemyRecord { Id = "guard", Visual = "guard", Health = 22, Speed = 1.1f, Damage = 12, Experience = 2, UnlockSeconds = 45 },
                new EnemyRecord { Id = "wisp", Visual = "wisp", Health = 12, Speed = 2.45f, Damage = 7, Experience = 2, UnlockSeconds = 90 },
                new EnemyRecord { Id = "vampire", Visual = "vampire", Health = 150, Speed = 1.5f, Damage = 20, Experience = 15, Scale = 1.6f, Elite = true }
            }, tables);
            Rows<ProjectileTable, ProjectileRecord>("Projectiles", new[] {
                new ProjectileRecord { Id = "arrow", Visual = "arrow", Speed = 11, Lifetime = 2, Pierce = 2 },
                new ProjectileRecord { Id = "arcane", Visual = "arcane", Speed = 7, Lifetime = 3, Homing = true }
            }, tables);
            Rows<WeaponTable, WeaponRecord>("Weapons", new[] {
                new WeaponRecord { Id = "sword", Icon = "Sword", Damage = 12, Cooldown = 1.05f, Range = 1.65f },
                new WeaponRecord { Id = "bow", Icon = "Bow", Damage = 15, Cooldown = .85f, Range = 9, ProjectileId = "arrow" },
                new WeaponRecord { Id = "staff", Icon = "BlueStaff", Damage = 24, Cooldown = 1.6f, Range = 10, ProjectileId = "arcane" }
            }, tables);
            var upgrades = new[] {
                Upgrade("sword", "TEMPERED STEEL", "Unlock or improve your sword.\n+25% damage and greater reach.", "Sword", 5, 14, "sword"),
                Upgrade("bow", "THE RANGER", "Unlock or improve your bow.\nArrows pierce through two enemies.", "Bow", 5, 14, "bow"),
                Upgrade("staff", "ARCANE MEMORY", "Unlock or improve your staff.\nSeeking bolts find nearby enemies.", "BlueStaff", 5, 14, "staff"),
                Upgrade("might", "BLOOD OATH", "All weapons deal +20% damage.\nLet every strike count.", "DamageSkill", 5, 12),
                Upgrade("haste", "QUICKENING", "Weapons attack 12% faster.\nA moment can save your life.", "FireSkill", 5, 12),
                Upgrade("swift", "LIGHT FOOTED", "Move 12% faster.\nKeep a step ahead of the horde.", "SpeedSkill", 5, 9),
                Upgrade("vitality", "SECOND WIND", "+20 maximum health.\nRecover 20 health immediately.", "HealthHeart", 5, 10),
                Upgrade("magnet", "SOULKEEPER", "+0.65 pickup radius.\nGather the spoils from afar.", "ExperienceGem", 4, 8),
                Upgrade("multishot", "MANY AS ONE", "+1 projectile for bow and staff.\nFill the dark with light.", "GreenStaff", 3, 8),
                Upgrade("ward", "IRON RESOLVE", "Take 8% less contact damage.\nThe watch does not break.", "ShieldSkill", 5, 9)
            };
            Rows<UpgradeTable, UpgradeRecord>("Upgrades", upgrades, tables);
            var waves = new[] {
                new SpawnWaveRecord { Id = "wake", StartSeconds = 0, EndSeconds = 90, Interval = .65f, MaximumAlive = 90, Enemies = new[] { "skeleton", "guard" } },
                new SpawnWaveRecord { Id = "gather", StartSeconds = 90, EndSeconds = 240, Interval = .4f, MaximumAlive = 130, Enemies = new[] { "skeleton", "guard", "wisp" } },
                new SpawnWaveRecord { Id = "horde", StartSeconds = 240, EndSeconds = 420, Interval = .2f, MaximumAlive = 180, Enemies = new[] { "skeleton", "guard", "wisp" } },
                new SpawnWaveRecord { Id = "dawn", StartSeconds = 420, EndSeconds = 600, Interval = .1f, MaximumAlive = 220, Enemies = new[] { "skeleton", "guard", "wisp" } }
            };
            Rows<SpawnWaveTable, SpawnWaveRecord>("SpawnWaves", waves, tables);
            Rows<StageTable, StageRecord>("Stages", new[] { new StageRecord { Id = "dungeon", Enemies = new[] { "skeleton", "guard", "wisp", "vampire" }, Upgrades = upgrades.Select(u => u.Id).ToArray(), Waves = waves.Select(w => w.Id).ToArray() } }, tables);
            Rows<RewardTable, RewardRecord>("Rewards", new[] { new RewardRecord { Id = "standard" } }, tables);
            var catalog = Asset<ConfigCatalog>(ConfigRoot + "/SurvivorCatalog.asset");
            catalog.Tables = tables.ToArray(); EditorUtility.SetDirty(catalog);
            var errors = catalog.Validate(); if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        }
        private static UpgradeRecord Upgrade(string id, string title, string description, string icon, int max, int weight, string weapon = null)
            => new UpgradeRecord { Id = id, Title = title, Description = description, Icon = icon, Maximum = max, Weight = weight, WeaponId = weapon };

        private static void BuildInput()
        {
            var path = Presentation + "/SurvivorInput.asset";
            var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (input == null)
            {
                input = ScriptableObject.CreateInstance<InputActionAsset>();
                var gameplay = input.AddActionMap("Gameplay");
                var move = gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
                move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
                move.AddBinding("<Gamepad>/leftStick");
                gameplay.AddAction("Pause", InputActionType.Button, expectedControlLayout: "Button").AddBinding("<Keyboard>/escape");
                gameplay.FindAction("Pause").AddBinding("<Gamepad>/start");
                var ui = input.AddActionMap("UI");
                ui.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position", expectedControlLayout: "Vector2");
                ui.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton", expectedControlLayout: "Button");
                var nav = ui.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
                nav.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
                nav.AddBinding("<Gamepad>/dpad"); nav.AddBinding("<Gamepad>/leftStick");
                ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter", expectedControlLayout: "Button").AddBinding("<Gamepad>/buttonSouth");
                ui.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape", expectedControlLayout: "Button").AddBinding("<Gamepad>/buttonEast");
                ui.AddAction("Scroll", InputActionType.PassThrough, "<Mouse>/scroll", expectedControlLayout: "Vector2");
                AssetDatabase.CreateAsset(input, path);
            }
            var profile = Asset<UIInputProfile>(Presentation + "/SurvivorInputProfile.asset");
            profile.Actions = input; profile.BaseMap = "Gameplay"; profile.UIMap = "UI"; EditorUtility.SetDirty(profile);
        }
        private static Sprite[] Sprites(string path) => AssetDatabase.LoadAllAssetsAtPath(Root + "/Art/" + path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
        private static Sprite Named(string path, string name) => Sprites(path).Single(s => s.name == name);
        private static void BuildArt()
        {
            s_art = Asset<SurvivorSpriteSet>(Presentation + "/SurvivorArt.asset");
            var sequences = new List<SpriteSequence>();
            Action<string, Sprite[], float> add = (id, sprites, fps) => sequences.Add(new SpriteSequence { Id = id, Frames = sprites, FramesPerSecond = fps });
            add("knight_idle", Sprites("Characters/Player/knight_idle_spritesheet.png"), 7);
            add("knight_run", Sprites("Characters/Player/knight_run_spritesheet.png"), 11);
            foreach (var pair in new[] { new[] { "skeleton", "skeleton1" }, new[] { "guard", "skeleton2" }, new[] { "wisp", "skull" }, new[] { "vampire", "vampire" } })
            {
                var all = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Characters/Enemies/" + pair[1] })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>()).ToArray();
                add(pair[0], all, 7);
            }
            foreach (var pair in new[] { new[] { "coin", "Coins" }, new[] { "potion", "Health" }, new[] { "chest", "Chests" } })
            {
                var all = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Art/Pickups/" + pair[1] }).Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !p.Contains("_open_")).OrderBy(p => p).SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>()).ToArray();
                add(pair[0], all, 7);
            }
            foreach (var icon in Sprites("Icons/FantasyIcons.png")) add(icon.name, new[] { icon }, 1);
            add("arrow", new[] { Named("Weapons/Bullets 3-Sheet.png", "Projectile_02") }, 1);
            add("arcane", new[] { Named("Weapons/Bullets 3-Sheet.png", "Projectile_03") }, 1);
            add("slash", Sprites("Weapons/Weapons 2-Sheet.png").Where(s => string.CompareOrdinal(s.name, "Attack_45") >= 0 && string.CompareOrdinal(s.name, "Attack_48") <= 0).ToArray(), 18);
            add("explosion", Sprites("Effects/explosion_anim_spritesheet.png").Take(5).ToArray(), 18);
            s_art.Sequences = sequences.ToArray();
            s_art.Input = AssetDatabase.LoadAssetAtPath<InputActionAsset>(Presentation + "/SurvivorInput.asset");
            s_art.InputProfile = AssetDatabase.LoadAssetAtPath<UIInputProfile>(Presentation + "/SurvivorInputProfile.asset");
            s_art.CatalogKey = Key(ConfigRoot + "/SurvivorCatalog.asset");
            s_art.PlayerKey = Yato.Generated.AssetKeys.SurvivorPrefabsPlayer.Value; s_art.EnemyKey = Yato.Generated.AssetKeys.SurvivorPrefabsEnemy.Value;
            s_art.ProjectileKey = Yato.Generated.AssetKeys.SurvivorPrefabsProjectile.Value; s_art.PickupKey = Yato.Generated.AssetKeys.SurvivorPrefabsPickup.Value; s_art.EffectKey = Yato.Generated.AssetKeys.SurvivorPrefabsEffect.Value;
            s_art.Music = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameRes/Audio/Survivor/watch.wav");
            s_art.HitSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameRes/Audio/Survivor/hit.wav");
            s_art.PickupSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameRes/Audio/Survivor/pickup.wav");
            s_art.UpgradeSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameRes/Audio/Survivor/upgrade.wav");
            EditorUtility.SetDirty(s_art);
        }
        private static string Key(string path) => BundleNaming.GetAssetKey(path, BundleNaming.DefaultAssetRoot);
        private static void BuildEntityPrefab(string name)
        {
            var go = new GameObject(name, typeof(SpriteRenderer), typeof(SurvivorEntity));
            try
            {
                go.GetComponent<SpriteRenderer>().sprite = s_art.Find("knight_idle").Frames[0];
                go.GetComponent<SurvivorEntity>().Renderer = go.GetComponent<SpriteRenderer>();
                go.SetActive(name == "Player"); PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + name + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var color); return color; }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        {
            var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = s_uiSprite; image.type = Image.Type.Sliced;
            image.color = color; image.raycastTarget = false; return image;
        }
        private static Text Label(Transform parent, string name, string text, float x, float y, float width, float height, int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
        {
            var label = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Text>();
            label.font = s_font; label.fontSize = size; label.text = text; label.color = color; label.alignment = align;
            label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        private static void Bind(ComponentBinder binder, string name, Component component) => binder.Add(name, component);
        private static Button Button(ComponentBinder binder, Transform parent, string name, string text, float x, float y, float width = 320, bool primary = false)
        {
            var image = Box(parent, name, x, y, width, 58, primary ? Gold : Hex("2a3545")); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f); colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.7f, .7f, .7f); button.colors = colors;
            Label(image.transform, "Label", text, 14, 0, width - 28, 58, 20, primary ? Ink : Light, TextAnchor.MiddleCenter);
            Bind(binder, name, button); return button;
        }
        private static Image Picture(Transform parent, Sprite sprite, float x, float y, float size, Color? tint = null)
        {
            var image = Box(parent, "Art", x, y, size, size, tint ?? Color.white); image.sprite = sprite; image.type = Image.Type.Simple; image.preserveAspect = true; return image;
        }
        private static void BuildPage(SurvivorPageKind kind)
        {
            var viewType = Type.GetType("Yato.Game.UI.Survivor" + kind + "PageView, Yato.Survivor") ?? typeof(SurvivorPageView);
            var root = new GameObject("Survivor" + kind, typeof(RectTransform), typeof(ComponentBinder), viewType, typeof(UIPageFocus));
            try
            {
                var rect = (RectTransform)root.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
                var binder = root.GetComponent<ComponentBinder>(); var view = root.GetComponent<SurvivorPageView>(); view.Kind = kind;
                var focus = root.GetComponent<UIPageFocus>(); focus.CaptureInput = kind != SurvivorPageKind.Hud && kind != SurvivorPageKind.Loading;
                focus.AllowBack = kind == SurvivorPageKind.Pause || kind == SurvivorPageKind.Settings;
                if (kind == SurvivorPageKind.Hud) { Hud(root.transform, binder); focus.DefaultSelected = null; }
                else if (kind == SurvivorPageKind.Menu) Menu(root.transform, binder, focus);
                else
                {
                    var shade = Box(root.transform, "Shade", 0, 0, 1600, 900, new Color(Ink.r, Ink.g, Ink.b, kind == SurvivorPageKind.Loading || kind == SurvivorPageKind.Settings ? 1 : .94f)); shade.raycastTarget = true;
                    if (kind == SurvivorPageKind.Upgrade) UpgradePage(root.transform, binder, focus);
                    else if (kind == SurvivorPageKind.Results) ResultPage(root.transform, binder, focus);
                    else if (kind == SurvivorPageKind.Settings) SettingsPage(root.transform, binder, focus);
                    else
                    {
                        Box(root.transform, "Rule", 500, 224, 600, 2, Gold);
                        Bind(binder, "Txt_Title", Label(root.transform, "Txt_Title", kind == SurvivorPageKind.Pause ? "A MOMENT OF RESPITE" : kind == SurvivorPageKind.Error ? "THE PATH IS BLOCKED" : "ENTERING THE DEPTHS", 350, 260, 900, 80, 44, Light, TextAnchor.MiddleCenter));
                        Bind(binder, "Txt_Status", Label(root.transform, "Txt_Status", kind == SurvivorPageKind.Pause ? "The watch continues when you are ready." : kind == SurvivorPageKind.Loading ? "Gathering your gear and lighting the way..." : "", 400, 355, 800, 130, 21, Muted, TextAnchor.MiddleCenter));
                        if (kind == SurvivorPageKind.Pause)
                        {
                            focus.DefaultSelected = Button(binder, root.transform, "Btn_Resume", "CONTINUE THE WATCH", 640, 480, primary: true).gameObject;
                            Button(binder, root.transform, "Btn_Settings", "SETTINGS", 640, 556);
                            Button(binder, root.transform, "Btn_Abandon", "END EXPEDITION", 640, 632);
                        }
                        else if (kind == SurvivorPageKind.Error) focus.DefaultSelected = Button(binder, root.transform, "Btn_Menu", "RETURN TO CAMP", 640, 550, primary: true).gameObject;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, UiRoot + "/Survivor" + kind + ".prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Menu(Transform root, ComponentBinder binder, UIPageFocus focus)
        {
            Box(root, "Backdrop", 0, 0, 1600, 900, Ink).raycastTarget = true;
            Box(root, "Accent", 96, 102, 62, 4, Gold);
            Label(root, "Eyebrow", "Y A T O   /   A   S U R V I V O R   S T O R Y", 96, 130, 650, 30, 17, Gold);
            Label(root, "Title", "THE LAST\nWATCH", 88, 192, 750, 245, 92, Light);
            Label(root, "Subtitle", "Steel in your hand. Dawn on the horizon.\nSurvive ten minutes in a dungeon that never sleeps.", 100, 460, 680, 80, 24, Muted);
            focus.DefaultSelected = Button(binder, root, "Btn_Start", "BEGIN THE WATCH    >", 100, 596, 390, true).gameObject;
            Button(binder, root, "Btn_Settings", "SETTINGS", 100, 672, 186); Button(binder, root, "Btn_Quit", "QUIT", 304, 672, 186);
            Bind(binder, "Txt_History", Label(root, "Txt_History", "", 100, 766, 660, 65, 17, Muted));
            Bind(binder, "Txt_Status", Label(root, "Txt_Status", "", 100, 844, 1300, 30, 15, Muted));
            Box(root, "PortraitPanel", 900, 110, 590, 620, Panel);
            for (var x = 0; x < 7; x++) for (var y = 0; y < 7; y++)
                Picture(root, Sprites("Environment/Floors/floor_5.png")[0], 918 + x * 80, 130 + y * 80, 80, new Color(.65f, .72f, .85f));
            Picture(root, s_art.Find("knight_idle").Frames[0], 1060, 275, 260);
            Picture(root, s_art.Find("Sword").Frames[0], 1000, 490, 85);
            Picture(root, s_art.Find("Bow").Frames[0], 1152, 530, 85);
            Picture(root, s_art.Find("BlueStaff").Frames[0], 1300, 490, 85);
            Label(root, "ArtLabel", "01   /   THE FORGOTTEN HALLS", 940, 755, 550, 30, 20, Gold);
            Label(root, "Controls", "WASD / ARROWS  Move     ESC  Pause\nAuto-attack. Gather souls. Choose your blessings.", 940, 802, 600, 65, 17, Muted);
        }
        private static void Hud(Transform root, ComponentBinder binder)
        {
            Box(root, "TopBar", 0, 0, 1600, 106, new Color(Ink.r, Ink.g, Ink.b, .92f));
            Box(root, "HealthTrack", 35, 55, 310, 16, Hex("343443"));
            var health = Box(root, "Img_Health", 35, 55, 310, 16, Red); health.sprite = s_fillSprite; health.type = Image.Type.Filled; health.fillMethod = Image.FillMethod.Horizontal; Bind(binder, "Img_Health", health);
            Bind(binder, "Txt_Health", Label(root, "Txt_Health", "", 36, 24, 310, 30, 21, Light));
            Bind(binder, "Txt_Stage", Label(root, "Txt_Stage", "", 510, 18, 580, 25, 17, Gold, TextAnchor.MiddleCenter));
            Bind(binder, "Txt_Time", Label(root, "Txt_Time", "00:00", 650, 40, 300, 48, 38, Light, TextAnchor.MiddleCenter));
            Label(root, "KillsLabel", "SLAIN", 1160, 23, 100, 25, 14, Muted);
            Bind(binder, "Txt_Kills", Label(root, "Txt_Kills", "0000", 1160, 47, 100, 35, 25, Light));
            Label(root, "CoinsLabel", "GOLD", 1300, 23, 90, 25, 14, Muted);
            Bind(binder, "Txt_Coins", Label(root, "Txt_Coins", "000", 1300, 47, 90, 35, 25, Gold));
            Button(binder, root, "Btn_Pause", "II", 1460, 24, 92);
            Box(root, "XpTrack", 0, 106, 1600, 7, Panel);
            var xp = Box(root, "Img_Experience", 0, 106, 1600, 7, Teal); xp.sprite = s_fillSprite; xp.type = Image.Type.Filled; xp.fillMethod = Image.FillMethod.Horizontal; Bind(binder, "Img_Experience", xp);
            Bind(binder, "Txt_Level", Label(root, "Txt_Level", "LV 1", 35, 119, 120, 30, 19, Teal));
            Box(root, "Bottom", 510, 840, 580, 44, new Color(Ink.r, Ink.g, Ink.b, .85f));
            Bind(binder, "Txt_Weapons", Label(root, "Txt_Weapons", "", 530, 848, 540, 28, 18, Light, TextAnchor.MiddleCenter));
        }
        private static void UpgradePage(Transform root, ComponentBinder binder, UIPageFocus focus)
        {
            Bind(binder, "Txt_Title", Label(root, "Txt_Title", "GROW STRONGER", 300, 110, 1000, 75, 52, Light, TextAnchor.MiddleCenter));
            Bind(binder, "Txt_Subtitle", Label(root, "Txt_Subtitle", "", 300, 195, 1000, 40, 22, Muted, TextAnchor.MiddleCenter));
            for (var i = 0; i < 3; i++)
            {
                var x = 218 + i * 398;
                var button = Button(binder, root, "Btn_Choice" + i, "CHOOSE THIS BLESSING", x, 680, 370, i == 0);
                if (i == 0) focus.DefaultSelected = button.gameObject;
                Box(root, "Card" + i, x, 290, 370, 370, Panel);
                Box(root, "Edge" + i, x, 290, 370, 3, i == 0 ? Gold : Teal);
                Bind(binder, "Img_Choice" + i, Picture(root, s_art.Find("Sword").Frames[0], x + 137, 330, 96));
                Bind(binder, "Txt_Rank" + i, Label(root, "Txt_Rank" + i, "", x + 24, 458, 322, 25, 16, Gold, TextAnchor.MiddleCenter));
                Bind(binder, "Txt_Choice" + i, Label(root, "Txt_Choice" + i, "", x + 20, 499, 330, 58, 27, Light, TextAnchor.MiddleCenter));
                Bind(binder, "Txt_Description" + i, Label(root, "Txt_Description" + i, "", x + 26, 570, 318, 78, 18, Muted, TextAnchor.UpperCenter));
            }
        }
        private static void ResultPage(Transform root, ComponentBinder binder, UIPageFocus focus)
        {
            Label(root, "Eyebrow", "E X P E D I T I O N   C O M P L E T E", 300, 145, 1000, 35, 18, Gold, TextAnchor.MiddleCenter);
            Bind(binder, "Txt_Title", Label(root, "Txt_Title", "", 280, 214, 1040, 95, 70, Light, TextAnchor.MiddleCenter));
            Bind(binder, "Txt_Subtitle", Label(root, "Txt_Subtitle", "", 300, 321, 1000, 50, 24, Muted, TextAnchor.MiddleCenter));
            var keys = new[] { "Time", "Kills", "Level", "Coins" }; var labels = new[] { "TIME SURVIVED", "ENEMIES SLAIN", "LEVEL REACHED", "GOLD GATHERED" };
            for (var i = 0; i < 4; i++)
            {
                var x = 240 + i * 290; Box(root, "Stat", x, 430, 250, 145, Panel);
                Bind(binder, "Txt_Result" + keys[i], Label(root, "Txt_Result" + keys[i], "", x, 447, 250, 65, 48, i == 3 ? Gold : Light, TextAnchor.MiddleCenter));
                Label(root, "StatLabel", labels[i], x, 526, 250, 25, 15, Muted, TextAnchor.MiddleCenter);
            }
            focus.DefaultSelected = Button(binder, root, "Btn_Retry", "TAKE THE WATCH AGAIN", 460, 650, 340, true).gameObject;
            Button(binder, root, "Btn_Menu", "RETURN TO CAMP", 830, 650, 300);
            Bind(binder, "Txt_Status", Label(root, "Txt_Status", "", 300, 765, 1000, 70, 17, Muted, TextAnchor.MiddleCenter));
        }
        private static void SettingsPage(Transform root, ComponentBinder binder, UIPageFocus focus)
        {
            Label(root, "Title", "PREPARE FOR THE WATCH", 280, 100, 1040, 80, 44, Light, TextAnchor.MiddleCenter);
            var keys = new[] { "Master", "Music", "Effects" };
            for (var i = 0; i < 3; i++)
            {
                var y = 240 + i * 85; Label(root, "Label", keys[i].ToUpperInvariant() + " VOLUME", 410, y, 300, 40, 21, Muted);
                var track = Box(root, "Slider_" + keys[i], 760, y + 8, 430, 15, Panel); track.raycastTarget = true;
                var fill = Box(track.transform, "Fill", 0, 0, 430, 15, Gold);
                fill.sprite = s_fillSprite; fill.type = Image.Type.Simple;
                fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
                fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
                var handle = Box(track.transform, "Handle", 0, -5, 20, 25, Light);
                handle.rectTransform.pivot = new Vector2(.5f, .5f); handle.rectTransform.sizeDelta = new Vector2(20, 10);
                handle.rectTransform.anchoredPosition = Vector2.zero;
                var slider = track.gameObject.AddComponent<Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
                slider.targetGraphic = handle; slider.minValue = 0; slider.maxValue = 1; slider.wholeNumbers = false;
                Bind(binder, "Slider_" + keys[i], slider);
            }
            Button(binder, root, "Btn_Rebind", "REBIND MOVE UP", 410, 520, 370);
            Button(binder, root, "Btn_ResetBindings", "RESET CONTROLS", 820, 520, 370);
            focus.DefaultSelected = Button(binder, root, "Btn_Save", "SAVE SETTINGS", 410, 610, 370, true).gameObject;
            Button(binder, root, "Btn_Close", "BACK", 820, 610, 370);
            Button(binder, root, "Btn_Recover", "RECOVER HISTORY BACKUP", 600, 702, 400);
            Bind(binder, "Txt_Status", Label(root, "Txt_Status", "", 350, 800, 900, 75, 17, Muted, TextAnchor.MiddleCenter));
        }
        private static void BuildScenes()
        {
            var bootstrap = new GameObject("SurvivorBootstrap");
            try
            {
                bootstrap.AddComponent<SurvivorGame>(); bootstrap.AddComponent<GameBootstrap>();
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); events.transform.SetParent(bootstrap.transform, false);
                var camera = new GameObject("CampCamera", typeof(Camera), typeof(AudioListener)); camera.transform.SetParent(bootstrap.transform, false);
                camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor; camera.GetComponent<Camera>().backgroundColor = Ink;
                camera.transform.position = new Vector3(0, 0, -10); camera.GetComponent<Camera>().depth = -10;
                SceneFromPrefab(bootstrap, "Bootstrap");
            }
            finally { UnityEngine.Object.DestroyImmediate(bootstrap); }
            var arena = new GameObject("ArenaRoot", typeof(Grid));
            try
            {
                var mapObject = new GameObject("Floor", typeof(Tilemap), typeof(TilemapRenderer)); mapObject.transform.SetParent(arena.transform, false);
                var map = mapObject.GetComponent<Tilemap>(); mapObject.GetComponent<TilemapRenderer>().sortingOrder = -1000;
                var floors = new[] { 5, 10, 6, 7 }.Select(index => {
                    var tile = Asset<Tile>(Presentation + "/Floor" + index + ".asset"); tile.sprite = Sprites("Environment/Floors/floor_" + index + ".png")[0];
                    tile.color = new Color(.7f, .74f, .82f); tile.colliderType = Tile.ColliderType.None; EditorUtility.SetDirty(tile); return tile;
                }).ToArray();
                var wall = Asset<Tile>(Presentation + "/Wall.asset"); wall.sprite = Sprites("Environment/Walls/wall_1.png")[0]; wall.colliderType = Tile.ColliderType.None; EditorUtility.SetDirty(wall);
                var random = new System.Random(2026);
                for (var x = -21; x <= 20; x++) for (var y = -17; y <= 16; y++)
                    map.SetTile(new Vector3Int(x, y, 0), x == -21 || x == 20 || y == -17 || y == 16 ? wall : floors[random.Next(10) < 7 ? 0 : random.Next(floors.Length)]);
                var camera = new GameObject("ArenaCamera", typeof(Camera)); camera.transform.SetParent(arena.transform, false);
                var component = camera.GetComponent<Camera>(); component.orthographic = true; component.orthographicSize = 7;
                component.clearFlags = CameraClearFlags.SolidColor; component.backgroundColor = Ink; camera.transform.position = new Vector3(0, 0, -10);
                SceneFromPrefab(arena, "Arena");
            }
            finally { UnityEngine.Object.DestroyImmediate(arena); }
            var paths = new[] { "Assets/SurvivorSample/Scenes/SurvivorBootstrap.unity", "Assets/SurvivorSample/Scenes/SurvivorArena.unity" };
            var scenes = EditorBuildSettings.scenes.Where(s => File.Exists(s.path) && !paths.Contains(s.path)).ToList();
            scenes.InsertRange(0, paths.Select(p => new EditorBuildSettingsScene(p, true))); EditorBuildSettings.scenes = scenes.ToArray();
        }
        private static void SceneFromPrefab(GameObject root, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/SurvivorSample/Scenes/Survivor" + name + "Root.prefab");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab.transform, out string guid, out long transformId);
            var scene = "Assets/SurvivorSample/Scenes/Survivor" + name + ".unity";
            // Existing scenes may contain user overrides; updating their referenced prefab is sufficient.
            if (File.Exists(scene)) return;
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1001 &100100000\nPrefabInstance:\n  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Modification:\n    serializedVersion: 3\n    m_TransformParent: {fileID: 0}\n    m_Modifications: []\n    m_RemovedComponents: []\n    m_RemovedGameObjects: []\n    m_AddedGameObjects: []\n    m_AddedComponents: []\n  m_SourcePrefab: {fileID: 100100000, guid: " + guid + ", type: 3}\n--- !u!4 &100100001 stripped\nTransform:\n  m_CorrespondingSourceObject: {fileID: " + transformId + ", guid: " + guid + ", type: 3}\n  m_PrefabInstance: {fileID: 100100000}\n  m_PrefabAsset: {fileID: 0}\n--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots:\n  - {fileID: 100100001}\n";
            File.WriteAllText(scene, yaml); AssetDatabase.ImportAsset(scene, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
