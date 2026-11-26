using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class HandController : MonoBehaviour
{
    [SerializeField] private Image _handImage;

    [Range(0f, 2f)]
    public float _handScale = 1f;
    [Range(0f, 1f)]
    public float _handTransparency = 1f;

    [SerializeField] private float smoothFactor = 1f;
    [SerializeField] private bool fadeFunctionality;
    private Vector2 previousMousePosition;
    private Vector2 currentMousePosition;
    private bool mouseMoved;



    private bool isFadedOut = false;
    private float idleTimer = 0f;
    [SerializeField] private float idleTimeToFade = 1.0f; // Time in seconds to start fading
    [SerializeField] private float fadeDuration = 0.5f; // How long fade in/out takes


    public RectTransform cursorImage;
    public Canvas canvas;
    [SerializeField] private CanvasGroup canvasGroup;

    [SerializeField] private Animator _animator;
    [SerializeField] private bool _isAnimating = false;
    private void OnValidate()
    {
        if (cursorImage != null)
        {
            cursorImage.localScale = Vector2.one * _handScale;
            canvasGroup.alpha = _handTransparency;
        }
    }
    private void Start()
    {
        canvasGroup = cursorImage.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = cursorImage.gameObject.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = _handTransparency;
        previousMousePosition = Input.mousePosition;
    }

    private void Update()
    {

        cursorImage.localScale = Vector2.one * _handScale;

        currentMousePosition = Input.mousePosition;
        mouseMoved = (currentMousePosition != previousMousePosition);
        previousMousePosition = currentMousePosition;


        CursorMovement();
        CursorAnimation();
        CursorFade();
       
 
    }

    private void CursorMovement()
    {
        //Get mouse position
        /*Vector2 currentMousePosition = Input.mousePosition;
        bool mouseMoved = (currentMousePosition != previousMousePosition);
        previousMousePosition = currentMousePosition;*/

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            currentMousePosition,
            canvas.worldCamera,
            out Vector2 mousePosition
        );

        Vector2 currentPos = cursorImage.anchoredPosition;
        Vector2 targetPos = mousePosition;
       

        //this is where the image follows the mouse
        cursorImage.anchoredPosition = Vector2.Lerp(currentPos, targetPos, Time.deltaTime * smoothFactor);
    }
    private void CursorAnimation()
    {
        if (Input.GetMouseButtonDown(0) && !_isAnimating)
        {
            _animator.SetBool("isDown", true);
            _isAnimating = true;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _animator.SetBool("isDown", false);
            _isAnimating = false;
        }
    }
    private void CursorFade()
    {
        //Get mouse position
        if (_isAnimating) return;
       
        /*Vector2 currentMousePosition = Input.mousePosition;
        bool mouseMoved = (currentMousePosition != previousMousePosition);
        previousMousePosition = currentMousePosition;*/
        
        if (fadeFunctionality)
        {
            if (mouseMoved)
            {
                idleTimer = 0f;

                if (isFadedOut)
                {
                    StopAllCoroutines();
                    StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 1f, fadeDuration));
                    isFadedOut = false;
                }
            }
            else
            {
                idleTimer += Time.deltaTime;

                if (!isFadedOut && idleTimer >= idleTimeToFade)
                {
                    StopAllCoroutines();
                    StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 0f, fadeDuration));
                    isFadedOut = true;

                }
            }
        }
    }
    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float start, float end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cg.alpha = Mathf.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        
        cg.alpha = end;
       

    }

    public IEnumerator SetCursordIdle()
    {
        yield return new WaitForSeconds(1f);
        _isAnimating = false;
    }
}
