namespace BilliardPhysics.Runtime.BallInfo
{
using System;
using System.Collections.Generic;
using UnityEngine;

public enum BallType
{
    Solid,
    Stripe,
    Eight,
    Cue
}

public struct BallData
{
    public int Number;
    public BallType Type;
    public Vector3 Position;

    public BallData(int number, BallType type, Vector3 position)
    {
        Number = number;
        Type = type;
        Position = position;
    }
}

public class RackResult
{
    public BallData CueBall;
    public List<BallData> ObjectBalls;
}

public static class BallRackHelper
{
    // ===== 桌参数（mm）=====
    public const float TableLength = 2540f;
    public const float TableWidth  = 1270f;

    const float BallDiameter = 57.15f;
    const float Gap = 0.05f; // 避免初始重叠
    const float Spacing = BallDiameter + Gap;

    const float ApexX = TableLength * 0.75f;
    const float CenterY = TableWidth * 0.5f;

    public const float HalfBallDiameter = BallDiameter / 2;
    // 预计算三角阵型位置（只算一次）
    static readonly Vector3[] RackPositions = GenerateRackPositions();

    // =========================================================
    // 主入口：生成球堆
    // =========================================================
    public static RackResult GenerateRack()
    {
        var result = new RackResult
        {
            ObjectBalls = new List<BallData>(15)
        };

        // ===== 母球 =====
        result.CueBall = new BallData(
            0,
            BallType.Cue,
            new Vector3(TableLength * 0.25f, CenterY,-HalfBallDiameter)
        );

        // ===== 号码池 =====
        List<int> solids  = new List<int> {1,2,3,4,5,6,7};
        List<int> stripes = new List<int> {9,10,11,12,13,14,15};

        Shuffle(solids);
        Shuffle(stripes);

        int?[] slots = new int?[15];

        // 8号固定在第三排中间（索引4）
        slots[4] = 8;

        // 底角规则（索引10 & 14）
        bool leftCornerSolid = UnityEngine.Random.value > 0.5f;

        if (leftCornerSolid)
        {
            slots[10] = Pop(solids);
            slots[14] = Pop(stripes);
        }
        else
        {
            slots[10] = Pop(stripes);
            slots[14] = Pop(solids);
        }

        // 剩余填充
        List<int> remain = new List<int>();
        remain.AddRange(solids);
        remain.AddRange(stripes);
        Shuffle(remain);

        int r = 0;
        for (int i = 0; i < 15; i++)
        {
            if (!slots[i].HasValue)
                slots[i] = remain[r++];
        }

        // 组装数据
        for (int i = 0; i < 15; i++)
        {
            int number = slots[i].Value;
            result.ObjectBalls.Add(
                new BallData(number, GetBallType(number), RackPositions[i])
            );
        }

        return result;
    }

    // =========================================================
    // 坐标转换：左旋90° + 右移1270mm
    // =========================================================
    // 公式：
    // x' = 1270 - y
    // y' = x
    // =========================================================
    public static Vector3 RotateLeft90AndShift(Vector3 pos)
    {
        return new Vector3(
            TableWidth - pos.y,
            pos.x,
            -HalfBallDiameter
        );
    }

    // 批量转换整个球堆
    public static RackResult ConvertRackToRotated(RackResult original)
    {
        var result = new RackResult
        {
            ObjectBalls = new List<BallData>(15)
        };

        // 转母球
        result.CueBall = new BallData(
            original.CueBall.Number,
            original.CueBall.Type,
            RotateLeft90AndShift(original.CueBall.Position)
        );

        // 转目标球
        foreach (var ball in original.ObjectBalls)
        {
            result.ObjectBalls.Add(
                new BallData(
                    ball.Number,
                    ball.Type,
                    RotateLeft90AndShift(ball.Position)
                )
            );
        }

        return result;
    }

    // =========================================================
    // 预计算阵型
    // =========================================================
    static Vector3[] GenerateRackPositions()
    {
        Vector3[] pos = new Vector3[15];

        float rowOffsetX = Spacing * Mathf.Sin(Mathf.Deg2Rad * 60f);
        float rowOffsetY = Spacing * 0.5f;

        int index = 0;

        for (int row = 0; row < 5; row++)
        {
            int count = row + 1;

            float x = ApexX + row * rowOffsetX;
            float startY = CenterY - row * rowOffsetY;

            for (int col = 0; col < count; col++)
            {
                pos[index++] = new Vector3(
                    x,
                    startY + col * Spacing,
                    -HalfBallDiameter
                );
            }
        }

        return pos;
    }

    // =========================================================
    // 工具函数
    // =========================================================
    static BallType GetBallType(int number)
    {
        if (number == 8) return BallType.Eight;
        if (number <= 7) return BallType.Solid;
        return BallType.Stripe;
    }

    static int Pop(List<int> list)
    {
        int v = list[0];
        list.RemoveAt(0);
        return v;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }
}
}