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
│   │   ├── Fix64.cs              # 32.32 有符号定点数
│   │   └── FixVec2.cs            # 基于定点数的 2D 向量
│   ├── Physics/
│   │   ├── Ball.cs               # 球的状态与物理参数
│   │   ├── Segment.cs            # 台边/库边线段
│   │   ├── Pocket.cs             # 球袋
│   │   ├── PhysicsWorld2D.cs     # 主仿真入口
│   │   ├── CCDSystem.cs          # 连续碰撞检测
│   │   ├── ImpulseResolver.cs    # 碰撞冲量解算
│   │   ├── MotionSimulator.cs    # 摩擦力与运动积分
│   │   └── CueStrike.cs          # 球杆击打
│   └── Table/
│       ├── TableDefinition.cs    # 台面配置（ScriptableObject）
│       └── PocketDefinition.cs   # 球袋配置（ScriptableObject）
└── Editor/
    ├── BilliardPhysics.Editor.asmdef
    ├── TableEditorTool.cs        # 台面编辑器工具
    └── PocketEditorTool.cs       # 球袋编辑器工具
```

## 快速上手

### 1. 创建仿真世界

```csharp
using BilliardPhysics;

// 创建物理世界
var world = new PhysicsWorld2D();

// 从 TableDefinition ScriptableObject 加载台边
TableDefinition tableDef = ...; // 通过 Inspector 赋值或 Resources.Load
world.SetTableSegments(tableDef.BuildSegments());

// 添加球袋
world.AddPocket(new Pocket(center, radius, reboundVelocityThreshold));
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

在 Unity 编辑器中，通过菜单 **Assets → Create → BilliardPhysics → TableDefinition** 创建台面 ScriptableObject，在 Inspector 中添加台边线段（起点/终点坐标）。

球袋同理通过 **PocketDefinition** ScriptableObject 配置。

编辑器工具 `TableEditorTool` 和 `PocketEditorTool` 提供了场景视图内的可视化编辑功能。

## 技术要点

- **定点数运算**：`Fix64`（32.32 格式）保证仿真在不同平台、不同帧率下产生完全一致的结果，适用于联机对战等需要确定性的场景。
- **CCD（连续碰撞检测）**：通过二次方程求解扫掠圆与圆、扫掠圆与线段的碰撞时刻（TOI），防止高速球穿透。
- **冲量解算**：同时考虑线速度和角速度，支持库仑摩擦约束，模拟真实台球碰撞效果。

## 环境要求

- Unity 2020.3 或更高版本
- C# 7.3+

