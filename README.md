# BilliardPhysics Core

一个基于 Unity 的 2D 台球物理模拟核心库，使用定点数运算保证跨平台确定性。

## 项目简介

`BilliardPhysics` 是一个纯 C# 编写的台球物理引擎，以 Unity Package（Assembly Definition）形式提供。它实现了：

- 连续碰撞检测（CCD），防止高速球穿透台边或其他球
- 定点数（Fixed-Point）数学库，消除浮点误差，确保模拟结果的确定性
- 球与球、球与台边的冲量碰撞解算
- 滑动 / 滚动 / 旋转三段式摩擦力模型
- 球杆击打（包含顶旋、底旋、侧旋）
- 球袋捕获逻辑
- Unity 编辑器工具，支持在 Inspector 中配置台面和球袋

## 目录结构

```
Assets/BilliardPhysics/
├── Runtime/
│   ├── BilliardPhysics.Runtime.asmdef
│   ├── Math/
│   │   ├── Fix64.cs                      # 32.32 有符号定点数
│   │   └── FixVec2.cs                    # 基于定点数的 2D 向量
│   ├── Physics/
│   │   ├── Ball.cs                       # 球的状态与物理参数
│   │   ├── Segment.cs                    # 台边/库边折线段（支持 ConnectionPoints）
│   │   ├── Pocket.cs                     # 球袋（含口沿线段 RimSegments）
│   │   ├── PhysicsWorld2D.cs             # 主仿真入口
│   │   ├── CCDSystem.cs                  # 连续碰撞检测
│   │   ├── ImpulseResolver.cs            # 碰撞冲量解算
│   │   ├── MotionSimulator.cs            # 摩擦力与运动积分
│   │   └── CueStrike.cs                  # 球杆击打
│   ├── AimAssist/
│   │   └── AimAssistRenderer.cs          # 击球辅助线 MonoBehaviour（LineRenderer 渲染）
│   ├── AniHelp/
│   │   ├── PocketDropAniHelper.cs        # 球落袋动画纯逻辑辅助（三段式：吸引→下沉→消失）
│   │   └── PocketPostRollAniHelper.cs    # 落袋后滚动动画纯逻辑辅助（含物理角速度、Z轴钳位）
│   └── Table/
│       ├── TableDefinition.cs            # 台面配置（ScriptableObject，传统方式）
│       ├── PocketDefinition.cs           # 球袋配置（ScriptableObject，传统方式）
│       ├── TableAndPocketAuthoring.cs    # 台面与球袋 MonoBehaviour 编辑组件
│       ├── TableAndPocketBinaryLoader.cs # 从二进制资产加载台面与球袋
│       └── RimSegmentHelper.cs           # 口沿折线段端点升降辅助工具
├── Editor/
│   ├── BilliardPhysics.Editor.asmdef
│   ├── TableEditorTool.cs                # 台面场景视图编辑器工具
│   ├── PocketEditorTool.cs               # 球袋场景视图编辑器工具
│   ├── TableAndPocketAuthoringEditor.cs  # TableAndPocketAuthoring 自定义 Inspector
│   ├── ExportFixedBinaryHelper.cs        # 二进制导出纯逻辑辅助（可单元测试）
│   └── ImportFixedBinaryHelper.cs        # 二进制导入纯逻辑辅助（可单元测试）
└── Tests/
    └── Editor/
        ├── BilliardPhysics.Tests.Editor.asmdef
        ├── ExportFixedBinaryHelperTests.cs
        ├── ImportFixedBinaryHelperTests.cs
        ├── RimFromCircleGeneratorTests.cs
        ├── RimSegmentHelperTests.cs
        ├── SegmentPolylineTests.cs
        └── TableAndPocketBinaryLoaderTests.cs
```

## 快速上手

### 1. 创建仿真世界

**方式 A：从 ScriptableObject 加载（传统方式）**

```csharp
using BilliardPhysics;

// 创建物理世界
var world = new PhysicsWorld2D();

// 从 TableDefinition ScriptableObject 加载台边
TableDefinition tableDef = ...; // 通过 Inspector 赋值或 Resources.Load
world.SetTableSegments(tableDef.BuildSegments());

// 从 PocketDefinition ScriptableObject 加载球袋
PocketDefinition pocketDef = ...; // 通过 Inspector 赋值或 Resources.Load
foreach (var pocket in pocketDef.BuildPockets())
    world.AddPocket(pocket);
```

**方式 B：从二进制资产加载（推荐，运行时零 GC）**

