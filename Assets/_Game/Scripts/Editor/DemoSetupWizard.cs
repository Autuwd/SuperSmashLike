using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using SuperSmashLike.Core;
using SuperSmashLike.Combat;
using SuperSmashLike.Managers;
using SuperSmashLike.Stage;
using SuperSmashLike.InputSystem;
using UnityEngine.InputSystem;
using UnityEditor.Animations;
using System.IO;

// ============================================================
// DemoSetupWizard — 一键初始化工具
// 在 Unity 顶部菜单栏点击 "SuperSmashLike → Setup Demo"
// 自动创建所有 SO 资产、预制体、场景，让项目立即可以 Play
// ============================================================
public class DemoSetupWizard : EditorWindow
{
    [MenuItem("SuperSmashLike/1. Setup All Demo Assets", false, 1)]
    public static void SetupAll()
    {
        CreateDirectories();
        CreateGameSettings();
        CreateFighterDataAssets();
        CreateStageData();
        CreateInputActions();
        CreateAnimatorController();
        CreateFighterPrefab();
        CreateStagePrefab();
        CreateDemoScene();
        Debug.Log("✅ Demo setup complete! Open Scenes/BattleTest and press Play.");
    }

    // ==================== 创建目录结构 ====================
    private static void CreateDirectories()
    {
        EnsureDirectory("Assets/_Game/ScriptableObjects/Characters");
        EnsureDirectory("Assets/_Game/ScriptableObjects/Stages");
        EnsureDirectory("Assets/_Game/ScriptableObjects/GameModes");
        EnsureDirectory("Assets/_Game/Prefabs/Fighters");
        EnsureDirectory("Assets/_Game/Prefabs/Stages");
        EnsureDirectory("Assets/_Game/Prefabs/Effects");
        EnsureDirectory("Assets/_Game/Animations");
        EnsureDirectory("Assets/_Game/Scenes");
        EnsureDirectory("Assets/_Game/Input");
    }

