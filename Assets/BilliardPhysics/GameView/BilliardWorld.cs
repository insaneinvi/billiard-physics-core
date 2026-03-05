using System;
using System.Collections;
using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.Runtime.BallInfo;
using BilliardPhysics.Runtime.ViewTool;
using UnityEngine;
using UnityEngine.EventSystems;
using Random = System.Random;

public class BilliardWorld : MonoBehaviour
{
    
    public GameObject tempBall;
    public GameObject tempShadow;
    
    public DirFineAdjustment dirFineAdjustment;
    public CueBallSpinSelector cueBallSpinSelector;
    public CueController cueController;
    public AimController aimController;
    public BallDropController ballDropController;
    
    private Texture[] ballTextures;
    // Start is called before the first frame update
    private Dictionary<int, GameObject> ballDict;
    private Dictionary<int, GameObject> ballShadowDict;
    private Dictionary<int, Quaternion> _ballRotations;

    private Ball cueBall;
    private PhysicsWorld2D _physicsWorld;
    private Vector3 playerTouchPoint;
    private Vector3 aimPoint;
    private bool isAllBallMotionless = false;
    private Fix64 hitStrength = 0;
    private Fix64 spinX = 0;
    private Fix64 spinY = 0;
    
    private bool isDragging = false;
    private readonly List<int> _stepPocketBalls = new List<int>();
    void Start()
    {
        var stepInterval = 1 / 60f;
        Time.fixedDeltaTime = stepInterval;
        var originRack = BallRackHelper.GenerateRack();
        var rackResult = BallRackHelper.ConvertRackToRotated(originRack);
        
        InitPhysicsWorldAndBall(rackResult);
        aimController.BindPhysicsWorld(_physicsWorld);
        
        InitViewWorld(rackResult);
        InitAimData();
        InitActionController();
        UpdateCueState(true);

        // DebugGraphy();
    }
    private void DebugGraphy()
    {
        var debug = new PhysicsWorld2DDebug();
        debug.SetTableGeometry(_physicsWorld.TableSegments, _physicsWorld.Pockets);
        debug.SetBalls(_physicsWorld.Balls);
        debug.SetDebug(true);
        pDebug = debug; 
    }

    private void ClearDebugGraphy()
    {
        pDebug.Dispose();
    }

    private PhysicsWorld2DDebug pDebug;
    private void OnDestroy()
    {
        // ClearDebugGraphy();
        _physicsWorld.OnBallPocketed -= OnBallPocketedHandler;
        dirFineAdjustment.OnDeltaValue -= OnDirFineAdjustment;
        cueController.onPullDeltaChanged -= cuePullDeltaChanged;
        cueController.onReturnDeltaChanged -= cueReturnDeltaChange;
        cueBallSpinSelector.onSpinChanged -= cueBallSpinHandler;
    }

    private void InitActionController()
    {
        dirFineAdjustment.OnDeltaValue += OnDirFineAdjustment;
        cueController.onPullDeltaChanged += cuePullDeltaChanged;
        cueController.onReturnDeltaChanged += cueReturnDeltaChange;
        cueBallSpinSelector.onSpinChanged += cueBallSpinHandler;
    }

    private void cueBallSpinHandler(Vector2 delta)
    {
        spinX = -BilliardsPhysicsDefaults.SpinParam * Fix64.FromFloat(delta.x);
        spinY = BilliardsPhysicsDefaults.SpinParam * Fix64.FromFloat(delta.y);
    }
    private void cuePullDeltaChanged(float delta)
    {
        aimController.UpdateCueHitPosition(delta);
        if (delta != 0)
        {
            hitStrength = BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMax * Fix64.FromFloat(delta);
            if (hitStrength < BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMin)
            {
                hitStrength = BilliardsPhysicsDefaults.ApplyCueStrike_StrengthMin;
            }
        }
        else
        {
            hitStrength = Fix64.Zero;
        }
        
    }

    private void cueReturnDeltaChange(float delta, bool isEnd)
    {
        aimController.UpdateCueHitPosition(delta);
        if (isEnd)
        {
            OnShoot();
            UpdateCueState(false);
        }
    }

    private void ResetHitInfo()
    {
        cueBallSpinSelector.ResetSpin();
        hitStrength = 0;
        spinX = 0;
        spinY = 0;
    }

    private void UpdateCueState(bool state)
    {
        if (state)
        {
            ResetHitInfo();
        }
        cueController.SetCanPull(state);
        aimController.SetPlayerAimState(state);
    }
    
    private void OnDirFineAdjustment(float deltaValue)
    {
        aimController.AdjustCueDir(deltaValue);
    }
    