```csharp
using BilliardPhysics;

// 通过 Inspector 赋值或 Resources.Load 获取由编辑器导出的 .bytes 资产
TextAsset binaryAsset = ...;

var (tableSegments, pockets) = TableAndPocketBinaryLoader.Load(binaryAsset);

var world = new PhysicsWorld2D();
world.SetTableSegments(tableSegments);
foreach (var pocket in pockets)
    world.AddPocket(pocket);
```

### 2. 添加球

```csharp
// 使用标准半径（0.5）和质量（1.0）
var cueBall = new Ball(0);
cueBall.Position = new FixVec2(Fix64.Zero, Fix64.Zero);
world.AddBall(cueBall);

// 自定义半径和质量
var ball = new Ball(1, Fix64.FromFloat(0.5f), Fix64.FromFloat(1.0f));
ball.Position = new FixVec2(Fix64.FromFloat(1.0f), Fix64.Zero);
world.AddBall(ball);
```

### 3. 击打母球

```csharp
// direction: 归一化方向向量（Normalized 属性返回新的单位向量，不修改原向量）
// strength: 击打力度
// spinX: 左右侧旋偏移
// spinY: 上下旋偏移（顶旋/底旋）
FixVec2 direction = new FixVec2(Fix64.One, Fix64.Zero).Normalized;
Fix64 strength = Fix64.From(10);
world.ApplyCueStrike(cueBall, direction, strength, Fix64.Zero, Fix64.Zero);
```

### 4. 推进仿真

```csharp
// 在每帧（或固定时间步）调用，默认时间步为 1/60 秒
void Update()
{
    world.Step();

    // 读取球的最新位置用于渲染
    foreach (var ball in world.Balls)
    {
        if (!ball.IsPocketed)
        {
            float x = (float)ball.Position.X;
            float y = (float)ball.Position.Y;
            // 更新对应 GameObject 的位置
        }
    }
}
```

### 5. 重置仿真

```csharp
// 重置所有球的速度和落袋状态，保留位置
world.Reset();
```

### 6. 击球辅助线（Aim Assist）

`AimAssistRenderer` 是一个 Unity `MonoBehaviour` 组件，在场景中用 `LineRenderer` 实时绘制：
- 白球到首次碰撞位置的路径线段
- 碰撞位置的空心圆（表示白球触碰时的球心位置）
- 若首次碰撞为球-球碰撞，额外绘制两球碰后的方向线

**挂载方式：**
1. 在场景任意 GameObject 上 **Add Component → BilliardPhysics → Aim Assist Renderer**（或在代码中 `AddComponent<AimAssistRenderer>()`）。
2. 通过 Inspector 或代码调用 `SetPhysicsWorld(world)` 传入 `PhysicsWorld2D` 实例。
3. 在 Inspector 中按需调整参数（球半径、最大检测距离、颜色、线宽、速度比例等）。

**使用示例：**

```csharp
using BilliardPhysics;
using BilliardPhysics.AimAssist;
using UnityEngine;

public class AimController : MonoBehaviour
{
    // 在 Inspector 中赋值
    public AimAssistRenderer aimAssist;

    private PhysicsWorld2D _world;

    void Start()
    {
        // 创建并配置物理世界 ...
        _world = new PhysicsWorld2D();
        // world.SetTableSegments(...);  world.AddBall(...); 等

        aimAssist.SetPhysicsWorld(_world);
    }

    void Update()
    {
        if (IsPlayerAiming())
        {
            // 从物理坐标转为 Unity 世界坐标（Z 视需求设置）
            Ball cueBall = _world.Balls[0];
            Vector3 cueBallPos = new Vector3(
                cueBall.Position.X.ToFloat(),
                cueBall.Position.Y.ToFloat(),
                0f);

            // cueDirection 可来自鼠标、摇杆等输入
            Vector3 cueDir = new Vector3(inputX, inputY, 0f);

            aimAssist.DrawAimAssist(cueBallPos, cueDir);
        }
        else
        {
            aimAssist.Clear();
        }
    }
}
```

**可配置参数一览：**

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `BallRadius` | 0.28575 | 白球半径（物理单位） |
| `MaxDistance` | 10 | 最大检测/绘制距离 |
| `CircleSegments` | 32 | 空心圆的折线段数 |
| `LineWidth` | 0.02 | 所有线段宽度（世界单位） |
| `PathColor` | 白色 | 路径线段颜色 |
| `GhostCircleColor` | 白色 | 空心圆颜色 |
| `CueBallPostColor` | 白色 | 白球碰后方向线颜色 |
| `TargetBallPostColor` | 黄色 | 目标球碰后方向线颜色 |
| `LineMaterial` | null | 自定义材质（留空使用 Unity 默认） |
| `V0` | 1.0 | 参考速度（仅影响碰后线段长度比例） |
| `ScaleFactor` | 1.0 | 碰后线段长度 = 速度分量 × ScaleFactor |

