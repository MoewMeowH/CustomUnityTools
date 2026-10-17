using System;
using UnityEngine;

/// <summary>
/// 인스펙터 슬롯에서 필수적으로 할당되어야 하는 필드에 부여합니다.
/// 미할당(null 또는 Missing) 시 HB_TestFrameworkHelper 검증기 및 Test Runner에서 에러를 리포트합니다.
/// 배열 · List 슬롯의 빈 원소는 경고로만 리포트하며, warnNullElements로 끌 수 있습니다.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class NeedSlotNullCheckAttribute : PropertyAttribute
{
    public string Description { get; }

    /// <summary>배열 · List 슬롯의 null 원소를 경고로 리포트할지 (기본 true). 단일 슬롯에는 영향이 없습니다.</summary>
    public bool WarnNullElements { get; }

    public NeedSlotNullCheckAttribute(string description = null, bool warnNullElements = true)
    {
        Description = description;
        WarnNullElements = warnNullElements;
    }
}
