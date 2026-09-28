using System;
using System.Collections.Generic;
using UnityEngine;

public class InteractiveZone : MonoBehaviour
{
    [Flags]
    public enum ZoneType
    {
        None = 0,
        Enter = 1 << 0,
        Exit = 1 << 1,
        Stay = 1 << 2,
    }

    [Serializable]
    public class LogicAction
    {
        public LogicBase target;
        public bool active;
        public bool isForce;
    }

    [Header("Zone Condition")]
    [SerializeField]
    private ZoneType zoneType;

    [Header("Actions")]
    [SerializeField]
    private List<LogicAction> actions = new List<LogicAction>();

    private readonly HashSet<Collider> insideColliders = new HashSet<Collider>();


    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other))
            return;

        bool wasEmpty = insideColliders.Count == 0;

        insideColliders.Add(other);

        if (!wasEmpty)
            return;

        if (HasType(ZoneType.Enter))
        {
            ExecuteActions();
        }
    }


    private void OnTriggerExit(Collider other)
    {
        if (!insideColliders.Remove(other))
            return;

        // 플레이어의 다른 Collider가 아직 영역 안에 있음
        if (insideColliders.Count > 0)
            return;

        if (HasType(ZoneType.Exit))
        {
            ExecuteActions();
        }

        // Stay는 영역을 벗어나면 원래 상태로 복귀
        if (HasType(ZoneType.Stay))
        {
            ExecuteActions(true);
        }
    }


    private void FixedUpdate()
    {
        if (!HasType(ZoneType.Stay))
            return;

        if (insideColliders.Count == 0)
            return;

        ExecuteActions();
    }


    private void ExecuteActions(bool reverse = false)
    {
        foreach (var action in actions)
        {
            if (action.target == null)
                continue;

            bool value = reverse ? !action.active : action.active;

            if (action.isForce)
                action.target.SetForceActive(value);
            else
                action.target.SetActive(value);
        }
    }


    private bool HasType(ZoneType type)
    {
        return (zoneType & type) != 0;
    }


    private bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<PlayerController>() != null;
    }
}