**隐藏/清除：**
- `aimAssist.Clear()` — 清除所有线段并隐藏
- `aimAssist.Hide()` — 仅隐藏，不清除内部数据

### 7. 球落袋动画（PocketDropAniHelper）

`PocketDropAniHelper` 是一个纯逻辑、无 GC 分配的动画辅助类，驱动三段式"球落袋"视觉效果：

1. **Attract（吸引）** — 球从当前位置平滑滑向球袋方向（EaseOut）
2. **Sink（下沉）**   — 球沿世界 `-Z` 方向下沉（EaseIn）
3. **Vanish（消失）** — 球缩放至 0、alpha 渐隐至 0（EaseIn）

完全解耦渲染层：不持有任何 `MonoBehaviour`、`Transform`、`Renderer` 或 `Material` 引用，一个实例可复用（对象池友好）。

**为什么需要对象池？**

`PocketDropAniHelper` 是单条动画轨道，一次 `StartDrop` 对应一套独立的播放状态。当多个球在同一帧落袋时，若共享同一个 helper，后一次 `StartDrop` 会立即覆盖前一次的内部状态，导致先落袋球的动画被中断。因此**并发动画必须各持独立实例**；对象池让这些实例在动画完成后（`Finished`）得以复用，避免每次落袋都触发 GC。

**helper 的回收点固定在 `PocketDropPhase.Finished`**：此时动画已完整播完，`Reset()` 后即可安全投入下一次使用。

**Ball 是唯一权威数据源：Scale 用组合方式附加**

`BallDropController` 以 `Ball`（物理层）实例作为唯一权威数据源——落袋动画的起始位置直接读自 `Ball.Position`，确保视觉与物理状态一致。

`PocketDropState.scale` 描述的是动画期间的临时缩放，**不存入 `Ball` 的持久字段**。取而代之，控制器为每颗正在落袋的球动态创建一个 `BallScaleState` 组合对象，仅在动画期间有效；动画结束后该对象被丢弃，`Ball` 的核心状态模型保持干净。

**落袋后滚动路径（PostPocketRollPath）**

`TableConfig` 上有一个可选字段 `PostPocketRollPath`（类型为 `SegmentData`），在 `TableAndPocketAuthoring` Inspector 中配置，所有球袋共用该路径：

- **Start** → **ConnectionPoints**（0..N 个中间路径点）→ **End** 构成完整滚动轨迹。
- 当 `Start == End` 且 `ConnectionPoints` 为空时，视为"未配置"，跳过滚动阶段。
- 路径在场景视图中以**橙色**折线显示（带方向箭头），便于编辑器内直观确认路径走向。

**多球并发与碰撞停止规则**

- 落袋动画（`_activeDrops`）和落袋后滚动（`_activeRolls`）各有独立的活跃列表，可同时驱动任意数量的球。
- 滚动阶段由 `PocketPostRollAniHelper` 驱动：每颗球持有独立的 helper 实例，碰撞检测与早停逻辑均由 helper 内部完成（通过 `PocketPostRollRequest.stoppedBalls` 传入已停球信息）。
- 新一局开始前调用 `ClearPocketedBalls()` 清空停止记录。

**使用示例：**

