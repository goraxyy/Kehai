using System;
using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;
using UnityEngine.AI;

// Uses the original shopper, route, till and exit. No extra NPC or omniscient customer spawn.
[DisallowMultipleComponent]
public sealed class CustomerComplaint : MonoBehaviour
{
    static readonly List<CustomerComplaint> all = new List<CustomerComplaint>();
    public static IReadOnlyList<CustomerComplaint> All => all;
    public static int LostThisShift { get; private set; }
    public static event Action<CustomerComplaint, bool> Resolved; // bool: heard out
    public bool Active { get; private set; }
    public bool HeardOut { get; private set; }
    public bool Waiting => Active && patience.AtTill;
    public const float HoldSeconds = 20f;
    readonly ComplaintPatience patience = new ComplaintPatience();
    CustomerNPC npc;
    SpeechBubble bubble;
    float walkingSpeed;
    public Vector3 FixPosition => npc != null && npc.cashierPoint != null ? npc.cashierPoint.position : transform.position;
    public static bool Eligible(CustomerNPC customer)
    {
        if (customer == null || customer.cashierPoint == null || customer.exitPoint == null ||
            customer.CurrentActivity == CustomerNPC.Activity.Leaving || customer.GetComponent<CustomerComplaint>() != null) return false;
        var memory = customer.GetComponent<CustomerMemory>();
        var request = customer.GetComponent<CustomerRequest>();
        if (memory == null || !memory.HasSpokenToKaren || memory.possessed ||
            (request != null && request.CurrentStage != CustomerRequest.Stage.None)) return false;
        var agent = customer.agent;
        if (agent == null || !agent.isOnNavMesh) return false;
        var path = new NavMeshPath();
        return agent.CalculatePath(customer.cashierPoint.position, path) && path.status == NavMeshPathStatus.PathComplete;
    }
    public static CustomerComplaint Begin(CustomerNPC customer, ShiftManager shift)
    {
        if (shift == null || !shift.IsShiftActive || shift.DoorsClosed || !Eligible(customer)) return null;
        var complaint = customer.gameObject.AddComponent<CustomerComplaint>();
        complaint.npc = customer;
        complaint.Active = true;
        customer.TakeComplaintRoute();
        complaint.walkingSpeed = customer.agent.speed;
        customer.agent.speed *= 1.3f; // visibly angry walk; the bubble travels with the shopper
        customer.agent.isStopped = !PowerSystem.PowerOn;
        customer.agent.SetDestination(customer.cashierPoint.position);
        if (Edition.ShowUI)
        {
            complaint.bubble = SpeechBubble.Create(customer.transform, 2.2f);
            complaint.bubble.Show("I want to make a complaint. At the till. Now.");
        }
        TaskManager.NotifyWorldChanged();
        return complaint;
    }
    void OnEnable() => all.Add(this);
    void OnDisable()
    {
        // Despawn/scene unload clears the job without fabricating a lost customer.
        Active = false;
        all.Remove(this);
        if (npc != null) npc.SetForcedHighlight(false);
        if (bubble != null) CctvWall.Drop(bubble.gameObject);
        TaskManager.NotifyWorldChanged();
    }
    void Update() => Step(Time.deltaTime);
    public void Step(float dt)
    {
        if (!Active || npc == null) return;
        bool paused = GamePause.Paused || !PowerSystem.PowerOn;
        var agent = npc.agent;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = paused || Waiting;
            if (!Waiting && !agent.pathPending && FairnessGuard.Flat(transform.position, FixPosition) <= agent.stoppingDistance + .6f)
            {
                patience.Arrive();
                npc.WaitForComplaint();
                npc.SetForcedHighlight(true);
                if (bubble != null) bubble.Show("Hear me out. This will take twenty seconds.");
                TaskManager.NotifyWorldChanged();
            }
        }
        if (patience.Step(dt, paused)) Finish(false);
    }
    public bool CanHold => Waiting && PowerSystem.PowerOn;
    public string Prompt => Waiting ? BackstreetFix.Prompt("hear the complaint") : "The shopper is going to complain at the till";
    public void HearOut() { if (CanHold) Finish(true); }
    void Finish(bool heard)
    {
        if (!Active) return;
        Active = false;
        HeardOut = heard;
        if (!heard) LostThisShift++;
        if (npc.agent != null) npc.agent.speed = walkingSpeed;
        npc.SetForcedHighlight(false);
        if (bubble != null) { CctvWall.Drop(bubble.gameObject); bubble = null; }
        TaskManager.NotifyWorldChanged();
        if (heard) GameEvents.RaiseCustomerServed(npc);
        Resolved?.Invoke(this, heard);
        npc.LeaveStore();
    }
    public static void ResetForShift()
    {
        foreach (var complaint in all.ToArray()) if (complaint != null && complaint.Active) complaint.Cancel();
        LostThisShift = 0;
        foreach (var memory in CustomerMemory.All) if (memory != null) memory.ClearConversation();
    }
    void Cancel()
    {
        Active = false;
        if (npc != null)
        {
            if (npc.agent != null) npc.agent.speed = walkingSpeed;
            npc.SetForcedHighlight(false);
            npc.LeaveStore();
        }
        if (bubble != null) { CctvWall.Drop(bubble.gameObject); bubble = null; }
        TaskManager.NotifyWorldChanged();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); LostThisShift = 0; Resolved = null; }
}