    private void InitViewWorld(RackResult rackResult)
    {
        ballDict = new();
        ballShadowDict = new();
        _ballRotations = new();
        
        ballTextures = Resources.LoadAll<Texture>("Temps/BallsTextures");
        
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
        cueBallShadow.transform.position = new Vector3(rackResult.CueBall.Position.x + 0.1f,  rackResult.CueBall.Position.y+0.05f, -0.1f );
        
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
            ballShadow.transform.position = new Vector3(ob.Position.x + 0.1f,  ob.Position.y+0.05f, -0.1f );
            ballShadowDict.Add(ob.Number, ballShadow);
            _ballRotations.Add(ob.Number, Quaternion.identity);
        }
    }
    private void InitPhysicsWorldAndBall(RackResult  rackResult)
    {
        var physicsData = Resources.Load<TextAsset>("Data/tb8v_m");
        var (tableSegments, pockets, _) = TableAndPocketBinaryLoader.Load(physicsData);
        _physicsWorld = new();
        _physicsWorld.OnBallPocketed += OnBallPocketedHandler;
        foreach (var tableSegment in tableSegments)
        {
            tableSegment.Restitution = BilliardsPhysicsDefaults.Segment_Restitution;
        }
        _physicsWorld.SetTableSegments(tableSegments);
        foreach (var pocket in pockets)
        {
            pocket.RimSegment.Restitution = BilliardsPhysicsDefaults.PocketRimRestitution;
            _physicsWorld.AddPocket(pocket);
        }

        cueBall = new Ball(0, BilliardsPhysicsDefaults.Ball_Radius, BilliardsPhysicsDefaults.Ball_Mass);
        cueBall.Position = new FixVec2(Fix64.FromFloat(rackResult.CueBall.Position.x),  Fix64.FromFloat(rackResult.CueBall.Position.y));
        _physicsWorld.AddBall(cueBall);
        
        foreach (var ob in rackResult.ObjectBalls)
        {
            var objBall = new Ball(ob.Number);
            objBall.Position = new FixVec2(Fix64.FromFloat(ob.Position.x),  Fix64.FromFloat(ob.Position.y));
            _physicsWorld.AddBall(objBall);
        }
    }

    private void InitAimData()
    {
        var cueBall = _physicsWorld.Balls[0];
        var cueBallPos = new Vector3(cueBall.Position.X.ToFloat(), cueBall.Position.Y.ToFloat(), 0);
        aimPoint = new Vector3(0, 1, cueBallPos.z);
        aimController.UpdateAimPoint(aimPoint);
    }

    private bool RayTouchPoint()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        {
            if (hit.collider.CompareTag("Table"))
            {
                aimPoint =  hit.point;
                return true;
            }
        }

        return false;
    }
    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            isDragging = RayTouchPoint();
            aimController.UpdateAimPoint(aimPoint);
        }
        else
        {
            if (isDragging && Input.GetMouseButton(0))
            {
                RayTouchPoint();
                aimController.UpdateAimPoint(aimPoint);
            }

            if (isDragging && Input.GetMouseButtonUp(0))
            {
                isDragging = false;
            }
        }         
    }

    private void FixedUpdate()
    {
        _stepPocketBalls.Clear();
        _physicsWorld.Step();
        if (_stepPocketBalls.Count > 0)
        {
            
        }
        var cbml =  IsAllBallMotionless();
        if (!isAllBallMotionless && cbml)
        {
            UpdateAllBallsPositionInfo();
            UpdateCueState(true);
        }
        isAllBallMotionless = cbml;
        if(!isAllBallMotionless)
            UpdateAllBallsPositionInfo();
    }

    private void UpdateAllBallsPositionInfo()
    {
        foreach (var ball in _physicsWorld.Balls)
        {
            if(ball.IsPocketed)continue;
            var ballObj = ballDict[ball.Id];
            var ballShadowObj = ballShadowDict[ball.Id];

            // Pass raw physics ω to IntegrateRotation; it applies the correct
            // axial-vector transform (physics Z-up → view Z-negated) internally.
            var omega = ball.AngularVelocity;
            var physicsOmega = new Vector3(omega.X.ToFloat(), omega.Y.ToFloat(), omega.Z.ToFloat());
            _ballRotations[ball.Id] = PhysicsToView.IntegrateRotation(
                _ballRotations[ball.Id], physicsOmega, Time.fixedDeltaTime);

            ballObj.transform.position = new Vector3(ball.Position.X.ToFloat(), ball.Position.Y.ToFloat(), -BallRackHelper.HalfBallDiameter);
            ballObj.transform.rotation = _ballRotations[ball.Id];
            ballShadowObj.transform.position = new Vector3( ballObj.transform.position.x + 0.1f,   ballObj.transform.position.y+0.05f, -0.1f );
        }
    }

    private bool IsAllBallMotionless()
    {
        foreach (var physicsWorldBall in _physicsWorld.Balls)
        {
            if (!physicsWorldBall.IsPocketed && !physicsWorldBall.IsMotionless) return false;
        }
        return true;
    }

    public void OnShoot()
    {
        if (!isAllBallMotionless) return;
        FixVec2 direction = new FixVec2(Fix64.FromFloat(aimController.cueDir.x), Fix64.FromFloat(aimController.cueDir.y)).Normalized;
        _physicsWorld.ApplyCueStrike(cueBall, direction, hitStrength, spinX, spinY);
    }

    private void OnBallPocketedHandler(int ballId)
    {
        _stepPocketBalls.Add(ballId);
    }
}