```csharp
using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.AniHelp;
using UnityEngine;

// ── Scale 组合对象（不是 Ball 的持久字段）────────────────────────────────────
// Ball.cs 上无 scale 字段。BallDropController 在落袋动画期间临时为每颗球附加一个
// BallScaleState；动画结束后该对象被丢弃，Ball 的核心状态模型保持干净。
public sealed class BallScaleState
{
    /// <summary>当前均匀缩放值（Vanish 阶段 1 → 0）。仅落袋动画期间有效。</summary>
    public float Scale = 1f;
}

// ── 轻量对象池：避免并发动画时频繁 new helper ─────────────────────────────────
public sealed class PocketDropAniHelperPool
{
    private readonly Stack<PocketDropAniHelper> _stack = new Stack<PocketDropAniHelper>();

    /// <summary>取出一个 helper（池为空则新建）。</summary>
    public PocketDropAniHelper Rent()
        => _stack.Count > 0 ? _stack.Pop() : new PocketDropAniHelper();

    /// <summary>归还 helper。归还前自动调用 Reset() 清除播放状态。</summary>
    public void Return(PocketDropAniHelper helper)
    {
        helper.Reset();
        _stack.Push(helper);
    }
}

// ── 单条落袋动画的数据容器 ────────────────────────────────────────────────────
// Ball 是唯一物理权威数据源；BallScaleState 是落袋期间的临时组合，不持久化到 Ball。
internal sealed class ActiveDrop
{
    public Ball            BallData;       // 物理权威：位置由此读取
    public Transform       BallTransform;  // 渲染层 Transform
    public Renderer        BallRenderer;   // 渲染层 Renderer
    public Color           BaseColor;      // 落袋瞬间的基础颜色
    public BallScaleState  ScaleState;     // 组合式缩放（动画结束后丢弃）
    public PocketDropAniHelper Helper;     // 动画驱动（来自池）
    public SegmentData     RollPath;       // 落袋后滚动路径（来自 TableConfig.PostPocketRollPath）
}

// ── 落袋后滚动状态容器（使用 PocketPostRollAniHelper 驱动）────────────────────
internal sealed class ActiveRoll
{
    public Transform               BallTransform;  // 渲染层 Transform
    public PocketPostRollAniHelper Helper;          // 滚动动画驱动（处理 Z 轴钳位与物理角速度）
    public float                   BallRadius;      // 球半径（供停止记录）
}

// ── 控制器 ────────────────────────────────────────────────────────────────────
public class BallDropController : MonoBehaviour
{
    [Tooltip("落袋后整段滚动动画的总时长（秒）")]
    public float RollDuration = 1.5f;

    // 对象池：在同一个 Controller 实例内复用 helper
    private readonly PocketDropAniHelperPool _pool        = new PocketDropAniHelperPool();
    // 活跃落袋动画列表：同一帧可容纳任意数量的并发落袋动画
    private readonly List<ActiveDrop>        _activeDrops = new List<ActiveDrop>();
    // 活跃滚动列表：落袋动画完成后进入此列表
    private readonly List<ActiveRoll>        _activeRolls = new List<ActiveRoll>();
    // 已停止在路径上的球，供后续球的 PocketPostRollRequest.stoppedBalls 使用
    private readonly List<StoppedBallInfo>   _stoppedBalls = new List<StoppedBallInfo>();

    // ── 公共 API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 单球落袋入口。物理层检测到球落袋时调用。
    /// Ball 的当前 Position 将作为动画的起始位置（唯一权威数据源）。
    /// </summary>
    /// <param name="ball">物理球对象（唯一权威数据源）。</param>
    /// <param name="ballTransform">落袋球的 Transform，用于驱动渲染位置与缩放。</param>
    /// <param name="ballRenderer">落袋球的 Renderer，用于驱动材质 alpha。</param>
    /// <param name="pocketWorldPos">球袋中心的世界坐标。</param>
    /// <param name="rollPath">
    /// 落袋后滚动路径（来自 <c>TableConfig.PostPocketRollPath</c>）。
    /// Start == End 且无 ConnectionPoints 时跳过滚动。
    /// </param>
    public void OnBallPocketed(
        Ball ball, Transform ballTransform, Renderer ballRenderer,
        Vector3 pocketWorldPos, SegmentData rollPath)
    {
        StartOneDrop(ball, ballTransform, ballRenderer, pocketWorldPos, rollPath);
    }

    /// <summary>
    /// 多球同时落袋入口。同一帧内每颗球各得独立的 helper 实例，可并发播放。
    /// </summary>
    public void OnBallsPocketed(
        IReadOnlyList<(Ball ball, Transform t, Renderer r,
                       Vector3 pocketWorldPos, SegmentData rollPath)> drops)
    {
        foreach (var (ball, t, r, pocketWorldPos, rollPath) in drops)
            StartOneDrop(ball, t, r, pocketWorldPos, rollPath);
    }

    /// <summary>新一局开始前调用，清除上一局已停球记录。</summary>
    public void ClearPocketedBalls() => _stoppedBalls.Clear();

    // ── 内部：配置动画并加入活跃列表 ─────────────────────────────────────────
    private void StartOneDrop(
        Ball ball, Transform ballTransform, Renderer ballRenderer,
        Vector3 pocketWorldPos, SegmentData rollPath)
    {
        // 从 Ball 读取落袋瞬间的位置（Ball 是唯一权威数据源）。
        // Ball.Position 是 FixVec2（定点数 2D）；Z 轴取渲染层当前值保持平面一致。
        Vector3 startPos = new Vector3(
            ball.Position.X.ToFloat(),
            ball.Position.Y.ToFloat(),
            ballTransform.position.z);

        PocketDropAniHelper helper = _pool.Rent();
        helper.StartDrop(new PocketDropRequest
        {
            startPos        = startPos,
            pocketPos       = pocketWorldPos,
            duration        = 0.25f,
            sinkDepth       = 0.18f,
            attractRatio    = 0.30f,
            sinkRatio       = 0.50f,
            vanishRatio     = 0.20f,
            attractStrength = 0.25f,
        });

        // 为这颗球创建临时 Scale 组合对象（不是 Ball 字段）
        var scaleState = new BallScaleState { Scale = 1f };

        _activeDrops.Add(new ActiveDrop
        {
            BallData      = ball,
            BallTransform = ballTransform,
            BallRenderer  = ballRenderer,
            BaseColor     = ballRenderer.material.color,
            ScaleState    = scaleState,
            Helper        = helper,
            RollPath      = rollPath,
        });
    }

    // ── Update：驱动落袋动画与落袋后滚动 ─────────────────────────────────────
    void Update()
    {
        // 1. 驱动落袋动画（倒序遍历，便于安全移除已完成条目）
        for (int i = _activeDrops.Count - 1; i >= 0; i--)
        {
            ActiveDrop      drop  = _activeDrops[i];
            PocketDropState state = drop.Helper.Update(Time.deltaTime);

            // 将 PocketDropState 回写渲染层（以 Ball 落袋瞬间状态为基准）
            drop.BallTransform.position      = state.position;
            drop.ScaleState.Scale            = state.scale;   // Scale 保存在组合对象中
            drop.BallTransform.localScale    = Vector3.one * state.scale;
            drop.BallRenderer.material.color = new Color(
                drop.BaseColor.r, drop.BaseColor.g, drop.BaseColor.b, state.alpha);

            if (state.phase == PocketDropPhase.Finished)
            {
                // 回收点：归还 helper 到池；ScaleState 不再被引用，自然 GC
                _pool.Return(drop.Helper);

                // 检查是否配置了有效的滚动路径
                var path = drop.RollPath;
                bool hasPath = path != null &&
                               (path.Start != path.End ||
                                (path.ConnectionPoints != null &&
                                 path.ConnectionPoints.Count > 0));
                if (hasPath)
                {
                    // 动画已完成，交棒给滚动阶段（恢复到正常缩放）
                    drop.BallTransform.localScale = Vector3.one;
                    StartRoll(drop.BallData, drop.BallTransform, path);
                }
                else
                {
                    // 无滚动路径：直接隐藏；也可在此归还球对象池
                    drop.BallTransform.gameObject.SetActive(false);
                }

                _activeDrops.RemoveAt(i);
            }
        }

        // 2. 驱动落袋后滚动
        for (int i = _activeRolls.Count - 1; i >= 0; i--)
        {
            ActiveRoll             roll  = _activeRolls[i];
            PocketPostRollState    state = roll.Helper.Update(Time.deltaTime);

            // 应用位置（Z 轴已由 PocketPostRollAniHelper 钳位至起点 Z，不会漂移）
            roll.BallTransform.position = state.position;

            // 应用物理角速度（无滑动滚动条件：ω = Cross(forward, v) / r）
            if (state.angularVelocity != Vector3.zero)
                roll.BallTransform.Rotate(
                    state.angularVelocity * Time.deltaTime * Mathf.Rad2Deg,
                    Space.World);

            if (state.phase == PocketPostRollPhase.Finished)
            {
                // 记录停止位置，供后续球的 PocketPostRollRequest.stoppedBalls 使用
                _stoppedBalls.Add(new StoppedBallInfo
                {
                    ballId   = i,
                    position = state.position,
                    radius   = roll.BallRadius,
                });

                // 隐藏；也可归还球对象池
                roll.BallTransform.gameObject.SetActive(false);
                _activeRolls.RemoveAt(i);
            }
        }
    }

    // ── 构建路径并加入活跃滚动列表 ────────────────────────────────────────────
    private void StartRoll(Ball ball, Transform ballTransform, SegmentData path)
    {
        int cpCount = path.ConnectionPoints?.Count ?? 0;
        // Waypoints: [Start, CP0, CP1, …, End]（Z 统一取当前渲染层 Z）
        float z = ballTransform.position.z;
        var waypoints = new Vector3[cpCount + 2];
        waypoints[0] = new Vector3(path.Start.x, path.Start.y, z);
        for (int k = 0; k < cpCount; k++)
            waypoints[k + 1] = new Vector3(
                path.ConnectionPoints[k].x, path.ConnectionPoints[k].y, z);
        waypoints[cpCount + 1] = new Vector3(path.End.x, path.End.y, z);

        // 将球放置在路径起点
        ballTransform.position = waypoints[0];

        float radius = ball.Radius.ToFloat();
        var rollHelper = new PocketPostRollAniHelper();
        rollHelper.Start(new PocketPostRollRequest
        {
            pathPoints   = waypoints,
            duration     = RollDuration,
            ballRadius   = radius,
            isCueBall    = false,
            stoppedBalls = _stoppedBalls.ToArray(),
        });

        _activeRolls.Add(new ActiveRoll
        {
            BallTransform = ballTransform,
            Helper        = rollHelper,
            BallRadius    = radius,
        });
    }
}
```

