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

