using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 숫자 변환 표기 타입
/// </summary>
public enum ENumberConvertType
{
    None = 0,
    Comma = 1,        // 1,000,000
    Sign = 2,         // 1M
    Sign_Comma = 3    // 1.5M
}

/// <summary>
/// 시간 문자열 변환 타입
/// </summary>
public enum ETimeTextType
{
    ProgressTime,
    RemainTime,
    HourMinSecTimer,
    HourMinTimer
}

/// <summary>
/// 한국어 조사 종류
/// </summary>
public enum EJosaType
{
    EunNeun,   // 은/는
    IGa,       // 이/가
    EulReul,   // 을/를
    GwaWa,     // 과/와
    EuroRo     // 으로/로
}

/// <summary>
/// 프로젝트 전반에서 사용하는 순수 범용 유틸리티 및 확장 함수 모음 클래스입니다.
/// </summary>
public static class CommonFunction
{
    #region Json

    public static string ObjectToJson(object obj, bool prettyPrint = false)
    {
        if (obj == null) return null;
        try
        {
            return JsonUtility.ToJson(obj, prettyPrint);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CommonFunction] ObjectToJson error: {e}");
            return null;
        }
    }

    public static T JsonToObject<T>(string jsonData)
    {
        if (string.IsNullOrEmpty(jsonData)) return default;
        try
        {
            return JsonUtility.FromJson<T>(jsonData);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CommonFunction] JsonToObject error: {e}");
            return default;
        }
    }

    #endregion

    #region Number & Unit Formatting

    public static readonly string[] NumberUnits_Stat = new string[] { string.Empty, "K", "M", "G", "T", "P", "E", "Z", "Y", "V", "R", "O", "Q", "X" };
    public static readonly string[] NumberUnits_Damage = new string[] { "K", "M", "G", "T", "P", "E", "Z", "Y", "V", "R", "O", "Q", "X" };
    public static readonly string[] NumberUnits_Asset = new string[] { "K", "M", "G", "T", "P", "E", "Z", "Y", "V", "R", "O", "Q", "X" };

    public static string ConvertNumberToString(long number, ENumberConvertType convertType)
    {
        return convertType switch
        {
            ENumberConvertType.Comma => $"{number:#,##0}",
            ENumberConvertType.Sign or ENumberConvertType.Sign_Comma => GetSubDigit(number.ToString(), 3, 1, NumberUnits_Asset),
            _ => number.ToString()
        };
    }

    public static string ConvertNumberToString(int number, ENumberConvertType convertType) => ConvertNumberToString((long)number, convertType);

    public static string ConvertNumberToString(float number, ENumberConvertType convertType) => ConvertNumberToString((double)number, convertType);

    public static string ConvertNumberToString(double number, ENumberConvertType convertType)
    {
        return convertType switch
        {
            ENumberConvertType.Comma => $"{number:#,##0}",
            ENumberConvertType.Sign or ENumberConvertType.Sign_Comma => GetSubDigit(number, 3, NumberUnits_Asset),
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static string GetSubDigit(double number, int digit, string[] units)
    {
        if (number < 0) return string.Empty;

        string strNum = number.ToString(CultureInfo.InvariantCulture);
        string search = strNum.ToLower();

        if (search.Contains("e"))
        {
            search = search.Replace(".", "");
            string[] partsExp = search.Split('e');
            if (partsExp.Length < 2 || !int.TryParse(partsExp[1], out int digitCount))
                return string.Empty;

            strNum = partsExp[0];
            int addCount = digitCount - partsExp[0].Length;
            for (int i = 0; i <= addCount; ++i) strNum += "0";
        }
        else
        {
            string[] parts = strNum.Split('.');
            strNum = parts[0];
        }

        return GetSubDigit(strNum, digit, units);
    }

    public static string GetSubDigit(string strNum, int digit, string[] units)
    {
        if (digit < 0 || string.IsNullOrEmpty(strNum) || units == null || units.Length == 0)
            return string.Empty;

        const int stepUnit = 3;
        int length = strNum.Length;
        digit = Mathf.Min(length, digit);

        int ret = (length - digit) / stepUnit;
        ret = Mathf.Clamp(ret, 0, units.Length - 1);

        string unit = units[ret];
        strNum = strNum.Substring(0, length - (stepUnit * ret));

        if (!long.TryParse(strNum, out long parsedNum))
            return string.Empty;

        return $"{parsedNum:#,##0}{unit}";
    }

    public static string GetSubDigit(string number, int digitCount, int outputCount, string[] units)
    {
        if (digitCount <= 0 || outputCount < 0 || string.IsNullOrEmpty(number) || number.Length <= digitCount)
            return number;

        if (units == null || units.Length == 0) return number;

        int unitIndex = number.Length / digitCount;
        int remainLength = number.Length % digitCount;
        if (remainLength == 0)
        {
            remainLength = digitCount;
            --unitIndex;
        }

        int totalRemainLength = remainLength + outputCount;
        if (totalRemainLength < 0 || totalRemainLength > number.Length - 1 || unitIndex <= 0 || unitIndex > units.Length)
            return number;

        if (string.IsNullOrEmpty(units[unitIndex - 1])) return number;

        string remainNumber = number.Substring(0, remainLength);
        var builder = new StringBuilder();
        builder.Append(remainNumber);

        if (outputCount > 0)
        {
            string outputNumber = number.Substring(remainLength, outputCount);
            if (!string.IsNullOrEmpty(outputNumber))
            {
                builder.Append(".");
                builder.Append(outputNumber);
            }
        }

        builder.Append(units[unitIndex - 1]);
        return builder.ToString();
    }

    #endregion

    #region GameObject & Component

    public static void SetActive(this Component comp, bool active)
    {
        if (comp == null || comp.gameObject == null) return;
        comp.gameObject.SetActive(active);
    }

    public static void SetActive(GameObject obj, bool active)
    {
        if (obj == null) return;
        obj.SetActive(active);
    }

    public static void SetActiveList(IReadOnlyList<GameObject> list, bool active)
    {
        if (list == null) return;
        for (int i = 0; i < list.Count; ++i)
        {
            if (list[i] != null) list[i].SetActive(active);
        }
    }

    public static void SetActiveList(GameObject[] list, bool active)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; ++i)
        {
            if (list[i] != null) list[i].SetActive(active);
        }
    }

    public static bool IsActive(Behaviour behaviour)
    {
        if (behaviour == null || behaviour.gameObject == null) return false;
        return behaviour.gameObject.activeInHierarchy;
    }

    public static bool IsActive(GameObject obj)
    {
        if (obj == null) return false;
        return obj.activeSelf;
    }

    public static bool IsEnabled(Behaviour behaviour)
    {
        return behaviour != null && behaviour.enabled;
    }

    public static T GetOrAddComponent<T>(this GameObject obj) where T : Component
    {
        if (obj == null) return null;
        if (!obj.TryGetComponent<T>(out var comp))
        {
            comp = obj.AddComponent<T>();
        }
        return comp;
    }

    public static T GetOrAddComponent<T>(this Component comp) where T : Component
    {
        if (comp == null) return null;
        return comp.gameObject.GetOrAddComponent<T>();
    }

    public static Transform FindChild(this Transform parent, string childName)
    {
        if (parent == null || string.IsNullOrEmpty(childName)) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == childName) return child;
            var sub = FindChild(child, childName);
            if (sub != null) return sub;
        }
        return null;
    }

    public static T FindChild<T>(this Transform parent, string childName) where T : Component
    {
        var target = FindChild(parent, childName);
        return target != null ? target.GetComponent<T>() : null;
    }

    public static void DestroyChildren(this Transform parent, bool immediate = false)
    {
        if (parent == null) return;
        int childCount = parent.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (immediate)
                UnityEngine.Object.DestroyImmediate(child);
            else
                UnityEngine.Object.Destroy(child);
        }
    }

    #endregion

    #region Transform & RectTransform

    public static void ResetLocal(this Transform t)
    {
        if (t == null) return;
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
    }

    public static void SetPositionX(this Transform t, float x) { if (t != null) { var p = t.position; p.x = x; t.position = p; } }
    public static void SetPositionY(this Transform t, float y) { if (t != null) { var p = t.position; p.y = y; t.position = p; } }
    public static void SetPositionZ(this Transform t, float z) { if (t != null) { var p = t.position; p.z = z; t.position = p; } }

    public static void SetLocalPositionX(this Transform t, float x) { if (t != null) { var p = t.localPosition; p.x = x; t.localPosition = p; } }
    public static void SetLocalPositionY(this Transform t, float y) { if (t != null) { var p = t.localPosition; p.y = y; t.localPosition = p; } }
    public static void SetLocalPositionZ(this Transform t, float z) { if (t != null) { var p = t.localPosition; p.z = z; t.localPosition = p; } }

    public static void SetLocalScaleX(this Transform t, float x) { if (t != null) { var s = t.localScale; s.x = x; t.localScale = s; } }
    public static void SetLocalScaleY(this Transform t, float y) { if (t != null) { var s = t.localScale; s.y = y; t.localScale = s; } }
    public static void SetLocalScaleZ(this Transform t, float z) { if (t != null) { var s = t.localScale; s.z = z; t.localScale = s; } }
    public static void SetLocalScale(this Transform t, float scale) { if (t != null) t.localScale = Vector3.one * scale; }

    public static void SetAnchoredPosition(RectTransform rectTransform, Vector2 position)
    {
        if (rectTransform != null) rectTransform.anchoredPosition = position;
    }

    private static readonly Vector2 s_UITargetFocusingPadding = new Vector2(40f, 80f);

    public static void UITargetFocusing(this RectTransform currentTrans, Transform targetTrans, RectTransform canvasRectTrans = null)
    {
        if (currentTrans == null || targetTrans == null) return;
        if (targetTrans is not RectTransform targetRectTrans) return;

        if (canvasRectTrans == null)
        {
            var canvas = currentTrans.GetComponentInParent<Canvas>();
            if (canvas != null) canvasRectTrans = canvas.GetComponent<RectTransform>();
        }
        if (canvasRectTrans == null) return;

        Vector2 screenP = RectTransformUtility.WorldToScreenPoint(null, targetRectTrans.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRectTrans, screenP, null, out Vector2 localPoint);

        Vector2 targetSizeDelta = targetRectTrans.sizeDelta;
        Vector2 targetPivot = targetRectTrans.pivot;
        Vector2 targetPivotAddPos = new Vector2(targetSizeDelta.x * (0.5f - targetPivot.x), targetSizeDelta.y * (0.5f - targetPivot.y));
        localPoint += targetPivotAddPos;

        Vector2 checkPivot = currentTrans.pivot;
        float rootWidth = canvasRectTrans.rect.width * 0.5f;
        float rootHeight = canvasRectTrans.rect.height * 0.5f;
        float minX = -rootWidth + (currentTrans.rect.width * checkPivot.x) + s_UITargetFocusingPadding.x;
        float maxX = rootWidth - (currentTrans.rect.width * (1f - checkPivot.x)) - s_UITargetFocusingPadding.x;
        float minY = -rootHeight + (currentTrans.rect.height * checkPivot.y) + s_UITargetFocusingPadding.y;
        float maxY = rootHeight - (currentTrans.rect.height * (1f - checkPivot.y)) - s_UITargetFocusingPadding.y;

        localPoint.x = Mathf.Clamp(localPoint.x, minX, maxX);
        localPoint.y = Mathf.Clamp(localPoint.y, minY, maxY);

        currentTrans.localPosition = localPoint;
    }

    #endregion

    #region UI - TextMeshPro

    public static void SetValue(TextMeshProUGUI tmp, string val)
    {
        if (tmp == null) return;
        tmp.text = val ?? string.Empty;
    }

    public static void SetValue(TextMeshProUGUI tmp, long number, ENumberConvertType convertType = ENumberConvertType.None)
    {
        SetValue(tmp, ConvertNumberToString(number, convertType));
    }

    public static void SetValue(TextMeshProUGUI tmp, int number, ENumberConvertType convertType = ENumberConvertType.None)
    {
        SetValue(tmp, ConvertNumberToString(number, convertType));
    }

    public static void SetValue(TextMeshProUGUI tmp, float number, ENumberConvertType convertType = ENumberConvertType.None)
    {
        SetValue(tmp, ConvertNumberToString(number, convertType));
    }

    public static void SetValue(TextMeshProUGUI tmp, double number, ENumberConvertType convertType = ENumberConvertType.None)
    {
        SetValue(tmp, ConvertNumberToString(number, convertType));
    }

    public static void SetText(this TextMeshProUGUI tmp, string text) => SetValue(tmp, text);

    public static void SetColor(TextMeshProUGUI text, Color color)
    {
        if (text != null) text.color = color;
    }

    public static void SetColor(TextMeshProUGUI text, string stringColor)
    {
        if (text != null && ColorUtility.TryParseHtmlString(stringColor, out Color color))
        {
            text.color = color;
        }
    }

    public static void SetAlpha(TextMeshProUGUI text, float alpha)
    {
        if (text == null) return;
        var c = text.color;
        c.a = Mathf.Clamp01(alpha);
        text.color = c;
    }

    public static void SetBold(this TextMeshProUGUI tmp, bool isBold)
    {
        if (tmp == null) return;
        if (isBold) tmp.fontStyle |= FontStyles.Bold;
        else tmp.fontStyle &= ~FontStyles.Bold;
    }

    #endregion

    #region UI - Image

    public static void SetValue(Image image, Sprite sprite)
    {
        if (image != null) image.sprite = sprite;
    }

    public static void SetSprite(this Image image, Sprite sprite) => SetValue(image, sprite);

    public static void SetFillValue(Image image, float fillAmount, bool checkFillAmount = false)
    {
        if (image == null) return;
        if (checkFillAmount && image.fillAmount >= fillAmount) return;
        image.fillAmount = Mathf.Clamp01(fillAmount);
    }

    public static void SetColor(Image image, Color color)
    {
        if (image != null) image.color = color;
    }

    public static void SetColor(Image image, string stringColor)
    {
        if (image != null && ColorUtility.TryParseHtmlString(stringColor, out Color color))
        {
            image.color = color;
        }
    }

    public static void SetAlpha(Image image, float alpha)
    {
        if (image == null) return;
        var c = image.color;
        c.a = Mathf.Clamp01(alpha);
        image.color = c;
    }

    #endregion

    #region UI - Slider

    public static void SetValue(Slider slider, float min, float max, float value)
    {
        if (slider == null) return;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
    }

    public static void SetValue(Slider slider, float value)
    {
        if (slider == null) return;
        slider.value = value;
    }

    public static float GetValue(Slider slider) => slider != null ? slider.value : 0f;
    public static float GetMaxValue(Slider slider) => slider != null ? slider.maxValue : 0f;

    #endregion

    #region UI - Toggle

    public static void SetValue(Toggle toggle, bool value)
    {
        if (toggle != null) toggle.isOn = value;
    }

    public static bool GetValue(Toggle toggle) => toggle != null && toggle.isOn;

    #endregion

    #region UI - Button & Selectable

    public static void SetInteractable(Selectable selectable, bool interactable)
    {
        if (selectable != null) selectable.interactable = interactable;
    }

    public static bool IsInteractable(Selectable selectable) => selectable != null && selectable.interactable;

    public static void SetOnClick(this Button button, UnityAction action)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        if (action != null) button.onClick.AddListener(action);
    }

    #endregion

    #region UI - InputField

    public static void SetValue(TMP_InputField inputField, string text)
    {
        if (inputField != null) inputField.text = text ?? string.Empty;
    }

    public static string GetValue(TMP_InputField inputField) => inputField != null ? inputField.text : string.Empty;

    public static void SetValue(InputField inputField, string text)
    {
        if (inputField != null) inputField.text = text ?? string.Empty;
    }

    public static string GetValue(InputField inputField) => inputField != null ? inputField.text : string.Empty;

    #endregion

    #region UI - Layout & HitTest

    public static void SetLayout(VerticalLayoutGroup layoutGroup)
    {
        if (layoutGroup != null) layoutGroup.SetLayoutVertical();
    }

    public static void SetLayout(HorizontalLayoutGroup layoutGroup)
    {
        if (layoutGroup != null) layoutGroup.SetLayoutHorizontal();
    }

    public static void ForceRebuildLayout(RectTransform rect)
    {
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    public static bool IsPointOverUI(RectTransform rectTransform, Vector2 screenPosition, Camera cam = null)
    {
        if (rectTransform == null) return false;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPosition, cam, out Vector2 localPoint))
        {
            return rectTransform.rect.Contains(localPoint);
        }
        return false;
    }

    public static bool IsPointOverUI(GameObject obj, Vector2 screenPosition, Camera cam = null)
    {
        if (obj == null) return false;
        return obj.transform is RectTransform rt && IsPointOverUI(rt, screenPosition, cam);
    }

    #endregion

    #region Animator & Animation

    public static void SetEnabled(Animator animator, bool enabled)
    {
        if (animator != null && animator.enabled != enabled) animator.enabled = enabled;
    }

    public static void SetTrigger(Animator animator, string triggerName)
    {
        if (animator != null && !string.IsNullOrEmpty(triggerName)) animator.SetTrigger(triggerName);
    }

    public static void SetBool(Animator animator, string parameterName, bool value)
    {
        if (animator != null && !string.IsNullOrEmpty(parameterName)) animator.SetBool(parameterName, value);
    }

    public static void SetFloat(Animator animator, string parameterName, float value)
    {
        if (animator != null && !string.IsNullOrEmpty(parameterName)) animator.SetFloat(parameterName, value);
    }

    public static void SetInteger(Animator animator, string parameterName, int value)
    {
        if (animator != null && !string.IsNullOrEmpty(parameterName)) animator.SetInteger(parameterName, value);
    }

    public static void Play(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.speed = 1f;
        animator.Play(stateName);
    }

    public static void Reverse(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return;
        animator.speed = -1f;
        animator.Play(stateName);
    }

    public static void SetSpeed(Animator animator, float speed)
    {
        if (animator != null) animator.speed = speed;
    }

    public static bool HasLayer(this Animator anim, int layer) => anim != null && anim.layerCount > layer;

    public static void SetLayerWeightSafety(this Animator anim, int layer, float weight)
    {
        if (anim != null && anim.HasLayer(layer))
        {
            anim.SetLayerWeight(layer, weight);
        }
    }

    public static void PlayAnimation(Animation animation, string animationName, WrapMode mode = WrapMode.Once)
    {
        if (animation == null || string.IsNullOrEmpty(animationName)) return;
        var clipState = animation[animationName];
        if (clipState == null) return;
        clipState.wrapMode = mode;
        animation.Play(animationName);
    }

    #endregion

    #region Time & Timer

    public static string ToD2(int value) => value.ToString("D2");

    public static TimeSpan GetRemainTime(DateTime endDate, DateTime curDate)
    {
        var span = endDate - curDate;
        return span < TimeSpan.Zero ? TimeSpan.Zero : span;
    }

    public static TimeSpan GetRemainTime(double totalSeconds)
    {
        return totalSeconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(totalSeconds);
    }

    public static string GetRemainTimeString(long totalSeconds)
    {
        if (totalSeconds <= 0) return "00:00:00";
        var span = TimeSpan.FromSeconds(totalSeconds);
        return (int)span.TotalHours > 0
            ? $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }

    public static string GetFormattedTime(double totalSeconds)
    {
        if (totalSeconds <= 0) return "00s";
        var span = TimeSpan.FromSeconds(totalSeconds);

        if (span.TotalDays >= 1)
            return $"{span.Days}d {ToD2(span.Hours)}h";
        if (span.TotalHours >= 1)
            return $"{span.Hours}h {ToD2(span.Minutes)}m";
        if (span.TotalMinutes >= 1)
            return $"{span.Minutes}m {ToD2(span.Seconds)}s";

        return $"{span.Seconds}s";
    }

    public static string GetDate_MonthString(DateTime dateTime) => dateTime.ToString("MM");
    public static string GetDate_DayString(DateTime dateTime) => dateTime.ToString("dd");
    public static string GetDate_HourString(DateTime dateTime) => dateTime.ToString("HH");
    public static string GetDate_MinString(DateTime dateTime) => dateTime.ToString("mm");
    public static string GetDate_SecondsString(DateTime dateTime) => dateTime.ToString("ss");
    public static string GetDate_HourMinutesSecondsString(DateTime dateTime) => dateTime.ToString("HH:mm:ss");
    public static string GetDate_HourMinutesString(DateTime dateTime) => dateTime.ToString("HH:mm");
    public static string GetDate_MinutesSecondsString(DateTime dateTime) => dateTime.ToString("mm:ss");

    public static long GetUnixTimestamp(DateTime? dateTime = null)
    {
        var dt = dateTime ?? DateTime.UtcNow;
        return new DateTimeOffset(dt).ToUnixTimeSeconds();
    }

    #endregion

    #region Math & Vector

    public static Vector3 RemoveAxisY(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    public static Vector3 RemoveAxisY2Normalized(Vector3 v)
    {
        v.y = 0f;
        return v.normalized;
    }

    public static float DistanceSqr(Vector3 a, Vector3 b)
    {
        Vector3 diff = a - b;
        return Vector3.Dot(diff, diff);
    }

    public static bool IsNearPos(Vector3 v1, Vector3 v2, float radius = 0f, bool ignoreY = true)
    {
        Vector3 diff = v1 - v2;
        if (ignoreY) diff.y = 0f;
        return Vector3.Dot(diff, diff) <= (radius * radius);
    }

    public static Vector3 GetDirectionMovePos(Vector3 myPos, Vector3 targetPos, float maxDist, out float dist)
    {
        Vector3 dir = (targetPos - myPos).normalized;
        dist = Mathf.Min(Vector3.Distance(myPos, targetPos), maxDist);
        return myPos + dir * dist;
    }

    public static Vector2 RotatePoint(Vector2 point, Vector2 pivot, float rotateRadian)
    {
        Vector2 translated = point - pivot;
        float rotatedX = translated.x * Mathf.Cos(rotateRadian) - translated.y * Mathf.Sin(rotateRadian);
        float rotatedY = translated.x * Mathf.Sin(rotateRadian) + translated.y * Mathf.Cos(rotateRadian);
        return new Vector2(rotatedX, rotatedY) + pivot;
    }

    public static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
    {
        if (Mathf.Approximately(inMin, inMax)) return outMin;
        float t = Mathf.InverseLerp(inMin, inMax, value);
        return Mathf.Lerp(outMin, outMax, t);
    }

    public static System.Numerics.Vector3 ToSystemVector3(this Vector3 v)
    {
        return new System.Numerics.Vector3(v.x, v.y, v.z);
    }

    #endregion

    #region String & RichText

    public static string SetRichTextColor(string text, string hexColor) => $"<color={hexColor}>{text}</color>";
    public static string SetRichTextBold(string text) => $"<b>{text}</b>";
    public static string SetRichTextSize(string text, int size) => $"<size={size}>{text}</size>";

    public static bool ContainsNonNumericCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return Regex.IsMatch(input, @"\D");
    }

    public static string GetJosa(string word, EJosaType josaType)
    {
        if (string.IsNullOrEmpty(word)) return string.Empty;
        char lastChar = word[word.Length - 1];

        // 한글 유니코드 음절 범위 검사 (가 ~ 힣)
        if (lastChar < 0xAC00 || lastChar > 0xD7A3)
        {
            return josaType switch
            {
                EJosaType.EunNeun => "은(는)",
                EJosaType.IGa => "이(가)",
                EJosaType.EulReul => "을(를)",
                EJosaType.GwaWa => "과(와)",
                EJosaType.EuroRo => "으로(로)",
                _ => string.Empty
            };
        }

        int jongseongIndex = (lastChar - 0xAC00) % 28;
        bool hasJongseong = jongseongIndex > 0;

        return josaType switch
        {
            EJosaType.EunNeun => hasJongseong ? "은" : "는",
            EJosaType.IGa => hasJongseong ? "이" : "가",
            EJosaType.EulReul => hasJongseong ? "을" : "를",
            EJosaType.GwaWa => hasJongseong ? "과" : "와",
            EJosaType.EuroRo => (jongseongIndex == 8 || !hasJongseong) ? "로" : "으로", // 'ㄹ' 받침 예외 처리
            _ => string.Empty
        };
    }

    #endregion

    #region Collections & Enum

    public static bool IsNullOrEmptyCollection<T>(this ICollection<T> collection)
    {
        return collection == null || collection.Count == 0;
    }

    public static bool IsNullOrEmptyCollection<T>(this T[] array)
    {
        return array == null || array.Length == 0;
    }

    public static T SafeGet<T>(this IReadOnlyList<T> list, int index, T defaultValue = default)
    {
        if (list == null || index < 0 || index >= list.Count) return defaultValue;
        return list[index];
    }

    public static T SafeGet<T>(this IList<T> list, int index, T defaultValue = default)
    {
        if (list == null || index < 0 || index >= list.Count) return defaultValue;
        return list[index];
    }

    public static T GetRandom<T>(this IList<T> list)
    {
        if (list == null || list.Count == 0) return default;
        return list[UnityEngine.Random.Range(0, list.Count)];
    }

    public static void Shuffle<T>(this IList<T> list)
    {
        if (list == null || list.Count <= 1) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int r = UnityEngine.Random.Range(0, i + 1);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    public static bool TryParseEnum<T>(string value, out T result) where T : struct, Enum
    {
        return Enum.TryParse(value, true, out result);
    }

    #endregion

    #region Diagnostics

    public static string GetMemoryLog(string title = "")
    {
        static string ToMB(long bytes) => (bytes / (1024f * 1024f)).ToString("F2", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine("-------------------- LOG - Memory --------------------");
        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine($"Title: {title}");
            sb.AppendLine("------------------------------------------------------");
        }
        sb.AppendLine($"Total Reserved by Unity: {ToMB(UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong())} MB");
        sb.AppendLine($"Total Allocated by Unity: {ToMB(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong())} MB");
        sb.AppendLine($"Allocated by GPU: {ToMB(UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver())} MB");
        sb.AppendLine($"Unused Reserved Memory: {ToMB(UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong())} MB");
        sb.AppendLine($"Used Heap Size: {ToMB(UnityEngine.Profiling.Profiler.usedHeapSizeLong)} MB");
        sb.AppendLine($"System Total Memory: {SystemInfo.systemMemorySize} MB");
        sb.AppendLine("------------------------------------------------------");
        return sb.ToString();
    }

    #endregion
}