**落袋后滚动路径配置（编辑器）**

在场景中选中挂有 `TableAndPocketAuthoring` 的 GameObject，Inspector 中每个 Pocket 条目下均有 `Post Pocket Roll Path`（`SegmentData`）字段：

| 字段 | 说明 |
|------|------|
| `Start` | 路径起点（球落袋后的出发点，通常设在落袋口附近） |
| `Connection Points` | 中间路径点（0..N 个，拖动折点调整轨迹弯曲） |
| `End` | 路径终点（球沿路径滚到此处或被阻停后隐藏） |

场景视图中会以**橙色**折线 + 箭头实时预览路径走向。

**PocketDropRequest 参数一览：**

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `startPos` | — | 球落袋瞬间的世界坐标（由 `Ball.Position` 转换而来） |
| `pocketPos` | — | 球袋中心的世界坐标 |
| `duration` | 0.25 s | 整段动画总时长（≤ 0 使用默认值） |
| `sinkDepth` | 0.18 | Sink 阶段沿 `-Z` 下沉的距离（物理单位，< 0 使用默认值） |
| `attractRatio` | 0.30 | Attract 阶段占总时长的比例（无效时使用默认值并归一化） |
| `sinkRatio` | 0.50 | Sink 阶段占总时长的比例（无效时使用默认值并归一化） |
| `vanishRatio` | 0.20 | Vanish 阶段占总时长的比例（无效时使用默认值并归一化） |
| `attractStrength` | 0.25 | Attract 阶段横向移动量占 startPos→pocketPos 距离的比例（≤ 0 使用默认值；典型范围 0..1，大于 1 表示球超过袋口位置） |

