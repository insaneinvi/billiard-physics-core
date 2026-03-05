using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class DirFineAdjustment :
    MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler
{
    [Header("References")]
    public RectTransform rollNode;
    public RectTransform dragArea; // DirFineAdjustment
    
    [Header("Ruler Settings")]
    public float width = 580f;
    public float valuePerPixel = 1f;

    // ===== 对外事件 =====
    public event Action<float> OnDeltaValue;     // 每次移动的deltaX

    private float totalValue;

    private Vector2 lastLocalPoint;
    private bool dragging;

    #region Pointer

    public void OnPointerDown(PointerEventData eventData)
    {
        dragging = true;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dragArea,
            eventData.position,
            eventData.pressEventCamera,
            out lastLocalPoint
        );
    }

    public void OnDrag(PointerEventData eventData)
    {
        Vector2 localPoint;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dragArea,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint
        );

        float deltaX = localPoint.x - lastLocalPoint.x;
        lastLocalPoint = localPoint;

        Move(deltaX);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        dragging = false;
    }

    #endregion

    void Move(float deltaX)
    {
        Vector2 pos = rollNode.anchoredPosition;
        pos.x += deltaX;

        // 无限循环
        if (pos.x > 0)
            pos.x -= width;
        else if (pos.x < -width)
            pos.x += width;

        rollNode.anchoredPosition = pos;

        // 数值计算
        float deltaValue = deltaX * valuePerPixel;
        totalValue += deltaValue;

        // ===== 触发事件 =====
        OnDeltaValue?.Invoke(deltaValue);
    }

    public void ResetValue(float value = 0)
    {
        totalValue = value;
        rollNode.anchoredPosition = Vector2.zero;
    }
}