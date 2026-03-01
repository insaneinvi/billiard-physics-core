using System;
using System.Collections;
using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.Runtime.BallInfo;
using BilliardPhysics.Runtime.ViewTool;
using UnityEngine;

public class BilliardTest : MonoBehaviour
{
    
    public GameObject tempBall;
    public GameObject tempShadow;

    private Texture[] ballTextures;
    // Start is called before the first frame update
    private Dictionary<int, GameObject> ballDict;
    private Dictionary<int, GameObject> ballShadowDict;
    private Dictionary<int, Quaternion> _ballRotations;

    private Ball cueBall;
    private PhysicsWorld2D _physicsWorld;

    private bool isAllBallMotionless = false;
    void Start()
    {
        var rackResult = BallRackHelper.GenerateRack();
        InitPhysicsWorldAndBall(rackResult);
        InitViewWorld(rackResult);
        var stepInterval = 1 / 60f;
        Time.fixedDeltaTime = stepInterval;
    }

    private void InitViewWorld(RackResult rackResult)
    {
        ballDict = new();
        ballShadowDict = new();
        _ballRotations = new();
        
        ballTextures = Resources.LoadAll<Texture>("Temp/BallsTextures");
        
        Array.Sort(ballTextures, (a, b) =>
        {
            var an = a.name.Replace("ballTexture", "");
            var ai = int.Parse(an);
            var bn = b.name.Replace("ballTexture", "");
            var bi = int.Parse(bn);
            return ai < bi ? -1 : 1;
        });
        
        
        var cueBall = Instantiate(tempBall);
        cueBall.transform.position = rackResult.CueBall.Position;
        cueBall.GetComponent<Renderer>().material.SetTexture("_MainTex", ballTextures[rackResult.CueBall.Number]);
        
        var cueBallShadow = Instantiate(tempShadow);
        cueBallShadow.transform.position = new Vector3(rackResult.CueBall.Position.x + 15,  rackResult.CueBall.Position.y+5, -0.1f );
        
        ballShadowDict.Add(rackResult.CueBall.Number, cueBallShadow);
        ballDict.Add(rackResult.CueBall.Number, cueBall);
        _ballRotations.Add(rackResult.CueBall.Number, Quaternion.identity);
        
        foreach (var ob in rackResult.ObjectBalls)
        {
            var objBall = Instantiate(tempBall);
            objBall.transform.position = ob.Position;
            objBall.GetComponent<Renderer>().material.SetTexture("_MainTex", ballTextures[ob.Number]);
            ballDict.Add(ob.Number, objBall);
            
            var ballShadow = Instantiate(tempShadow);
            ballShadow.transform.position = new Vector3(ob.Position.x + 10,  ob.Position.y+5, -0.1f );
            ballShadowDict.Add(ob.Number, ballShadow);
            _ballRotations.Add(ob.Number, Quaternion.identity);
        }
    }
    private void InitPhysicsWorldAndBall(RackResult  rackResult)
    {
        var physicsData = Resources.Load<TextAsset>("Data/tb8h");
        var (tableSegments, pockets) = TableAndPocketBinaryLoader.Load(physicsData);
        _physicsWorld = new();
        _physicsWorld.SetTableSegments(tableSegments);
        foreach (var pocket in pockets)
        {
            _physicsWorld.AddPocket(pocket);
        }

        cueBall = new Ball(0);
        cueBall.Position = new FixVec2(Fix64.FromFloat(rackResult.CueBall.Position.x),  Fix64.FromFloat(rackResult.CueBall.Position.y));
        _physicsWorld.AddBall(cueBall);
        
        foreach (var ob in rackResult.ObjectBalls)
        {
            var objBall = new Ball(ob.Number);
            objBall.Position = new FixVec2(Fix64.FromFloat(ob.Position.x),  Fix64.FromFloat(ob.Position.y));
            _physicsWorld.AddBall(objBall);
        }
    }

    private void FixedUpdate()
    {
        _physicsWorld.Step();

        foreach (var ball in _physicsWorld.Balls)
        {
            if(ball.IsPocketed)continue;
            var ballObj = ballDict[ball.Id];
            var ballShadowObj = ballShadowDict[ball.Id];

            // Convert physics ω (Z-down) to Unity space (Z-up) and integrate rotation.
            var omega = ball.AngularVelocity;
            var omegaUnity = new Vector3(omega.X.ToFloat(), omega.Y.ToFloat(), -omega.Z.ToFloat());
            _ballRotations[ball.Id] = PhysicsToView.IntegrateRotation(
                _ballRotations[ball.Id], omegaUnity, Time.fixedDeltaTime);

            ballObj.transform.position = new Vector3(ball.Position.X.ToFloat(), ball.Position.Y.ToFloat(), -BallRackHelper.HalfBallDiameter);
            ballObj.transform.rotation = _ballRotations[ball.Id];
            ballShadowObj.transform.position = new Vector3( ballObj.transform.position.x + 15,   ballObj.transform.position.y+5, -0.1f );
        }

        isAllBallMotionless = IsAllBallMotionless();

    }

    private bool IsAllBallMotionless()
    {
        foreach (var physicsWorldBall in _physicsWorld.Balls)
        {
            if (!physicsWorldBall.IsMotionless) return false;
        }
        return true;
    }

    public void OnShoot()
    {
        if (!isAllBallMotionless) return;
        FixVec2 direction = new FixVec2(Fix64.One, Fix64.Zero).Normalized;
        Fix64 strength = Fix64.From(160000);
        _physicsWorld.ApplyCueStrike(cueBall, direction, strength, Fix64.Zero, Fix64.Zero);
    }
}