**PocketDropState 字段一览：**

| 字段 | 说明 |
|------|------|
| `position` | 当前帧建议的球世界坐标 |
| `scale` | 均匀缩放值（Vanish 阶段 1 → 0）；应用至 `BallScaleState.Scale` 及 `Transform.localScale` |
| `alpha` | 不透明度（Vanish 阶段 1 → 0） |
| `phase` | 当前阶段（`Attract` / `Sink` / `Vanish` / `Finished`） |
| `normalizedTime` | 动画整体进度 0..1 |

**公共 API：**

| 方法 | 说明 |
|------|------|
| `StartDrop(in PocketDropRequest)` | 启动（或重置后重新启动）动画 |
| `Update(float deltaTime)` | 按帧推进动画，返回当前 `PocketDropState`（无堆分配） |
| `Evaluate(float normalizedTime)` | 在任意归一化时间采样动画状态，不修改内部状态 |
| `Stop()` | 立即停止动画（不标记为 Finished） |
| `Reset()` | 重置为初始空闲状态，适用于归还对象池前调用 |
| `IsRunning` | 动画是否正在播放 |
| `IsFinished` | 动画是否已完成 |

### 8. 落袋后滚动动画（PocketPostRollAniHelper）

`PocketPostRollAniHelper` 是 `PocketDropAniHelper` 的后续阶段，负责驱动已落袋球沿预设路径（`PostPocketRollPath`）滚动直至停止。它是一个纯逻辑类，不持有任何 `MonoBehaviour`、`Transform`、`Renderer` 或 `Material` 引用，单个实例可复用（对象池友好）。

> **PR #74 变更说明（2026-03）**：本类在此 PR 中引入了两项物理正确性修复：
> 1. **Z 轴漂移修复**：`_startZ` 固定为 `pathPoints[0].z`，所有后续帧的 `state.position.z` 均被钳位到此值。
> 2. **物理角速度输出**：`PocketPostRollState` 新增 `angularVelocity` 字段（单位：rad/s）。

#### Z 轴漂移修复

**原因**：台面是 XY 平面（Z 轴朝上/朝外）；当路径折线各点的 Z 值略有差异时，线性插值会导致球的 Z 坐标在滚动过程中持续偏移，使球浮离或穿入台面。

**修复方式**：`Start()` 调用时从 `pathPoints[0].z` 捕获 `_startZ`；`PositionAtArcLength()` 在每次返回前强制 `pos.z = _startZ`，`_finalPosition.z` 也同样钳位。

**影响与注意事项**：
- 调用方传入的 `pathPoints` 各点 Z 值可以不一致（例如从 `SegmentData` 转换时直接取渲染层 Z），helper 会自动忽略并钳位到起点 Z。
- 如需不同高度的滚动路径（非平面台面），应在传入前自行修正各点的 Z 值，或不依赖此字段。

