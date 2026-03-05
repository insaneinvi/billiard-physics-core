using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class CueController : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler
{
    [Header("References")]
    [SerializeField] private RectTransform cue;

    [Header("Config")]
    [SerializeField] private float maxPullDistance = 300f;
    [SerializeField] private float returnSpeed = 2500f;

    private Vector2 _initialPos;
    private bool _isDragging;
    private bool _canPull = true;

    private float _currentDeltaY;

    private Vector2 _pressLocalPoint;
    private float _pressStartDelta;

    public event Action<float> onPullDeltaChanged;      // 下拉回调
    public event Action<float,bool> onReturnDeltaChanged;    // 回弹回调

    #region Unity

    private void Awake()
    {
        if (cue == null)
        {
            Debug.LogError("CueController: cue 未绑定");
            enabled = false;
            return;
        }

        _initialPos = cue.anchoredPosition;
    }

    private void Update()
    {
        // 回弹逻辑
        if (!_isDragging && !Mathf.Approximately(_currentDeltaY, 0))
        {
            _currentDeltaY = Mathf.MoveTowards(
                _currentDeltaY,
                0,
                returnSpeed * Time.deltaTime);

            UpdateCuePosition();

            // 回弹回调
            onReturnDeltaChanged?.Invoke(-_currentDeltaY/maxPullDistance, false);

            // 回弹完成
            if (Mathf.Approximately(_currentDeltaY, 0))
            {
                onReturnDeltaChanged?.Invoke(0f, true);
            }
        }
    }

    #endregion

    #region Pointer

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!_canPull) return;

        _isDragging = true;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cue.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out _pressLocalPoint);

        _pressStartDelta = _currentDeltaY;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_canPull) return;

        Vector2 currentLocalPoint;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cue.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out currentLocalPoint);

        float deltaY = currentLocalPoint.y - _pressLocalPoint.y;

        float newDelta = _pressStartDelta + deltaY;

        // 不允许上拉
        newDelta = Mathf.Min(newDelta, 0f);

        // 最大下拉限制
        newDelta = Mathf.Max(newDelta, -maxPullDistance);

        _currentDeltaY =newDelta;

        UpdateCuePosition();
        onPullDeltaChanged?.Invoke(-_currentDeltaY/maxPullDistance);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isDragging = false;
    }

    #endregion

    #region Core

    private void UpdateCuePosition()
    {
        cue.anchoredPosition = _initialPos + new Vector2(0, _currentDeltaY);
    }

    #endregion

    #region Public API

    /// <summary>
    /// 设置是否允许下拉
    /// </summary>
    public void SetCanPull(bool canPull)
    {
        _canPull = canPull;

        if (!canPull)
        {
            _isDragging = false;
            _currentDeltaY = 0;
            UpdateCuePosition();
        }
    }
    #endregion
}