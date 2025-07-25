﻿(*********************************************************************************************)
(* FIO - A Type-Safe, Purely Functional Effect System for Asynchronous and Concurrent F#     *)
(* Copyright (c) 2022-2025 - Daniel "iyyel" Larsen and Technical University of Denmark (DTU) *)
(* All rights reserved                                                                       *)
(*********************************************************************************************)

module internal FSharp.FIO.Runtime.Tools.DeadlockDetector

open FSharp.FIO.DSL

open System.Threading
open System.Collections.Concurrent

[<AbstractClass>]
type internal Worker () =
    abstract Working: unit -> bool

// TODO: This implementation is currently wrong. It currently takes data from the channels, which is not correct.
// Rather, it should take a copy of the current contents of the channels, without removing anything.
// Perhaps the channel could hold a buffer when compilation directives are present.
type internal DeadlockDetector<'B, 'E when 'B :> Worker and 'E :> Worker>(activeWorkItemChan: InternalChannel<WorkItem>, intervalMs: int) as this =
    let blockingItems = ConcurrentDictionary<BlockingItem, unit>()
    let mutable blockingWorkers: List<'B> = []
    let mutable evalWorkers: List<'E> = []
    let mutable countDown = 10
    
    let startMonitor () =
        task {
            while true do
                (*
                * If there's no work left in the work queue and no eval workers are working,
                * BUT there are still blocking items, then we know we have a deadlock.
                *)
                if activeWorkItemChan.Count <= 0 && this.AllEvalWorkersIdle() && blockingItems.Count > 0 then
                    if countDown <= 0 then
                        printfn "DEADLOCK_DETECTOR: ############ WARNING: Potential deadlock detected! ############"
                        printfn "DEADLOCK_DETECTOR:     Suspicion: No work items left, All EvalWorkers idling, Existing blocking items"
                    else
                        countDown <- countDown - 1
                else
                    countDown <- 10

                Thread.Sleep intervalMs
        } |> ignore

    do startMonitor ()

    member internal _.AddBlockingItem blockingItem =
        blockingItems.TryAdd (blockingItem, ())

    member internal _.RemoveBlockingItem blockingItem =
        blockingItems.TryRemove blockingItem |> ignore

    member private _.AllEvalWorkersIdle () =
        not (
            List.contains true
            <| List.map (fun (evalWorker: 'E) -> evalWorker.Working ()) evalWorkers
        )

    member private _.AllBlockingWorkersIdle () =
        not (
            List.contains true
            <| List.map (fun (evalWorker: 'B) -> evalWorker.Working ()) blockingWorkers
        )

    member internal _.SetEvalWorkers workers =
        evalWorkers <- workers

    member internal _.SetBlockingWorkers workers =
        blockingWorkers <- workers