    private static void EnsureDirectory(string path)
    {
        if (!AssetDatabase.IsValidFolder(path))
        {
            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string dirName = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, dirName);
        }
    }

    // ==================== 创建 GameSettings 资产 ====================
    [MenuItem("SuperSmashLike/GameSettings Asset", false, 10)]
    public static void CreateGameSettings()
    {
        var gs = ScriptableObject.CreateInstance<GameSettings>();
        gs.matchMode = GameSettings.MatchMode.Stock;
        gs.stockCount = 3;
        gs.matchTimeSeconds = 300;
        gs.damageRatio = 1f;
        gs.respawnTime = 3f;
        gs.respawnInvincibilityFrames = 180;
        gs.shieldMaxHP = 100f;
        gs.shieldRegenPerSecond = 10f;
        gs.hitstopScale = 1f;
        gs.enableItems = false;

        AssetDatabase.CreateAsset(gs, "Assets/_Game/ScriptableObjects/GameModes/GS_Default.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("Created GS_Default.asset");
    }

    // ==================== 创建 FighterData 资产 ====================
    [MenuItem("SuperSmashLike/FighterData Assets", false, 11)]
    public static void CreateFighterDataAssets()
    {
        CreateFighterData("FD_Knight", "Knight", 110, 4.5f, 7f, 5f, 10f, 2);
        CreateFighterData("FD_Archer", "Archer", 85, 6.5f, 9f, 7f, 13f, 2);
        CreateFighterData("FD_Mage", "Mage", 75, 5.5f, 8f, 7.5f, 14f, 3);
        AssetDatabase.SaveAssets();
        Debug.Log("Created 3 FighterData assets");
    }

    private static void CreateFighterData(string assetName, string displayName,
        float weight, float walkSpeed, float runSpeed, float airSpeed,
        float jumpForce, int jumpCount)
    {
        var fd = ScriptableObject.CreateInstance<FighterData>();
        fd.fighterName = displayName;
        fd.weight = weight;
        fd.walkSpeed = walkSpeed;
        fd.runSpeed = runSpeed;
        fd.airSpeed = airSpeed;
        fd.jumpForce = jumpForce;
        fd.jumpCount = jumpCount;

        // 填充所有攻击数据（使用默认值，用户可后续微调）
        fd.jab1 = MakeAttack("Jab1", 3, 80, 10, 20, 0.05f, 0.05f, 0.1f);
        fd.jab2 = MakeAttack("Jab2", 3, 80, 15, 25, 0.05f, 0.05f, 0.1f);
        fd.jab3 = MakeAttack("Jab3", 5, 70, 30, 40, 0.08f, 0.05f, 0.15f);
        fd.tiltSide = MakeAttack("SideTilt", 8, 40, 30, 50, 0.1f, 0.07f, 0.2f);
        fd.tiltUp = MakeAttack("UpTilt", 7, 90, 35, 45, 0.1f, 0.08f, 0.2f);
        fd.tiltDown = MakeAttack("DownTilt", 6, 0, 30, 40, 0.08f, 0.07f, 0.17f);
        fd.smashSide = MakeAttack("SmashSide", 15, 45, 50, 80, 0.3f, 0.07f, 0.3f);
        fd.smashUp = MakeAttack("SmashUp", 14, 90, 55, 75, 0.28f, 0.08f, 0.28f);
        fd.smashDown = MakeAttack("SmashDown", 13, 0, 50, 70, 0.25f, 0.07f, 0.3f);
        fd.aerialNeutral = MakeAttack("AirN", 8, 70, 35, 40, 0.08f, 0.15f, 0.15f);
        fd.aerialForward = MakeAttack("AirF", 10, 50, 40, 55, 0.1f, 0.1f, 0.18f);
        fd.aerialBack = MakeAttack("AirB", 10, 60, 40, 55, 0.1f, 0.1f, 0.18f);
        fd.aerialUp = MakeAttack("AirU", 8, 90, 35, 45, 0.08f, 0.15f, 0.2f);
        fd.aerialDown = MakeAttack("AirD", 9, 0, 40, 50, 0.1f, 0.15f, 0.12f);

        AssetDatabase.CreateAsset(fd, $"Assets/_Game/ScriptableObjects/Characters/{assetName}.asset");
    }

    private static AttackData MakeAttack(string name, float damage, float angle,
        float baseKB, float growth, float startup, float active, float recovery)
    {
        return new AttackData
        {
            attackName = name,
            damage = damage,
            knockbackAngle = angle,
            knockbackBase = baseKB,
            knockbackGrowth = growth,
            startupTime = startup,
            activeTime = active,
            recoveryTime = recovery,
            hitstopDuration = damage > 10 ? 0.08f : 0.04f,
        };
    }

    // ==================== 创建 StageData 资产 ====================
    [MenuItem("SuperSmashLike/StageData Asset", false, 12)]
    public static void CreateStageData()
    {
        var sd = ScriptableObject.CreateInstance<StageData>();
        sd.stageName = "Battle Arena";
        sd.cameraBoundsMin = new Vector2(-20f, -10f);
        sd.cameraBoundsMax = new Vector2(20f, 15f);
        sd.blastZoneWidth = 30f;
        sd.blastZoneHeight = 20f;

        AssetDatabase.CreateAsset(sd, "Assets/_Game/ScriptableObjects/Stages/SD_Arena.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("Created SD_Arena.asset");
    }

    // ==================== 创建 InputActions 文件 ====================
    [MenuItem("SuperSmashLike/InputActions Asset", false, 13)]
    public static void CreateInputActions()
    {
        string path = "Assets/_Game/Input/Controls.inputactions";
        if (File.Exists(path))
        {
            Debug.Log("Controls.inputactions already exists, skipping.");
            return;
        }

        // Input Action Asset 是 JSON 格式
        // 这里用 Unity 的 InputActionAsset API 来创建
        var inputActionAsset = ScriptableObject.CreateInstance<UnityEngine.InputSystem.InputActionAsset>();

        // 直接用 Unity API 创建
        // 由于 InputActionAsset 的创建比较复杂，我们写一个简化版本
        // 用户随后可以通过 Input Actions 编辑器微调
        string jsonContent = @"{
    ""name"": ""Controls"",
    ""maps"": [
        {
            ""name"": ""Gameplay"",
            ""actions"": [
                { ""name"": ""Move"", ""type"": ""Value"", ""expectedControlType"": ""Vector2"" },
                { ""name"": ""Jump"", ""type"": ""Button"" },
                { ""name"": ""Attack"", ""type"": ""Button"" },
                { ""name"": ""Special"", ""type"": ""Button"" },
                { ""name"": ""Shield"", ""type"": ""Button"" },
                { ""name"": ""Grab"", ""type"": ""Button"" },
                { ""name"": ""Taunt"", ""type"": ""Button"" },
                { ""name"": ""Pause"", ""type"": ""Button"" }
            ],
            ""bindings"": [
                { ""action"": ""Move"", ""path"": ""<Gamepad>/leftStick"", ""groups"": ""Gamepad"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/w"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/a"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/s"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/d"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/upArrow"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/leftArrow"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/downArrow"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Move"", ""path"": ""<Keyboard>/rightArrow"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Jump"", ""path"": ""<Gamepad>/buttonSouth"", ""groups"": ""Gamepad"" },
                { ""action"": ""Jump"", ""path"": ""<Keyboard>/space"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Jump"", ""path"": ""<Keyboard>/numpad0"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Attack"", ""path"": ""<Gamepad>/buttonWest"", ""groups"": ""Gamepad"" },
                { ""action"": ""Attack"", ""path"": ""<Keyboard>/j"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Attack"", ""path"": ""<Keyboard>/numpad1"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Special"", ""path"": ""<Gamepad>/buttonNorth"", ""groups"": ""Gamepad"" },
                { ""action"": ""Special"", ""path"": ""<Keyboard>/k"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Special"", ""path"": ""<Keyboard>/numpad2"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Shield"", ""path"": ""<Gamepad>/leftTrigger"", ""groups"": ""Gamepad"" },
                { ""action"": ""Shield"", ""path"": ""<Keyboard>/l"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Shield"", ""path"": ""<Keyboard>/numpad3"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Grab"", ""path"": ""<Gamepad>/rightShoulder"", ""groups"": ""Gamepad"" },
                { ""action"": ""Grab"", ""path"": ""<Keyboard>/u"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Grab"", ""path"": ""<Keyboard>/numpad4"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Taunt"", ""path"": ""<Gamepad>/dpad/up"", ""groups"": ""Gamepad"" },
                { ""action"": ""Taunt"", ""path"": ""<Keyboard>/t"", ""groups"": ""KeyboardP1"" },
                { ""action"": ""Taunt"", ""path"": ""<Keyboard>/numpad5"", ""groups"": ""KeyboardP2"" },
                { ""action"": ""Pause"", ""path"": ""<Gamepad>/start"", ""groups"": ""Gamepad"" },
                { ""action"": ""Pause"", ""path"": ""<Keyboard>/escape"", ""groups"": ""KeyboardP1;KeyboardP2"" }
            ]
        }
    ],
    ""controlSchemes"": [
        { ""name"": ""KeyboardP1"", ""bindingGroup"": ""KeyboardP1"", ""devices"": [{ ""devicePath"": ""<Keyboard>"" }, { ""devicePath"": ""<Mouse>"" }] },
        { ""name"": ""KeyboardP2"", ""bindingGroup"": ""KeyboardP2"", ""devices"": [{ ""devicePath"": ""<Keyboard>"" }, { ""devicePath"": ""<Mouse>"" }] },
        { ""name"": ""Gamepad"", ""bindingGroup"": ""Gamepad"", ""devices"": [{ ""devicePath"": ""<Gamepad>"" }] }
    ]
}";
        File.WriteAllText(path, jsonContent);
        AssetDatabase.Refresh();
        Debug.Log("Created Controls.inputactions");
    }

    // ==================== 创建基础 Animator Controller ====================
    [MenuItem("SuperSmashLike/Animator Controller", false, 14)]
    public static void CreateAnimatorController()
    {
        string path = "Assets/_Game/Animations/FighterAnimator.controller";
        if (File.Exists(path))
        {
            Debug.Log("FighterAnimator.controller already exists, skipping.");
            return;
        }

        var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
        var rootStateMachine = controller.layers[0].stateMachine;

        // 添加参数
        controller.AddParameter("State", AnimatorControllerParameterType.Int);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);

        // 创建基础状态
        var idleState = rootStateMachine.AddState("Idle");
        rootStateMachine.defaultState = idleState;

        var runState = rootStateMachine.AddState("Run");
        var jumpState = rootStateMachine.AddState("Jump");
        var fallState = rootStateMachine.AddState("Fall");
        var attackState = rootStateMachine.AddState("Attack");
        var hitState = rootStateMachine.AddState("Hit");
        var knockbackState = rootStateMachine.AddState("Knockback");
        var shieldState = rootStateMachine.AddState("Shield");

        // 添加基础转换
        var idleToRun = idleState.AddTransition(runState);
        idleToRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        var runToIdle = runState.AddTransition(idleState);
        runToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        var idleToJump = idleState.AddTransition(jumpState);
        idleToJump.AddCondition(AnimatorConditionMode.Greater, 0.1f, "VerticalSpeed");
        idleToJump.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");

        var jumpToFall = jumpState.AddTransition(fallState);
        jumpToFall.AddCondition(AnimatorConditionMode.Less, 0, "VerticalSpeed");

        var fallToIdle = fallState.AddTransition(idleState);
        fallToIdle.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");

        Debug.Log("Created FighterAnimator.controller (basic states)");
    }

    // ==================== 创建 Fighter 预制体 ====================
    [MenuItem("SuperSmashLike/Fighter Prefab", false, 15)]
    public static void CreateFighterPrefab()
    {
        // 创建 Fighter 的 GameObject 层级结构
        var fighterRoot = new GameObject("Fighter_Base");

        // Rigidbody2D
        var rb = fighterRoot.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 3f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // Collider
        var col = fighterRoot.AddComponent<BoxCollider2D>();
        col.size = new Vector2(1f, 1.8f);
        col.offset = new Vector2(0f, 0.9f);

        // Hurtbox 子物体
        var hurtboxObj = new GameObject("Hurtbox");
        hurtboxObj.transform.SetParent(fighterRoot.transform);
        hurtboxObj.transform.localPosition = Vector3.zero;
        var hurtboxCol = hurtboxObj.AddComponent<BoxCollider2D>();
        hurtboxCol.size = new Vector2(0.8f, 1.6f);
        hurtboxCol.isTrigger = true;
        var hurtbox = hurtboxObj.AddComponent<Hurtbox>();

        // GroundCheck 子物体
        var groundCheck = new GameObject("GroundCheck");
        groundCheck.transform.SetParent(fighterRoot.transform);
        groundCheck.transform.localPosition = new Vector3(0f, -0.9f, 0f);

        // Visual 子物体（用 Sprite 临时替代模型）
        var visual = new GameObject("Visual");
        visual.transform.SetParent(fighterRoot.transform);
        visual.transform.localPosition = Vector3.zero;
        var sr = visual.AddComponent<SpriteRenderer>();
        sr.color = Color.white;
        // 创建一个简单的方形纹理作为占位
        var texture = new Texture2D(32, 32);
        Color[] pixels = new Color[1024];
        for (int i = 0; i < pixels.Length; i++)
        {
            int x = i % 32;
            int y = i / 32;
            // 身体为蓝色，头为肤色
            pixels[i] = y > 20 ? Color.blue : new Color(1f, 0.8f, 0.6f);
        }
        texture.SetPixels(pixels);
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32);
        sr.sprite = sprite;
        sr.sortingOrder = 1;

        // 加载 FighterData 资产
        string knightPath = "Assets/_Game/ScriptableObjects/Characters/FD_Knight.asset";
        var knightData = AssetDatabase.LoadAssetAtPath<FighterData>(knightPath);

        // FighterController
        var fc = fighterRoot.AddComponent<FighterController>();
        fc.fighterData = knightData;
        fc.groundCheck = groundCheck.transform;
        fc.groundCheckRadius = 0.1f;
        fc.groundLayer = LayerMask.GetMask("Default");
        fc.hurtbox = hurtbox;

        // InputManager
        fighterRoot.AddComponent<InputManager>();

        // 保存为预制体
        string prefabPath = "Assets/_Game/Prefabs/Fighters/Fighter_Base.prefab";
        PrefabUtility.SaveAsPrefabAsset(fighterRoot, prefabPath);
        Object.DestroyImmediate(fighterRoot);

        Debug.Log($"Created {prefabPath}");
    }

    // ==================== 创建舞台预制体 ====================
    [MenuItem("SuperSmashLike/Stage Prefab", false, 16)]
    public static void CreateStagePrefab()
    {
        var stage = new GameObject("BattleArena");

        // 主平台
        var mainPlat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mainPlat.name = "MainPlatform";
        mainPlat.transform.SetParent(stage.transform);
        mainPlat.transform.localScale = new Vector3(20f, 0.5f, 1f);
        mainPlat.transform.localPosition = new Vector3(0f, -2f, 0f);
        mainPlat.GetComponent<Renderer>().material.color = new Color(0.3f, 0.3f, 0.3f);
        Object.DestroyImmediate(mainPlat.GetComponent<BoxCollider>());
        var mainCol = mainPlat.AddComponent<BoxCollider2D>();
        mainCol.size = new Vector2(20f, 0.5f);
        var mainPlatScript = mainPlat.AddComponent<Platform>();

        // 左侧浮台
        var leftPlat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leftPlat.name = "LeftPlatform";
        leftPlat.transform.SetParent(stage.transform);
        leftPlat.transform.localScale = new Vector3(4f, 0.5f, 1f);
        leftPlat.transform.localPosition = new Vector3(-6f, 2f, 0f);
        leftPlat.GetComponent<Renderer>().material.color = new Color(0.4f, 0.4f, 0.4f);
        Object.DestroyImmediate(leftPlat.GetComponent<BoxCollider>());
        var leftCol = leftPlat.AddComponent<BoxCollider2D>();
        leftCol.size = new Vector2(4f, 0.5f);
        leftPlat.AddComponent<Platform>();

        // 右侧浮台
        var rightPlat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rightPlat.name = "RightPlatform";
        rightPlat.transform.SetParent(stage.transform);
        rightPlat.transform.localScale = new Vector3(4f, 0.5f, 1f);
        rightPlat.transform.localPosition = new Vector3(6f, 2f, 0f);
        rightPlat.GetComponent<Renderer>().material.color = new Color(0.4f, 0.4f, 0.4f);
        Object.DestroyImmediate(rightPlat.GetComponent<BoxCollider>());
        var rightCol = rightPlat.AddComponent<BoxCollider2D>();
        rightCol.size = new Vector2(4f, 0.5f);
        rightPlat.AddComponent<Platform>();

        // 顶部浮台
        var topPlat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        topPlat.name = "TopPlatform";
        topPlat.transform.SetParent(stage.transform);
        topPlat.transform.localScale = new Vector3(6f, 0.5f, 1f);
        topPlat.transform.localPosition = new Vector3(0f, 5f, 0f);
        topPlat.GetComponent<Renderer>().material.color = new Color(0.5f, 0.5f, 0.5f);
        Object.DestroyImmediate(topPlat.GetComponent<BoxCollider>());
        var topCol = topPlat.AddComponent<BoxCollider2D>();
        topCol.size = new Vector2(6f, 0.5f);
        topPlat.AddComponent<Platform>();

        // BlastZone 上
        CreateBlastZone(stage, "BlastZone_Top", new Vector3(0f, 15f, 0f), new Vector2(50f, 2f), true, false, false, false);
        // BlastZone 下
        CreateBlastZone(stage, "BlastZone_Bottom", new Vector3(0f, -15f, 0f), new Vector2(50f, 2f), false, true, false, false);
        // BlastZone 左
        CreateBlastZone(stage, "BlastZone_Left", new Vector3(-25f, 0f, 0f), new Vector2(2f, 40f), false, false, true, false);
        // BlastZone 右
        CreateBlastZone(stage, "BlastZone_Right", new Vector3(25f, 0f, 0f), new Vector2(2f, 40f), false, false, false, true);

        // SpawnPoints
        CreateSpawnPoint(stage, "SpawnPoint_1", new Vector3(-4f, 3f, 0f));
        CreateSpawnPoint(stage, "SpawnPoint_2", new Vector3(4f, 3f, 0f));
        CreateSpawnPoint(stage, "SpawnPoint_3", new Vector3(-2f, 8f, 0f));
        CreateSpawnPoint(stage, "SpawnPoint_4", new Vector3(2f, 8f, 0f));

        string prefabPath = "Assets/_Game/Prefabs/Stages/BattleArena.prefab";
        PrefabUtility.SaveAsPrefabAsset(stage, prefabPath);
        Object.DestroyImmediate(stage);
        Debug.Log($"Created {prefabPath}");
    }

    private static void CreateBlastZone(GameObject parent, string name, Vector3 pos, Vector2 size,
        bool isTop, bool isBottom, bool isLeft, bool isRight)
    {
        var bz = new GameObject(name);
        bz.transform.SetParent(parent.transform);
        bz.transform.localPosition = pos;
        var col = bz.AddComponent<BoxCollider2D>();
        col.size = size;
        col.isTrigger = true;
        var bzScript = bz.AddComponent<BlastZone>();
        bzScript.isTop = isTop;
        bzScript.isBottom = isBottom;
        bzScript.isLeft = isLeft;
        bzScript.isRight = isRight;
    }

    private static void CreateSpawnPoint(GameObject parent, string name, Vector3 pos)
    {
        var sp = new GameObject(name);
        sp.transform.SetParent(parent.transform);
        sp.transform.localPosition = pos;
    }

    // ==================== 创建 Demo 场景 ====================
    [MenuItem("SuperSmashLike/Demo Scene (BattleTest)", false, 20)]
    public static void CreateDemoScene()
    {
        string scenePath = "Assets/_Game/Scenes/BattleTest.unity";
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 移除默认的 Main Camera（我们会用自己的）
        var defaultCam = GameObject.Find("Main Camera");
        if (defaultCam != null) Object.DestroyImmediate(defaultCam);

        // === 1. GameManager ===
        var gmObj = new GameObject("GameManager");
        var gm = gmObj.AddComponent<GameManager>();
        string gsPath = "Assets/_Game/ScriptableObjects/GameModes/GS_Default.asset";
        gm.gameSettings = AssetDatabase.LoadAssetAtPath<GameSettings>(gsPath);
        gm.skipToBattle = true;
        var pooler = gmObj.AddComponent<ObjectPooler>();

        // === 2. Camera ===
        var camObj = new GameObject("MainCamera");
        var cam = camObj.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 12f;
        cam.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
        camObj.transform.position = new Vector3(0f, 0f, -10f);
        var camMan = camObj.AddComponent<CameraManager>();
        camMan.targetCamera = cam;
        string sdPath = "Assets/_Game/ScriptableObjects/Stages/SD_Arena.asset";
        var sd = AssetDatabase.LoadAssetAtPath<StageData>(sdPath);
        if (sd != null)
        {
            camMan.cameraBoundsMin = sd.cameraBoundsMin;
            camMan.cameraBoundsMax = sd.cameraBoundsMax;
        }

        // === 3. MatchManager ===
        var mmObj = new GameObject("MatchManager");
        var mm = mmObj.AddComponent<MatchManager>();

        // === 4. Stage ===
        string stagePath = "Assets/_Game/Prefabs/Stages/BattleArena.prefab";
        var stagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(stagePath);
        var stageInstance = PrefabUtility.InstantiatePrefab(stagePrefab) as GameObject;
        stageInstance.name = "BattleArena";

        // 关联 SpawnPoints
        var spawnPoints = stageInstance.GetComponentsInChildren<Transform>();
        var spawnList = new System.Collections.Generic.List<Transform>();
        foreach (var t in spawnPoints)
        {
            if (t.name.StartsWith("SpawnPoint"))
                spawnList.Add(t);
        }
        mm.spawnPoints = spawnList.ToArray();

        // === 5. Fighter P1 ===
        string fighterPath = "Assets/_Game/Prefabs/Fighters/Fighter_Base.prefab";
        var fighterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fighterPath);
        var p1 = PrefabUtility.InstantiatePrefab(fighterPrefab) as GameObject;
        p1.name = "Fighter_P1";
        p1.transform.position = new Vector3(-4f, 3f, 0f);
        var fc1 = p1.GetComponent<FighterController>();
        fc1.playerID = 0;
        var p1Input = p1.GetComponent<PlayerInput>();
        if (p1Input == null) p1Input = p1.AddComponent<PlayerInput>();
        p1Input.defaultActionMap = "Gameplay";
        p1Input.defaultControlScheme = "KeyboardP1";
        string inputPath = "Assets/_Game/Input/Controls.inputactions";
        var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(inputPath);
        if (inputAsset != null) p1Input.actions = inputAsset;

        // === 6. Fighter P2 ===
        var p2 = PrefabUtility.InstantiatePrefab(fighterPrefab) as GameObject;
        p2.name = "Fighter_P2";
        p2.transform.position = new Vector3(4f, 3f, 0f);
        var fc2 = p2.GetComponent<FighterController>();
        fc2.playerID = 1;
        var p2Input = p2.GetComponent<PlayerInput>();
        if (p2Input == null) p2Input = p2.AddComponent<PlayerInput>();
        p2Input.defaultActionMap = "Gameplay";
        p2Input.defaultControlScheme = "KeyboardP2";
        if (inputAsset != null) p2Input.actions = inputAsset;

        // === 7. 关联引用 ===
        mm.cameraManager = camMan;
        // 注册角色到 MatchManager
        var fighters = new FighterController[] { fc1, fc2 };
        // MatchManager 没有直接持有 fighters 数组字段
        // 但 GameManager 的 RegisterFighter 会在 Start 中自动注册

        // === 8. EventSystem ===
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // === 9. 设置 Build Settings ===
        EditorSceneManager.SaveScene(scene, scenePath);
        var buildSettingsScenes = EditorBuildSettings.scenes;
        bool alreadyAdded = false;
        foreach (var bs in buildSettingsScenes)
        {
            if (bs.path == scenePath) { alreadyAdded = true; break; }
        }
        if (!alreadyAdded)
        {
            var newScenes = new EditorBuildSettingsScene[buildSettingsScenes.Length + 1];
            System.Array.Copy(buildSettingsScenes, newScenes, buildSettingsScenes.Length);
            newScenes[newScenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = newScenes;
        }

        Debug.Log($"✅ Created and saved BattleTest scene at {scenePath}");
        Debug.Log("✅ All done! Press Play to test the demo.");
    }
}