**验证 Z 轴不再漂移（单元测试风格）：**

```csharp
var helper = new PocketPostRollAniHelper();
helper.Start(new PocketPostRollRequest
{
    pathPoints = new[]
    {
        new Vector3(0f, 0f, 2f),   // 起点 Z = 2
        new Vector3(5f, 0f, 2.1f), // 中间点 Z 故意不同
        new Vector3(10f, 0f, 1.9f),
    },
    duration   = 1f,
    ballRadius = 0.286f,
});

// 在任意采样点，Z 均应等于起点 Z（2.0）
for (float t = 0f; t <= 1f; t += 0.1f)
{
    PocketPostRollState s = helper.Evaluate(t);
    Debug.Assert(Mathf.Approximately(s.position.z, 2f),
        $"Z drift detected at t={t}: z={s.position.z}");
}
```

#### 物理角速度（angularVelocity）

**含义**：球在无滑动滚动（no-slip）条件下的角速度向量，单位 **rad/s**。

**坐标系与方向约定**：
- 台面在 **XY 平面**，法线方向为 `Vector3.forward`（`+Z`）。
- 公式：`ω = Cross(Vector3.forward, linearVelocity) / ballRadius`
- 向 `+X` 方向滚动 → `ω = (0, +ω, 0)`（绕 Y 轴正向）
- 向 `+Y` 方向滚动 → `ω = (-ω, 0, 0)`（绕 X 轴负向）
- 停止或动画结束时为 `Vector3.zero`。
- 当 `ballRadius <= 0` 时为 `Vector3.zero`。

**`PocketPostRollState` 字段一览：**

| 字段 | 类型 | 说明 |
|------|------|------|
| `position` | `Vector3` | 当前帧建议的球世界坐标（Z 钳位至起点 Z） |
| `angularVelocity` | `Vector3` | 物理角速度（rad/s），无滑动滚动条件；停止时为 `Vector3.zero` |
| `phase` | `PocketPostRollPhase` | 当前阶段（`None` / `Rolling` / `Finished`） |
| `normalizedTime` | `float` | 动画整体进度 0..1 |

**如何读取并应用 angularVelocity：**

```csharp
// 每帧驱动滚动动画
if (rollHelper.IsRunning)
{
    PocketPostRollState state = rollHelper.Update(Time.deltaTime);

    // 1. 更新位置（Z 不会漂移）
    ballTransform.position = state.position;

    // 2. 应用物理角速度驱动球的自旋（欧拉积分，适合视觉表现）
    if (state.angularVelocity != Vector3.zero)
    {
        ballTransform.Rotate(
            state.angularVelocity * Time.deltaTime * Mathf.Rad2Deg,
            Space.World);
    }

    // 3. 若接入刚体物理引擎，也可直接赋值
    // rigidbody.angularVelocity = state.angularVelocity;
}
```

**`PocketPostRollRequest` 参数一览：**

| 参数 | 说明 |
|------|------|
| `pathPoints` | 路径折线点数组（至少 2 个）：`[start, CP0, …, end]` |
| `duration` | 全程动画总时长（秒）；`<= 0` 使用默认值 1.0 s |
| `ballRadius` | 球半径（世界单位），用于碰撞检测与 `angularVelocity` 计算 |
| `isCueBall` | 是否为母球；为 `true` 时停止后触发 `OnCueBallRetrieved` 回调 |
| `stoppedBalls` | 已停在路径上的球信息数组（`StoppedBallInfo[]`），可为 `null` |

**公共 API：**

| 方法 / 属性 | 说明 |
|------------|------|
| `Start(in PocketPostRollRequest)` | 启动（或复用实例重新启动）滚动动画 |
| `Update(float deltaTime)` | 按帧推进动画，返回当前 `PocketPostRollState`（无堆分配） |
| `Evaluate(float normalizedTime)` | 在任意归一化时间采样状态，不修改内部状态 |
| `Stop()` | 立即停止（不标记为 Finished） |
| `Reset()` | 重置为空闲状态（归还对象池前调用） |
| `IsRunning` | 动画是否正在播放 |
| `IsFinished` | 动画是否已完成 |
| `OnStop` | `Action<Vector3>`，球停止时触发，参数为停止位置 |
| `OnCueBallRetrieved` | `Action`，母球停止后触发（`isCueBall == true` 时） |

## 物理参数说明

### Ball 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `Radius` | 0.5 | 球的半径（物理单位） |
| `Mass` | 1.0 | 球的质量 |
| `Restitution` | 0.95 | 碰撞恢复系数（弹性） |
| `SlidingFriction` | 0.2 | 滑动摩擦系数 |
| `RollingFriction` | 0.01 | 滚动摩擦系数 |
| `SpinFriction` | 0.05 | 旋转衰减系数 |

