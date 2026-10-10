using System;
using UnityEngine;

/// <summary>
/// 인스펙터 슬롯에서 필수적으로 할당되어야 하는 필드에 부여합니다.
/// 미할당(null 또는 Missing) 시 HB_TestFrameworkHelper 검증기 및 Test Runner에서 에러를 리포트합니다.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public class NeedSlotNullCheckAttribute : PropertyAttribute
{
    public string Description { get; }

    public NeedSlotNullCheckAttribute(string description = null)
    {
        Description = description;
    }
}
