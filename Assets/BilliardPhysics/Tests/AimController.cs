using System;
using System.Collections;
using System.Collections.Generic;
using BilliardPhysics;
using BilliardPhysics.AimAssist;
using UnityEngine;

public class AimController : MonoBehaviour
{
    public GameObject cue;
    private AimAssistRenderer aimAssist;
    private PhysicsWorld2D _world;
    private bool isPlayerAiming = false;
    private Vector3 cueBallPos;
    
    [HideInInspector]
    public Vector3 cueDir;
    private void Start()
    {
        aimAssist = GetComponent<AimAssistRenderer>();
    }

    void Update()
    {
        if (isPlayerAiming)
        {
            aimAssist.DrawAimAssist(cueBallPos,cueDir);
        }
        else
        {
            aimAssist.Clear();
        }
    }

    public void BindPhysicsWorld(PhysicsWorld2D world)
    {
        _world = world; 
        aimAssist.SetPhysicsWorld(_world);
    }
    public void SetPlayerAimState(bool state)
    {
        isPlayerAiming = state;
        if (isPlayerAiming)
        {
            var cueBall = _world.Balls[0];
            cueBallPos = new(cueBall.Position.X.ToFloat(), cueBall.Position.Y.ToFloat(), -3);
            cue.transform.position = cueBallPos;
        }
        cue.SetActive(state);
    }

    public void UpdateAimPoint(Vector3 point)
    {
        cueDir = point - cueBallPos;
        float angle = Mathf.Atan2(cueDir.y, cueDir.x)*Mathf.Rad2Deg;
        angle += 180;
        cue.transform.rotation = Quaternion.Euler(0, 0, angle);
    }
}
