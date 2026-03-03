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
│   │   └── PocketDropAniHelper.cs        # 球落袋动画纯逻辑辅助（三段式：吸引→下沉→消失）
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

**使用示例：**

```csharp
using BilliardPhysics.AniHelp;
using UnityEngine;

public class BallDropController : MonoBehaviour
{
    // 在 Inspector 或代码中赋值
    public Transform    ballTransform;
    public Renderer     ballRenderer;

    private PocketDropAniHelper _dropHelper = new PocketDropAniHelper();
    private Color               _baseColor;

    void Awake()
    {
        _baseColor = ballRenderer.material.color;
    }

    // 当物理层检测到球落袋时调用
    public void OnBallPocketed(Vector3 ballWorldPos, Vector3 pocketWorldPos)
    {
        var req = new PocketDropRequest
        {
            startPos        = ballWorldPos,
            pocketPos       = pocketWorldPos,
            duration        = 0.25f,
            sinkDepth       = 0.18f,
            attractRatio    = 0.30f,
            sinkRatio       = 0.50f,
            vanishRatio     = 0.20f,
            attractStrength = 0.25f,
        };
        _dropHelper.StartDrop(in req);
    }

    void Update()
    {
        if (_dropHelper.IsRunning)
        {
            PocketDropState state = _dropHelper.Update(Time.deltaTime);

            // 将状态应用到球的 Transform 和材质（渲染层自行负责）
            ballTransform.position   = state.position;
            ballTransform.localScale = Vector3.one * state.scale;
            ballRenderer.material.color = new Color(
                _baseColor.r, _baseColor.g, _baseColor.b, state.alpha);

            if (state.phase == PocketDropPhase.Finished)
                gameObject.SetActive(false);  // 或归还对象池，由调用方决定
        }
    }
}
```

**PocketDropRequest 参数一览：**

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `startPos` | — | 球落袋瞬间的世界坐标 |
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
| `scale` | 均匀缩放值（Vanish 阶段 1 → 0） |
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

