using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class CueBallSpinSelector : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public GameObject spinSelectObj;
    public RectTransform spinSmallCircle;
    [Header("配置")]
    public RectTransform circle;        // 子节点circle
    public float cueBallRadius = 175;  // cueball半径（UI单位，像素）

    public event Action<Vector2> onSpinChanged;

    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        if (circle == null)
        {
            Debug.LogError("请在Inspector中绑定circle节点");
        }
    }

    // 点击事件
    public void OnPointerDown(PointerEventData eventData)
    {
        UpdateCirclePosition(eventData);
        eventData.Use();
    }

    // 拖动事件
    public void OnDrag(PointerEventData eventData)
    {
        UpdateCirclePosition(eventData);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        eventData.Use();
    }
    

    private void UpdateCirclePosition(PointerEventData eventData)
    {
        Vector2 localPoint;
        // 将屏幕坐标转成RectTransform的本地坐标
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            // 限制在圆内
            if (localPoint.magnitude > cueBallRadius)
            {
                localPoint = localPoint.normalized * cueBallRadius;
            }

            circle.localPosition = localPoint;
            spinSmallCircle.localPosition = localPoint;
            
            Debug.Log(localPoint);
            Debug.Log("==========================================="+cueBallRadius);
            // 发送归一化事件
            Vector2 normalized = localPoint / cueBallRadius; // x, y范围 -1 ~ 1
            Debug.Log(normalized);
            onSpinChanged?.Invoke(normalized);
        }
    }

    public void OpenSpinSelect()
    {
        circle.localPosition = Vector3.zero;
        spinSelectObj.SetActive(true);
    }

    public void CloseSpinSelect()
    {
        spinSelectObj.SetActive(false);
    }

    public void ResetSpin()
    {
        circle.localPosition = Vector3.zero;
        spinSmallCircle.localPosition = Vector3.zero;
    }
}