using System;
using System.Collections;
using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.Runtime.BallInfo;
using UnityEngine;

public class BilliardTest : MonoBehaviour
{
    
    public GameObject tempBall;
    public GameObject tempShadow;

    private Texture[] ballTextures;
    // Start is called before the first frame update
    private Dictionary<int, GameObject> ballDict;
    private Dictionary<int, GameObject> ballShadowDict;
    
    private PhysicsWorld2D _physicsWorld;
    void Start()
    {
        var rackResult = BallRackHelper.GenerateRack();
        InitPhysicsWorld(rackResult);
        InitViewWorld(rackResult);
        var stepInterval = 1 / 60f;
        Time.fixedDeltaTime = stepInterval;
    }

    private void InitViewWorld(RackResult rackResult)
    {
        ballDict = new();
        ballShadowDict = new();
        
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
        
        foreach (var ob in rackResult.ObjectBalls)
        {
            var objBall = Instantiate(tempBall);
            objBall.transform.position = ob.Position;
            objBall.GetComponent<Renderer>().material.SetTexture("_MainTex", ballTextures[ob.Number]);
            ballDict.Add(ob.Number, objBall);
            
            var ballShadow = Instantiate(tempShadow);
            ballShadow.transform.position = new Vector3(ob.Position.x + 10,  ob.Position.y+5, -0.1f );
            ballShadowDict.Add(ob.Number, ballShadow);
        }
    }
    private void InitPhysicsWorld(RackResult  rackResult)
    {
        var physicsData = Resources.Load<TextAsset>("Data/tb8h");
        var (tableSegments, pockets) = TableAndPocketBinaryLoader.Load(physicsData);
        _physicsWorld = new();
        _physicsWorld.SetTableSegments(tableSegments);
        foreach (var pocket in pockets)
        {
            _physicsWorld.AddPocket(pocket);
        }

        var cueBall = new Ball(0);
        cueBall.Position = new FixVec2(Fix64.FromFloat(rackResult.CueBall.Position.x),  Fix64.FromFloat(rackResult.CueBall.Position.y));
        _physicsWorld.AddBall(cueBall);
        
        foreach (var ob in rackResult.ObjectBalls)
        {
            var objBall = new Ball(ob.Number);
            objBall.Position = new FixVec2(Fix64.FromFloat(ob.Position.x),  Fix64.FromFloat(ob.Position.y));
            _physicsWorld.AddBall(objBall);
        }
    }

}