### MotionSimulator

| 常量 | 值 | 说明 |
|------|----|------|
| `Gravity` | 9 units/s² | 用于缩放摩擦减速的重力加速度（近似值，可按台面比例调整） |

### PhysicsWorld2D

| 常量 | 值 | 说明 |
|------|----|------|
| `MaxSubSteps` | 20 | 每帧最多碰撞子步数 |
| `CaptureRadiusFactor` | 0.5 | 球袋捕获半径系数 |
| `CollisionEpsilon` | 1e-5 s | 碰撞后时间安全偏移量 |

### ImpulseResolver（球-球位置修正参数）

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `PenetrationSlop` | 0.1 物理单位 | 触发位置修正的最小穿透深度（≈ 球半径的 0.35%）。小于此值的重叠由定点数舍入误差引起，忽略以避免抖动。 |
| `CorrectionPercent` | 0.8 | 每次碰撞求解调用时修正的超量穿透比例（80%）。剩余 20% 由速度分离自然消除，避免位置突变。 |

> **说明**：`PenetrationSlop` 与 `CorrectionPercent` 是 `static` 字段，可在运行时按需调整。  
> 若出现抖动，可适当减小 `CorrectionPercent`（如 0.5）；若重叠消除过慢，可适当增大。

## 台面配置

本库提供两套台面与球袋配置工作流，可按需选择：

### 方式 A：ScriptableObject（传统）

通过菜单 **Assets → Create → BilliardPhysics → TableDefinition** 创建台面 ScriptableObject，在 Inspector 中添加台边线段（起点 / 终点，以及可选的中间折点 ConnectionPoints）。

球袋同理通过 **Assets → Create → BilliardPhysics → PocketDefinition** 创建。

编辑器工具 `TableEditorTool` 和 `PocketEditorTool` 提供了场景视图内的可视化编辑功能。

### 方式 B：MonoBehaviour + 二进制导出（推荐）

1. 在场景中的任意 GameObject 上挂载 **TableAndPocketAuthoring** 组件（菜单路径：Component → BilliardPhysics → Table And Pocket Authoring）。
2. 在 Inspector 中配置台边（`Table.Segments`，每段包含 Start、ConnectionPoints、End）和球袋（`Pockets`，包含中心、半径、反弹速度阈值，以及口沿折线段 `RimSegments`）。场景视图工具 `TableEditorTool` 和 `PocketEditorTool` 同样适用于此组件。
3. 在 Inspector 底部点击 **Export Fixed Binary** 按钮，将台面与球袋数据序列化为 `.bytes` 二进制资产（调用 `ExportFixedBinaryHelper` 完成文件名校验与写入）。
4. 运行时通过 `TableAndPocketBinaryLoader.Load(textAsset)` 一行代码加载，零额外 GC，支持热更新。

如需将已有的 `.bytes` 资产重新导入回编辑器，点击 **Import Fixed Binary** 按钮，`ImportFixedBinaryHelper` 会将二进制数据还原为 `TableConfig` 和 `PocketConfig`，填充回组件的 Inspector 字段。

> **口沿折线段（RimSegments）**：每个球袋可配置若干 `Segment`，描述球袋口的弧形边缘。折线通过 `ConnectionPoints` 逐段定义，`RimSegmentHelper` 提供了 `TryPromoteLastCPToEnd` / `TryPromoteFirstCPToStart` 两个辅助方法，用于在编辑器中安全地删减端点而不导致线段退化。

## 技术要点

- **定点数运算**：`Fix64`（32.32 格式）保证仿真在不同平台、不同帧率下产生完全一致的结果，适用于联机对战等需要确定性的场景。
- **CCD（连续碰撞检测）**：通过二次方程求解扫掠圆与圆、扫掠圆与线段的碰撞时刻（TOI），防止高速球穿透。
- **冲量解算**：同时考虑线速度和角速度，支持库仑摩擦约束，模拟真实台球碰撞效果。
- **折线段（Polyline Segment）**：`Segment` 支持在 Start 与 End 之间插入任意数量的 `ConnectionPoints`，从而用一个对象描述台边或球袋口的曲折形状，CCD 和冲量解算会对每个子段分别计算。
- **二进制资产工作流**：台面与球袋数据可通过编辑器序列化为紧凑的定点数二进制格式（`.bytes`），运行时由 `TableAndPocketBinaryLoader` 直接解析，避免浮点转换误差，同时兼容版本 1（平坦子段）与版本 2（含 ConnectionPoints）两种格式。

## 环境要求

- Unity 2020.3 或更高版本
- C# 7.3+

